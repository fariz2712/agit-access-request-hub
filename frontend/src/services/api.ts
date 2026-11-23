export class ApiError extends Error {
  constructor(readonly status: number, message: string) {
    super(message)
  }
}

export async function api<T>(path: string, email?: string, options?: { body?: unknown; signal?: AbortSignal }) {
  const headers = new Headers({ Accept: 'application/json' })
  if (email) headers.set('X-Demo-User', email)
  if (options?.body !== undefined) headers.set('Content-Type', 'application/json')
  const response = await fetch('/api' + path, {
    headers,
    method: options?.body === undefined ? 'GET' : 'POST',
    body: options?.body === undefined ? undefined : JSON.stringify(options.body),
    signal: options?.signal,
    cache: 'no-store',
  })
  const result = await response.json().catch(() => undefined)
  if (!response.ok) {
    throw new ApiError(response.status, result?.detail ?? result?.title ?? 'Request failed (' + response.status + ').')
  }
  if (result === undefined) throw new Error('The server returned an unreadable response.')
  return result as T
}

export function errorMessage(error: unknown) {
  if (error instanceof ApiError) return error.message
  if (error instanceof TypeError) return 'Could not reach the server. Check that the API is running and try again.'
  return error instanceof Error ? error.message : 'An unexpected error occurred.'
}
