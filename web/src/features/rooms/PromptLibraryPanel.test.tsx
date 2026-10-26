import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import { PromptLibraryPanel } from './PromptLibraryPanel'
import { commandPrompt, getPrompts, RoomError } from '../../api/rooms'
import type { PromptView } from '../../api/rooms'
import { room } from '../../test/room'

vi.mock('../../api/rooms', async (load) => ({
  ...(await load<typeof import('../../api/rooms')>()),
  getPrompts: vi.fn(),
  commandPrompt: vi.fn(),
}))
const prompts: PromptView[] = [
  {
    id: 'tot-001',
    gameId: 'this-or-that',
    category: 'Travel',
    tags: ['calm'],
    text: 'Choose',
    optionA: 'Mountains',
    optionB: 'Seaside',
    source: 'built-in',
    enabled: true,
    revision: 2,
    used: false,
  },
  {
    id: 'custom',
    gameId: 'this-or-that',
    category: 'Food',
    tags: ['snacks'],
    text: 'Choose',
    optionA: 'Berries',
    optionB: 'Grapes',
    source: 'room',
    enabled: true,
    revision: 1,
    used: false,
  },
  {
    id: 'nhie-001',
    gameId: 'never-have-i-ever',
    category: 'Learning',
    tags: ['stories'],
    text: 'Never have I ever made bread',
    optionA: '',
    optionB: '',
    source: 'built-in',
    enabled: true,
    revision: 2,
    used: false,
  },
]
const accept = vi.fn(),
  refresh = vi.fn().mockResolvedValue(undefined),
  choose = vi.fn().mockResolvedValue(undefined)
async function open(disabled = false) {
  render(
    <PromptLibraryPanel
      snapshot={room}
      disabled={disabled}
      accept={accept}
      refresh={refresh}
      choose={choose}
    />,
  )
  fireEvent.click(screen.getByRole('button', { name: 'Browse prompts' }))
  if (!disabled) await screen.findByText('Mountains / Seaside')
}
beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(getPrompts).mockResolvedValue({ roomVersion: room.version, prompts })
  vi.mocked(commandPrompt).mockResolvedValue({
    snapshot: { ...room, version: room.version + 1 },
    appliedVersion: room.version + 1,
    replayed: false,
  })
})
it('browses and composes category, tag and normalized search filters', async () => {
  await open()
  expect(screen.getByRole('status')).toHaveTextContent('2 prompts · 2 unused')
  fireEvent.change(screen.getByLabelText('Category'), { target: { value: 'Food' } })
  fireEvent.change(screen.getByLabelText('Tag'), { target: { value: 'snacks' } })
  fireEvent.change(screen.getByLabelText('Search prompts'), { target: { value: 'ＧＲＡＰＥＳ' } })
  expect(screen.queryByText('Mountains / Seaside')).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Use this prompt' }))
  expect(choose).toHaveBeenCalledWith({
    gameId: 'this-or-that',
    promptId: 'custom',
    category: 'Food',
    tag: 'snacks',
    search: 'ＧＲＡＰＥＳ',
  })
})
it('draws randomly from current filters and offers reset only when exhausted', async () => {
  vi.mocked(getPrompts).mockResolvedValue({
    roomVersion: room.version,
    prompts: prompts.map((p) => ({ ...p, used: true })),
  })
  await open()
  expect(screen.getByRole('button', { name: 'Draw random prompt' })).toBeDisabled()
  fireEvent.click(screen.getByRole('button', { name: 'Reset pool and draw' }))
  expect(choose).toHaveBeenCalledWith({
    gameId: 'this-or-that',
    category: '',
    tag: '',
    search: '',
    resetPool: true,
  })
})
it('offers prepared game prompts for gameplay', async () => {
  await open()
  fireEvent.change(screen.getByLabelText('Game'), { target: { value: 'never-have-i-ever' } })
  expect(screen.queryByText(/Gameplay for this game is coming soon/)).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Use this prompt' })).toBeEnabled()
})
it('creates a room prompt with optional trimmed tags', async () => {
  await open()
  fireEvent.click(screen.getByRole('button', { name: 'Add room prompt' }))
  fireEvent.change(screen.getByLabelText('Option A'), { target: { value: 'Paint' } })
  fireEvent.change(screen.getByLabelText('Option B'), { target: { value: 'Sculpt' } })
  fireEvent.change(screen.getByLabelText('Tags separated by commas'), {
    target: { value: ' art, creative, ' },
  })
  fireEvent.click(screen.getByRole('button', { name: 'Save prompt' }))
  await waitFor(() => expect(accept).toHaveBeenCalled())
  expect(commandPrompt).toHaveBeenCalledWith(
    room.id,
    expect.objectContaining({
      action: 'create',
      expectedVersion: room.version,
      input: expect.objectContaining({
        optionA: 'Paint',
        optionB: 'Sculpt',
        tags: ['art', 'creative'],
      }),
    }),
  )
})
it('edits only room content and requires an explicit deletion confirmation', async () => {
  await open()
  expect(screen.getAllByRole('button', { name: 'Edit prompt' })).toHaveLength(1)
  fireEvent.click(screen.getByRole('button', { name: 'Edit prompt' }))
  expect(screen.getByLabelText('Option A')).toHaveValue('Berries')
  fireEvent.click(screen.getByLabelText('Enabled for drawing'))
  fireEvent.click(screen.getByRole('button', { name: 'Save prompt' }))
  await waitFor(() => expect(commandPrompt).toHaveBeenCalled())
  expect(commandPrompt).toHaveBeenLastCalledWith(
    room.id,
    expect.objectContaining({
      action: 'edit',
      promptId: 'custom',
      promptRevision: 1,
      input: expect.objectContaining({ enabled: false }),
    }),
  )
  fireEvent.click(screen.getByRole('button', { name: 'Delete prompt' }))
  expect(commandPrompt).toHaveBeenCalledTimes(1)
  fireEvent.click(screen.getByRole('button', { name: 'Confirm delete' }))
  await waitFor(() => expect(commandPrompt).toHaveBeenCalledTimes(2))
})
it('keeps a form after validation errors and refreshes a stale room', async () => {
  vi.mocked(commandPrompt).mockRejectedValue(
    new RoomError('duplicate_prompt', 'That prompt already exists', 409),
  )
  await open()
  fireEvent.click(screen.getByRole('button', { name: 'Edit prompt' }))
  fireEvent.click(screen.getByRole('button', { name: 'Save prompt' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('already exists')
  expect(screen.getByLabelText('Option A')).toHaveValue('Berries')
  expect(refresh).toHaveBeenCalledOnce()
})
it('reuses the exact command after an ambiguous save', async () => {
  vi.mocked(commandPrompt)
    .mockRejectedValueOnce(new TypeError('network'))
    .mockResolvedValueOnce({ snapshot: room, appliedVersion: 3, replayed: true })
  await open()
  fireEvent.click(screen.getByRole('button', { name: 'Edit prompt' }))
  fireEvent.click(screen.getByRole('button', { name: 'Save prompt' }))
  fireEvent.click(await screen.findByRole('button', { name: 'Retry prompt change' }))
  await waitFor(() => expect(accept).toHaveBeenCalled())
  expect(vi.mocked(commandPrompt).mock.calls[0][1]).toEqual(
    vi.mocked(commandPrompt).mock.calls[1][1],
  )
})
it('shows an empty search state and keeps disabled prompts out of drawing', async () => {
  vi.mocked(getPrompts).mockResolvedValue({
    roomVersion: room.version,
    prompts: [{ ...prompts[0], enabled: false }],
  })
  await open()
  expect(screen.getByRole('button', { name: 'Use this prompt' })).toBeDisabled()
  fireEvent.change(screen.getByLabelText('Search prompts'), { target: { value: 'not present' } })
  expect(screen.getByText('No prompts match these filters')).toBeInTheDocument()
  expect(
    within(screen.getByRole('list', { name: 'Available prompts' })).queryAllByRole('listitem'),
  ).toHaveLength(0)
})

it('keeps a selected empty category after its last prompt is deleted', async () => {
  await open()
  fireEvent.change(screen.getByLabelText('Category'), { target: { value: 'Food' } })
  vi.mocked(getPrompts).mockResolvedValue({
    roomVersion: room.version + 1,
    prompts: [prompts[0], prompts[2]],
  })
  fireEvent.click(screen.getByRole('button', { name: 'Delete prompt' }))
  fireEvent.click(screen.getByRole('button', { name: 'Confirm delete' }))
  await screen.findByText('No prompts match these filters')
  expect(screen.getByLabelText('Category')).toHaveValue('Food')
})
it('clears a failed library read after closing and reopening', async () => {
  vi.mocked(getPrompts)
    .mockRejectedValueOnce(new TypeError('network'))
    .mockResolvedValueOnce({ roomVersion: room.version, prompts })
  render(
    <PromptLibraryPanel
      snapshot={room}
      disabled={false}
      accept={accept}
      refresh={refresh}
      choose={choose}
    />,
  )
  fireEvent.click(screen.getByRole('button', { name: 'Browse prompts' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('could not be loaded')
  fireEvent.click(screen.getByRole('button', { name: 'Browse prompts' }))
  fireEvent.click(screen.getByRole('button', { name: 'Browse prompts' }))
  await screen.findByText('Mountains / Seaside')
  expect(screen.queryByRole('alert')).not.toBeInTheDocument()
})

it('keeps a confirmed save separate from a failed library refresh', async () => {
  await open()
  vi.mocked(getPrompts).mockRejectedValue(new TypeError('network'))
  fireEvent.click(screen.getByRole('button', { name: 'Edit prompt' }))
  fireEvent.click(screen.getByRole('button', { name: 'Save prompt' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Your prompt change is saved')
  expect(accept).toHaveBeenCalledOnce()
  expect(screen.queryByRole('button', { name: 'Retry prompt change' })).not.toBeInTheDocument()
})

it('labels the scrollable prompt list and exposes category, tags and status beside each action', async () => {
  vi.mocked(getPrompts).mockResolvedValue({
    roomVersion: room.version,
    prompts: [prompts[0], { ...prompts[1], used: true, enabled: false }],
  })
  await open()
  const region = screen.getByRole('region', { name: 'Available prompts' })
  const list = within(region).getByRole('list', { name: 'Available prompts' })
  expect(list).toHaveAttribute('tabindex', '0')
  expect(list).toHaveAccessibleDescription(
    'Scroll within the list, or focus it and use Page Up / Page Down',
  )
  const rows = within(list).getAllByRole('listitem')
  expect(within(rows[0]).getByText('Travel')).toBeInTheDocument()
  expect(within(rows[0]).getByText('calm')).toBeInTheDocument()
  expect(within(rows[0]).getByRole('button', { name: 'Use this prompt' })).toBeEnabled()
  expect(within(rows[1]).getByText(/Room prompt.*Used.*Disabled/)).toBeInTheDocument()
  expect(within(rows[1]).getByRole('button', { name: 'Use this prompt' })).toBeDisabled()
  expect(within(rows[1]).getByRole('button', { name: 'Edit prompt' })).toBeEnabled()
})

it('provides a page heading and directly loaded library in the dedicated Prompts view', async () => {
  render(
    <PromptLibraryPanel
      snapshot={room}
      disabled={false}
      accept={accept}
      refresh={refresh}
      choose={choose}
      expanded
    />,
  )
  expect(screen.getByRole('heading', { name: 'Your prompt library', level: 1 })).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Browse prompts' })).not.toBeInTheDocument()
  await screen.findByText('Mountains / Seaside')
  expect(screen.getByRole('heading', { name: 'Available prompts', level: 2 })).toBeVisible()
})
