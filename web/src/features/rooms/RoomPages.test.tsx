import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { RoomEntry, RoomLobby } from './RoomPages'
import { room } from '../../test/room'
import { RoomError } from '../../api/rooms'
import type { RoomSnapshot } from '../../api/rooms'

const mocks = vi.hoisted(() => ({
  enter: vi.fn(),
  command: vi.fn(),
  hook: vi.fn(),
  accept: vi.fn(),
  refresh: vi.fn(),
  retry: vi.fn(),
}))
vi.mock('../../api/rooms', async (original) => ({
  ...(await original<typeof import('../../api/rooms')>()),
  enterRoom: mocks.enter,
  commandRoom: mocks.command,
}))
vi.mock('../../realtime/useRoom', () => ({ useRoom: mocks.hook }))
function entry(create: boolean, path = '/') {
  render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/" element={<RoomEntry create={create} />} />
        <Route path="/rooms/:id" element={<h1>Joined room</h1>} />
      </Routes>
    </MemoryRouter>,
  )
}
function lobby(snapshot: RoomSnapshot = room, status = 'live', error = '') {
  mocks.hook.mockReturnValue({
    snapshot,
    status,
    error,
    accept: mocks.accept,
    refresh: mocks.refresh,
    retry: mocks.retry,
  })
  render(
    <MemoryRouter>
      <RoomLobby />
    </MemoryRouter>,
  )
}
beforeEach(() => {
  vi.clearAllMocks()
  sessionStorage.clear()
  mocks.enter.mockResolvedValue({ snapshot: room })
  mocks.command.mockResolvedValue({ snapshot: { ...room, version: 3 } })
  mocks.refresh.mockResolvedValue(undefined)
})
describe('room entry', () => {
  it.each([true, false])('submits a trimmed name and invitation when create=%s', async (create) => {
    entry(create, create ? '/' : '/?code=abc234')
    await userEvent.type(screen.getByLabelText('Your name'), ' Alex ')
    await userEvent.click(
      screen.getByRole('button', { name: create ? 'Create room' : 'Join room' }),
    )
    expect(await screen.findByRole('heading', { name: 'Joined room' })).toBeVisible()
    expect(mocks.enter).toHaveBeenCalledWith(
      expect.objectContaining({ displayName: 'Alex', code: create ? undefined : 'ABC234' }),
      create,
    )
  })
  it('shows server validation errors without losing the form', async () => {
    mocks.enter.mockRejectedValue(new RoomError('name_taken', 'That name is taken', 409))
    entry(false, '/?code=ABC234')
    await userEvent.type(screen.getByLabelText('Your name'), 'Alex')
    await userEvent.click(screen.getByRole('button', { name: 'Join room' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('That name is taken')
    expect(screen.getByLabelText('Your name')).toHaveValue('Alex')
  })
  it('reuses the entry command ID after a lost response', async () => {
    mocks.enter
      .mockRejectedValueOnce(new TypeError('network'))
      .mockResolvedValueOnce({ snapshot: room })
    entry(true)
    await userEvent.type(screen.getByLabelText('Your name'), 'Alex')
    await userEvent.click(screen.getByRole('button', { name: 'Create room' }))
    await screen.findByRole('alert')
    await userEvent.click(screen.getByRole('button', { name: 'Create room' }))
    await screen.findByRole('heading', { name: 'Joined room' })
    expect(mocks.enter.mock.calls[0][0].commandId).toBe(mocks.enter.mock.calls[1][0].commandId)
  })
})
describe('room lobby', () => {
  it.each([room.youId, room.participants[1].id])(
    'places team and invitations before gameplay for %s',
    (youId) => {
      lobby({
        ...room,
        youId,
        allowedActions: youId === room.facilitatorId ? room.allowedActions : [],
      })
      const team = screen.getByRole('region', { name: /Your team/ })
      const invite = screen.getByRole('complementary', { name: 'Bring your people' })
      const game = screen.getByRole('region', { name: 'This or That' })
      expect(team.compareDocumentPosition(game) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
      expect(invite.compareDocumentPosition(game) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
      expect(screen.getByRole('button', { name: 'Copy invite link' })).toBeEnabled()
    },
  )

  it.each([
    ['Lock joining', 'lock', undefined, true],
    ['Remove Player', 'remove', room.participants[1].id, undefined],
    ['Make Player facilitator', 'transfer', room.participants[1].id, undefined],
  ])('submits %s with the current room version', async (button, action, targetId, locked) => {
    lobby()
    if (action === 'transfer')
      expect(screen.getByRole('button', { name: button as string })).toHaveTextContent(
        'Make Player facilitator',
      )
    await userEvent.click(screen.getByRole('button', { name: button as string }))
    await waitFor(() => expect(mocks.accept).toHaveBeenCalled())
    expect(mocks.command).toHaveBeenCalledWith(
      '',
      expect.objectContaining({ action, targetId, locked, expectedVersion: room.version }),
    )
  })
  it('unlocks joining and reports that existing members can reconnect while locked', async () => {
    lobby({ ...room, joinLocked: true })
    expect(screen.getByText(/Existing participants can still reconnect/)).toBeVisible()
    await userEvent.click(screen.getByRole('button', { name: 'Unlock joining' }))
    await waitFor(() =>
      expect(mocks.command).toHaveBeenCalledWith('', expect.objectContaining({ locked: false })),
    )
  })
  it('requires a separate close confirmation and permits canceling', async () => {
    lobby()
    await userEvent.click(screen.getByRole('button', { name: 'Close room' }))
    expect(screen.getByRole('alertdialog')).toBeVisible()
    await userEvent.click(screen.getByRole('button', { name: 'Keep room open' }))
    expect(mocks.command).not.toHaveBeenCalled()
    await userEvent.click(screen.getByRole('button', { name: 'Close room' }))
    await userEvent.click(screen.getByRole('button', { name: 'Close room for everyone' }))
    await waitFor(() =>
      expect(mocks.command).toHaveBeenCalledWith('', expect.objectContaining({ action: 'close' })),
    )
  })
  it('copies both the invitation and code and reports clipboard failures', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined)
    vi.stubGlobal('navigator', { clipboard: { writeText } })
    lobby()
    fireEvent.click(screen.getByRole('button', { name: 'Copy invite link' }))
    await screen.findByText('Invite link copied')
    expect(writeText).toHaveBeenLastCalledWith(`${window.location.origin}/join?code=ABC234`)
    fireEvent.click(screen.getByRole('button', { name: 'Copy code' }))
    await screen.findByText('Room code copied')
    expect(writeText).toHaveBeenLastCalledWith('ABC234')
    writeText.mockRejectedValueOnce(new Error('denied'))
    fireEvent.click(screen.getByRole('button', { name: 'Copy code' }))
    expect(await screen.findByText(/Copying is unavailable/)).toBeVisible()
  })
  it('hides facilitator controls from participants and disables them during recovery', () => {
    lobby({ ...room, allowedActions: [] })
    expect(screen.queryByRole('button', { name: 'Lock joining' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Remove Player' })).not.toBeInTheDocument()
  })
  it('shows connection recovery and permits manual retry', async () => {
    lobby(room, 'offline', 'Connection lost')
    expect(screen.getByRole('button', { name: 'Lock joining' })).toBeDisabled()
    await userEvent.click(screen.getByRole('button', { name: 'Retry connection' }))
    expect(mocks.retry).toHaveBeenCalled()
  })
  it('resynchronizes after a stale command and explains the conflict', async () => {
    mocks.command.mockRejectedValueOnce(new RoomError('version_changed', 'The room changed', 409))
    lobby()
    await userEvent.click(screen.getByRole('button', { name: 'Lock joining' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('The room changed')
    expect(mocks.refresh).toHaveBeenCalled()
  })
  it.each([new TypeError('network'), new RoomError('request_failed', 'Unavailable', 500)])(
    'safely retries a command after an ambiguous failure: %s',
    async (cause) => {
      mocks.command.mockRejectedValueOnce(cause).mockResolvedValueOnce({ snapshot: room })
      lobby()
      await userEvent.click(screen.getByRole('button', { name: 'Lock joining' }))
      await screen.findByRole('button', { name: 'Retry last change' })
      await userEvent.click(screen.getByRole('button', { name: 'Retry last change' }))
      await waitFor(() => expect(mocks.command).toHaveBeenCalledTimes(2))
      expect(mocks.command.mock.calls[0][1]).toEqual(mocks.command.mock.calls[1][1])
    },
  )
  it('replaces private content with a terminal explanation after access ends', () => {
    lobby(room, 'ended', 'This room has expired')
    expect(screen.getByRole('alert')).toHaveTextContent('This room has expired')
    expect(screen.queryByText('Host (you)')).not.toBeInTheDocument()
  })
  it('shows closed rooms without controls', () => {
    lobby({ ...room, status: 'closed' })
    expect(screen.getByRole('heading', { name: 'This room is closed' })).toBeVisible()
    expect(screen.queryByRole('button', { name: 'Lock joining' })).not.toBeInTheDocument()
  })
})
