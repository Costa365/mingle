# Mingle

**Team games**

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

The first implementation milestone, **M0 — Buildable foundation**, now includes
the responsive five-game catalogue and detail pages, loading/error recovery,
an ASP.NET Core 10 API, SQLite migrations, persistent Data Protection keys, and
Docker Compose deployment. Unit, API functional, and desktop/mobile browser tests
are included, along with a GitHub Actions workflow.

Room creation, guest identity, realtime gameplay, facilitator controls, and
administration are **not implemented yet**. The interface labels hosting as coming
soon. See the implementation plan for local verification and pending CI status.

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

Build on the foundation with room creation, invitation, guest identity, and
reconnect recovery. Deliver a complete **This or
That** game before expanding to the remaining games. The first release includes
all five games, prompt management, administrator controls, and deployment docs.

See the [implementation plan](docs/implementation-plan.md) for the full sequence.
