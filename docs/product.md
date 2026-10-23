# Product requirements

## Source and terminology

[idea.md](../idea.md) is the original brief. This document makes its requirements
implementable. Details labeled as defaults are proposals that can be changed.

A **room** is an invited team gathering. A **game session** is one selected game
inside a room. A **round** is one prompt or one presenter's turn. A **stage** is the
current activity, such as collecting answers, voting, or revealing results.

## Required first release

| Requirement | Observable behavior |
| --- | --- |
| Account-free entry | Create a room or join an invitation with a display name; no registration |
| Facilitator ownership | Creator receives server-enforced facilitator rights and an invite link/code |
| Five games | All games named in the brief are playable from start to finish |
| Extensibility | A new game uses the module contract without changing room/reconnect infrastructure |
| Live gameplay | Participants see current stage, submission progress, and revealed results without refreshing |
| Connection recovery | Refreshes, mobile suspension, and short-lived sockets preserve identity and accepted answers |
| Facilitator pacing | Advance, reveal, skip a round, or finish a game despite missing/disconnected players |
| This or That content | Substantial built-in library, random choice, facilitator selection, and new prompts |
| Responsive UI | Complete create/join/play/facilitate flows work on phone and desktop |
| Operations | Single-host Docker Compose deployment with documented CDN behavior |
| Administration | Protected overview of rooms/game sessions and controls to manage them |
| Maintainability | Agent guidance, developer documentation, tests, and deployment documentation |

## Shared room behavior

- The facilitator can also play, choose a game, start it, view answer counts,
  reveal results, start another round, switch games after ending the current one,
  remove a player, lock joining, transfer facilitation, and close the room.
- The invite contains a room identifier/code only. Joining creates a participant
  membership; knowing the invite never grants facilitator permissions.
- Display names are plain text, limited to 32 characters. Reject case-insensitive
  duplicates within a room with a helpful suggestion. Identity is a separate ID.
- Players may pass on any prompt. A pass counts as a response for progress, but
  does not count as a vote. No game requires alcohol or personal disclosure.
- Explicit facilitator actions control stage changes, even when everyone answers.
  Missing responses become abstentions when a stage closes. Zero answers produces
  an empty result, not an error or a fabricated winner.
- Late arrivals can see the current public stage and participate from the next
  round. The round's eligible participant roster is fixed when it starts.
- A temporary disconnect preserves eligibility and accepted answers. A removed
  participant loses access immediately; before reveal, exclude their submissions
  and nominations targeting them. Once revealed, preserve the finalized result
  with a neutral removed-player label where needed.
- Same-browser refresh or another tab reuses the same membership. Multiple tabs
  never count as extra players or extra votes. Cross-device identity recovery is
  outside the first release.
- When the facilitator disconnects, retain their role and show a waiting status.
  Do not automatically promote another player. They can transfer ownership before
  leaving; an administrator can reassign it if recovery is necessary.

## Game rules for the first release

| Game | Stages and input | Reveal and completion |
| --- | --- | --- |
| This or That | Facilitator selects or randomly draws a pair; players choose A, B, or pass | Show totals and percentages for valid votes; facilitator starts next round or ends |
| Would You Rather? | Same binary response mechanic, with a dilemma-style prompt and two options | Same aggregate reveal; keep its own content and presentation |
| Never Have I Ever | Show a statement; players choose “I have,” “I haven't,” or pass | Show aggregate counts; no drinking rules or elimination |
| Most Likely To | Show a prompt; each player nominates one other eligible participant or passes | Show nomination counts and joint winners on a tie; voters remain unattributed |
| Two Truths and a Lie | Collect three statements and a private lie selection from each player; facilitator closes collection, then each ready presenter gets a voting round | Other players guess a statement or pass; reveal the lie and guess totals, then proceed to the next presenter |

For Two Truths and a Lie, incomplete submissions are skipped when collection
closes. If nobody is ready, the facilitator can reopen collection or finish the
game. The presenter cannot guess their own lie; disconnected presenters with a
complete submission can still be presented or skipped. Lie selections are visible
only to their author before reveal. Other players' statements are not published
until their presenter round. Statement input is limited to 200 characters each.

Responses can be changed until the stage closes. Results include submitted votes
only, with passes and missing answers shown separately. Most Likely To has no
self-voting; when fewer than two eligible players remain, offer skip or end. Two
Truths and a Lie likewise needs at least two players for a voting round. Other
games allow one-player previews. A cumulative leaderboard is outside initial scope.

## Prompt library

- Proposed launch target: at least **200 distinct This or That pairs**, covering
  at least eight categories such as food, travel, hobbies, work preferences,
  technology, entertainment, everyday life, and imaginative choices.
- Include at least 50 reviewed prompts each for Would You Rather?, Never Have I
  Ever, and Most Likely To. Two Truths and a Lie uses player-written statements.
- Facilitators can search/filter, select a prompt, or draw randomly. Track used
  prompts per game session and avoid repeats until the eligible pool is exhausted;
  then explain that the pool is exhausted and offer a reset.
- Facilitators can add/edit/delete room-local prompts. Administrators can manage
  the persistent shared library. Room content does not become global automatically.
- Store stable IDs, game type, category/tags, text/options, source (built-in,
  administrator, or room), and enabled status. Store the chosen text in each round
  so later library edits cannot change an active or historical round.
- Validate lengths, blank/identical options, supported prompt shapes, and normalized
  duplicates. Seed updates must be repeatable and preserve administrator edits.
- Use original, workplace-appropriate content. Review variety and readability
  manually; count and schema checks alone do not establish content quality.

## Interface direction

Aim for a playful tabletop feel: clear typography, expressive game-specific color,
large answer controls, participant avatars, and restrained transitions. Give each
game recognizable visual identity while sharing navigation and controls.

The key screens are create/join, lobby/invite, game selection, prompt selection,
live round, reveal, game summary, and administration. On mobile, prioritize the
prompt, response, and current status; keep facilitator actions reachable without
covering answers. On desktop, use extra space for the participant roster.

Include explicit loading, reconnecting, recovered, expired-room, removed-player,
empty-result, and error states. After reconnecting, explain when a round has moved
on. Require accessible labels, keyboard navigation, visible focus, reduced-motion
support, readable contrast, and results distinguishable without color. Verify at
360px width and on a desktop viewport, including browser zoom and touch controls.

## Administration

Use a deployment-configured administrator password/token with a separate login.
Administrators can list active and retained closed rooms, inspect participants and
game-session status, lock joining, remove a participant, reassign the facilitator,
end a game, close/delete a room, and manage shared prompts. Confirm destructive
deletion in the interface and record a content-free audit event. Administrative
status inspection does not expose hidden answers or grant gameplay impersonation.

## Proposed operating defaults and boundaries

- English first; no player accounts, chat, video calls, public matchmaking,
  billing, tournaments, or user-uploaded executable game plugins.
- Start with a configurable cap of 20 players per room, including the facilitator.
  Benchmark 20 concurrent full rooms on a documented host before setting capacity.
- Expire rooms after 24 hours without accepted user activity; heartbeat traffic
  does not extend this. Retain closed room data for 24 hours, then delete answers,
  memberships, room prompts, sessions, and command receipts together. Shared
  prompts persist. Keep content-free administrative audit records for seven days.
- Guest credentials last seven days at most; room expiry still applies. Admin
  sessions expire after one hour. These values must be configurable.
- Survive an API/container restart without losing acknowledged gameplay or role
  ownership. Multi-host operation and automatic failover are future work.
- CDN provider, final brand, exact capacity, and default retention can be refined
  during implementation; none blocks the first local slice.
