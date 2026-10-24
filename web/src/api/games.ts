export interface GameSummary {
  id: string
  name: string
  description: string
  instructions: string
  category: string
  minimumPlayers: number
  suggestedMinutes: number
}

function isGame(value: unknown): value is GameSummary {
  if (typeof value !== 'object' || value === null) return false
  const game = value as Record<string, unknown>
  return (
    ['id', 'name', 'description', 'instructions', 'category'].every(
      (key) => typeof game[key] === 'string' && (game[key] as string).trim().length > 0,
    ) &&
    typeof game.minimumPlayers === 'number' &&
    Number.isInteger(game.minimumPlayers) &&
    game.minimumPlayers >= 1 &&
    typeof game.suggestedMinutes === 'number' &&
    Number.isInteger(game.suggestedMinutes) &&
    game.suggestedMinutes >= 1 &&
    /^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(game.id as string)
  )
}

export async function loadGames(signal: AbortSignal): Promise<GameSummary[]> {
  const response = await fetch('/api/games', {
    signal: AbortSignal.any([signal, AbortSignal.timeout(10_000)]),
    headers: { Accept: 'application/json' },
  })
  if (!response.ok) throw new Error('The game catalogue is unavailable.')
  const value: unknown = await response.json()
  if (
    !Array.isArray(value) ||
    !value.every(isGame) ||
    new Set(value.map((game) => game.id)).size !== value.length
  ) {
    throw new Error('The game catalogue could not be read.')
  }
  return value
}
