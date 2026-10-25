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
    // Deliberately include 'one' in an answer: player identities must match their own label.
    await page.getByRole('button', { name: 'Browse prompts' }).click()
    const prompt = page
      .getByRole('list', { name: 'Available prompts' })
      .getByRole('listitem')
      .filter({
        has: page.getByText('Practice a little daily / Have one long hobby session', {
          exact: true,
        }),
      })
    await prompt.getByRole('button', { name: 'Use this prompt', exact: true }).click()
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
    await expect(
      choices.getByRole('listitem').filter({ has: one.locator('strong', { hasText: /^Host$/ }) }),
    ).toHaveText(/No answer/)
    await expect(
      choices.getByRole('listitem').filter({ has: one.locator('strong', { hasText: /^One$/ }) }),
    ).toHaveText(/Passed/)
    await expect(
      choices.getByRole('listitem').filter({ has: one.locator('strong', { hasText: /^Two$/ }) }),
    ).toContainText(game.optionB)
    await expect(one.getByRole('button', { name: 'Next round' })).toHaveCount(0)
    await page.getByRole('button', { name: 'Next round' }).click()
    await expect(page.getByText('Round 2 · Make your choice')).toBeVisible()
    await page.getByRole('button', { name: 'Skip round' }).click()
    await expect(one.getByRole('heading', { name: 'Round 2 · Skipped' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Round 1', exact: true })).toBeHidden()
    await expect(page.getByRole('button', { name: 'Next round' })).toBeInViewport()
    await page.getByText('Earlier rounds (1)', { exact: true }).click()
    await expect(page.getByRole('heading', { name: 'Round 1', exact: true })).toBeVisible()
    await page.getByText('Earlier rounds (1)', { exact: true }).click()
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

test('remote starts focus Game, named responses stay private and room navigation is not interrupted by answers', async ({
  page,
  browser,
  baseURL,
}) => {
  const context = await browser.newContext({
    baseURL,
    viewport: page.viewportSize(),
    reducedMotion: 'reduce',
  })
  try {
    const room = await host(page)
    const player = await context.newPage()
    await join(player, room.code, 'Maya')
    const navigation = player.getByRole('navigation', { name: 'Room navigation' })
    await expect(navigation.getByRole('button', { name: 'Prompts', exact: true })).toHaveCount(0)
    await page.getByRole('button', { name: 'Prompts', exact: true }).click()
    await page.getByLabel('Search prompts').fill('Mountains')
    await expect(page.getByText('1 prompt · 1 unused and enabled')).toBeVisible()
    await page.getByRole('button', { name: 'Use this prompt', exact: true }).click()
    const heading = player.getByRole('heading', { name: 'This or That', exact: true })
    await expect(heading).toBeFocused()
    await expect(player).toHaveTitle('This or That — Mingle')
    await expect(heading).toBeInViewport()
    await expect(player.getByText('This or That has started', { exact: true })).toBeVisible()
    const choices = player.getByRole('group', { name: 'Your choice' }).getByRole('button')
    await expect(choices).toHaveCount(2)
    const answer = await choices.first().boundingBox()
    expect(answer!.height).toBeLessThanOrEqual(test.info().project.name === 'desktop' ? 80 : 70)
    const pass = player.getByRole('button', { name: 'Pass this round' })
    const passBox = await pass.boundingBox()
    expect(passBox!.height).toBeGreaterThanOrEqual(44)
    expect(passBox!.width).toBeLessThan(answer!.width)
    await expect(player.getByText('You can always pass', { exact: true })).toHaveCount(0)
    await pass.focus()
    await player.keyboard.press('Enter')
    await expect(pass).toHaveAttribute('aria-pressed', 'true')
    const responses = page.getByRole('list', { name: 'Participant responses' })
    await expect(responses.getByRole('listitem').filter({ hasText: /^Maya/ })).toHaveText(
      'MayaAnswered',
    )
    await expect(responses).not.toContainText(/pass/i)
    const privateView = (await snapshot(page, room.id)).game!
    expect(privateView.ownChoice).toBeNull()
    expect(privateView.results).toEqual([])
    expect(privateView.responses).toEqual(
      expect.arrayContaining([
        { participantId: (await snapshot(player, room.id)).youId, hasResponded: true },
      ]),
    )
    await player.getByRole('button', { name: /^Room \(/ }).click()
    const copy = player.getByRole('button', { name: 'Copy invite link' })
    await copy.focus()
    await page.getByRole('group', { name: 'Your choice' }).getByRole('button').first().click()
    await expect(page.getByText('2 of 2 responded')).toBeVisible()
    await expect(copy).toBeFocused()
    await expect(player.getByRole('heading', { name: 'The gathering place' })).toBeVisible()
    await page.getByRole('button', { name: 'Reveal answers' }).click()
    await expect(heading).toBeFocused()
    await page.getByRole('button', { name: 'Prompts', exact: true }).click()
    await page.getByLabel('Search prompts').fill('')
    await page.getByRole('button', { name: 'Draw random prompt', exact: true }).click()
    await expect(player.getByText('Round 2 · Make your choice')).toBeVisible()
    await expect(heading).toBeFocused()
    await expect(page.getByRole('heading', { name: 'Your prompt library' })).toBeHidden()
    await expect(player.getByRole('list', { name: 'Participant responses' })).not.toContainText(
      'Answered',
    )
    await player.reload()
    await expect(player.getByRole('group', { name: 'Your choice' })).toBeVisible()
    expect((await new AxeBuilder({ page: player }).analyze()).violations).toEqual([])
    expect(await player.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
      true,
    )
    await player.screenshot({
      path: `test-results/focused-game-${test.info().project.name}.png`,
      fullPage: true,
    })
  } finally {
    await context.close()
  }
})
