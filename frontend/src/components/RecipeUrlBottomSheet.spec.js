import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import { useRecipeStore } from '@/stores/recipes'
import RecipeUrlBottomSheet from './RecipeUrlBottomSheet.vue'

vi.mock('@/services/api', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
  assetUrl: vi.fn((p) => p ?? ''),
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn() }),
}))

const vuetify = createVuetify({ components, directives })

function mountSheet(modelValue = true) {
  const pinia = createPinia()
  setActivePinia(pinia)

  const wrapper = mount(RecipeUrlBottomSheet, {
    props: { modelValue },
    global: {
      plugins: [vuetify, pinia],
      stubs: {
        // Render sheet content directly so we can find child components in tests
        VBottomSheet: { template: '<div><slot /></div>' },
        teleport: true,
      },
    },
  })
  return { wrapper, store: useRecipeStore() }
}

describe('RecipeUrlBottomSheet', () => {
  beforeEach(() => vi.clearAllMocks())

  it('renders a URL text field when open', async () => {
    const { wrapper } = mountSheet(true)
    await wrapper.vm.$nextTick()
    const input = wrapper.findComponent({ name: 'VTextField' })
    expect(input.exists()).toBe(true)
  })

  it('shows progress linear while scrapeLoading is true', async () => {
    const { wrapper, store } = mountSheet(true)
    store.scrapeLoading = true
    await wrapper.vm.$nextTick()
    const progress = wrapper.findComponent({ name: 'VProgressLinear' })
    expect(progress.exists()).toBe(true)
  })

  it('shows loading message while scrapeLoading is true', async () => {
    const { wrapper, store } = mountSheet(true)
    store.scrapeLoading = true
    await wrapper.vm.$nextTick()
    expect(wrapper.text()).toContain('Extracting recipe')
  })

  it('displays scrapeError message when set', async () => {
    const { wrapper, store } = mountSheet(true)
    store.scrapeError = 'URL unreachable'
    await wrapper.vm.$nextTick()
    expect(wrapper.text()).toContain('URL unreachable')
  })

  it('Import button is disabled when URL is empty', async () => {
    const { wrapper } = mountSheet(true)
    await wrapper.vm.$nextTick()
    const buttons = wrapper.findAllComponents({ name: 'VBtn' })
    const importBtn = buttons.find(b => b.text().includes('Import'))
    expect(importBtn?.props('disabled')).toBe(true)
  })

  it('Import button is disabled while loading', async () => {
    const { wrapper, store } = mountSheet(true)
    store.scrapeLoading = true
    await wrapper.vm.$nextTick()
    const buttons = wrapper.findAllComponents({ name: 'VBtn' })
    const importBtn = buttons.find(b => b.text().includes('Import'))
    expect(importBtn?.props('disabled')).toBe(true)
  })
})
