import { describe, expect, it, vi } from 'vitest'
import { loadGames } from './games'
import { games } from '../test/games'

describe('game catalogue client', () => {
  it('loads the real transport shape and supplies cancellation', async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json(games))
    vi.stubGlobal('fetch', fetchMock)
    const controller = new AbortController()
    expect(await loadGames(controller.signal)).toEqual(games)
    const [url, options] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/games')
    controller.abort()
    expect(options.signal.aborted).toBe(true)
  })

  it('accepts an empty catalogue', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(Response.json([])))
    expect(await loadGames(new AbortController().signal)).toEqual([])
  })

  it.each([
    {},
    [null],
    [{ ...games[0], minimumPlayers: 0 }],
    [{ ...games[0], suggestedMinutes: 1.5 }],
    [{ ...games[0], id: '../bad-route' }],
    [{ ...games[0], name: '' }],
    [games[0], games[0]],
  ])('rejects malformed catalogue %j', async (value) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(Response.json(value)))
    await expect(loadGames(new AbortController().signal)).rejects.toThrow('could not be read')
  })

  it('turns server failures into a safe error', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(new Response('internal details', { status: 500 })),
    )
    await expect(loadGames(new AbortController().signal)).rejects.toThrow('unavailable')
  })
})
