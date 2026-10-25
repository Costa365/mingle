# Implementation plan

## Goal and current state

Deliver the application in [product.md](product.md) using the proposed
[architecture](architecture.md). This plan was initialized on 2026-10-23 from
[idea.md](../idea.md). **M0 — Foundation**, **M1 — Rooms and recovery**,
**M2 — This or That**, and **M3 — Prompt libraries and authoring** are implemented
and verified locally and in hosted CI. Teams can play This or That with named
reveals, browse 350 built-in prompts, author room-local content, filter/select/draw
prompts, and recover identity and saved rounds across API restarts. Only This or
That is playable; the other libraries prepare M4's games. M3 passed the
[required hosted check](https://github.com/Costa365/mingle/actions/runs/40178486428)
on `edb922f`. M4–M6 remain planned; M4 is next.

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
  If hosted CI is unavailable, record it as an explicit pending acceptance gate.
  Update implementation checkboxes and link the successful CI run when it passes.
- Missing, skipped, quarantined, or blocked required tests leave the feature
  incomplete. Manual review and a test of a similar feature are not substitutes.
- Bug fixes include regression coverage. Follow the detailed test definitions in
  [development.md](development.md).

M0 acceptance coverage (paths are relative to the repository root):

| Feature / acceptance criteria | Unit tests | Functional tests | Execution evidence / remaining gaps |
| --- | --- | --- | --- |
| People icon replaces all star marks; wordmark reads "Mingle" with the tagline "Team games"; headings contain no full stops | `web/src/app/App.test.tsx`: brand name/tagline, decorative icon semantics, punctuation on home/how-to/not-found/detail and error headings | `web/e2e/foundation.spec.ts`: loaded icons, exact brand/tagline and Mingle browser titles, punctuation across routes and error state, both viewports, axe and keyboard checks | 27 frontend unit tests and 20 browser cases pass locally; build/lint/format pass; screenshots reviewed; [hosted CI passed](https://github.com/Costa365/mingle/actions/runs/40099054327) |
| All five catalogue entries have valid metadata; exact IDs resolve and unknown IDs fail | `tests/TeamGames.Core.Tests/GameCatalogTests.cs`: metadata, uniqueness, exact/unknown IDs; `web/src/api/games.test.ts`: malformed/duplicate payloads and request failures | `tests/TeamGames.Api.Tests/FoundationFunctionalTests.cs`: catalogue and every detail through HTTP; `web/e2e/foundation.spec.ts`: all five detail flows | Local .NET and frontend suites pass; [hosted CI passed](https://github.com/Costa365/mingle/actions/runs/40099054327) |
| Home, detail, how-to, and not-found routes render and allow navigation; gameplay availability is clear | `web/src/app/App.test.tsx`: each detail route, navigation, instructions, recovery links, room hosting and future-gameplay explanation | `web/e2e/foundation.spec.ts`: each route through the real backend, direct loads and reloads, desktop/mobile | Browser results recorded below; [hosted CI passed](https://github.com/Costa365/mingle/actions/runs/40099054327) |
| Loading is announced; an API failure offers retry and recovers | `web/src/app/App.test.tsx`: pending request, cancellation, error/retry on home and detail; `web/src/api/games.test.ts`: rejected/invalid responses | `web/e2e/foundation.spec.ts`: delayed real request and real API stop/restart/retry in both viewports | Browser results recorded below; [hosted CI passed](https://github.com/Costa365/mingle/actions/runs/40099054327) |
| Storage paths are validated; fresh SQLite is migrated; readiness reflects storage while liveness remains independent | `tests/TeamGames.Core.Tests/StoragePathsTests.cs`: relative/absolute/invalid paths; `tests/TeamGames.Api.Tests/StorageHealthCheckTests.cs`: success, failure, sanitized exceptions, cancellation | `tests/TeamGames.Api.Tests/FoundationFunctionalTests.cs`: migration, dropped storage table, invalid/unwritable configuration | Local .NET suites pass; [hosted CI passed](https://github.com/Costa365/mingle/actions/runs/40099054327) |
| Database identity and Data Protection keys survive host/container recreation | Storage path and readiness unit cases above cover persistence configuration and failure reporting | `FoundationFunctionalTests.cs`: fresh host restores installation and decrypts a protected value; `scripts/verify-compose.sh`: recreates API and compares database, migrations, WAL mode, and key ring | Container persistence checks pass; [hosted CI passed](https://github.com/Costa365/mingle/actions/runs/40099054327) |
| Same-origin deployment routes API errors correctly, supports SPA deep links, and applies cache/security headers | Catalogue request/route and health unit cases above | `scripts/check-deployment.py`: HTTP assertions through Nginx; `foundation.spec.ts`: desktop/mobile navigation, keyboard skip link, overflow and axe checks; `FoundationFunctionalTests.cs`: development-only OpenAPI | Deployment checks pass; browser results below; [hosted CI passed](https://github.com/Costa365/mingle/actions/runs/40099054327) |

## M0 — Buildable foundation

**Status: Complete.** Implementation and acceptance checks pass locally and in
[hosted CI](https://github.com/Costa365/mingle/actions/runs/40099054327). The workflow
runs both verification scripts and retains build, deployment, and browser results.

- [x] Create the .NET 10 solution, API/Core/Infrastructure projects, and test
  projects; enable nullable references and consistent formatting.
- [x] Scaffold React/TypeScript/Vite with routing, strict types, linting, component
  testing, and a Playwright setup.
- [x] Pin compatible SDK/runtime/package versions and commit lockfiles. Add
  `.gitignore`, `.editorconfig`, `.env.example`, and documented configuration.
- [x] Add frontend/API Docker builds, Nginx routing, Compose volumes and health
  checks. Add EF Core SQLite, the first migration, and key persistence.
- [x] Establish an initial app shell, error/loading components, and design tokens
  usable at 360px and desktop widths.
- [x] Add meaningful unit and functional coverage for initial app-shell behavior,
  configuration validation, readiness, routing, and persistence initialization.
- [x] Add required CI jobs for unit, API/hub functional, and browser functional
  suites; document verified local commands and test evidence.

**Acceptance:** A fresh checkout can install dependencies, build both applications,
run initial checks, and start via `docker compose up --build`. The browser reaches
the app and a readiness endpoint through the same origin. Storage survives API
recreation. No placeholder test is counted as evidence of gameplay.

## M1 — Rooms, identity, and recovery foundation

Depends on M0.

**Status: Complete.** Implementation and acceptance checks pass locally and in
[hosted CI](https://github.com/Costa365/mingle/actions/runs/40099054327).
`bash scripts/verify.sh` passes **4 verifier unit tests, 47 backend unit tests, 16 API/hub functional tests,
and 65 frontend unit/component tests** plus build, type checking, lint, formatting,
generated-contract drift checking, and Compose validation. The final isolated
`bash scripts/verify-compose.sh` passes deployment/persistence assertions and all
**30 browser cases** through Nginx on desktop and 360px mobile (1.0 minute for the hosted
browser suite). No required tests were skipped.

| Feature / acceptance criteria | Unit coverage | Functional coverage | Evidence / remaining gate |
| --- | --- | --- | --- |
| Protected guest identity; validated names/codes; case-insensitive uniqueness, capacity, and entry limits | `RoomRulesTests.cs`: names, Unicode normalization, codes, capacity, lock/rejoin/removal; `RoomBoundaryTests.cs`: same-origin and upgrade checks; `RoomPages.test.tsx`: both entry forms, server errors and safe retry | `RoomFunctionalTests.cs`: real cookies/antiforgery for every mutation, forged/expired sessions, HTTPS Secure cookies, input/capacity/rate limits, duplicate create/join; `rooms.spec.ts`: create, invite join, duplicate-name error, reload and duplicate tab | Both layers pass locally; [hosted CI passed](https://github.com/Costa365/mingle/actions/runs/40099054327) |
| Invite link/code copying, roster, facilitator controls, closure confirmation, and room isolation | `RoomRulesTests.cs`: every control's permission/target/version rules; `RoomPages.test.tsx`: lock/unlock/remove/transfer, copy/failure, confirmation/cancel, role-specific controls and terminal views | `RoomFunctionalTests.cs`: unauthorized controls/read, stale versions, ownership changes, removed access, closed room behavior; `rooms.spec.ts`: both clipboard actions, lock/unlock, removal, transfer, closed-room fanout, cross-room access, keyboard Escape/focus restoration | Both layers pass; desktop/mobile screenshots reviewed and axe checks pass; [hosted CI passed](https://github.com/Costa365/mingle/actions/runs/40099054327) |
| Authorized live subscription and unique-person presence across tabs | `RoomBoundaryTests.cs`: tab counts, stale disconnect and timed grace; `rooms.test.ts`: snapshot validation and ordering | `RoomFunctionalTests.cs`: real SignalR Long Polling, rejected outsider subscription, live roster, tab count, removal revocation and rejected closed subscription; browser tests verify independent live participants and duplicate tabs | Both layers pass locally; [hosted CI passed](https://github.com/Costa365/mingle/actions/runs/40099054327) |
| Transactions persist before notification; UUID retries deduplicate and reject payload reuse | `RoomRulesTests.cs`: monotonic versions/conflicts; `RoomPages.test.tsx` and `rooms.test.ts`: stable retry UUIDs and antiforgery refresh | `RoomFunctionalTests.cs`: concurrent duplicate commands, payload mismatch, role/membership rechecks and retained receipts after restart; `rooms.spec.ts`: lost create acknowledgment, reload, retry; lost control acknowledgment changes version once | Both layers pass locally; [hosted CI passed](https://github.com/Costa365/mingle/actions/runs/40099054327) |
| Initial retry, reconnect/resubscribe/resync, manual recovery, stale-view rejection, and periodic reconciliation | `useRoom.test.tsx`: handler registration, initial retry, reconnect, out-of-order state, periodic polling, disposal, expiry and terminal closure; `rooms.test.ts`: independent presence epoch/version ordering; `RoomPages.test.tsx`: recovery controls | `RoomFunctionalTests.cs`: complete host restart preserves cookies, ownership and receipts; `rooms.spec.ts`: real API outage disables controls, restart reconnects both browsers with unchanged membership/ownership/version | Both layers pass locally and through Compose; [hosted CI passed](https://github.com/Costa365/mingle/actions/runs/40099054327) |
| Expiry and retained-data cleanup revoke live access and delete dependent data; migration preserves existing installation | `RoomRulesTests.cs`: exact inactivity boundary and closure; `RoomBoundaryTests.cs`: timed presence grace; hook/lobby tests: access-ended view | `RoomFunctionalTests.cs`: fake-time HTTP/hub expiry, guest expiry, cascade removal of rooms/memberships/receipts, upgrade from M0 SQLite schema; browser removal exercises the access-ended UI | Both layers pass locally; [hosted CI passed](https://github.com/Costa365/mingle/actions/runs/40099054327) |
| Explicit HTTP/hub DTOs match client types; credentials remain private | `rooms.test.ts`: valid/malformed protocol and snapshots; generated DTO drift check in `verify.sh` | Real API/hub serialization asserts no guest IDs, verifiers or receipts in views; all browser flows consume actual generated/validated snapshots through the API | Both layers and contract check pass; [hosted CI passed](https://github.com/Costa365/mingle/actions/runs/40099054327) |
| Invitation codes, referrers, and transport tokens stay out of proxy diagnostics | `logs.test.ts`: redaction of invite, hub, and referrer queries in development diagnostics | `check-deployment.py` sends query/referrer canaries; `verify-compose.sh` scans actual Nginx logs after all browser/outage flows for canaries and hub-token URLs | Both layers and the real proxy-log scan pass; [hosted CI passed](https://github.com/Costa365/mingle/actions/runs/40099054327) |

- [x] Implement guest credentials, antiforgery handling, memberships, facilitator
  assignment, and room creation/joining with input limits and rate limits.
- [x] Build create/join/lobby screens, invite link/code copying, participant list,
  join locking, player removal, ownership transfer, and room closure.
- [x] Add authorized hub subscriptions, per-viewer snapshots, monotonically
  increasing versions, presence, and duplicate-tab handling.
- [x] Add command receipts and transactional room mutations from the start.
- [x] Implement initial connection retry, reconnect/resubscribe/snapshot recovery,
  stale-version rejection, periodic reconciliation, and recovery UI.
- [x] Implement room expiry and retained-data cleanup.
- [x] Add unit and functional tests for every room/identity/control/recovery
  feature above, including independent browser flows for each UI action.

**Acceptance:** Independent browsers join the same room and see live roster changes.
Reload and connection loss preserve membership and ownership. Duplicate tabs count
as one player. Another room cannot read snapshots or subscribe. A removed player
loses HTTP and hub access. Restart preserves the room; fake-time expiry deletes
its retained data and stops access.

## M2 — First complete game: This or That

Depends on M1. This is the first playable vertical slice.

- [x] Define the game module/registry contract and the shared round command model.
- [x] Implement responding, pass/change answer, reveal, skip, next round, finish,
  and return-to-lobby behavior with a small reviewed prompt seed.
- [x] Add separate stage/response revisions, deduplication, frozen round rosters,
  per-viewer projections, and finalized results.
- [x] Build facilitator controls and player answer/reveal screens with visible
  response counts and clear unavailable actions.
- [x] Add unit and functional tests for every game action and UI state, including
  rules, authorization, concurrent commands, hidden state, and browser flows.

**Acceptance:** One facilitator and at least two players finish multiple rounds.
The facilitator advances with absent or disconnected players, including zero
answers. Double-clicks/retries cannot advance twice. Simultaneous valid player
answers succeed. A stale answer after reveal fails clearly. Choices stay private
until reveal, when each player’s choice is shown with their name. API restart restores the exact last acknowledged stage and answers.

### M2 implementation and verification — 2026-10-24

This or That is implemented with 12 reviewed seed pairs and a server module
registry. All five M2 items are complete, including the requested named choices
after reveal. Both test layers passed locally and in the
[hosted verification run](https://github.com/Costa365/mingle/actions/runs/40116725312)
for `4bd98cc`.

Verification evidence:

- `scripts/verify.sh` passed locked restores, backend/frontend builds, generated
  DTO checks, formatting, lint, four verifier unit tests, 67 backend unit tests,
  22 real API/hub functional tests, and 82 frontend unit/component tests.
- All 36 desktop/mobile browser tests passed through Nginx in `scripts/verify-compose.sh`. Container verification passed routing,
  health, cache/security headers, sanitized logs, WAL mode, three migrations,
  installation identity, and key-ring persistence after API recreation.
- The real local preview was upgraded from M1 after a stopped-volume backup.
  Before/after comparisons preserved installation, rooms, memberships, guest
  identities, command receipts, and keys; SQLite integrity passed. Both
  containers are healthy on port 8080.
- Browser gameplay, named reveal, and summary screenshots were inspected at
  360px and desktop size; axe,
  keyboard reveal, reduced-motion operation, and no horizontal overflow passed.
  CI retains its reports/screenshots in the `verification-results` artifact.
- A final retention assertion also verifies that starting another session keeps
  the previous session in persisted state; its focused unit test passed.

| Feature and acceptance | Unit coverage | Functional coverage |
| --- | --- | --- |
| Start a game; freeze the round roster; late joins wait for the next round | `GameRulesTests`, `GamePanel.test.tsx` | `GameFunctionalTests`, `game.spec.ts` late arrival |
| Choose A/B/pass and change before reveal; concurrent people succeed and duplicate tabs conflict | `GameRulesTests` response and revision cases; `GamePanel.test.tsx` choice cases | Three-player API/hub and browser flows |
| Reveal, skip, next, finish and return; allow zero, missing and disconnected answers | `GameRulesTests` stage/zero-response/exhaustion cases; game component controls/results | API multi-round flow and desktop/mobile gameplay flow |
| Keep others' choices private; removal before reveal excludes them; finalized aggregates remain unchanged | `GameRulesTests` projection and removal cases | Per-viewer real hub snapshots, cross-room access, removal before/after reveal |
| Authorize every action; reject stale stage/session/round and invalid input; deduplicate after commit | `GameRulesTests` invalid and permission cases; UI stale/ambiguous error cases | Cookie/CSRF/origin tests, duplicate/concurrent command tests, browser lost acknowledgement |
| Preserve state, answers, results and receipts across process restart; expire playing rooms | `GameRulesTests` lifecycle case; existing storage and room tests | API restart through SQLite, real browser API stop/start, expiry and purge |
| Usable mobile/keyboard UI, reduced motion and accessible controls | Game/room component tests, transport validation tests | Desktop and 360px browser flows with axe, Enter-key reveal, overflow checks and screenshots |

Named reveal refinement coverage: `GameRulesTests` checks A/B/pass/missing
entries and privacy/removal behavior; `GameFunctionalTests` checks actual HTTP/hub
reveal payloads, restart, neutral removed labels and version 1 aggregate-only state;
`GamePanel.test.tsx` and `game.spec.ts` check named results and pre-reveal privacy.
The JSON envelope advances to version 2 on the next mutation, while version 1
results remain readable with empty choice lists. Local and hosted verification passed 89 backend and 82 frontend tests plus
four verifier unit tests and all 36 Docker/browser tests. The version 1
compatibility and named-result privacy tests passed in both environments.

M2 decisions:

- Gameplay uses the same authenticated, CSRF-protected HTTP command path and
  post-commit per-viewer hub publisher as room controls. Separate stage and own
  response revisions avoid rejecting another player's concurrent answer.
- A versioned JSON document in `Rooms.GameState` stores sessions, round prompt
  text, frozen rosters, private submissions, and finalized aggregate results.
  Starting another game preserves previous sessions in that document. The third
  EF migration adds a nullable column so existing M1 rooms remain usable.
- The public room protocol remains version 1 with an additive optional `game`
  view. Browser module registration selects the renderer; storage, hub, and
  reconnect handling do not inspect game IDs. Future module schema changes must
  migrate stored state explicitly before changing seed identifiers or shape.
- Skipping or finishing an open round records it as skipped without revealing
  submitted choices. Percentages exclude passes and missing answers; zero votes
  produces zero percentages. Revealed results now include each eligible player’s
  name and choice to support conversation. A removed player’s finalized entry uses a neutral label.
- The seed deck does not repeat within a session. After its 12 rounds, finishing
  remains available; prompt browsing, reset, custom content, and launch content
  remain M3. No leaderboard is added.
- Browser accessibility checks found a roster transfer button label mismatch;
  visible and accessible labels now match, with component and browser coverage.

## M3 — Prompt browsing, authoring, and launch content

Depends on M2.

- [x] Add typed prompt storage, repeatable seed updates, category/tag filtering,
  search, manual selection, and random drawing without repetition.
- [x] Let facilitators create/edit/delete room prompts with validation. Snapshot
  selected prompt text so later edits do not alter rounds.
- [x] Supply at least 200 reviewed This or That pairs across eight categories and
  50 prompts for each of the other three library-driven games.
- [x] Add schema, duplicate, option, count, and seed-preservation checks. Record
  completion of the manual editorial review.
- [x] Add unit and functional tests for each prompt browsing, selection, authoring,
  validation, and seed-update feature; test the corresponding browser flows.

**Acceptance:** A facilitator can choose a prompt, draw a random prompt, and play a
newly authored prompt. Refresh/restart preserves it. Pool exhaustion offers a reset.
Room prompts never leak into another room; seed reruns preserve edited content.

### M3 implementation and verification — 2026-10-25

All five M3 items are complete. The
[hosted verification run](https://github.com/Costa365/mingle/actions/runs/40178486428)
passed for `edb922f`, including the original M0–M2 regressions.

Verification evidence:

- `scripts/verify.sh` passed locked restore, backend/frontend builds, generated
  contracts, formatting and lint, four verifier tests, 80 backend unit tests,
  27 real API/hub/storage functional tests, and 93 frontend unit/component tests.
- `scripts/verify-compose.sh` passed all 46 desktop/mobile browser tests, routing,
  health, proxy privacy/cache/security checks, WAL mode, all four EF migrations,
  and installation/key-ring persistence after API recreation. Hosted CI repeated
  the same checks successfully and retains reports/screenshots in its artifact.
- New browser flows cover search/category/tag filters, manual/random draws,
  custom authoring, validation, edit/delete snapshots, explicit exhaustion reset,
  reload and real API restart, lost acknowledgements, concurrent-tab conflicts,
  and confirmed saves followed by a failed library reload. Keyboard selection,
  reduced motion, axe checks and a 360px viewport passed. The mobile authoring
  screen was visually inspected, including an emptied category after deletion.
- A stopped-volume backup preceded the real local M2-to-M3 upgrade. Comparisons
  preserved installation, guest identities, acknowledged game state and key ring;
  350 built-ins were seeded and SQLite integrity passed. Both preview containers
  are healthy on port 8080.

| Acceptance | Unit coverage | Functional coverage |
| --- | --- | --- |
| 350 original prompts, typed shapes, stable IDs, eight This or That categories | `PromptRulesTests` launch/validation cases | Real startup and library API count/filter tests |
| Category/tag/search browsing; manual and random draws avoid repeats | `PromptRulesTests` matching and selection; prompt panel component tests | `PromptFunctionalTests`, `prompts.spec.ts` filter/manual/random flows |
| Facilitator room-local create/edit/disable/delete; validate duplicates, lengths and shapes | Domain validation/normalization and component form/error/confirmation cases | Actual API permissions, CSRF/origin, duplicate and revision tests; browser authoring |
| Round text is immutable after edits or deletion; restart restores custom content and usage | Module snapshot/reset and M2 usage migration tests | Real API restart with a custom round, edits/deletion, and browser restart |
| Exhaustion offers explicit reset or new filters | Module reset and prompt panel exhausted-state tests | Custom one-prompt pool reset through API and browser |
| Repeatable seeds preserve manual edits, disabled content, conflicts and room prompts | Seed fingerprint guard and normalized conflict tests | Seed rerun/update tests against real SQLite |

Editorial review of every entry is recorded in
[prompt-editorial-review.md](prompt-editorial-review.md). Counts and schema checks
support that review but do not substitute for reading the content.

Decisions:

- Prompts use stable IDs and a dedicated SQLite table, with a nullable owning room
  and scoped duplicate indexes. Only the facilitator can browse or change their
  room's library; built-ins are read-only in the room UI. Room deletion cascades
  its custom prompts, and custom content never becomes global automatically.
- Prompt commands share authorization, expected room version, actor-scoped UUID
  receipts, the single writer transaction, and post-commit per-viewer publication.
  Prompt revisions additionally protect edit/delete operations.
- Modules receive enabled global/room prompts as domain input. The room store,
  hub, and reconnect code do not select prompts by game-specific branches.
  Every round freezes its text/options before acknowledgement.
- This or That remembers stable used IDs per session. The M2 twelve-item index
  list migrates to its corresponding stable IDs. The game-state envelope accepts
  versions 1/2 and advances to version 3 on the next mutation. Exhaustion reset clears usage
  only for the selected eligible pool, while historical results and round
  numbering remain intact. Changing filters preserves other pools' usage.
- Seed fingerprints identify untouched rows. Updates preserve administrator edits
  and disabling; conflicting seed changes are skipped instead of overwriting
  content or breaking startup. No administrator UI is introduced before M5.
- The full libraries for the other three prompt-driven games can be browsed and
  prepared now. Their gameplay remains M4; Two Truths and a Lie is player-authored.

### Prompt-list usability refinement — 2026-10-25

The approved Superdesign draft uses one desktop row for the prompt, category/tags,
and action, with readable stacked mobile rows. A named, bordered scroll region,
visible column labels, stable scrollbar gutter, native scrolling, keyboard focus
and concise scroll help make the list boundary and available actions clear.
Selection, authorization and round pacing retain their existing behavior.

Acceptance criteria and coverage:

- Unit: `PromptLibraryPanel.test.tsx` verifies the named list, keyboard focus,
  accessible scroll description, visible tags/status, and action availability.
- Functional: `prompts.spec.ts` verifies three desktop columns, compact row height
  and at least eight visible desktop rows; mobile stacking/no overflow; native
  Page Down/End and pointer scrolling; axe checks and actual API-backed selection
  and authoring regressions.
- Design: desktop/mobile draft rendering and keyboard scrolling were checked;
  the user approved the draft before implementation. The local design context
  and resume state are ignored under `.superdesign/`.

Local verification passed on 2026-10-25: 94 frontend unit/component tests,
all 48 Docker/Nginx desktop/mobile browser tests, TypeScript checking, lint,
formatting, production build, proxy/persistence checks and visual review. The
new scrolling/layout case also passed independently against Vite. The existing
required hosted CI check runs before this change can update protected main.

Hosted verification additionally exposed a pre-existing named-reveal test that
matched a player's name against the entire answer row. The regression now chooses
a prompt containing the word “one” and matches each exact identity label, so
answer text cannot be confused with the player named One.

### Room-panel placement refinement — 2026-10-25

The team roster and invitation panels precede gameplay and the prompt library in
both DOM and visual order. Existing desktop columns and mobile stacking remain;
room-action errors/retry controls stay with the room panels. Invitation sharing
remains available to participants as requested.

Coverage: `RoomPages.test.tsx` verifies ordering for facilitators and participants;
`rooms.spec.ts` checks desktop/mobile geometry, keyboard action order, and panel
placement after opening the long library and starting a game. Verification uses
the existing frontend unit and real Docker/API-backed browser suites.

Local verification passed: all 96 frontend unit/component tests and all 50
Docker/API-backed browser tests, plus type checking, lint, formatting, and the
production build. Browser checks include keyboard navigation, accessibility,
and narrow mobile layout. The required hosted CI check gates updating main.

### Focused game workspace refinement — 2026-10-25

Approved design: https://p.superdesign.dev/draft/3f088dfc-d0b5-4efd-8045-28dcb74df171
The user additionally requested shorter choice buttons and removal of redundant
pass reassurance. These refinements are implemented and locally verified.

- [x] Separate Room, Game and facilitator Prompts views; roster/invite remain at
  the top of Room, with persistent navigation and a return-to-game action.
- [x] New sessions/rounds/stages focus Game for every connected player; ordinary
  response, presence and same-stage reconnect updates preserve deliberate navigation.
- [x] Server-projected named Answered/Waiting status reveals participation only;
  passes count as Answered, removed members disappear, late arrivals wait for next round.
- [x] Two shorter choices dominate; pass is a secondary text action with at least
  a 44px touch target and saved feedback, without redundant reassurance.
- [x] Core and component unit coverage, HTTP/hub/persistence functional coverage,
  desktop/mobile browser flows, keyboard/reduced-motion/accessibility checks pass.

Coverage: GameRulesTests/GameFunctionalTests for private status projection,
roster changes and recovery; rooms.test.ts for transport validation; RoomPages and
GamePanel component tests for view/focus/response behavior; game/rooms/prompts browser
suites for actual navigation, remote start/round focus, permissions, shorter choices,
pass privacy, prompt authoring and recovery through the real API.


Local verification passed: 72 Core tests and 38 API unit/functional tests, 103
frontend unit/component tests, 4 deployment-verifier unit tests, and all 52
Docker/Nginx desktop/mobile browser tests. Generated contracts, backend/frontend
formatting, TypeScript checks, lint and production builds passed. Real API restart
and private HTTP/hub projections preserve response status without disclosing
choices. A final two-browser-case rerun checked the shorter choice geometry,
private pass status, headings, navigation and mobile accessibility after visual
polish; a fixed short prompt makes size assertions deterministic while long text
can grow naturally. The local preview was rebuilt after a stopped-API data/key
backup. Required hosted CI gates updating protected main.

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

- 2026-10-24: **M0 and M1 hosted CI gates passed** in
  [run 40099054327](https://github.com/Costa365/mingle/actions/runs/40099054327)
  for commit `bc5e96d`. All workflow steps succeeded: **4 verifier unit tests,
  47 backend unit tests, 16 API/hub functional tests, 65 frontend unit/component
  tests, and 30 browser cases** through Docker/Nginx. Build, formatting, lint,
  generated-contract checking, migration/key persistence, and proxy-log privacy
  checks passed. The `verification-results` artifact was uploaded successfully.
  Checked all 14 implemented M0/M1 checklist items; M2–M6 remain unchecked.

- 2026-10-24: Connected `https://github.com/Costa365/mingle`. The
  [first hosted run](https://github.com/Costa365/mingle/actions/runs/40097369421)
  passed build/unit/API checks and container persistence, then failed because the
  runner's Compose rejects `start --wait`. Replaced it with the compatible
  `up --detach --wait --no-recreate` operation. Added four verifier unit cases for
  older Compose behavior, retained failure diagnostics, original exit status, and
  teardown failure. Updated official Actions to pinned Node 24 releases, enabled
  manual dispatch, and retained verification logs/reports as artifacts. Hosted
  completion is confirmed by the successful run recorded above.

- 2026-10-24: Implemented M1 rooms, guest credentials, facilitator controls,
  authenticated SignalR subscriptions, transient presence, durable receipts,
  expiration/cleanup, generated client contracts, and recovery UI. Required local
  verification passes with 63 backend tests, 65 frontend tests, and 30 browser
  cases through Docker. The Compose test project and its volume were removed after
  success. M1's feature-by-feature acceptance record appears above.
- Reviewed desktop and 360px lobby screenshots; automated scans found no axe
  violations. Browser tests cover invite clipboard behavior and keyboard focus/
  Escape when canceling room closure. Guest cookies and memberships survive API
  restart; the new SQLite migration preserves the M0 installation marker.
- Retry regression coverage includes a real committed room change followed by a
  simulated gateway error: the original UUID remains available and a retry does
  not apply the change twice. Nginx request diagnostics exclude query strings and
  referrers; request error logging is suppressed because its fixed format can
  expose transport-token URLs. Development proxy diagnostics redact queries too.
- The existing local preview was upgraded after stopping the API and backing up
  its complete data directory outside the repository. Both containers are healthy.
- At the initial M1 checkpoint, no Git remote or hosted run existed; this gate is
  now closed by the successful run above. The supplied
  Compose profile remains local HTTP; public TLS/forwarded-proxy configuration and
  provider rehearsal remain deployment work for M6. Gameplay remains M2–M4.

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
- At the initial M0 checkpoint, no remote or hosted run existed and room features
  had not yet been implemented. Later M1 and CI entries above supersede that
  checkpoint; M2 is now the next milestone.

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
  rebuilt and became healthy. Hosted CI was pending at that branding checkpoint
  and is now verified by the later run recorded above.

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
| 2026-10-24 | One SQLite writer gate shared by room commands, guest creation and maintenance | Avoid cross-room SQLite write contention on the required single process; measure capacity before changing coordination |
| 2026-10-24 | Receipts unique per guest/command ID, with request fingerprint and applied version | Deduplicate creation before a room ID is known; return current authorized state alongside the original applied version |
| 2026-10-24 | Presence revision plus process epoch | A persisted room version can stay unchanged across restart while all connections change; authoritative resync accepts the new epoch |
| 2026-10-24 | Closure ends live subscriptions but preserves a terminal read for existing members until cleanup | Make reload and lost-ack recovery clear; prohibit new joins, new subscriptions and state changes |
| 2026-10-24 | Generate HTTP/hub TypeScript interfaces directly from public Core DTOs | Prevent drift without exporting persistence entities or credentials; keep runtime protocol validation and real transport tests |
| 2026-10-24 | Entry budget 90/minute per API peer IP; local insecure cookies require an explicit override | Accommodate a small team joining behind one proxy/network while retaining a bounded entry rate; public deployment needs TLS/proxy configuration |

## Open refinements

Confirm the CDN/hosting environment when preparing production configuration; use
documented proxy defaults locally. Tune retention/capacity from product feedback
and measured load. Choose final brand assets during UI work. The defaults in
[product.md](product.md) permit implementation to begin without these answers.
