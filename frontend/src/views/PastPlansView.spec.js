import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import PastPlansView from './PastPlansView.vue'
import { makeMealPlanListItem } from '@/test/factories'
import { runInTimeZone, WEST_OF_UTC } from '@/test/timeZone'

vi.mock('@/services/api', () => ({
  default: { get: vi.fn().mockResolvedValue({ data: [] }) },
  assetUrl: vi.fn((p) => p ?? ''),
}))

const vuetify = createVuetify({ components, directives })

let pinia

beforeEach(() => {
  pinia = createPinia()
  setActivePinia(pinia)
  vi.clearAllMocks()
})

async function mountView() {
  const { useMealPlanStore } = await import('@/stores/mealPlans')
  const store = useMealPlanStore()
  vi.spyOn(store, 'fetchPlans').mockResolvedValue(undefined)

  const wrapper = mount(PastPlansView, {
    global: {
      plugins: [vuetify, pinia],
      stubs: {
        RouterLink: { template: '<a><slot/></a>' },
        'router-link': { template: '<a><slot/></a>' },
        teleport: true,
      },
    },
  })
  await flushPromises()
  return { wrapper, store }
}

describe('PastPlansView', () => {
  it('shows empty state when no past plans', async () => {
    const { wrapper, store } = await mountView()
    store.plans = []
    await wrapper.vm.$nextTick()
    expect(wrapper.text()).toContain('No past meal plans')
  })

  it('lists only non-active plans', async () => {
    const { wrapper, store } = await mountView()
    store.plans = [
      makeMealPlanListItem({ id: 'p1', name: 'Closed Plan', isActive: false }),
      makeMealPlanListItem({ id: 'p2', name: 'Active Plan', isActive: true }),
    ]
    await wrapper.vm.$nextTick()
    expect(wrapper.text()).toContain('Closed Plan')
    expect(wrapper.text()).not.toContain('Active Plan')
  })

  it('shows recipe count for each plan', async () => {
    const { wrapper, store } = await mountView()
    store.plans = [makeMealPlanListItem({ isActive: false, plannedMealCount: 4 })]
    await wrapper.vm.$nextTick()
    expect(wrapper.text()).toContain('4 recipes')
  })

  describe('west of UTC', () => {
    runInTimeZone(WEST_OF_UTC)

    it('shows the scheduled date range as stored', async () => {
      const { wrapper, store } = await mountView()
      store.plans = [makeMealPlanListItem({ firstScheduledDate: '2026-06-02', lastScheduledDate: '2026-06-05' })]
      await wrapper.vm.$nextTick()

      expect(wrapper.text()).toMatch(/2 June 2026\s*– 5 June 2026/)
    })
  })

  it('shows a loading skeleton while loading', async () => {
    const { wrapper, store } = await mountView()
    store.loading = true
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.v-skeleton-loader').exists()).toBe(true)
  })
})
