import { fireEvent, render, screen } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { PromptEditor } from './PromptEditor'
it('builds binary prompt input with parsed tags and enabled status', () => {
  const save = vi.fn()
  render(<PromptEditor busy={false} save={save} cancel={vi.fn()} />)
  fireEvent.change(screen.getByLabelText('Category'), { target: { value: 'Team' } })
  fireEvent.change(screen.getByLabelText('Tags, separated by commas'), {
    target: { value: 'fun, work' },
  })
  fireEvent.change(screen.getByLabelText('Prompt text'), { target: { value: 'Pick one' } })
  fireEvent.change(screen.getByLabelText('Option A'), { target: { value: 'Tea' } })
  fireEvent.change(screen.getByLabelText('Option B'), { target: { value: 'Coffee' } })
  fireEvent.click(screen.getByLabelText('Enabled'))
  fireEvent.click(screen.getByRole('button', { name: 'Save shared prompt' }))
  expect(save).toHaveBeenCalledWith({
    gameId: 'this-or-that',
    category: 'Team',
    tags: ['fun', 'work'],
    text: 'Pick one',
    optionA: 'Tea',
    optionB: 'Coffee',
    enabled: false,
  })
})
it('omits binary choices for statement games and blocks save while pending', () => {
  const save = vi.fn()
  render(<PromptEditor busy save={save} cancel={vi.fn()} />)
  fireEvent.change(screen.getByLabelText('Game'), { target: { value: 'never-have-i-ever' } })
  expect(screen.queryByLabelText('Option A')).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Save shared prompt' })).toBeDisabled()
  expect(save).not.toHaveBeenCalled()
})
