# Development and agent workflow

## Current workspace

This is a documentation-only project as of 2026-10-23. The environment exposes
.NET SDK 10.0.113, Node.js 22.22.1, npm 9.2.0, Docker 29.5.2, and Docker Compose
5.1.4. These are observed local versions, not final dependency pins. The Docker
daemon, package restores, and compatibility of the future scaffold have not been
verified. Pin supported compatible versions during M0.

`git status` currently reports that this workspace is not a Git repository. Its
`.git` path is a read-only environment placeholder. No Git metadata, commits, or
remote configuration were created; normal version control must be established in
a writable checkout before relying on CI or branch-based workflows.

## Planned commands

The following commands define the intended developer interface. They are **not
runnable yet** because the solution, package manifest, and Compose file do not
exist. Implement and verify these commands in M0, then replace this caveat with a
tested quickstart.

| Purpose | Planned command from repository root |
| --- | --- |
| Restore backend | `dotnet restore TeamGames.slnx` |
| Build backend | `dotnet build TeamGames.slnx --no-restore` |
| Backend unit and API functional/integration tests | `dotnet test TeamGames.slnx --no-build` |
| Backend format check | `dotnet format TeamGames.slnx --verify-no-changes --no-restore` |
| Install frontend | `npm --prefix web ci` |
| Run API | `dotnet watch --project server/TeamGames.Api` |
| Run frontend | `npm --prefix web run dev` |
| Type check | `npm --prefix web run typecheck` |
| Lint | `npm --prefix web run lint` |
| Frontend unit/component tests | `npm --prefix web run test -- --run` |
| Frontend build | `npm --prefix web run build` |
| Browser functional tests | `npm --prefix web run test:e2e` |
| Validate Compose | `docker compose config --quiet` |
| Run deployment | `docker compose up --build` |

The Vite development proxy should forward API and hub requests to ASP.NET so
development exercises same-origin cookies and WebSocket upgrades. Configure
Playwright to launch the application and use independent browser contexts for
participants; multiple tabs in one context intentionally share a guest identity.
Document browser installation and any integration-test services when configured.

## Testing requirement for every feature

Every new or changed feature must include automated **unit tests and functional
tests** in the same change. This covers every game and supporting feature: entry,
invites, identity, room controls, prompts, administration, UI states, reconnection,
and persistence. Small features follow the same rule. Do not postpone their tests
to the final verification milestone.

| Layer | What it establishes | Planned tooling |
| --- | --- | --- |
| Unit | Rules, validation, transitions, calculations, and component behavior in isolation | xUnit for C#; Vitest and Testing Library for TypeScript/React |
| API/hub functional | A feature's observable behavior through real application endpoints, authentication, serialization, and persistence | ASP.NET test hosting, HTTP/SignalR clients, and real temporary SQLite databases |
| Browser functional | A UI feature works through the running frontend and backend as a user experiences it | Playwright, with independent browser contexts for different participants |

Functional tests verify acceptance criteria across application boundaries. Use
API/hub tests for backend features and browser tests for every UI feature. A UI
component test with a mocked API is useful unit coverage, but cannot replace its
functional test. Do not mock the feature's own rules, authentication, transport,
or persistence in the functional path being verified.

For each feature:

1. Record its acceptance criteria and relevant success, invalid-input, permission,
   and edge cases in the implementation plan before or alongside implementation.
2. Add unit tests for its rules or component behavior and functional tests for its
   observable outcomes. Shared test helpers are encouraged, but each feature must
   be exercised explicitly; one representative game does not cover all five.
3. Run both layers, fix failures, and record test files/cases and results beside
   the feature's plan entry. Add regression tests with bug fixes and demonstrate
   the original failure before the fix where feasible.
4. Mark the feature complete only when its required unit and functional tests
   pass. Missing, skipped, quarantined, or blocked tests leave the feature
   incomplete; report the gap rather than treating it as a pass.

## Verification strategy and CI

- Keep unit tests deterministic. Inject time/randomness for stage transitions,
  eligibility, results, prompt selection, retries, and expiry.
- Use real temporary SQLite databases for functional tests involving storage,
  migrations, uniqueness, and recovery. An in-memory provider substitute does not
  establish SQLite behavior.
- Verify authorization and serialized payloads, including negative cases. Hiding
  a UI control does not establish access control or answer secrecy. Cover retries,
  races, disconnects, and restart recovery where the feature involves them.
- Use observable assertions and isolated test data. Await conditions/events in
  browser tests rather than arbitrary sleeps. Keep manual visual/accessibility
  review as additional evidence; it does not replace automated functional tests.
- CI must run backend and frontend unit suites, API/hub functional suites, and
  browser functional suites, alongside formatting/build and contract checks.
  Require passing results before merging or declaring a milestone complete.
  A small browser smoke suite alone does not satisfy the feature-testing rule.
- Keep full feature coverage when splitting CI jobs or sharding browser tests.
  Exercise proxy/recovery functional scenarios as part of the required suites;
  run capacity benchmarks and the full operator rehearsal before release or when
  affected behavior changes.
- Measure completeness against feature acceptance criteria and meaningful
  assertions, not an arbitrary number of tests. Avoid placeholder assertions or
  tests that simply reproduce the implementation.
- Documentation-only changes need link/content checks. They introduce no product
  feature and do not require creating an application test scaffold.

## Planning and documentation

Keep [AGENTS.md](../AGENTS.md) short and repository-specific. It provides durable
constraints and points to focused documents; task progress belongs in the
[implementation plan](implementation-plan.md). Add nested guidance only when a
subtree needs materially different instructions. This follows the documented
instruction discovery model and OpenAI's advice to prioritize consequential,
scoped invariants over repetitive rules.
[AGENTS.md guidance](https://learn.chatgpt.com/docs/agent-configuration/agents-md),
[repository review guidance](https://developers.openai.com/blog/custom-code-review-rules-for-codex)

For each substantial change, record the outcome, important decisions, checks run,
and limitations. Keep validation reproducible through scripts and CI. Do not add
new approval rituals or a large agent framework for ordinary implementation work.
User instructions define the authorized scope.

Document setup as it becomes executable. During implementation add an operator
guide with environment variables, CDN/TLS routing, key/database persistence,
upgrade/migration/rollback steps, backup/restore, and health troubleshooting. Add a
game-module guide after the second module proves the extension boundary. Keep
planned paths and commands clearly distinct from files that already exist.
