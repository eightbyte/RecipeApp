import api, { assetUrl } from './api'

// ── assetUrl ──────────────────────────────────────────────────────────────────

describe('assetUrl', () => {
  it('prepends API host to a relative path', () => {
    const result = assetUrl('/uploads/images/foo.jpg')
    // VITE_API_BASE_URL is http://localhost:5000/api/v1 in the test env.
    // assetUrl strips /api/v1, so host is http://localhost:5000
    expect(result).toBe('http://localhost:5000/uploads/images/foo.jpg')
  })

  it('returns an absolute http URL unchanged', () => {
    const url = 'http://cdn.example.com/image.jpg'
    expect(assetUrl(url)).toBe(url)
  })

  it('returns an absolute https URL unchanged', () => {
    const url = 'https://cdn.example.com/image.jpg'
    expect(assetUrl(url)).toBe(url)
  })

  it('returns empty string for null input', () => {
    expect(assetUrl(null)).toBe('')
  })

  it('returns empty string for undefined input', () => {
    expect(assetUrl(undefined)).toBe('')
  })
})

// ── Response error interceptor ────────────────────────────────────────────────
// Access the registered rejection handler directly to test the transformation
// without needing real network calls.

function getResponseRejectionHandler() {
  // axios stores handlers in interceptors.response.handlers[]
  // Each handler has { fulfilled, rejected, runWhen }
  const handlers = api.interceptors.response.handlers
  const handler = handlers.find(h => h !== null && h.rejected)
  return handler?.rejected
}

describe('error interceptor', () => {
  it('rejects with {status, message, original} for a response error', async () => {
    const rejected = getResponseRejectionHandler()
    const axiosError = {
      response: { status: 422, data: { message: 'Validation failed' } },
      message: 'Request failed with status code 422',
    }

    await expect(rejected(axiosError)).rejects.toMatchObject({
      status: 422,
      message: 'Validation failed',
      original: axiosError,
    })
  })

  it('uses error.message when there is no response body message', async () => {
    const rejected = getResponseRejectionHandler()
    const axiosError = {
      response: { status: 500, data: {} },
      message: 'Internal Server Error',
    }

    await expect(rejected(axiosError)).rejects.toMatchObject({
      status: 500,
      message: 'Internal Server Error',
    })
  })

  it('rejects with message for a network error (no response)', async () => {
    const rejected = getResponseRejectionHandler()
    const networkError = {
      message: 'Network Error',
      // no .response property
    }

    await expect(rejected(networkError)).rejects.toMatchObject({
      message: 'Network Error',
    })
  })
})
