import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import { useRecipeStore } from '@/stores/recipes'
import RecipeScrapePreviewView from './RecipeScrapePreviewView.vue'
import { UNIT_PICKER_ITEMS } from '@/constants/units'

vi.mock('@/services/api', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
  assetUrl: vi.fn((p) => p ?? ''),
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn() }),
  RouterLink: { template: '<a><slot /></a>' },
}))

const vuetify = createVuetify({ components, directives })

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
    const confirmSpy = vi.spyOn(store, 'confirmScrape').mockResolvedValue('new-id')

    await saveButton(wrapper).trigger('click')
    await flushPromises()

    expect(confirmSpy.mock.calls[0][0].ingredients[0]).toMatchObject({
      amount: 1, unit: 'lb', sourceAmount: 1, sourceUnit: 'pound',
    })
  })

  it('rounds a row with no entry unit to 2 decimals, and saves the rounded amount', async () => {
    const preview = makePreview()
    Object.assign(preview.ingredients[0], {
      amount: 236.588, unit: 'ml', sourceAmount: 1, sourceUnit: 'cup', entryUnit: null,
    })
    const { wrapper, store } = await mountView(preview)
    const confirmSpy = vi.spyOn(store, 'confirmScrape').mockResolvedValue('new-id')

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
    const confirmSpy = vi.spyOn(store, 'confirmScrape').mockResolvedValue('new-id')

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
    const confirmSpy = vi.spyOn(store, 'confirmScrape').mockResolvedValue('new-id')
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
      const confirmSpy = vi.spyOn(store, 'confirmScrape').mockResolvedValue('new-id')
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
    const confirmSpy = vi.spyOn(store, 'confirmScrape').mockResolvedValue('new-id')

    wrapper.vm.form.ingredients[0].unit = 'g'
    await saveButton(wrapper).trigger('click')
    await flushPromises()

    expect(confirmSpy.mock.calls[0][0].ingredients[0]).toMatchObject({ amount: 300, unit: 'g' })
  })

  it('saves an amount whose unit was cleared as a count', async () => {
    // The API stores an amount with no unit as pcs.
    const { wrapper, store } = await mountView()
    const confirmSpy = vi.spyOn(store, 'confirmScrape').mockResolvedValue('new-id')

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
    const confirmSpy = vi.spyOn(store, 'confirmScrape').mockResolvedValue('new-id')

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
    const confirmSpy = vi.spyOn(store, 'confirmScrape').mockResolvedValue('new-id')

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
    const confirmSpy = vi.spyOn(store, 'confirmScrape').mockResolvedValue('new-id')

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
    const confirmSpy = vi.spyOn(store, 'confirmScrape').mockResolvedValue('new-id')

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
    vi.spyOn(store, 'confirmScrape').mockResolvedValue('new-id')

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
