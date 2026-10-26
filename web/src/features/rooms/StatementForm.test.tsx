import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { StatementForm } from './StatementForm'
import type { GameView } from '../../api/rooms'
const game: GameView = {
  sessionId: crypto.randomUUID(),
  gameId: 'two-truths-and-a-lie',
  roundId: crypto.randomUUID(),
  roundNumber: 1,
  stageRevision: 1,
  stage: 'collection',
  prompt: 'Write',
  optionA: 'Statements',
  optionB: 'Guesses',
  eligibleCount: 1,
  responseCount: 1,
  eligible: true,
  ownChoice: null,
  responseRevision: 1,
  allowedActions: ['submit', 'answer'],
  results: [],
  ownSubmission: { statements: ['One', 'Two', 'Three'], lieIndex: 2 },
}
it('keeps a dirty statement draft and its original revision when another tab saves', async () => {
  const submit = vi.fn().mockResolvedValue(undefined),
    pass = vi.fn()
  const view = render(<StatementForm game={game} disabled={false} submit={submit} pass={pass} />)
  fireEvent.change(screen.getByLabelText('Statement 1', { exact: true }), {
    target: { value: 'Local' },
  })
  const newer = {
    ...game,
    responseRevision: 2,
    ownSubmission: { statements: ['Remote', 'Two', 'Three'], lieIndex: 1 },
  }
  view.rerender(<StatementForm game={newer} disabled={false} submit={submit} pass={pass} />)
  expect(screen.getByLabelText('Statement 1', { exact: true })).toHaveValue('Local')
  fireEvent.click(screen.getByRole('button', { name: 'Save statements' }))
  await waitFor(() =>
    expect(submit).toHaveBeenCalledWith({ statements: ['Local', 'Two', 'Three'], lieIndex: 2 }, 1),
  )
  fireEvent.click(screen.getByRole('button', { name: 'Use saved statements' }))
  expect(screen.getByLabelText('Statement 1', { exact: true })).toHaveValue('Remote')
  expect(screen.getByRole('radio', { name: 'Statement 2 is my lie' })).toBeChecked()
})
it('rejects duplicate statements and disables private input for ineligible players', () => {
  const submit = vi.fn(),
    pass = vi.fn()
  const view = render(<StatementForm game={game} disabled={false} submit={submit} pass={pass} />)
  fireEvent.change(screen.getByLabelText('Statement 1', { exact: true }), {
    target: { value: 'Two' },
  })
  fireEvent.click(screen.getByRole('button', { name: 'Save statements' }))
  expect(screen.getByRole('alert')).toHaveTextContent(/three different/)
  expect(submit).not.toHaveBeenCalled()
  view.rerender(
    <StatementForm
      game={{ ...game, eligible: false, allowedActions: [] }}
      disabled={false}
      submit={submit}
      pass={pass}
    />,
  )
  expect(screen.getByRole('button', { name: 'Save statements' })).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Sit this game out' })).toBeDisabled()
})
