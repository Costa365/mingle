import { useCallback, useEffect, useRef, useState } from 'react'
import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { ensureSession, getRoom, newerSnapshot, parseSnapshot, RoomError } from '../api/rooms'
import type { RoomSnapshot } from '../api/rooms'

export type ConnectionStatus = 'connecting' | 'live' | 'reconnecting' | 'offline' | 'ended'
export function useRoom(id: string) {
  const [snapshot, setSnapshot] = useState<RoomSnapshot | null>(null)
  const current = useRef<RoomSnapshot | null>(null)
  const [status, setStatus] = useState<ConnectionStatus>('connecting')
  const [error, setError] = useState('')
  const [generation, setGeneration] = useState(0)
  const refreshRef = useRef<() => Promise<void>>(async () => {})
  const accept = useCallback((next: RoomSnapshot, resynchronized = false) => {
    current.current = newerSnapshot(current.current, next, resynchronized)
    setSnapshot(current.current)
  }, [])
  const retry = useCallback(() => setGeneration((value) => value + 1), [])
  const refresh = useCallback(() => refreshRef.current(), [])

  useEffect(() => {
    if (current.current?.id !== id) {
      current.current = null
      setSnapshot(null)
    }
    let disposed = false
    let terminal = false
    let sequence = 0
    const abort = new AbortController()
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/rooms')
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (context) =>
          context.elapsedMilliseconds > 30_000
            ? null
            : Math.min(8000, 1000 * 2 ** context.previousRetryCount) + Math.random() * 500,
      })
      .configureLogging(LogLevel.None)
      .build()
    const updateStatus = (value: ConnectionStatus) => {
      if (!disposed) setStatus(value)
    }
    const fail = (cause: unknown) => {
      if (disposed) return
      if (cause instanceof RoomError && [401, 403, 404, 410].includes(cause.status)) {
        terminal = true
        setStatus('ended')
        setError(cause.message)
        void connection.stop()
      }
    }
    const synchronize = async (resynchronized = false) => {
      const request = ++sequence
      try {
        const next = await getRoom(id, abort.signal)
        if (!disposed && request === sequence) {
          accept(next, resynchronized)
          setError('')
          if (current.current?.status === 'closed') {
            terminal = true
            setStatus('live')
            void connection.stop()
          }
        }
      } catch (cause) {
        fail(cause)
        throw cause
      }
    }
    const attach = async () => {
      await connection.invoke('Subscribe', id)
      await synchronize(true)
      if (!terminal) updateStatus('live')
    }
    connection.on('Snapshot', (value: unknown) => {
      if (disposed || terminal) return
      try {
        const next = parseSnapshot(value)
        accept(next)
        if (current.current?.status === 'closed') {
          terminal = true
          setStatus('live')
          void connection.stop()
        }
      } catch {
        setError('The room response could not be read')
      }
    })
    connection.on('AccessEnded', (value: { code: string }) => {
      terminal = true
      updateStatus('ended')
      if (!disposed)
        setError(
          value.code === 'room_expired'
            ? 'This room has expired'
            : 'You no longer have access to this room',
        )
      void connection.stop()
    })
    connection.onreconnecting(() => updateStatus('reconnecting'))
    connection.onreconnected(() => {
      void attach().catch(() => {
        if (!terminal) updateStatus('offline')
      })
    })
    connection.onclose(() => {
      if (!terminal) updateStatus('offline')
    })
    refreshRef.current = () => synchronize()
    updateStatus('connecting')
    const start = async () => {
      try {
        await ensureSession()
        // Fetch first to show precise expiry/removal errors even when hub access fails.
        await synchronize(true)
        if (terminal) return
        for (let attempt = 0; attempt < 5 && !disposed; attempt++) {
          try {
            await connection.start()
            if (!disposed) await attach()
            return
          } catch (cause) {
            if (disposed || terminal) return
            await connection.stop()
            if (attempt === 4) throw cause
            updateStatus('reconnecting')
            await new Promise<void>((resolve) => {
              const timer = window.setTimeout(
                resolve,
                Math.min(8000, 500 * 2 ** attempt) + Math.random() * 250,
              )
              abort.signal.addEventListener(
                'abort',
                () => {
                  clearTimeout(timer)
                  resolve()
                },
                { once: true },
              )
            })
          }
        }
      } catch (cause) {
        fail(cause)
        if (!disposed && !terminal) {
          setStatus('offline')
          setError('Connection lost; retry to rejoin your room')
        }
      }
    }
    void start()
    const poll = window.setInterval(
      () => {
        if (!disposed && !terminal) void synchronize().catch(() => {})
      },
      15_000 + Math.random() * 1000,
    )
    const wake = () => {
      if (document.visibilityState === 'hidden' || disposed || terminal) return
      if (connection.state === HubConnectionState.Connected) {
        updateStatus('reconnecting')
        void attach().catch(() => {
          if (!terminal) updateStatus('offline')
        })
      } else if (connection.state === HubConnectionState.Disconnected) retry()
    }
    window.addEventListener('online', wake)
    document.addEventListener('visibilitychange', wake)
    return () => {
      disposed = true
      abort.abort()
      clearInterval(poll)
      window.removeEventListener('online', wake)
      document.removeEventListener('visibilitychange', wake)
      void connection.stop()
    }
  }, [id, generation, accept, retry])
  return { snapshot, status, error, retry, refresh, accept }
}
