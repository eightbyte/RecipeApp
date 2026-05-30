import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import { useRecipeStore } from '@/stores/recipes'
import RecipesView from './RecipesView.vue'
import { makeRecipe } from '@/test/factories'

vi.mock('@/services/api', () => ({
  default: { get: vi.fn().mockResolvedValue({ data: [] }), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
  assetUrl: vi.fn((p) => p ?? ''),
}))

const vuetify = createVuetify({ components, directives })

let pinia

beforeEach(() => {
  pinia = createPinia()
  setActivePinia(pinia)
})

async function mountView() {
  const store = useRecipeStore()
  // Stub fetchRecipes so onMounted doesn't overwrite state we set in tests
  vi.spyOn(store, 'fetchRecipes').mockResolvedValue(undefined)

  const wrapper = mount(RecipesView, {
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
  return { wrapper, store }
}

describe('RecipesView', () => {
  it('shows loading skeletons when store.loading is true', async () => {
    const { wrapper, store } = await mountView()
    store.loading = true
    await wrapper.vm.$nextTick()

    expect(wrapper.find('.v-skeleton-loader').exists()).toBe(true)
  })

  it('renders a card for each recipe in store.recipes', async () => {
    const { wrapper, store } = await mountView()
    store.loading = false
    store.recipes = [makeRecipe({ id: '1', name: 'A' }), makeRecipe({ id: '2', name: 'B' })]
    await wrapper.vm.$nextTick()

    const cards = wrapper.findAllComponents({ name: 'VCard' })
    expect(cards.length).toBeGreaterThanOrEqual(2)
  })

  it('shows empty-state message when no recipes and not loading', async () => {
    const { wrapper, store } = await mountView()
    store.loading = false
    store.recipes = []
    await wrapper.vm.$nextTick()

    expect(wrapper.text()).toContain('No recipes yet')
  })

  it('recipe card has :to prop navigating to recipe-detail', async () => {
    const { wrapper, store } = await mountView()
    store.loading = false
    store.recipes = [makeRecipe({ id: 'r1' })]
    await wrapper.vm.$nextTick()

    const card = wrapper.findComponent({ name: 'VCard' })
    expect(card.props('to')).toEqual({ name: 'recipe-detail', params: { id: 'r1' } })
  })

  it('renders a speed-dial FAB for creating a new recipe', async () => {
    // The "Add manually" VBtn lives inside VSpeedDial's overlay (only rendered when open).
    // We verify the dial itself is present and the activator FAB renders.
    const { wrapper } = await mountView()
    const dial = wrapper.findComponent({ name: 'VSpeedDial' })
    expect(dial.exists()).toBe(true)

    const fab = wrapper.findComponent({ name: 'VFab' })
    expect(fab.exists()).toBe(true)
  })
})
