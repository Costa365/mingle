import AxeBuilder from '@axe-core/playwright'
import type { Page } from '@playwright/test'
import { test, expect } from './fixtures'
import type { RoomSnapshot } from '../src/api/rooms'
async function snapshot(page: Page) {
  return (await (
    await page.request.get(`/api/rooms/${new URL(page.url()).pathname.split('/').at(-1)}`)
  ).json()) as RoomSnapshot
}
async function expectSummaryChoices(page: Page, expected: Record<string, string>) {
  const choices = page.getByRole('list', { name: 'Player choices for round 1', exact: true })
  await expect(choices.getByRole('listitem')).toHaveCount(Object.keys(expected).length)
  for (const [name, choice] of Object.entries(expected)) {
    const row = choices
      .getByRole('listitem')
      .filter({ has: page.locator('strong', { hasText: new RegExp(`^${name}$`) }) })
    await expect(row.locator('span')).toHaveText(choice)
  }
}
async function create(page: Page) {
  await page.goto('/host')
  await page.getByLabel('Your name').fill('Host')
  await page.getByRole('button', { name: 'Create room' }).click()
  await expect(page.getByText('Connected', { exact: true })).toBeVisible()
  return snapshot(page)
}
async function join(page: Page, code: string) {
  await page.goto(`/join?code=${code}`)
  await page.getByLabel('Your name').fill('Player')
  await page.getByRole('button', { name: 'Join room' }).click()
  await expect(page.getByText('Connected', { exact: true })).toBeVisible()
}
async function start(page: Page, gameId: string) {
  await page.getByLabel('Choose a game').selectOption(gameId)
  await page.getByRole('button', { name: /^Start / }).click()
  await expect(
    page.getByText(/has started|Private statement collection/, { exact: false }),
  ).toBeVisible()
}
async function statements(page: Page, prefix: string) {
  for (let i = 0; i < 3; i++)
    await page
      .getByLabel(`Statement ${i + 1}`, { exact: true })
      .fill(`${prefix} statement ${i + 1}`)
  await page.getByRole('radio', { name: 'Statement 3 is my lie' }).check()
  await page.getByRole('button', { name: 'Save statements' }).click()
  await expect(
    page.getByText('Your private response is saved. You can edit until collection closes.'),
  ).toBeVisible()
}

test('all five games complete in one room with private binary answers and anonymous tied nominations', async ({
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
    const room = await create(page),
      player = await context.newPage()
    await join(player, room.code)
    const originalIdentity = (await snapshot(player)).youId
    for (const id of ['this-or-that', 'would-you-rather', 'never-have-i-ever', 'most-likely-to']) {
      await start(page, id)
      await expect(player.getByRole('group', { name: 'Your choice' })).toBeVisible()
      if (id === 'most-likely-to') {
        await expect(page.getByRole('button', { name: 'Host', exact: true })).toHaveCount(0)
        await page.getByRole('button', { name: 'Player', exact: true }).click()
        await player.getByRole('button', { name: 'Host', exact: true }).click()
      } else {
        if (id === 'never-have-i-ever') {
          await expect(page.getByRole('button', { name: 'I have', exact: true })).toBeVisible()
          await expect(player.getByRole('button', { name: "I haven't", exact: true })).toBeVisible()
        }
        await page.getByRole('group', { name: 'Your choice' }).getByRole('button').first().click()
        await player.getByRole('button', { name: 'Pass this round' }).click()
        await expect(player.getByRole('button', { name: 'Pass this round' })).toHaveAttribute(
          'aria-pressed',
          'true',
        )
        await player.getByRole('group', { name: 'Your choice' }).getByRole('button').nth(1).click()
      }
      await expect(page.getByText('2 of 2 responded')).toBeVisible()
      expect((await snapshot(page)).game?.results).toEqual([])
      const played = (await snapshot(page)).game!
      await page.getByRole('button', { name: 'Reveal answers', exact: true }).click()
      if (id === 'most-likely-to') {
        await expect(player.getByText(/share the lead/)).toBeVisible()
        expect((await snapshot(player)).game!.results[0].choices).toEqual([])
      } else {
        await expectSummaryChoices(page, { Host: played.optionA, Player: played.optionB })
        await expectSummaryChoices(player, { Host: played.optionA, Player: played.optionB })
        await player.reload()
        await expectSummaryChoices(player, { Host: played.optionA, Player: played.optionB })
        await player.screenshot({
          path: `test-results/reveal-${id}-${test.info().project.name}.png`,
          fullPage: true,
        })
      }
      expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([])
      await page.getByRole('button', { name: 'Next random prompt', exact: true }).click()
      await page.getByRole('button', { name: 'Skip round', exact: true }).click()
      await page.getByRole('button', { name: 'Finish game', exact: true }).click()
      await expect(player.getByRole('heading', { name: 'Thanks for playing' })).toBeVisible()
      if (id === 'most-likely-to') {
        await expect(player.getByRole('list', { name: /Player choices/ })).toHaveCount(0)
        expect((await snapshot(player)).game!.results[0].choices).toEqual([])
      } else {
        await expectSummaryChoices(player, { Host: played.optionA, Player: played.optionB })
        await expect(player.getByRole('list', { name: 'Player choices for round 2' })).toHaveCount(
          0,
        )
        if (id === 'never-have-i-ever')
          await expect(player.getByText(played.prompt, { exact: true })).toBeVisible()
        await player.reload()
        await expectSummaryChoices(player, { Host: played.optionA, Player: played.optionB })
        expect((await new AxeBuilder({ page: player }).analyze()).violations).toEqual([])
        expect(
          await player.evaluate(() => document.documentElement.scrollWidth <= innerWidth),
        ).toBe(true)
        await player.screenshot({
          path: `test-results/summary-${id}-${test.info().project.name}.png`,
          fullPage: true,
        })
      }
      await page.getByRole('button', { name: 'Return to lobby', exact: true }).click()
      expect((await snapshot(player)).youId).toBe(originalIdentity)
    }
    await start(page, 'two-truths-and-a-lie')
    await expect(player.getByRole('group', { name: 'Facilitator controls' })).toHaveCount(0)
    await statements(page, 'Host')
    await statements(player, 'Player')
    expect((await new AxeBuilder({ page: player }).analyze()).violations).toEqual([])
    await player.screenshot({
      path: `test-results/m4-collection-${test.info().project.name}.png`,
      fullPage: true,
    })
    await page.getByRole('button', { name: 'Close collection' }).click()
    await expect(page.getByRole('group', { name: 'Your choice' })).toBeVisible()
    const game = (await snapshot(page)).game!,
      hostId = (await snapshot(page)).youId
    const presenter = game.presenter!.participantId === hostId ? page : player,
      guesser = presenter === page ? player : page
    await expect(presenter.getByText(/This is your turn/)).toBeVisible()
    await expect(
      presenter.getByRole('group', { name: 'Your choice' }).getByRole('button').first(),
    ).toBeDisabled()
    await expect(guesser.getByText(' · This was the lie', { exact: true })).toHaveCount(0)
    await guesser.getByRole('group', { name: 'Your choice' }).getByRole('button').nth(2).focus()
    await guesser.keyboard.press('Enter')
    await expect(guesser.getByText(/Your answer is saved/)).toBeVisible()
    await page.getByRole('button', { name: 'Reveal answers', exact: true }).click()
    await expect(player.getByText('· This was the lie', { exact: true })).toBeVisible()
    expect((await snapshot(player)).game!.results[0].lieIndex).toBe(2)
    await expectSummaryChoices(page, {
      [guesser === page ? 'Host' : 'Player']: game.presenter!.statements[2],
    })
    await expectSummaryChoices(player, {
      [guesser === page ? 'Host' : 'Player']: game.presenter!.statements[2],
    })
    await player.reload()
    await expectSummaryChoices(player, {
      [guesser === page ? 'Host' : 'Player']: game.presenter!.statements[2],
    })
    await page.getByRole('button', { name: 'Next round', exact: true }).click()
    await expect(player.getByText('Round 2 · Make your choice')).toBeVisible()
    await page.getByRole('button', { name: 'Finish game', exact: true }).click()
    await expect(player.getByRole('heading', { name: 'Thanks for playing' })).toBeVisible()
    expect((await snapshot(player)).game!.results[1].lieIndex).toBeNull()
    const namedGuesses = player.getByRole('list', {
      name: 'Player choices for round 1',
      exact: true,
    })
    await expect(namedGuesses).toHaveText(
      `${guesser === page ? 'Host' : 'Player'}${game.presenter!.statements[2]}`,
    )
    await expect(player.getByRole('list', { name: 'Player choices for round 2' })).toHaveCount(0)
    await player.reload()
    await expect(namedGuesses).toHaveText(
      `${guesser === page ? 'Host' : 'Player'}${game.presenter!.statements[2]}`,
    )
    expect((await new AxeBuilder({ page: player }).analyze()).violations).toEqual([])
    expect(await player.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
      true,
    )
    await player.screenshot({
      path: `test-results/m4-summary-${test.info().project.name}.png`,
      fullPage: true,
    })
    await page.getByRole('button', { name: 'Return to lobby', exact: true }).click()
    expect((await snapshot(player)).youId).toBe(originalIdentity)
  } finally {
    await context.close()
  }
})

test('private statements and guesses survive API restarts and empty collection can reopen', async ({
  page,
  browser,
  baseURL,
  backend,
}) => {
  test.setTimeout(120_000)
  const context = await browser.newContext({ baseURL, viewport: page.viewportSize() })
  try {
    const room = await create(page),
      player = await context.newPage()
    await join(player, room.code)
    await start(page, 'two-truths-and-a-lie')
    await page.getByRole('button', { name: 'Sit this game out' }).click()
    await expect(page.getByText(/You are sitting this game out/)).toBeVisible()
    await page.getByRole('button', { name: 'Close collection' }).click()
    await expect(page.getByText(/No one is ready to present/)).toBeVisible()
    await page.getByRole('button', { name: 'Reopen collection' }).click()
    await statements(player, 'Private')
    const other = await context.newPage()
    await other.goto(player.url())
    await expect(other.getByLabel('Statement 1', { exact: true })).toHaveValue(
      'Private statement 1',
    )
    await player.getByLabel('Statement 1', { exact: true }).fill('My local draft')
    await statements(other, 'Private newer')
    await expect(player.getByText(/Your saved statements changed in another tab/)).toBeVisible()
    await player.getByRole('button', { name: 'Save statements', exact: true }).click()
    await expect(player.getByRole('alert')).toHaveText('Your statements changed in another tab')
    await expect(player.getByLabel('Statement 1', { exact: true })).toHaveValue('My local draft')
    await player.getByRole('button', { name: 'Use saved statements', exact: true }).click()
    await expect(player.getByLabel('Statement 1', { exact: true })).toHaveValue(
      'Private newer statement 1',
    )
    await other.close()
    expect((await snapshot(page)).game!.ownSubmission).toBeNull()
    expect(JSON.stringify((await snapshot(page)).game)).not.toContain('Private statement')
    async function recover() {
      const before = (await snapshot(player)).game
      await backend.stop()
      try {
        await expect(player.getByText('Reconnecting…', { exact: true })).toBeVisible()
      } finally {
        await backend.start()
      }
      await expect(player.getByText('Connected', { exact: true })).toBeVisible({ timeout: 25_000 })
      await expect(page.getByText('Connected', { exact: true })).toBeVisible({ timeout: 25_000 })
      expect((await snapshot(player)).game).toEqual(before)
    }
    await recover()
    await page.getByRole('button', { name: 'Close collection' }).click()
    await page.getByRole('group', { name: 'Your choice' }).getByRole('button').first().click()
    await expect(page.getByText(/Your answer is saved/)).toBeVisible()
    await recover()
    await page.getByRole('button', { name: 'Reveal answers', exact: true }).click()
    await expect(player.getByText('· This was the lie', { exact: true })).toBeVisible()
    await recover()
    await page.getByRole('button', { name: 'Finish game', exact: true }).click()
    await page.getByRole('button', { name: 'Return to lobby', exact: true }).click()
    await start(page, 'most-likely-to')
    await page.getByRole('button', { name: /^Room \(/ }).click()
    await page.getByRole('button', { name: 'Remove Player', exact: true }).click()
    await page.getByRole('button', { name: 'Return to game →' }).click()
    await expect(
      page.getByText('This round needs two players. Skip this round or finish the game.', {
        exact: true,
      }),
    ).toBeVisible()
    await expect(page.getByRole('button', { name: 'Pass this round' })).toBeDisabled()
    await page.getByRole('button', { name: 'Skip round', exact: true }).click()
    await page.getByRole('button', { name: 'Finish game', exact: true }).click()
  } finally {
    await context.close()
  }
})
