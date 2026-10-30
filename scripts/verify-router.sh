#!/usr/bin/env bash
# Isolated TLS router -> HTTP frontend -> real API/SQLite rehearsal.
set -euo pipefail
cd "$(dirname "$0")/.."
project="mingle-router-${GITHUB_RUN_ID:-$$}"
private_directory=$(mktemp -d)
chmod 700 "$private_directory"
export MINGLE_ROUTER_TEST_DIRECTORY="$private_directory"
export MINGLE_ROUTER_TEST_NETWORK="$project-edge"
export MINGLE_ROUTER_TEST_IMAGE="$project-web"
export MINGLE_ROUTER_TEST_PORT="${MINGLE_ROUTER_TEST_PORT:-19443}"
network_octet=$(( $$ % 200 + 20 ))
export MINGLE_NETWORK_SUBNET="172.29.$network_octet.0/24" MINGLE_API_IP="172.29.$network_octet.2" MINGLE_WEB_IP="172.29.$network_octet.3"
export MINGLE_ADMIN_CREDENTIAL="$(python3 -c 'import secrets; print(secrets.token_hex(32))')"
compose=(docker compose -p "$project" -f docker-compose-prod.yml -f "$private_directory/compose.yaml")
cleanup() {
    status=$?
    trap - EXIT
    "${compose[@]}" down --volumes --remove-orphans || { if [ "$status" -eq 0 ]; then status=1; fi; }
    rm -rf "$private_directory"
    exit "$status"
}
trap cleanup EXIT
mkdir "$private_directory/tls"
chmod 755 "$private_directory/tls"
openssl req -x509 -newkey rsa:2048 -nodes -days 2 -keyout "$private_directory/tls/privkey.pem" -out "$private_directory/tls/fullchain.pem" -subj /CN=localhost -addext 'subjectAltName=DNS:localhost,IP:127.0.0.1' > /dev/null 2>&1
chmod 644 "$private_directory/tls/"*.pem
export NODE_EXTRA_CA_CERTS="$private_directory/tls/fullchain.pem"
cat > "$private_directory/router.conf" <<'NGINX'
map $http_upgrade $connection_upgrade { default upgrade; '' close; }
# Unpublished readiness listener for Docker; exercise both HTTP proxy hops.
server {
    listen 8080;
    access_log off;
    error_log /dev/null;
    location = /healthz {
        proxy_pass http://web:8080/api/health/ready;
        proxy_set_header Host localhost;
    }
    location / { return 404; }
}
server {
    listen 8443 ssl;
    ssl_certificate /etc/nginx/tls/fullchain.pem;
    ssl_certificate_key /etc/nginx/tls/privkey.pem;
    access_log off;
    error_log /dev/null;
    location / {
        proxy_pass http://web:8080;
        proxy_http_version 1.1;
        proxy_set_header Host $http_host;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection $connection_upgrade;
        # Deliberately wrong: the inner frontend must overwrite these values.
        proxy_set_header X-Forwarded-Proto http;
        proxy_set_header X-Forwarded-For 203.0.113.123;
        proxy_buffering off;
        proxy_read_timeout 100s;
    }
}
NGINX
cat > "$private_directory/compose.yaml" <<'YAML'
services:
  api:
    environment:
      Security__PublicOrigin: "https://localhost:${MINGLE_ROUTER_TEST_PORT}"
      AllowedHosts: "localhost;127.0.0.1"
  web:
    image: "${MINGLE_ROUTER_TEST_IMAGE}"
    ports: !reset []
  router:
    image: "${MINGLE_ROUTER_TEST_IMAGE}"
    pull_policy: never
    depends_on:
      web:
        condition: service_healthy
    ports:
      - "127.0.0.1:${MINGLE_ROUTER_TEST_PORT}:8443"
    volumes:
      - "${MINGLE_ROUTER_TEST_DIRECTORY}/tls:/etc/nginx/tls:ro"
      - "${MINGLE_ROUTER_TEST_DIRECTORY}/router.conf:/etc/nginx/conf.d/default.conf:ro"
    networks:
      - nginx-proxy
    healthcheck:
      test: ["CMD", "wget", "-q", "-O", "/dev/null", "http://127.0.0.1:8080/healthz"]
      interval: 3s
      timeout: 3s
      start_period: 5s
      retries: 10
networks:
  nginx-proxy:
    external: false
    name: "${MINGLE_ROUTER_TEST_NETWORK}"
YAML
"${compose[@]}" up --build --detach --wait --wait-timeout 180
base="https://localhost:$MINGLE_ROUTER_TEST_PORT"
node scripts/check-tls.mjs "$base" setup "$private_directory/session.json"
node scripts/check-tls-browser.mjs "$base" "$private_directory/tls/fullchain.pem"
"${compose[@]}" restart api
"${compose[@]}" up --detach --wait --no-recreate --wait-timeout 90
node scripts/check-tls.mjs "$base" restored "$private_directory/session.json"
