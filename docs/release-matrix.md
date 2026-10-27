# Release verification matrix

This maps product requirements to automated evidence. See the implementation plan
for run/commit status. Passing engineering checks does not waive the deferred
privacy notice or a real deployment's hosting/CDN review before public launch.

| Product requirement | Unit evidence | Functional evidence |
| --- | --- | --- |
| Account-free create/join, invitations and identity | Room rules, entry components, transport parsing | `RoomFunctionalTests`, `rooms.spec.ts`: independent cookie sessions, names/capacity/locks, refresh and duplicate tabs |
| Facilitator ownership and controls | Room rules, GamePanel/RoomPages components | Real role transfer/removal/closure, admin intervention, reconnect and cross-room denial |
| Five games and extensibility | Game rules, M4 rules, module registry, private forms | Game/M4 API tests and `game.spec.ts` / `m4.spec.ts`: all five sessions without rejoining |
| Prompt libraries and authoring | Prompt rules, PromptLibraryPanel, PromptEditor | Prompt/Admin API tests and prompt/admin browser cases: filtering, CRUD, disabled/deleted seeds and frozen round text |
| Private answers and results | Module projections, eligibility, response revisions | Raw HTTP/hub payload checks by recipient; `ResilienceFunctionalTests` verifies batched private projections and removed/expired actors |
| Retry and transaction integrity | CommandBatcher, game/room rules and client pending-state tests | Concurrent API retries, invalid peers, real SQLite failure/rollback, post-commit transport failure/restart, 400-client receipt/result assertions |
| Reconnect and mobile suspension | `useRoom` wake/order/retry/stale-terminal regressions | `resilience.spec.ts`: real socket termination, offline/online wake, stale terminal frame, blocked WebSocket/SSE with Long Polling; load reconnects 400 identities |
| Responsive/accessible experience | Component state/controls and private input tests | Desktop/360px browser projects, keyboard flows, reduced motion, axe, overflow and all game states |
| Administration | Credential/configuration and admin UI tests | Real login/expiry/revocation/rotation, paging, room/game operations, shared prompts, audit privacy/retention and browser recovery |
| Security and origin boundary | RoomBoundary/Deployment settings and trusted-peer tests | CSRF/origin/cookie/expiry/rate/input checks, HTTPS/WSS rehearsal, forged forwarding headers, 64 KiB bounds and private/static cache checks |
| Persistence and operational recovery | Storage manifest/hash/path/confirmation tests, storage health checks | Fresh migration, M4 upgrade, API restart/recreation, exact stopped-volume backup/mutate/restore with protected identities and private state |
| Single-host capacity | Percentile and integrity helpers, bounded queue/cache tests | Recorded 20 × 20 reference-host load report and full correctness scenario in CI; shared CI timing is reported rather than treated as a production SLA |
| Deployment and guidance | Document content/link/command checks | Standard Compose, production TLS profile, health/routing/log redaction, operator rehearsal and documented provider-specific pre-launch checks |
| Privacy before public launch | Deferred until notice/consent behavior is implemented | Remains open: storage/lifetime/legal basis/operator/hosting review, published notice/links and consent controls if required |

The `.github/workflows/ci.yml` required check runs build/lint/format/contracts,
unit/API suites, real Docker desktop/mobile browsers, HTTPS/storage rehearsal and
400-client capacity/reconnect integrity. Synthetic credentials, certificates,
private room data and backups never form committed verification evidence. Load
reports include only counts/timing and sanitized machine characteristics.
