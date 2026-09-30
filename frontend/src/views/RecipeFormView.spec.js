import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import { useRecipeStore } from '@/stores/recipes'
import { useIngredientStore } from '@/stores/ingredients'
import RecipeFormView from './RecipeFormView.vue'
import { makeRecipeDetail, makeIngredient } from '@/test/factories'

vi.mock('@/services/api', () => ({
  default: { get: vi.fn().mockResolvedValue({ data: [] }), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
  assetUrl: vi.fn((p) => p ?? ''),
}))

const mockPush = vi.fn()

vi.mock('vue-router', async (importActual) => {
  const actual = await importActual()
  return {
    ...actual,
    useRoute: () => ({ params: {} }),
    useRouter: () => ({ push: mockPush }),
  }
})

const vuetify = createVuetify({ components, directives })

let pinia

beforeEach(() => {
  pinia = createPinia()
  setActivePinia(pinia)
  mockPush.mockReset()
})

function mountForm(props = {}) {
  return mount(RecipeFormView, {
    props,
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

describe('RecipeFormView — create mode', () => {
  it('form fields start empty in create mode', () => {
    const wrapper = mountForm()
    expect(wrapper.vm.form.name).toBe('')
    expect(wrapper.vm.form.description).toBe('')
    expect(wrapper.vm.isEdit).toBe(false)
  })

  it('clicking Add Ingredient adds a row to form.ingredients', async () => {
    const wrapper = mountForm()
    const before = wrapper.vm.form.ingredients.length

    wrapper.vm.addIngredient()
    await wrapper.vm.$nextTick()

    expect(wrapper.vm.form.ingredients).toHaveLength(before + 1)
  })

  it('removeIngredient removes the ingredient row at the given index', () => {
    const wrapper = mountForm()
    wrapper.vm.form.ingredients.push({ ingredientId: null, amount: 1, unit: 'g', notes: '' })
    expect(wrapper.vm.form.ingredients).toHaveLength(1)

    wrapper.vm.removeIngredient(0)
    expect(wrapper.vm.form.ingredients).toHaveLength(0)
  })

  it('addStep adds a step row', () => {
    const wrapper = mountForm()
    const before = wrapper.vm.form.steps.length

    wrapper.vm.addStep()
    expect(wrapper.vm.form.steps).toHaveLength(before + 1)
  })

  it('submit without a name does not call createRecipe', async () => {
    const wrapper = mountForm()
    const store = useRecipeStore()
    vi.spyOn(store, 'createRecipe')

    // formRef.validate() will return { valid: false } since name is empty
    await wrapper.vm.submit()

    expect(store.createRecipe).not.toHaveBeenCalled()
  })

  it('shows submitError when submitting with no ingredients', async () => {
    const wrapper = mountForm()
    wrapper.vm.form.name = 'My Recipe'
    wrapper.vm.form.servings = 4

    // Patch formRef.validate to simulate a valid form so we reach the ingredient check
    wrapper.vm.formRef = { validate: vi.fn().mockResolvedValue({ valid: true }) }

    await wrapper.vm.submit()

    expect(wrapper.vm.submitError).toBeTruthy()
  })
})

describe('RecipeFormView — edit mode', () => {
  it('pre-fills fields when populateForm is called with a recipe', async () => {
    const detail = makeRecipeDetail({ name: 'Existing Recipe', servings: 6 })

    const wrapper = mountForm({ id: 'r1' })
    await wrapper.vm.$nextTick()

    wrapper.vm.populateForm(detail)
    await wrapper.vm.$nextTick()

    expect(wrapper.vm.form.name).toBe('Existing Recipe')
    expect(wrapper.vm.form.servings).toBe(6)
    expect(wrapper.vm.form.ingredients).toHaveLength(detail.ingredients.length)
  })

  it('calls updateRecipe in edit mode on submit', async () => {
    const detail = makeRecipeDetail({ id: 'r1', name: 'Old Name' })

    const wrapper = mountForm({ id: 'r1' })
    const recipeStore = useRecipeStore()
    const ingStore = useIngredientStore()

    ingStore.ingredients = [makeIngredient()]
    wrapper.vm.populateForm(detail)
    await wrapper.vm.$nextTick()

    vi.spyOn(recipeStore, 'updateRecipe').mockResolvedValue(makeRecipeDetail({ id: 'r1' }))
    wrapper.vm.formRef = { validate: vi.fn().mockResolvedValue({ valid: true }) }

    await wrapper.vm.submit()

    expect(recipeStore.updateRecipe).toHaveBeenCalledWith('r1', expect.any(Object))
  })

  it('sends an empty amount as null, never as 0', async () => {
    // A hand-entered "salt" row with no quantity. Coercing to 0 would render as "0 g Salt"
    // and sum into the shopping list (Phase 9.1 §3.4).
    const detail = makeRecipeDetail({ id: 'r1' })

    const wrapper = mountForm({ id: 'r1' })
    const recipeStore = useRecipeStore()
    const ingStore = useIngredientStore()

    ingStore.ingredients = [makeIngredient()]
    wrapper.vm.populateForm(detail)
    await wrapper.vm.$nextTick()

    wrapper.vm.form.ingredients[0].amount = ''
    vi.spyOn(recipeStore, 'updateRecipe').mockResolvedValue(makeRecipeDetail({ id: 'r1' }))
    wrapper.vm.formRef = { validate: vi.fn().mockResolvedValue({ valid: true }) }

    await wrapper.vm.submit()

    const [, payload] = recipeStore.updateRecipe.mock.calls[0]
    expect(payload.ingredients[0].amount).toBeNull()
    expect(payload.ingredients[0].unit).toBeNull()
  })

  it('round-trips a null amount loaded from the API back as null', async () => {
    const detail = makeRecipeDetail({ id: 'r1' })
    detail.ingredients[0].amount = null
    detail.ingredients[0].unit = null

    const wrapper = mountForm({ id: 'r1' })
    const recipeStore = useRecipeStore()
    const ingStore = useIngredientStore()

    ingStore.ingredients = [makeIngredient()]
    wrapper.vm.populateForm(detail)
    await wrapper.vm.$nextTick()

    vi.spyOn(recipeStore, 'updateRecipe').mockResolvedValue(makeRecipeDetail({ id: 'r1' }))
    wrapper.vm.formRef = { validate: vi.fn().mockResolvedValue({ valid: true }) }

    await wrapper.vm.submit()

    const [, payload] = recipeStore.updateRecipe.mock.calls[0]
    expect(payload.ingredients[0].amount).toBeNull()
    expect(payload.ingredients[0].unit).toBeNull()
  })
})
