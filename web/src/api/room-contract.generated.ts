// Generated from TeamGames.Core room DTOs. Run the contracts tool to update.

export interface PlayerChoiceView {
  participantId: string
  name: string
  choice: string
}

export interface GameCommand {
  commandId: string
  action: string
  sessionId?: string | null
  roundId?: string | null
  stageRevision?: number
  responseRevision?: number
  choice?: string | null
  gameId?: string | null
  expectedVersion?: number
}

export interface RoundResult {
  roundId: string
  number: number
  prompt: string
  optionA: string
  optionB: string
  skipped: boolean
  a: number
  b: number
  passes: number
  missing: number
  percentA: number
  percentB: number
  choices: PlayerChoiceView[]
}

export interface GameView {
  sessionId: string
  gameId: string
  roundId: string
  roundNumber: number
  stageRevision: number
  stage: string
  prompt: string
  optionA: string
  optionB: string
  eligibleCount: number
  responseCount: number
  eligible: boolean
  ownChoice: string | null
  responseRevision: number
  allowedActions: string[]
  results: RoundResult[]
}

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
  game?: GameView | null
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
