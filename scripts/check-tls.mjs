import fs from "node:fs";
import { createRequire } from "node:module";
import { randomUUID } from "node:crypto";
import assert from "node:assert/strict";
const require = createRequire(new URL("../web/package.json", import.meta.url));
const {
  HubConnectionBuilder,
  HttpTransportType,
  LogLevel,
} = require("@microsoft/signalr");
const [base, phase, file] = process.argv.slice(2);
const target = new URL(base);
if (
  target.protocol !== "https:" ||
  !["localhost", "127.0.0.1"].includes(target.hostname)
)
  throw new Error(
    "TLS rehearsal must use an isolated loopback HTTPS deployment",
  );
const saved =
  phase === "setup"
    ? { cookies: {}, room: null }
    : JSON.parse(fs.readFileSync(file, "utf8"));
async function raw(path, payload, headers = {}) {
  const response = await fetch(base + path, {
    method: payload === undefined ? "GET" : "POST",
    headers: {
      Origin: base,
      Cookie: Object.entries(saved.cookies)
        .map(([k, v]) => `${k}=${v}`)
        .join("; "),
      ...(payload === undefined ? {} : { "Content-Type": "application/json" }),
      ...headers,
    },
    ...(payload === undefined ? {} : { body: JSON.stringify(payload) }),
  });
  for (const cookie of response.headers.getSetCookie()) {
    const [name, ...value] = cookie.split(";")[0].split("=");
    saved.cookies[name] = value.join("=");
  }
  return response;
}
async function json(path, payload, headers = {}) {
  if (payload !== undefined) {
    const prefix = path.startsWith("/api/admin")
      ? "/api/admin/session"
      : "/api/session";
    const token = (await (await raw(prefix)).json()).requestToken;
    headers = { "X-CSRF-TOKEN": token, ...headers };
  }
  const response = await raw(path, payload, headers);
  assert.equal(response.ok, true, `TLS API status ${response.status}`);
  if (response.status === 204) return null;
  return response.json();
}
if (phase === "setup") {
  const initial = await raw("/api/session");
  const initialToken = (await initial.json()).requestToken;
  for (const cookie of initial.headers.getSetCookie())
    assert.equal(/; secure/i.test(cookie), true, "Secure CSRF cookie missing");
  const guest = await raw("/api/session", {}, { "X-CSRF-TOKEN": initialToken });
  assert.equal(guest.status, 200);
  assert.equal(
    /; secure/i.test(
      guest.headers.getSetCookie().find((c) => c.startsWith("mingle.guest=")) ??
        "",
    ),
    true,
    "Secure guest cookie missing",
  );
  await json("/api/admin/session");
  const login = await raw(
    "/api/admin/login",
    { credential: process.env.MINGLE_ADMIN_CREDENTIAL },
    {
      "X-CSRF-TOKEN": (await json("/api/admin/session")).requestToken,
      "X-Forwarded-Proto": "http",
      "X-Forwarded-For": "203.0.113.123",
    },
  );
  assert.equal(login.status, 200);
  assert.equal(
    /; secure/i.test(
      login.headers.getSetCookie().find((c) => c.startsWith("mingle.admin=")) ??
        "",
    ),
    true,
    "Secure admin cookie missing",
  );
  assert.equal((await raw("/api/admin/logout", {})).status, 400);
  assert.equal(
    (await raw("/api/rooms", {}, { Origin: "https://attacker.invalid" }))
      .status,
    403,
  );
  assert.equal(
    (await raw("/api/rooms", { padding: "x".repeat(70000) })).status,
    413,
  );
  saved.room = (
    await json("/api/rooms", {
      commandId: randomUUID(),
      displayName: "TLS synthetic host",
    })
  ).snapshot;
  let room = (
    await json(`/api/rooms/${saved.room.id}/game`, {
      commandId: randomUUID(),
      action: "start",
      gameId: "this-or-that",
      expectedVersion: saved.room.version,
    })
  ).snapshot;
  room = (
    await json(`/api/rooms/${room.id}/game`, {
      commandId: randomUUID(),
      action: "answer",
      sessionId: room.game.sessionId,
      roundId: room.game.roundId,
      stageRevision: room.game.stageRevision,
      responseRevision: 0,
      choice: "a",
    })
  ).snapshot;
  saved.room = room;
  const headers = {
    Cookie: Object.entries(saved.cookies)
      .map(([k, v]) => `${k}=${v}`)
      .join("; "),
    Origin: base,
  };
  const hub = new HubConnectionBuilder()
    .withUrl(base + "/hubs/rooms", {
      transport: HttpTransportType.WebSockets,
      headers,
    })
    .configureLogging(LogLevel.None)
    .build();
  let snapshot;
  hub.on("Snapshot", (value) => {
    snapshot = value;
  });
  try {
    await hub.start();
    await hub.invoke("Subscribe", room.id);
    assert.equal(snapshot?.youId, room.youId);
  } finally {
    await hub.stop();
  }
  assert.equal(
    (await raw(`/api/rooms/${room.id}`)).headers.get("cache-control"),
    "no-store",
  );
  const html = await raw("/");
  assert.equal(html.headers.get("cache-control"), "no-cache");
  const source = await html.text();
  const asset = source.match(/src="(\/assets\/[^" ]+\.js)"/)[1];
  assert.match((await raw(asset)).headers.get("cache-control"), /immutable/);
  assert.match(
    (await raw("/")).headers.get("strict-transport-security"),
    /max-age=/,
  );
  fs.writeFileSync(file, JSON.stringify(saved), { mode: 0o600 });
  console.log(
    "TLS certificate verification, Secure cookies, WSS, origin/CSRF/body limits and private/static cache boundaries passed",
  );
} else if (phase === "mutate") {
  const room = await json(`/api/rooms/${saved.room.id}`),
    game = room.game;
  await json(`/api/rooms/${room.id}/game`, {
    commandId: randomUUID(),
    action: "answer",
    sessionId: game.sessionId,
    roundId: game.roundId,
    stageRevision: game.stageRevision,
    responseRevision: 1,
    choice: "b",
  });
  const latest = await json(`/api/rooms/${room.id}`);
  await json(`/api/rooms/${room.id}/commands`, {
    commandId: randomUUID(),
    action: "lock",
    expectedVersion: latest.version,
    locked: true,
  });
  console.log("Synthetic post-backup changes accepted");
} else if (phase === "crashed") {
  const room = await json(`/api/rooms/${saved.room.id}`);
  assert.equal(room.youId, saved.room.youId);
  assert.equal(room.facilitatorId, saved.room.facilitatorId);
  assert.equal(room.joinLocked, true);
  assert.equal(room.game.ownChoice, "b");
  assert.equal(room.version, saved.room.version + 2);
  assert.equal((await json("/api/admin/session")).authenticated, true);
  console.log(
    "Acknowledged answer, room change and protected identities survived SIGKILL and WAL recovery",
  );
} else if (phase === "restored") {
  const room = await json(`/api/rooms/${saved.room.id}`);
  assert.equal(room.youId, saved.room.youId);
  assert.equal(room.facilitatorId, saved.room.facilitatorId);
  assert.equal(room.joinLocked, false);
  assert.equal(room.game.ownChoice, "a");
  assert.equal(room.version, saved.room.version);
  assert.equal((await json("/api/admin/session")).authenticated, true);
  await json("/api/admin/logout", {});
  assert.equal((await raw("/api/admin/rooms")).status, 401);
  console.log(
    "Restored guest/facilitator identity, admin session, private answer and exact room version passed; logout revocation passed",
  );
} else throw new Error("Unknown rehearsal phase");
