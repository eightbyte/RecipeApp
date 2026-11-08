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
      amount: 113.398, unit: 'g', sourceAmount: 4, sourceUnit: 'ounces',
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

  it('blocks saving until every unstorable unit has been picked', async () => {
    const preview = makePreview()
    preview.ingredients[0].unit = 'can'
    const { wrapper, store } = await mountView(preview)
    const confirmSpy = vi.spyOn(store, 'confirmScrape').mockResolvedValue('new-id')

    const buttons = wrapper.findAllComponents({ name: 'VBtn' })
    const saveBtn = buttons.find(b => b.text().includes('Save Recipe'))
    await saveBtn?.trigger('click')
    await flushPromises()

    expect(confirmSpy).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('Pick a unit for "Chicken Breast"')
  })

  it('names the unstorable source unit and offers clearing the amount', async () => {
    const preview = makePreview()
    preview.ingredients[0].amount     = 1
    preview.ingredients[0].unit       = 'can'
    preview.ingredients[0].sourceUnit = 'can'
    const { wrapper } = await mountView(preview)

    await saveButton(wrapper).trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain(
      'Pick a unit for "Chicken Breast", or clear its amount. The source said "can", which this app can\'t store.')
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
