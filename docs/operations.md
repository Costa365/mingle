# Running Mingle

## Current scope

The catalogue, guest sessions, rooms, live lobbies, facilitator controls, and
recovery, all five games, prompt authoring and administrator operations are implemented.
Consult the implementation plan for required verification evidence.
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
| `Admin__Credential` / Compose `MINGLE_ADMIN_CREDENTIAL` | absent (disabled) | Deployment administrator credential, 16–1024 characters; use a random token |
| `Admin__SessionMinutes` / Compose `MINGLE_ADMIN_SESSION_MINUTES` | `60` | Absolute session lifetime, 1–1440 minutes |
| `Admin__AuditRetentionDays` / Compose `MINGLE_ADMIN_AUDIT_RETENTION_DAYS` | `7` | Content-free audit retention, 1–30 days |
| `Admin__LoginRateLimit` | `5` per minute per API peer IP | Administrator login attempts, 1–100; configurable in an API override |
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
host variables are not automatically passed into the container. `.env` supplies the published port and the three `MINGLE_ADMIN_*` settings above. For `dotnet watch`, set these variables directly in
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

## Shared server router and Cloudflare

For `mingle.costa365.site` on the existing server, use the standalone production
file (do not combine it with `compose.yaml` or the direct-TLS override):

```bash
docker compose -f docker-compose-prod.yml up --build --detach --wait
docker compose -f docker-compose-prod.yml ps
```

Open **https://mingle.costa365.site**. The external Docker network `nginx-proxy`
must already exist and contain the server's router. The frontend advertises
`VIRTUAL_HOST=mingle.costa365.site` and `VIRTUAL_PORT=8080`. Its loopback-only
diagnostic port is **8085**, so other services using host port 8080 are unaffected.
Check routing from the host with:

```bash
curl --fail -H 'Host: mingle.costa365.site' http://127.0.0.1:8085/api/health/ready
```

Use the public HTTPS URL for gameplay: Secure cookies will not work through the
plain HTTP diagnostic port. This profile sets the exact public origin, requires
Secure cookies, and trusts only the frontend's fixed private IP at the API. The
frontend overwrites forwarded scheme with `https` and forwarded address with its
immediate peer; it never trusts incoming client-IP or scheme headers. Rate limits
therefore share the router's peer budget. Only the frontend joins the shared
router network; the API and persistent `app-data` volume stay private to Mingle.
The subnet/IP overrides described below also apply to this file.

Require HTTPS at Cloudflare (Always Use HTTPS) and preserve Host, Origin,
cookies and WebSocket upgrades through the existing router. Bypass caching for
API/hub paths as listed below. Cloudflare Flexible mode reaches the server over
HTTP; Full (strict) requires a valid origin certificate on the shared router.
This Compose file does not issue or install an origin certificate or change your
Cloudflare configuration. See [Cloudflare encryption modes](https://developers.cloudflare.com/ssl/origin-configuration/ssl-modes/).

An optional `MINGLE_ADMIN_CREDENTIAL` in the ignored `.env` enables `/admin`.
Other `.env` values from the direct-TLS profile, such as its origin/certificate
directory, are not used here. To update or stop this installation, keep using
`-f docker-compose-prod.yml`; `down` preserves data, while `down --volumes`
deletes it. The normal project name is `mingle` when run from this directory;
check `docker compose ls` before using the backup commands below. Do not run
the local and production files as separate installations with the same project
name. If local Mingle containers already exist, back up their volume before
switching profiles; Compose reuses the project's existing volume.

`bash scripts/verify-router.sh` rehearses this topology in an isolated project
with a disposable TLS router/network/certificate, real API/SQLite, Secure cookies,
WebSockets, browser gameplay, hostile forwarding headers, origin/CSRF rejection,
and identity/private-answer recovery after API restart. It never uses the live
`nginx-proxy` network or host port 8085. Actual Cloudflare/router verification and
the public-launch privacy work remain required for public launch.

## CDN and HTTPS boundary

The default Compose file is for loopback HTTP. The opt-in production override
terminates TLS directly at the non-root Nginx edge, publishes only HTTPS, sets
Secure cookies and trusts forwarded scheme/address only from that edge's fixed
container IP. The edge overwrites incoming forwarding headers. No
`X-Forwarded-Host` or unrestricted proxy/network trust is enabled.

Provision a valid certificate and full chain for your domain, outside this repo.
The container's Nginx UID 101 must be able to read `fullchain.pem` and
`privkey.pem`; keep the key restricted to its owner/group and mount it read-only.
For example, a certificate directory owned by root with group 101, mode 750 and
key mode 640 works on a Linux Docker host. The test rehearsal uses a disposable
self-signed CA; public deployment requires a certificate trusted by users.

Set these private deployment values in the ignored `.env`:

```dotenv
MINGLE_PUBLIC_ORIGIN=https://mingle.example.com
MINGLE_TLS_DIRECTORY=/srv/mingle/tls
MINGLE_HTTPS_PORT=443
# Configure MINGLE_ADMIN_CREDENTIAL separately if administration is wanted
```

The production override requires Docker Compose 2.24.4 or later for its
[port replacement](https://docs.docker.com/reference/compose-file/merge/#replace-value).
Start the TLS profile explicitly:

```bash
docker compose -f compose.yaml -f infra/compose.production.yaml up --build --detach --wait
```

The profile requires an exact HTTPS origin and certificate directory. Its default
private subnet is `172.30.96.0/24` (API `.2`, edge `.3`). If that subnet conflicts
with your host, override `MINGLE_NETWORK_SUBNET`, `MINGLE_API_IP` and
`MINGLE_WEB_IP` together; only the selected edge IP is trusted. HTTPS binds all
host interfaces by default; `MINGLE_HTTPS_BIND=127.0.0.1` limits exposure when an
additional local edge is used. The internal plain HTTP listener serves only its
health probe and is not published. Direct clients cannot select their scheme/IP
with forged forwarding headers. The API and proxy both limit request bodies to
64 KiB. HSTS applies only to the HTTPS profile, without assuming subdomain policy.

Renew certificates using your chosen certificate manager, then reload Nginx:

```bash
docker compose -f compose.yaml -f infra/compose.production.yaml exec web nginx -s reload
```

TLS tests use certificate verification, Secure guest/admin/antiforgery cookies,
WSS, forgery/origin rejection and cache checks through this same profile. No
public DNS, certificate issuance or external hosting is configured automatically.

For a CDN, apply these explicit policies (not a cache-everything rule):

| Request | CDN policy |
| --- | --- |
| `/api`, `/api/*`, `/hubs`, `/hubs/*` | Bypass cache regardless of cookies, method or status; preserve cookies and upgrades |
| `/assets/*` with a successful hashed asset | Respect `Cache-Control: public, max-age=31536000, immutable` |
| HTML, SPA routes and errors | Respect origin revalidation/no-store; never cache personalized API output |
| WebSocket upgrade | Preserve Host, Origin, Cookie and Upgrade; allow fallback HTTP transports too |

Example cache-bypass condition: path equals `/api` or `/hubs`, or starts with
`/api/` or `/hubs/`. Preserve the public Host including its port and use verified
HTTPS to the origin. Do not turn on client-controlled real-IP headers: with a CDN
in front, the direct peer is the CDN and rate limits share that peer's budget
until you configure a provider-specific, restricted edge trust policy. Test the
selected provider's socket limits, cache rules and origin access restrictions
before public launch; that provider rehearsal remains deployment-specific.

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
image and the matching data backup for rollback. M6 includes a tested stopped-volume backup/restore rehearsal. M2 verifies private answers and stage/results
recovery across API restart and a local M1-to-M2 upgrade with a stopped-volume
backup, preservation comparisons, and SQLite integrity validation.

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

## M3 prompt persistence

M3 adds the fourth EF migration and a `Prompts` table. Startup seeds 350 built-in
entries by stable ID. Fingerprints preserve administrator edits and disabling;
room-local prompts remain scoped to their room and are deleted with its retained
data. A seed conflict preserves existing content rather than failing startup.

Game-state JSON advances to schema version 3 on its next mutation; versions 1 and
2 remain readable. M2's twelve-index prompt usage maps to stable IDs without
changing saved text or results. Keep the matching pre-upgrade database and key
backup if rollback to an older image is needed. The local M2-to-M3 upgrade was
verified against a stopped-volume backup, including SQLite integrity and keys.

## Administrator access

Open `/admin` or use the footer's Administration link. No configuration means no
administrator access. To enable it locally, put `MINGLE_ADMIN_CREDENTIAL` in the
ignored `.env` file with a random token (at least 16 characters), then run
`docker compose up --build --detach --wait`. Generate a token using a password
manager or `openssl rand -hex 32`. Keep it out of Git, logs and shared screenshots.
For a direct API process use `Admin__Credential` in its deployment environment.
Use HTTPS/Secure cookies for a public deployment as described above.

Sign in with that credential. The session ends after one hour by default; Sign
out revokes it server-side for duplicate tabs too. Replacing/removing the
configured credential and recreating the API invalidates previous sessions.
Guest identity/cookies remain separate; administrator access never makes you a
player or reveals private statements, choices or lie indices.

Rooms lists active and retained rooms with paging and status filters. Manage a
room to lock/unlock joining, replace the facilitator, remove a non-facilitator,
end an active game without revealing unfinished answers, close it or delete it.
Reassign the facilitator before removing them. Deletion requires the exact room
code and removes memberships, game data, room prompts and guest command receipts.
Connected browsers receive the changes through the normal room publisher.

Shared prompts manages all four prompt libraries. Edit/disable/delete affects
future draws; existing rounds retain their saved content. Seeded prompt edits,
disabling and deletions survive startup seeding. Audit shows only action names,
times and target identifiers. Maintenance removes old audit events after the
configured retention and removes expired/rotated administrator sessions.

If a command response is lost, use Retry same action; its UUID is retained across
reload in the current tab and admin session. A stale room/prompt conflict requires
reviewing refreshed state and a new deliberate action. Refresh failures after a
confirmed write never invite another mutation.

## Backup and restore commands

The storage tool operates on one existing Compose project's named `/data` volume.
It does not recreate the container or change its deployment environment. Both
operations stop the API, then restart it and wait for health. Backups include the
whole database/WAL/key directory with a SHA-256 manifest and SQLite integrity and
foreign-key checks. New backup directories are private; existing paths are never
overwritten. Keep backups encrypted/off-host as appropriate for your deployment.

```bash
python3 scripts/storage.py backup --project team-games --directory /srv/private/mingle-backup-2026-10-27
python3 scripts/storage.py restore --project team-games --directory /srv/private/mingle-backup-2026-10-27 --confirm team-games
```

Use your actual Compose project name (`docker compose ls`). Restore requires that
exact name as confirmation, validates all files before stopping/modifying the
app, rejects changed/extra files and symlinks, and restores ownership for `app`.
It supports named volumes; bind-mount installations need an equivalent operator
procedure. The maintenance container has no network and mounts only that project's
volume plus the read-only backup. Neither command prints database or key content.

Schedule a maintenance window for restore and have players refresh their pages
when it completes: restored room versions can predate a browser's latest state.
Changes accepted after the backup are intentionally rolled back. Keep the matching
application image and backup together; do not run an old image against a newer
schema. If recovery fails, retain the backup and inspect readiness/permissions
before retrying. Never use `docker compose down --volumes` on live data.

Run `bash scripts/verify-production.sh` to rehearse HTTPS, backup, a synthetic
post-backup mutation, SIGKILL/WAL recovery, restore and cookie/identity/private-answer recovery in an
isolated project. Temporary keys, credentials, backups and room state are deleted
by its exit trap and are not verification artifacts.

## Troubleshooting

| Symptom | Check/action |
| --- | --- |
| API unhealthy | Check sanitized API logs, writable `/data`, certificate/profile settings and migration readiness; keep the volume intact |
| Login/commands rejected with origin/CSRF errors | Use the configured public HTTPS origin, refresh the page/token, verify the edge's IP and origin configuration; do not disable validation |
| WebSocket interrupted | Clients resubscribe and reconcile saved state; verify CDN upgrades/timeouts, allow Long Polling and use Retry connection after a sustained outage |
| Two incognito windows are the same player | They share the browser session; use separate profiles/contexts for independent players |
| Another tab changed an answer/prompt | Review refreshed state before deliberately replacing it; do not replay a stale revision with a new UUID automatically |
| Admin unavailable | Configure the deployment credential and recreate the API; missing configuration intentionally grants no access |
| Restored browser shows stale state | Refresh the page after the operator's restore window |
| Repeated 429 responses | Check per-peer budgets and proxy trust; do not raise production limits just to hide a configuration error |
