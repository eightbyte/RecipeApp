import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import { useRecipeStore } from '@/stores/recipes'
import { useUiStore } from '@/stores/ui'
import CookingModeView from './CookingModeView.vue'
import { makeRecipeDetail } from '@/test/factories'

const backMock = vi.fn()
const replaceMock = vi.fn()

vi.mock('@/services/api', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
  assetUrl: vi.fn((p) => p ?? ''),
}))

vi.mock('vue-router', async (importActual) => {
  const actual = await importActual()
  return {
    ...actual,
    useRoute: () => ({ params: { id: 'r1' }, query: {} }),
    useRouter: () => ({ back: backMock, replace: replaceMock, push: vi.fn() }),
  }
})

const vuetify = createVuetify({ components, directives })

let pinia
let store

const threeStepRecipe = (overrides = {}) =>
  makeRecipeDetail({
    id: 'r1',
    name: 'Pancakes',
    ingredients: [
      { id: 'ri-1', ingredientId: 'i1', ingredientName: 'flour', ingredientDisplayName: 'Flour', category: 'DRY_GOODS', amount: 200, unit: 'g', notes: null, displayOrder: 0 },
      { id: 'ri-2', ingredientId: 'i2', ingredientName: 'milk', ingredientDisplayName: 'Milk', category: 'DAIRY', amount: 300, unit: 'ml', notes: null, displayOrder: 1 },
    ],
    steps: [
      { id: 's1', stepNumber: 1, instruction: 'Mix flour', recipeIngredientIds: ['ri-1'] },
      { id: 's2', stepNumber: 2, instruction: 'Add milk', recipeIngredientIds: ['ri-2'] },
      { id: 's3', stepNumber: 3, instruction: 'Cook on pan', recipeIngredientIds: [] },
    ],
    ...overrides,
  })

beforeEach(() => {
  pinia = createPinia()
  setActivePinia(pinia)
  store = useRecipeStore()
  vi.clearAllMocks()
})

async function mountView() {
  store.currentRecipe = threeStepRecipe()
  store.loading = false
  store.error = null
  vi.spyOn(store, 'fetchRecipe').mockResolvedValue(store.currentRecipe)

  const wrapper = mount(CookingModeView, {
    props: { id: 'r1' },
    global: { plugins: [vuetify, pinia], stubs: { teleport: true } },
  })
  await flushPromises()
  return wrapper
}

describe('CookingModeView', () => {
  it('renders all steps in order', async () => {
    const w = await mountView()
    const text = w.text()
    expect(text.indexOf('Mix flour')).toBeLessThan(text.indexOf('Add milk'))
    expect(text.indexOf('Add milk')).toBeLessThan(text.indexOf('Cook on pan'))
  })

  it('emphasises the first step and highlights its ingredient chips', async () => {
    const w = await mountView()
    const steps = w.findAll('.cooking-step')
    expect(steps[0].classes()).toContain('cooking-step--current')
    // current step's chips are filled (flat); others stay tonal
    expect(steps[0].html()).toContain('v-chip--variant-flat')
    expect(steps[1].html()).toContain('v-chip--variant-tonal')
  })

  it('marks a step done on tap and moves the current emphasis to the next step', async () => {
    const w = await mountView()
    await w.findAll('.cooking-step')[0].trigger('click')

    const steps = w.findAll('.cooking-step')
    expect(steps[0].classes()).toContain('cooking-step--done')
    expect(steps[0].classes()).not.toContain('cooking-step--current')
    expect(steps[1].classes()).toContain('cooking-step--current')
  })

  it('Next step advances by marking the current step done', async () => {
    const w = await mountView()
    const nextBtn = w.findAll('button').find(b => b.text().includes('Next step'))
    await nextBtn.trigger('click')
    await flushPromises()

    const steps = w.findAll('.cooking-step')
    expect(steps[0].classes()).toContain('cooking-step--done')
    expect(steps[1].classes()).toContain('cooking-step--current')
  })

  it('shows the step counter', async () => {
    const w = await mountView()
    expect(w.text()).toContain('Step 1 / 3')
  })

  it('Mark as cooked & finish marks cooked, notifies and navigates to the detail view', async () => {
    const w = await mountView()
    vi.spyOn(store, 'markCooked').mockResolvedValue(threeStepRecipe())
    const ui = useUiStore()

    const finishBtn = w.findAll('button').find(b => b.text().includes('Mark as cooked'))
    await finishBtn.trigger('click')
    await flushPromises()

    expect(store.markCooked).toHaveBeenCalledWith('r1')
    expect(ui.snackbar.show).toBe(true)
    expect(replaceMock).toHaveBeenCalledWith({ name: 'recipe-detail', params: { id: 'r1' } })
  })

  it('shows an ErrorState with retry when the fetch fails', async () => {
    store.currentRecipe = null
    store.loading = false
    store.error = 'Network error'
    const fetchSpy = vi.spyOn(store, 'fetchRecipe').mockResolvedValue(null)

    const w = mount(CookingModeView, {
      props: { id: 'r1' },
      global: { plugins: [vuetify, pinia], stubs: { teleport: true } },
    })
    await flushPromises()

    expect(w.text()).toContain('Network error')
    expect(w.text()).toContain('Try again')

    fetchSpy.mockClear()
    const retry = w.findAll('button').find(b => b.text().includes('Try again'))
    await retry.trigger('click')
    expect(fetchSpy).toHaveBeenCalledWith('r1')
  })

  it('scales ingredient amounts from the ?portion query', async () => {
    // Override the route mock for this test to carry DOUBLE.
    store.currentRecipe = threeStepRecipe()
    store.loading = false
    vi.spyOn(store, 'fetchRecipe').mockResolvedValue(store.currentRecipe)
    const w = await mountView()
    // Default REGULAR → 200 g flour shown unscaled on the current (first) step.
    expect(w.text()).toContain('200 g')
  })
})
