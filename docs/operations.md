# Running the foundation

## Current scope

This milestone serves a public, read-only game catalogue. Rooms, authentication,
SignalR subscriptions, administration, and gameplay arrive in later milestones.
Do not treat the planned authorization architecture as already implemented.

## Local containers

Run `docker compose up --build --detach --wait`, then visit
http://localhost:8080. Both containers run as non-root users with dropped
capabilities. Only Nginx publishes a port, bound to the host's loopback interface.
The API becomes ready after migrations and storage initialization succeed.

Use `docker compose ps` for health status and `docker compose logs api web` for
logs. `docker compose down` preserves data. Avoid `down --volumes` unless deleting
that installation's data is intentional.

| Setting | Default | Purpose |
| --- | --- | --- |
| `TEAMGAMES_PORT` | `8080` | Local Nginx host port, set via environment or `.env` |
| `Storage__DataDirectory` | `.data` locally; `/data` in Compose | Directory holding `teamgames.db` and `keys/` |
| `ASPNETCORE_ENVIRONMENT` | `Production` in Compose | OpenAPI is exposed only in Development |
| `API_PROXY_TARGET` | `http://127.0.0.1:5100` | Vite development/preview proxy destination |

Keep `.env`, databases, and key files out of Git. Docker's `app-data` volume is
initialized with ownership for the API's non-root `app` user. A host bind mount
must provide equivalent write permissions; a read-only or invalid storage path
fails startup. One API process owns the database; do not scale API replicas.

## Health and routing

- `/api/health/live` reports whether the process serves requests.
- `/api/health/ready` reports whether the initialized database is accessible.
  A failed readiness check returns HTTP 503 without storage details.
- `/api/games` and `/api/games/{id}` serve catalogue information.
- Nginx `/healthz` checks the frontend independently of the API.
- Unknown API/hub URLs return API errors, never the SPA HTML shell. Frontend deep
  links load the SPA, which renders its own not-found screen for unknown pages.

The proxy re-resolves the API service address after container recreation. API and
hub routes are not cached or buffered. Hashed assets receive immutable cache
headers; HTML is revalidated. Fonts are bundled, so the UI makes no external font
requests. Browser security headers include a same-origin Content Security Policy.

## CDN and HTTPS boundary

For a public deployment, put an HTTPS reverse proxy or tunnel in front of the local
Nginx port. Provider-specific TLS/domain configuration is outside M0. A CDN must
bypass caching for `/api/*` and `/hubs/*`, preserve cookies and WebSocket upgrade
headers, and allow the frontend's static assets. The prepared realtime proxy uses
100-second read/send timeouts; match these to the chosen provider during M1/M6.
Forwarded-header trust and secure cookie behavior must be implemented and tested
with authentication before exposing private rooms. There are no private rooms yet.

## Persistence, upgrades, and backups

`app-data` contains the SQLite database, its WAL files while running, and the Data
Protection key ring. Treat the entire volume as private. Keys currently rely on
filesystem permissions; host administrators can read them. Add encryption at rest
at the deployment layer if needed before storing sensitive room data.

Before an upgrade, stop the API and back up the complete volume using the host's
volume backup tooling. Copying a live database file without its WAL is not a
consistent backup. Keep the database and keys together so protected identities
can be recovered. Restore only while the API is stopped and preserve its ownership.

Startup applies EF migrations. Review future migrations before upgrading; an
older image is not automatically compatible with a newer schema. Keep the previous
image and the matching data backup for rollback. A full operator backup/restore
rehearsal is scheduled for M6; M0 verifies host/container restart persistence.

## Automated deployment verification

After installing dependencies and the Playwright Chromium browser, run:

```bash
bash scripts/verify.sh
bash scripts/verify-compose.sh
```

The Compose verifier creates its own uniquely named project and volume. It checks
HTTP routing and cache headers, copies the database only while stopped, recreates
the API, compares the installation record/migrations/key ring, then runs browser
flows and a real API outage/retry through Nginx on desktop and mobile. Its exit trap
removes only the temporary test project and data.
