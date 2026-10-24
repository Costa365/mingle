import type * as Contract from './room-contract.generated'

export type Participant = Contract.ParticipantView
export type EntryRequest = Contract.EntryRequest
export interface RoomSnapshot extends Omit<Contract.RoomSnapshot, 'status'> {
  status: 'lobby' | 'closed'
}
export interface RoomCommand extends Omit<Contract.RoomCommand, 'targetId' | 'locked'> {
  targetId?: string
  locked?: boolean
}
export interface CommandResult extends Omit<Contract.CommandResult, 'snapshot'> {
  snapshot: RoomSnapshot
}

export class RoomError extends Error {
  constructor(
    public code: string,
    message: string,
    public status = 0,
  ) {
    super(message)
  }
}

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
export function parseSnapshot(value: unknown): RoomSnapshot {
  if (!value || typeof value !== 'object')
    throw new RoomError('invalid_response', 'The room response could not be read')
  const s = value as RoomSnapshot
  if (
    s.protocolVersion !== 1 ||
    !uuid.test(s.id) ||
    !uuid.test(s.youId) ||
    !uuid.test(s.facilitatorId) ||
    !uuid.test(s.presenceEpoch) ||
    typeof s.code !== 'string' ||
    !/^[A-Z2-9]{6}$/.test(s.code) ||
    !Number.isSafeInteger(s.version) ||
    s.version < 1 ||
    !Number.isSafeInteger(s.presenceRevision) ||
    s.presenceRevision < 0 ||
    typeof s.joinLocked !== 'boolean' ||
    !Number.isSafeInteger(s.capacity) ||
    s.capacity < 2 ||
    !['lobby', 'closed'].includes(s.status) ||
    !Array.isArray(s.allowedActions) ||
    !s.allowedActions.every((a) => ['lock', 'remove', 'transfer', 'close'].includes(a)) ||
    !Array.isArray(s.participants) ||
    !s.participants.every(
      (p) =>
        p &&
        uuid.test(p.id) &&
        typeof p.name === 'string' &&
        p.name.length > 0 &&
        p.name.length <= 32 &&
        typeof p.isOnline === 'boolean' &&
        typeof p.isFacilitator === 'boolean',
    ) ||
    new Set(s.participants.map((p) => p.id)).size !== s.participants.length ||
    !s.participants.some((p) => p.id === s.youId) ||
    !s.participants.some((p) => p.id === s.facilitatorId && p.isFacilitator)
  )
    throw new RoomError('invalid_response', 'The room response could not be read')
  return s
}

export function newerSnapshot(
  current: RoomSnapshot | null,
  next: RoomSnapshot,
  resynchronized = false,
): RoomSnapshot {
  if (!current) return next
  if (next.id !== current.id || next.version < current.version) return current
  if (next.presenceEpoch !== current.presenceEpoch) return resynchronized ? next : current
  return next.version > current.version || next.presenceRevision >= current.presenceRevision
    ? next
    : current
}

async function read<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const error = (await response.json().catch(() => ({}))) as Partial<Contract.RoomErrorView>
    throw new RoomError(
      error.code ?? 'request_failed',
      error.message ??
        (response.status === 401
          ? 'Your guest session has expired; join again'
          : 'The request failed; please try again'),
      response.status,
    )
  }
  return (await response.json()) as T
}

let session: Promise<string> | undefined
async function bootstrap(): Promise<string> {
  const begin = await read<Contract.SessionView>(
    await fetch('/api/session', { signal: AbortSignal.timeout(10_000) }),
  )
  const result = await read<Contract.SessionView>(
    await fetch('/api/session', {
      method: 'POST',
      headers: { 'X-CSRF-TOKEN': begin.requestToken },
      signal: AbortSignal.timeout(10_000),
    }),
  )
  return result.requestToken
}
export function ensureSession(): Promise<string> {
  session ??= (async () =>
    navigator.locks
      ? await navigator.locks.request('mingle-session', bootstrap)
      : await bootstrap())().catch((error) => {
    session = undefined
    throw error
  })
  return session
}

async function mutate(path: string, payload: EntryRequest | RoomCommand): Promise<CommandResult> {
  for (let attempt = 0; attempt < 2; attempt++) {
    const token = await ensureSession()
    const response = await fetch(path, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token },
      body: JSON.stringify(payload),
      signal: AbortSignal.timeout(12_000),
    })
    try {
      const result = await read<CommandResult>(response)
      return { ...result, snapshot: parseSnapshot(result.snapshot) }
    } catch (error) {
      if (error instanceof RoomError && error.code === 'csrf_invalid' && attempt === 0) {
        session = undefined
        continue
      }
      throw error
    }
  }
  throw new RoomError('request_failed', 'Please refresh and try again')
}
export const enterRoom = (request: EntryRequest, create: boolean) =>
  mutate(create ? '/api/rooms' : '/api/rooms/join', request)
export const commandRoom = (id: string, command: RoomCommand) =>
  mutate(`/api/rooms/${id}/commands`, command)
export async function getRoom(id: string, signal?: AbortSignal): Promise<RoomSnapshot> {
  const response = await fetch(`/api/rooms/${id}`, {
    signal: signal
      ? AbortSignal.any([signal, AbortSignal.timeout(10_000)])
      : AbortSignal.timeout(10_000),
  })
  return parseSnapshot(await read<unknown>(response))
}
