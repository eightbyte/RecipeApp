import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import { useRecipeStore } from '@/stores/recipes'
import { useIngredientStore } from '@/stores/ingredients'
import { useUiStore } from '@/stores/ui'
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

const banner = wrapper => wrapper.find('[data-test="recipe-problems"]')

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

    await wrapper.vm.submit()

    expect(store.createRecipe).not.toHaveBeenCalled()
  })

  it('lists a missing ingredient list when submitting with no ingredients', async () => {
    const wrapper = mountForm()
    const store = useRecipeStore()
    vi.spyOn(store, 'createRecipe')
    wrapper.vm.form.name = 'My Recipe'

    await wrapper.vm.submit()
    await flushPromises()

    expect(store.createRecipe).not.toHaveBeenCalled()
    expect(banner(wrapper).text()).toContain('Ingredients — Add at least one ingredient')
  })
})

describe('RecipeFormView — problems shown before Save', () => {
  const ingredientPickers = wrapper => wrapper.findAllComponents({ name: 'VAutocomplete' })
    .filter(a => a.props('label') === 'Ingredient')
  const fieldLabelled = (wrapper, label) => wrapper.findAllComponents({ name: 'VTextField' })
    .find(f => f.props('label') === label)

  it('shows nothing on a fresh New Recipe form', async () => {
    const wrapper = mountForm()
    await flushPromises()

    expect(fieldLabelled(wrapper, 'Recipe name').props('errorMessages')).toEqual([])
    expect(banner(wrapper).exists()).toBe(false)
  })

  it('does not flag a newly added blank row until one of its fields is left', async () => {
    const wrapper = mountForm()
    wrapper.vm.addIngredient()
    await flushPromises()
    expect(ingredientPickers(wrapper)[0].props('errorMessages')).toEqual([])

    fieldLabelled(wrapper, 'Amount').vm.$emit('update:focused', false)
    await flushPromises()

    expect(ingredientPickers(wrapper)[0].props('errorMessages')).toEqual(['Pick an ingredient'])
  })

  it('flags a newly added blank row once Save is pressed', async () => {
    const wrapper = mountForm()
    wrapper.vm.form.name = 'My Recipe'
    wrapper.vm.addIngredient()

    await wrapper.vm.submit()
    await flushPromises()

    expect(ingredientPickers(wrapper)[0].props('errorMessages')).toEqual(['Pick an ingredient'])
    expect(banner(wrapper).text()).toContain('1 problem needs fixing before saving')
  })

  it('lists every problem on one Save, and clears each as soon as it is fixed', async () => {
    const wrapper = mountForm()
    const store = useRecipeStore()
    vi.spyOn(store, 'createRecipe')
    wrapper.vm.addIngredient()
    wrapper.vm.addStep()

    await wrapper.vm.submit()
    await flushPromises()

    expect(store.createRecipe).not.toHaveBeenCalled()
    const text = banner(wrapper).text()
    expect(text).toContain('3 problems need fixing before saving')
    expect(text).toContain('Recipe name — Required')
    expect(text).toContain('Ingredient 1 — Pick an ingredient')
    expect(text).toContain('Step 1 — Required')

    wrapper.vm.form.name = 'My Recipe'
    await flushPromises()
    expect(fieldLabelled(wrapper, 'Recipe name').props('errorMessages')).toEqual([])
    expect(banner(wrapper).text()).toContain('2 problems need fixing before saving')
  })

  async function mountEditing(detail = makeRecipeDetail({ id: 'r1' })) {
    vi.spyOn(useRecipeStore(), 'fetchRecipe').mockResolvedValue(detail)
    const wrapper = mountForm({ id: 'r1' })
    await flushPromises()
    return wrapper
  }

  it('scrolls the banner into view when the only problem has no field of its own', async () => {
    // jsdom does not implement scrolling.
    const scrollIntoView = vi.fn()
    Element.prototype.scrollIntoView = scrollIntoView
    const wrapper = mountForm()
    wrapper.vm.form.name = 'My Recipe'

    await wrapper.vm.submit()
    await flushPromises()

    expect(scrollIntoView.mock.contexts[0]).toBe(banner(wrapper).element)
    delete Element.prototype.scrollIntoView
  })

  it('flags clearing the name only once the field is left', async () => {
    const wrapper = await mountEditing()
    wrapper.vm.form.name = ''
    await flushPromises()
    expect(fieldLabelled(wrapper, 'Recipe name').props('errorMessages')).toEqual([])

    fieldLabelled(wrapper, 'Recipe name').vm.$emit('update:focused', false)
    await flushPromises()
    expect(fieldLabelled(wrapper, 'Recipe name').props('errorMessages')).toEqual(['Required'])
  })

  it('flags an over-long description only after a Save attempt, even once it is left', async () => {
    const wrapper = await mountEditing()
    const description = () => wrapper.findAllComponents({ name: 'VTextarea' })
      .find(t => t.props('label') === 'Description (optional)')

    wrapper.vm.form.description = 'x'.repeat(2001)
    description().vm.$emit('update:focused', false)
    await flushPromises()
    expect(description().props('errorMessages')).toEqual([])

    await wrapper.vm.submit()
    await flushPromises()
    expect(description().props('errorMessages')).toEqual(['Must be 2000 characters or fewer'])
  })

  it('flags notes loaded over the limit as soon as the recipe opens', async () => {
    const detail = makeRecipeDetail({ id: 'r1' })
    detail.ingredients[0].notes = 'x'.repeat(501)
    const wrapper = await mountEditing(detail)

    expect(fieldLabelled(wrapper, 'Notes').props('errorMessages')).toEqual(['Must be 500 characters or fewer'])
  })
})

describe('RecipeFormView — removing with Undo', () => {
  const twoIngredientRecipe = () => makeRecipeDetail({
    ingredients: [
      { ...makeRecipeDetail().ingredients[0], id: 'ri-1', ingredientId: 'flour' },
      { ...makeRecipeDetail().ingredients[0], id: 'ri-2', ingredientId: 'sugar' },
    ],
    steps: [
      { id: 's1', stepNumber: 1, instruction: 'Sift', recipeIngredientIds: ['ri-1'] },
      { id: 's2', stepNumber: 2, instruction: 'Mix',  recipeIngredientIds: ['ri-1', 'ri-2'] },
    ],
  })

  async function mountLoaded() {
    const wrapper = mountForm({ id: 'r1' })
    await flushPromises()
    wrapper.vm.populateForm(twoIngredientRecipe())
    await flushPromises()
    return wrapper
  }

  const removeButtons = wrapper => wrapper.findAllComponents({ name: 'VBtn' })
    .filter(b => b.props('icon') === 'mdi-close')

  it('offers Undo after removing an ingredient, which brings it back with its step selections', async () => {
    const wrapper = await mountLoaded()
    const ui = useUiStore()

    await removeButtons(wrapper)[0].trigger('click')
    expect(wrapper.vm.form.ingredients.map(i => i.ingredientId)).toEqual(['sugar'])
    expect(wrapper.vm.form.steps.map(s => s.ingredientIndexes)).toEqual([[], [0]])
    expect(ui.snackbar).toMatchObject({ show: true, message: 'Ingredient removed', action: { label: 'Undo' } })

    ui.runAction()
    await flushPromises()

    expect(wrapper.vm.form.ingredients.map(i => i.ingredientId)).toEqual(['flour', 'sugar'])
    expect(wrapper.vm.form.steps.map(s => [...s.ingredientIndexes].sort())).toEqual([[0], [0, 1]])
  })

  it('offers Undo after removing a step, which brings it back in place', async () => {
    const wrapper = await mountLoaded()
    const ui = useUiStore()
    // The two ingredient rows' remove buttons come first.
    await removeButtons(wrapper)[2].trigger('click')
    expect(wrapper.vm.form.steps.map(s => s.instruction)).toEqual(['Mix'])
    expect(ui.snackbar.message).toBe('Step removed')

    ui.runAction()

    expect(wrapper.vm.form.steps.map(s => s.instruction)).toEqual(['Sift', 'Mix'])
  })

  it('undoes only the most recent removal', async () => {
    const wrapper = await mountLoaded()
    const ui = useUiStore()

    wrapper.vm.removeStep(0)
    wrapper.vm.removeIngredient(0)
    ui.runAction()

    expect(wrapper.vm.form.ingredients).toHaveLength(2)
    expect(wrapper.vm.form.steps.map(s => s.instruction)).toEqual(['Mix'])
  })

  it('makes the removal final once the snackbar has gone', async () => {
    const wrapper = await mountLoaded()
    const ui = useUiStore()

    wrapper.vm.removeIngredient(0)
    ui.dismiss()
    ui.runAction()

    expect(wrapper.vm.form.ingredients).toHaveLength(1)
  })

  it('takes its Undo away on leaving the page', async () => {
    const wrapper = await mountLoaded()
    const ui = useUiStore()
    wrapper.vm.removeIngredient(0)

    wrapper.unmount()

    expect(ui.snackbar.show).toBe(false)
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

    await wrapper.vm.submit()

    expect(recipeStore.updateRecipe).toHaveBeenCalledWith('r1', expect.any(Object))
  })

  it('sends an empty amount as null, never as 0', async () => {
    // A hand-entered "salt" row with no quantity must not be coerced to 0, which would render as "0 g Salt".
    const detail = makeRecipeDetail({ id: 'r1' })

    const wrapper = mountForm({ id: 'r1' })
    const recipeStore = useRecipeStore()
    const ingStore = useIngredientStore()

    ingStore.ingredients = [makeIngredient()]
    wrapper.vm.populateForm(detail)
    await wrapper.vm.$nextTick()

    wrapper.vm.form.ingredients[0].amount = ''
    vi.spyOn(recipeStore, 'updateRecipe').mockResolvedValue(makeRecipeDetail({ id: 'r1' }))

    await wrapper.vm.submit()

    const [, payload] = recipeStore.updateRecipe.mock.calls[0]
    expect(payload.ingredients[0].amount).toBeNull()
    expect(payload.ingredients[0].unit).toBeNull()
  })

  it('saves an amount whose Unit was cleared, which the API stores as pcs', async () => {
    const wrapper = mountForm({ id: 'r1' })
    const recipeStore = useRecipeStore()
    useIngredientStore().ingredients = [makeIngredient()]
    wrapper.vm.populateForm(makeRecipeDetail({ id: 'r1' }))
    await wrapper.vm.$nextTick()

    const unitSelect = wrapper.findAllComponents({ name: 'VSelect' }).filter(s => s.props('label') === 'Unit')[0]
    unitSelect.vm.$emit('update:modelValue', null)
    await wrapper.vm.$nextTick()
    expect(unitSelect.props('placeholder')).toBe('pcs')

    vi.spyOn(recipeStore, 'updateRecipe').mockResolvedValue(makeRecipeDetail({ id: 'r1' }))
    await wrapper.vm.submit()

    const [, payload] = recipeStore.updateRecipe.mock.calls[0]
    expect(payload.ingredients[0]).toMatchObject({ amount: 200, unit: null })
  })

  it('starts a new row in grams', async () => {
    const wrapper = mountForm()
    wrapper.vm.addIngredient()
    expect(wrapper.vm.form.ingredients.at(-1).unit).toBe('g')
  })

  it('shows the Unit field blank and disabled while the amount is empty, and restores it after', async () => {
    const detail = makeRecipeDetail({ id: 'r1' })
    detail.ingredients[0].amount = 100
    detail.ingredients[0].unit   = 'g'

    const wrapper = mountForm({ id: 'r1' })
    useIngredientStore().ingredients = [makeIngredient()]
    wrapper.vm.populateForm(detail)
    await wrapper.vm.$nextTick()

    const unitSelect = () => wrapper.findAllComponents({ name: 'VSelect' })
      .filter(s => s.props('label') === 'Unit')[0]

    wrapper.vm.form.ingredients[0].amount = ''
    await wrapper.vm.$nextTick()
    expect(unitSelect().props('modelValue')).toBeNull()
    expect(unitSelect().props('disabled')).toBe(true)

    wrapper.vm.form.ingredients[0].amount = 50
    await wrapper.vm.$nextTick()
    expect(unitSelect().props('modelValue')).toBe('g')
    expect(unitSelect().props('disabled')).toBe(false)
  })

  describe('entry-only units', () => {
    const importedFeta = () => makeRecipeDetail({
      id: 'r1',
      ingredients: [{
        ...makeRecipeDetail().ingredients[0],
        amount: 113.398, unit: 'g', sourceAmount: 4, sourceUnit: 'ounces', entryUnit: 'oz',
      }],
    })

    async function mountEditing(detail) {
      const wrapper = mountForm({ id: 'r1' })
      useIngredientStore().ingredients = [makeIngredient()]
      wrapper.vm.populateForm(detail)
      await wrapper.vm.$nextTick()
      return wrapper
    }

    const unitSelect = wrapper => wrapper.findAllComponents({ name: 'VSelect' })
      .filter(s => s.props('label') === 'Unit')[0]

    async function submittedRow(wrapper) {
      const recipeStore = useRecipeStore()
      vi.spyOn(recipeStore, 'updateRecipe').mockResolvedValue(makeRecipeDetail({ id: 'r1' }))
      await wrapper.vm.submit()
      return recipeStore.updateRecipe.mock.calls[0][1].ingredients[0]
    }

    it('reopens a row in the entry-only unit its source states, not the stored grams', async () => {
      const wrapper = await mountEditing(importedFeta())
      expect(unitSelect(wrapper).props('modelValue')).toBe('oz')
      expect(wrapper.vm.form.ingredients[0].amount).toBe(4)
    })

    it('saves a reopened row unchanged as typed, keeping its source for the API to match', async () => {
      const wrapper = await mountEditing(importedFeta())
      expect(await submittedRow(wrapper)).toMatchObject({
        amount: 4, unit: 'oz', sourceAmount: 4, sourceUnit: 'ounces',
      })
    })

    it('reopens a row with no entry unit as stored', async () => {
      const detail = importedFeta()
      detail.ingredients[0] = { ...detail.ingredients[0], amount: 150, entryUnit: null }
      const wrapper = await mountEditing(detail)
      expect(wrapper.vm.form.ingredients[0]).toMatchObject({ amount: 150, unit: 'g' })
    })

    it('rounds a row with no entry unit to 2 decimals, and saves the rounded amount', async () => {
      const detail = importedFeta()
      detail.ingredients[0] = { ...detail.ingredients[0], amount: 236.588, unit: 'ml', entryUnit: null }
      const wrapper = await mountEditing(detail)
      expect(wrapper.vm.form.ingredients[0].amount).toBe(236.59)
      expect(await submittedRow(wrapper)).toMatchObject({ amount: 236.59, unit: 'ml' })
    })

    it('clears the source when the row is changed to a storable unit', async () => {
      const wrapper = await mountEditing(importedFeta())
      unitSelect(wrapper).vm.$emit('update:modelValue', 'g')
      await wrapper.vm.$nextTick()
      expect(await submittedRow(wrapper)).toMatchObject({
        amount: 4, unit: 'g', sourceAmount: null, sourceUnit: null,
      })
    })

    it('sends a unit picked as oz for the API to convert', async () => {
      const wrapper = await mountEditing(makeRecipeDetail({ id: 'r1' }))
      wrapper.vm.form.ingredients[0].amount = 4
      unitSelect(wrapper).vm.$emit('update:modelValue', 'oz')
      await wrapper.vm.$nextTick()
      expect(await submittedRow(wrapper)).toMatchObject({
        amount: 4, unit: 'oz', sourceAmount: null, sourceUnit: null,
      })
    })
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

    await wrapper.vm.submit()

    const [, payload] = recipeStore.updateRecipe.mock.calls[0]
    expect(payload.ingredients[0].amount).toBeNull()
    expect(payload.ingredients[0].unit).toBeNull()
  })
})
