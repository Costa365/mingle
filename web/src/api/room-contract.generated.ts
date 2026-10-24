// Generated from TeamGames.Core room DTOs. Run the contracts tool to update.

export interface ParticipantView {
  id: string
  name: string
  isFacilitator: boolean
  isOnline: boolean
}

export interface RoomSnapshot {
  protocolVersion: number
  id: string
  code: string
  version: number
  presenceRevision: number
  presenceEpoch: string
  status: string
  joinLocked: boolean
  capacity: number
  youId: string
  facilitatorId: string
  participants: ParticipantView[]
  allowedActions: string[]
}

export interface EntryRequest {
  commandId: string
  displayName: string
  code?: string | null
}

export interface RoomCommand {
  commandId: string
  expectedVersion: number
  action: string
  targetId?: string | null
  locked?: boolean | null
}

export interface CommandResult {
  snapshot: RoomSnapshot
  appliedVersion: number
  replayed: boolean
}

export interface SessionView {
  requestToken: string
}

export interface RoomErrorView {
  code: string
  message: string
}
