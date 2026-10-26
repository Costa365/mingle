// Generated from TeamGames.Core room DTOs. Run the contracts tool to update.

export interface AdminSessionView {
  enabled: boolean
  authenticated: boolean
  requestToken: string
  expiresTicks: number | null
}

export interface AdminLogin {
  credential: string
}

export interface AdminGameView {
  sessionId: string
  gameId: string
  stage: string
  roundNumber: number
}

export interface AdminRoomView {
  id: string
  code: string
  version: number
  status: string
  joinLocked: boolean
  participantCount: number
  lastActivityTicks: number
  facilitatorId: string
  participants: ParticipantView[]
  games: AdminGameView[]
}

export interface AdminRoomPage {
  page: number
  pageSize: number
  total: number
  rooms: AdminRoomView[]
}

export interface AdminRoomCommand {
  commandId: string
  expectedVersion: number
  action: string
  targetId?: string | null
  locked?: boolean | null
  confirmation?: string | null
}

export interface AdminPromptCommand {
  commandId: string
  action: string
  input?: PromptInput | null
  promptId?: string | null
  promptRevision?: number
}

export interface AdminPromptPage {
  page: number
  pageSize: number
  total: number
  prompts: PromptView[]
}

export interface AdminAuditView {
  id: string
  createdTicks: number
  action: string
  targetId: string
}

export interface AdminAuditPage {
  page: number
  pageSize: number
  total: number
  events: AdminAuditView[]
}

export interface AdminCommandResult {
  appliedVersion: number
  replayed: boolean
}

export interface PromptInput {
  gameId: string
  category: string
  tags: string[]
  text: string
  optionA?: string
  optionB?: string
  enabled?: boolean
}

export interface PromptCommand {
  commandId: string
  expectedVersion: number
  action: string
  input?: PromptInput | null
  promptId?: string | null
  promptRevision?: number
}

export interface PromptView {
  id: string
  gameId: string
  category: string
  tags: string[]
  text: string
  optionA: string
  optionB: string
  source: string
  enabled: boolean
  revision: number
  used: boolean
}

export interface PromptPage {
  roomVersion: number
  prompts: PromptView[]
}

export interface StatementSubmission {
  statements: string[]
  lieIndex: number
}

export interface GameOption {
  value: string
  label: string
}

export interface VoteTotal {
  value: string
  label: string
  count: number
  percent: number
  winner: boolean
}

export interface PresenterView {
  participantId: string
  name: string
  statements: string[]
}

export interface PlayerChoiceView {
  participantId: string
  name: string
  choice: string
}

export interface ResponseStatusView {
  participantId: string
  hasResponded: boolean
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
  promptId?: string | null
  category?: string | null
  tag?: string | null
  search?: string | null
  resetPool?: boolean
  submission?: StatementSubmission | null
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
  totals?: VoteTotal[]
  lieIndex?: number | null
  presenterName?: string | null
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
  remainingPrompts?: number
  poolSize?: number
  category?: string
  tag?: string
  search?: string
  usedPromptIds?: string[]
  responses?: ResponseStatusView[]
  options?: GameOption[]
  ownSubmission?: StatementSubmission | null
  presenter?: PresenterView | null
  notice?: string | null
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
