import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import MealPlanBuilderView from './MealPlanBuilderView.vue'
import { makeMealPlanDetail, makePlannedMeal } from '@/test/factories'
import api from '@/services/api'

vi.mock('@/services/api', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn() },
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
  vi.spyOn(store, 'createPlan').mockResolvedValue('new-plan-id')
  vi.spyOn(store, 'fetchSuggestions').mockResolvedValue(undefined)

  const wrapper = mount(MealPlanBuilderView, {
    global: {
      plugins: [vuetify, pinia],
      stubs: {
        RouterLink: { template: '<a><slot/></a>' },
        'router-link': { template: '<a><slot/></a>' },
        RecipeBrowser: { template: '<div class="stub-browser" />' },
        SuggestionsPanel: { template: '<div class="stub-suggestions" />' },
        teleport: true,
      },
    },
  })
  await flushPromises()
  return { wrapper, store }
}

describe('MealPlanBuilderView', () => {
  it('shows plan name entry phase initially', async () => {
    const { wrapper } = await mountView()
    expect(wrapper.text()).toContain('Plan name')
    expect(wrapper.text()).toContain('Create plan')
  })

  it('calls createPlan when Create plan button is clicked', async () => {
    const { wrapper, store } = await mountView()

    const input = wrapper.find('input[type="text"], input:not([type])')
    await input.setValue('Week of June')

    const btn = wrapper.findAll('button').find(b => b.text().includes('Create plan'))
    await btn?.trigger('click')
    await flushPromises()

    expect(store.createPlan).toHaveBeenCalledWith('Week of June')
  })

  it('shows builder phase after plan is created', async () => {
    const { wrapper, store } = await mountView()
    store.currentPlan = makeMealPlanDetail({ name: 'My Plan' })
    await wrapper.vm.$nextTick()

    expect(wrapper.text()).toContain('My Plan')
    expect(wrapper.find('.stub-browser').exists()).toBe(true)
  })

  it('resets a stale currentPlan to start at name entry', async () => {
    const { useMealPlanStore } = await import('@/stores/mealPlans')
    const store = useMealPlanStore()
    store.currentPlan = makeMealPlanDetail({ name: 'Stale Plan' })

    const { wrapper } = await mountView()

    expect(store.currentPlan).toBeNull()
    expect(wrapper.text()).toContain('Plan name')
  })

  describe('editing a Planned Meal', () => {
    const plannedMealPath = '/meal-plans/plan-1/planned-meals/planned-meal-1'

    async function mountWithPlannedMeal() {
      const mounted = await mountView()
      const plan = makeMealPlanDetail({
        id: 'plan-1',
        plannedMeals: [makePlannedMeal({ scheduledDate: '2026-06-03', batchSize: 'HALF' })],
      })
      api.put.mockResolvedValue({ data: {} })
      api.get.mockResolvedValue({ data: plan })
      mounted.store.currentPlan = plan
      await mounted.wrapper.vm.$nextTick()
      return mounted
    }

    it('keeps the Batch Size when the date changes', async () => {
      const { wrapper } = await mountWithPlannedMeal()

      await wrapper.find('input[type="date"]').setValue('2026-06-05')
      await flushPromises()

      expect(api.put).toHaveBeenCalledWith(plannedMealPath, { scheduledDate: '2026-06-05', batchSize: 'HALF' })
    })

    it('keeps the date when the Batch Size changes', async () => {
      const { wrapper } = await mountWithPlannedMeal()

      const doubleButton = wrapper.findAll('button').find(button => button.text() === '2×')
      await doubleButton.trigger('click')
      await flushPromises()

      expect(api.put).toHaveBeenCalledWith(plannedMealPath, { scheduledDate: '2026-06-03', batchSize: 'DOUBLE' })
    })
  })

  it('warns that creating a new plan closes the current active plan', async () => {
    const { useMealPlanStore } = await import('@/stores/mealPlans')
    const store = useMealPlanStore()
    store.activePlan = makeMealPlanDetail({ name: 'Current Week' })

    const { wrapper } = await mountView()

    expect(wrapper.text()).toContain('closes your current active plan')
    expect(wrapper.text()).toContain('Current Week')
  })
})
