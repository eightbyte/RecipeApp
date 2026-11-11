import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import { useRecipeStore } from '@/stores/recipes'
import { useUiStore } from '@/stores/ui'
import RecipeScrapePreviewView from './RecipeScrapePreviewView.vue'
import { UNIT_PICKER_ITEMS } from '@/constants/units'
import { makeIngredient } from '@/test/factories'

const { catalogue } = vi.hoisted(() => ({ catalogue: [] }))

vi.mock('@/services/api', () => ({
  default: {
    get: vi.fn(() => Promise.resolve({ data: catalogue })),
    post: vi.fn(), put: vi.fn(), delete: vi.fn(),
  },
  assetUrl: vi.fn((p) => p ?? ''),
}))

catalogue.push(
  makeIngredient({ id: 'existing-id', name: 'chicken breast', displayName: 'Chicken Breast', category: 'MEAT_SEAFOOD' }),
  makeIngredient({ id: 'broth-id',    name: 'chicken broth',  displayName: 'Chicken Broth',  category: 'SOUPS_BROTH' }),
  makeIngredient({ id: 'starch-id',   name: 'cornstarch',     displayName: 'Cornstarch',     category: 'BAKING_SPICES' }),
)

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn() }),
  RouterLink: { template: '<a><slot /></a>' },
}))

const vuetify = createVuetify({ components, directives })

/** What confirmScrape resolves with for a save that kept its photo or had none. */
const SAVED = { id: 'new-id', photoSkipped: false }

const makePreview = (overrides = {}) => ({
  name:        'Chicken Stir Fry',
  description: 'Quick weeknight dinner',
  servings:    2,
  sourceUrl:   'https://example.com/stir-fry',
  ingredients: [
    {
      ingredientId:      'existing-id',
      name:              'chicken breast',
      displayName:       'Chicken Breast',
      amount:            300,
      unit:              'g',
      notes:             null,
      isNew:             false,
      suggestedCategory: 'MEAT_SEAFOOD',
      displayOrder:      0,
    },
    {
      ingredientId:      null,
      name:              'bok choy',
      displayName:       'Bok Choy',
      amount:            200,
      unit:              'g',
      notes:             null,
      isNew:             true,
      suggestedCategory: 'PRODUCE',
      displayOrder:      1,
    },
  ],
  steps: [
    { stepNumber: 1, instruction: 'Slice the chicken', ingredientIndexes: [0] },
  ],
  ...overrides,
})

async function mountView(preview = makePreview()) {
  const pinia = createPinia()
  setActivePinia(pinia)
  const store = useRecipeStore()
  store.scrapePreview = preview

  const wrapper = mount(RecipeScrapePreviewView, {
    global: {
      plugins: [vuetify, pinia],
      stubs: {
        RouterLink: { template: '<a><slot /></a>' },
        'router-link': { template: '<a><slot /></a>' },
        teleport: true,
      },
    },
  })
  await flushPromises()
  return { wrapper, store }
}

const saveButton = wrapper =>
  wrapper.findAllComponents({ name: 'VBtn' }).find(b => b.text().includes('Save Recipe'))

describe('RecipeScrapePreviewView', () => {
  it('renders the recipe name pre-filled', async () => {
    const { wrapper } = await mountView()
    const fields = wrapper.findAllComponents({ name: 'VTextField' })
    const nameField = fields.find(f => f.props('label') === 'Recipe name')
    expect(nameField?.props('modelValue')).toBe('Chicken Stir Fry')
  })

  it('shows a New chip for new ingredients', async () => {
    const { wrapper } = await mountView()
    const chips = wrapper.findAllComponents({ name: 'VChip' })
    const newChip = chips.find(c => c.text().trim() === 'New')
    expect(newChip?.exists()).toBe(true)
  })

  it('renders a category VSelect only for new ingredients', async () => {
    const { wrapper } = await mountView()
    const categorySelects = wrapper.findAllComponents({ name: 'VSelect' })
      .filter(s => s.props('label') === 'Category')
    expect(categorySelects.length).toBe(1)
  })

  it('labels the Category select with the category titles', async () => {
    const { wrapper } = await mountView()
    const categorySelect = wrapper.findAllComponents({ name: 'VSelect' })
      .find(s => s.props('label') === 'Category')

    expect(categorySelect.props('items')).toContainEqual(
      expect.objectContaining({ value: 'GRAINS_RICE', title: 'Grains & Rice' }))
    expect(categorySelect.text()).toContain('Produce')
  })

  it('opens a row converted from an entry-only unit in that unit', async () => {
    const preview = makePreview()
    Object.assign(preview.ingredients[0], {
      amount: 453.592, unit: 'g', sourceAmount: 1, sourceUnit: 'lb', entryUnit: 'lb',
    })
    const { wrapper } = await mountView(preview)

    const amountField = wrapper.findAllComponents({ name: 'VTextField' })
      .filter(f => f.props('label') === 'Amount')[0]
    const unitSelect = wrapper.findAllComponents({ name: 'VSelect' })
      .filter(s => s.props('label') === 'Unit')[0]
    expect(amountField.props('modelValue')).toBe(1)
    expect(unitSelect.props('modelValue')).toBe('lb')
  })

  it('saves a row opened in its entry unit with the source measurement kept', async () => {
    const preview = makePreview()
    Object.assign(preview.ingredients[0], {
      amount: 453.592, unit: 'g', sourceAmount: 1, sourceUnit: 'pound', entryUnit: 'lb',
    })
    const { wrapper, store } = await mountView(preview)
    const confirmSpy = vi.spyOn(store, 'confirmScrape').mockResolvedValue(SAVED)

    await saveButton(wrapper).trigger('click')
    await flushPromises()

    expect(confirmSpy.mock.calls[0][0].ingredients[0]).toMatchObject({
      amount: 1, unit: 'lb', sourceAmount: 1, sourceUnit: 'pound',
    })
  })

  describe('amounts from steps', () => {
    const stepAmountHint = wrapper => wrapper.findAll('[data-test="step-amounts"]')

    it('shows the step amounts a filled row was added up from', async () => {
      const preview = makePreview()
      Object.assign(preview.ingredients[0], {
        amount: 1, unit: 'tsp', amountFromSteps: true,
        stepAmounts: [{ amount: 0.5, unit: 'tsp' }, { amount: 0.5, unit: 'tsp' }],
      })
      const { wrapper } = await mountView(preview)

      const hints = stepAmountHint(wrapper)
      expect(hints).toHaveLength(1)
      expect(hints[0].text()).toBe('From steps: 0.5 tsp + 0.5 tsp')
    })

    it('asks for a total on a row whose step amounts could not be added up', async () => {
      const preview = makePreview()
      Object.assign(preview.ingredients[0], {
        amount: null, unit: null, amountFromSteps: false,
        stepAmounts: [{ amount: 1, unit: 'tbsp' }, { amount: 10, unit: 'g' }],
      })
      const { wrapper } = await mountView(preview)

      expect(stepAmountHint(wrapper)[0].text()).toBe('Steps say 1 tbsp + 10 g. Enter the total.')
    })

    it('names a Sizeless Unit stated with no amount by its word', async () => {
      const preview = makePreview()
      Object.assign(preview.ingredients[0], {
        amount: null, unit: null, sourceUnit: 'pinch', amountFromSteps: true,
        stepAmounts: [{ amount: null, unit: 'pinch' }, { amount: 1, unit: 'pinch' }],
      })
      const { wrapper } = await mountView(preview)

      expect(stepAmountHint(wrapper)[0].text()).toBe('From steps: pinch + 1 pinch')
    })

    it('shows no hint on rows whose list stated the amount', async () => {
      const { wrapper } = await mountView()

      expect(stepAmountHint(wrapper)).toHaveLength(0)
    })

    it('never sends the hint on save', async () => {
      const preview = makePreview()
      Object.assign(preview.ingredients[0], {
        amount: 1, unit: 'tsp', amountFromSteps: true, stepAmounts: [{ amount: 1, unit: 'tsp' }],
      })
      const { wrapper, store } = await mountView(preview)
      const confirmSpy = vi.spyOn(store, 'confirmScrape').mockResolvedValue(SAVED)

      await saveButton(wrapper).trigger('click')
      await flushPromises()

      const sent = confirmSpy.mock.calls[0][0].ingredients[0]
      expect(sent).not.toHaveProperty('amountFromSteps')
      expect(sent).not.toHaveProperty('stepAmounts')
    })
  })

  it('rounds a row with no entry unit to 2 decimals, and saves the rounded amount', async () => {
    const preview = makePreview()
    Object.assign(preview.ingredients[0], {
      amount: 236.588, unit: 'ml', sourceAmount: 1, sourceUnit: 'cup', entryUnit: null,
    })
    const { wrapper, store } = await mountView(preview)
    const confirmSpy = vi.spyOn(store, 'confirmScrape').mockResolvedValue(SAVED)

    await saveButton(wrapper).trigger('click')
    await flushPromises()

    expect(confirmSpy.mock.calls[0][0].ingredients[0]).toMatchObject({ amount: 236.59, unit: 'ml' })
  })

  it('renders the unit as a select over the grouped picker units, one per ingredient', async () => {
    const { wrapper } = await mountView()
    const unitSelects = wrapper.findAllComponents({ name: 'VSelect' })
      .filter(s => s.props('label') === 'Unit')

    expect(unitSelects.length).toBe(2)
    expect(unitSelects[0].props('items')).toEqual(UNIT_PICKER_ITEMS)
  })

  it('sends a unit picked as oz for the API to convert', async () => {
    const { wrapper, store } = await mountView()
    const confirmSpy = vi.spyOn(store, 'confirmScrape').mockResolvedValue(SAVED)

    wrapper.vm.form.ingredients[0].amount = 4
    wrapper.findAllComponents({ name: 'VSelect' })
      .filter(s => s.props('label') === 'Unit')[0]
      .vm.$emit('update:modelValue', 'oz')
    await saveButton(wrapper).trigger('click')
    await flushPromises()

    expect(confirmSpy.mock.calls[0][0].ingredients[0]).toMatchObject({ amount: 4, unit: 'oz' })
  })

  it('keeps the scraped source on a row passed through oz and back to a storable unit', async () => {
    const preview = makePreview()
    Object.assign(preview.ingredients[0], { amount: 113.398, sourceAmount: 4, sourceUnit: 'ounces' })
    const { wrapper, store } = await mountView(preview)
    const confirmSpy = vi.spyOn(store, 'confirmScrape').mockResolvedValue(SAVED)
    const unitSelect = () => wrapper.findAllComponents({ name: 'VSelect' })
      .filter(s => s.props('label') === 'Unit')[0]

    unitSelect().vm.$emit('update:modelValue', 'oz')
    await wrapper.vm.$nextTick()
    unitSelect().vm.$emit('update:modelValue', 'g')
    await saveButton(wrapper).trigger('click')
    await flushPromises()

    expect(confirmSpy.mock.calls[0][0].ingredients[0]).toMatchObject({
      amount: 113.4, unit: 'g', sourceAmount: 4, sourceUnit: 'ounces',
    })
  })

  it('leaves an unstorable scraped unit unselected so it has to be picked', async () => {
    // The backend passes an unrecognised unit through verbatim for a human to correct.
    const preview = makePreview()
    preview.ingredients[0].unit = 'can'
    const { wrapper } = await mountView(preview)

    const unitSelects = wrapper.findAllComponents({ name: 'VSelect' })
      .filter(s => s.props('label') === 'Unit')
    expect(unitSelects[0].props('modelValue')).toBe(null)
    expect(unitSelects[0].props('placeholder')).toBeUndefined()
    expect(unitSelects[1].props('placeholder')).toBe('pcs')
  })

  describe('problems shown before Save', () => {
    const unitSelects = wrapper => wrapper.findAllComponents({ name: 'VSelect' })
      .filter(s => s.props('label') === 'Unit')
    const fieldsLabelled = (wrapper, label) => wrapper.findAllComponents({ name: 'VTextField' })
      .filter(f => f.props('label') === label)
    const banner = wrapper => wrapper.find('[data-test="recipe-problems"]')

    const withCans = () => {
      const preview = makePreview()
      preview.ingredients[0].unit = 'can'
      preview.ingredients[1].unit = 'jar'
      return preview
    }

    it('flags every unstorable unit on load, on the field, the row and the banner', async () => {
      const { wrapper } = await mountView(withCans())

      expect(unitSelects(wrapper).map(s => s.props('errorMessages'))).toEqual([
        ['Source said "can": pick a unit or clear the amount.'],
        ['Source said "jar": pick a unit or clear the amount.'],
      ])
      expect(wrapper.findAllComponents({ name: 'VChip' }).filter(c => c.text() === 'Needs unit')).toHaveLength(2)
      expect(banner(wrapper).text()).toContain('2 problems need fixing before saving')
    })

    it('lists every problem on one Save, and does not save', async () => {
      const { wrapper, store } = await mountView(withCans())
      const confirmSpy = vi.spyOn(store, 'confirmScrape').mockResolvedValue(SAVED)
      wrapper.vm.form.name = ''

      await saveButton(wrapper).trigger('click')
      await flushPromises()

      expect(confirmSpy).not.toHaveBeenCalled()
      const text = banner(wrapper).text()
      expect(text).toContain('3 problems need fixing before saving')
      expect(text).toContain('Recipe name — Required')
      expect(text).toContain('Ingredient 1 (Chicken Breast) — Source said "can"')
      expect(text).toContain('Ingredient 2 (Bok Choy) — Source said "jar"')
    })

    it('clears a problem as soon as it is fixed after a Save attempt', async () => {
      const { wrapper } = await mountView(withCans())
      await saveButton(wrapper).trigger('click')
      await flushPromises()

      unitSelects(wrapper)[0].vm.$emit('update:modelValue', 'g')
      await flushPromises()

      expect(unitSelects(wrapper)[0].props('errorMessages')).toEqual([])
      expect(banner(wrapper).text()).toContain('1 problem needs fixing before saving')

      unitSelects(wrapper)[1].vm.$emit('update:modelValue', 'g')
      await flushPromises()
      expect(banner(wrapper).exists()).toBe(false)
    })

    it('shows a new problem immediately once Save has been pressed', async () => {
      const { wrapper, store } = await mountView()
      vi.spyOn(store, 'confirmScrape').mockRejectedValue({ message: 'offline' })
      await saveButton(wrapper).trigger('click')
      await flushPromises()

      wrapper.vm.form.ingredients[0].amount = 0
      await flushPromises()

      expect(fieldsLabelled(wrapper, 'Amount')[0].props('errorMessages'))
        .toEqual(['Must be more than 0, or left empty'])
    })

    it('flags an amount of 0 the user types only once the field is left', async () => {
      const { wrapper } = await mountView()
      const amount = () => fieldsLabelled(wrapper, 'Amount')[0]

      wrapper.vm.form.ingredients[0].amount = 0
      await flushPromises()
      expect(amount().props('errorMessages')).toEqual([])
      expect(banner(wrapper).exists()).toBe(false)

      amount().vm.$emit('update:focused', false)
      await flushPromises()
      expect(amount().props('errorMessages')).toEqual(['Must be more than 0, or left empty'])
      expect(wrapper.findAllComponents({ name: 'VChip' }).some(c => c.text() === 'Needs attention')).toBe(true)
    })

    it('flags an over-long field only after a Save attempt, even once it is left', async () => {
      const { wrapper } = await mountView()
      const notes = () => fieldsLabelled(wrapper, 'Notes')[0]

      wrapper.vm.form.ingredients[0].notes = 'x'.repeat(501)
      notes().vm.$emit('update:focused', false)
      await flushPromises()
      expect(notes().props('errorMessages')).toEqual([])

      await saveButton(wrapper).trigger('click')
      await flushPromises()
      expect(notes().props('errorMessages')).toEqual(['Must be 500 characters or fewer'])
    })

    it('flags a scraped step with no instruction on load', async () => {
      const { wrapper } = await mountView(makePreview({
        steps: [{ stepNumber: 1, instruction: '', ingredientIndexes: [] }],
      }))

      const instruction = wrapper.findAllComponents({ name: 'VTextarea' })
        .find(t => t.props('label') === 'Step 1 instruction')
      expect(instruction.props('errorMessages')).toEqual(['Required'])
    })

    it('treats an arrived problem as the user\'s own once it has been fixed', async () => {
      const { wrapper } = await mountView(makePreview({ description: 'x'.repeat(2001) }))
      const description = () => wrapper.findAllComponents({ name: 'VTextarea' })
        .find(t => t.props('label') === 'Description (optional)')

      wrapper.vm.form.description = 'Short'
      await flushPromises()
      wrapper.vm.form.description = 'x'.repeat(2001)
      description().vm.$emit('update:focused', false)
      await flushPromises()

      expect(description().props('errorMessages')).toEqual([])
    })

    it('flags only the field that was left, not the rest of its row', async () => {
      const { wrapper } = await mountView()

      wrapper.vm.form.ingredients[0].amount = 0
      fieldsLabelled(wrapper, 'Notes')[0].vm.$emit('update:focused', false)
      await flushPromises()

      expect(fieldsLabelled(wrapper, 'Amount')[0].props('errorMessages')).toEqual([])
    })

    it('scrolls the first field in error into view on Save', async () => {
      // jsdom does not implement scrolling.
      const scrollIntoView = vi.fn()
      Element.prototype.scrollIntoView = scrollIntoView
      const { wrapper } = await mountView(withCans())

      await saveButton(wrapper).trigger('click')
      await flushPromises()

      expect(scrollIntoView).toHaveBeenCalledTimes(1)
      expect(scrollIntoView.mock.contexts[0]).toBe(unitSelects(wrapper)[0].element)
      delete Element.prototype.scrollIntoView
    })

    it('flags a scraped description over the limit on load', async () => {
      const { wrapper } = await mountView(makePreview({ description: 'x'.repeat(2001) }))

      const description = wrapper.findAllComponents({ name: 'VTextarea' })
        .find(t => t.props('label') === 'Description (optional)')
      expect(description.props('errorMessages')).toEqual(['Must be 2000 characters or fewer'])
    })
  })


  it('saves an unstorable unit once one has been picked', async () => {
    const preview = makePreview()
    preview.ingredients[0].unit = 'can'
    const { wrapper, store } = await mountView(preview)
    const confirmSpy = vi.spyOn(store, 'confirmScrape').mockResolvedValue(SAVED)

    wrapper.vm.form.ingredients[0].unit = 'g'
    await saveButton(wrapper).trigger('click')
    await flushPromises()

    expect(confirmSpy.mock.calls[0][0].ingredients[0]).toMatchObject({ amount: 300, unit: 'g' })
  })

  it('saves an amount whose unit was cleared as a count', async () => {
    // The API stores an amount with no unit as pcs.
    const { wrapper, store } = await mountView()
    const confirmSpy = vi.spyOn(store, 'confirmScrape').mockResolvedValue(SAVED)

    wrapper.vm.form.ingredients[0].unit = null
    await saveButton(wrapper).trigger('click')
    await flushPromises()

    expect(confirmSpy).toHaveBeenCalled()
    expect(confirmSpy.mock.calls[0][0].ingredients[0]).toMatchObject({ amount: 300, unit: null })
  })

  it('saves a scraped amount that arrived with no unit', async () => {
    const preview = makePreview()
    preview.ingredients[0].unit = null
    const { wrapper, store } = await mountView(preview)
    const confirmSpy = vi.spyOn(store, 'confirmScrape').mockResolvedValue(SAVED)

    await saveButton(wrapper).trigger('click')
    await flushPromises()

    expect(confirmSpy).toHaveBeenCalled()
  })

  it('saves a row with no amount as an Unquantified Ingredient, keeping its source', async () => {
    const preview = makePreview()
    preview.ingredients[0].amount       = null
    preview.ingredients[0].unit         = null
    preview.ingredients[0].sourceAmount = 1
    preview.ingredients[0].sourceUnit   = 'pinch'
    const { wrapper, store } = await mountView(preview)
    const confirmSpy = vi.spyOn(store, 'confirmScrape').mockResolvedValue(SAVED)

    await saveButton(wrapper).trigger('click')
    await flushPromises()

    const row = confirmSpy.mock.calls[0][0].ingredients[0]
    expect(row).toMatchObject({ amount: null, unit: null, sourceAmount: 1, sourceUnit: 'pinch' })
  })

  it('saves a row whose amount was cleared, even though its unit could not be stored', async () => {
    const preview = makePreview()
    preview.ingredients[0].unit       = 'can'
    preview.ingredients[0].sourceUnit = 'can'
    const { wrapper, store } = await mountView(preview)
    const confirmSpy = vi.spyOn(store, 'confirmScrape').mockResolvedValue(SAVED)

    wrapper.vm.form.ingredients[0].amount = ''
    await saveButton(wrapper).trigger('click')
    await flushPromises()

    expect(confirmSpy).toHaveBeenCalled()
    expect(confirmSpy.mock.calls[0][0].ingredients[0]).toMatchObject({ amount: null, unit: null })
  })

  it('shows the Unit field blank and disabled for a row with no amount', async () => {
    const preview = makePreview()
    preview.ingredients[0].amount = null
    const { wrapper } = await mountView(preview)

    const unitSelect = wrapper.findAllComponents({ name: 'VSelect' })
      .filter(s => s.props('label') === 'Unit')[0]
    expect(unitSelect.props('modelValue')).toBeNull()
    expect(unitSelect.props('disabled')).toBe(true)
  })

  it('carries the source measurement into the confirm payload', async () => {
    const preview = makePreview()
    preview.ingredients[0].amount       = 240
    preview.ingredients[0].unit         = 'g'
    preview.ingredients[0].sourceAmount = 2
    preview.ingredients[0].sourceUnit   = 'cups'
    const { wrapper, store } = await mountView(preview)
    const confirmSpy = vi.spyOn(store, 'confirmScrape').mockResolvedValue(SAVED)

    const buttons = wrapper.findAllComponents({ name: 'VBtn' })
    const saveBtn = buttons.find(b => b.text().includes('Save Recipe'))
    await saveBtn?.trigger('click')
    await flushPromises()

    const payload = confirmSpy.mock.calls[0][0]
    expect(payload.ingredients[0].sourceAmount).toBe(2)
    expect(payload.ingredients[0].sourceUnit).toBe('cups')
  })

  it('shows warning when scrapePreview is null', async () => {
    const { wrapper } = await mountView(null)
    expect(wrapper.text()).toContain('No preview available')
  })

  it('Save Recipe button calls confirmScrape', async () => {
    const { wrapper, store } = await mountView()
    vi.spyOn(store, 'confirmScrape').mockResolvedValue(SAVED)

    const buttons = wrapper.findAllComponents({ name: 'VBtn' })
    const saveBtn = buttons.find(b => b.text().includes('Save Recipe'))
    await saveBtn?.trigger('click')
    await flushPromises()

    expect(store.confirmScrape).toHaveBeenCalledWith(
      expect.objectContaining({ name: 'Chicken Stir Fry' })
    )
  })

  it('shows the reason a save was rejected in the error alert', async () => {
    const { wrapper, store } = await mountView()
    // The shape the api interceptor rejects with.
    vi.spyOn(store, 'confirmScrape').mockRejectedValue({
      status: 400,
      message: 'One or more validation errors occurred. Name is required.',
    })

    const saveBtn = wrapper.findAllComponents({ name: 'VBtn' }).find(b => b.text().includes('Save Recipe'))
    await saveBtn.trigger('click')
    await flushPromises()

    expect(wrapper.findComponent({ name: 'VAlert' }).text())
      .toContain('One or more validation errors occurred. Name is required.')
  })

  it('shows loading state while confirmLoading is true', async () => {
    const { wrapper, store } = await mountView()
    store.confirmLoading = true
    await wrapper.vm.$nextTick()
    const progress = wrapper.findComponent({ name: 'VProgressLinear' })
    expect(progress.exists()).toBe(true)
  })
})

describe('RecipeScrapePreviewView — editing ingredients and steps', () => {
  const twoStepPreview = () => makePreview({
    steps: [
      { stepNumber: 1, instruction: 'Slice the chicken', ingredientIndexes: [0] },
      { stepNumber: 2, instruction: 'Stir-fry it all',   ingredientIndexes: [0, 1] },
    ],
  })

  const ingredientBoxes = wrapper => wrapper.findAllComponents({ name: 'VAutocomplete' })
    .filter(a => a.props('label') === 'Ingredient')
  const buttonLabelled = (wrapper, label) => wrapper.findAllComponents({ name: 'VBtn' })
    .filter(b => b.attributes('aria-label') === label)
  const buttonText = (wrapper, text) => wrapper.findAllComponents({ name: 'VBtn' })
    .find(b => b.text() === text)
  const stepChips = (wrapper, stepIdx) => wrapper.findAll('[data-test="step-ingredients"]')[stepIdx]
    .findAllComponents({ name: 'VChip' })

  async function save(wrapper, store) {
    const confirmSpy = vi.spyOn(store, 'confirmScrape').mockResolvedValue(SAVED)
    await saveButton(wrapper).trigger('click')
    await flushPromises()
    return confirmSpy.mock.calls[0]?.[0]
  }

  /** Types into an ingredient box and picks the dropdown entry with the given title. */
  async function pick(wrapper, rowIdx, typed, title) {
    const box = () => ingredientBoxes(wrapper)[rowIdx]
    box().vm.$emit('update:search', typed)
    await flushPromises()
    const item = box().props('items').find(i => i.displayName === title)
    expect(item, `no "${title}" entry`).toBeDefined()
    box().vm.$emit('update:modelValue', item.id)
    await flushPromises()
  }

  describe('removing an ingredient', () => {
    it('unselects it from every step and shifts later selections down, leaving instructions alone', async () => {
      const { wrapper } = await mountView(twoStepPreview())

      await buttonLabelled(wrapper, 'Remove ingredient 1')[0].trigger('click')

      expect(wrapper.vm.form.ingredients.map(i => i.displayName)).toEqual(['Bok Choy'])
      expect(wrapper.vm.form.steps.map(s => s.ingredientIndexes)).toEqual([[], [0]])
      expect(wrapper.vm.form.steps.map(s => s.instruction)).toEqual(['Slice the chicken', 'Stir-fry it all'])
    })

    it('never sends a removed New ingredient', async () => {
      const { wrapper, store } = await mountView()

      await buttonLabelled(wrapper, 'Remove ingredient 2')[0].trigger('click')
      const payload = await save(wrapper, store)

      expect(payload.ingredients).toHaveLength(1)
      expect(payload.ingredients.some(i => i.newIngredientName)).toBe(false)
    })

    it('offers Undo, which puts it back with its step selections', async () => {
      const { wrapper } = await mountView(twoStepPreview())
      const ui = useUiStore()

      await buttonLabelled(wrapper, 'Remove ingredient 1')[0].trigger('click')
      expect(ui.snackbar).toMatchObject({ message: 'Ingredient removed', action: { label: 'Undo' } })
      ui.runAction()
      await flushPromises()

      expect(wrapper.vm.form.ingredients.map(i => i.displayName)).toEqual(['Chicken Breast', 'Bok Choy'])
      expect(wrapper.vm.form.steps.map(s => [...s.ingredientIndexes].sort())).toEqual([[0], [0, 1]])
    })

    it('shows a scraped problem again once its row is put back', async () => {
      const preview = makePreview()
      preview.ingredients[0].unit = 'can'
      const { wrapper } = await mountView(preview)

      await buttonLabelled(wrapper, 'Remove ingredient 1')[0].trigger('click')
      await flushPromises()
      useUiStore().runAction()
      await flushPromises()

      expect(wrapper.findAllComponents({ name: 'VChip' }).filter(c => c.text() === 'Needs unit')).toHaveLength(1)
    })
  })

  describe('steps', () => {
    it('adds a step, numbering every step by position on save', async () => {
      const { wrapper, store } = await mountView(twoStepPreview())

      await buttonText(wrapper, 'Add step').trigger('click')
      wrapper.vm.form.steps[2].instruction = 'Serve'
      await buttonLabelled(wrapper, 'Remove step 1')[0].trigger('click')
      const payload = await save(wrapper, store)

      expect(payload.steps.map(s => [s.stepNumber, s.instruction])).toEqual([[1, 'Stir-fry it all'], [2, 'Serve']])
    })

    it('offers Undo after removing a step, which puts it back in place', async () => {
      const { wrapper } = await mountView(twoStepPreview())
      const ui = useUiStore()

      await buttonLabelled(wrapper, 'Remove step 1')[0].trigger('click')
      expect(ui.snackbar.message).toBe('Step removed')
      ui.runAction()

      expect(wrapper.vm.form.steps.map(s => s.instruction)).toEqual(['Slice the chicken', 'Stir-fry it all'])
    })
  })

  describe('the ingredient search box', () => {
    it('opens on the scraped match and on a scraped New name', async () => {
      const { wrapper } = await mountView()

      const [matched, pending] = ingredientBoxes(wrapper)
      expect(matched.text()).toContain('Chicken Breast')
      expect(pending.text()).toContain('Bok Choy')
    })

    it('fixes a wrong match by picking another catalogue Ingredient', async () => {
      const { wrapper, store } = await mountView()

      await pick(wrapper, 0, 'broth', 'Chicken Broth')
      const payload = await save(wrapper, store)

      expect(payload.ingredients[0]).toMatchObject({ ingredientId: 'broth-id', newIngredientName: null })
    })

    it('turns a pending New row into a match when a catalogue Ingredient is picked', async () => {
      const { wrapper, store } = await mountView()

      await pick(wrapper, 1, 'starch', 'Cornstarch')

      expect(wrapper.findAllComponents({ name: 'VChip' }).some(c => c.text() === 'New')).toBe(false)
      const payload = await save(wrapper, store)
      expect(payload.ingredients[1]).toMatchObject({ ingredientId: 'starch-id', newIngredientName: null, category: null })
    })

    it('offers the typed name as new, even beside a partial match', async () => {
      const { wrapper } = await mountView()

      ingredientBoxes(wrapper)[0].vm.$emit('update:search', 'corn')
      await flushPromises()

      const titles = ingredientBoxes(wrapper)[0].props('items').map(i => i.displayName)
      expect(titles).toContain('Cornstarch')
      expect(titles).toContain('Add "corn" as new')
    })

    it('does not offer a name the catalogue already has as new', async () => {
      const { wrapper } = await mountView()

      ingredientBoxes(wrapper)[1].vm.$emit('update:search', 'chicken broth')
      await flushPromises()

      expect(ingredientBoxes(wrapper)[1].props('items').map(i => i.displayName))
        .not.toContain('Add "chicken broth" as new')
    })
  })

  describe('adding an ingredient', () => {
    it('adds a row matched from the catalogue', async () => {
      const { wrapper, store } = await mountView()

      await buttonText(wrapper, 'Add').trigger('click')
      await pick(wrapper, 2, 'broth', 'Chicken Broth')
      const payload = await save(wrapper, store)

      expect(payload.ingredients[2]).toMatchObject({
        ingredientId: 'broth-id', newIngredientName: null, amount: 1, unit: 'g', displayOrder: 2,
      })
    })

    it('adds a pending New row with a Category select starting at Other, created only on save', async () => {
      const { wrapper, store } = await mountView()
      const ingredientsApi = (await import('@/services/api')).default

      await buttonText(wrapper, 'Add').trigger('click')
      await pick(wrapper, 2, 'Star Anise', 'Add "Star Anise" as new')

      expect(wrapper.findAllComponents({ name: 'VChip' }).filter(c => c.text() === 'New')).toHaveLength(2)
      const categories = wrapper.findAllComponents({ name: 'VSelect' }).filter(s => s.props('label') === 'Category')
      expect(categories.map(s => s.props('modelValue'))).toEqual(['PRODUCE', 'OTHER'])
      expect(ingredientsApi.post).not.toHaveBeenCalled()

      const payload = await save(wrapper, store)
      expect(payload.ingredients[2]).toMatchObject({
        ingredientId: null, newIngredientName: 'star anise', newIngredientDisplayName: 'Star Anise', category: 'OTHER',
      })
    })

    it('keeps the scraped Category guess when a matched row is made New', async () => {
      const { wrapper } = await mountView()

      await pick(wrapper, 0, 'chicken thigh', 'Add "chicken thigh" as new')

      expect(wrapper.vm.form.ingredients[0]).toMatchObject({ isNew: true, ingredientId: null, category: 'MEAT_SEAFOOD' })
    })

    it('will not save a row whose ingredient was never picked', async () => {
      const { wrapper, store } = await mountView()

      await buttonText(wrapper, 'Add').trigger('click')
      const payload = await save(wrapper, store)

      expect(payload).toBeUndefined()
      expect(ingredientBoxes(wrapper)[2].props('errorMessages')).toEqual(['Pick an ingredient'])
    })
  })

  describe('step chips', () => {
    it('keeps a pending New ingredient selectable', async () => {
      const { wrapper } = await mountView()

      const bokChoy = stepChips(wrapper, 0).find(c => c.text() === 'Bok Choy')
      expect(bokChoy.props('disabled')).toBe(false)
      await bokChoy.trigger('click')

      expect(wrapper.vm.form.steps[0].ingredientIndexes).toEqual([0, 1])
    })

    it('labels a step chip with a New name added on the preview', async () => {
      const { wrapper } = await mountView()

      await buttonText(wrapper, 'Add').trigger('click')
      await pick(wrapper, 2, 'Star Anise', 'Add "Star Anise" as new')

      expect(stepChips(wrapper, 0).map(c => c.text())).toContain('Star Anise')
    })

    it('saves pending New ingredients selected in steps', async () => {
      const { wrapper, store } = await mountView()

      await buttonText(wrapper, 'Add').trigger('click')
      await pick(wrapper, 2, 'Star Anise', 'Add "Star Anise" as new')
      await stepChips(wrapper, 0).find(c => c.text() === 'Bok Choy').trigger('click')
      await stepChips(wrapper, 0).find(c => c.text() === 'Star Anise').trigger('click')
      const payload = await save(wrapper, store)

      expect(payload.steps[0].ingredientIndexes).toEqual([0, 1, 2])
      expect(payload.ingredients.slice(1).map(i => i.newIngredientName)).toEqual(['bok choy', 'star anise'])
    })
  })

  it('takes its Undo away on leaving the page', async () => {
    const { wrapper } = await mountView()
    await buttonLabelled(wrapper, 'Remove ingredient 1')[0].trigger('click')

    wrapper.unmount()

    expect(useUiStore().snackbar.show).toBe(false)
  })
})

describe('RecipeScrapePreviewView — photo', () => {
  const PHOTO = 'https://cdn.example.com/stir-fry.jpg'

  const photo = wrapper => wrapper.find('[data-test="preview-photo"]')
  const removePhotoButton = wrapper => wrapper.findAllComponents({ name: 'VBtn' })
    .find(b => b.attributes('aria-label') === 'Remove photo')

  async function save(wrapper, store, result = SAVED) {
    const confirmSpy = vi.spyOn(store, 'confirmScrape').mockResolvedValue(result)
    await saveButton(wrapper).trigger('click')
    await flushPromises()
    return confirmSpy.mock.calls[0]?.[0]
  }

  it('shows the photo straight from the source site without a referrer', async () => {
    const { wrapper } = await mountView(makePreview({ imageUrl: PHOTO }))

    const img = wrapper.findComponent({ name: 'VImg' })
    expect(photo(wrapper).exists()).toBe(true)
    expect(img.props('src')).toBe(PHOTO)
    expect(img.props('referrerpolicy')).toBe('no-referrer')
  })

  it('shows nothing when the page had no photo', async () => {
    const { wrapper } = await mountView(makePreview({ imageUrl: null }))

    expect(photo(wrapper).exists()).toBe(false)
    expect(removePhotoButton(wrapper)).toBeUndefined()
  })

  it('sends the photo with the recipe', async () => {
    const { wrapper, store } = await mountView(makePreview({ imageUrl: PHOTO }))

    const payload = await save(wrapper, store)

    expect(payload.imageUrl).toBe(PHOTO)
  })

  it('Remove hides the photo and leaves it out of the save', async () => {
    const { wrapper, store } = await mountView(makePreview({ imageUrl: PHOTO }))

    await removePhotoButton(wrapper).trigger('click')
    expect(photo(wrapper).exists()).toBe(false)

    const payload = await save(wrapper, store)
    expect(payload).not.toHaveProperty('imageUrl')
  })

  it('tells the user when the recipe saved without its photo', async () => {
    const { wrapper, store } = await mountView(makePreview({ imageUrl: PHOTO }))

    await save(wrapper, store, { id: 'new-id', photoSkipped: true })

    expect(useUiStore().snackbar).toMatchObject({
      show:    true,
      message: 'Saved without the photo. You can add one on the Edit page.',
    })
  })

  it('raises no snackbar when the photo saved', async () => {
    const { wrapper, store } = await mountView(makePreview({ imageUrl: PHOTO }))

    await save(wrapper, store)

    expect(useUiStore().snackbar.show).toBe(false)
  })
})
