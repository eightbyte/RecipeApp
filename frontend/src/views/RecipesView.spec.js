import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import { useRecipeStore } from '@/stores/recipes'
import RecipesView from './RecipesView.vue'
import { makeRecipe } from '@/test/factories'
import { RECIPE_LIST_SORT_STORAGE_KEY } from '@/constants/recipeSorts'

vi.mock('@/services/api', () => ({
  default: { get: vi.fn().mockResolvedValue({ data: [] }), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
  assetUrl: vi.fn((p) => p ?? ''),
}))

const vuetify = createVuetify({ components, directives })

let pinia

beforeEach(() => {
  pinia = createPinia()
  setActivePinia(pinia)
  window.localStorage.clear()
})

afterEach(() => {
  vi.useRealTimers()
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

  it('offers a New recipe button beside the search field instead of a floating button', async () => {
    const { wrapper } = await mountView()

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

describe('RecipesView sorting', () => {
  const sortRow = (wrapper) => wrapper.find('[aria-label="Sort recipes"]')
  const sortField = (wrapper) => wrapper.findComponent({ name: 'VSelect' })
  const directionButton = (wrapper) => sortRow(wrapper).find('button')
  const lastFetchParams = (store) => store.fetchRecipes.mock.lastCall[0]

  async function mountWithRecipes() {
    const mounted = await mountView()
    mounted.store.recipes = [makeRecipe({ id: 'r1' })]
    await mounted.wrapper.vm.$nextTick()
    return mounted
  }

  it('sends newest added first on mount when nothing is stored', async () => {
    const { store } = await mountView()

    expect(store.fetchRecipes).toHaveBeenCalledTimes(1)
    expect(lastFetchParams(store)).toEqual({ sort: 'DATE_ADDED', direction: 'DESC' })
  })

  it('restores the stored choice on mount', async () => {
    window.localStorage.setItem(RECIPE_LIST_SORT_STORAGE_KEY, JSON.stringify({ sort: 'NAME', direction: 'ASC' }))

    const { wrapper, store } = await mountWithRecipes()

    expect(lastFetchParams(store)).toEqual({ sort: 'NAME', direction: 'ASC' })
    expect(sortField(wrapper).props('modelValue')).toBe('NAME')
    expect(directionButton(wrapper).attributes('aria-label')).toBe('A to Z')
  })

  it.each([
    ['an unknown field', JSON.stringify({ sort: 'RATING', direction: 'ASC' })],
    ['text that is not JSON', '{not json'],
  ])('falls back to the default when the stored value is %s', async (_description, storedText) => {
    window.localStorage.setItem(RECIPE_LIST_SORT_STORAGE_KEY, storedText)

    const { store } = await mountView()

    expect(lastFetchParams(store)).toEqual({ sort: 'DATE_ADDED', direction: 'DESC' })
  })

  it('refetches straight away when the field changes, keeping the direction, and saves the choice', async () => {
    const { wrapper, store } = await mountWithRecipes()

    await sortField(wrapper).vm.$emit('update:modelValue', 'NAME')

    expect(lastFetchParams(store)).toEqual({ sort: 'NAME', direction: 'DESC' })
    expect(JSON.parse(window.localStorage.getItem(RECIPE_LIST_SORT_STORAGE_KEY)))
      .toEqual({ sort: 'NAME', direction: 'DESC' })
  })

  it('flips the direction from the toggle, relabelling it for the field, and saves the choice', async () => {
    const { wrapper, store } = await mountWithRecipes()
    expect(directionButton(wrapper).attributes('aria-label')).toBe('Newest first')

    await directionButton(wrapper).trigger('click')

    expect(lastFetchParams(store)).toEqual({ sort: 'DATE_ADDED', direction: 'ASC' })
    expect(directionButton(wrapper).attributes('aria-label')).toBe('Oldest first')
    expect(sortRow(wrapper).findComponent({ name: 'VTooltip' }).props('text')).toBe('Oldest first')
    expect(JSON.parse(window.localStorage.getItem(RECIPE_LIST_SORT_STORAGE_KEY)))
      .toEqual({ sort: 'DATE_ADDED', direction: 'ASC' })
  })

  it('keeps the sort when searching and the search text when sorting', async () => {
    const { wrapper, store } = await mountWithRecipes()
    await sortField(wrapper).vm.$emit('update:modelValue', 'LAST_COOKED')
    vi.useFakeTimers()

    await wrapper.find('input[placeholder="Search recipes or ingredients…"]').setValue('soup')
    vi.runAllTimers()

    expect(lastFetchParams(store)).toEqual({ search: 'soup', sort: 'LAST_COOKED', direction: 'DESC' })

    await directionButton(wrapper).trigger('click')

    expect(lastFetchParams(store)).toEqual({ search: 'soup', sort: 'LAST_COOKED', direction: 'ASC' })
  })

  it('hides the sort row when the library has no recipes', async () => {
    const { wrapper, store } = await mountView()
    store.recipes = []
    await wrapper.vm.$nextTick()

    expect(wrapper.text()).toContain('No recipes yet')
    expect(sortRow(wrapper).exists()).toBe(false)
  })

  it('keeps the sort row hidden while a recipe-less list reloads', async () => {
    const { wrapper, store } = await mountView()
    store.recipes = []
    store.loading = true
    await wrapper.vm.$nextTick()

    expect(sortRow(wrapper).exists()).toBe(false)
  })

  it('keeps the sort row for a search with no results', async () => {
    const { wrapper, store } = await mountView()
    vi.useFakeTimers()

    await wrapper.find('input[placeholder="Search recipes or ingredients…"]').setValue('zzz')
    store.recipes = []
    vi.runAllTimers()
    await flushPromises()

    expect(wrapper.text()).toContain('No recipes match your search')
    expect(sortRow(wrapper).exists()).toBe(true)
  })

  it('keeps the sort row while a cleared search with no results waits to refetch', async () => {
    const { wrapper, store } = await mountView()
    vi.useFakeTimers()
    const searchInput = wrapper.find('input[placeholder="Search recipes or ingredients…"]')
    await searchInput.setValue('zzz')
    store.recipes = []
    vi.runAllTimers()
    await flushPromises()

    await searchInput.setValue('')

    expect(sortRow(wrapper).exists()).toBe(true)
  })
})
