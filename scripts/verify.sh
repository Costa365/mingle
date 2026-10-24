#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

python3 -m unittest discover -s tests/verification -p 'test_*.py'
dotnet restore TeamGames.slnx --locked-mode
dotnet build TeamGames.slnx --no-restore --disable-build-servers -m:1
dotnet run --project tools/TeamGames.Contracts --no-build -- --check
dotnet format TeamGames.slnx --verify-no-changes --no-restore
dotnet test TeamGames.slnx --no-build --disable-build-servers -m:1
npm --prefix web ci --no-fund --no-audit
npm --prefix web run typecheck
npm --prefix web run lint
npm --prefix web run format:check
npm --prefix web run test -- --run
npm --prefix web run build
docker compose config --quiet
