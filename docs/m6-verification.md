# M6 release engineering and verification

Status: engineering implemented and locally verified on 2026-10-27; required
hosted CI gates the update to protected main. The public-launch privacy work remains explicitly deferred
by the user; it is a separate release gate and must not be marked complete here.

## Acceptance and test matrix

| Slice | Unit coverage | Functional evidence required |
| --- | --- | --- |
| TLS edge, exact proxy trust and request limits | Deployment settings/header middleware tests | Real TLS Nginx + API with trusted self-signed test CA; Secure guest/admin/CSRF cookies, origin/forgery/cache checks, WSS and forged forwarding headers |
| Backup/restore and rollback rehearsal | Manifest/hash/path/integrity and confirmation tests | Isolated named volume backup, deliberate post-backup mutation, restore, same guest/admin identities and preserved private state/keys |
| Transport recovery | `useRoom` reconnect, ordering, wake and retry tests | Real socket closure, blocked WebSockets/Long Polling, offline/online mobile wake, concurrent reconnect, stale delivery and notification interruption |
| Security and lifecycle | Existing core/admin/boundary tests plus body/proxy bounds | Real HTTP/hub expired/revoked identities, changed roles, CSRF/origin, rate/input limits and CDN exclusions |
| 20 × 20 capacity | Latency percentile and result-integrity tests | Synthetic 400 live clients through Nginx, accepted-response/duplicate checks, recorded host and command-to-hub latency; tune only from evidence |
| Interaction polish | Component/accessibility behavior regression tests | Desktop/360px keyboard, reduced motion, error/empty states and all five game identities |
| Operations and gameplay | Documentation/link/command checks | Fresh install, upgrade/recreation, backup/restore, TLS and provider-neutral CDN/TLS examples, operator and gameplay walkthrough |

Use disposable projects/volumes for all destructive rehearsal and load work.
Keep certificates, keys, credentials, room IDs/codes, answers and database copies
out of source control and public verification artifacts. Reports contain synthetic
counts/timings and sanitized host characteristics only.

## Implementation decisions and measured evidence

The baseline 400-client burst had about 10 seconds p95 end-to-end latency, with
no lost/duplicated responses. Repeated room/prompt reads and per-recipient
publication were the main redundant work. M6 adds a bounded prompt-content cache
(invalidated after edits/deletion, with epoch protection against an in-flight old
load), one room read with separate per-viewer authorization/projection, and
coalesced publications. Credentials, memberships and game answers are not cached.

A five-millisecond bounded command queue groups game commands per room. The
existing single SQLite writer authorizes and checks every command/round/revision,
records distinct receipts, commits the batch, then acknowledges each result.
Invalid inputs are isolated; database failures roll back every uncommitted peer.
Room/prompt/admin mutations keep the same writer boundary. Generic scheduling has
unit tests; real API tests cover concurrent duplicates/invalid/outsider requests,
a SQLite trigger-induced failure with safe same-ID retry, and a transport failure
strictly after commit followed by restart/replay.

The [reference load result](verification/m6-reference-load.json) records 20 rooms,
20 players each, 800 response samples over two This or That rounds, p95 767 ms and HTTP
acknowledgement p95 774 ms on Linux x64, Ryzen 3 5300U, 8 logical CPUs and 15 GiB
reported RAM. All 400 clients reconnected (1709 ms total); no response/transition
integrity checks failed. It uses real Nginx, WebSockets and SQLite, with an entry
limit of 10000/minute because one synthetic peer represents 400 independent users.
This is a local reference measurement, not a production SLA, DOM-paint benchmark
or evidence for multiple API replicas. Shared CI runner timing is report-only;
its 400-client correctness and reconnect checks remain required.

A real TLS/stopped-volume rehearsal passed certificate verification, secure
sessions/CSRF cookies, a real HTTPS browser sign-in/round/reload with certificate
public-key pinning, WSS, origin/forgery/body rejection, caching, backup integrity,
post-backup mutation, abrupt SIGKILL/WAL recovery and restore of exact room version, guest/facilitator identity,
admin session and private answer. Final full-suite/hosted evidence is recorded in
the implementation plan. Selected public hosting/CDN behavior still requires its
operator's pre-launch verification.

The browser recovery review found and fixed stale terminal snapshots stopping a
newer live view. Initial subscription retries stop their prior transport; mobile
wake resubscribes and resynchronizes before controls resume. Browser cases force
WebSocket/SSE failure to prove Long Polling, close real routed sockets, suspend/
wake the client and inject an older terminal frame before continuing a newer
round. Existing five-game, 360px/keyboard, reduced-motion and axe checks remain.
The approved design direction is preserved; no new legal/consent UI is introduced.

## Final local checks

The standard verification scripts passed: 93 core, 66 API, 129 frontend,
12 Python and 2 load-helper unit cases; all 66 Docker browser cases; production
TLS/browser and backup/restore rehearsal; and the reference load target. Both
containers and temporary volumes were removed after each isolated run. The
existing approved desktop/mobile UI was reviewed; M6 changes its recovery behavior
without redesigning the workspace. The release remains gated on the deferred
privacy work and selected real-provider checks noted above.
