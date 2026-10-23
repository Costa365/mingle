# Implementation plan

## Goal and current state

Deliver the application in [product.md](product.md) using the proposed
[architecture](architecture.md). This plan was initialized on 2026-10-23 from
[idea.md](../idea.md). Only documentation exists. Every implementation milestone
below is pending; no application checks have been run.

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

Use this record for each implemented feature:

| Feature / acceptance criteria | Unit tests | Functional tests | Execution evidence / remaining gaps |
| --- | --- | --- | --- |
| Add concrete feature entries during implementation | Test file and cases | Test file and cases | Commands, results, and CI run |

## M0 — Buildable foundation

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
- Workflow updated at the user's request: every feature now requires unit and
  functional tests in the same change, tracked acceptance coverage, and passing
  CI results before completion. No application tests exist or have run yet.
- Next action: M0, beginning with the .NET solution and React scaffold.

## Decision log

| Date | Decision | Rationale / revisit condition |
| --- | --- | --- |
| 2026-10-23 | React SPA + ASP.NET Core 10 + SignalR | Matches the required stack and interactive room model |
| 2026-10-23 | One API process with SQLite | Simple single-host durability; revisit after measured capacity constraints |
| 2026-10-23 | HTTP commands, versioned snapshots over SignalR | Retry and recovery remain independent of socket lifetimes |
| 2026-10-23 | This or That first | Exercises the entire room/game/prompt path with straightforward rules |
| 2026-10-23 | Guest cookies and configured admin credential | Account-free entry with distinct, server-enforced privileges |
| 2026-10-23 | Room-local facilitator prompts; admin global prompts | Allows authoring while controlling the shared library |
| 2026-10-23 | Unit and functional tests for every feature | User requirement; both layers ship with the feature and gate completion |

## Open refinements

Confirm the CDN/hosting environment when preparing production configuration; use
documented proxy defaults locally. Tune retention/capacity from product feedback
and measured load. Choose final brand assets during UI work. The defaults in
[product.md](product.md) permit implementation to begin without these answers.
