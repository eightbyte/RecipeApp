import api, { assetUrl, problemMessage } from './api'

// ── assetUrl ──────────────────────────────────────────────────────────────────

describe('assetUrl', () => {
  it('prepends API host to a relative path', () => {
    const result = assetUrl('/uploads/images/foo.jpg')
    // assetUrl strips /api/v1 from the test env's VITE_API_BASE_URL, leaving the host.
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

// ── problemMessage ────────────────────────────────────────────────────────────

describe('problemMessage', () => {
  const fallback = 'Request failed with status code 400'
  const validation = {
    title: 'One or more validation errors occurred.',
    status: 400,
    errors: {
      'Steps[0].IngredientIndexes': ['Index 18 is out of range for the ingredients list (0–17).'],
      Name: ['Name is required.'],
    },
  }

  it('uses detail when it is present', () => {
    expect(problemMessage({ detail: 'Recipe extraction timed out.' }, fallback)).toBe('Recipe extraction timed out.')
  })

  it('builds the title and every message from an errors map', () => {
    expect(problemMessage(validation, fallback)).toBe(
      'One or more validation errors occurred. ' +
      'Index 18 is out of range for the ingredients list (0–17). Name is required.',
    )
  })

  it('prefers detail over an errors map', () => {
    expect(problemMessage({ ...validation, detail: 'The recipe could not be saved.' }, fallback))
      .toBe('The recipe could not be saved.')
  })

  it('falls back when the body carries neither detail nor errors', () => {
    expect(problemMessage({ title: 'Bad Request', status: 400 }, fallback)).toBe(fallback)
    expect(problemMessage(undefined, fallback)).toBe(fallback)
  })

  it('states a message shared by several fields once', () => {
    const repeated = 'Index 18 is out of range for the ingredients list (0–17).'
    const data = {
      title: 'One or more validation errors occurred.',
      errors: {
        'Steps[0].IngredientIndexes': [repeated, 'Index 19 is out of range for the ingredients list (0–17).'],
        'Steps[8].IngredientIndexes': [repeated],
      },
    }

    expect(problemMessage(data, fallback)).toBe(
      'One or more validation errors occurred. ' +
      'Index 18 is out of range for the ingredients list (0–17). ' +
      'Index 19 is out of range for the ingredients list (0–17).',
    )
  })

  it('keeps the legacy message field ahead of the fallback', () => {
    expect(problemMessage({ message: 'Validation failed' }, fallback)).toBe('Validation failed')
  })
})

// ── Response error interceptor ────────────────────────────────────────────────
// Call the registered rejection handler directly to test the transformation without network calls.

function getResponseRejectionHandler() {
  // axios stores handlers as { fulfilled, rejected, runWhen } in interceptors.response.handlers.
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

  it('prefers response.data.detail (RFC 7807 ProblemDetails) over data.message', async () => {
    const rejected = getResponseRejectionHandler()
    const axiosError = {
      response: { status: 422, data: { detail: 'Recipe extraction timed out. Please try again.' } },
      message: 'Request failed with status code 422',
    }

    await expect(rejected(axiosError)).rejects.toMatchObject({
      status: 422,
      message: 'Recipe extraction timed out. Please try again.',
    })
  })

  it('turns a validation problem into readable text rather than the Axios message', async () => {
    const rejected = getResponseRejectionHandler()
    const axiosError = {
      response: {
        status: 400,
        data: { title: 'One or more validation errors occurred.', errors: { Name: ['Name is required.'] } },
      },
      message: 'Request failed with status code 400',
    }

    await expect(rejected(axiosError)).rejects.toMatchObject({
      status: 400,
      message: 'One or more validation errors occurred. Name is required.',
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
      timedOut: false,
    })
  })

  it('flags a request that ran past its timeout', async () => {
    const rejected = getResponseRejectionHandler()
    const timeoutError = { code: 'ETIMEDOUT', message: 'timeout of 10000ms exceeded' }

    await expect(rejected(timeoutError)).rejects.toMatchObject({ timedOut: true })
  })

  it('reports a timeout as ETIMEDOUT rather than as an aborted request', () => {
    expect(api.defaults.transitional.clarifyTimeoutError).toBe(true)
  })
})
