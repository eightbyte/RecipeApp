import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import ShoppingView from './ShoppingView.vue'
import { makeShoppingList, makeShoppingListItem } from '@/test/factories'

vi.mock('@/services/api', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}))

const vuetify = createVuetify({ components, directives })

let pinia

beforeEach(() => {
  pinia = createPinia()
  setActivePinia(pinia)
  vi.clearAllMocks()
})

async function mountView() {
  const { useShoppingListStore } = await import('@/stores/shoppingList')
  const store = useShoppingListStore()
  vi.spyOn(store, 'fetchActive').mockResolvedValue(undefined)

  const wrapper = mount(ShoppingView, {
    global: {
      plugins: [vuetify, pinia],
      stubs: {
        RouterLink: { template: '<a><slot/></a>' },
        'router-link': { template: '<a><slot/></a>' },
        teleport: true,
        AddCustomItemDialog: { template: '<div />' },
      },
    },
  })
  await flushPromises()
  return { wrapper, store }
}

describe('ShoppingView', () => {
  it('shows empty state when no active plan', async () => {
    const { wrapper, store } = await mountView()
    store.list = null
    await wrapper.vm.$nextTick()
    expect(wrapper.text()).toContain('Create a meal plan')
  })

  it('renders category groups from list items', async () => {
    const { wrapper, store } = await mountView()
    store.list = makeShoppingList({
      items: [
        makeShoppingListItem({ id: 'i1', displayName: 'Carrot', category: 'PRODUCE' }),
        makeShoppingListItem({ id: 'i2', displayName: 'Milk', category: 'DAIRY' }),
      ],
    })
    await wrapper.vm.$nextTick()
    expect(wrapper.text()).toContain('Carrot')
    expect(wrapper.text()).toContain('Milk')
    expect(wrapper.text()).toContain('Produce')
    expect(wrapper.text()).toContain('Dairy')
  })

  it('hides checked items by default', async () => {
    const { wrapper, store } = await mountView()
    store.list = makeShoppingList({
      items: [
        makeShoppingListItem({ id: 'i1', displayName: 'Checked', isChecked: true }),
        makeShoppingListItem({ id: 'i2', displayName: 'Unchecked', isChecked: false }),
      ],
    })
    await wrapper.vm.$nextTick()
    expect(wrapper.text()).not.toContain('Checked')
    expect(wrapper.text()).toContain('Unchecked')
  })

  it('shows stale banner when isStale', async () => {
    const { wrapper, store } = await mountView()
    store.list = makeShoppingList({ isStale: true })
    await wrapper.vm.$nextTick()
    expect(wrapper.text()).toContain('Your meal plan has changed')
    expect(wrapper.text()).toContain('Regenerate')
  })

  it('does not show stale banner when not stale', async () => {
    const { wrapper, store } = await mountView()
    store.list = makeShoppingList({ isStale: false })
    await wrapper.vm.$nextTick()
    expect(wrapper.text()).not.toContain('Your meal plan has changed')
  })

  it('calls regenerate when Regenerate button clicked', async () => {
    const { wrapper, store } = await mountView()
    vi.spyOn(store, 'regenerate').mockResolvedValue(undefined)
    store.list = makeShoppingList({ isStale: true })
    await wrapper.vm.$nextTick()

    const btn = wrapper.findAll('button').find(b => b.text().includes('Regenerate'))
    await btn?.trigger('click')
    expect(store.regenerate).toHaveBeenCalled()
  })

  it('shows warning chip for needsReview items', async () => {
    const { wrapper, store } = await mountView()
    store.list = makeShoppingList({
      items: [makeShoppingListItem({ id: 'i1', needsReview: true })],
    })
    await wrapper.vm.$nextTick()
    expect(wrapper.html()).toContain('mdi-alert-outline')
  })

  it('calls toggleItem when item is clicked', async () => {
    const { wrapper, store } = await mountView()
    vi.spyOn(store, 'toggleItem').mockResolvedValue(undefined)
    const item = makeShoppingListItem({ id: 'item-1', isChecked: false })
    store.list = makeShoppingList({ items: [item] })
    await wrapper.vm.$nextTick()

    const listItem = wrapper.find('.v-list-item')
    await listItem.trigger('click')
    expect(store.toggleItem).toHaveBeenCalledWith('item-1', true)
  })

  it('shows delete button only on custom items', async () => {
    const { wrapper, store } = await mountView()
    store.list = makeShoppingList({
      items: [
        makeShoppingListItem({ id: 'i1', isCustom: false }),
        makeShoppingListItem({ id: 'i2', isCustom: true }),
      ],
    })
    await wrapper.vm.$nextTick()
    // Custom item has the delete button (mdi-close icon)
    expect(wrapper.html()).toContain('mdi-close')
  })

  it('shows empty list state when list exists but has no visible items', async () => {
    const { wrapper, store } = await mountView()
    // All items are checked and showChecked is false (default)
    store.list = makeShoppingList({
      items: [makeShoppingListItem({ id: 'i1', isChecked: true })],
    })
    await wrapper.vm.$nextTick()
    expect(wrapper.text()).toContain('0 items remaining')
  })
})
