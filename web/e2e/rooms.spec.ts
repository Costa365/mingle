import AxeBuilder from '@axe-core/playwright'
import type { Browser, BrowserContext, Page } from '@playwright/test'
import { test, expect } from './fixtures'
import type { CommandResult, RoomSnapshot } from '../src/api/rooms'

async function visitor(
  browser: Browser,
  baseURL: string | undefined,
  page: Page,
): Promise<BrowserContext> {
  return await browser.newContext({
    baseURL,
    viewport: page.viewportSize(),
    reducedMotion: 'reduce',
    isMobile: test.info().project.name === 'mobile',
    hasTouch: test.info().project.name === 'mobile',
    permissions: ['clipboard-read', 'clipboard-write'],
  })
}
async function host(page: Page, name = 'Alex') {
  await page.goto('/host')
  await page.getByLabel('Your name').fill(name)
  await page.getByRole('button', { name: 'Create room' }).click()
  await expect(page.getByRole('heading', { name: 'The gathering place' })).toBeVisible()
  await expect(page.getByText('Connected', { exact: true })).toBeVisible()
  const id = page.url().split('/').at(-1)!
  return await snapshot(page, id)
}
async function snapshot(page: Page, id: string): Promise<RoomSnapshot> {
  const response = await page.request.get(`/api/rooms/${id}`)
  expect(response.ok()).toBe(true)
  return (await response.json()) as RoomSnapshot
}
async function join(page: Page, code: string, name = 'Sam') {
  await page.goto(`/join?code=${code}`)
  await expect(page.getByLabel('Room code')).toHaveValue(code)
  await page.getByLabel('Your name').fill(name)
  await page.getByRole('button', { name: 'Join room' }).click()
  await expect(page.getByRole('heading', { name: 'The gathering place' })).toBeVisible()
  await expect(page.getByText('Connected', { exact: true })).toBeVisible()
}

test('host, invitation copying, live join, duplicate-name validation, and duplicate-tab recovery', async ({
  page,
  browser,
  baseURL,
}) => {
  const context = await visitor(browser, baseURL, page)
  try {
    const room = await host(page)
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([])
    await page.context().grantPermissions(['clipboard-read', 'clipboard-write'])
    await page.getByRole('button', { name: 'Copy invite link' }).click()
    await expect(page.getByText('Invite link copied')).toBeVisible()
    const link = await page.evaluate(() => navigator.clipboard.readText())
    expect(new URL(link).searchParams.get('code')).toBe(room.code)
    await page.getByRole('button', { name: 'Copy code', exact: true }).click()
    await expect(page.getByText('Room code copied')).toBeVisible()
    expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(room.code)
    const player = await context.newPage()
    await player.goto(link)
    await player.getByLabel('Your name').fill('ALEX')
    await player.getByRole('button', { name: 'Join room' }).click()
    await expect(player.getByRole('alert')).toHaveText(/already uses that name/)
    await player.getByLabel('Your name').fill('Sam')
    await player.getByRole('button', { name: 'Join room' }).click()
    await expect(player.getByRole('heading', { name: 'The gathering place' })).toBeVisible()
    await expect(page.getByText('Sam', { exact: true })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Your team (2/20)' })).toBeVisible()
    await expect(player.getByRole('button', { name: 'Lock joining' })).toHaveCount(0)
    const before = await snapshot(player, room.id)
    await player.reload()
    await expect(player.getByText('Connected', { exact: true })).toBeVisible()
    const duplicate = await context.newPage()
    await duplicate.goto(`/rooms/${room.id}`)
    await expect(duplicate.getByText('Sam (you)', { exact: true })).toBeVisible()
    expect((await snapshot(duplicate, room.id)).youId).toBe(before.youId)
    await expect(page.getByRole('heading', { name: 'Your team (2/20)' })).toBeVisible()
    expect((await new AxeBuilder({ page: player }).analyze()).violations).toEqual([])
    await page.screenshot({
      path: `test-results/lobby-${test.info().project.name}.png`,
      fullPage: true,
    })
  } finally {
    await context.close()
  }
})

test('locking, unlocking, removal, and confirmed closure affect other browsers', async ({
  page,
  browser,
  baseURL,
}) => {
  const context = await visitor(browser, baseURL, page)
  const blockedContext = await visitor(browser, baseURL, page)
  try {
    const room = await host(page)
    const player = await context.newPage()
    const waiting = await blockedContext.newPage()
    await join(player, room.code)
    await page.getByRole('button', { name: 'Lock joining', exact: true }).click()
    await expect(page.getByRole('button', { name: 'Unlock joining' })).toBeEnabled()
    await waiting.goto(`/join?code=${room.code}`)
    await waiting.getByLabel('Your name').fill('Taylor')
    await waiting.getByRole('button', { name: 'Join room' }).click()
    await expect(waiting.getByRole('alert')).toHaveText(/locked joining/)
    await player.reload()
    await expect(player.getByText('Sam (you)')).toBeVisible()
    await page.getByRole('button', { name: 'Unlock joining' }).click()
    await expect(page.getByRole('button', { name: 'Lock joining', exact: true })).toBeEnabled()
    await waiting.getByRole('button', { name: 'Join room' }).click()
    await expect(waiting.getByRole('heading', { name: 'The gathering place' })).toBeVisible()
    await expect(page.getByText('Taylor', { exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'Remove Sam', exact: true }).click()
    await expect(player.getByRole('heading', { name: 'Your room is unavailable' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Remove Sam', exact: true })).toHaveCount(0)
    await page.getByRole('button', { name: 'Close room', exact: true }).click()
    await expect(page.getByRole('button', { name: 'Keep room open' })).toBeFocused()
    await page.keyboard.press('Escape')
    await expect(page.getByRole('alertdialog')).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Close room', exact: true })).toBeFocused()
    await page.getByRole('button', { name: 'Close room', exact: true }).click()
    await page.getByRole('button', { name: 'Close room for everyone' }).click()
    await expect(page.getByRole('heading', { name: 'This room is closed' })).toBeVisible()
    await expect(waiting.getByRole('heading', { name: 'This room is closed' })).toBeVisible()
  } finally {
    await context.close()
    await blockedContext.close()
  }
})

test('ownership transfer survives reload and another room cannot read the lobby', async ({
  page,
  browser,
  baseURL,
}) => {
  const context = await visitor(browser, baseURL, page)
  const outsideContext = await visitor(browser, baseURL, page)
  try {
    const room = await host(page)
    const player = await context.newPage()
    await join(player, room.code)
    const outsider = await outsideContext.newPage()
    await host(outsider, 'Outsider')
    await outsider.goto(`/rooms/${room.id}`)
    await expect(outsider.getByRole('heading', { name: 'Your room is unavailable' })).toBeVisible()
    await expect(outsider.getByText('Alex', { exact: true })).toHaveCount(0)
    await page.getByRole('button', { name: 'Make Sam facilitator' }).click()
    await expect(page.getByRole('button', { name: 'Lock joining' })).toHaveCount(0)
    await expect(player.getByRole('button', { name: 'Lock joining' })).toBeEnabled()
    await player.reload()
    await expect(player.getByRole('button', { name: 'Lock joining' })).toBeEnabled()
    await player.getByRole('button', { name: 'Make Alex facilitator' }).click()
    await expect(page.getByRole('button', { name: 'Lock joining' })).toBeEnabled()
  } finally {
    await context.close()
    await outsideContext.close()
  }
})

test('lost create and command acknowledgements retry without duplicating changes', async ({
  page,
}) => {
  let created: CommandResult | undefined
  let firstCreate = true
  await page.route('**/api/rooms', async (route) => {
    if (!firstCreate) return await route.continue()
    firstCreate = false
    const response = await route.fetch()
    created = (await response.json()) as CommandResult
    await route.abort('failed')
  })
  await page.goto('/host')
  await page.getByLabel('Your name').fill('Alex')
  await page.getByRole('button', { name: 'Create room' }).click()
  await expect(page.getByRole('alert')).toHaveText(/safely retry/)
  await page.reload()
  await page.getByLabel('Your name').fill('Alex')
  await page.getByRole('button', { name: 'Create room' }).click()
  await expect(page.getByText('Connected', { exact: true })).toBeVisible()
  expect(page.url()).toContain(created!.snapshot.id)
  const before = await snapshot(page, created!.snapshot.id)
  let firstCommand = true
  await page.route('**/api/rooms/*/commands', async (route) => {
    if (!firstCommand) return await route.continue()
    firstCommand = false
    await route.fetch()
    await route.fulfill({
      status: 502,
      contentType: 'application/json',
      body: JSON.stringify({
        code: 'gateway_unavailable',
        message: 'The acknowledgment could not be delivered',
      }),
    })
  })
  await page.getByRole('button', { name: 'Lock joining', exact: true }).click()
  await page.getByRole('button', { name: 'Retry last change' }).click()
  await expect(page.getByRole('button', { name: 'Unlock joining' })).toBeEnabled()
  expect((await snapshot(page, before.id)).version).toBe(before.version + 1)
})

test('API restart reconnects existing room members and preserves ownership', async ({
  page,
  browser,
  baseURL,
  backend,
}) => {
  const context = await visitor(browser, baseURL, page)
  try {
    const room = await host(page)
    const player = await context.newPage()
    await join(player, room.code)
    const before = await snapshot(page, room.id)
    await backend.stop()
    try {
      await expect(page.getByRole('button', { name: 'Lock joining', exact: true })).toBeDisabled({
        timeout: 20_000,
      })
      await expect(page.getByText('Reconnecting…', { exact: true })).toBeVisible()
    } finally {
      await backend.start()
    }
    await expect(page.getByText('Connected', { exact: true })).toBeVisible({ timeout: 25_000 })
    await expect(player.getByText('Connected', { exact: true })).toBeVisible({ timeout: 25_000 })
    const restored = await snapshot(page, room.id)
    expect(restored.youId).toBe(before.youId)
    expect(restored.facilitatorId).toBe(before.facilitatorId)
    expect(restored.version).toBe(before.version)
    expect(restored.presenceEpoch).not.toBe(before.presenceEpoch)
    await expect(page.getByRole('button', { name: 'Lock joining', exact: true })).toBeEnabled()
    await expect(player.getByText('Alex', { exact: true })).toBeVisible()
  } finally {
    await context.close()
  }
})

test('Room keeps team and invitations at the top while Game and Prompts have dedicated views', async ({
  page,
}) => {
  await host(page)
  const team = page.getByRole('region', { name: /Your team/ })
  const invite = page.getByRole('complementary', { name: 'Bring your people' })
  const game = page.getByRole('region', { name: 'This or That', exact: true })
  async function expectPanelsFirst() {
    const teamBox = await team.boundingBox()
    const inviteBox = await invite.boundingBox()
    const gameBox = await game.boundingBox()
    expect(teamBox!.y + teamBox!.height).toBeLessThanOrEqual(gameBox!.y)
    expect(inviteBox!.y + inviteBox!.height).toBeLessThanOrEqual(gameBox!.y)
    if (test.info().project.name === 'desktop')
      expect(Math.abs(teamBox!.y - inviteBox!.y)).toBeLessThanOrEqual(1)
    else expect(teamBox!.y + teamBox!.height).toBeLessThanOrEqual(inviteBox!.y)
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  }
  await expectPanelsFirst()
  if (test.info().project.name === 'desktop')
    await expect(page.getByRole('button', { name: 'Copy invite link' })).toBeInViewport()
  await page.getByRole('button', { name: 'Lock joining', exact: true }).focus()
  await page.keyboard.press('Tab')
  await expect(page.getByRole('button', { name: 'Start This or That' })).toBeFocused()
  await page.getByRole('button', { name: 'Browse prompts' }).click()
  await expect(page.getByText('200 prompts · 200 unused and enabled')).toBeVisible()
  await expect(team).toBeHidden()
  await expect(invite).toBeHidden()
  await page.getByRole('button', { name: /^Room \(/ }).click()
  await expectPanelsFirst()
  await page.getByRole('button', { name: 'Start This or That' }).click()
  await expect(page.getByRole('group', { name: 'Your choice' })).toBeVisible()
  await expect(team).toBeHidden()
  await page.getByRole('button', { name: /^Room \(/ }).click()
  await expect(team).toBeVisible()
  await expect(invite).toBeVisible()
  await expect(game).toBeHidden()
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([])
  await page.screenshot({
    path: `test-results/room-panels-first-${test.info().project.name}.png`,
    fullPage: true,
  })
})

test('game setup separates start and prompt browsing with a responsive gap', async ({ page }) => {
  await host(page)
  const actions = page.getByRole('group', { name: 'Game setup' })
  const start = actions.getByRole('button', { name: 'Start This or That' })
  const browse = actions.getByRole('button', { name: 'Browse prompts' })
  const startBox = (await start.boundingBox())!
  const browseBox = (await browse.boundingBox())!
  if (Math.abs(startBox.y - browseBox.y) < startBox.height)
    expect(browseBox.x - startBox.x - startBox.width).toBeGreaterThanOrEqual(16)
  else expect(browseBox.y - startBox.y - startBox.height).toBeGreaterThanOrEqual(16)
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  await browse.click()
  await expect(page.getByRole('heading', { name: 'Your prompt library' })).toBeVisible()
  await page.getByRole('button', { name: 'Game', exact: true }).click()
  await start.click()
  await expect(page.getByRole('group', { name: 'Your choice' })).toBeVisible()
})
