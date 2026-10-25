import AxeBuilder from '@axe-core/playwright'
import { test, expect } from './fixtures'
import type { Page } from '@playwright/test'
import type { RoomSnapshot } from '../src/api/rooms'
async function host(page: Page) {
  await page.goto('/host')
  await page.getByLabel('Your name').fill('Prompt host')
  await page.getByRole('button', { name: 'Create room' }).click()
  await expect(page.getByText('Connected', { exact: true })).toBeVisible()
  return page.url().split('/').at(-1)!
}
async function snapshot(page: Page, id: string): Promise<RoomSnapshot> {
  return (await (await page.request.get(`/api/rooms/${id}`)).json()) as RoomSnapshot
}
async function open(page: Page) {
  await page.getByRole('button', { name: 'Browse prompts' }).click()
  await expect(page.getByText('200 prompts · 200 unused and enabled')).toBeVisible()
}
async function custom(page: Page) {
  await page.getByRole('button', { name: 'Add room prompt' }).click()
  await page.getByLabel('Option A', { exact: true }).fill('Build a kite')
  await page.getByLabel('Option B', { exact: true }).fill('Build a birdhouse')
  await page.getByLabel('Prompt category', { exact: true }).fill('Our team')
  await page.getByLabel('Tags separated by commas').fill('weekend, creative')
  await page.getByRole('button', { name: 'Save prompt', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'New room prompt' })).toHaveCount(0)
  await page.getByRole('combobox', { name: 'Category', exact: true }).selectOption('Our team')
  await expect(page.getByText('1 prompt · 1 unused and enabled')).toBeVisible()
}

test('facilitator browses launch content, filters and manually selects a built-in prompt', async ({
  page,
  browser,
  baseURL,
}) => {
  const context = await browser.newContext({ baseURL, viewport: page.viewportSize() })
  try {
    const id = await host(page)
    const room = await snapshot(page, id)
    const player = await context.newPage()
    await player.goto(`/join?code=${room.code}`)
    await player.getByLabel('Your name').fill('Player')
    await player.getByRole('button', { name: 'Join room' }).click()
    await expect(player.getByText('Connected', { exact: true })).toBeVisible()
    await expect(player.getByRole('button', { name: 'Browse prompts' })).toHaveCount(0)
    await page.route(`**/api/rooms/${id}/prompts`, (route) => route.abort(), { times: 1 })
    await page.getByRole('button', { name: 'Browse prompts' }).click()
    await expect(page.getByRole('alert')).toHaveText(/could not be loaded/)
    await page.getByRole('button', { name: 'Browse prompts' }).click()
    await open(page)
    await expect(page.getByRole('alert')).toHaveCount(0)
    await page.getByRole('combobox', { name: 'Category', exact: true }).selectOption('Food')
    await page.getByRole('combobox', { name: 'Tag', exact: true }).selectOption('preferences')
    await expect(page.getByText('25 prompts · 25 unused and enabled')).toBeVisible()
    await page.getByLabel('Search prompts').fill('coffee')
    await expect(page.getByText('1 prompt · 1 unused and enabled')).toBeVisible()
    expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([])
    await page.getByRole('button', { name: 'Use this prompt', exact: true }).focus()
    await page.keyboard.press('Enter')
    await expect(player.getByRole('button', { name: 'Tea', exact: true })).toBeVisible()
    expect((await snapshot(page, id)).game!.optionB).toBe('Coffee')
    await page.getByRole('button', { name: 'Reveal answers' }).click()
    await expect(
      page.getByText('The selected prompt pool is exhausted. Reset it or browse another category.'),
    ).toBeVisible()
    await page.getByRole('button', { name: 'Browse prompts' }).click()
    await page.getByRole('button', { name: 'Reset pool and draw', exact: true }).click()
    await expect(page.getByText('Round 2 · Make your choice')).toBeVisible()
    expect((await snapshot(page, id)).game!.optionB).toBe('Coffee')
  } finally {
    await context.close()
  }
})

test('room authoring validates, survives restart and preserves round text through edits and deletion', async ({
  page,
  backend,
}) => {
  const id = await host(page)
  await open(page)
  await page.getByRole('button', { name: 'Add room prompt' }).click()
  await page.getByLabel('Option A', { exact: true }).fill('Same')
  await page.getByLabel('Option B', { exact: true }).fill('same')
  await page.getByRole('button', { name: 'Save prompt', exact: true }).click()
  await expect(page.getByRole('alert')).toHaveText('The options must be different')
  await page.getByRole('button', { name: 'Cancel editing' }).click()
  await custom(page)
  await page.reload()
  await expect(page.getByText('Connected', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Browse prompts' }).click()
  await page.getByRole('combobox', { name: 'Category', exact: true }).selectOption('Our team')
  await page.getByRole('button', { name: 'Use this prompt', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Build a kite', exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Edit prompt', exact: true }).click()
  await page.getByLabel('Option A', { exact: true }).fill('Paint a kite')
  await page.getByRole('button', { name: 'Save prompt', exact: true }).click()
  await expect(page.getByText('Paint a kite / Build a birdhouse')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Build a kite', exact: true })).toBeVisible()
  await backend.stop()
  await expect(page.getByText('Reconnecting…', { exact: true })).toBeVisible()
  await backend.start()
  await page.getByRole('button', { name: 'Retry connection' }).click()
  await expect(page.getByText('Connected', { exact: true })).toBeVisible()
  expect((await snapshot(page, id)).game!.optionA).toBe('Build a kite')
  await page.getByRole('button', { name: 'Reveal answers' }).click()
  await page.getByRole('button', { name: 'Browse prompts' }).click() // Close library for the shared reset control.
  await page.getByRole('button', { name: 'Reset pool and draw', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Paint a kite', exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Browse prompts' }).click()
  await page.getByRole('button', { name: 'Delete prompt', exact: true }).click()
  await page.getByRole('button', { name: 'Confirm delete', exact: true }).click()
  await expect(page.getByText('Paint a kite / Build a birdhouse')).toHaveCount(0)
  await expect(page.getByRole('combobox', { name: 'Category', exact: true })).toHaveValue(
    'Our team',
  )
  await expect(page.getByRole('button', { name: 'Paint a kite', exact: true })).toBeVisible()
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([])
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  await page.screenshot({
    path: `test-results/prompt-authoring-${test.info().project.name}.png`,
    fullPage: true,
  })
})

test('random drawing respects filters, avoids repetition and lets future-game prompts be prepared', async ({
  page,
}) => {
  const id = await host(page)
  await open(page)
  await page.getByRole('combobox', { name: 'Game', exact: true }).selectOption('would-you-rather')
  await expect(page.getByText('50 prompts · 50 unused and enabled')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Draw random prompt', exact: true })).toBeDisabled()
  await page.getByRole('combobox', { name: 'Game', exact: true }).selectOption('this-or-that')
  await page.getByRole('combobox', { name: 'Category', exact: true }).selectOption('Technology')
  await page.getByRole('button', { name: 'Draw random prompt', exact: true }).click()
  await expect(page.getByText('Round 1 · Make your choice')).toBeVisible()
  await expect(page.getByText('25 prompts · 24 unused and enabled')).toBeVisible()
  const first = (await snapshot(page, id)).game!
  await page.getByRole('button', { name: 'Reveal answers' }).click()
  await page.getByRole('button', { name: 'Draw random prompt', exact: true }).click()
  await expect(page.getByText('Round 2 · Make your choice')).toBeVisible()
  const second = (await snapshot(page, id)).game!
  expect(second.optionA).not.toBe(first.optionA)
  expect(second.remainingPrompts).toBe(23)
})

test('ambiguous prompt saves retry once and edits from another tab reject stale revisions', async ({
  page,
}) => {
  const id = await host(page)
  await open(page)
  let first = true
  const commands: unknown[] = []
  await page.route(`**/api/rooms/${id}/prompts`, async (route) => {
    if (route.request().method() !== 'POST') {
      await route.continue()
      return
    }
    commands.push(route.request().postDataJSON())
    const response = await route.fetch()
    if (first) {
      first = false
      await route.fulfill({ status: 502, contentType: 'application/json', body: '{}' })
    } else await route.fulfill({ response })
  })
  await page.getByRole('button', { name: 'Add room prompt' }).click()
  await page.getByLabel('Option A', { exact: true }).fill('Paper sculptures')
  await page.getByLabel('Option B', { exact: true }).fill('Clay sculptures')
  await page.getByRole('button', { name: 'Save prompt', exact: true }).click()
  await page.getByRole('button', { name: 'Retry prompt change' }).click()
  await expect(page.getByRole('button', { name: 'Retry prompt change' })).toHaveCount(0)
  expect(commands).toHaveLength(2)
  expect(commands[0]).toEqual(commands[1])
  await page
    .getByRole('combobox', { name: 'Category', exact: true })
    .selectOption('Team favourites')
  await page.getByRole('button', { name: 'Edit prompt', exact: true }).click()
  await page.getByLabel('Option A', { exact: true }).fill('My local draft')
  const other = await page.context().newPage()
  try {
    await other.goto(`/rooms/${id}`)
    await expect(other.getByText('Connected', { exact: true })).toBeVisible()
    await other.getByRole('button', { name: 'Browse prompts' }).click()
    await other
      .getByRole('combobox', { name: 'Category', exact: true })
      .selectOption('Team favourites')
    await other.getByRole('button', { name: 'Edit prompt', exact: true }).click()
    await other.getByLabel('Option A', { exact: true }).fill('Updated in another tab')
    await other.getByRole('button', { name: 'Save prompt', exact: true }).click()
    await expect(page.getByText('Updated in another tab / Clay sculptures')).toBeVisible()
    await page.getByRole('button', { name: 'Save prompt', exact: true }).click()
    await expect(page.getByRole('alert')).toHaveText('The prompt changed; review it and try again')
    await expect(page.getByLabel('Option A', { exact: true })).toHaveValue('My local draft')
  } finally {
    await other.close()
  }
})

test('a confirmed save survives a failed library refresh without inviting another mutation', async ({
  page,
}) => {
  const id = await host(page)
  await open(page)
  await custom(page)
  await page.getByRole('button', { name: 'Edit prompt', exact: true }).click()
  await page.getByLabel('Option A', { exact: true }).fill('Fold a paper kite')
  await page.route(`**/api/rooms/${id}/prompts`, async (route) => {
    if (route.request().method() === 'GET') await route.abort()
    else await route.continue()
  })
  await page.getByRole('button', { name: 'Save prompt', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Edit room prompt' })).toHaveCount(0)
  await expect(page.getByRole('alert')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Retry prompt change' })).toHaveCount(0)
  await page.unroute(`**/api/rooms/${id}/prompts`)
  await page.getByRole('button', { name: 'Browse prompts' }).click()
  await page.getByRole('button', { name: 'Browse prompts' }).click()
  await expect(page.getByText('Fold a paper kite / Build a birdhouse')).toBeVisible()
  await expect(page.getByRole('alert')).toHaveCount(0)
  const response = await page.request.get(`/api/rooms/${id}/prompts?search=birdhouse`)
  expect((await response.json()).prompts).toHaveLength(1)
})

test('prompt list has compact desktop rows and supports keyboard and pointer scrolling', async ({
  page,
}) => {
  await host(page)
  await open(page)
  const region = page.getByRole('region', { name: 'Available prompts', exact: true })
  const list = region.getByRole('list', { name: 'Available prompts', exact: true })
  await expect(list).toHaveAttribute('tabindex', '0')
  await expect(region.getByText('Scroll for more ↓')).toBeVisible()
  const dimensions = await list.evaluate((element) => ({
    height: element.clientHeight,
    content: element.scrollHeight,
  }))
  expect(dimensions.content).toBeGreaterThan(dimensions.height)
  const row = list.getByRole('listitem').first()
  const title = await row.locator('.prompt-result-title').boundingBox()
  const metadata = await row.locator('.prompt-result-meta').boundingBox()
  const action = await row.getByRole('button', { name: 'Use this prompt' }).boundingBox()
  if (test.info().project.name === 'desktop') {
    expect(title!.x + title!.width).toBeLessThan(metadata!.x)
    expect(metadata!.x + metadata!.width).toBeLessThan(action!.x)
    expect((await row.boundingBox())!.height).toBeLessThanOrEqual(64)
    expect(
      Math.floor(dimensions.height / (await row.boundingBox())!.height),
    ).toBeGreaterThanOrEqual(8)
  } else {
    expect(title!.y + title!.height).toBeLessThanOrEqual(metadata!.y)
    expect(metadata!.y + metadata!.height).toBeLessThanOrEqual(action!.y)
  }
  await list.focus()
  await page.keyboard.press('PageDown')
  await expect.poll(() => list.evaluate((element) => element.scrollTop)).toBeGreaterThan(0)
  // Wait for native keyboard scrolling to settle before checking pointer scrolling.
  await page.keyboard.press('End')
  await expect
    .poll(() =>
      list.evaluate(
        (element) => Math.ceil(element.scrollTop + element.clientHeight) >= element.scrollHeight,
      ),
    )
    .toBe(true)
  const before = await list.evaluate((element) => element.scrollTop)
  await list.hover()
  await page.mouse.wheel(0, -250)
  await expect.poll(() => list.evaluate((element) => element.scrollTop)).toBeLessThan(before)
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([])
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  await page.screenshot({
    path: `test-results/prompt-list-${test.info().project.name}.png`,
    fullPage: true,
  })
})
