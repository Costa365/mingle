// Presentation names stay separate from room lifecycle and authorization rules.
const names: Record<string, string> = {
  'this-or-that': 'This or That',
  'would-you-rather': 'Would You Rather?',
  'never-have-i-ever': 'Never Have I Ever',
  'most-likely-to': 'Most Likely To',
  'two-truths-and-a-lie': 'Two Truths and a Lie',
}
export function gameName(id: string) {
  return names[id] ?? 'Game'
}
