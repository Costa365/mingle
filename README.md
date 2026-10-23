# Team Games

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

Planning initialized from [idea.md](idea.md) on 2026-10-23. No application code,
dependency manifests, tests, or Docker configuration have been created yet.

The proposed implementation uses React with TypeScript and Vite, an ASP.NET Core
10 API, SignalR, and SQLite. Docker Compose will run an Nginx frontend/proxy and one
API container with persistent storage. These supporting technology choices are
planning decisions; React, .NET 10, and Docker Compose come from the brief.

## Documentation

| Document | Purpose |
| --- | --- |
| [Original idea](idea.md) | Original product brief |
| [Agent guidance](AGENTS.md) | Repository conventions and critical invariants |
| [Product requirements](docs/product.md) | Scope, game behavior, UX, and proposed defaults |
| [Architecture](docs/architecture.md) | Components, realtime recovery, data, and access control |
| [Implementation plan](docs/implementation-plan.md) | Milestones, dependencies, and acceptance criteria |
| [Development guide](docs/development.md) | Tooling, planned commands, testing, and workflow |

## First implementation target

Create the buildable frontend/API/Compose foundation, then implement room creation,
invitation, guest identity, and reconnect recovery. Deliver a complete **This or
That** game before expanding to the remaining games. The first release includes
all five games, prompt management, administrator controls, and deployment docs.

See the [implementation plan](docs/implementation-plan.md) for the full sequence.
There is no runnable quickstart yet; verified startup commands will be added with
the application scaffold.
