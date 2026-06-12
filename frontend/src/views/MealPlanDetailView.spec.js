import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import MealPlanDetailView from './MealPlanDetailView.vue'
import { makeMealPlanDetail, makeMealPlanRecipe } from '@/test/factories'

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
      recipes: [makeMealPlanRecipe({ recipeName: 'Risotto' })],
    })
    await wrapper.vm.$nextTick()
    expect(wrapper.text()).toContain('June Week')
    expect(wrapper.text()).toContain('Risotto')
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
