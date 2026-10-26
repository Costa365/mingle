import { afterEach, expect, it, vi } from 'vitest'
import { adminRead, adminSession, AdminError } from './admin'
afterEach(() => vi.unstubAllGlobals())
it('uses the administrator CSRF identity for mutations without storing credentials', async () => {
  const fetch = vi
    .fn()
    .mockResolvedValueOnce(
      new Response(
        JSON.stringify({ enabled: true, authenticated: true, requestToken: 'admin-token' }),
      ),
    )
    .mockResolvedValueOnce(new Response(null, { status: 204 }))
  vi.stubGlobal('fetch', fetch)
  await adminRead('/logout', {})
  expect(fetch.mock.calls[1][0]).toBe('/api/admin/logout')
  expect(fetch.mock.calls[1][1]).toMatchObject({
    method: 'POST',
    headers: { 'X-CSRF-TOKEN': 'admin-token' },
    body: '{}',
  })
})
it('rejects malformed sessions and preserves status codes for authorization failures', async () => {
  vi.stubGlobal(
    'fetch',
    vi
      .fn()
      .mockResolvedValueOnce(new Response('{}'))
      .mockResolvedValueOnce(new Response('{}', { status: 401 })),
  )
  await expect(adminSession()).rejects.toThrow('could not be read')
  await expect(adminRead('/rooms')).rejects.toMatchObject({ status: 401 })
})
it('treats network failures as ambiguous so callers retain the same command', async () => {
  vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('offline')))
  await expect(adminRead('/rooms')).rejects.toBeInstanceOf(AdminError)
  await expect(adminRead('/rooms')).rejects.toMatchObject({ status: 0 })
})
