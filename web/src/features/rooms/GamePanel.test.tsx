import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { GamePanel } from './GamePanel'
import { commandGame, RoomError } from '../../api/rooms'
import type { GameView } from '../../api/rooms'
import { room as snapshot } from '../../test/room'

vi.mock('../../api/rooms', async (load) => ({
  ...(await load<typeof import('../../api/rooms')>()),
  commandGame: vi.fn(),
}))
const game: GameView = {
  sessionId: '50000000-0000-0000-0000-000000000001',
  gameId: 'this-or-that',
  roundId: '50000000-0000-0000-0000-000000000002',
  roundNumber: 1,
  stageRevision: 1,
  stage: 'responding',
  prompt: 'Which would you choose?',
  optionA: 'Tea',
  optionB: 'Coffee',
  eligibleCount: 2,
  responseCount: 0,
  eligible: true,
  ownChoice: null,
  responseRevision: 0,
  allowedActions: ['answer', 'reveal', 'skip', 'finish'],
  results: [],
}
const accept = vi.fn()
const refresh = vi.fn().mockResolvedValue(undefined)
function show(overrides: Partial<GameView> = {}, disabled = false, participant = false) {
  return render(
    <GamePanel
      snapshot={{
        ...snapshot,
        youId: participant ? snapshot.participants[1].id : snapshot.youId,
        status: 'playing',
        game: { ...game, ...overrides },
      }}
      disabled={disabled}
      accept={accept}
      refresh={refresh}
    />,
  )
}
beforeEach(() => vi.clearAllMocks())
describe('game controls', () => {
  it('offers starting to the facilitator and waiting to participants', () => {
    const view = render(
      <GamePanel snapshot={snapshot} disabled={false} accept={accept} refresh={refresh} />,
    )
    expect(screen.getByRole('button', { name: 'Start This or That' })).toBeEnabled()
    view.rerender(
      <GamePanel
        snapshot={{ ...snapshot, youId: snapshot.participants[1].id }}
        disabled={false}
        accept={accept}
        refresh={refresh}
      />,
    )
    expect(screen.queryByRole('button', { name: 'Start This or That' })).not.toBeInTheDocument()
    expect(screen.getByText(/Waiting for your facilitator/)).toBeInTheDocument()
  })
  it.each([
    ['Tea', 'a'],
    ['Coffee', 'b'],
    ['Pass this round', 'pass'],
  ])('submits %s with independent revisions', async (label, choice) => {
    vi.mocked(commandGame).mockResolvedValue({ snapshot, appliedVersion: 2, replayed: false })
    show()
    fireEvent.click(screen.getByRole('button', { name: label }))
    await waitFor(() => expect(accept).toHaveBeenCalledWith(snapshot))
    expect(commandGame).toHaveBeenCalledWith(
      snapshot.id,
      expect.objectContaining({
        action: 'answer',
        choice,
        stageRevision: 1,
        responseRevision: 0,
        sessionId: game.sessionId,
        roundId: game.roundId,
      }),
    )
  })
  it('shows the saved choice and allows editing without publishing others', () => {
    show({ ownChoice: 'b', responseRevision: 3, responseCount: 1 })
    expect(screen.getByRole('button', { name: 'Coffee' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByText(/1 of 2 responded/)).toBeInTheDocument()
    expect(screen.getByText(/Your answer is saved/)).toBeInTheDocument()
    expect(screen.queryByLabelText('Round results')).not.toBeInTheDocument()
  })
  it('shows named response status without disclosing a pass, and separates late arrivals', () => {
    show({
      ownChoice: 'pass',
      eligibleCount: 1,
      responseCount: 1,
      responses: [{ participantId: snapshot.youId, hasResponded: true }],
    })
    const responses = screen.getByRole('list', { name: 'Participant responses' })
    expect(responses).toHaveTextContent('Host (you)Answered')
    expect(responses).not.toHaveTextContent(/pass/i)
    expect(screen.getByText('Joining next round: Player')).toBeVisible()
    expect(screen.getByText('You passed. You can change it until reveal.')).toBeVisible()
    expect(screen.getByRole('button', { name: 'Pass this round' })).toHaveClass('pass-button')
    expect(
      screen.getByRole('group', { name: 'Your choice' }).querySelectorAll('button'),
    ).toHaveLength(2)
    expect(screen.queryByText('You can always pass')).not.toBeInTheDocument()
  })
  it('labels away eligible players as waiting without removing them from progress', () => {
    render(
      <GamePanel
        snapshot={{
          ...snapshot,
          participants: snapshot.participants.map((p) => ({ ...p, isOnline: false })),
          game: {
            ...game,
            responses: snapshot.participants.map((p) => ({
              participantId: p.id,
              hasResponded: false,
            })),
          },
        }}
        disabled={false}
        accept={accept}
        refresh={refresh}
      />,
    )
    expect(screen.getByRole('list', { name: 'Participant responses' })).toHaveTextContent(
      'Player · AwayWaiting',
    )
  })
  it('disables input while unsynchronized and for late arrivals', () => {
    const view = show({}, true)
    expect(screen.getByRole('button', { name: 'Tea' })).toBeDisabled()
    view.unmount()
    show({ eligible: false, allowedActions: [] })
    expect(screen.getByRole('button', { name: 'Tea' })).toBeDisabled()
    expect(screen.getByText(/You joined during this round/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Reveal answers' })).not.toBeInTheDocument()
    expect(screen.queryByRole('group', { name: 'Facilitator controls' })).not.toBeInTheDocument()
  })
  it.each([
    ['Reveal answers', 'reveal'],
    ['Skip round', 'skip'],
    ['Finish game', 'finish'],
  ])('sends %s once while busy', async (label, action) => {
    vi.mocked(commandGame).mockReturnValue(new Promise(() => {}))
    show()
    fireEvent.click(screen.getByRole('button', { name: label }))
    fireEvent.click(screen.getByRole('button', { name: label }))
    expect(commandGame).toHaveBeenCalledTimes(1)
    expect(commandGame).toHaveBeenCalledWith(snapshot.id, expect.objectContaining({ action }))
  })
  it('shows results and summary with pass and missing counts', () => {
    show({
      stage: 'finished',
      allowedActions: ['lobby'],
      results: [
        {
          roundId: game.roundId,
          number: 1,
          prompt: game.prompt,
          optionA: 'Tea',
          optionB: 'Coffee',
          skipped: false,
          a: 1,
          b: 0,
          passes: 1,
          missing: 2,
          percentA: 100,
          percentB: 0,
          choices: [
            { participantId: snapshot.youId, name: 'Host', choice: 'a' },
            { participantId: snapshot.participants[1].id, name: 'Player', choice: 'pass' },
          ],
        },
      ],
    })
    expect(screen.getByText('Tea: 1 vote (100%)')).toBeInTheDocument()
    expect(screen.getByText('1 passed · 2 unanswered')).toBeInTheDocument()
    expect(screen.getByRole('list', { name: 'Player choices for round 1' })).toHaveTextContent(
      'HostTeaPlayerPassed',
    )
    expect(screen.getByRole('button', { name: 'Return to lobby' })).toBeEnabled()
    expect(screen.queryByRole('button', { name: 'Tea' })).not.toBeInTheDocument()
  })
  it.each([
    ['revealed', 'next', 'Next round'],
    ['finished', 'lobby', 'Return to lobby'],
  ])('allows the facilitator to continue from %s', async (stage, action, label) => {
    vi.mocked(commandGame).mockResolvedValue({ snapshot, appliedVersion: 2, replayed: false })
    show({ stage, allowedActions: [action] })
    fireEvent.click(screen.getByRole('button', { name: label }))
    await waitFor(() => expect(accept).toHaveBeenCalled())
    expect(commandGame).toHaveBeenCalledWith(snapshot.id, expect.objectContaining({ action }))
  })
  it('shows a skipped result and waits for the facilitator without exposing answer controls', () => {
    show(
      {
        stage: 'revealed',
        allowedActions: [],
        results: [
          {
            roundId: game.roundId,
            number: 1,
            prompt: game.prompt,
            optionA: 'Tea',
            optionB: 'Coffee',
            skipped: true,
            a: 0,
            b: 0,
            passes: 0,
            missing: 2,
            percentA: 0,
            percentB: 0,
            choices: [],
          },
        ],
      },
      false,
      true,
    )
    expect(screen.getByRole('heading', { name: 'Round 1 · Skipped' })).toBeInTheDocument()
    expect(screen.getByText('Waiting for your facilitator to continue')).toBeInTheDocument()
    expect(screen.queryByRole('group', { name: 'Your choice' })).not.toBeInTheDocument()
  })
  it('retains the command ID after an ambiguous failure', async () => {
    vi.mocked(commandGame)
      .mockRejectedValueOnce(new TypeError('network'))
      .mockResolvedValueOnce({ snapshot, appliedVersion: 2, replayed: true })
    show()
    fireEvent.click(screen.getByRole('button', { name: 'Reveal answers' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Retry game change' }))
    await waitFor(() => expect(accept).toHaveBeenCalled())
    expect(vi.mocked(commandGame).mock.calls[0][1]).toEqual(vi.mocked(commandGame).mock.calls[1][1])
  })
  it('reports a stale answer and refreshes without silently resubmitting', async () => {
    vi.mocked(commandGame).mockRejectedValue(
      new RoomError('stage_changed', 'The round moved on', 409),
    )
    show()
    fireEvent.click(screen.getByRole('button', { name: 'Tea' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('The round moved on')
    expect(refresh).toHaveBeenCalledOnce()
    expect(screen.queryByRole('button', { name: 'Retry game change' })).not.toBeInTheDocument()
  })
})

it('keeps the current reveal and advance controls ahead of collapsed history', () => {
  const result = {
    roundId: game.roundId,
    number: 1,
    prompt: 'Choose',
    optionA: 'Tea',
    optionB: 'Coffee',
    skipped: true,
    a: 0,
    b: 0,
    passes: 0,
    missing: 2,
    percentA: 0,
    percentB: 0,
    choices: [],
  }
  show({
    stage: 'revealed',
    roundNumber: 2,
    allowedActions: ['next', 'finish'],
    results: [result, { ...result, roundId: crypto.randomUUID(), number: 2 }],
  })
  const current = screen.getByRole('heading', { name: 'Round 2 · Skipped' })
  expect(screen.getByRole('heading', { name: 'Round 1 · Skipped' })).not.toBeVisible()
  expect(
    screen.getByRole('button', { name: 'Next round' }).compareDocumentPosition(current) &
      Node.DOCUMENT_POSITION_FOLLOWING,
  ).toBeTruthy()
  const history = screen.getByText('Earlier rounds (1)').closest('details')!
  expect(history).not.toHaveAttribute('open')
})
