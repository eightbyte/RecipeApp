import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import { useRecipeStore } from '@/stores/recipes'
import RecipeScrapePreviewView from './RecipeScrapePreviewView.vue'

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
    const selects = wrapper.findAllComponents({ name: 'VSelect' })
    expect(selects.length).toBe(1)
    expect(selects[0].props('label')).toBe('Category')
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

  it('shows loading state while confirmLoading is true', async () => {
    const { wrapper, store } = await mountView()
    store.confirmLoading = true
    await wrapper.vm.$nextTick()
    const progress = wrapper.findComponent({ name: 'VProgressLinear' })
    expect(progress.exists()).toBe(true)
  })
})
