import type { GameSummary } from '../api/games'

export const games: GameSummary[] = [
  ['this-or-that', 'This or That'],
  ['two-truths-and-a-lie', 'Two Truths and a Lie'],
  ['never-have-i-ever', 'Never Have I Ever'],
  ['would-you-rather', 'Would You Rather?'],
  ['most-likely-to', 'Most Likely To'],
].map(([id, name]) => ({
  id,
  name,
  description: 'A conversation starter.',
  instructions: 'Choose an answer and share a story.',
  category: 'Connect',
  minimumPlayers: 2,
  suggestedMinutes: 10,
}))
