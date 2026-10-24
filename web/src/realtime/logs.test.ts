import { expect, it } from 'vitest'
import { redactProxyLog } from './logs'

it.each([
  'proxy error: /hubs/rooms?id=private-token&transport=WebSockets\nECONNREFUSED',
  'GET /join?code=private-token HTTP/1.1',
  'referrer "https://mingle.example/join?code=private-token"',
])('keeps diagnostics useful without exposing query credentials: %s', (message) => {
  const safe = redactProxyLog(message)
  expect(safe).not.toContain('private-token')
  expect(safe).toContain('?[redacted]')
  expect(safe.split('?')[0]).toBe(message.split('?')[0])
})
