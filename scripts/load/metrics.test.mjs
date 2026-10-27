import { test } from "node:test";
import assert from "node:assert/strict";
import { percentile, verifyRound } from "./metrics.mjs";
test("tail latency uses the nearest rank and preserves the original samples", () => {
  const samples = [4, 1, 100, 2];
  assert.equal(percentile(samples, 0.95), 100);
  assert.equal(percentile(samples, 0.5), 2);
  assert.deepEqual(samples, [4, 1, 100, 2]);
  assert.throws(() => percentile([], 0.95));
});
test("a complete round rejects both dropped responses and duplicate transitions", () => {
  verifyRound({ version: 41, game: { responseCount: 20 } }, 21, 20);
  assert.throws(() =>
    verifyRound({ version: 42, game: { responseCount: 20 } }, 21, 20),
  );
  assert.throws(() =>
    verifyRound({ version: 40, game: { responseCount: 19 } }, 21, 20),
  );
});
