import { setActivePinia, createPinia } from 'pinia'
import { useShoppingListStore } from './shoppingList'
import { makeShoppingList, makeShoppingListItem } from '@/test/factories'

vi.mock('@/services/api', () => ({
  default: {
    get:    vi.fn(),
    post:   vi.fn(),
    put:    vi.fn(),
    delete: vi.fn(),
  },
}))

import api from '@/services/api'

beforeEach(() => {
  setActivePinia(createPinia())
  vi.clearAllMocks()
})

// ── fetchActive ───────────────────────────────────────────────────────────────

describe('fetchActive', () => {
  it('stores the list on success', async () => {
    const list = makeShoppingList()
    api.get.mockResolvedValue({ data: list })

    const store = useShoppingListStore()
    await store.fetchActive()

    expect(store.list).toMatchObject({ id: 'list-1' })
    expect(store.error).toBeNull()
  })

  it('sets list to null on 404 without setting error', async () => {
    api.get.mockRejectedValue({ status: 404, message: 'Not Found' })

    const store = useShoppingListStore()
    await store.fetchActive()

    expect(store.list).toBeNull()
    expect(store.error).toBeNull()
  })

  it('sets error on non-404 failure', async () => {
    api.get.mockRejectedValue({ status: 500, message: 'Server error' })

    const store = useShoppingListStore()
    await store.fetchActive()

    expect(store.error).toBe('Server error')
  })
})

// ── regenerate ────────────────────────────────────────────────────────────────

describe('regenerate', () => {
  it('calls POST and replaces list', async () => {
    const list = makeShoppingList({ id: 'list-regenerated', isStale: false })
    api.post.mockResolvedValue({ data: list })

    const store = useShoppingListStore()
    store.list = makeShoppingList({ isStale: true })
    await store.regenerate()

    expect(api.post).toHaveBeenCalledWith('/shopping-lists/active/generate')
    expect(store.list.id).toBe('list-regenerated')
    expect(store.list.isStale).toBe(false)
  })
})

// ── toggleItem ────────────────────────────────────────────────────────────────

describe('toggleItem', () => {
  it('optimistically flips isChecked', async () => {
    const item = makeShoppingListItem({ id: 'item-1', isChecked: false })
    const list = makeShoppingList({ items: [item] })
    api.put.mockResolvedValue({ data: { ...item, isChecked: true } })

    const store = useShoppingListStore()
    store.list = list

    const promise = store.toggleItem('item-1', true)
    expect(store.list.items[0].isChecked).toBe(true)
    await promise
  })

  it('reverts the flip on API error', async () => {
    const item = makeShoppingListItem({ id: 'item-1', isChecked: false })
    const list = makeShoppingList({ items: [item] })
    api.put.mockRejectedValue(new Error('Network error'))

    const store = useShoppingListStore()
    store.list = list
    await store.toggleItem('item-1', true)

    expect(store.list.items[0].isChecked).toBe(false)
  })
})

// ── addCustomItem ─────────────────────────────────────────────────────────────

describe('addCustomItem', () => {
  it('calls POST and appends the returned item', async () => {
    const newItem = makeShoppingListItem({ id: 'item-custom', isCustom: true, displayName: 'Bread' })
    api.post.mockResolvedValue({ data: newItem })

    const store = useShoppingListStore()
    store.list = makeShoppingList({ id: 'list-1', items: [] })
    await store.addCustomItem({ name: 'Bread', amount: null, unit: null, category: 'OTHER' })

    expect(api.post).toHaveBeenCalledWith('/shopping-lists/list-1/items',
      { name: 'Bread', amount: null, unit: null, category: 'OTHER' })
    expect(store.list.items).toHaveLength(1)
    expect(store.list.items[0].displayName).toBe('Bread')
  })
})

// ── updateCustomItem ──────────────────────────────────────────────────────────

describe('updateCustomItem', () => {
  const edit = { name: 'Duck eggs', amount: 12, unit: 'each', category: 'DAIRY' }

  function storeWith(items) {
    const store = useShoppingListStore()
    store.list = makeShoppingList({ id: 'list-1', items })
    return store
  }

  it('PUTs the edit with its own 10 second timeout', async () => {
    api.put.mockResolvedValue({ data: makeShoppingListItem({ id: 'eggs', isCustom: true, ...edit }) })
    const store = storeWith([makeShoppingListItem({ id: 'eggs', isCustom: true })])

    await store.updateCustomItem('eggs', edit)

    expect(api.put).toHaveBeenCalledWith('/shopping-lists/list-1/custom-items/eggs', edit,
      expect.objectContaining({ timeout: 10_000 }))
  })

  it('replaces the item in place when its Category is unchanged', async () => {
    const store = storeWith([
      makeShoppingListItem({ id: 'eggs', displayName: 'Eggs', category: 'DAIRY', isCustom: true }),
      makeShoppingListItem({ id: 'milk', displayName: 'Milk', category: 'DAIRY' }),
    ])
    api.put.mockResolvedValue({
      data: makeShoppingListItem({ id: 'eggs', displayName: 'Duck eggs', category: 'DAIRY', isCustom: true }),
    })

    await store.updateCustomItem('eggs', edit)

    expect(store.list.items.map(i => i.displayName)).toEqual(['Duck eggs', 'Milk'])
  })

  it('moves the item to the end of the list when its Category changes', async () => {
    const store = storeWith([
      makeShoppingListItem({ id: 'eggs', displayName: 'Eggs', category: 'OTHER', isCustom: true }),
      makeShoppingListItem({ id: 'milk', displayName: 'Milk', category: 'DAIRY' }),
      makeShoppingListItem({ id: 'soap', displayName: 'Soap', category: 'OTHER', isCustom: true }),
    ])
    api.put.mockResolvedValue({
      data: makeShoppingListItem({ id: 'eggs', displayName: 'Duck eggs', category: 'DAIRY', isCustom: true }),
    })

    await store.updateCustomItem('eggs', edit)

    expect(store.list.items.map(i => i.displayName)).toEqual(['Milk', 'Soap', 'Duck eggs'])
  })

  it('rejects and leaves the item unchanged when the save fails', async () => {
    const original = makeShoppingListItem({ id: 'eggs', displayName: 'Eggs', category: 'OTHER', isCustom: true })
    const store = storeWith([original])
    api.put.mockRejectedValue({ status: 500, message: 'Server error' })

    await expect(store.updateCustomItem('eggs', edit)).rejects.toMatchObject({ message: 'Server error' })

    expect(store.list.items).toEqual([makeShoppingListItem({ id: 'eggs', displayName: 'Eggs', category: 'OTHER', isCustom: true })])
  })
})

// ── deleteItem ────────────────────────────────────────────────────────────────

describe('deleteItem', () => {
  it('calls DELETE and removes item from list', async () => {
    api.delete.mockResolvedValue({})

    const item = makeShoppingListItem({ id: 'item-to-delete', isCustom: true })
    const store = useShoppingListStore()
    store.list = makeShoppingList({ id: 'list-1', items: [item] })
    await store.deleteItem('item-to-delete')

    expect(api.delete).toHaveBeenCalledWith('/shopping-lists/list-1/items/item-to-delete')
    expect(store.list.items).toHaveLength(0)
  })
})

// ── isStale computed ──────────────────────────────────────────────────────────

describe('isStale', () => {
  it('returns true when list.isStale is true', () => {
    const store = useShoppingListStore()
    store.list = makeShoppingList({ isStale: true })
    expect(store.isStale).toBe(true)
  })

  it('returns false when list is null', () => {
    const store = useShoppingListStore()
    store.list = null
    expect(store.isStale).toBe(false)
  })
})
