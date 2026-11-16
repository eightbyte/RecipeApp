import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import LlmCostsView from './LlmCostsView.vue'
import { makeLlmUsage, makeLlmUsageTotal, makeLlmCall } from '@/test/factories'
import { runInTimeZone } from '@/test/timeZone'

vi.mock('@/services/api', () => ({
  default: { get: vi.fn() },
  assetUrl: vi.fn((path) => path ?? ''),
}))

import api from '@/services/api'

const vuetify = createVuetify({ components, directives })

const NOW = new Date('2026-11-15T12:00:00.000Z')

const requestedParams = () => api.get.mock.calls.map(([, config]) => config.params)
const lastParams = () => requestedParams().at(-1)

function deferred() {
  let resolve
  const promise = new Promise((res) => { resolve = res })
  return { promise, resolve }
}

beforeEach(() => {
  setActivePinia(createPinia())
  vi.clearAllMocks()
  vi.useFakeTimers({ toFake: ['Date'], now: NOW })
  api.get.mockResolvedValue({ data: makeLlmUsage() })
})

afterEach(() => vi.useRealTimers())

async function mountView() {
  const wrapper = mount(LlmCostsView, { global: { plugins: [vuetify] } })
  await flushPromises()
  return wrapper
}

async function goToPage(wrapper, page) {
  const button = wrapper.findComponent({ name: 'VPagination' }).find(`[aria-label="Go to page ${page}"]`)
  await button.trigger('click')
  await flushPromises()
}

async function clickChip(wrapper, label) {
  const chip = wrapper.findAllComponents({ name: 'VChip' }).find((c) => c.text() === label)
  await chip.trigger('click')
  await flushPromises()
}

describe('LlmCostsView', () => {
  describe('range', () => {
    it('opens on the last 30 days, page 1, sending no `to`', async () => {
      await mountView()

      expect(api.get).toHaveBeenCalledTimes(1)
      expect(api.get.mock.calls[0][0]).toBe('/llm/usage')
      expect(lastParams()).toEqual({ from: '2026-10-16T12:00:00.000Z', page: 1, pageSize: 20 })
    })

    it.each([
      ['7 days',   '2026-11-08T12:00:00.000Z'],
      ['30 days',  '2026-10-16T12:00:00.000Z'],
      ['90 days',  '2026-08-17T12:00:00.000Z'],
      ['All time', '1970-01-01T00:00:00.000Z'],
    ])('asks %s back from now as `from`, sending no `to`', async (label, from) => {
      const wrapper = await mountView()
      // Start from a different chip so choosing the 30-day default is a change too.
      await clickChip(wrapper, label === '7 days' ? '90 days' : '7 days')

      await clickChip(wrapper, label)

      expect(lastParams()).toEqual({ from, page: 1, pageSize: 20 })
    })

    it('goes back to page 1 when the range changes', async () => {
      api.get.mockResolvedValue({ data: makeLlmUsage({ totalCount: 65 }) })
      const wrapper = await mountView()
      await goToPage(wrapper, 3)

      await clickChip(wrapper, '7 days')

      expect(lastParams()).toEqual({ from: '2026-11-08T12:00:00.000Z', page: 1, pageSize: 20 })
      expect(wrapper.findComponent({ name: 'VPagination' }).props('modelValue')).toBe(1)
    })
  })

  describe('summary', () => {
    it('adds up the groups into a grand total over every call in the range', async () => {
      api.get.mockResolvedValue({ data: makeLlmUsage({
        totals: [
          makeLlmUsageTotal({ provider: 'modelServer', model: 'qwen', estimatedCostUsd: 0 }),
          makeLlmUsageTotal({ estimatedCostUsd: 1.25 }),
          makeLlmUsageTotal({ operation: 'ScrapeText', estimatedCostUsd: 0.5 }),
        ],
        totalCount: 42,
      }) })
      const wrapper = await mountView()

      const total = wrapper.find('[data-test="grand-total"]')
      expect(total.text()).toContain('$1.75')
      expect(total.text()).toContain('42 calls')
    })

    it('shows a grand total under a cent to 4 decimals', async () => {
      api.get.mockResolvedValue({ data: makeLlmUsage({
        totals: [makeLlmUsageTotal({ estimatedCostUsd: 0.0042 })],
      }) })
      const wrapper = await mountView()

      expect(wrapper.find('[data-test="grand-total"]').text()).toContain('$0.0042')
    })

    it('flags each group with unpriced calls, and counts them all in the grand-total note', async () => {
      api.get.mockResolvedValue({ data: makeLlmUsage({
        totals: [
          makeLlmUsageTotal({ model: 'priced', unpricedCalls: 0 }),
          makeLlmUsageTotal({ model: 'new-a', unpricedCalls: 1 }),
          makeLlmUsageTotal({ model: 'new-b', unpricedCalls: 2 }),
        ],
      }) })
      const wrapper = await mountView()

      const flagged = wrapper.findAll('[data-test="usage-group"]')
        .map((group) => group.find('[data-test="unpriced-flag"]').exists())
      expect(flagged).toEqual([false, true, true])
      expect(wrapper.find('[data-test="grand-total"]').text()).toContain('Excludes 3 unpriced calls')
    })

    it('shows a group whose calls are all unpriced as Unpriced, not as costing nothing', async () => {
      api.get.mockResolvedValue({ data: makeLlmUsage({
        totals: [makeLlmUsageTotal({ calls: 2, unpricedCalls: 2, estimatedCostUsd: 0 })],
      }) })
      const wrapper = await mountView()

      const group = wrapper.find('[data-test="usage-group"]')
      expect(group.text()).toContain('Unpriced')
      expect(group.text()).not.toContain('$0.00')
    })

    it('leaves out the unpriced note when every call is priced', async () => {
      const wrapper = await mountView()

      expect(wrapper.find('[data-test="usage-group"]').exists()).toBe(true)
      expect(wrapper.find('[data-test="unpriced-flag"]').exists()).toBe(false)
      expect(wrapper.text()).not.toContain('unpriced')
    })

    it('labels each group by provider, model and operation in the order the API gives', async () => {
      api.get.mockResolvedValue({ data: makeLlmUsage({
        totals: [
          makeLlmUsageTotal({ provider: 'anthropic', model: 'claude-sonnet-5-5', operation: 'ScrapeUrl' }),
          makeLlmUsageTotal({ provider: 'modelServer', model: null, operation: 'SeedNormalise' }),
        ],
      }) })
      const wrapper = await mountView()

      const groups = wrapper.findAll('[data-test="usage-group"]').map((group) => group.text())
      expect(groups[0]).toContain('Claude (Anthropic) · claude-sonnet-5-5 · Scrape (URL)')
      expect(groups[1]).toContain('Model Server · Unknown model · Seed normalise')
    })
  })

  describe('call list', () => {
    it('shows each call as when, operation and cost, then model, outcome, tokens and duration, then its source', async () => {
      api.get.mockResolvedValue({ data: makeLlmUsage({
        items: [makeLlmCall({
          createdAt: '2026-11-15T09:56:00Z', operation: 'ScrapeUrl', step: 'IngredientMatching',
          model: null, outcome: 'Timeout', durationMs: 3240,
          inputTokens: 900, cacheReadTokens: 100, cacheWriteTokens: null, outputTokens: null,
          estimatedCostUsd: null, sourceUrl: 'https://example.com/recipes/soup?ref=feed',
        })],
      }) })
      const wrapper = await mountView()

      const call = wrapper.find('[data-test="llm-call"]')
      expect(call.text()).toContain('Scrape (URL) / Ingredient matching')
      expect(call.text()).toContain('Unpriced')
      expect(call.text()).toContain('Unknown model')
      expect(call.text()).toContain('in 1,000 · out 0')
      expect(call.text()).toContain('3.2 s')
      expect(call.find('.text-error').text()).toBe('Timeout')
      const link = call.find('a')
      expect(link.attributes()).toMatchObject({
        href: 'https://example.com/recipes/soup?ref=feed', target: '_blank', rel: 'noopener',
      })
      expect(link.text()).toBe('example.com/recipes/soup')
    })

    it('shows a Model Server call costing nothing as $0.00, with no step or source', async () => {
      api.get.mockResolvedValue({ data: makeLlmUsage({
        items: [makeLlmCall({ provider: 'modelServer', operation: 'SeedNormalise', step: null, estimatedCostUsd: 0 })],
      }) })
      const wrapper = await mountView()

      const call = wrapper.find('[data-test="llm-call"]')
      expect(call.text()).toContain('$0.00')
      expect(call.text()).not.toContain('/')
      expect(call.find('a').exists()).toBe(false)
      expect(call.find('.text-error').exists()).toBe(false)
    })

    describe('in UTC', () => {
      runInTimeZone('UTC')

      it('shows when the call was made as a short day and 24-hour time', async () => {
        const wrapper = await mountView()

        expect(wrapper.find('[data-test="llm-call"]').text()).toContain('15 Nov, 09:56')
      })
    })
  })

  describe('states', () => {
    it('keeps the range chips and shows the empty state when the range has no calls', async () => {
      api.get.mockResolvedValue({ data: makeLlmUsage({ totals: [], items: [], totalCount: 0 }) })
      const wrapper = await mountView()

      expect(wrapper.text()).toContain('No LLM calls in this range')
      expect(wrapper.findAllComponents({ name: 'VChip' })).toHaveLength(4)
      expect(wrapper.find('[data-test="grand-total"]').exists()).toBe(false)
      expect(wrapper.findComponent({ name: 'VPagination' }).exists()).toBe(false)
    })

    it('shows the error, and Retry asks again for the same range and page', async () => {
      api.get.mockResolvedValue({ data: makeLlmUsage({ totalCount: 65 }) })
      const wrapper = await mountView()
      await clickChip(wrapper, '7 days')
      await goToPage(wrapper, 2)
      api.get.mockRejectedValueOnce({ message: 'Network error' })
      await goToPage(wrapper, 3)

      expect(wrapper.text()).toContain('Network error')
      await wrapper.findComponent({ name: 'ErrorState' }).find('button').trigger('click')
      await flushPromises()

      expect(lastParams()).toEqual({ from: '2026-11-08T12:00:00.000Z', page: 3, pageSize: 20 })
      expect(wrapper.text()).not.toContain('Network error')
    })

    it('aborts the request in flight when the range changes, and ignores its response', async () => {
      const stale = deferred()
      api.get.mockReturnValueOnce(stale.promise)
        .mockResolvedValueOnce({ data: makeLlmUsage({ totalCount: 7 }) })
      const wrapper = mount(LlmCostsView, { global: { plugins: [vuetify] } })

      await clickChip(wrapper, '7 days')
      stale.resolve({ data: makeLlmUsage({ totalCount: 99 }) })
      await flushPromises()

      expect(api.get.mock.calls[0][1].signal.aborted).toBe(true)
      expect(wrapper.find('[data-test="grand-total"]').text()).toContain('7 calls')
    })
  })

  describe('paging', () => {
    it.each([
      [1, 1], [20, 1], [21, 2], [65, 4],
    ])('shows %i calls over %i pages', async (totalCount, length) => {
      api.get.mockResolvedValue({ data: makeLlmUsage({ totalCount }) })
      const wrapper = await mountView()

      expect(wrapper.findComponent({ name: 'VPagination' }).props('length')).toBe(length)
    })

    it('asks for the chosen page in the same range', async () => {
      api.get.mockResolvedValue({ data: makeLlmUsage({ totalCount: 65 }) })
      const wrapper = await mountView()

      await goToPage(wrapper, 3)

      expect(lastParams()).toEqual({ from: '2026-10-16T12:00:00.000Z', page: 3, pageSize: 20 })
    })
  })
})
