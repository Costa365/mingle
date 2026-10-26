import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { AdminPage } from './AdminPage'
import { AdminError, adminRead, adminSession } from '../../api/admin'
vi.mock('../../api/admin', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../api/admin')>()
  return { ...original, adminRead: vi.fn(), adminSession: vi.fn() }
})
const session = { enabled: true, authenticated: true, requestToken: 'csrf', expiresTicks: null }
const room = {
  id: 'room-id',
  code: 'ABC234',
  version: 4,
  status: 'playing',
  joinLocked: false,
  participantCount: 2,
  lastActivityTicks: 638000000000000000,
  facilitatorId: 'host',
  participants: [
    { id: 'host', name: 'Host', isFacilitator: true, isOnline: true },
    { id: 'player', name: 'Player', isFacilitator: false, isOnline: false },
  ],
  games: [{ sessionId: 'session', gameId: 'this-or-that', stage: 'responding', roundNumber: 1 }],
}
afterEach(() => {
  vi.resetAllMocks()
  sessionStorage.clear()
})
it('fails closed when configuration is absent and exposes no admin controls', async () => {
  vi.mocked(adminSession).mockResolvedValue({ ...session, enabled: false, authenticated: false })
  render(<AdminPage />)
  expect(
    await screen.findByText('Administration is not configured on this deployment'),
  ).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Sign in' })).not.toBeInTheDocument()
  expect(adminRead).not.toHaveBeenCalled()
})
it('clears credential input when login is submitted and shows authentication failure', async () => {
  vi.mocked(adminSession).mockResolvedValue({ ...session, authenticated: false })
  vi.mocked(adminRead).mockRejectedValue(new AdminError('Credential rejected', 401))
  render(<AdminPage />)
  const input = await screen.findByLabelText('Administrator credential')
  fireEvent.change(input, { target: { value: 'secret-input' } })
  fireEvent.click(screen.getByRole('button', { name: 'Sign in' }))
  expect(input).toHaveValue('')
  expect(await screen.findByRole('alert')).toHaveTextContent('Credential rejected')
})
it('requires room code confirmation and retries the exact command after a lost acknowledgement', async () => {
  vi.mocked(adminSession).mockResolvedValue(session)
  let attempts = 0
  vi.mocked(adminRead).mockImplementation(async (path) => {
    if (path.startsWith('/rooms?')) return { page: 1, pageSize: 20, total: 1, rooms: [room] }
    if (path === '/rooms/room-id') return room
    if (path.endsWith('/commands')) {
      if (attempts++ === 0) throw new AdminError('Response lost')
      return { appliedVersion: 5, replayed: true }
    }
    throw new Error(path)
  })
  render(<AdminPage />)
  fireEvent.click(await screen.findByRole('button', { name: 'Manage ABC234' }))
  fireEvent.click(await screen.findByRole('button', { name: 'Delete room' }))
  const confirm = screen.getByRole('button', { name: 'Permanently delete room' })
  expect(confirm).toBeDisabled()
  fireEvent.change(screen.getByLabelText('Enter room code to confirm'), {
    target: { value: 'ABC234' },
  })
  fireEvent.click(confirm)
  fireEvent.click(await screen.findByRole('button', { name: 'Retry same action' }))
  await waitFor(() => expect(screen.getByText('Change saved')).toBeVisible())
  const calls = vi.mocked(adminRead).mock.calls.filter(([path]) => path.endsWith('/commands'))
  expect(calls).toHaveLength(2)
  expect(calls[1]).toEqual(calls[0])
  expect(calls[0][1]).toMatchObject({
    action: 'delete',
    expectedVersion: 4,
    confirmation: 'ABC234',
  })
})
it('drops admin data and requires sign in when an API operation reports expiry', async () => {
  vi.mocked(adminSession).mockResolvedValue(session)
  vi.mocked(adminRead).mockRejectedValue(new AdminError('Session ended', 401))
  render(<AdminPage />)
  expect(await screen.findByRole('heading', { name: 'Administrator sign in' })).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Sign out' })).not.toBeInTheDocument()
})
it('does not reoffer a confirmed mutation when refreshing room details fails', async () => {
  vi.mocked(adminSession).mockResolvedValue(session)
  let detail = 0
  vi.mocked(adminRead).mockImplementation(async (path) => {
    if (path.startsWith('/rooms?')) return { page: 1, pageSize: 20, total: 1, rooms: [room] }
    if (path === '/rooms/room-id') {
      if (detail++ > 0) throw new AdminError('Refresh unavailable')
      return room
    }
    if (path.endsWith('/commands')) return { appliedVersion: 5, replayed: false }
    throw new Error(path)
  })
  render(<AdminPage />)
  fireEvent.click(await screen.findByRole('button', { name: 'Manage ABC234' }))
  fireEvent.click(await screen.findByRole('button', { name: 'Lock joining' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Refresh unavailable')
  expect(screen.queryByRole('button', { name: 'Retry same action' })).not.toBeInTheDocument()
  expect(sessionStorage.getItem('mingle-admin-pending')).toBeNull()
  expect(
    vi.mocked(adminRead).mock.calls.filter(([path]) => path.endsWith('/commands')),
  ).toHaveLength(1)
})
