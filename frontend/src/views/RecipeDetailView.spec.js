import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import { useRecipeStore } from '@/stores/recipes'
import RecipeDetailView from './RecipeDetailView.vue'
import { makeRecipeDetail } from '@/test/factories'
import { servingsFromCookingQuery } from '@/router/cookingRoute'

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

function servingsChip(wrapper) {
  return wrapper.get('.servings-chip')
}

function stepperValue(wrapper) {
  return wrapper.get('.servings-stepper-value').text()
}

function highlightedShortcuts(wrapper) {
  return wrapper.get('[aria-label="Batch"]').findAll('button.v-btn--active').map(button => button.text())
}

function findButton(wrapper, label) {
  return wrapper.findAll('button').find(button =>
    button.attributes('aria-label') === label || button.text() === label)
}

async function clickButton(wrapper, label) {
  await findButton(wrapper, label).trigger('click')
  await flushPromises()
}

describe('RecipeDetailView', () => {
  it('renders the recipe name', async () => {
    const wrapper = await mountView({ name: 'Chocolate Cake' })
    expect(wrapper.text()).toContain('Chocolate Cake')
  })

  it('starts at the recipe’s Servings with its amounts unscaled', async () => {
    const wrapper = await mountView()
    expect(servingsChip(wrapper).text()).toBe('4 servings')
    expect(stepperValue(wrapper)).toBe('4')
    expect(wrapper.get('#ingredient-ri-1').text()).toContain('200 g')
  })

  it('scales the amounts and the servings chip by the chosen servings when stepped up', async () => {
    const wrapper = await mountView()
    await clickButton(wrapper, 'More servings')
    expect(stepperValue(wrapper)).toBe('5')
    expect(servingsChip(wrapper).text()).toBe('5 servings')
    expect(wrapper.get('#ingredient-ri-1').text()).toContain('250 g')
  })

  it('scales the amounts down when stepped down', async () => {
    const wrapper = await mountView()
    await clickButton(wrapper, 'Fewer servings')
    expect(servingsChip(wrapper).text()).toBe('3 servings')
    expect(wrapper.get('#ingredient-ri-1').text()).toContain('150 g')
  })

  it('keeps 2-decimal rounding on amounts scaled by an uneven number of servings', async () => {
    const wrapper = await mountView({ servings: 3 })
    await clickButton(wrapper, 'More servings')
    // 200 g x 4/3 is 266.666…
    expect(wrapper.get('#ingredient-ri-1').text()).toContain('266.67 g')
  })

  it('reads "1 serving" at exactly one serving', async () => {
    const wrapper = await mountView({ servings: 2 })
    await clickButton(wrapper, 'Fewer servings')
    expect(servingsChip(wrapper).text()).toBe('1 serving')
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

  it('scales the source measurement with the chosen servings', async () => {
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

    await clickButton(wrapper, '2×')

    expect(wrapper.text()).toContain('480 g')
    expect(wrapper.text()).toContain('(4 cups)')
  })

  it('sets the chosen servings exactly from a shortcut', async () => {
    const wrapper = await mountView({ servings: 3 })
    await clickButton(wrapper, '½×')
    expect(stepperValue(wrapper)).toBe('1.5')
    expect(servingsChip(wrapper).text()).toBe('1.5 servings')
    expect(wrapper.get('#ingredient-ri-1').text()).toContain('100 g')
  })

  it('highlights 1× at the recipe’s own Servings', async () => {
    const wrapper = await mountView()
    expect(highlightedShortcuts(wrapper)).toEqual(['1×'])
  })

  it('highlights a shortcut when stepping lands on its preset', async () => {
    const wrapper = await mountView({ servings: 2 })
    await clickButton(wrapper, 'Fewer servings')
    expect(highlightedShortcuts(wrapper)).toEqual(['½×'])
  })

  it('highlights no shortcut when the chosen servings match none of them', async () => {
    const wrapper = await mountView()
    await clickButton(wrapper, 'More servings')
    expect(highlightedShortcuts(wrapper)).toEqual([])
  })

  it('keeps the chosen servings when the highlighted shortcut is tapped again', async () => {
    const wrapper = await mountView()
    await clickButton(wrapper, '2×')
    await clickButton(wrapper, '2×')
    expect(servingsChip(wrapper).text()).toBe('8 servings')
    expect(highlightedShortcuts(wrapper)).toEqual(['2×'])
  })

  it('steps a fractional number of servings to the next whole number', async () => {
    const wrapper = await mountView({ servings: 3 })
    await clickButton(wrapper, '½×')
    await clickButton(wrapper, 'More servings')
    expect(stepperValue(wrapper)).toBe('2')

    await clickButton(wrapper, '½×')
    await clickButton(wrapper, 'Fewer servings')
    expect(stepperValue(wrapper)).toBe('1')
  })

  it('cannot step below 1 serving', async () => {
    const wrapper = await mountView({ servings: 2 })
    expect(findButton(wrapper, 'Fewer servings').attributes('disabled')).toBeUndefined()
    await clickButton(wrapper, 'Fewer servings')
    expect(findButton(wrapper, 'Fewer servings').attributes('disabled')).toBeDefined()
  })

  it('cannot step above 100 servings', async () => {
    const wrapper = await mountView({ servings: 99 })
    expect(findButton(wrapper, 'More servings').attributes('disabled')).toBeUndefined()
    await clickButton(wrapper, 'More servings')
    expect(stepperValue(wrapper)).toBe('100')
    expect(findButton(wrapper, 'More servings').attributes('disabled')).toBeDefined()
  })

  it('lets a shortcut go below 1 serving, then steps back up to a whole number', async () => {
    const wrapper = await mountView({ servings: 1 })
    await clickButton(wrapper, '½×')
    expect(servingsChip(wrapper).text()).toBe('0.5 servings')
    expect(findButton(wrapper, 'Fewer servings').attributes('disabled')).toBeDefined()

    await clickButton(wrapper, 'More servings')
    expect(stepperValue(wrapper)).toBe('1')
  })

  it('lets a shortcut go above 100 servings, then steps back down to a whole number', async () => {
    const wrapper = await mountView({ servings: 80 })
    await clickButton(wrapper, '2×')
    expect(servingsChip(wrapper).text()).toBe('160 servings')
    expect(findButton(wrapper, 'More servings').attributes('disabled')).toBeDefined()

    await clickButton(wrapper, 'Fewer servings')
    expect(stepperValue(wrapper)).toBe('159')
  })

  it('renders an unquantified ingredient as its name alone', async () => {
    // "salt" states no quantity, so it is stored with a null amount and unit and must render as "Salt", never "0 g Salt".
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

    // Assert the row is exactly the name, because scaling a null amount by the multiplier yields 0 in JavaScript and "0 g" alone would pass against that bug.
    expect(wrapper.get('#ingredient-ri-1').text().trim()).toBe('Salt')
    expect(wrapper.vm.formatAmount(null, null)).toBeNull()
  })

  it('leaves an unquantified ingredient blank at a chosen number of servings', async () => {
    // There is nothing to multiply, and 5/4 x nothing must not become a number.
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

    await clickButton(wrapper, 'More servings')

    expect(servingsChip(wrapper).text()).toBe('5 servings')
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

describe('RecipeDetailView — cooking, loading and error states', () => {
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

  it('starts cooking at the chosen servings', async () => {
    const wrapper = await mountView()
    await clickButton(wrapper, 'More servings')
    const startBtn = wrapper.findAllComponents({ name: 'VBtn' })
      .find(b => b.text().includes('Start cooking'))
    expect(servingsFromCookingQuery(startBtn.props('to').query, 4)).toBe(5)
  })

  it('starts cooking at the recipe’s own Servings when none were chosen', async () => {
    const wrapper = await mountView()
    const startBtn = wrapper.findAllComponents({ name: 'VBtn' })
      .find(b => b.text().includes('Start cooking'))
    // A Batch Size link would also read back as 4, so check the link names servings itself.
    expect(startBtn.props('to').query).toEqual({ servings: 4 })
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
