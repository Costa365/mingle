# M5 administrator operations

Status: implemented and locally verified on `feat/m5-admin`. Required hosted CI
gates updating protected main with the exact verified commit.

## Decisions

- `/admin` has Rooms, Shared prompts and Audit views within the existing Mingle
  shell. The [design draft](https://p.superdesign.dev/draft/2deca1fd-63b3-46f8-a011-2a299275009d)
  follows the repository's automatic design approval instruction.
- Configure a deployment credential of at least 16 characters; prefer a random
  token. Absent configuration disables login and all admin authorization. A
  separate HttpOnly, Strict SameSite, `/api/admin` cookie represents a durable,
  revocable session. Sessions expire after 60 minutes by default, without sliding
  renewal; credential rotation invalidates existing sessions. Login is limited
  to five attempts per source IP per minute. All mutations require same-origin
  and antiforgery validation under the administrator identity.
- Administrators never acquire guest membership or facilitator identity. Explicit
  admin room DTOs expose room status, active names/counts, facilitator, last
  activity and module-owned session/stage metadata only. `IGameModule.Describe`
  cannot return answer data. `Finish` closes input without revealing unfinished
  answers. Both are part of each module's extension contract.
- Room management shares RoomStore's SQLite writer/transaction and RoomRules'
  management transitions. Expected versions reject stale changes. The current
  facilitator must be replaced before removal. No game-specific conditionals are
  introduced into room storage. After commit the existing publisher sends forced
  updates or access-ended events to connected clients.
- Admin mutation receipts are content-free audit rows keyed by session/command
  UUID and a request hash. Replays cannot mutate twice or reuse an ID for another
  request. Deletion receipts survive deletion; guest receipts, memberships, game
  state and room-local prompts cascade with the room. The UI requires the exact
  room code before deletion. Ambiguous requests keep their UUID/payload across
  reload in session storage, bound to the current admin session expiry. Credentials
  are never stored there. Confirmed writes are not reoffered after refresh failure.
- Shared prompts use existing content validation/revisions and frozen round text.
  Admin-created/edited prompts preserve their source and seed edit protection.
  Shared changes increment active room versions and publish fresh snapshots so
  connected facilitator libraries reload without advancing a stage. Disabling excludes future draws; deletion of a seeded prompt creates a stable-ID
  tombstone so seeding cannot resurrect it. Room-local content remains isolated.
- Migration 5 adds admin sessions, audit/receipt rows and seed deletion tombstones.
  Audit rows contain action/target identifiers, timestamps, receipt hashes and
  versions only; no names, content or credentials. Maintenance deletes old audit
  rows after seven days by default and removes expired/rotated sessions.

## Acceptance and coverage

| Feature | Unit coverage | Real functional coverage |
| --- | --- | --- |
| Configured login, disabled access, expiry and revocation | `AdminRulesTests`, `AdminPage.test`, `admin.test` | `AdminFunctionalTests` tests absent config, wrong/forged login, rate limit, session/credential rotation, restart, logout replay and guest isolation; `admin.spec.ts` signs in/out with independent browser contexts |
| Room/session overview and pagination | `AdminRulesTests`, `AdminPage.test` | API paging/filter bounds and explicit status-only payload inspection across all five modules; browser room list/detail and shared pagination |
| Lock/unlock, transfer/remove, finish and close | Shared room rules + admin module-finish theory for all five games | HTTP/hub tests cover replay, stale/reused commands and live forced updates; browser controls another context's active room |
| Confirmed deletion and cascade | `AdminRulesTests`, `AdminPage.test` | API wrong-confirmation/replay/dependent-data deletion and browser typed-code confirmation/access denial |
| Shared prompt create/edit/enable/disable/delete | `PromptRulesTests`, `PromptEditor.test`, `admin.test` | Real API uniqueness, stale revisions, frozen round text, restart/seed tombstones; browser editor, pagination, confirmation/cancellation and lost-response retry across reload |
| Content-free audit and retention | Core configuration/paging tests | Real API serialized audit privacy, database retention sweep and browser audit view |
| Narrow/keyboard/accessibility and recovery | Component states and DTO transport tests | Desktop/360px browser cases, axe, no overflow and actual API restart retaining admin session/shared edits |

Execution evidence is recorded in the M5 section of the implementation plan as
checks complete. Never treat the design prototype as functional test evidence.

## Local verification evidence

On 2026-10-26: 90 core tests, 55 API unit/functional tests, 126 frontend tests,
4 deployment-verifier unit tests and all 62 Docker/Nginx desktop/mobile browser
cases passed (337 total). Standard checked-in container builds succeeded without
a cache override. Build, locked restore, generated-contract, format, lint,
TypeScript, proxy/log privacy and database/key recreation checks passed. Browser
screenshots were reviewed at 360px; room details/deletion focus and keyboard login
are included. Hosted CI must pass for the same commit before main is updated.
