# M4 implementation design

Status: implemented and locally verified on `feat/m4-games`. All required local
unit and functional layers pass; hosted CI gates updating protected main.

## Ordered slices

1. Extract the existing binary rules into a reusable prompt-game engine while
   keeping old This or That JSON readable. Register Would You Rather and Never
   Have I Ever with their own IDs, content and labels. The 2026-10-29 reveal
   refinement publishes named finalized choices for both, as in This or That,
   and retains them in the finished summary.
2. Add Most Likely To as a separate nomination module. Freeze the round roster,
   authorize targets, forbid self-voting, exclude removed voters/nominees, calculate
   ties deterministically and support zero valid votes and insufficient players.
3. Add Two Truths and a Lie with collection, guessing, reveal and finished stages.
   Save private submissions durably, freeze presenter order when collection closes,
   and project only the current presenter's statements during their guessing round.
   Incomplete/opted-out submissions never enter the presenter queue. No-ready
   collection can reopen or finish. Presenter cannot vote on their own statements.
4. Complete the shared game chooser, game-view registry, prompt selection for all
   four prompt games, stage-specific views and game availability messaging. Keep
   the approved Room/Game/Prompts shell, smaller choices and secondary pass action.
5. Document module registration, contracts, projections and both test layers;
   verify all five games in one room, then close M4 only with passing evidence.

## Boundaries and persistence

- RoomStore, SignalR subscriptions, guest identity and room authorization remain
  game-agnostic. Modules interpret their own serialized state and build projections.
- Continue using existing room transactions and actor/room command receipts. Every
  submission/vote carries stage and response revisions; stage changes remain
  facilitator-only and reject stale commands. Never acknowledge before commit.
- Add explicit transport records for nomination totals, statement submission,
  presenter statements and revealed guesses as needed. Generate TypeScript types
  and extend runtime validation. Never export serialized module state.
- Before reveal, only the author receives their own answer/submission/lie index.
  Other viewers, including facilitator, receive participation flags rather than
  private values. Most Likely To never exposes voter-to-nominee associations,
  including on reveal. Guess results show aggregate counts rather than voters.
- Keep current active/revealed/finalized M2/M3 games readable across deployment.
  Existing SQLite JSON storage can hold new modules without new table ownership;
  a schema change must have migration and restart evidence if implementation
  shows one is needed. Do not declare migration compatibility without tests.

## Edge-case decisions to enforce

- Disconnects keep accepted responses and eligibility. Facilitators can advance
  with missing answers; they never wait for a socket or an online participant.
- Newly joined players cannot answer a round already in progress. Include them
  in the next eligible voting roster. Collection uses its own frozen roster;
  reopening collection refreshes that roster explicitly.
- Removed players lose access immediately. Their unrevealed contributions are
  excluded. A removed nominee's invalid vote must not expose its voter through
  public response-status changes: progress records receipt, while only valid
  nominations enter reveal totals. The author can be privately prompted to choose
  again. Retain per-author revision checks when replacing an invalidated vote.
- A removed current presenter cannot have their lie revealed. Offer skip/end;
  moving on selects the next active queued presenter. Skips/early finishes do
  not publish unfinished private choices or lie indices.
- Once a reveal is finalized, preserve its totals; redact removed identities to
  the existing neutral Removed player label. Escape all user-written text.
- Passes are optional, count toward participation and never count as votes.
  For statement collection, Sit this game out records a completed opt-out rather
  than fabricating three statements. No personal disclosure is required.

## Acceptance and test coverage

| Slice | Unit coverage | API/hub and persistence coverage | Browser coverage |
| --- | --- | --- | --- |
| Shared binary / Would You Rather | Both choices, pass/change, named reveal and summary, repeat/reset filters, old This or That state | Real cookie-authenticated round, private hub payloads, duplicate receipts, stale commands, restart and legacy state | Select game, prompt, answer/pass/change, reveal, next, finish and switch |
| Never Have I Ever | Fixed labels, counts/percentages, empty reveal, pass and eligibility | Private answers, invalid input, permissions, reconnect/restart and finalized totals | Statement and two labeled choices, pass feedback, named reveal and summary |
| Most Likely To | No self/unknown/late targets, removal, ties, zero votes, insufficient players, anonymous projection | Cross-room/role attacks, target removal before/after reveal, duplicate votes/advance and durable restart | Nominate a teammate, change/pass, no self control, tied reveal, missing/away players and finish |
| Two Truths and a Lie | Exactly three valid statements, lengths/lie selection, private projection, queue order, presenter exclusion, no-ready reopen, skips/removal | Inspect raw HTTP and per-viewer hub payloads at collection/guess/reveal, author-only lie index, non-author denial, stale/duplicate submissions and restart at every stage | Two independent players write/save/opt out, close collection, guess/pass, reveal lie and named guesses, next presenter, summary; missing submissions and narrow keyboard flow |
| Shared shell / switching | Registry, chooser permissions, correct controls, stage focus and pending retry state | Five sessions in one durable room, stable membership and facilitator, old game data compatibility | All five games without rejoining, chooser and prompt filtering, title/heading order, mobile overflow, reduced motion and axe checks |

Use the existing verification commands from development.md. Run both test layers
with real API/SQLite, plus desktop and 360px mobile browser projects. Hosted
Unit and functional tests must pass for the exact commit before main is updated.

## Design review evidence

The interactive [M4 draft](https://p.superdesign.dev/draft/3f088dfc-d0b5-4efd-8045-28dcb74df171)
uses fictional demo data. Desktop and 360px prototype checks cover game switching,
nomination self-exclusion, collection controls, presenter exclusion, reveal-only
lie marking, no-ready reopening and insufficient-player states. These are design
checks, not evidence that M4 exists in the application or enforces server privacy.
The user approved these interactions and subsequently instructed automatic design
approval unless review is explicitly requested. This is recorded in AGENTS.md.

## Implementation verification

79 Core tests, 44 API unit/functional tests, 115 frontend component/transport tests,
4 deployment-verifier unit tests and all 58 Docker/Nginx browser cases passed on
2026-10-26. The latter cover desktop and 360px layouts, collection/reveal axe
checks, keyboard guesses and chooser order, all-five-game switching, real API
stage restarts and cross-tab private draft conflicts. Generated contracts, build,
lint and format checks passed. The container build used a temporary locked-package
cache to work around local NuGet DNS timeouts; checked-in deployment files and
dependency versions remain unchanged. Required hosted CI uses the standard build.
