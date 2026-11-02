import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import RecipeBrowser from './RecipeBrowser.vue'
import { makeRecipe } from '@/test/factories'
import { RECIPE_LIST_SORT_STORAGE_KEY } from '@/constants/recipeSorts'

vi.mock('@/services/api', () => ({
  default: { get: vi.fn(), post: vi.fn() },
  assetUrl: vi.fn((p) => p ?? ''),
}))

const vuetify = createVuetify({ components, directives })

let pinia

beforeEach(() => {
  pinia = createPinia()
  setActivePinia(pinia)
  vi.clearAllMocks()
})

async function mountBrowser() {
  // Spy on fetchRecipes to prevent actual API calls on mount
  const { useRecipeStore } = await import('@/stores/recipes')
  const store = useRecipeStore()
  vi.spyOn(store, 'fetchRecipes').mockResolvedValue(undefined)

  const wrapper = mount(RecipeBrowser, {
    global: {
      plugins: [vuetify, pinia],
      stubs: { teleport: true, RecentlyCookedDialog: true },
    },
  })
  await flushPromises()
  return { wrapper, store }
}

describe('RecipeBrowser', () => {
  it('shows "No recipes found" when store is empty', async () => {
    const { wrapper } = await mountBrowser()
    expect(wrapper.text()).toContain('No recipes found')
  })

  it('renders a list item for each recipe', async () => {
    const { wrapper, store } = await mountBrowser()
    store.recipes = [makeRecipe({ id: 'r1', name: 'Soup' }), makeRecipe({ id: 'r2', name: 'Pasta' })]
    await wrapper.vm.$nextTick()

    expect(wrapper.text()).toContain('Soup')
    expect(wrapper.text()).toContain('Pasta')
  })

  it('calls fetchRecipes with search param on input change', async () => {
    const { wrapper, store } = await mountBrowser()

    const input = wrapper.find('input')
    await input.setValue('pasta')
    await input.trigger('update:modelValue')

    expect(store.fetchRecipes).toHaveBeenCalledWith(expect.objectContaining({ search: 'pasta' }))
  })

  it('sends no sort params, keeping the default order even when the Recipes tab has a stored sort', async () => {
    window.localStorage.setItem(RECIPE_LIST_SORT_STORAGE_KEY, JSON.stringify({ sort: 'NAME', direction: 'ASC' }))
    try {
      const { wrapper, store } = await mountBrowser()
      const input = wrapper.find('input')
      await input.setValue('pasta')
      await input.trigger('update:modelValue')

      expect(store.fetchRecipes).toHaveBeenCalled()
      for (const [params] of store.fetchRecipes.mock.calls) {
        expect(params).not.toHaveProperty('sort')
        expect(params).not.toHaveProperty('direction')
      }
    } finally {
      window.localStorage.clear()
    }
  })

  it('emits add for a recipe not recently cooked', async () => {
    const { wrapper, store } = await mountBrowser()
    store.recipes = [makeRecipe({ id: 'r1', name: 'Soup', lastCookedAt: null })]
    await wrapper.vm.$nextTick()

    const item = wrapper.find('.v-list-item')
    await item.trigger('click')

    expect(wrapper.emitted('add')).toBeTruthy()
  })
})
