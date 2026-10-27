#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
project="mingle-tls-${GITHUB_RUN_ID:-$$}"
private_directory=$(mktemp -d)
chmod 700 "$private_directory"
cleanup() { status=$?; trap - EXIT; docker compose -f compose.yaml -f infra/compose.production.yaml -p "$project" down --volumes --remove-orphans || { if [ "$status" -eq 0 ]; then status=1; fi; }; rm -rf "$private_directory"; exit "$status"; }
export MINGLE_TLS_DIRECTORY="$private_directory/tls"
mkdir "$MINGLE_TLS_DIRECTORY"
chmod 755 "$MINGLE_TLS_DIRECTORY"
openssl req -x509 -newkey rsa:2048 -nodes -days 2 -keyout "$MINGLE_TLS_DIRECTORY/privkey.pem" -out "$MINGLE_TLS_DIRECTORY/fullchain.pem" -subj /CN=localhost -addext 'subjectAltName=DNS:localhost,IP:127.0.0.1' > /dev/null 2>&1
# The containing temporary parent is private; the non-root edge reads this mount.
chmod 644 "$MINGLE_TLS_DIRECTORY/"*.pem
export MINGLE_HTTPS_BIND=127.0.0.1 MINGLE_HTTPS_PORT="${TEAMGAMES_TLS_TEST_PORT:-18443}"
export MINGLE_PUBLIC_ORIGIN="https://localhost:$MINGLE_HTTPS_PORT"
network_octet=$(( $$ % 200 + 20 ))
export MINGLE_NETWORK_SUBNET="172.30.$network_octet.0/24" MINGLE_API_IP="172.30.$network_octet.2" MINGLE_WEB_IP="172.30.$network_octet.3"
export MINGLE_ADMIN_CREDENTIAL="$(python3 -c 'import secrets; print(secrets.token_hex(32))')"
export NODE_EXTRA_CA_CERTS="$MINGLE_TLS_DIRECTORY/fullchain.pem"
trap cleanup EXIT
docker compose -f compose.yaml -f infra/compose.production.yaml -p "$project" up --build --detach --wait --wait-timeout 180
node scripts/check-tls.mjs "$MINGLE_PUBLIC_ORIGIN" setup "$private_directory/session.json"
node scripts/check-tls-browser.mjs "$MINGLE_PUBLIC_ORIGIN" "$MINGLE_TLS_DIRECTORY/fullchain.pem"
python3 scripts/storage.py backup --project "$project" --directory "$private_directory/backup"
node scripts/check-tls.mjs "$MINGLE_PUBLIC_ORIGIN" mutate "$private_directory/session.json"
docker compose -f compose.yaml -f infra/compose.production.yaml -p "$project" kill --signal SIGKILL api
docker compose -f compose.yaml -f infra/compose.production.yaml -p "$project" up --detach --wait --no-recreate --wait-timeout 90 api
node scripts/check-tls.mjs "$MINGLE_PUBLIC_ORIGIN" crashed "$private_directory/session.json"
python3 scripts/storage.py restore --project "$project" --directory "$private_directory/backup" --confirm "$project"
node scripts/check-tls.mjs "$MINGLE_PUBLIC_ORIGIN" restored "$private_directory/session.json"
