import { setActivePinia, createPinia } from 'pinia'
import { useLlmUsageStore } from './llmUsage'

vi.mock('@/services/api', () => ({
  default: { get: vi.fn() },
  assetUrl: vi.fn((path) => path ?? ''),
}))

import api from '@/services/api'

beforeEach(() => {
  setActivePinia(createPinia())
  vi.clearAllMocks()
})

describe('fetchUsage', () => {
  it('asks GET /llm/usage for the given range and page, and returns the response', async () => {
    const usage = { totals: [], items: [], totalCount: 0 }
    api.get.mockResolvedValue({ data: usage })
    const { signal } = new AbortController()

    const result = await useLlmUsageStore().fetchUsage({
      from: '2026-11-08T00:00:00.000Z', page: 2, pageSize: 20, signal,
    })

    expect(api.get).toHaveBeenCalledWith('/llm/usage', {
      params: { from: '2026-11-08T00:00:00.000Z', page: 2, pageSize: 20 },
      signal,
    })
    expect(result).toBe(usage)
  })
})
