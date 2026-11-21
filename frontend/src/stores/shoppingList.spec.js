import { setActivePinia, createPinia } from 'pinia'
import { useShoppingListStore } from './shoppingList'
import { makeShoppingList, makeShoppingListItem } from '@/test/factories'
import { useUiStore } from './ui'

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
  // Only the retry timers are faked, because fake-indexeddb schedules its own work with setImmediate.
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
  setActivePinia(createPinia())
  vi.clearAllMocks()
})

afterEach(() => {
  vi.useRealTimers()
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

// ── Pending Changes: the one write path ───────────────────────────────────────

const TIMEOUT = expect.objectContaining({ timeout: 10_000 })
const noResponse  = { status: undefined, message: 'Network Error' }
const serverError = { status: 500, message: 'Server error' }
const notFound    = { status: 404, message: 'Not Found' }

const milk  = (overrides = {}) => makeShoppingListItem({ id: 'milk', ingredientId: 'ing-milk', displayName: 'Milk', category: 'DAIRY', ...overrides })
const eggs  = (overrides = {}) => makeShoppingListItem({ id: 'eggs', ingredientId: null, displayName: 'Eggs', category: 'OTHER', isCustom: true, ...overrides })
const bread = { name: 'Bread', amount: null, unit: null, category: 'BAKERY' }

function storeWith(items, overrides = {}) {
  const store = useShoppingListStore()
  store.list = makeShoppingList({ id: 'list-1', items, ...overrides })
  return store
}

/** A fresh Pinia over the same IndexedDB, as after a reload or a killed PWA. */
function reopen() {
  setActivePinia(createPinia())
  return useShoppingListStore()
}

const shown = store => store.list.items.map(i => [i.id, i.displayName, i.isChecked])

describe('Pending Changes', () => {
  it('ticks at once and sends only Purchased, with a timeout, until the queue is empty', async () => {
    const store = storeWith([milk()])
    api.put.mockResolvedValue({ data: milk({ isChecked: true }) })

    const ticking = store.toggleItem('milk', true)
    expect(store.list.items[0].isChecked).toBe(true)
    await ticking
    await store.sendPending()

    expect(api.put).toHaveBeenCalledWith('/shopping-lists/list-1/items/milk', { isChecked: true }, TIMEOUT)
    expect(store.pendingCount).toBe(0)
  })

  it('shows an added Custom Item at once, then gives it the server’s id, and a tick made meanwhile waits behind the add', async () => {
    const store = storeWith([])
    let answerAdd
    api.post.mockReturnValue(new Promise(resolve => { answerAdd = resolve }))
    api.put.mockResolvedValue({ data: eggs({ id: 'bread-1', displayName: 'Bread', isChecked: true }) })

    await store.addCustomItem(bread)
    const [added] = store.list.items
    expect(added).toMatchObject({ displayName: 'Bread', isCustom: true })
    await store.toggleItem(added.id, true)
    expect(api.put).not.toHaveBeenCalled()

    answerAdd({ data: eggs({ id: 'bread-1', displayName: 'Bread', category: 'BAKERY' }) })
    await store.sendPending()

    expect(api.post).toHaveBeenCalledWith('/shopping-lists/list-1/items', bread, TIMEOUT)
    expect(api.put).toHaveBeenCalledWith('/shopping-lists/list-1/items/bread-1', { isChecked: true }, TIMEOUT)
    expect(shown(store)).toEqual([['bread-1', 'Bread', true]])
  })

  it('sends every tick made while the add’s answer is still being stored under the server’s id', async () => {
    const store = storeWith([])
    const answered = eggs({ id: 'bread-1', displayName: 'Bread', category: 'BAKERY' })
    const ticks = []
    // A tick on every macrotask after the answer sweeps the whole time the store spends writing it to IndexedDB.
    api.post.mockImplementation(() => new Promise(resolve => setImmediate(() => {
      resolve({ data: answered })
      const tickAgain = n => setImmediate(() => {
        const [item] = store.list.items
        ticks.push(store.toggleItem(item.id, !item.isChecked))
        if (n > 1) tickAgain(n - 1)
      })
      tickAgain(30)
    })))
    api.put.mockImplementation((_url, body) => Promise.resolve({ data: { ...answered, isChecked: body.isChecked } }))

    await store.addCustomItem(bread)
    await vi.waitUntil(() => ticks.length === 30, { interval: 1 })
    await Promise.all(ticks)
    await store.sendPending()

    expect(api.put.mock.calls.map(([url]) => url).filter(url => !url.endsWith('/items/bread-1'))).toEqual([])
    expect(useUiStore().snackbar.show).toBe(false)
  })

  it('shows an added item once when a List Refresh brings it back before the add’s answer', async () => {
    const store = storeWith([])
    const answered = eggs({ id: 'bread-1', displayName: 'Bread', category: 'BAKERY' })
    let answerAdd
    api.post.mockReturnValue(new Promise(resolve => { answerAdd = resolve }))

    await store.addCustomItem(bread)
    store.applyRefresh(makeShoppingList({ id: 'list-1', items: [answered] }))
    answerAdd({ data: answered })
    await store.sendPending()

    expect(store.list.items.map(i => i.id)).toEqual(['bread-1'])
    const reopened = reopen()
    await reopened.restore()
    expect(reopened.list.items.map(i => i.id)).toEqual(['bread-1'])
  })

  it('edits a Custom Item at once, moving it last when its Category changes, and sends the edit', async () => {
    const edit = { name: 'Duck eggs', amount: 12, unit: 'pcs', category: 'DAIRY' }
    const store = storeWith([eggs(), milk()])
    api.put.mockResolvedValue({ data: eggs({ displayName: 'Duck eggs', amount: 12, unit: 'pcs', category: 'DAIRY' }) })

    await store.updateCustomItem('eggs', edit)
    expect(store.list.items.map(i => i.displayName)).toEqual(['Milk', 'Duck eggs'])
    await store.sendPending()

    expect(api.put).toHaveBeenCalledWith('/shopping-lists/list-1/custom-items/eggs', edit, TIMEOUT)
    expect(store.list.items.map(i => i.displayName)).toEqual(['Milk', 'Duck eggs'])
  })

  it('removes a deleted Custom Item at once and sends the delete', async () => {
    const store = storeWith([eggs(), milk()])
    api.delete.mockResolvedValue({})

    await store.deleteItem('eggs')
    expect(store.list.items.map(i => i.id)).toEqual(['milk'])
    await store.sendPending()

    expect(api.delete).toHaveBeenCalledWith('/shopping-lists/list-1/items/eggs', TIMEOUT)
  })

  it('sends nothing for an add then a delete made while offline', async () => {
    const store = storeWith([])
    api.post.mockRejectedValue(noResponse)
    store.markOffline()

    await store.addCustomItem(bread)
    await store.sendPending()
    api.post.mockClear()
    await store.deleteItem(store.list.items[0].id)
    await store.sendPending()

    expect(api.post).not.toHaveBeenCalled()
    expect(api.delete).not.toHaveBeenCalled()
    expect(store.pendingCount).toBe(0)
  })
})

describe('sending Pending Changes', () => {
  it('keeps a change that got no response, marks the app offline, and clears offline on the next response', async () => {
    const store = storeWith([milk()])
    api.put.mockRejectedValueOnce(noResponse)

    await store.toggleItem('milk', true)
    await store.sendPending()
    expect(store.offline).toBe(true)
    expect(store.pendingCount).toBe(1)
    expect(store.list.items[0].isChecked).toBe(true)

    api.put.mockResolvedValueOnce({ data: milk({ isChecked: true }) })
    await store.sendPending()
    expect(store.offline).toBe(false)
    expect(store.pendingCount).toBe(0)
  })

  it('keeps a change the server answered with a 5xx, without calling the app offline', async () => {
    const store = storeWith([milk()])
    api.put.mockRejectedValue(serverError)

    await store.toggleItem('milk', true)
    await store.sendPending()

    expect(store.pendingCount).toBe(1)
    expect(store.offline).toBe(false)
  })

  it('sends in the order the changes were made, and stops at the first that fails', async () => {
    const store = storeWith([milk(), eggs()])
    api.put.mockRejectedValue(noResponse)

    await store.toggleItem('milk', true)
    await store.toggleItem('eggs', true)
    await store.sendPending()

    expect(api.put.mock.calls.map(([url]) => url)).toEqual(['/shopping-lists/list-1/items/milk'])
  })

  it('retries on a backoff from 5 s doubling to 60 s, and stops once the queue is empty', async () => {
    const store = storeWith([milk()])
    api.put.mockRejectedValue(noResponse)
    await store.toggleItem('milk', true)
    await store.sendPending()
    const sends = () => api.put.mock.calls.length
    // vi.waitFor would advance the fake clock itself, so this polls on setImmediate, which isn't faked.
    const retriedOnce = async count => {
      for (let i = 0; i < 100 && (sends() === count || store.sending); i++) await new Promise(setImmediate)
      expect(sends()).toBe(count + 1)
      expect(store.sending).toBe(false)
    }

    for (const delay of [5_000, 10_000, 20_000, 40_000, 60_000, 60_000]) {
      const count = sends()
      await vi.advanceTimersByTimeAsync(delay - 1)
      expect(store.sending, `not before ${delay} ms`).toBe(false)
      expect(sends(), `not before ${delay} ms`).toBe(count)
      await vi.advanceTimersByTimeAsync(1)
      await retriedOnce(count)
    }

    api.put.mockResolvedValue({ data: milk({ isChecked: true }) })
    const count = sends()
    await vi.advanceTimersByTimeAsync(60_000)
    await retriedOnce(count)
    await vi.advanceTimersByTimeAsync(10 * 60_000)
    expect(sends()).toBe(count + 1)
    expect(store.pendingCount).toBe(0)
  })

  it('drops a change any other 4xx rejects, saying why, and sends the next', async () => {
    const store = storeWith([eggs(), milk()])
    api.put.mockRejectedValueOnce({ status: 400, message: 'Item name is required' })
    api.put.mockResolvedValueOnce({ data: milk({ isChecked: true }) })

    await store.updateCustomItem('eggs', { ...bread, name: ' ' })
    await store.toggleItem('milk', true)
    await store.sendPending()

    expect(store.pendingCount).toBe(0)
    expect(api.put).toHaveBeenCalledTimes(2)
    expect(useUiStore().snackbar).toMatchObject({ show: true, message: 'Item name is required', color: 'error' })
  })

  it('goes offline on the browser’s say-so and on a refresh that gets no response', async () => {
    const store = storeWith([milk()])
    store.markOffline()
    expect(store.offline).toBe(true)

    api.get.mockResolvedValueOnce({ data: makeShoppingList() })
    await store.fetchRefresh()
    expect(store.offline).toBe(false)

    api.get.mockRejectedValueOnce(noResponse)
    await expect(store.fetchRefresh()).rejects.toBe(noResponse)
    expect(store.offline).toBe(true)
  })
})

describe('conflicts', () => {
  const snackbar = () => useUiStore().snackbar
  const regenerated = items => ({ data: makeShoppingList({ id: 'list-1', items }) })

  it('moves a tick on a Generated Item a regeneration removed onto the one new item for the same Ingredient', async () => {
    const store = storeWith([milk()])
    api.put.mockRejectedValueOnce(notFound)
    api.put.mockResolvedValueOnce({ data: milk({ id: 'milk-2', isChecked: true }) })
    api.get.mockResolvedValue(regenerated([milk({ id: 'milk-2' }), eggs()]))

    await store.toggleItem('milk', true)
    await store.sendPending()

    expect(api.put).toHaveBeenLastCalledWith('/shopping-lists/list-1/items/milk-2', { isChecked: true }, TIMEOUT)
    expect(store.pendingCount).toBe(0)
    expect(snackbar().show).toBe(false)
  })

  it.each([
    ['no', []],
    ['two', [milk({ id: 'milk-2' }), milk({ id: 'milk-3' })]],
  ])('drops such a tick when the new list has %s items for that Ingredient, with one summary', async (_, items) => {
    const store = storeWith([milk(), milk({ id: 'oat-milk' })])
    api.put.mockRejectedValue(notFound)
    api.get.mockResolvedValue(regenerated(items))

    await store.toggleItem('milk', true)
    await store.toggleItem('oat-milk', true)
    await store.sendPending()

    expect(store.pendingCount).toBe(0)
    expect(snackbar()).toMatchObject({
      show: true,
      message: '2 ticked items left the list when it was regenerated, so their ticks weren’t saved',
    })
  })

  it('keeps such a tick waiting, and still merging, when the new list can’t be loaded', async () => {
    const store = storeWith([milk()])
    api.put.mockRejectedValue(notFound)
    api.get.mockRejectedValue(noResponse)

    await store.toggleItem('milk', true)
    await store.sendPending()
    expect(store.pendingCount).toBe(1)
    expect(store.offline).toBe(true)

    await store.toggleItem('milk', false)
    expect(store.pendingChanges.map(c => c.isChecked)).toEqual([false])
  })

  it('drops an edit of a Custom Item the other phone deleted, naming it', async () => {
    const store = storeWith([eggs()])
    api.put.mockRejectedValue(notFound)

    await store.updateCustomItem('eggs', { ...bread, name: 'Duck eggs' })
    await store.sendPending()

    expect(store.pendingCount).toBe(0)
    expect(snackbar()).toMatchObject({ show: true, message: '“Eggs” was removed from the list' })
  })

  it('drops a tick of a Custom Item the other phone deleted, naming it', async () => {
    const store = storeWith([eggs()])
    api.put.mockRejectedValue(notFound)

    await store.toggleItem('eggs', true)
    await store.sendPending()

    expect(api.get).not.toHaveBeenCalled()
    expect(snackbar()).toMatchObject({ show: true, message: '“Eggs” was removed from the list' })
  })

  it('drops a delete of a Custom Item that is already gone, without a word', async () => {
    const store = storeWith([eggs()])
    api.delete.mockRejectedValue(notFound)

    await store.deleteItem('eggs')
    await store.sendPending()

    expect(store.pendingCount).toBe(0)
    expect(snackbar().show).toBe(false)
  })

  it('drops every change for a list that is no longer active, with one summary, and never moves them', async () => {
    const store = storeWith([milk(), eggs()])
    store.markOffline()
    api.put.mockRejectedValue(noResponse)
    await store.toggleItem('milk', true)
    await store.updateCustomItem('eggs', { ...bread, name: 'Duck eggs' })
    await store.addCustomItem(bread)
    await store.sendPending()

    api.put.mockReset()
    api.put.mockRejectedValue({ status: 410, message: 'Gone' })
    await store.sendPending()

    expect(api.put).toHaveBeenCalledTimes(1)
    expect(api.post).not.toHaveBeenCalled()
    expect(store.pendingCount).toBe(0)
    expect(snackbar()).toMatchObject({
      show: true,
      message: '3 offline changes were for an earlier list and weren’t saved',
    })
  })
})

describe('persistence and cold start', () => {
  async function tickWhileOffline(store, itemId) {
    store.markOffline()
    api.put.mockRejectedValue(noResponse)
    await store.toggleItem(itemId, true)
    await store.sendPending()
  }

  it('keeps Pending Changes through a reload, and sends them from the new page', async () => {
    await tickWhileOffline(storeWith([milk()]), 'milk')

    const reopened = reopen()
    await reopened.restore()
    expect(reopened.pendingCount).toBe(1)

    api.put.mockReset()
    api.put.mockResolvedValue({ data: milk({ isChecked: true }) })
    await reopened.sendPending()
    expect(api.put).toHaveBeenCalledWith('/shopping-lists/list-1/items/milk', { isChecked: true }, TIMEOUT)
  })

  it('shows the saved snapshot at once on a cold start, with the Pending Changes applied on top', async () => {
    api.get.mockResolvedValue({ data: makeShoppingList({ id: 'list-1', items: [milk(), eggs()] }) })
    const store = useShoppingListStore()
    await store.fetchActive()
    await tickWhileOffline(store, 'milk')

    const reopened = reopen()
    expect(await reopened.restore()).toBe(true)

    expect(shown(reopened)).toEqual([['milk', 'Milk', true], ['eggs', 'Eggs', false]])
  })

  it('saves the snapshot after every List Refresh that is applied', async () => {
    storeWith([milk()]).applyRefresh(makeShoppingList({ id: 'list-1', items: [milk(), eggs()] }))

    const reopened = reopen()
    await reopened.restore()

    expect(shown(reopened)).toEqual([['milk', 'Milk', false], ['eggs', 'Eggs', false]])
  })

  it('keeps the snapshot in step with confirmed writes, so a reload shows the server’s ids', async () => {
    api.get.mockResolvedValue({ data: makeShoppingList({ id: 'list-1', items: [milk()] }) })
    api.post.mockResolvedValue({ data: eggs({ id: 'bread-1', displayName: 'Bread', category: 'BAKERY' }) })
    const store = useShoppingListStore()
    await store.fetchActive()
    await store.addCustomItem(bread)
    await store.sendPending()

    const reopened = reopen()
    await reopened.restore()

    expect(shown(reopened)).toEqual([['milk', 'Milk', false], ['bread-1', 'Bread', false]])
    expect(reopened.pendingCount).toBe(0)
  })

  it('shows nothing offline when this phone has never saved the list', async () => {
    const store = useShoppingListStore()
    expect(await store.restore()).toBe(false)

    api.get.mockRejectedValue(noResponse)
    await store.fetchActive()

    expect(store.list).toBeNull()
    expect(store.offline).toBe(true)
  })
})

describe('two tabs on one phone', () => {
  // Grants each request in turn, as navigator.locks does for one lock name.
  function fakeLocks() {
    let tail = Promise.resolve()
    return {
      request(_name, send) {
        const granted = tail.then(() => send())
        tail = granted.catch(() => {})
        return granted
      },
    }
  }

  beforeEach(() => {
    Object.defineProperty(navigator, 'locks', { value: fakeLocks(), configurable: true })
  })

  afterEach(() => {
    delete navigator.locks
  })

  it('never send the same Pending Change twice', async () => {
    const first = storeWith([milk()])
    first.markOffline()
    api.put.mockRejectedValue(noResponse)
    await first.toggleItem('milk', true)
    await first.sendPending()

    const second = reopen()
    await second.restore()
    api.put.mockReset()
    api.put.mockResolvedValue({ data: milk({ isChecked: true }) })
    await Promise.all([first.sendPending(), second.sendPending()])

    expect(api.put).toHaveBeenCalledTimes(1)
  })
})

// ── List Refresh ──────────────────────────────────────────────────────────────

describe('List Refresh', () => {
  const carrot = (overrides = {}) => makeShoppingListItem({ id: 'carrot', displayName: 'Carrot', ...overrides })
  const milk   = (overrides = {}) => makeShoppingListItem({ id: 'milk', displayName: 'Milk', category: 'DAIRY', ...overrides })

  function storeWith(items) {
    const store = useShoppingListStore()
    store.list = makeShoppingList({ id: 'list-1', items })
    return store
  }

  it('fetches the active list without changing what is on screen', async () => {
    const store = storeWith([carrot()])
    api.get.mockResolvedValue({ data: makeShoppingList({ items: [milk()] }) })

    const latest = await store.fetchRefresh()

    expect(api.get).toHaveBeenCalledWith('/shopping-lists/active', TIMEOUT)
    expect(latest.items.map(i => i.id)).toEqual(['milk'])
    expect(store.list.items.map(i => i.id)).toEqual(['carrot'])
    expect(store.loading).toBe(false)
  })

  it('resolves to no list when the server has no active list, and rejects on any other failure', async () => {
    const store = storeWith([carrot()])
    api.get.mockRejectedValueOnce({ status: 404, message: 'Not Found' })
    expect(await store.fetchRefresh()).toBeNull()

    api.get.mockRejectedValueOnce({ status: 500, message: 'Server error' })
    await expect(store.fetchRefresh()).rejects.toMatchObject({ message: 'Server error' })
    expect(store.error).toBeNull()
    expect(store.list.items.map(i => i.id)).toEqual(['carrot'])
  })

  it('takes the server’s copy of every item, including added, removed and Purchased ones', () => {
    const store = storeWith([carrot(), milk()])

    store.applyRefresh(makeShoppingList({
      isStale: true,
      items: [carrot({ isChecked: true }), makeShoppingListItem({ id: 'bread', displayName: 'Bread', isCustom: true })],
    }))

    expect(store.list.items).toEqual([
      carrot({ isChecked: true }),
      makeShoppingListItem({ id: 'bread', displayName: 'Bread', isCustom: true }),
    ])
    expect(store.isStale).toBe(true)
  })

  it('keeps an item with a Pending Change as it is on screen, from the moment it is made until it is confirmed', async () => {
    const store = storeWith([carrot(), milk()])
    let answer
    api.put.mockReturnValue(new Promise(resolve => { answer = resolve }))

    const ticking = store.toggleItem('carrot', true)
    store.applyRefresh(makeShoppingList({ items: [carrot({ isChecked: false }), milk({ isChecked: true })] }))
    expect(store.list.items.map(i => [i.id, i.isChecked])).toEqual([['carrot', true], ['milk', true]])
    await ticking

    store.applyRefresh(makeShoppingList({ items: [carrot({ displayName: 'Carrots', isChecked: false })] }))
    expect(shown(store)).toEqual([['carrot', 'Carrot', true]])

    answer({ data: carrot({ isChecked: true }) })
    await store.sendPending()
    store.applyRefresh(makeShoppingList({ items: [carrot({ isChecked: false })] }))
    expect(store.list.items[0].isChecked).toBe(false)
  })

  it('keeps a waiting add and a waiting delete through a List Refresh', async () => {
    const store = storeWith([carrot(), makeShoppingListItem({ id: 'soap', displayName: 'Soap', isCustom: true })])
    store.markOffline()
    api.post.mockRejectedValue(noResponse)
    api.delete.mockRejectedValue(noResponse)

    await store.deleteItem('soap')
    await store.addCustomItem(bread)
    await store.sendPending()
    store.applyRefresh(makeShoppingList({
      items: [carrot(), makeShoppingListItem({ id: 'soap', displayName: 'Soap', isCustom: true })],
    }))

    expect(store.list.items.map(i => i.displayName)).toEqual(['Carrot', 'Bread'])
  })

  it('keeps an item the caller asks to keep, such as one inside its Purchased window', () => {
    const store = storeWith([carrot({ isChecked: true }), milk()])

    store.applyRefresh(
      makeShoppingList({ items: [carrot({ isChecked: false }), milk({ isChecked: true })] }),
      { keep: id => id === 'carrot' },
    )

    expect(store.list.items.map(i => [i.id, i.isChecked])).toEqual([['carrot', true], ['milk', true]])
  })

  it('drops a kept item the server no longer has, as after a regeneration', () => {
    const store = storeWith([carrot({ isChecked: true })])

    store.applyRefresh(makeShoppingList({ items: [carrot({ id: 'carrot-regenerated' })] }), { keep: () => true })

    expect(store.list.items.map(i => i.id)).toEqual(['carrot-regenerated'])
  })

  it('shows no list once the server has none', () => {
    const store = storeWith([carrot()])
    store.applyRefresh(null)
    expect(store.list).toBeNull()
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
