/**
 * Axios instance for the RecipeApp API, with its base URL read from VITE_API_BASE_URL
 * (the .NET API directly in development, /api/v1 proxied via Nginx in production).
 */
import axios, { AxiosError } from 'axios'

// Strip the /api/v1 path so we have just the origin for static asset URLs
const _apiHost = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/api\/v\d+\/?$/, '')

export function assetUrl(path) {
  if (!path) return ''
  if (/^https?:\/\//.test(path)) return path
  return `${_apiHost}${path}`
}

/**
 * Readable text for an error response body. An RFC 7807 `detail` comes first. A validation problem carries only
 * `title` and an `errors` map of field to messages, so it reads as the title then each distinct message.
 */
export function problemMessage(data, fallback) {
  if (data?.detail) return data.detail

  const messages = [...new Set(Object.values(data?.errors ?? {}).flat())]
  if (messages.length > 0) return [data.title, ...messages].filter(Boolean).join(' ')

  return data?.message ?? fallback
}

const api = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? '/api/v1',
  headers: {
    'Content-Type': 'application/json',
    Accept: 'application/json',
  },
  // timeout: 15_000,
  // Without this a timeout shares ECONNABORTED with an aborted request.
  transitional: { clarifyTimeoutError: true },
})

// ── Request interceptor ───────────────────────────────────────────────────────
api.interceptors.request.use(
  (config) => config,
  (error) => Promise.reject(error),
)

// ── Response interceptor ─────────────────────────────────────────────────────
api.interceptors.response.use(
  (response) => response,
  (error) => {
    const status = error.response?.status
    const message = problemMessage(error.response?.data, error.message ?? 'Unknown error')

    // Surface a simple error object so callers don't have to dig into Axios internals
    return Promise.reject({ status, message, timedOut: error.code === AxiosError.ETIMEDOUT, original: error })
  },
)

export default api
