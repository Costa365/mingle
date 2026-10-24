/** Proxy diagnostics must never include invitation or transport-token queries. */
export function redactProxyLog(message: string): string {
  return message.replace(/\?[^\s"'<>]*/g, '?[redacted]')
}
