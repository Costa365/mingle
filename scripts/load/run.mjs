import { createRequire } from "node:module";
import { randomUUID } from "node:crypto";
import { performance } from "node:perf_hooks";
import os from "node:os";
import fs from "node:fs";
import { percentile, verifyRound } from "./metrics.mjs";
const require = createRequire(
  new URL("../../web/package.json", import.meta.url),
);
const {
  HubConnectionBuilder,
  HttpTransportType,
  LogLevel,
} = require("@microsoft/signalr");
const base = new URL(process.argv[2] ?? "http://127.0.0.1:18180");
if (!["127.0.0.1", "localhost"].includes(base.hostname))
  throw new Error(
    "Use an isolated loopback deployment for this synthetic load test",
  );
const roomCount = Number(process.env.LOAD_ROOMS ?? 20),
  players = Number(process.env.LOAD_PLAYERS ?? 20),
  rounds = Number(process.env.LOAD_ROUNDS ?? 2);
if (
  ![roomCount, players, rounds].every((n) => Number.isInteger(n) && n > 0) ||
  roomCount > 20 ||
  players > 20 ||
  rounds > 10
)
  throw new Error("Invalid load bounds");
const connections = [],
  samples = [],
  acknowledgements = [];
async function request(actor, path, payload) {
  const response = await fetch(new URL(path, base), {
    method: payload ? "POST" : "GET",
    headers: {
      Cookie: [...actor.cookies].map(([k, v]) => `${k}=${v}`).join("; "),
      Origin: base.origin,
      ...(payload
        ? { "Content-Type": "application/json", "X-CSRF-TOKEN": actor.token }
        : {}),
    },
    ...(payload ? { body: JSON.stringify(payload) } : {}),
    signal: AbortSignal.timeout(120_000),
  });
  for (const cookie of response.headers.getSetCookie()) {
    const [name, ...value] = cookie.split(";")[0].split("=");
    actor.cookies.set(name, value.join("="));
  }
  if (!response.ok)
    throw new Error(`Load request failed with HTTP ${response.status}`);
  return response.json();
}
async function actor() {
  const a = { cookies: new Map(), token: "" };
  a.token = (await request(a, "/api/session")).requestToken;
  a.token = (await request(a, "/api/session", {})).requestToken;
  return a;
}
function waitFor(a, predicate) {
  if (a.snapshot && predicate(a.snapshot)) return Promise.resolve(a.snapshot);
  return new Promise((resolve, reject) => {
    const waiter = {
      predicate,
      resolve,
      timer: setTimeout(() => {
        a.waiters.delete(waiter);
        reject(new Error("A committed update did not reach its hub observer"));
      }, 120_000),
    };
    a.waiters.add(waiter);
  });
}
async function connect(a, room) {
  a.waiters = new Set();
  a.hub = new HubConnectionBuilder()
    .withUrl(new URL("/hubs/rooms", base).href, {
      transport: HttpTransportType.WebSockets,
      headers: {
        Cookie: [...a.cookies].map(([k, v]) => `${k}=${v}`).join("; "),
        Origin: base.origin,
      },
    })
    .configureLogging(LogLevel.None)
    .build();
  a.hub.on("Snapshot", (s) => {
    a.snapshot = s;
    for (const w of a.waiters)
      if (w.predicate(s)) {
        clearTimeout(w.timer);
        a.waiters.delete(w);
        w.resolve(s);
      }
  });
  connections.push(a.hub);
  await a.hub.start();
  await a.hub.invoke("Subscribe", room.id);
}
async function command(a, room, action) {
  const g = (await request(a, `/api/rooms/${room.id}`)).game;
  const payload = {
    commandId: randomUUID(),
    action,
    sessionId: g?.sessionId,
    roundId: g?.roundId,
    stageRevision: g?.stageRevision ?? 0,
    gameId: "this-or-that",
    expectedVersion: (await request(a, `/api/rooms/${room.id}`)).version,
  };
  return request(a, `/api/rooms/${room.id}/game`, payload);
}
try {
  const rooms = await Promise.all(
    Array.from({ length: roomCount }, async () => {
      const host = await actor();
      const room = (
        await request(host, "/api/rooms", {
          commandId: randomUUID(),
          displayName: "Synthetic 01",
        })
      ).snapshot;
      const members = [host];
      for (let i = 1; i < players; i++) {
        const a = await actor();
        await request(a, "/api/rooms/join", {
          commandId: randomUUID(),
          displayName: `Synthetic ${String(i + 1).padStart(2, "0")}`,
          code: room.code,
        });
        members.push(a);
      }
      await Promise.all(members.map((a) => connect(a, room)));
      await command(host, room, "start");
      await Promise.all(
        members.map((a) => waitFor(a, (s) => s.game?.stage === "responding")),
      );
      return { room, members };
    }),
  );
  for (let round = 0; round < rounds; round++) {
    await Promise.all(
      rooms.map(async ({ room, members }) => {
        const before = await request(members[0], `/api/rooms/${room.id}`),
          g = before.game;
        await Promise.all(
          members.map(async (a, i) => {
            const payload = {
              commandId: randomUUID(),
              action: "answer",
              sessionId: g.sessionId,
              roundId: g.roundId,
              stageRevision: g.stageRevision,
              responseRevision: 0,
              choice: "a",
            };
            const start = performance.now(),
              visible = waitFor(
                a,
                (s) =>
                  s.game?.roundId === g.roundId &&
                  s.game?.responseRevision === 1 &&
                  s.game?.ownChoice === "a",
              ).then(() => performance.now() - start);
            const accepted = await request(
              a,
              `/api/rooms/${room.id}/game`,
              payload,
            );
            acknowledgements.push(performance.now() - start);
            samples.push(await visible);
            if (i === 0) {
              const replay = await request(
                a,
                `/api/rooms/${room.id}/game`,
                payload,
              );
              if (
                !replay.replayed ||
                replay.appliedVersion !== accepted.appliedVersion
              )
                throw new Error("A retried answer did not deduplicate");
            }
          }),
        );
        verifyRound(
          await request(members[0], `/api/rooms/${room.id}`),
          before.version,
          players,
        );
        await command(members[0], room, "reveal");
        await Promise.all(
          members.map((a) =>
            waitFor(
              a,
              (s) =>
                s.game?.roundId === g.roundId && s.game?.stage === "revealed",
            ),
          ),
        );
        const result = (
          await request(members[0], `/api/rooms/${room.id}`)
        ).game.results.at(-1);
        if (result.a !== players || result.missing !== 0)
          throw new Error("Finalized results lost an acknowledged answer");
        if (round + 1 < rounds) {
          await command(members[0], room, "next");
          await Promise.all(
            members.map((a) =>
              waitFor(
                a,
                (s) =>
                  s.game?.roundNumber === round + 2 &&
                  s.game?.stage === "responding",
              ),
            ),
          );
        }
      }),
    );
  }
  const reconnectStart = performance.now();
  await Promise.all(
    rooms.flatMap(({ room, members }) =>
      members.map(async (a) => {
        const identity = a.snapshot.youId;
        await a.hub.stop();
        await a.hub.start();
        await a.hub.invoke("Subscribe", room.id);
        if (
          a.snapshot.youId !== identity ||
          a.snapshot.participants.length !== players
        )
          throw new Error("Reconnect lost identity or duplicated membership");
      }),
    ),
  );
  const reconnectMs = Math.round(performance.now() - reconnectStart);
  const p95 = percentile(samples, 0.95),
    target = Number(process.env.LOAD_TARGET_MS ?? 1000);
  const report = {
    measuredAtUtc: new Date().toISOString(),
    acknowledgementP95Ms: Math.round(percentile(acknowledgements, 0.95)),
    reconnectedClients: connections.length,
    reconnectMs,
    testEntryLimitPerMinute: 10000,
    gameId: "this-or-that",
    rooms: roomCount,
    playersPerRoom: players,
    simultaneousConnections: connections.length,
    rounds,
    samples: samples.length,
    p50Ms: Math.round(percentile(samples, 0.5)),
    p95Ms: Math.round(p95),
    maxMs: Math.round(Math.max(...samples)),
    targetMs: target,
    targetMet: p95 < target,
    duplicateOrLostResponses: 0,
    transport: "WebSockets through Nginx",
    host: {
      os: os.platform(),
      release: os.release(),
      architecture: os.arch(),
      cpus: os.availableParallelism(),
      cpuModel: os.cpus()[0].model,
      totalMemoryGiB: Math.round(os.totalmem() / 2 ** 30),
    },
  };
  fs.mkdirSync("artifacts/verification", { recursive: true });
  fs.writeFileSync(
    "artifacts/verification/load.json",
    JSON.stringify(report, null, 2) + "\n",
  );
  console.log(JSON.stringify(report));
  if (!report.targetMet && process.env.LOAD_ENFORCE_TARGET !== "false")
    process.exitCode = 2;
} finally {
  await Promise.allSettled(connections.map((hub) => hub.stop()));
}
