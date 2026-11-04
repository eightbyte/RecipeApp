import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import MealPlanDetailView from './MealPlanDetailView.vue'
import { makeMealPlanDetail, makePlannedMeal } from '@/test/factories'
import { runInTimeZone, WEST_OF_UTC } from '@/test/timeZone'

vi.mock('@/services/api', () => ({
  default: { get: vi.fn(), post: vi.fn() },
  assetUrl: vi.fn((p) => p ?? ''),
}))

const vuetify = createVuetify({ components, directives })

let pinia

beforeEach(() => {
  pinia = createPinia()
  setActivePinia(pinia)
  vi.clearAllMocks()
})

async function mountView(id = 'plan-1') {
  const { useMealPlanStore } = await import('@/stores/mealPlans')
  const store = useMealPlanStore()
  vi.spyOn(store, 'fetchPlan').mockResolvedValue(undefined)

  const wrapper = mount(MealPlanDetailView, {
    props: { id },
    global: {
      plugins: [vuetify, pinia],
      stubs: { teleport: true },
    },
  })
  await flushPromises()
  return { wrapper, store }
}

describe('MealPlanDetailView', () => {
  it('shows "Plan not found" when currentPlan is null', async () => {
    const { wrapper, store } = await mountView()
    store.currentPlan = null
    await wrapper.vm.$nextTick()
    expect(wrapper.text()).toContain('Plan not found')
  })

  it('renders plan name and recipe names', async () => {
    const { wrapper, store } = await mountView()
    store.currentPlan = makeMealPlanDetail({
      name: 'June Week',
      plannedMeals: [makePlannedMeal({ recipeName: 'Risotto' })],
    })
    await wrapper.vm.$nextTick()
    expect(wrapper.text()).toContain('June Week')
    expect(wrapper.text()).toContain('Risotto')
  })

  describe('west of UTC', () => {
    runInTimeZone(WEST_OF_UTC)

    it('shows a Planned Meal’s scheduled day and month as stored', async () => {
      const { wrapper, store } = await mountView()
      store.currentPlan = makeMealPlanDetail({ plannedMeals: [makePlannedMeal({ scheduledDate: '2026-06-03' })] })
      await wrapper.vm.$nextTick()

      const dateTexts = wrapper.findAll('.text-center .text-caption').map(el => el.text())
      expect(dateTexts).toEqual(['3', 'Jun'])
    })
  })

  it('calls fetchPlan with the id prop on mount', async () => {
    const { store } = await mountView('test-id-123')
    expect(store.fetchPlan).toHaveBeenCalledWith('test-id-123')
  })

  it('shows a loading skeleton while loading', async () => {
    const { wrapper, store } = await mountView()
    store.loading = true
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.v-skeleton-loader').exists()).toBe(true)
  })

  it('shows an ErrorState with retry on fetch error', async () => {
    const { wrapper, store } = await mountView()
    store.loading = false
    store.error = 'Boom'
    store.currentPlan = null
    await wrapper.vm.$nextTick()

    expect(wrapper.text()).toContain('Boom')
    const retry = wrapper.findAll('button').find(b => b.text().includes('Try again'))
    store.fetchPlan.mockClear()
    await retry.trigger('click')
    expect(store.fetchPlan).toHaveBeenCalledWith('plan-1')
  })
})
