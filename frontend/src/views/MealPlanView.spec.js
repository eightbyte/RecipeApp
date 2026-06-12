import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import MealPlanView from './MealPlanView.vue'
import { makeMealPlanDetail, makeMealPlanRecipe } from '@/test/factories'

const pushMock = vi.fn()

vi.mock('@/services/api', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
  assetUrl: vi.fn((p) => p ?? ''),
}))

vi.mock('vue-router', async (importActual) => {
  const actual = await importActual()
  return { ...actual, useRouter: () => ({ push: pushMock }) }
})

const vuetify = createVuetify({ components, directives })

let pinia

beforeEach(() => {
  pinia = createPinia()
  setActivePinia(pinia)
  vi.clearAllMocks()
})

async function mountView() {
  const { useMealPlanStore } = await import('@/stores/mealPlans')
  const mealPlanStore = useMealPlanStore()
  vi.spyOn(mealPlanStore, 'fetchActivePlan').mockResolvedValue(undefined)

  const wrapper = mount(MealPlanView, {
    global: {
      plugins: [vuetify, pinia],
      stubs: {
        RouterLink: { template: '<a><slot/></a>' },
        'router-link': { template: '<a><slot/></a>' },
        teleport: true,
        RecipeBrowser: true,
      },
    },
  })
  await flushPromises()
  return { wrapper, mealPlanStore }
}

describe('MealPlanView', () => {
  it('shows a loading skeleton while the active plan is loading', async () => {
    const { wrapper, mealPlanStore } = await mountView()
    mealPlanStore.loading = true
    mealPlanStore.activePlan = null
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.v-skeleton-loader').exists()).toBe(true)
  })

  it('shows an empty state when there is no active plan', async () => {
    const { wrapper, mealPlanStore } = await mountView()
    mealPlanStore.loading = false
    mealPlanStore.activePlan = null
    await wrapper.vm.$nextTick()
    expect(wrapper.text()).toContain('No active meal plan')
  })

  it('gives the overflow trigger an accessible name', async () => {
    const { wrapper, mealPlanStore } = await mountView()
    mealPlanStore.activePlan = makeMealPlanDetail({ recipes: [makeMealPlanRecipe()] })
    await wrapper.vm.$nextTick()
    expect(wrapper.html()).toContain('aria-label="Meal options"')
  })

  it('exposes a Plan options menu when a plan is active', async () => {
    const { wrapper, mealPlanStore } = await mountView()
    mealPlanStore.activePlan = makeMealPlanDetail({ recipes: [makeMealPlanRecipe()] })
    await wrapper.vm.$nextTick()
    expect(wrapper.html()).toContain('aria-label="Plan options"')
  })

  it('Start new plan navigates to the meal-plan builder', async () => {
    const { wrapper } = await mountView()
    wrapper.vm.startNewPlan()
    expect(pushMock).toHaveBeenCalledWith({ name: 'meal-plan-create' })
  })

  it('Start cooking navigates to the cooking route carrying the meal portion', async () => {
    const { wrapper } = await mountView()
    wrapper.vm.startCooking({ recipeId: 'r9', portionSize: 'DOUBLE' })
    expect(pushMock).toHaveBeenCalledWith({
      name: 'recipe-cooking',
      params: { id: 'r9' },
      query: { portion: 'DOUBLE' },
    })
  })

  it('Mark as cooked marks the recipe, refreshes the plan and notifies', async () => {
    const { wrapper, mealPlanStore } = await mountView()
    const { useRecipeStore } = await import('@/stores/recipes')
    const { useUiStore } = await import('@/stores/ui')
    const recipesStore = useRecipeStore()
    const ui = useUiStore()
    vi.spyOn(recipesStore, 'markCooked').mockResolvedValue({})
    const refreshSpy = vi.spyOn(mealPlanStore, 'fetchActivePlan').mockResolvedValue(undefined)

    await wrapper.vm.markMealCooked({ recipeId: 'r9', recipeName: 'Soup' })

    expect(recipesStore.markCooked).toHaveBeenCalledWith('r9')
    expect(refreshSpy).toHaveBeenCalled()
    expect(ui.snackbar.show).toBe(true)
    expect(ui.snackbar.color).toBe('success')
  })
})
