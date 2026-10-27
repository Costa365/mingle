#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

# Own an isolated project and volume. Never run down -v against the user's app.
project="teamgames-verification-${GITHUB_RUN_ID:-$$}"
check_directory="$(mktemp -d)"
# A disposable credential for this isolated verification project only. Never log it.
export MINGLE_ADMIN_CREDENTIAL="$(python3 -c 'import secrets; print(secrets.token_hex(32))')"
export TEAMGAMES_PORT="${TEAMGAMES_TEST_PORT:-18080}"
compose() { docker compose -p "$project" -f compose.yaml "$@"; }
cleanup() {
  status=$?
  trap - EXIT
  set +e
  if [ "$status" -ne 0 ]; then
    mkdir -p artifacts/verification
    compose ps --all > artifacts/verification/compose-status.txt 2>&1
    compose logs --no-color --tail=200 > artifacts/verification/compose.log 2>&1
    cat artifacts/verification/compose.log
  fi
  compose down --volumes --remove-orphans
  cleanup_status=$?
  rm -rf "$check_directory"
  if [ "$status" -eq 0 ] && [ "$cleanup_status" -ne 0 ]; then status=$cleanup_status; fi
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
        assert db.execute('SELECT count(*) FROM __EFMigrationsHistory').fetchone()[0] == 5
        return db.execute('SELECT Id, InstanceId, CreatedUtc FROM Installations').fetchall()
before, after = installation('before.db'), installation('after.db')
assert len(before) == 1 and before == after, 'Persisted installation changed after recreation'
def keys(name):
    return {p.name: p.read_bytes() for p in (directory / name).glob('*.xml')}
assert keys('keys-before') and keys('keys-before') == keys('keys-after'), 'Key ring did not persist'
print('Database, migration history, WAL mode, and Data Protection keys survived API recreation.')
PY
compose up --detach --wait --no-recreate --wait-timeout 60 api
PLAYWRIGHT_BASE_URL="http://127.0.0.1:$TEAMGAMES_PORT" \
TEAMGAMES_TEST_COMPOSE_PROJECT="$project" npm --prefix web run test:e2e
compose logs --no-color web > "$check_directory/web.log"
python3 - "$check_directory/web.log" <<'PY'
import pathlib, re, sys
logs = pathlib.Path(sys.argv[1]).read_text()
assert 'mingle-log-canary' not in logs, 'Request credentials leaked to proxy logs'
assert not re.search(r'/hubs/rooms\?id=', logs), 'Hub transport tokens leaked to proxy logs'
print('Invitation, Referer, and SignalR transport queries are excluded from proxy logs.')
PY
