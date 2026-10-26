# Mingle

**Team games**

[![Verify](https://github.com/Costa365/mingle/actions/workflows/ci.yml/badge.svg)](https://github.com/Costa365/mingle/actions/workflows/ci.yml)

An account-free web application for remote teams to play facilitated games:

- Two Truths and a Lie
- Never Have I Ever
- This or That
- Would You Rather?
- Most Likely To

A facilitator creates a room and shares an invitation. Players join with a display
name, play on desktop or mobile, and recover their place after a connection drops.
The facilitator controls the pace and can continue without waiting for everyone.

## Status

The foundation and **M1 — Rooms, identity, and recovery** are implemented. You can
host a room, invite teammates by link/code, join without an account, and see the
live roster. Facilitators can lock joining, remove players, transfer ownership,
and close the room. Reloads, duplicate tabs, and API restarts preserve membership
and ownership. Room changes and retry receipts persist in SQLite.

**M2 — This or That** now has a playable flow: choose either option or pass,
change your answer before reveal, compare totals and each player’s named choice after reveal, and continue, skip,
or finish.
Facilitators can advance with missing or disconnected players. Private answers,
stages, finalized results, and retry receipts survive API restarts. M3 adds a verified persistent prompt library and room-local authoring. The launch content includes 200 This or That pairs across eight
categories and 50 prompts each for Would You Rather, Never Have I Ever and Most Likely To.

**M4 — All five games** is implemented. The room chooser now starts any game
without rejoining. Would You Rather and Never Have I Ever reveal aggregate
answers; Most Likely To supports anonymous nominations and joint winners; Two
Truths and a Lie supports private statement collection, opt-out, presenter turns,
guessing and reveal. All preserve stage/answer recovery and facilitator pacing.

**M5 — Administrator operations** adds `/admin`: separate expiring sessions,
paginated room/session inspection without answers, room/game controls, confirmed
room deletion, shared prompt management and content-free audit retention. Login
requires a deployment-configured credential; see [administrator setup](docs/operations.md#administrator-access).

Deployment hardening remains planned (M6). Unit, API/hub
functional and desktop/mobile browser tests gate changes in CI. See the
[implementation plan](docs/implementation-plan.md) for current verification evidence.

## Run locally with Docker

```bash
docker compose up --build --detach --wait
```

Open http://localhost:8080. Only the Nginx frontend port is exposed, on loopback.
The API and SQLite storage stay inside the Compose project. `docker compose down`
stops the application and preserves its volume; adding `--volumes` deletes data.
Set `TEAMGAMES_PORT` in `.env` to choose another local port.

For frontend/API development without Docker, follow the
[development guide](docs/development.md). See the
[operator guide](docs/operations.md) for configuration and persistence.

## Documentation

| Document | Purpose |
| --- | --- |
| [Original idea](idea.md) | Original product brief |
| [Agent guidance](AGENTS.md) | Repository conventions and critical invariants |
| [Product requirements](docs/product.md) | Scope, game behavior, UX, and proposed defaults |
| [Architecture](docs/architecture.md) | Components, realtime recovery, data, and access control |
| [Implementation plan](docs/implementation-plan.md) | Milestones, dependencies, and acceptance criteria |
| [Game modules](docs/game-modules.md) | Registering modules, safe projections and required test layers |
| [Development guide](docs/development.md) | Tooling, verified commands, testing, and workflow |
| [Operator guide](docs/operations.md) | Local containers, persistent data, health checks, and edge routing |

## Next implementation target

Implement **M6 — Deployment resilience and experience verification** after M5 verification closes. The first
release also includes production deployment hardening and an operator rehearsal.

See the [implementation plan](docs/implementation-plan.md) for the full sequence.
