import AxeBuilder from '@axe-core/playwright'
import type { Page } from '@playwright/test'
import { test, expect } from './fixtures'

async function create(page: Page) {
  await page.goto('/host')
  await page.getByLabel('Your name').fill('Host')
  await page.getByRole('button', { name: 'Create room' }).click()
  await expect(page.getByRole('heading', { name: /Your team/ })).toBeVisible()
  const id = page.url().split('/').at(-1)!
  const room = (await (await page.request.get(`/api/rooms/${id}`)).json()) as { code: string }
  return { id, code: room.code }
}
async function login(page: Page) {
  await page.goto('/admin')
  await page.getByLabel('Administrator credential').fill(process.env.MINGLE_ADMIN_CREDENTIAL!)
  await page.getByLabel('Administrator credential').press('Enter')
  await expect(page.getByRole('button', { name: 'Sign out', exact: true })).toBeVisible()
}
test('administrator manages another browser room and connected players see forced controls and deletion', async ({
  page,
  browser,
  baseURL,
}) => {
  const context = await browser.newContext({
    baseURL,
    viewport: page.viewportSize(),
    reducedMotion: 'reduce',
  })
  const guestContext = await browser.newContext({
    baseURL,
    viewport: page.viewportSize(),
    reducedMotion: 'reduce',
  })
  try {
    const host = await context.newPage(),
      player = await guestContext.newPage()
    const room = await create(host)
    await player.goto(`/join?code=${room.code}`)
    await player.getByLabel('Your name').fill('Player')
    await player.getByRole('button', { name: 'Join room', exact: true }).click()
    await expect(host.getByText('Player', { exact: true })).toBeVisible()
    await host.getByRole('button', { name: /^Game/ }).click()
    await host.getByRole('button', { name: 'Start This or That', exact: true }).click()
    await expect(player.getByRole('group', { name: 'Your choice' })).toBeVisible()
    await page.goto('/admin')
    await page.getByLabel('Administrator credential').fill('wrong')
    await page.getByRole('button', { name: 'Sign in', exact: true }).click()
    await expect(page.getByRole('alert')).toContainText('not accepted')
    await login(page)
    await page.getByLabel('Room status').selectOption('playing')
    await page.getByRole('button', { name: `Manage ${room.code}` }).click()
    await page.getByRole('button', { name: 'Lock joining', exact: true }).click()
    await expect(page.getByRole('button', { name: 'Unlock joining', exact: true })).toBeVisible()
    await host.getByRole('button', { name: /^Room \(/ }).click()
    await expect(host.getByRole('button', { name: 'Unlock joining', exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'Unlock joining', exact: true }).click()
    await expect(page.getByRole('button', { name: 'Lock joining', exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'End game', exact: true }).click()
    await player.getByRole('button', { name: /^Game/ }).click()
    await expect(player.getByRole('heading', { name: 'Thanks for playing' })).toBeVisible()
    expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([])
    await page.screenshot({
      path: `test-results/admin-room-${test.info().project.name}.png`,
      fullPage: true,
    })
    await page.getByRole('button', { name: 'Make Player facilitator' }).click()
    await expect(page.getByText('Player · Facilitator', { exact: false })).toBeVisible()
    await page.getByRole('button', { name: 'Remove Host', exact: true }).click()
    await expect(host.getByRole('heading', { name: 'Your room is unavailable' })).toBeVisible()
    await page.getByRole('button', { name: 'Close room', exact: true }).click()
    await expect(player.getByText('This room is closed', { exact: false })).toBeVisible()
    await page.getByRole('button', { name: 'Delete room', exact: true }).click()
    const deletion = page.getByRole('region', { name: 'Confirm deletion' })
    await expect(deletion.getByRole('button', { name: 'Permanently delete room' })).toBeDisabled()
    await page.getByLabel('Enter room code to confirm').fill(room.code)
    await deletion.getByRole('button', { name: 'Permanently delete room' }).click()
    await expect(deletion).toHaveCount(0)
    await expect(page.getByRole('button', { name: `Manage ${room.code}` })).toHaveCount(0)
    expect((await player.request.get(`/api/rooms/${room.id}`)).status()).toBe(404)
    expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([])
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    await page.getByRole('button', { name: 'Sign out', exact: true }).click()
    await expect(page.getByRole('heading', { name: 'Administrator sign in' })).toBeVisible()
    expect((await page.request.get('/api/admin/rooms')).status()).toBe(401)
  } finally {
    await context.close()
    await guestContext.close()
  }
})
test('shared prompt authoring, pagination, safe retries and audit work across refresh and API restart', async ({
  page,
  backend,
  browser,
  baseURL,
}) => {
  test.setTimeout(150_000)
  const context = await browser.newContext({
    baseURL,
    viewport: page.viewportSize(),
    reducedMotion: 'reduce',
  })
  try {
    const host = await context.newPage()
    await create(host)
    await host.getByRole('button', { name: /^Game/ }).click()
    await host.getByRole('button', { name: 'Browse prompts', exact: true }).click()
    const shared = host
      .getByRole('list', { name: 'Available prompts' })
      .getByRole('listitem')
      .filter({ hasText: `Fly a kite ${test.info().project.name}` })
    await login(page)
    await page.getByRole('button', { name: 'Shared prompts', exact: true }).click()
    await expect(page.getByText(/Page 1 ·/)).toBeVisible()
    await page.getByRole('button', { name: 'Next page', exact: true }).click()
    await expect(page.getByText(/Page 2 ·/)).toBeVisible()
    await page.getByRole('button', { name: 'Create shared prompt', exact: true }).click()
    const editor = page.getByRole('form', { name: 'Create shared prompt' })
    await editor.getByLabel('Category', { exact: true }).fill('Admin browser')
    await editor.getByLabel('Tags, separated by commas').fill('browser, shared')
    await editor.getByLabel('Prompt text').fill(`Browser shared ${test.info().project.name}`)
    await editor
      .getByLabel('Option A', { exact: true })
      .fill(`Fly a kite ${test.info().project.name}`)
    await editor
      .getByLabel('Option B', { exact: true })
      .fill(`Build a raft ${test.info().project.name}`)
    expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([])
    let lost = false
    await page.route('**/api/admin/prompts', async (route) => {
      if (!lost && route.request().method() === 'POST') {
        lost = true
        const response = await route.fetch()
        expect(response.ok()).toBe(true)
        await route.abort('failed')
      } else await route.continue()
    })
    await editor.getByRole('button', { name: 'Save shared prompt' }).click()
    await expect(page.getByRole('button', { name: 'Retry same action' })).toBeVisible()
    await page.reload()
    await page.getByRole('button', { name: 'Retry same action' }).click()
    await expect(page.getByText('Change saved', { exact: true })).toBeVisible()
    await page.unroute('**/api/admin/prompts')
    await page
      .getByLabel('Search shared prompts')
      .fill(`Browser shared ${test.info().project.name}`)
    await page.getByRole('button', { name: 'Search', exact: true }).click()
    let row = page
      .getByRole('listitem')
      .filter({ hasText: `Browser shared ${test.info().project.name}` })
    await expect(row).toHaveCount(1)
    await expect(shared).toHaveCount(1)
    await expect(shared).toContainText('Shared prompt')
    await row.getByRole('button', { name: 'Disable', exact: true }).click()
    await expect(row.getByRole('button', { name: 'Enable', exact: true })).toBeVisible()
    await expect(shared).toHaveCount(0)
    await row.getByRole('button', { name: 'Enable', exact: true }).click()
    await expect(row.getByRole('button', { name: 'Disable', exact: true })).toBeVisible()
    await expect(shared).toHaveCount(1)
    await row.getByRole('button', { name: 'Edit', exact: true }).click()
    await page
      .getByRole('form', { name: 'Edit shared prompt' })
      .getByLabel('Category', { exact: true })
      .fill('Edited category')
    await page.getByRole('button', { name: 'Save shared prompt' }).click()
    await expect(row).toContainText('Edited category')
    await expect(shared).toContainText('Edited category')
    await backend.stop()
    await backend.start()
    await page.reload()
    await expect(page.getByRole('button', { name: 'Sign out', exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'Shared prompts', exact: true }).click()
    await page
      .getByLabel('Search shared prompts')
      .fill(`Browser shared ${test.info().project.name}`)
    await page.getByRole('button', { name: 'Search', exact: true }).click()
    row = page
      .getByRole('listitem')
      .filter({ hasText: `Browser shared ${test.info().project.name}` })
    await expect(row).toContainText('Edited category')
    await row.getByRole('button', { name: 'Delete', exact: true }).click()
    await page.getByRole('button', { name: 'Cancel deletion' }).click()
    await expect(row).toHaveCount(1)
    await row.getByRole('button', { name: 'Delete', exact: true }).click()
    await page.getByRole('button', { name: 'Confirm delete prompt' }).click()
    await expect(page.getByText('No prompts match this view')).toBeVisible()
    await page.getByRole('button', { name: 'Audit', exact: true }).click()
    await expect(page.getByRole('heading', { name: 'Audit trail' })).toBeVisible()
    await expect(page.getByText('prompt-delete', { exact: true }).first()).toBeVisible()
    await expect(
      page.getByText(`Browser shared ${test.info().project.name}`, { exact: true }),
    ).toHaveCount(0)
    expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([])
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    await page.screenshot({
      path: `test-results/admin-audit-${test.info().project.name}.png`,
      fullPage: true,
    })
  } finally {
    await context.close()
  }
})
