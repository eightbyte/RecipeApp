import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import { useRecipeStore } from '@/stores/recipes'
import RecipeDetailView from './RecipeDetailView.vue'
import { makeRecipeDetail } from '@/test/factories'

vi.mock('@/services/api', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
  assetUrl: vi.fn((p) => p ?? ''),
}))

vi.mock('vue-router', async (importActual) => {
  const actual = await importActual()
  return {
    ...actual,
    useRoute: () => ({ params: { id: 'r1' } }),
    useRouter: () => ({ push: vi.fn() }),
  }
})

const vuetify = createVuetify({ components, directives })

let pinia
let store

beforeEach(() => {
  pinia = createPinia()
  setActivePinia(pinia)
  store = useRecipeStore()
  // Prevent onMounted's fetchRecipe from clearing our pre-set currentRecipe
  vi.spyOn(store, 'fetchRecipe').mockResolvedValue(null)
})

async function mountView(recipeOverrides = {}) {
  store.currentRecipe = makeRecipeDetail(recipeOverrides)
  store.loading = false

  const wrapper = mount(RecipeDetailView, {
    props: { id: 'r1' },
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
  return wrapper
}

describe('RecipeDetailView', () => {
  it('renders the recipe name', async () => {
    const wrapper = await mountView({ name: 'Chocolate Cake' })
    expect(wrapper.text()).toContain('Chocolate Cake')
  })

  it('defaults portion selector to Regular', async () => {
    const wrapper = await mountView()
    expect(wrapper.vm.portionSize).toBe('REGULAR')
  })

  it('doubles ingredient amounts when switching to Double', async () => {
    const wrapper = await mountView()
    wrapper.vm.portionSize = 'DOUBLE'
    await wrapper.vm.$nextTick()
    expect(wrapper.vm.portionMultiplier).toBe(2)
    expect(wrapper.vm.formatAmount(100, 'g')).toBe('200 g')
  })

  it('shows the source measurement alongside the converted amount', async () => {
    // "240 g (2 cups)" — the recipe reads as written while the shopping list sums grams.
    const wrapper = await mountView({
      ingredients: [{
        id: 'ri-1',
        ingredientId: 'test-ingredient-1',
        ingredientName: 'flour',
        ingredientDisplayName: 'Flour',
        category: 'DRY_GOODS',
        amount: 240,
        unit: 'g',
        sourceAmount: 2,
        sourceUnit: 'cups',
        notes: null,
        displayOrder: 0,
      }],
    })

    expect(wrapper.text()).toContain('240 g')
    expect(wrapper.text()).toContain('(2 cups)')
  })

  it('scales the source measurement with the portion multiplier', async () => {
    const wrapper = await mountView({
      ingredients: [{
        id: 'ri-1',
        ingredientId: 'test-ingredient-1',
        ingredientName: 'flour',
        ingredientDisplayName: 'Flour',
        category: 'DRY_GOODS',
        amount: 240,
        unit: 'g',
        sourceAmount: 2,
        sourceUnit: 'cups',
        notes: null,
        displayOrder: 0,
      }],
    })

    wrapper.vm.portionSize = 'DOUBLE'
    await wrapper.vm.$nextTick()

    expect(wrapper.text()).toContain('480 g')
    expect(wrapper.text()).toContain('(4 cups)')
  })

  it('renders an unquantified ingredient as its name alone', async () => {
    // "salt" states no quantity, so it is stored with a null amount and a null unit and must
    // render as "Salt", never "0 g Salt" (Phase 9.1 §3.4).
    const wrapper = await mountView({
      ingredients: [{
        id: 'ri-1',
        ingredientId: 'test-ingredient-1',
        ingredientName: 'salt',
        ingredientDisplayName: 'Salt',
        category: 'DRY_GOODS',
        amount: null,
        unit: null,
        sourceAmount: null,
        sourceUnit: null,
        notes: null,
        displayOrder: 0,
      }],
    })

    // The row is exactly the name — no amount, no unit, and no bare "0". Scaling a null amount
    // by the portion multiplier yields 0 in JavaScript, so asserting only on "0 g" would pass
    // against exactly that bug.
    expect(wrapper.get('#ingredient-ri-1').text().trim()).toBe('Salt')
    expect(wrapper.vm.formatAmount(null, null)).toBeNull()
  })

  it('leaves an unquantified ingredient unscaled at Double portions', async () => {
    // There is nothing to multiply, and 2 x nothing must not become a number.
    const wrapper = await mountView({
      ingredients: [{
        id: 'ri-1',
        ingredientId: 'test-ingredient-1',
        ingredientName: 'salt',
        ingredientDisplayName: 'Salt',
        category: 'DRY_GOODS',
        amount: null,
        unit: null,
        sourceAmount: null,
        sourceUnit: null,
        notes: null,
        displayOrder: 0,
      }],
    })

    wrapper.vm.portionSize = 'DOUBLE'
    await wrapper.vm.$nextTick()

    expect(wrapper.vm.formatAmount(null, null)).toBeNull()
    expect(wrapper.get('#ingredient-ri-1').text().trim()).toBe('Salt')
  })

  it('shows no source measurement for a hand-entered row', async () => {
    const wrapper = await mountView()
    expect(wrapper.text()).toContain('200 g')
    expect(wrapper.text()).not.toContain('(')
  })

  it('applies grayscale class when isRecentlyCooked returns true', async () => {
    vi.spyOn(store, 'isRecentlyCooked').mockReturnValue(true)
    const wrapper = await mountView({ lastCookedAt: new Date(Date.now() - 2 * 86400_000).toISOString() })

    const img = wrapper.findComponent({ name: 'VImg' })
    expect(img.classes()).toContain('grayscale')
  })

  it('does not apply grayscale class when not recently cooked', async () => {
    vi.spyOn(store, 'isRecentlyCooked').mockReturnValue(false)
    const wrapper = await mountView({ lastCookedAt: new Date(Date.now() - 30 * 86400_000).toISOString() })

    const img = wrapper.findComponent({ name: 'VImg' })
    expect(img.classes()).not.toContain('grayscale')
  })

  it('calls store.markCooked when markCooked() is invoked', async () => {
    vi.spyOn(store, 'markCooked').mockResolvedValue(makeRecipeDetail())
    const wrapper = await mountView()

    await wrapper.vm.markCooked()

    expect(store.markCooked).toHaveBeenCalledWith(store.currentRecipe.id)
  })

  it('renders steps in the order provided by the store (sorting is the service layer\'s job)', async () => {
    // The service sorts steps before storing them; the component renders in store order.
    const wrapper = await mountView({
      steps: [
        { id: 's1', stepNumber: 1, instruction: 'First step', recipeIngredientIds: [] },
        { id: 's2', stepNumber: 2, instruction: 'Second step', recipeIngredientIds: [] },
        { id: 's3', stepNumber: 3, instruction: 'Third step', recipeIngredientIds: [] },
      ],
    })

    const text = wrapper.text()
    const firstIdx = text.indexOf('First step')
    const secondIdx = text.indexOf('Second step')
    const thirdIdx = text.indexOf('Third step')

    expect(firstIdx).toBeLessThan(secondIdx)
    expect(secondIdx).toBeLessThan(thirdIdx)
  })
})

describe('RecipeDetailView — Phase 7', () => {
  function mountRaw() {
    return mount(RecipeDetailView, {
      props: { id: 'r1' },
      global: {
        plugins: [vuetify, pinia],
        stubs: {
          RouterLink: { template: '<a><slot/></a>' },
          'router-link': { template: '<a><slot/></a>' },
          teleport: true,
        },
      },
    })
  }

  it('renders a Start cooking CTA linking to the cooking route', async () => {
    const wrapper = await mountView()
    const startBtn = wrapper.findAllComponents({ name: 'VBtn' })
      .find(b => b.text().includes('Start cooking'))
    expect(startBtn).toBeTruthy()
    expect(startBtn.props('to')).toMatchObject({
      name: 'recipe-cooking',
      params: { id: 'test-recipe-1' },
    })
  })

  it('shows a "Recipe not found" empty state when missing and there is no error', async () => {
    store.currentRecipe = null
    store.loading = false
    store.error = null
    const wrapper = mountRaw()
    await flushPromises()
    expect(wrapper.text()).toContain('Recipe not found')
  })

  it('shows an ErrorState (not "not found") on fetch error, and retry refetches', async () => {
    store.currentRecipe = null
    store.loading = false
    store.error = 'Server unavailable'
    const wrapper = mountRaw()
    await flushPromises()

    expect(wrapper.text()).toContain('Server unavailable')
    expect(wrapper.text()).toContain('Try again')
    expect(wrapper.text()).not.toContain('Recipe not found')

    store.fetchRecipe.mockClear()
    const retry = wrapper.findAll('button').find(b => b.text().includes('Try again'))
    await retry.trigger('click')
    expect(store.fetchRecipe).toHaveBeenCalledWith('r1')
  })

  it('notifies on successful mark-as-cooked', async () => {
    vi.spyOn(store, 'markCooked').mockResolvedValue(makeRecipeDetail())
    const wrapper = await mountView()
    const { useUiStore } = await import('@/stores/ui')
    const ui = useUiStore()

    await wrapper.vm.markCooked()
    expect(ui.snackbar.show).toBe(true)
    expect(ui.snackbar.color).toBe('success')
  })
})
