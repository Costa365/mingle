# Team Games

Web application that allows remote teams to play team-building games.

It should support the following games, but also be extendable to support more in future:

- Two Truths And A Lie
- Never Have I Ever
- This or That (should have a comprehensive set of strings, which can be chosen at random or selected by the facilitator. It should be possible to add new strings)
- Would You Rather?
- Most Likely To

It should be easy to join, without needing to create an account. The person that creates the games is the facilitator and can invite other players.

It should use websockets or some other means for realtime gameplay. It should use Docker and Compose. It should have a fun, modern looking and intuitive UI, without looking like every other AI generated application. It should be usable on mobile as well as desktop.

It should should handle player disconnects/reconnects and the faciliator should be able to move to the next stage of a game without waiting for everyone.

Stack: React, ASP.NET API, .NET Core 10. It should be possible to run it on a single host via docker compose and it should be easy to use a CDN (handle potential short lived websockets).

An administrator should be able to see all the games and manage them - it can be a hardcoded token or password.

Follow current best practices for Codex agentic software development. Include AGENTS.md and create documentation.
