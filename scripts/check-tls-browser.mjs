import { createRequire } from "node:module";
import { createHash, X509Certificate } from "node:crypto";
import fs from "node:fs";
import assert from "node:assert/strict";
const require = createRequire(new URL("../web/package.json", import.meta.url));
const { chromium, expect } = require("@playwright/test");
const origin = process.argv[2],
  certificate = process.argv[3];
const target = new URL(origin);
if (
  target.protocol !== "https:" ||
  !["localhost", "127.0.0.1"].includes(target.hostname)
)
  throw new Error("Use the isolated loopback TLS rehearsal");
const cert = new X509Certificate(fs.readFileSync(certificate));
const pin = createHash("sha256")
  .update(cert.publicKey.export({ type: "spki", format: "der" }))
  .digest("base64");
// Trust only this disposable certificate's public key, never ignore all TLS errors.
const browser = await chromium.launch({
  args: [`--ignore-certificate-errors-spki-list=${pin}`],
});
try {
  const context = await browser.newContext({ reducedMotion: "reduce" }),
    page = await context.newPage();
  let secureSocket = false;
  page.on("websocket", (socket) => {
    if (socket.url().startsWith("wss:"))
      socket.on("framereceived", (event) => {
        if (event.payload.toString().includes('"target":"Snapshot"'))
          secureSocket = true;
      });
  });
  await page.goto(origin + "/admin");
  await page
    .getByLabel("Administrator credential")
    .fill(process.env.MINGLE_ADMIN_CREDENTIAL);
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await expect(
    page.getByRole("button", { name: "Sign out", exact: true }),
  ).toBeVisible();
  await page.goto(origin + "/host");
  await page.getByLabel("Your name").fill("TLS browser host");
  await page.getByRole("button", { name: "Create room" }).click();
  await expect(page.getByText("Connected", { exact: true })).toBeVisible();
  await page
    .getByRole("button", { name: "Start This or That", exact: true })
    .click();
  await page
    .getByRole("group", { name: "Your choice" })
    .getByRole("button")
    .first()
    .click();
  await expect(page.getByText(/Your answer is saved/)).toBeVisible();
  await page.reload();
  await expect(page.getByText("Connected", { exact: true })).toBeVisible();
  await page
    .getByRole("button", { name: "Reveal answers", exact: true })
    .click();
  await expect(page.getByText("Round 1 · Time to compare")).toBeVisible();
  assert.equal(
    secureSocket,
    true,
    "The browser must establish WSS through the TLS edge",
  );
  const cookies = await context.cookies(origin + "/api/admin");
  for (const name of ["mingle.guest", "mingle.csrf", "mingle.admin"]) {
    const cookie = cookies.find((c) => c.name === name);
    assert.equal(
      Boolean(cookie?.secure && cookie?.httpOnly),
      true,
      `Secure HttpOnly ${name} cookie missing`,
    );
  }
  console.log(
    "HTTPS browser sign-in, Secure cookie jar, WSS and reload/reveal flow passed",
  );
} finally {
  await browser.close();
}
