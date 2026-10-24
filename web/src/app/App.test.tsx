import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import { App } from './App'
import { games } from '../test/games'

function show(path = '/') {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <App />
    </MemoryRouter>,
  )
}

describe('application shell', () => {
  it.each(['/', '/how-to-play', '/missing', '/games/this-or-that'])(
    'uses the people branding and headings without full stops at %s',
    async (path) => {
      vi.stubGlobal('fetch', vi.fn().mockResolvedValue(Response.json(games)))
      show(path)
      await screen.findByRole('heading', { level: 1 })
      const brand = screen.getByRole('link', { name: 'Mingle Team games home' })
      expect(within(brand).getByText('Mingle', { exact: true })).toBeVisible()
      expect(within(brand).getByText('Team games', { exact: true })).toBeVisible()
      const icon = brand.querySelector('img')
      expect(icon).toHaveAttribute('src', '/people.svg')
      expect(icon).toHaveAttribute('alt', '')
      for (const heading of screen.getAllByRole('heading')) {
        expect(heading).not.toHaveTextContent('.')
      }
      expect(document.body).not.toHaveTextContent('✳')
    },
  )

  it('announces loading while the catalogue is pending and cancels on unmount', () => {
    const fetchMock = vi.fn().mockReturnValue(new Promise(() => {}))
    vi.stubGlobal('fetch', fetchMock)
    const view = show()
    expect(screen.getByRole('status')).toHaveTextContent('Gathering the games')
    const signal = fetchMock.mock.calls[0][1].signal as AbortSignal
    view.unmount()
    expect(signal.aborted).toBe(true)
  })

  it('lists all five games and navigates to instructions', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(Response.json(games)))
    show()
    for (const game of games)
      expect(await screen.findByRole('heading', { name: game.name })).toBeVisible()
    const section = screen.getByRole('region', { name: 'Small games, big connections' })
    expect(within(section).getAllByRole('link')).toHaveLength(5)
    await userEvent.click(within(section).getAllByRole('link')[0])
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('This or That')
    expect(screen.getByText('Gameplay is coming soon')).toBeVisible()
  })

  it.each(games)('supports direct navigation to $name', async (game) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(Response.json(games)))
    show(`/games/${game.id}`)
    expect(await screen.findByRole('heading', { name: game.name, level: 1 })).toBeVisible()
    expect(screen.getByText(game.instructions)).toBeVisible()
  })

  it.each(['/', '/games/this-or-that'])('recovers from a load failure at %s', async (path) => {
    const fetchMock = vi
      .fn()
      .mockRejectedValueOnce(new Error('offline'))
      .mockResolvedValueOnce(Response.json(games))
    vi.stubGlobal('fetch', fetchMock)
    show(path)
    expect(await screen.findByRole('alert')).toHaveTextContent("We couldn't load the games")
    expect(screen.getByRole('heading', { name: "We couldn't load the games" })).toBeVisible()
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }))
    expect(await screen.findByRole('heading', { name: 'This or That' })).toBeVisible()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledTimes(2)
  })

  it('explains an empty catalogue', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(Response.json([])))
    show()
    expect(await screen.findByText(/game shelf is empty/)).toBeVisible()
  })

  it.each(['/missing', '/games/missing'])('offers a working return link for %s', async (path) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(Response.json(games)))
    show(path)
    expect(await screen.findByRole('heading', { name: /isn't.*in the deck/ })).toBeVisible()
    await userEvent.click(screen.getByRole('link', { name: 'Back to the games' }))
    expect(screen.getByRole('heading', { name: /Make room/ })).toBeVisible()
  })

  it('explains future gameplay and offers room hosting', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(Response.json(games)))
    show('/how-to-play')
    expect(screen.getByRole('list').children).toHaveLength(3)
    expect(screen.getByText(/Gameplay is coming next/)).toBeVisible()
    await userEvent.click(screen.getByRole('link', { name: /Explore the games/ }))
    expect(await screen.findByRole('heading', { name: 'This or That' })).toBeVisible()
  })
})
