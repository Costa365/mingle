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
change your answer before reveal, compare totals, and continue, skip, or finish.
Facilitators can advance with missing or disconnected players. Private answers,
stages, finalized results, and retry receipts survive API restarts. The starter
deck contains 12 reviewed pairs; larger prompt libraries and authoring are M3.

The other four games and administration remain planned. Unit, API/hub functional,
and desktop/mobile browser tests run in CI. M2 is complete, including
[hosted CI](https://github.com/Costa365/mingle/actions/runs/40114848655). See the
implementation plan for acceptance and test evidence.

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
| [Development guide](docs/development.md) | Tooling, verified commands, testing, and workflow |
| [Operator guide](docs/operations.md) | Local containers, persistent data, health checks, and edge routing |

## Next implementation target

Build **M3 — Prompt browsing, authoring, and launch content** on the playable
This or That flow before expanding to the remaining games. The first release includes
all five games, prompt management, administrator controls, and deployment docs.

See the [implementation plan](docs/implementation-plan.md) for the full sequence.
