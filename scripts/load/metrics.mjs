export function percentile(samples, fraction) {
  if (
    !samples.length ||
    fraction < 0 ||
    fraction > 1 ||
    samples.some((n) => !Number.isFinite(n) || n < 0)
  )
    throw new Error("Invalid latency samples");
  return [...samples].sort((a, b) => a - b)[
    Math.max(0, Math.ceil(samples.length * fraction) - 1)
  ];
}
export function verifyRound(snapshot, baseVersion, players) {
  if (
    snapshot.version !== baseVersion + players ||
    snapshot.game.responseCount !== players
  )
    throw new Error("Lost or duplicate accepted responses");
}
