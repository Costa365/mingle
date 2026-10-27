#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
project="mingle-load-${GITHUB_RUN_ID:-$$}"
export TEAMGAMES_PORT="${TEAMGAMES_LOAD_PORT:-18180}"
compose() { docker compose -f compose.yaml -f infra/compose.load.yaml -p "$project" "$@"; }
cleanup() { status=$?; trap - EXIT; compose down --volumes --remove-orphans || { if [ "$status" -eq 0 ]; then status=1; fi; }; exit "$status"; }
trap cleanup EXIT
compose up --build --detach --wait --wait-timeout 180
node scripts/load/run.mjs "http://127.0.0.1:$TEAMGAMES_PORT"
