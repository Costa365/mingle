# Development and agent workflow

## Tooling and setup

Use a .NET 10 SDK and Node.js 22.22.1 (`.nvmrc`). `global.json` allows installed
.NET 10 feature bands; Docker and CI pin the Microsoft SDK to 10.0.402 and the
runtime/EF packages to 10.0.13. The local Ubuntu SDK is 10.0.113. npm and NuGet
lockfiles pin dependencies; restore in locked mode in verification and containers.
Docker Engine with the Compose plugin and Python 3 are required for deployment
checks. Git is initialized; changes to `.git` may need sandbox approval in Codex.

From the repository root:

```bash
dotnet restore TeamGames.slnx --locked-mode
dotnet tool restore
npm --prefix web ci
```

For development, use two terminals:

```bash
dotnet watch --project server/TeamGames.Api
```

```bash
npm --prefix web run dev
```

Open http://localhost:5173. Vite forwards `/api` and `/hubs` to the API on port
5100. `/hubs/rooms` carries authorized lobby snapshots. Development OpenAPI is
available at http://localhost:5100/openapi/v1.json. The Development configuration
explicitly allows local HTTP cookies; production defaults require Secure cookies.
Local data defaults to `server/TeamGames.Api/.data/`, which is ignored by Git.
Set `Storage__DataDirectory` to a different directory when needed.

## Commands

| Purpose | Command from repository root |
| --- | --- |
| Restore backend | `dotnet restore TeamGames.slnx --locked-mode` |
| Build backend | `dotnet build TeamGames.slnx --no-restore --disable-build-servers -m:1` |
| Backend unit and API functional tests | `dotnet test TeamGames.slnx --no-build --disable-build-servers -m:1` |
| Backend format check | `dotnet format TeamGames.slnx --verify-no-changes --no-restore` |
| Generate room transport types | `dotnet run --project tools/TeamGames.Contracts` |
| Check generated types after building | `dotnet run --project tools/TeamGames.Contracts --no-build -- --check` |
| Install frontend | `npm --prefix web ci` |
| Type check | `npm --prefix web run typecheck` |
| Lint | `npm --prefix web run lint` |
| Frontend format check | `npm --prefix web run format:check` |
| Frontend unit/component tests | `npm --prefix web run test -- --run` |
| Frontend production build | `npm --prefix web run build` |
| Browser functional tests | `npm --prefix web run test:e2e` |
| Validate Compose | `docker compose config --quiet` |
| Run deployment | `docker compose up --build --detach --wait` |
| Build/lint/unit/API verification | `bash scripts/verify.sh` |
| Container/browser verification | `bash scripts/verify-compose.sh` |

Before the first browser run, install Chromium from `web/`:

```bash
npx playwright install --with-deps chromium
```

Build the backend in Debug and the frontend before running `test:e2e`. Playwright
starts the built API on port 5101 and serves the frontend build through Vite
preview on port 4173. It owns an isolated temporary SQLite directory, stops the
real API to test failure/retry, and tears down its process and data afterward.
Do not run another server on those ports during this suite. Desktop (1440px) and
mobile (360px) projects both run all flows and accessibility checks.

`verify-compose.sh` builds an isolated Compose project on port 18080 (override
with `TEAMGAMES_TEST_PORT`), validates routing/cache/security headers, recreates the
API to check database and key persistence, and runs the browser suite through
Nginx. Its cleanup deletes only that temporary project's containers and volume.
It requires installed browser dependencies; it does not install them itself.

For a constrained agent environment, local test runners and browser servers may
need permission to bind sockets. `DOTNET_CLI_HOME`, `npm_config_cache`, and
`PLAYWRIGHT_BROWSERS_PATH` can point to writable temporary directories. These are
execution-environment adjustments, not application requirements. Do not bypass
sandbox restrictions; request the permitted escalation when necessary.

## Migrations and contracts

Create migrations with the pinned repository tool:

```bash
dotnet ef migrations add ChangeName --project server/TeamGames.Infrastructure --startup-project server/TeamGames.Api --output-dir Persistence/Migrations
```

Startup applies migrations before listening and creates the installation marker
once. Readiness checks query that marker; liveness is independent of storage.
Tests use real temporary SQLite files and exercise a complete host restart.

The `/api/games` contract remains represented by `GameSummary` in C# and TypeScript.
Room request/response and hub snapshot interfaces are generated from the public
Core DTOs into `web/src/api/room-contract.generated.ts`. Regenerate after changing
those DTOs; `verify.sh` rejects stale output. The client validates snapshots at
runtime too. `Snapshot` carries protocol version 1; `AccessEnded` carries only an
access error code. Real HTTP/hub tests and browser flows verify the wire behavior.

Room commands use a caller-generated UUID and expected room version. An ambiguous
network failure must reuse that UUID and payload. Entry forms retain their pending
UUID in session storage across reloads; a failed lobby change offers a dedicated
retry. A version conflict refreshes the room and requires a new deliberate action.
Receipts store the applied version; retries return it alongside a fresh authorized
snapshot. The client never replaces a newer snapshot with an older one.

Transient presence has a separate revision and process epoch. A successful
resubscription plus HTTP resync accepts a new epoch after restart; ordinary old
events do not. Five seconds of disconnect grace avoids flickering duplicate-tab
presence. Reconciliation runs roughly every 15 seconds. Initial connection and
automatic reconnect retry with bounded backoff; manual retry remains available.

## CI

The [GitHub repository](https://github.com/Costa365/mingle) runs
`.github/workflows/ci.yml` on pushes, pull requests, and manual dispatch. Its
**Unit and functional tests** job runs both verification scripts: verifier unit
tests, backend unit/API/hub tests, frontend unit tests, and desktop/mobile browser
flows through real Docker/Nginx. Any failed layer fails the job. Bash `pipefail`
preserves failures while saving console output with `tee`.

The workflow pins official Node 24 Actions by immutable commit, installs the
repository's .NET and Node versions, and caches package downloads. The
`verification-results` artifact contains build/test logs and browser reports or
failure traces. Failed container runs also save sanitized Compose status/logs;
databases and key rings are never uploaded. Diagnostic or teardown failures cannot
mask the original test failure. Cleanup failure also fails an otherwise green run.

Use **Unit and functional tests** as the required check for `main`. Hosted CI
results and milestone closure evidence are recorded in the implementation plan.
The verifier uses `compose up --wait --no-recreate` to resume the API because the
hosted Compose version does not support `compose start --wait`.

## Testing requirement for every feature

Every new or changed feature must include automated **unit tests and functional
tests** in the same change. This covers every game and supporting feature: entry,
invites, identity, room controls, prompts, administration, UI states, reconnection,
and persistence. Small features follow the same rule. Do not postpone their tests
to the final verification milestone.

| Layer | What it establishes | Tooling |
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

Keep the setup commands and [operator guide](operations.md) current as features
land. Add a game-module guide after the second module proves the extension
boundary. Keep planned capabilities clearly distinct from implemented behavior.
