import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import IngredientsView from './IngredientsView.vue'
import { makeIngredient } from '@/test/factories'

vi.mock('@/services/api', () => ({
  default: { get: vi.fn() },
  assetUrl: vi.fn((path) => path ?? ''),
}))

import api from '@/services/api'

const vuetify = createVuetify({ components, directives })
const Blank = { template: '<div/>' }

const onion   = makeIngredient({ id: 'i1', name: 'onion', displayName: 'Onion', category: 'PRODUCE', aliases: ['brown onion'] })
const flour   = makeIngredient({ id: 'i2', name: 'flour', displayName: 'Flour', category: 'BAKING_SPICES' })
const removed = makeIngredient({
  id: 'i3', name: 'shallot', displayName: 'Shallot', category: 'PRODUCE', removedAt: '2026-11-01T00:00:00Z',
})

beforeEach(() => {
  setActivePinia(createPinia())
  vi.clearAllMocks()
  api.get.mockResolvedValue({ data: [onion, flour, removed] })
})

async function mountView() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/admin/ingredients', name: 'admin-ingredients', component: IngredientsView },
      { path: '/admin/ingredients/new', name: 'admin-ingredient-new', component: Blank },
      { path: '/admin/ingredients/:id', name: 'admin-ingredient-edit', component: Blank },
    ],
  })
  await router.push('/admin/ingredients')
  const wrapper = mount(IngredientsView, { global: { plugins: [vuetify, router] } })
  await flushPromises()
  return wrapper
}

const rows = (wrapper) => wrapper.findAll('[data-test="ingredient-row"]')
const rowTitles = (wrapper) => rows(wrapper).map((row) => row.find('.v-list-item-title').text())

async function search(wrapper, text) {
  await wrapper.find('[data-test="ingredient-search"] input').setValue(text)
  await flushPromises()
}

describe('IngredientsView', () => {
  it('lists the active Ingredients by display name, with their Category', async () => {
    const wrapper = await mountView()

    expect(api.get).toHaveBeenCalledWith('/ingredients', { params: { includeRemoved: true } })
    expect(rowTitles(wrapper)).toEqual(['Flour', 'Onion'])
    expect(rows(wrapper)[1].text()).toContain('Produce')
  })

  it('shows Removed Ingredients, marked as removed, only while Show removed is on', async () => {
    const wrapper = await mountView()

    await wrapper.findComponent({ name: 'VSwitch' }).find('input').setValue(true)

    expect(rowTitles(wrapper)).toEqual(['Flour', 'Onion', 'Shallot'])
    expect(rows(wrapper)[2].text()).toContain('Removed')
    expect(rows(wrapper)[1].text()).not.toContain('Removed')
  })

  it('searches display names and aliases, ignoring case', async () => {
    const wrapper = await mountView()

    await search(wrapper, 'FLO')
    expect(rowTitles(wrapper)).toEqual(['Flour'])

    await search(wrapper, 'brown')
    expect(rowTitles(wrapper)).toEqual(['Onion'])
  })

  it('filters by Category', async () => {
    const wrapper = await mountView()

    wrapper.findComponent('[data-test="category-filter"]').vm.$emit('update:modelValue', 'PRODUCE')
    await flushPromises()

    expect(rowTitles(wrapper)).toEqual(['Onion'])
  })

  it('opens an Ingredient\'s edit form from its row, and the add form from Add Ingredient', async () => {
    const wrapper = await mountView()

    const onionRow = wrapper.findAllComponents({ name: 'VListItem' }).find((item) => item.text().includes('Onion'))
    expect(onionRow.props('to')).toEqual({ name: 'admin-ingredient-edit', params: { id: 'i1' } })

    const add = wrapper.findAllComponents({ name: 'VBtn' }).find((button) => button.text() === 'Add Ingredient')
    expect(add.props('to')).toEqual({ name: 'admin-ingredient-new' })
  })

  it('shows 50 rows at a time, with Show more for the rest', async () => {
    api.get.mockResolvedValue({
      data: Array.from({ length: 60 }, (_, n) =>
        makeIngredient({ id: `i${n}`, displayName: `Ingredient ${String(n).padStart(2, '0')}` })),
    })
    const wrapper = await mountView()
    expect(rows(wrapper)).toHaveLength(50)

    await wrapper.findAllComponents({ name: 'VBtn' }).find((button) => button.text() === 'Show more').trigger('click')

    expect(rows(wrapper)).toHaveLength(60)
  })

  it('says so when nothing matches', async () => {
    const wrapper = await mountView()

    await search(wrapper, 'dragon fruit')

    expect(wrapper.text()).toContain('No ingredients match')
  })

  it('shows the error state when the list fails to load, and retries', async () => {
    api.get.mockRejectedValueOnce({ message: 'offline' })
    const wrapper = await mountView()
    expect(wrapper.text()).toContain('offline')

    await wrapper.findAllComponents({ name: 'VBtn' }).find((button) => button.text().includes('Try again')).trigger('click')
    await flushPromises()

    expect(rowTitles(wrapper)).toEqual(['Flour', 'Onion'])
  })
})
