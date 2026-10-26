# Adding and testing game modules

A game owns its rules and private serialized state behind `IGameModule` in
`server/TeamGames.Core/Games`. The room, guest cookie, transport, SQLite writer,
receipts and reconnect code do not interpret a game's answers or stages.

## Server module

1. Give the module a stable catalogue ID. Implement `Start`, `Apply`, `Project`, `Describe` and `Finish`,
   and register it as an `IGameModule` in `RoomEndpoints.AddRooms`. The `GameModules`
   registry dispatches by the saved session ID/type. Never use runtime type names
   supplied by clients to deserialize state.
   `Describe` returns only `AdminGameView` session/type/stage/round metadata.
   `Finish` terminates without a participant actor or a reveal of unfinished input;
   administrative authorization and deduplication happen in RoomStore. Add both
   methods to module tests, including private collection and removed presenters.
2. Validate the actor's allowed action, frozen round roster, round ID, stage
   revision, own response revision and input. Stage actions require the current
   facilitator. The existing RoomStore authenticates membership, serializes writes,
   deduplicates by actor/command UUID and commits before acknowledgment/broadcast.
3. Keep rules deterministic. Prompt modules use injected `IGameRandom`; module
   clocks, if added, must likewise be injectable. Presenter order is frozen from
   collection roster order, persisted, and never reshuffled by a reconnect.
4. Build a `GameView` for each viewer. `Options` contains their available response
   values, and `Responses` exposes receipt flags without selections. `OwnChoice`
   and `OwnSubmission` belong only to that viewer; `Presenter` contains only the
   current public statements, never its lie. Only finalized non-skipped results
   may carry a `LieIndex`. Nomination totals never carry voter identities.
5. Preserve stored states and finalized totals across upgrades. The existing JSON
   envelope remains schema 3; This or That also reads its M2 legacy prompt indices
   and aggregate-only results. New prompt games reuse `PromptGame` with separate
   IDs/content and aggregate-only reveals. Two Truths uses a separate state/queue.
   All are persisted by the same existing SQLite room transaction; M4 adds no EF
   migration. A new incompatible state shape needs an explicit read migration and
   legacy/restart fixtures before deployment.
6. Extend explicit public DTOs if needed, regenerate with
   `dotnet run --project tools/TeamGames.Contracts`, and update runtime checks in
   `web/src/api/rooms.ts`. Public DTOs must never contain persistence entities.

## Client view

Register the ID in the `GamePanel` view registry and `gameNames.ts`. The current
shared view renders prompt choices/options, nomination totals and presenter
statements; `StatementForm` owns only the author's editable draft and captures its
base revision to detect cross-tab edits. Add a dedicated view when a game needs
another input shape rather than adding game rules to RoomPages or useRoom.

Preserve the Room/Game/Prompts workspace, heading focus on stage changes, compact
mobile choices, accessible response labels, secondary pass action and retry IDs.
Prompt authoring remains facilitator-only in its separate view. A live game
cannot be replaced by a library choice from another game; finish and return to
lobby first. All four prompt games support filters, random/manual draw and the
existing repeat/reset pool behavior. Two Truths uses player-written statements.

## Required coverage

- Core unit tests: rules, invalid input, role/eligibility, own revision conflicts,
  frozen rosters, missing/zero answers, removals, privacy projections, ties/skips
  and old serialized state. M4 examples are in `M4GameRulesTests`.
- Real HTTP/hub functional tests: independent cookie-authenticated participants,
  raw private/public payloads, rejected unauthorized commands, receipt replay,
  stale stage rejection and real SQLite restarts. `M4GameFunctionalTests` restarts
  at collection, guessing and reveal; shared prompt modules are restarted too.
- Component unit tests: chooser/controls and submission payloads, saved/disabled
  states, private draft validation and cross-tab conflict behavior.
- Real browser flows: play/finish the new game and switch games without rejoining;
  check keyboard, mobile overflow, reduced motion and axe. Add browser recovery
  coverage for new input stages. `m4.spec.ts` plays all five games in one room and
  exercises no-ready collection, opt-out, cross-tab conflict, stage restarts and
  insufficient-player nominations.

Run the verification commands in [development.md](development.md). Both automated
layers must pass in required hosted CI before protected main is updated. Test
mock projections are never substitutes for inspecting real HTTP/hub payloads.
