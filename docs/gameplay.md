# Playing Mingle

Create a room at Host a room and share its invitation or six-character code.
Players join with unique display names. Separate browser profiles are separate
players; incognito windows in the same profile share a session. Reloads and short
outages preserve the same player's identity and saved responses. Clearing cookies
or switching devices does not recover that identity.

## Room, Game and Prompts

Room puts the team and invitations first. Anyone may share an invitation. The
facilitator can lock joining, remove a player, transfer facilitation and close
the room. Closing/deleting ends live access. A disconnected facilitator keeps
their role; another player is not promoted automatically.

Game focuses when a session/round/stage starts. Ordinary responses do not pull
you away from the workspace you chose. Named response status shows who has
responded; it does not disclose unrevealed choices or distinguish a pass publicly.
The facilitator can reveal, skip or finish with missing/offline players.

Prompts is the facilitator's library for the four prompt games. Filter by game,
category, tags and text. Use this prompt starts it from the lobby or selects the
next round of the current game after reveal. It does not preconfigure a whole
playlist. Other games can be browsed while playing but cannot replace a live
session. Random draws avoid used prompts; reset a depleted filtered pool
explicitly. Room prompts are private to that room; administrator shared prompts
appear across rooms. Editing/deleting content never changes an already-started
round's saved text.

## The five games

| Game | Play | After reveal |
| --- | --- | --- |
| This or That | Choose either option or pass; change until reveal | Named choices and totals invite conversation |
| Would You Rather? | Choose one side of a dilemma or pass | Aggregate totals; individual choices stay private |
| Never Have I Ever | Choose I have, I haven't or pass | Aggregate totals without individual identities |
| Most Likely To | Nominate another eligible teammate; no self-voting | Anonymous nomination totals and joint winners |
| Two Truths and a Lie | Privately save three distinct statements and mark the lie, or sit out | Each ready player presents; others guess, then the lie and anonymous guess totals appear |

Passing is optional and secondary. In Two Truths, the lie is visible only to its
author until that presenter's reveal. The facilitator cannot inspect private
collection. Close collection includes complete submissions and omits missing or
opted-out players. No-ready collection can reopen or finish. Presenters cannot
guess their own statements. Skipping/finishing does not reveal unfinished lies.

Late arrivals wait until the next eligible round/presenter turn. Removed players
cannot reconnect to the room; before reveal their contributions are excluded.
Finalized totals remain, with removed names replaced by a neutral label. Most
Likely To needs two eligible players; skip/finish remains available when there
are too few. If a removed nominee invalidates your vote, choose again when prompted.

## Connection and retry behavior

While reconnecting, controls pause until the room is synchronized. The client
tries supported transports, including Long Polling when WebSockets are
unavailable. Returning to a suspended tab resubscribes and refreshes state.
Retry connection is available after a sustained failure. A late older snapshot
cannot replace the newest round or close a newer active view.

If an action's response is lost, use its retry control to reuse the same command
ID. Do not create a second action to compensate for an uncertain first one.
Cross-tab conflicts ask you to review current data. Administrative recovery is
separate from gameplay and never gives an administrator access to hidden answers.

Before public launch the operator must finish the planned privacy/storage review
and publish the deployment's Privacy & Cookies notice. No legal notice or cookie
consent dialog has been added as part of the current engineering work.
