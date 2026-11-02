import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import RecipesView from './RecipesView.vue'
import { makeRecipe, makeRecipePage } from '@/test/factories'
import { InfiniteScrollStub } from '@/test/infiniteScrollStub'
import { RECIPE_LIST_SORT_STORAGE_KEY } from '@/constants/recipeSorts'

vi.mock('@/services/api', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
  assetUrl: vi.fn((p) => p ?? ''),
}))

import api from '@/services/api'

const vuetify = createVuetify({ components, directives })

const SEARCH_PLACEHOLDER = 'Search recipes or ingredients…'

let pinia

beforeEach(() => {
  pinia = createPinia()
  setActivePinia(pinia)
  window.localStorage.clear()
  vi.clearAllMocks()
  api.get.mockResolvedValue({ data: makeRecipePage([]) })
})

afterEach(() => {
  vi.useRealTimers()
})

const recipes = (...ids) => ids.map(id => makeRecipe({ id, name: `Recipe ${id}` }))
const pageResponse = (items, totalCount = items.length) => ({ data: makeRecipePage(items, { totalCount }) })
const lastFetchParams = () => api.get.mock.lastCall[1].params

function deferred() {
  let resolve, reject
  const promise = new Promise((res, rej) => { resolve = res; reject = rej })
  return { promise, resolve, reject }
}

async function mountView() {
  const wrapper = mount(RecipesView, {
    global: {
      plugins: [vuetify, pinia],
      stubs: {
        RouterLink: { template: '<a><slot/></a>' },
        'router-link': { template: '<a><slot/></a>' },
        teleport: true,
        VInfiniteScroll: InfiniteScrollStub,
      },
    },
  })

  await flushPromises()
  return wrapper
}

const recipeCards = (wrapper) => wrapper.findAllComponents({ name: 'VCard' })
const infiniteScroll = (wrapper) => wrapper.findComponent(InfiniteScrollStub)

async function scrollToEnd(wrapper) {
  const scrolled = infiniteScroll(wrapper).vm.scrollToEnd()
  await flushPromises()
  await scrolled
}

describe('RecipesView', () => {
  it('shows loading skeletons while page 1 loads', async () => {
    api.get.mockReturnValue(new Promise(() => {}))
    const wrapper = await mountView()

    expect(wrapper.find('.v-skeleton-loader').exists()).toBe(true)
    expect(infiniteScroll(wrapper).exists()).toBe(false)
  })

  it('renders a card for each recipe on page 1', async () => {
    api.get.mockResolvedValue(pageResponse(recipes('1', '2')))
    const wrapper = await mountView()

    expect(recipeCards(wrapper)).toHaveLength(2)
  })

  it('shows empty-state message when the library has no recipes', async () => {
    const wrapper = await mountView()

    expect(wrapper.text()).toContain('No recipes yet')
    expect(infiniteScroll(wrapper).exists()).toBe(false)
  })

  it('shows the error state when page 1 fails', async () => {
    api.get.mockRejectedValue({ message: 'Network error' })
    const wrapper = await mountView()

    expect(wrapper.findComponent({ name: 'ErrorState' }).exists()).toBe(true)
    expect(infiniteScroll(wrapper).exists()).toBe(false)
  })

  it('recipe card has :to prop navigating to recipe-detail', async () => {
    api.get.mockResolvedValue(pageResponse(recipes('r1')))
    const wrapper = await mountView()

    expect(recipeCards(wrapper)[0].props('to')).toEqual({ name: 'recipe-detail', params: { id: 'r1' } })
  })

  it('offers a New recipe button beside the search field instead of a floating button', async () => {
    const wrapper = await mountView()

    const newRecipeButton = wrapper
      .findAll('button')
      .find((button) => button.attributes('aria-label') === 'New recipe')
    expect(newRecipeButton).toBeDefined()

    const searchField = wrapper.findComponent({ name: 'VTextField' })
    expect(searchField.element.parentElement.contains(newRecipeButton.element)).toBe(true)

    expect(wrapper.findComponent({ name: 'VSpeedDial' }).exists()).toBe(false)
    expect(wrapper.findComponent({ name: 'VFab' }).exists()).toBe(false)
  })
})

describe('RecipesView paging', () => {
  it('appends the next page when the list scrolls to its end', async () => {
    api.get
      .mockResolvedValueOnce(pageResponse(recipes('1', '2'), 4))
      .mockResolvedValueOnce(pageResponse(recipes('3', '4'), 4))
    const wrapper = await mountView()

    await scrollToEnd(wrapper)

    expect(lastFetchParams()).toEqual({ sort: 'DATE_ADDED', direction: 'DESC', page: 2 })
    expect(recipeCards(wrapper)).toHaveLength(4)
  })

  it('shows a spinner at the end of the list while a later page loads', async () => {
    const pageTwo = deferred()
    api.get
      .mockResolvedValueOnce(pageResponse(recipes('1', '2'), 4))
      .mockReturnValueOnce(pageTwo.promise)
    const wrapper = await mountView()

    infiniteScroll(wrapper).vm.scrollToEnd()
    await flushPromises()

    expect(wrapper.find('.v-infinite-scroll__side .v-progress-circular').exists()).toBe(true)
    expect(wrapper.find('.v-skeleton-loader').exists()).toBe(false)
  })

  it('starts again from page 1 when the search changes', async () => {
    api.get
      .mockResolvedValueOnce(pageResponse(recipes('1', '2'), 4))
      .mockResolvedValueOnce(pageResponse(recipes('3', '4'), 4))
      .mockResolvedValueOnce(pageResponse(recipes('soup'), 1))
    const wrapper = await mountView()
    await scrollToEnd(wrapper)
    vi.useFakeTimers()

    await wrapper.find(`input[placeholder="${SEARCH_PLACEHOLDER}"]`).setValue('soup')
    vi.runAllTimers()
    await flushPromises()

    expect(lastFetchParams()).toEqual({ search: 'soup', sort: 'DATE_ADDED', direction: 'DESC', page: 1 })
    expect(recipeCards(wrapper)).toHaveLength(1)
  })

  it('says how many recipes there are once a second page completes the list', async () => {
    // An empty page ends the list, so the total need not be loaded to show the thousands separator.
    api.get
      .mockResolvedValueOnce(pageResponse(recipes('1', '2'), 1135))
      .mockResolvedValueOnce(pageResponse([], 1135))
    const wrapper = await mountView()

    await scrollToEnd(wrapper)

    expect(wrapper.text()).toContain('That’s all 1,135 recipes')
  })

  it('shows no end caption when page 1 holds every recipe', async () => {
    api.get.mockResolvedValueOnce(pageResponse(recipes('1', '2')))
    const wrapper = await mountView()

    await scrollToEnd(wrapper)

    expect(api.get).toHaveBeenCalledTimes(1)
    expect(wrapper.text()).not.toContain('That’s all')
  })

  it('keeps the loaded recipes and offers a retry when a later page fails', async () => {
    api.get
      .mockResolvedValueOnce(pageResponse(recipes('1', '2'), 4))
      .mockRejectedValueOnce({ message: 'Network error' })
      .mockResolvedValueOnce(pageResponse(recipes('3', '4'), 4))
    const wrapper = await mountView()

    await scrollToEnd(wrapper)

    expect(wrapper.text()).toContain('Couldn’t load more recipes')
    expect(recipeCards(wrapper)).toHaveLength(2)
    expect(wrapper.findComponent({ name: 'ErrorState' }).exists()).toBe(false)

    const retryButton = wrapper.findAll('button').find(button => button.text() === 'Retry')
    await retryButton.trigger('click')
    await flushPromises()

    expect(api.get.mock.lastCall[1].params.page).toBe(2)
    expect(recipeCards(wrapper)).toHaveLength(4)
    expect(wrapper.text()).not.toContain('Couldn’t load more recipes')
  })
})

describe('RecipesView sorting', () => {
  const sortRow = (wrapper) => wrapper.find('[aria-label="Sort recipes"]')
  const sortField = (wrapper) => wrapper.findComponent({ name: 'VSelect' })
  const directionButton = (wrapper) => sortRow(wrapper).find('button')

  async function mountWithRecipes() {
    api.get.mockResolvedValue(pageResponse(recipes('r1')))
    return mountView()
  }

  it('sends newest added first on mount when nothing is stored', async () => {
    await mountView()

    expect(api.get).toHaveBeenCalledTimes(1)
    expect(lastFetchParams()).toEqual({ sort: 'DATE_ADDED', direction: 'DESC', page: 1 })
  })

  it('restores the stored choice on mount', async () => {
    window.localStorage.setItem(RECIPE_LIST_SORT_STORAGE_KEY, JSON.stringify({ sort: 'NAME', direction: 'ASC' }))

    const wrapper = await mountWithRecipes()

    expect(lastFetchParams()).toEqual({ sort: 'NAME', direction: 'ASC', page: 1 })
    expect(sortField(wrapper).props('modelValue')).toBe('NAME')
    expect(directionButton(wrapper).attributes('aria-label')).toBe('A to Z')
  })

  it.each([
    ['an unknown field', JSON.stringify({ sort: 'RATING', direction: 'ASC' })],
    ['text that is not JSON', '{not json'],
  ])('falls back to the default when the stored value is %s', async (_description, storedText) => {
    window.localStorage.setItem(RECIPE_LIST_SORT_STORAGE_KEY, storedText)

    await mountView()

    expect(lastFetchParams()).toEqual({ sort: 'DATE_ADDED', direction: 'DESC', page: 1 })
  })

  it('refetches page 1 straight away when the field changes, keeping the direction, and saves the choice', async () => {
    const wrapper = await mountWithRecipes()

    await sortField(wrapper).vm.$emit('update:modelValue', 'NAME')

    expect(lastFetchParams()).toEqual({ sort: 'NAME', direction: 'DESC', page: 1 })
    expect(JSON.parse(window.localStorage.getItem(RECIPE_LIST_SORT_STORAGE_KEY)))
      .toEqual({ sort: 'NAME', direction: 'DESC' })
  })

  it('flips the direction from the toggle, relabelling it for the field, and saves the choice', async () => {
    const wrapper = await mountWithRecipes()
    expect(directionButton(wrapper).attributes('aria-label')).toBe('Newest first')

    await directionButton(wrapper).trigger('click')
    await flushPromises()

    expect(lastFetchParams()).toEqual({ sort: 'DATE_ADDED', direction: 'ASC', page: 1 })
    expect(directionButton(wrapper).attributes('aria-label')).toBe('Oldest first')
    expect(sortRow(wrapper).findComponent({ name: 'VTooltip' }).props('text')).toBe('Oldest first')
    expect(JSON.parse(window.localStorage.getItem(RECIPE_LIST_SORT_STORAGE_KEY)))
      .toEqual({ sort: 'DATE_ADDED', direction: 'ASC' })
  })

  it('keeps the sort row while the list reloads for a new sort', async () => {
    const wrapper = await mountWithRecipes()
    api.get.mockReturnValue(new Promise(() => {}))

    await sortField(wrapper).vm.$emit('update:modelValue', 'NAME')

    expect(wrapper.find('.v-skeleton-loader').exists()).toBe(true)
    expect(sortRow(wrapper).exists()).toBe(true)
  })

  it('keeps the sort when searching and the search text when sorting', async () => {
    const wrapper = await mountWithRecipes()
    await sortField(wrapper).vm.$emit('update:modelValue', 'LAST_COOKED')
    await flushPromises()
    vi.useFakeTimers()

    await wrapper.find(`input[placeholder="${SEARCH_PLACEHOLDER}"]`).setValue('soup')
    vi.runAllTimers()
    await flushPromises()

    expect(lastFetchParams()).toEqual({ search: 'soup', sort: 'LAST_COOKED', direction: 'DESC', page: 1 })

    await directionButton(wrapper).trigger('click')

    expect(lastFetchParams()).toEqual({ search: 'soup', sort: 'LAST_COOKED', direction: 'ASC', page: 1 })
  })

  it('hides the sort row when the library has no recipes', async () => {
    const wrapper = await mountView()

    expect(wrapper.text()).toContain('No recipes yet')
    expect(sortRow(wrapper).exists()).toBe(false)
  })

  it('keeps the sort row hidden while a recipe-less list reloads', async () => {
    const wrapper = await mountView()
    api.get.mockReturnValue(new Promise(() => {}))
    vi.useFakeTimers()

    await wrapper.find(`input[placeholder="${SEARCH_PLACEHOLDER}"]`).setValue('soup')
    vi.runAllTimers()
    await wrapper.vm.$nextTick()

    expect(wrapper.find('.v-skeleton-loader').exists()).toBe(true)
    expect(sortRow(wrapper).exists()).toBe(false)
  })

  it('keeps the sort row for a search with no results', async () => {
    const wrapper = await mountView()
    vi.useFakeTimers()

    await wrapper.find(`input[placeholder="${SEARCH_PLACEHOLDER}"]`).setValue('zzz')
    vi.runAllTimers()
    await flushPromises()

    expect(wrapper.text()).toContain('No recipes match your search')
    expect(sortRow(wrapper).exists()).toBe(true)
  })

  it('keeps the sort row while a cleared search with no results waits to refetch', async () => {
    const wrapper = await mountView()
    vi.useFakeTimers()
    const searchInput = wrapper.find(`input[placeholder="${SEARCH_PLACEHOLDER}"]`)
    await searchInput.setValue('zzz')
    vi.runAllTimers()
    await flushPromises()

    await searchInput.setValue('')

    expect(sortRow(wrapper).exists()).toBe(true)
  })
})
