import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import MealPlanBuilderView from './MealPlanBuilderView.vue'
import { makeMealPlanDetail, makePlannedMeal } from '@/test/factories'
import { servingsFromQuery } from '@/router/recipeRoutes'
import api from '@/services/api'

const pushMock    = vi.fn()
const replaceMock = vi.fn()
const route       = { query: {} }

vi.mock('@/services/api', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
  assetUrl: vi.fn((p) => p ?? ''),
}))

vi.mock('vue-router', async (importActual) => {
  const actual = await importActual()
  return { ...actual, useRouter: () => ({ push: pushMock, replace: replaceMock }), useRoute: () => route }
})

const vuetify = createVuetify({ components, directives })

let pinia

beforeEach(() => {
  pinia = createPinia()
  setActivePinia(pinia)
  vi.clearAllMocks()
  route.query = {}
})

afterEach(() => vi.useRealTimers())

async function mountView() {
  const { useMealPlanStore } = await import('@/stores/mealPlans')
  const store = useMealPlanStore()
  vi.spyOn(store, 'createPlan').mockImplementation(async (name) => {
    store.currentPlan = makeMealPlanDetail({ id: 'new-plan-id', name })
    return 'new-plan-id'
  })
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

  it('prefills an editable plan name for the week starting today', async () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date(2026, 10, 6, 9, 30))

    const { wrapper } = await mountView()

    const input = wrapper.find('input[type="text"], input:not([type])')
    expect(input.element.value).toBe('Week of 6 November')
    expect(input.attributes('readonly')).toBeUndefined()
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

  it('puts the created plan’s id in the URL, so back or a reload return to it', async () => {
    const { wrapper } = await mountView()

    await wrapper.findAll('button').find(b => b.text().includes('Create plan')).trigger('click')
    await flushPromises()

    expect(replaceMock).toHaveBeenCalledWith({ name: 'meal-plan-create', query: { plan: 'new-plan-id' } })
  })

  // The new URL mounts a fresh builder, so a recipe list shown here would be torn down mid-fetch.
  it('leaves the recipe list to the builder at the new URL', async () => {
    const { wrapper, store } = await mountView()

    await wrapper.findAll('button').find(b => b.text().includes('Create plan')).trigger('click')
    await flushPromises()

    expect(wrapper.find('.stub-browser').exists()).toBe(false)
    expect(store.fetchSuggestions).not.toHaveBeenCalled()
  })

  it('returns to the plan being built when its id is in the URL', async () => {
    route.query = { plan: 'plan-1' }
    api.get.mockResolvedValue({ data: makeMealPlanDetail({ id: 'plan-1', name: 'Week of 6 November' }) })

    const { wrapper, store } = await mountView()

    expect(api.get).toHaveBeenCalledWith('/meal-plans/plan-1')
    expect(store.fetchSuggestions).toHaveBeenCalledWith('plan-1')
    expect(wrapper.find('h2').text()).toBe('Week of 6 November')
    expect(wrapper.find('.stub-browser').exists()).toBe(true)
  })

  it('starts at name entry when the plan in the URL is not found', async () => {
    route.query = { plan: 'gone' }
    api.get.mockRejectedValue({ status: 404, message: 'Not found' })

    const { wrapper } = await mountView()

    expect(wrapper.text()).toContain('Create plan')
    expect(replaceMock).toHaveBeenCalledWith({ name: 'meal-plan-create' })
  })

  it('shows an error with a retry when the plan in the URL fails to load', async () => {
    route.query = { plan: 'plan-1' }
    api.get
      .mockRejectedValueOnce({ status: 500, message: 'Server unavailable' })
      .mockResolvedValue({ data: makeMealPlanDetail({ id: 'plan-1', name: 'Week of 6 November' }) })

    const { wrapper } = await mountView()

    expect(wrapper.text()).toContain('Server unavailable')
    expect(wrapper.text()).not.toContain('Create plan')

    await wrapper.findAll('button').find(b => b.text().includes('Try again')).trigger('click')
    await flushPromises()

    expect(wrapper.find('h2').text()).toBe('Week of 6 November')
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
      const plan = makeMealPlanDetail({
        id: 'plan-1',
        plannedMeals: [makePlannedMeal({ scheduledDate: '2026-06-03', batchSize: 'HALF' })],
      })
      route.query = { plan: 'plan-1' }
      api.put.mockResolvedValue({ data: {} })
      api.get.mockResolvedValue({ data: plan })
      return mountView()
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

    it('opens the recipe at the Planned Meal’s Batch Size when its row is tapped', async () => {
      const { wrapper } = await mountWithPlannedMeal()

      await wrapper.findAll('div').find(el => el.text() === 'Test Recipe').trigger('click')

      const [location] = pushMock.mock.calls[0]
      expect(location).toMatchObject({ name: 'recipe-detail', params: { id: 'test-recipe-1' } })
      // A Half Batch Size of a 4-serving recipe shows 2 servings.
      expect(servingsFromQuery(location.query, 4)).toBe(2)
    })

    it.each([
      ['date field', (wrapper) => wrapper.find('input[type="date"]')],
      ['Batch Size toggle', (wrapper) => wrapper.findAll('button').find(button => button.text() === '2×')],
      ['delete button', (wrapper) => wrapper.find('button .mdi-delete')],
    ])('keeps the %s from opening the recipe', async (_, control) => {
      api.delete.mockResolvedValue({ data: {} })
      const { wrapper } = await mountWithPlannedMeal()

      await control(wrapper).trigger('click')
      await flushPromises()

      expect(pushMock).not.toHaveBeenCalled()
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
