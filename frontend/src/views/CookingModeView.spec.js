import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import { useRecipeStore } from '@/stores/recipes'
import { useUiStore } from '@/stores/ui'
import CookingModeView from './CookingModeView.vue'
import { makeRecipeDetail } from '@/test/factories'
import { cookingRoute, servingsFromQuery } from '@/router/recipeRoutes'

const backMock = vi.fn()
const replaceMock = vi.fn()
let routeQuery = {}

vi.mock('@/services/api', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
  assetUrl: vi.fn((p) => p ?? ''),
}))

vi.mock('vue-router', async (importActual) => {
  const actual = await importActual()
  return {
    ...actual,
    useRoute: () => ({ params: { id: 'r1' }, query: routeQuery }),
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
  routeQuery = {}
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

function servingsChip(wrapper) {
  return wrapper.get('header .servings-chip')
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
    expect(replaceMock).toHaveBeenCalledWith(expect.objectContaining({ name: 'recipe-detail', params: { id: 'r1' } }))
  })

  it('returns to the recipe view at the servings Cooking Mode was opened at', async () => {
    routeQuery = cookingRoute('r1', 8).query
    const w = await mountView()
    vi.spyOn(store, 'markCooked').mockResolvedValue(threeStepRecipe())

    await w.findAll('button').find(b => b.text().includes('Mark as cooked')).trigger('click')
    await flushPromises()

    expect(servingsFromQuery(replaceMock.mock.lastCall[0].query, 4)).toBe(8)
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

  it('shows the recipe’s own Servings and unscaled amounts without a scale in the query', async () => {
    const w = await mountView()
    expect(servingsChip(w).text()).toBe('4 servings')
    expect(w.text()).toContain('200 g')
  })

  it('scales to the servings in a link from the recipe view', async () => {
    routeQuery = cookingRoute('r1', 6).query
    const w = await mountView()
    expect(servingsChip(w).text()).toBe('6 servings')
    expect(w.text()).toContain('300 g')
  })

  it('shows fractional servings the way amounts show', async () => {
    routeQuery = cookingRoute('r1', 1.5).query
    const w = await mountView()
    expect(servingsChip(w).text()).toBe('1.5 servings')
    expect(w.text()).toContain('75 g')
  })

  it('reads "1 serving" at exactly one serving', async () => {
    routeQuery = cookingRoute('r1', 1).query
    const w = await mountView()
    expect(servingsChip(w).text()).toBe('1 serving')
  })

  it('ignores a Batch Size left in an old link', async () => {
    routeQuery = { batch: 'DOUBLE' }
    const w = await mountView()
    expect(servingsChip(w).text()).toBe('4 servings')
    expect(w.text()).toContain('200 g')
  })

  it('offers no way to change the scale', async () => {
    const w = await mountView()
    expect(w.find('header [aria-label="Batch"]').exists()).toBe(false)
    expect(w.findAll('header button').map(button => button.attributes('aria-label'))).toEqual(['Exit cooking mode'])
  })

  it('shows an unquantified ingredient chip as its name alone', async () => {
    // `null * multiplier` is 0 in JavaScript, so scaling has to short-circuit or "Salt" renders as "0 Salt".
    store.currentRecipe = threeStepRecipe({
      ingredients: [
        { id: 'ri-1', ingredientId: 'i1', ingredientName: 'salt', ingredientDisplayName: 'Salt', category: 'DRY_GOODS', amount: null, unit: null, notes: null, displayOrder: 0 },
      ],
      steps: [
        { id: 's1', stepNumber: 1, instruction: 'Season', recipeIngredientIds: ['ri-1'] },
      ],
    })
    store.loading = false
    store.error = null
    vi.spyOn(store, 'fetchRecipe').mockResolvedValue(store.currentRecipe)

    const w = mount(CookingModeView, {
      props: { id: 'r1' },
      global: { plugins: [vuetify, pinia], stubs: { teleport: true } },
    })
    await flushPromises()

    expect(w.get('.cooking-step .v-chip').text().trim()).toBe('Salt')
  })

  async function mountWithChip(ingredient) {
    store.currentRecipe = threeStepRecipe({
      ingredients: [{ id: 'ri-1', ingredientId: 'i1', ingredientName: 'x', category: 'DAIRY', notes: null, displayOrder: 0, ...ingredient }],
      steps: [{ id: 's1', stepNumber: 1, instruction: 'Cook', recipeIngredientIds: ['ri-1'] }],
    })
    store.loading = false
    store.error = null
    vi.spyOn(store, 'fetchRecipe').mockResolvedValue(store.currentRecipe)

    const w = mount(CookingModeView, {
      props: { id: 'r1' },
      global: { plugins: [vuetify, pinia], stubs: { teleport: true } },
    })
    await flushPromises()
    return w.get('.cooking-step .v-chip').text().replace(/\s+/g, ' ').trim()
  }

  it('shows a count chip without its unit', async () => {
    expect(await mountWithChip({ ingredientDisplayName: 'Eggs', amount: 2, unit: 'pcs', sourceAmount: null, sourceUnit: null }))
      .toBe('2 Eggs')
  })

  it('shows a counted chip by its Count Word alone', async () => {
    expect(await mountWithChip({ ingredientDisplayName: 'Garlic', amount: 2, unit: 'pcs', sourceAmount: 2, sourceUnit: 'cloves' }))
      .toBe('(2 cloves) Garlic')
  })
})
