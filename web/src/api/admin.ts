import type * as C from './room-contract.generated'
export type {
  AdminSessionView,
  AdminRoomView,
  AdminRoomPage,
  AdminRoomCommand,
  AdminPromptCommand,
  AdminPromptPage,
  AdminAuditPage,
} from './room-contract.generated'
export class AdminError extends Error {
  constructor(
    message: string,
    public status = 0,
    public code = 'request_failed',
  ) {
    super(message)
  }
}
export async function adminRead<T>(path: string, payload?: unknown): Promise<T> {
  try {
    const headers: Record<string, string> = {}
    if (payload !== undefined) {
      const session = await adminSession()
      headers['Content-Type'] = 'application/json'
      headers['X-CSRF-TOKEN'] = session.requestToken
    }
    const response = await fetch(`/api/admin${path}`, {
      method: payload === undefined ? 'GET' : 'POST',
      headers,
      ...(payload === undefined ? {} : { body: JSON.stringify(payload) }),
      signal: AbortSignal.timeout(15_000),
    })
    if (!response.ok) {
      const error = (await response.json().catch(() => ({}))) as { message?: string; code?: string }
      throw new AdminError(
        error.message ??
          (response.status === 401
            ? 'Your admin session ended; sign in again'
            : 'The request failed'),
        response.status,
        error.code,
      )
    }
    return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
  } catch (error) {
    if (error instanceof AdminError) throw error
    throw new AdminError(
      'The response was not received. Retry the same action to check its result.',
    )
  }
}
export async function adminSession(): Promise<C.AdminSessionView> {
  const result = await adminRead<C.AdminSessionView>('/session')
  if (
    typeof result.enabled !== 'boolean' ||
    typeof result.authenticated !== 'boolean' ||
    typeof result.requestToken !== 'string'
  )
    throw new AdminError('The admin session response could not be read')
  return result
}
