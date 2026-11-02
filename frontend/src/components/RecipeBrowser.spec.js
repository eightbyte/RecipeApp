import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import RecipeBrowser from './RecipeBrowser.vue'
import { makeRecipe, makeRecipePage } from '@/test/factories'
import { InfiniteScrollStub } from '@/test/infiniteScrollStub'
import { SEARCH_DEBOUNCE_MS } from '@/composables/useRecipePages'
import { RECIPE_LIST_SORT_STORAGE_KEY } from '@/constants/recipeSorts'

vi.mock('@/services/api', () => ({
  default: { get: vi.fn(), post: vi.fn() },
  assetUrl: vi.fn((p) => p ?? ''),
}))

import api from '@/services/api'

const vuetify = createVuetify({ components, directives })

let pinia

beforeEach(() => {
  pinia = createPinia()
  setActivePinia(pinia)
  vi.clearAllMocks()
  api.get.mockResolvedValue({ data: makeRecipePage([]) })
})

afterEach(() => {
  vi.useRealTimers()
  window.localStorage.clear()
})

const pageResponse = (items, totalCount = items.length) => ({ data: makeRecipePage(items, { totalCount }) })
const lastFetchParams = () => api.get.mock.lastCall[1].params

async function mountBrowser() {
  const wrapper = mount(RecipeBrowser, {
    global: {
      plugins: [vuetify, pinia],
      stubs: { teleport: true, RecentlyCookedDialog: true, VInfiniteScroll: InfiniteScrollStub },
    },
  })
  await flushPromises()
  return wrapper
}

describe('RecipeBrowser', () => {
  it('shows a spinner while page 1 loads', async () => {
    api.get.mockReturnValue(new Promise(() => {}))
    const wrapper = await mountBrowser()

    expect(wrapper.find('.v-progress-circular').exists()).toBe(true)
    expect(wrapper.findComponent(InfiniteScrollStub).exists()).toBe(false)
  })

  it('shows "No recipes found" when page 1 is empty', async () => {
    const wrapper = await mountBrowser()

    expect(wrapper.text()).toContain('No recipes found')
    expect(wrapper.findComponent(InfiniteScrollStub).exists()).toBe(false)
  })

  it('still shows "No recipes found" when page 1 fails', async () => {
    api.get.mockRejectedValue({ message: 'Network error' })
    const wrapper = await mountBrowser()

    expect(wrapper.text()).toContain('No recipes found')
  })

  it('renders a list item for each recipe', async () => {
    api.get.mockResolvedValue(pageResponse([makeRecipe({ id: 'r1', name: 'Soup' }), makeRecipe({ id: 'r2', name: 'Pasta' })]))
    const wrapper = await mountBrowser()

    expect(wrapper.text()).toContain('Soup')
    expect(wrapper.text()).toContain('Pasta')
  })

  it('appends the next page when the list scrolls to its end', async () => {
    api.get
      .mockResolvedValueOnce(pageResponse([makeRecipe({ id: 'r1', name: 'Soup' })], 2))
      .mockResolvedValueOnce(pageResponse([makeRecipe({ id: 'r2', name: 'Pasta' })], 2))
    const wrapper = await mountBrowser()

    const scrolled = wrapper.findComponent(InfiniteScrollStub).vm.scrollToEnd()
    await flushPromises()
    await scrolled

    expect(lastFetchParams()).toEqual({ page: 2 })
    expect(wrapper.text()).toContain('Pasta')
  })

  it('searches page 1 once typing pauses for the shared debounce', async () => {
    const wrapper = await mountBrowser()
    vi.useFakeTimers()

    await wrapper.find('input[type="text"]').setValue('pasta')
    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS - 1)
    expect(api.get).toHaveBeenCalledTimes(1)

    vi.advanceTimersByTime(1)
    expect(api.get).toHaveBeenCalledTimes(2)
    expect(lastFetchParams()).toEqual({ search: 'pasta', page: 1 })
  })

  it('applies "Hide recently cooked" straight away, from page 1', async () => {
    const wrapper = await mountBrowser()

    await wrapper.find('input[type="checkbox"]').setValue(true)

    expect(lastFetchParams()).toEqual({ excludeRecentDays: 7, page: 1 })
  })

  it('sends no sort params, keeping the default order even when the Recipes tab has a stored sort', async () => {
    window.localStorage.setItem(RECIPE_LIST_SORT_STORAGE_KEY, JSON.stringify({ sort: 'NAME', direction: 'ASC' }))
    api.get.mockResolvedValue(pageResponse([makeRecipe({ id: 'r1' })], 2))
    const wrapper = await mountBrowser()
    vi.useFakeTimers()
    await wrapper.find('input[type="text"]').setValue('pasta')
    vi.runAllTimers()
    await flushPromises()
    vi.useRealTimers()

    const scrolled = wrapper.findComponent(InfiniteScrollStub).vm.scrollToEnd()
    await flushPromises()
    await scrolled

    expect(api.get).toHaveBeenCalledTimes(3)
    for (const [, { params }] of api.get.mock.calls) {
      expect(params).not.toHaveProperty('sort')
      expect(params).not.toHaveProperty('direction')
    }
  })

  it('emits add for a recipe not recently cooked', async () => {
    api.get.mockResolvedValue(pageResponse([makeRecipe({ id: 'r1', name: 'Soup', lastCookedAt: null })]))
    const wrapper = await mountBrowser()

    await wrapper.find('.v-list-item').trigger('click')

    expect(wrapper.emitted('add')).toBeTruthy()
  })
})
