import AxeBuilder from '@axe-core/playwright'
import type { Page, WebSocketRoute } from '@playwright/test'
import { test, expect } from './fixtures'
import type { RoomSnapshot } from '../src/api/rooms'
async function host(page: Page) {
  await page.goto('/host')
  await page.getByLabel('Your name').fill('Host')
  await page.getByRole('button', { name: 'Create room' }).click()
  await expect(page.getByText('Connected', { exact: true })).toBeVisible()
  const id = page.url().split('/').at(-1)!
  return (await (await page.request.get(`/api/rooms/${id}`)).json()) as RoomSnapshot
}
test('a real proxy connection falls back to Long Polling when WebSockets and SSE are unavailable', async ({
  page,
}) => {
  await page.routeWebSocket(/\/hubs\/rooms/, (socket) =>
    socket.close({ code: 1008, reason: 'Synthetic unavailable transport' }),
  )
  await page.route('**/hubs/rooms**', (route) =>
    route.request().headers().accept?.includes('text/event-stream')
      ? route.abort()
      : route.continue(),
  )
  let polls = 0
  page.on('request', (request) => {
    if (
      request.method() === 'GET' &&
      new URL(request.url()).pathname === '/hubs/rooms' &&
      !request.headers().accept?.includes('text/event-stream')
    )
      polls++
  })
  const room = await host(page)
  expect(polls).toBeGreaterThan(0)
  await page.getByRole('button', { name: 'Start This or That', exact: true }).click()
  await page.getByRole('group', { name: 'Your choice' }).getByRole('button').first().click()
  await expect(page.getByText(/Your answer is saved/)).toBeVisible()
  await page.reload()
  await expect(page.getByText('Connected', { exact: true })).toBeVisible()
  expect(
    ((await (await page.request.get(`/api/rooms/${room.id}`)).json()) as RoomSnapshot).youId,
  ).toBe(room.youId)
  await page.getByRole('button', { name: 'Reveal answers', exact: true }).click()
  await expect(page.getByText('Round 1 · Time to compare')).toBeVisible()
})
test('socket termination, mobile wake and a stale terminal delivery preserve the newest game and role', async ({
  page,
  browser,
  baseURL,
}) => {
  test.setTimeout(120_000)
  const context = await browser.newContext({
    baseURL,
    viewport: page.viewportSize(),
    reducedMotion: 'reduce',
  })
  try {
    let client: WebSocketRoute | undefined, server: WebSocketRoute | undefined
    let suspended = false
    await context.routeWebSocket(/\/hubs\/rooms/, (socket) => {
      if (suspended) {
        void socket.close({ code: 1012, reason: 'Synthetic suspension' })
        return
      }
      client = socket
      server = socket.connectToServer()
    })
    const player = await context.newPage(),
      room = await host(page)
    await player.goto(`/join?code=${room.code}`)
    await player.getByLabel('Your name').fill('Player')
    await player.getByRole('button', { name: 'Join room', exact: true }).click()
    await expect(player.getByText('Connected', { exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'Start This or That', exact: true }).click()
    await player.getByRole('group', { name: 'Your choice' }).getByRole('button').first().click()
    await expect(player.getByText(/Your answer is saved/)).toBeVisible()
    const before = (await (
      await player.request.get(`/api/rooms/${room.id}`)
    ).json()) as RoomSnapshot
    suspended = true
    await context.setOffline(true)
    await client!.close({ code: 1012, reason: 'Synthetic socket termination' })
    await server!.close({ code: 1012, reason: 'Synthetic socket termination' })
    await expect(player.getByText('Reconnecting…', { exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'Reveal answers', exact: true }).click()
    await page.getByRole('button', { name: 'Next round', exact: true }).click()
    suspended = false
    await context.setOffline(false)
    await player.evaluate(() => document.dispatchEvent(new Event('visibilitychange')))
    await expect(player.getByText('Connected', { exact: true })).toBeVisible({ timeout: 30_000 })
    await expect(player.getByText('Round 2 · Make your choice')).toBeVisible()
    const current = (await (
      await player.request.get(`/api/rooms/${room.id}`)
    ).json()) as RoomSnapshot
    expect(current.youId).toBe(before.youId)
    expect(current.facilitatorId).toBe(room.facilitatorId)
    expect(current.game!.results[0].a).toBe(1)
    await client!.send(
      JSON.stringify({
        type: 1,
        target: 'Snapshot',
        arguments: [{ ...before, status: 'closed', game: null }],
      }) + '\u001e',
    )
    await expect(player.getByText('Round 2 · Make your choice')).toBeVisible()
    await page.getByRole('button', { name: 'Reveal answers', exact: true }).click()
    await expect(player.getByText('Round 2 · Time to compare')).toBeVisible()
    expect((await new AxeBuilder({ page: player }).analyze()).violations).toEqual([])
    expect(
      await player.evaluate(() => matchMedia('(prefers-reduced-motion: reduce)').matches),
    ).toBe(true)
    expect(await player.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
      true,
    )
  } finally {
    await context.close()
  }
})
