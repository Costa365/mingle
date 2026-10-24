import AxeBuilder from '@axe-core/playwright'
import type { Page } from '@playwright/test'
import { test, expect } from './fixtures'

async function expectHeadingsWithoutFullStops(page: Page) {
  for (const heading of await page.getByRole('heading').allTextContents()) {
    expect(heading).not.toContain('.')
  }
}

const games = [
  ['this-or-that', 'This or That'],
  ['two-truths-and-a-lie', 'Two Truths and a Lie'],
  ['never-have-i-ever', 'Never Have I Ever'],
  ['would-you-rather', 'Would You Rather?'],
  ['most-likely-to', 'Most Likely To'],
]

test('home discovers the real API catalogue, fits the viewport, and is accessible', async ({
  page,
}) => {
  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'This or That' })).toBeVisible()
  const brand = page.getByRole('link', { name: 'Mingle Team games home' })
  await expect(brand.getByText('Mingle', { exact: true })).toBeVisible()
  await expect(brand.getByText('Team games', { exact: true })).toBeVisible()
  await expect(page).toHaveTitle('Mingle — Team games')
  await expect(
    page.getByRole('heading', { name: 'Make room for a little play', exact: true }),
  ).toBeVisible()
  const icons = page.locator('img[src="/people.svg"]')
  await expect(icons).toHaveCount(3)
  for (const icon of await icons.all()) {
    await expect(icon).toBeVisible()
    await expect(icon).toHaveAttribute('alt', '')
    expect(
      await icon.evaluate((element) => (element as HTMLImageElement).naturalWidth),
    ).toBeGreaterThan(0)
  }
  await expectHeadingsWithoutFullStops(page)
  const cards = page.getByRole('region', { name: 'Small games, big connections' }).getByRole('link')
  await expect(cards).toHaveCount(5)
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  )
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([])
  await page.keyboard.press('Tab')
  await expect(page.getByRole('link', { name: 'Skip to content' })).toBeFocused()
  await page.keyboard.press('Enter')
  await expect(page.getByRole('main')).toBeFocused()
  await page.screenshot({
    path: `test-results/home-${test.info().project.name}.png`,
    fullPage: true,
  })
})

for (const [id, name] of games) {
  test(`${name}: catalogue link, direct route, reload, and return`, async ({ page }) => {
    await page.goto('/')
    await page
      .getByRole('link')
      .filter({ has: page.getByRole('heading', { name, exact: true }) })
      .click()
    await expect(page).toHaveURL(new RegExp(`/games/${id}$`))
    await expect(page.getByRole('heading', { name, level: 1, exact: true })).toBeVisible()
    await expect(page).toHaveTitle(`${name} — Mingle`)
    await expectHeadingsWithoutFullStops(page)
    await expect(page.getByText('Gameplay is coming soon', { exact: true })).toBeVisible()
    await page.reload()
    await expect(page.getByRole('heading', { name, level: 1, exact: true })).toBeVisible()
    expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([])
    await page.getByRole('link', { name: 'All games' }).click()
    await expect(page.getByRole('heading', { name: /Make room/ })).toBeVisible()
  })
}

test('how-to page works by navigation and direct loading', async ({ page }) => {
  await page.goto('/')
  await page.getByRole('link', { name: 'How it works', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Bring your people' })).toBeVisible()
  await page.reload()
  await expect(page.getByRole('listitem')).toHaveCount(3)
  await expect(page).toHaveTitle('How it works — Mingle')
  await expectHeadingsWithoutFullStops(page)
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([])
  await page.getByRole('link', { name: /Explore the games/ }).click()
  await expect(page.getByRole('heading', { name: 'This or That' })).toBeVisible()
})

test('unknown browser and game routes have a recovery link', async ({ page }) => {
  for (const route of ['/missing', '/games/unknown']) {
    await page.goto(route)
    await expect(page.getByRole('heading', { name: /isn't.*in the deck/ })).toBeVisible()
    await expect(page).toHaveTitle('Page not found — Mingle')
    await expectHeadingsWithoutFullStops(page)
    await page.getByRole('link', { name: 'Back to the games' }).click()
    await expect(page.getByRole('heading', { name: 'This or That' })).toBeVisible()
  }
})

test('a delayed real API response displays a loading announcement', async ({ page }) => {
  let release!: () => void
  const gate = new Promise<void>((resolve) => {
    release = resolve
  })
  await page.route('**/api/games', async (route) => {
    await gate
    await route.continue()
  })
  try {
    await page.goto('/')
    await expect(page.getByRole('status')).toHaveText(/Gathering the games/)
  } finally {
    release()
  }
  await expect(page.getByRole('heading', { name: 'This or That' })).toBeVisible()
})

test('a real API outage offers retry and recovers after restart', async ({ page, backend }) => {
  await backend.stop()
  try {
    await page.goto('/')
    // A stopped container can leave the proxy resolving its address. Allow the
    // client's 10-second request deadline to elapse before expecting the alert.
    await expect(page.getByRole('alert')).toHaveText(/couldn't load the games/, { timeout: 15_000 })
    await expectHeadingsWithoutFullStops(page)
  } finally {
    await backend.start()
  }
  await page.getByRole('button', { name: 'Try again' }).click()
  await expect(page.getByRole('heading', { name: 'This or That' })).toBeVisible()
  await expect(page.getByRole('alert')).toHaveCount(0)
})
