import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { room } from '../test/room'
import { RoomError } from '../api/rooms'
import { useRoom } from './useRoom'

const mocks = vi.hoisted(() => ({
  get: vi.fn(),
  session: vi.fn(),
  start: vi.fn(),
  stop: vi.fn(),
  invoke: vi.fn(),
  events: {} as Record<string, (value: unknown) => void>,
  reconnecting: (() => {}) as () => void,
  reconnected: (() => {}) as () => void,
  closed: (() => {}) as () => void,
  order: [] as string[],
  policy: null as null | {
    nextRetryDelayInMilliseconds: (value: {
      elapsedMilliseconds: number
      previousRetryCount: number
    }) => number | null
  },
  connection: { state: 'Disconnected' },
}))
vi.mock('../api/rooms', async (original) => ({
  ...(await original<typeof import('../api/rooms')>()),
  ensureSession: mocks.session,
  getRoom: mocks.get,
}))
vi.mock('@microsoft/signalr', () => ({
  HubConnectionState: { Connected: 'Connected', Disconnected: 'Disconnected' },
  LogLevel: { None: 6 },
  HubConnectionBuilder: class {
    withUrl() {
      return this
    }
    withAutomaticReconnect(policy: typeof mocks.policy) {
      mocks.policy = policy
      return this
    }
    configureLogging() {
      return this
    }
    build() {
      return Object.assign(mocks.connection, {
        start: () => {
          mocks.order.push('start')
          return mocks.start()
        },
        stop: mocks.stop,
        invoke: mocks.invoke,
        on: (name: string, handler: (value: unknown) => void) => {
          mocks.order.push(name)
          mocks.events[name] = handler
        },
        onreconnecting: (handler: () => void) => {
          mocks.reconnecting = handler
        },
        onreconnected: (handler: () => void) => {
          mocks.reconnected = handler
        },
        onclose: (handler: () => void) => {
          mocks.closed = handler
        },
      })
    }
  },
}))
beforeEach(() => {
  vi.clearAllMocks()
  mocks.events = {}
  mocks.order = []
  mocks.connection.state = 'Disconnected'
  mocks.session.mockResolvedValue('token')
  mocks.get.mockResolvedValue(room)
  mocks.start.mockImplementation(async () => {
    mocks.connection.state = 'Connected'
  })
  mocks.stop.mockResolvedValue(undefined)
  mocks.invoke.mockResolvedValue(undefined)
})
afterEach(() => vi.useRealTimers())
describe('room connection recovery', () => {
  it('stops live recovery when a terminal closed view arrives', async () => {
    const view = renderHook(() => useRoom(room.id))
    await waitFor(() => expect(view.result.current.status).toBe('live'))
    act(() => mocks.events.Snapshot({ ...room, version: 3, status: 'closed' }))
    expect(view.result.current.snapshot?.status).toBe('closed')
    expect(mocks.stop).toHaveBeenCalled()
    act(() => mocks.closed())
    expect(view.result.current.status).toBe('live')
  })
  it('registers handlers before starting, then subscribes and resynchronizes', async () => {
    const view = renderHook(() => useRoom(room.id))
    await waitFor(() => expect(view.result.current.status).toBe('live'))
    expect(mocks.order.indexOf('Snapshot')).toBeLessThan(mocks.order.indexOf('start'))
    expect(mocks.invoke).toHaveBeenCalledWith('Subscribe', room.id)
    expect(mocks.get).toHaveBeenCalledTimes(2)
    view.unmount()
    expect(mocks.stop).toHaveBeenCalled()
  })
  it('initial connection failures retry without losing membership', async () => {
    vi.useFakeTimers()
    mocks.start.mockRejectedValueOnce(new Error('offline'))
    const view = renderHook(() => useRoom(room.id))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1)
    })
    expect(view.result.current.status).toBe('reconnecting')
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1000)
    })
    expect(view.result.current.status).toBe('live')
    expect(mocks.start).toHaveBeenCalledTimes(2)
    view.unmount()
  })
  it('reconnects by resubscribing, ignores older snapshots, and reconciles missed delivery', async () => {
    const view = renderHook(() => useRoom(room.id))
    await waitFor(() => expect(view.result.current.status).toBe('live'))
    act(() => mocks.events.Snapshot({ ...room, version: 4 }))
    await act(async () => {
      await view.result.current.refresh()
    })
    expect(view.result.current.snapshot?.version).toBe(4)
    act(() => mocks.reconnecting())
    expect(view.result.current.status).toBe('reconnecting')
    mocks.get.mockResolvedValue({
      ...room,
      version: 5,
      presenceEpoch: crypto.randomUUID(),
      presenceRevision: 0,
    })
    await act(async () => {
      mocks.reconnected()
      await Promise.resolve()
    })
    await waitFor(() => expect(view.result.current.status).toBe('live'))
    expect(view.result.current.snapshot?.version).toBe(5)
    expect(mocks.invoke).toHaveBeenCalledTimes(2)
  })
  it('periodically reloads committed state and stops polling after unmount', async () => {
    vi.useFakeTimers()
    const view = renderHook(() => useRoom(room.id))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1)
    })
    mocks.get.mockResolvedValue({ ...room, version: 3 })
    await act(async () => {
      await vi.advanceTimersByTimeAsync(17_000)
    })
    expect(view.result.current.snapshot?.version).toBe(3)
    const count = mocks.get.mock.calls.length
    view.unmount()
    await vi.advanceTimersByTimeAsync(20_000)
    expect(mocks.get).toHaveBeenCalledTimes(count)
  })
  it('ends recovery when access is revoked and bounds reconnect attempts', async () => {
    mocks.get.mockRejectedValue(new RoomError('room_expired', 'This room has expired', 410))
    const view = renderHook(() => useRoom(room.id))
    await waitFor(() => expect(view.result.current.status).toBe('ended'))
    expect(view.result.current.error).toBe('This room has expired')
    expect(mocks.start).not.toHaveBeenCalled()
    expect(
      mocks.policy?.nextRetryDelayInMilliseconds({
        elapsedMilliseconds: 30_001,
        previousRetryCount: 5,
      }),
    ).toBeNull()
  })
})
