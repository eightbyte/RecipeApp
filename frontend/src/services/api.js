/**
 * Axios instance pre-configured for the RecipeApp API.
 *
 * Base URL is read from VITE_API_BASE_URL environment variable:
 *  - Development:  http://localhost:5000/api/v1  (direct to .NET API)
 *  - Production:   /api/v1                        (proxied via Nginx)
 */
import axios from 'axios'

// Strip the /api/v1 path so we have just the origin for static asset URLs
const _apiHost = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/api\/v\d+\/?$/, '')

export function assetUrl(path) {
  if (!path) return ''
  if (/^https?:\/\//.test(path)) return path
  return `${_apiHost}${path}`
}

const api = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? '/api/v1',
  headers: {
    'Content-Type': 'application/json',
    Accept: 'application/json',
  },
  // timeout: 15_000,
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
    const message = error.response?.data?.detail ?? error.response?.data?.message ?? error.message ?? 'Unknown error'

    // Surface a simple error object so callers don't have to dig into Axios internals
    return Promise.reject({ status, message, original: error })
  },
)

export default api
