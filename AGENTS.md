# Mingle — repository guidance

## Purpose and scope

Build an account-free remote team games application from [idea.md](idea.md).
The required stack is React and ASP.NET Core on .NET 10, deployed on one host
with Docker Compose. Support all five requested games and future game modules.

The foundation and M1 room lifecycle are implemented: catalogue, create/join/lobby,
guest cookies, facilitator controls, authorized SignalR updates, durable receipts,
expiry, and recovery. M2 adds a playable This or That game with private answers,
facilitator pacing, named choices after reveal, finalized results, and durable
game recovery. M3 adds 350 built-in prompts, category/tag/search filters,
manual/random selection, explicit pool reset, facilitator room-local authoring,
and repeatable seed updates that preserve edits. The other four games, admin UI,
and deployment hardening remain planned (M4–M6).
Consult the implementation plan for verification status; do not describe unexecuted checks or
future features as working.

## Where to look

- [README.md](README.md): project status and documentation entry point.
- [docs/product.md](docs/product.md): requirements, game rules, and defaults.
- [docs/architecture.md](docs/architecture.md): boundaries, protocol, persistence,
  authorization, and deployment decisions.
- [docs/implementation-plan.md](docs/implementation-plan.md): ordered milestones,
  acceptance checks, progress, and decision log.
- [docs/development.md](docs/development.md): tooling, verification, and workflow.

Read the documents relevant to the task. Preserve `idea.md` as the original brief;
record refinements in the product document. User instructions override this plan.

## Implementation invariants

- The server owns game state, stage transitions, eligibility, and results.
  Client-side controls are not authorization checks.
- A participant's identity and facilitator role survive connection changes.
  Never use a SignalR connection ID, display name, or invite code as proof of role.
- Facilitators can advance with missing answers. Disconnects cannot block a room.
- Treat every game command as retryable: authorize it, check its round/stage,
  deduplicate it, and persist the result before acknowledging or broadcasting.
- Build public and participant-specific views on the server. Never send hidden
  answers, the lie index, other players' unrevealed choices, or credentials to an
  unauthorized client, including facilitator and administrator clients.
- Keep game rules in game modules. Transport, room lifecycle, reconnect handling,
  and persistence must not accumulate game-specific conditional branches.
- Use same-origin cookie authentication, request forgery protection, and explicit
  hub origin checks. Administrator credentials belong in deployment configuration.
- Keep the first deployment to one API process with durable SQLite storage.
  Multiple replicas require a separate persistence and coordination design.

## How to work

- Implement a small, usable slice with its unit tests, functional tests, and
  documentation in the same change. For multi-step work, update the implementation
  plan with progress and decisions.
- Use TypeScript strict mode, C# nullable references, and explicit transport DTOs.
  Keep game rules deterministic by injecting time and randomness where needed.
- Every new or changed feature requires both automated unit tests and automated
  functional tests. This applies to all features, including UI, room management,
  every game, prompts, administration, and recovery; testing is not deferred to
  a later milestone. Add regression coverage with bug fixes.
- Unit tests verify isolated rules and component behavior. Functional tests
  exercise observable behavior through the real API/hub and persistence, plus
  browser flows for UI features. Mock-only tests do not establish functional
  coverage. Cover success, relevant invalid input, permissions, and edge cases.
- Record each feature's acceptance criteria and the unit/functional tests that
  cover them in the implementation plan. A feature is complete only when both
  layers pass; missing, skipped, or blocked required tests leave it incomplete.
- Require both test layers in CI. Use the commands in the development guide once
  the scaffold exists; update that guide when commands change. Avoid tests that
  merely restate implementation or satisfy a test-count target.
- Review gameplay on a narrow mobile viewport and with keyboard navigation.
  Respect reduced motion and do not rely on color alone.
- Keep secrets, room credentials, submitted answers, and database files out of
  source control and logs. Keep changes focused and preserve unrelated user work.
- Report what changed, what was verified, and any remaining limitations. Mark a
  milestone complete only when its acceptance checks have evidence.

## Review priorities

Check for cross-room access, leaked hidden state, duplicate stage transitions,
stale submissions accepted after reveal, identity loss on reconnect, and data loss
after restart. These are release-blocking defects.
