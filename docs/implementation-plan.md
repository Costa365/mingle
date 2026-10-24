# Implementation plan

## Goal and current state

Deliver the application in [product.md](product.md) using the proposed
[architecture](architecture.md). This plan was initialized on 2026-10-23 from
[idea.md](../idea.md). The first stage, **M0 — Buildable foundation**, is implemented
and locally verified. It includes a public catalogue and game
instructions, not playable rooms. M1–M6 remain unimplemented. M0's hosted CI gate
is pending because this repository has no Git remote configured.

Build in order. Keep each change focused enough to review and demonstrate. Update
the progress and decision sections as implementation reveals new information.

## Definition of done for every feature

Every feature in every milestone requires automated unit tests and automated
functional tests, delivered with its implementation. This includes small changes,
each individual game, all room/prompt/admin controls, and recovery behavior.
Testing is part of each milestone, not deferred to M6.

- Record the feature's acceptance criteria, its unit test files/cases, its
  functional test files/cases, and their execution results alongside its task.
- Unit tests cover isolated rules, validation, calculations, and component
  behavior. Functional tests exercise real API/hub and storage behavior, plus
  browser flows for UI features, including relevant failure and permission cases.
- Both layers must pass locally and in CI before a feature is marked complete.
  Until CI is available in M0, record local results and keep CI verification
  pending; M0 is not complete until the required CI suites pass too.
- Missing, skipped, quarantined, or blocked required tests leave the feature
  incomplete. Manual review and a test of a similar feature are not substitutes.
- Bug fixes include regression coverage. Follow the detailed test definitions in
  [development.md](development.md).

M0 acceptance coverage (paths are relative to the repository root):

| Feature / acceptance criteria | Unit tests | Functional tests | Execution evidence / remaining gaps |
| --- | --- | --- | --- |
| People icon replaces all star marks; wordmark reads "Mingle" with the tagline "Team games"; headings contain no full stops | `web/src/app/App.test.tsx`: brand name/tagline, decorative icon semantics, punctuation on home/how-to/not-found/detail and error headings | `web/e2e/foundation.spec.ts`: loaded icons, exact brand/tagline and Mingle browser titles, punctuation across routes and error state, both viewports, axe and keyboard checks | 27 frontend unit tests and 20 browser cases pass locally; build/lint/format pass; screenshots reviewed; hosted CI pending |
| All five catalogue entries have valid metadata; exact IDs resolve and unknown IDs fail | `tests/TeamGames.Core.Tests/GameCatalogTests.cs`: metadata, uniqueness, exact/unknown IDs; `web/src/api/games.test.ts`: malformed/duplicate payloads and request failures | `tests/TeamGames.Api.Tests/FoundationFunctionalTests.cs`: catalogue and every detail through HTTP; `web/e2e/foundation.spec.ts`: all five detail flows | Local .NET and frontend suites pass; hosted CI pending |
| Home, detail, how-to, and not-found routes render and allow navigation; hosting is clearly unavailable | `web/src/app/App.test.tsx`: each detail route, navigation, instructions, recovery links, future-hosting explanation | `web/e2e/foundation.spec.ts`: each route through the real backend, direct loads and reloads, desktop/mobile | Browser results recorded below; hosted CI pending |
| Loading is announced; an API failure offers retry and recovers | `web/src/app/App.test.tsx`: pending request, cancellation, error/retry on home and detail; `web/src/api/games.test.ts`: rejected/invalid responses | `web/e2e/foundation.spec.ts`: delayed real request and real API stop/restart/retry in both viewports | Browser results recorded below; hosted CI pending |
| Storage paths are validated; fresh SQLite is migrated; readiness reflects storage while liveness remains independent | `tests/TeamGames.Core.Tests/StoragePathsTests.cs`: relative/absolute/invalid paths; `tests/TeamGames.Api.Tests/StorageHealthCheckTests.cs`: success, failure, sanitized exceptions, cancellation | `tests/TeamGames.Api.Tests/FoundationFunctionalTests.cs`: migration, dropped storage table, invalid/unwritable configuration | Local .NET suites pass; hosted CI pending |
| Database identity and Data Protection keys survive host/container recreation | Storage path and readiness unit cases above cover persistence configuration and failure reporting | `FoundationFunctionalTests.cs`: fresh host restores installation and decrypts a protected value; `scripts/verify-compose.sh`: recreates API and compares database, migrations, WAL mode, and key ring | Container persistence checks pass; hosted CI pending |
| Same-origin deployment routes API errors correctly, supports SPA deep links, and applies cache/security headers | Catalogue request/route and health unit cases above | `scripts/check-deployment.py`: HTTP assertions through Nginx; `foundation.spec.ts`: desktop/mobile navigation, keyboard skip link, overflow and axe checks; `FoundationFunctionalTests.cs`: development-only OpenAPI | Deployment checks pass; browser results below; hosted CI pending |

## M0 — Buildable foundation

**Status:** All implementation items below are present. Checkboxes remain pending
until local verification and the required hosted CI run both pass. The GitHub
Actions workflow runs both verification scripts and uploads browser artifacts;
the repository owner must connect a GitHub remote and require its **Unit and
functional tests** check before this milestone can be marked complete.

- [ ] Create the .NET 10 solution, API/Core/Infrastructure projects, and test
  projects; enable nullable references and consistent formatting.
- [ ] Scaffold React/TypeScript/Vite with routing, strict types, linting, component
  testing, and a Playwright setup.
- [ ] Pin compatible SDK/runtime/package versions and commit lockfiles. Add
  `.gitignore`, `.editorconfig`, `.env.example`, and documented configuration.
- [ ] Add frontend/API Docker builds, Nginx routing, Compose volumes and health
  checks. Add EF Core SQLite, the first migration, and key persistence.
- [ ] Establish an initial app shell, error/loading components, and design tokens
  usable at 360px and desktop widths.
- [ ] Add meaningful unit and functional coverage for initial app-shell behavior,
  configuration validation, readiness, routing, and persistence initialization.
- [ ] Add required CI jobs for unit, API/hub functional, and browser functional
  suites; document verified local commands and test evidence.

**Acceptance:** A fresh checkout can install dependencies, build both applications,
run initial checks, and start via `docker compose up --build`. The browser reaches
the app and a readiness endpoint through the same origin. Storage survives API
recreation. No placeholder test is counted as evidence of gameplay.

## M1 — Rooms, identity, and recovery foundation

Depends on M0.

- [ ] Implement guest credentials, antiforgery handling, memberships, facilitator
  assignment, and room creation/joining with input limits and rate limits.
- [ ] Build create/join/lobby screens, invite link/code copying, participant list,
  join locking, player removal, ownership transfer, and room closure.
- [ ] Add authorized hub subscriptions, per-viewer snapshots, monotonically
  increasing versions, presence, and duplicate-tab handling.
- [ ] Add command receipts and transactional room mutations from the start.
- [ ] Implement initial connection retry, reconnect/resubscribe/snapshot recovery,
  stale-version rejection, periodic reconciliation, and recovery UI.
- [ ] Implement room expiry and retained-data cleanup.
- [ ] Add unit and functional tests for every room/identity/control/recovery
  feature above, including independent browser flows for each UI action.

**Acceptance:** Independent browsers join the same room and see live roster changes.
Reload and connection loss preserve membership and ownership. Duplicate tabs count
as one player. Another room cannot read snapshots or subscribe. A removed player
loses HTTP and hub access. Restart preserves the room; fake-time expiry deletes
its retained data and stops access.

## M2 — First complete game: This or That

Depends on M1. This is the first playable vertical slice.

- [ ] Define the game module/registry contract and the shared round command model.
- [ ] Implement responding, pass/change answer, reveal, skip, next round, finish,
  and return-to-lobby behavior with a small reviewed prompt seed.
- [ ] Add separate stage/response revisions, deduplication, frozen round rosters,
  per-viewer projections, and finalized results.
- [ ] Build facilitator controls and player answer/reveal screens with visible
  response counts and clear unavailable actions.
- [ ] Add unit and functional tests for every game action and UI state, including
  rules, authorization, concurrent commands, hidden state, and browser flows.

**Acceptance:** One facilitator and at least two players finish multiple rounds.
The facilitator advances with absent or disconnected players, including zero
answers. Double-clicks/retries cannot advance twice. Simultaneous valid player
answers succeed. A stale answer after reveal fails clearly. Choices stay private
until reveal. API restart restores the exact last acknowledged stage and answers.

## M3 — Prompt browsing, authoring, and launch content

Depends on M2.

- [ ] Add typed prompt storage, repeatable seed updates, category/tag filtering,
  search, manual selection, and random drawing without repetition.
- [ ] Let facilitators create/edit/delete room prompts with validation. Snapshot
  selected prompt text so later edits do not alter rounds.
- [ ] Supply at least 200 reviewed This or That pairs across eight categories and
  50 prompts for each of the other three library-driven games.
- [ ] Add schema, duplicate, option, count, and seed-preservation checks. Record
  completion of the manual editorial review.
- [ ] Add unit and functional tests for each prompt browsing, selection, authoring,
  validation, and seed-update feature; test the corresponding browser flows.

**Acceptance:** A facilitator can choose a prompt, draw a random prompt, and play a
newly authored prompt. Refresh/restart preserves it. Pool exhaustion offers a reset.
Room prompts never leak into another room; seed reruns preserve edited content.

## M4 — Remaining games through the module interface

Depends on M2 and M3. Implement in this order to increase rule complexity gradually.

- [ ] Would You Rather?: reuse binary mechanics, add its content and presentation.
- [ ] Never Have I Ever: add response labels, aggregate reveal, and pass handling.
- [ ] Most Likely To: add nomination eligibility, no self-voting, removed-player
  behavior, ties, and insufficient-player handling.
- [ ] Two Truths and a Lie: add private statement collection, lie validation,
  presenter sequencing, guessing, reveal, and incomplete/skipped submissions.
- [ ] Add unit and functional tests for each game's actions, rules, projections,
  and UI states, including a complete browser flow for each game. Document how
  to add a module, its client view, and both test layers.

**Acceptance:** All five games work in one room without rejoining between games.
Each supports facilitator advance and reconnection at every input/reveal stage.
The lie is absent from non-author payloads until reveal, including facilitator and
administrator views. Adding the later games does not change room/reconnect code.

## M5 — Administrator operations

Depends on M1 and M3; integrate with all games after M4.

- [ ] Add configured administrator login, separate expiring sessions, rate limits,
  logout/revocation, and authorization tests.
- [ ] Build paginated room/game-session overview and detail screens with status,
  participant counts, and last activity; exclude hidden answer data.
- [ ] Add lock/unlock, remove player, reassign facilitator, end game, close room,
  and confirmed room deletion using the same command path as gameplay.
- [ ] Add shared prompt creation/editing/disabling/deletion and content-free audit
  records with retention cleanup.
- [ ] Add unit and functional tests for every admin action, authentication/session
  behavior, shared-prompt operation, and audit feature, including browser flows.

**Acceptance:** A configured administrator can manage another browser's room and
shared prompts. Unauthenticated players cannot call admin endpoints. Missing
configuration exposes no admin access. Forced changes reach connected clients;
deletion prevents reconnect and removes dependent data. Audit entries contain no
credentials or answer content.

## M6 — Deployment resilience and experience verification

Depends on M0–M5. Mobile/accessibility and security checks also run during earlier
milestones; this milestone exercises the integrated product.

- [ ] Complete visual/interaction polish, game identities, empty/error states,
  responsive facilitator controls, keyboard behavior, and reduced-motion behavior.
- [ ] Add or update unit and functional tests for every behavior added or changed
  during polish; reconcile feature records against the product requirements.
- [ ] Exercise real proxy routing with socket termination, missing WebSocket
  support/Long Polling, mobile suspension, stale snapshots, and reconnect storms.
- [ ] Verify cookie/antiforgery/origin enforcement, rate limits, input bounds,
  role changes, expired credentials, and CDN cache exclusions.
- [ ] Verify restart/recreation, commit-before-notification failure, expiry,
  migrations, persistent keys, and a documented backup/restore procedure.
- [ ] Run a load scenario with 20 rooms × 20 participants on a recorded host.
  Proposed target: p95 command-to-visible-update below one second in the local
  deployment, no duplicate transitions, and no lost acknowledged responses.
- [ ] Finish the operator guide, configuration reference, CDN/TLS examples,
  gameplay guide, troubleshooting, and module extension guide.
- [ ] Run the release verification matrix and record results/known limitations.

**Acceptance:** All five games pass multi-browser/mobile checks through Compose.
Reconnect and restart scenarios preserve correctness. The operator can deploy,
upgrade, back up, and restore using documented commands. Capacity claims match
measured evidence. Every required feature in the product document has passing unit
and functional coverage linked to its acceptance criteria. An unresolved required
test or acceptance failure prevents release completion and is recorded explicitly.

## Release verification matrix

| Concern | Evidence |
| --- | --- |
| All features | Acceptance criteria mapped to passing unit and functional cases, with local and CI results |
| Rules and stage progression | Deterministic unit tests for every game and forced transition |
| Concurrent/retried commands | Real SQLite integration tests for duplicate IDs, edit conflicts, submit/advance races |
| Identity and permissions | Create/join/transfer/kick/admin/expiry tests with independent cookie sessions |
| Hidden information | Assert serialized HTTP/hub payloads for each role and stage |
| Live recovery | Multi-context browser tests for reconnect, reload, duplicate tabs, and stale snapshots |
| Durable recovery | API restart after acknowledged writes and after commit before notification |
| Proxy/CDN behavior | Compose tests for forced socket closure, fallback, cookie forwarding, and cache headers |
| Accessibility and mobile | Keyboard/touch checks, automated accessibility scan, and narrow/desktop screenshots |
| Content | Seed/schema/duplicate checks plus manual prompt review |
| Operations | Fresh install, upgrade, backup/restore, health checks, and measured load run |

## Progress and evidence

- 2026-10-23: Read the original brief and inspected the workspace. Added repository
  guidance, product requirements, architecture, development notes, and this plan.
  Verified tooling is present; no application exists to build or test.
- Documentation verification: all 22 local links resolve, Markdown code fences
  are balanced, all five games are specified, and no implementation task is
  marked complete. The original `idea.md` remains unchanged.
- Workflow updated at the user's request: every feature requires unit and
  functional tests in the same change, tracked acceptance coverage, and passing
  CI results before completion. The following implementation supplies those layers.
- 2026-10-23: Implemented M0's .NET/React scaffold, five-game public catalogue,
  error/loading/retry and not-found views, real SQLite initialization, persisted
  Data Protection keys, Docker/Nginx deployment, verification scripts, and CI.
- `bash scripts/verify.sh`: passed locked restores, backend build/format,
  **19 backend unit tests + 7 API functional tests**, frontend type/lint/format,
  **23 frontend unit/component tests**, production build, and Compose validation.
  Local execution used writable temporary package/tool caches and permission to
  bind test sockets; these adjustments are documented in the development guide.
- Deployment verification passed HTTP routing, error/content types, deep links,
  security/cache headers, fresh migration, WAL, database identity, and key-ring
  persistence after API container recreation. Final `bash scripts/verify-compose.sh`
  passed all deployment assertions and **20/20 browser cases** through Nginx
  (46.9 seconds for the browser suite), with zero skipped tests. The verifier
  removed its temporary containers and volume after success.
- The initial browser run found insufficient contrast on a desktop card number
  (4.45:1). Increased text opacity; the existing axe assertion is regression
  coverage. Desktop and 360px mobile screenshots were also visually reviewed.
- The outage test's assertion originally shared the client's 10-second deadline,
  causing a race when Docker DNS resolution stalled. The assertion now allows
  15 seconds for the client's timeout to render the error. The direct local
  browser command passes **20/20 cases**, including real API stop/restart/retry.
- A functional test exposed early storage-configuration resolution before the
  test host's configuration override. Resolution now occurs through DI using final
  configuration; invalid-path, distinct temporary database, and restart tests pass.
- Hosted CI has **not run**: `git remote -v` is empty. M0 remains pending that gate.
  No rooms, authentication, hubs, game rules, prompt management, or administration
  are claimed as implemented or tested. Next implementation milestone is M1.

- Branding refinement: replaced all three star marks with a shared local people
  SVG; capitalized the wordmark; removed full stops from headings, eyebrow labels,
  and decorative card titles. Recorded the preference in the product requirements.
  All 27 frontend unit/component tests and all 20 desktop/mobile browser cases
  pass. Type checking, production build, lint, and formatting pass. Reviewed
  desktop and 360px screenshots and rebuilt the running local frontend.

## Decision log

- Renamed the product to **Mingle** with **Team games** beneath the wordmark.
  Updated browser titles, accessible home-link text, product guidance, and branding
  regression tests. Final verification: 27 frontend unit/component tests and all
  20 desktop/mobile browser cases passed, including axe checks (32.9 seconds).
  Build, type checking, lint, and formatting passed. The local Docker frontend was
  rebuilt and became healthy. Hosted CI remains pending.

| Date | Decision | Rationale / revisit condition |
| --- | --- | --- |
| 2026-10-23 | React SPA + ASP.NET Core 10 + SignalR | Matches the required stack and interactive room model |
| 2026-10-23 | One API process with SQLite | Simple single-host durability; revisit after measured capacity constraints |
| 2026-10-23 | HTTP commands, versioned snapshots over SignalR | Retry and recovery remain independent of socket lifetimes |
| 2026-10-23 | This or That first | Exercises the entire room/game/prompt path with straightforward rules |
| 2026-10-23 | Guest cookies and configured admin credential | Account-free entry with distinct, server-enforced privileges |
| 2026-10-23 | Room-local facilitator prompts; admin global prompts | Allows authoring while controlling the shared library |
| 2026-10-23 | Unit and functional tests for every feature | User requirement; both layers ship with the feature and gate completion |
| 2026-10-23 | Interpret "stage 1" as M0, the first milestone | Establish a usable and tested foundation before room/identity work |
| 2026-10-23 | .NET 10 SDK feature-band roll-forward locally; SDK 10.0.402/runtime 10.0.13 pinned in Docker/CI | Ubuntu's installed SDK is 10.0.113; official Microsoft container SDK tags differ. Both application images build successfully |
| 2026-10-23 | Keep SQLite and Data Protection keys in one `app-data` volume | Preserve them together across recreation and future backups; one API process only |
| 2026-10-23 | Self-host fonts and use CSS/SVG artwork | Same-origin assets work with the restrictive CSP and need no third-party runtime requests |
| 2026-10-23 | Browser tests own temporary API state and stop the real API for retry coverage | Verify observable recovery without a mocked backend; Compose tests delete only their isolated project/volume |

## Open refinements

Confirm the CDN/hosting environment when preparing production configuration; use
documented proxy defaults locally. Tune retention/capacity from product feedback
and measured load. Choose final brand assets during UI work. The defaults in
[product.md](product.md) permit implementation to begin without these answers.
