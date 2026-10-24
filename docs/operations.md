# Running Mingle

## Current scope

The catalogue, guest sessions, rooms, live lobbies, facilitator controls, and
recovery are implemented. Playable games, prompts, and administration come later.
Create a room at `/host` or join an invitation at `/join?code=ABC234` using the
actual code shared by the host. Clearing cookies or changing devices loses that
guest identity; transfer ownership before leaving if recovery might be needed.

## Local containers

Run `docker compose up --build --detach --wait`, then visit
http://localhost:8080. Both containers run as non-root users with dropped
capabilities. Only Nginx publishes a port, bound to the host's loopback interface.
The API becomes ready after migrations and storage initialization succeed.

Use `docker compose ps` for health status and `docker compose logs api web` for
logs. `docker compose down` preserves data. Avoid `down --volumes` unless deleting
that installation's data is intentional.

Nginx access logs contain method, path, status, and byte count, excluding query
strings, headers, and referrers. Request error logs are suppressed because their
fixed format can expose invitation and SignalR token URLs; use sanitized access
statuses, API logs, and health endpoints for diagnostics. Vite proxy diagnostics
also redact queries. Do not enable raw request/header/body logging for rooms.

| Setting | Default | Purpose |
| --- | --- | --- |
| `TEAMGAMES_PORT` | `8080` | Local Nginx host port, set via environment or `.env` |
| `Storage__DataDirectory` | `.data` locally; `/data` in Compose | Directory holding `teamgames.db` and `keys/` |
| `ASPNETCORE_ENVIRONMENT` | `Production` in Compose | OpenAPI is exposed only in Development |
| `API_PROXY_TARGET` | `http://127.0.0.1:5100` | Vite development/preview proxy destination |
| `Security__AllowInsecureCookies` | `false`; explicitly `true` in Development/local Compose | Permit cookies over local HTTP; production defaults require Secure cookies |
| `Security__PublicOrigin` | Request scheme and host | Exact allowed browser origin when an external deployment origin is configured |
| `Rooms__Capacity` | `20` | Maximum members including facilitator; accepted range 2–100 |
| `Rooms__IdleHours` | `24` | Expiry since last accepted user activity; accepted range 1–168 |
| `Rooms__RetentionHours` | `24` | Retain closed/expired room data before deletion; accepted range 1–168 |
| `Rooms__GuestDays` | `7` | Absolute guest credential lifetime; accepted range 1–7 |
| `Rooms__EntryRateLimit` | `90` per minute per API peer IP | Shared budget for session bootstrap, create, and join; use an explicit override for test traffic |

For Docker, add ASP.NET settings under `api.environment` in a Compose override;
host variables are not automatically passed into the container. `.env` currently
supplies the published port. For `dotnet watch`, set these variables directly in
its environment. Entry limits see the API peer address; shared-proxy traffic shares
that budget until a trusted client-address forwarding profile is configured.

Keep `.env`, databases, and key files out of Git. Docker's `app-data` volume is
initialized with ownership for the API's non-root `app` user. A host bind mount
must provide equivalent write permissions; a read-only or invalid storage path
fails startup. One API process owns the database; do not scale API replicas.

## Health and routing

- `/api/health/live` reports whether the process serves requests.
- `/api/health/ready` reports whether the initialized database is accessible.
  A failed readiness check returns HTTP 503 without storage details.
- `/api/games` and `/api/games/{id}` serve catalogue information.
- `/api/session` bootstraps antiforgery and guest identity; all JSON mutations
  require `X-CSRF-TOKEN` and room mutations require the protected HttpOnly cookie.
- `/api/rooms`, `/api/rooms/join`, `/api/rooms/{id}`, and the room's `/commands`
  endpoint implement entry, snapshots, and authorized facilitator changes.
- `/hubs/rooms` authenticates the guest and checks membership at subscription and
  before every delivery. WebSocket origins are checked explicitly. Long Polling
  is available; membership and ownership are independent of transport connections.
- Nginx `/healthz` checks the frontend independently of the API.
- Unknown API/hub URLs return API errors, never the SPA HTML shell. Frontend deep
  links load the SPA, which renders its own not-found screen for unknown pages.

The proxy re-resolves the API service address after container recreation. API and
hub routes are not cached or buffered. Hashed assets receive immutable cache
headers; HTML is revalidated. Fonts are bundled, so the UI makes no external font
requests. Browser security headers include a same-origin Content Security Policy.

## CDN and HTTPS boundary

The supplied Compose profile is for loopback HTTP and explicitly permits insecure
local cookies. Production cookie defaults are Secure and are covered by HTTPS API
tests. A public TLS edge profile and forwarded-header trust configuration are not
supplied yet: complete and verify that deployment configuration before publishing
rooms. Do not simply expose the local HTTP profile to the internet. A CDN must
bypass caching for `/api/*` and `/hubs/*`, preserve cookies and WebSocket upgrade
headers, and allow the frontend's static assets. The prepared realtime proxy uses
100-second read/send timeouts; match these to the chosen provider during M6.
Preserve the public Host including its port. Configure `Security__PublicOrigin`
when TLS termination changes the origin seen by the API; HTTPS scheme forwarding
must also be restricted to trusted proxies for Secure antiforgery cookies.

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

M1 migration adds guests, rooms, memberships, and command receipts without changing
the installation marker or key ring. An upgrade test starts from the M0 schema.
Rooms expire after accepted-activity inactivity; pings, snapshots, and receipt
replays do not extend them. A two-second startup/periodic maintenance loop marks
expiry, ends live access, and deletes retained memberships/receipts with their room.
Expired guest verifiers are deleted too. Existing members can read a terminal
closed view until cleanup; new joins, subscriptions, and changes are blocked.

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
