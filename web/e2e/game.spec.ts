import AxeBuilder from '@axe-core/playwright'
import { test, expect } from './fixtures'
import type { Page } from '@playwright/test'
import type { RoomSnapshot } from '../src/api/rooms'

async function snapshot(page: Page, id: string): Promise<RoomSnapshot> {
  const response = await page.request.get(`/api/rooms/${id}`)
  expect(response.ok()).toBe(true)
  return (await response.json()) as RoomSnapshot
}
async function host(page: Page) {
  await page.goto('/host')
  await page.getByLabel('Your name').fill('Host')
  await page.getByRole('button', { name: 'Create room' }).click()
  await expect(page.getByText('Connected', { exact: true })).toBeVisible()
  return snapshot(page, page.url().split('/').at(-1)!)
}
async function join(page: Page, code: string, name: string) {
  await page.goto(`/join?code=${code}`)
  await page.getByLabel('Your name').fill(name)
  await page.getByRole('button', { name: 'Join room' }).click()
  await expect(page.getByText('Connected', { exact: true })).toBeVisible()
}

test('three people choose, pass, change, reveal, skip and finish with private live views', async ({
  page,
  browser,
  baseURL,
}) => {
  const options = { baseURL, viewport: page.viewportSize(), reducedMotion: 'reduce' as const }
  const oneContext = await browser.newContext(options)
  const twoContext = await browser.newContext(options)
  try {
    const one = await oneContext.newPage()
    const two = await twoContext.newPage()
    const room = await host(page)
    await join(one, room.code, 'One')
    await join(two, room.code, 'Two')
    await expect(page.getByText('Two', { exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'Start This or That' }).click()
    await expect(one.getByRole('group', { name: 'Your choice' })).toBeVisible()
    const game = (await snapshot(page, room.id)).game!
    await Promise.all([
      one.getByRole('button', { name: game.optionA, exact: true }).click(),
      two.getByRole('button', { name: game.optionB, exact: true }).click(),
    ])
    await expect(page.getByText('2 of 3 responded')).toBeVisible()
    expect((await snapshot(page, room.id)).game!.ownChoice).toBeNull()
    await expect(one.getByRole('list', { name: 'Player choices for round 1' })).toHaveCount(0)
    expect((await snapshot(one, room.id)).game!.ownChoice).toBe('a')
    await one.getByRole('button', { name: 'Pass this round' }).click()
    await expect(one.getByRole('button', { name: 'Pass this round' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
    expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([])
    await page.getByRole('button', { name: 'Reveal answers' }).focus()
    await page.keyboard.press('Enter')
    await expect(two.getByText(`${game.optionB}: 1 vote (100%)`)).toBeVisible()
    await expect(one.getByText('1 passed · 1 unanswered')).toBeVisible()
    const choices = one.getByRole('list', { name: 'Player choices for round 1' })
    await expect(choices.getByRole('listitem').filter({ hasText: 'Host' })).toHaveText(/No answer/)
    await expect(choices.getByRole('listitem').filter({ hasText: 'One' })).toHaveText(/Passed/)
    await expect(choices.getByRole('listitem').filter({ hasText: 'Two' })).toContainText(
      game.optionB,
    )
    await expect(one.getByRole('button', { name: 'Next round' })).toHaveCount(0)
    await page.getByRole('button', { name: 'Next round' }).click()
    await expect(page.getByText('Round 2 · Make your choice')).toBeVisible()
    await page.getByRole('button', { name: 'Skip round' }).click()
    await expect(one.getByRole('heading', { name: 'Round 2 · Skipped' })).toBeVisible()
    await page.getByRole('button', { name: 'Next round' }).click()
    await twoContext.close() // A disconnected eligible participant must not block reveal.
    await page.getByRole('button', { name: 'Reveal answers' }).click()
    await expect(page.getByText('0 passed · 3 unanswered')).toBeVisible()
    await page.getByRole('button', { name: 'Finish game' }).click()
    await expect(one.getByRole('heading', { name: 'Thanks for playing' })).toBeVisible()
    expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([])
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    await page.screenshot({
      path: `test-results/game-summary-${test.info().project.name}.png`,
      fullPage: true,
    })
    await page.getByRole('button', { name: 'Return to lobby' }).click()
    await expect(page.getByRole('button', { name: 'Start This or That' })).toBeVisible()
    await expect(one.getByText('Waiting for your facilitator to start a game')).toBeVisible()
  } finally {
    await oneContext.close()
    await twoContext.close()
  }
})

test('late arrival waits, then plays next round and survives real API restart', async ({
  page,
  browser,
  baseURL,
  backend,
}) => {
  const context = await browser.newContext({ baseURL, viewport: page.viewportSize() })
  try {
    const room = await host(page)
    await page.getByRole('button', { name: 'Start This or That' }).click()
    await expect(page.getByRole('group', { name: 'Your choice' })).toBeVisible()
    const late = await context.newPage()
    await join(late, room.code, 'Late')
    await expect(late.getByText(/You joined during this round/)).toBeVisible()
    await expect(late.getByRole('button', { name: 'Pass this round' })).toBeDisabled()
    await page.getByRole('button', { name: 'Reveal answers' }).click()
    await page.getByRole('button', { name: 'Next round' }).click()
    await expect(late.getByRole('button', { name: 'Pass this round' })).toBeEnabled()
    await late.getByRole('button', { name: 'Pass this round' }).click()
    await expect(late.getByRole('button', { name: 'Pass this round' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
    const before = (await snapshot(late, room.id)).game!
    await backend.stop()
    await expect(page.getByText('Reconnecting…', { exact: true })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Reveal answers' })).toBeDisabled()
    await backend.start()
    await page.getByRole('button', { name: 'Retry connection' }).click()
    await late.getByRole('button', { name: 'Retry connection' }).click()
    await expect(late.getByText('Connected', { exact: true })).toBeVisible()
    expect((await snapshot(late, room.id)).game).toEqual(before)
    await expect(page.getByText('Connected', { exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'Finish game' }).click()
    await expect(late.getByRole('heading', { name: 'Round 2 · Skipped' })).toBeVisible()
    await page.getByRole('button', { name: 'Return to lobby' }).click()
  } finally {
    await context.close()
  }
})

test('an ambiguous game acknowledgement retries the same command without advancing twice', async ({
  page,
}) => {
  const room = await host(page)
  await page.getByRole('button', { name: 'Start This or That' }).click()
  await expect(page.getByRole('button', { name: 'Reveal answers' })).toBeVisible()
  const commands: unknown[] = []
  let first = true
  await page.route(`**/api/rooms/${room.id}/game`, async (route) => {
    commands.push(route.request().postDataJSON())
    const response = await route.fetch()
    if (first) {
      first = false
      await route.fulfill({ status: 502, contentType: 'application/json', body: '{}' })
    } else await route.fulfill({ response })
  })
  await page.getByRole('button', { name: 'Reveal answers' }).click()
  await page.getByRole('button', { name: 'Retry game change' }).click()
  await expect(page.getByRole('button', { name: 'Retry game change' })).toHaveCount(0)
  expect(commands).toHaveLength(2)
  expect(commands[0]).toEqual(commands[1])
  expect((await snapshot(page, room.id)).game!.results).toHaveLength(1)
})
