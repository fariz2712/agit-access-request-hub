import { useEffect, useState } from 'react'
import { api, errorMessage } from './api'

type Query<T> = { state: 'loading' } | { state: 'error'; message: string } | { state: 'ready'; data: T }

export function useQuery<T>(path: string, email?: string, revision = 0): Query<T> {
  const key = JSON.stringify([path, email, revision])
  const [result, setResult] = useState<{ key: string; query: Query<T> }>()
  useEffect(() => {
    const controller = new AbortController()
    api<T>(path, email, { signal: controller.signal }).then(
      data => {
        if (!controller.signal.aborted) setResult({ key, query: { state: 'ready', data } })
      },
      error => {
        if (!controller.signal.aborted) setResult({ key, query: { state: 'error', message: errorMessage(error) } })
      },
    )
    return () => controller.abort()
  }, [key, path, email])
  return result?.key === key ? result.query : { state: 'loading' }
}
