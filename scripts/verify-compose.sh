#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

# Own an isolated project and volume. Never run down -v against the user's app.
project="teamgames-verification-${GITHUB_RUN_ID:-$$}"
check_directory="$(mktemp -d)"
export TEAMGAMES_PORT="${TEAMGAMES_TEST_PORT:-18080}"
compose() { docker compose -p "$project" "$@"; }
cleanup() {
  status=$?
  if [ "$status" -ne 0 ]; then compose logs --tail=100; fi
  compose down --volumes --remove-orphans
  rm -rf "$check_directory"
  exit "$status"
}
trap cleanup EXIT

compose up --build --detach --wait --wait-timeout 180
python3 scripts/check-deployment.py "http://127.0.0.1:$TEAMGAMES_PORT"

# Copy only while stopped so WAL-backed SQLite files are consistent.
compose stop api
container="$(compose ps --all --quiet api)"
docker cp "$container:/data/teamgames.db" "$check_directory/before.db"
docker cp "$container:/data/keys" "$check_directory/keys-before"
compose up --detach --wait --force-recreate api
compose stop api
container="$(compose ps --all --quiet api)"
docker cp "$container:/data/teamgames.db" "$check_directory/after.db"
docker cp "$container:/data/keys" "$check_directory/keys-after"
python3 - "$check_directory" <<'PY'
import pathlib, sqlite3, sys
directory = pathlib.Path(sys.argv[1])
def installation(name):
    with sqlite3.connect(directory / name) as db:
        assert db.execute('PRAGMA journal_mode').fetchone()[0] == 'wal'
        assert db.execute('SELECT count(*) FROM __EFMigrationsHistory').fetchone()[0] == 1
        return db.execute('SELECT Id, InstanceId, CreatedUtc FROM Installations').fetchall()
before, after = installation('before.db'), installation('after.db')
assert len(before) == 1 and before == after, 'Persisted installation changed after recreation'
def keys(name):
    return {p.name: p.read_bytes() for p in (directory / name).glob('*.xml')}
assert keys('keys-before') and keys('keys-before') == keys('keys-after'), 'Key ring did not persist'
print('Database, migration history, WAL mode, and Data Protection keys survived API recreation.')
PY
compose start --wait api
PLAYWRIGHT_BASE_URL="http://127.0.0.1:$TEAMGAMES_PORT" \
TEAMGAMES_TEST_COMPOSE_PROJECT="$project" npm --prefix web run test:e2e
