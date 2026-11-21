import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import ShoppingView from './ShoppingView.vue'
import ServingsChip from '@/components/ServingsChip.vue'
import { makeItemSource, makeShoppingList, makeShoppingListItem } from '@/test/factories'
import { servingsFromQuery } from '@/router/recipeRoutes'
import { useUiStore } from '@/stores/ui'
import api from '@/services/api'
import { clearForTest } from '@/utils/shoppingListCache'

vi.mock('@/services/api', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}))

// IndexedDB answers over several macrotasks, more than flushPromises waits for, so the view sees an in-memory copy.
vi.mock('@/utils/shoppingListCache', () => {
  const stored = new Map()
  const copy = value => value === undefined ? undefined : JSON.parse(JSON.stringify(value))
  return {
    readPendingChanges:   async () => copy(stored.get('queue')) ?? [],
    updatePendingChanges: async change => { stored.set('queue', copy(change(stored.get('queue') ?? []))); return copy(stored.get('queue')) },
    readSnapshot:         async () => copy(stored.get('snapshot')),
    writeSnapshot:        async list => { stored.set('snapshot', copy(list)) },
    updateSnapshot:       async change => { if (stored.get('snapshot')) stored.set('snapshot', copy(change(stored.get('snapshot')))) },
    clearForTest:         () => stored.clear(),
  }
})

const vuetify = createVuetify({ components, directives })

let pinia

beforeEach(() => {
  pinia = createPinia()
  setActivePinia(pinia)
  // Reset, not just cleared, so a List Refresh never answers with another test's list.
  vi.resetAllMocks()
  clearForTest()
})

const SheetStub = {
  name: 'CustomItemSheet',
  props: { modelValue: Boolean, item: { type: Object, default: null } },
  emits: ['update:modelValue', 'confirm', 'save'],
  template: '<div />',
}

async function mountView({ attachTo } = {}) {
  const { useShoppingListStore } = await import('@/stores/shoppingList')
  const store = useShoppingListStore()
  vi.spyOn(store, 'fetchActive').mockResolvedValue(undefined)

  const wrapper = mount(ShoppingView, {
    attachTo,
    global: {
      plugins: [vuetify, pinia],
      stubs: {
        RouterLink: { template: '<a><slot/></a>' },
        'router-link': { template: '<a><slot/></a>' },
        teleport: true,
        CustomItemSheet: SheetStub,
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

  it('shows just the amount for an item with no unit', async () => {
    const { wrapper, store } = await mountView()
    store.list = makeShoppingList({
      items: [makeShoppingListItem({ id: 'i1', displayName: 'Apple', isCustom: true, amount: 2, unit: null })],
    })
    await wrapper.vm.$nextTick()
    expect(wrapper.get('.v-list-item-subtitle').text().trim()).toBe('2')
  })

  it('shows warning chip for needsReview items', async () => {
    const { wrapper, store } = await mountView()
    store.list = makeShoppingList({
      items: [makeShoppingListItem({ id: 'i1', needsReview: true })],
    })
    await wrapper.vm.$nextTick()
    expect(wrapper.html()).toContain('mdi-alert-outline')
  })

  it('does not toggle Purchased when the row is clicked', async () => {
    const { wrapper, store } = await mountView()
    vi.spyOn(store, 'toggleItem').mockResolvedValue(undefined)
    store.list = makeShoppingList({
      items: [makeShoppingListItem({ id: 'item-1', isChecked: false })],
    })
    await wrapper.vm.$nextTick()

    await wrapper.find('.v-list-item').trigger('click')
    expect(store.toggleItem).not.toHaveBeenCalled()
  })

  it('marks an item purchased when its checkbox is clicked', async () => {
    const { wrapper, store } = await mountView()
    vi.spyOn(store, 'toggleItem').mockResolvedValue(undefined)
    store.list = makeShoppingList({
      items: [makeShoppingListItem({ id: 'item-1', displayName: 'Carrot', isChecked: false })],
    })
    await wrapper.vm.$nextTick()

    await wrapper.find('input[aria-label="Mark Carrot purchased"]').trigger('click')
    expect(store.toggleItem).toHaveBeenCalledOnce()
    expect(store.toggleItem).toHaveBeenCalledWith('item-1', true)
  })

  it('unmarks a purchased item when its checkbox is clicked', async () => {
    const { wrapper, store } = await mountView()
    vi.spyOn(store, 'toggleItem').mockResolvedValue(undefined)
    store.list = makeShoppingList({
      items: [makeShoppingListItem({ id: 'item-1', displayName: 'Carrot', isChecked: true })],
    })
    await wrapper.vm.$nextTick()

    const showPurchased = wrapper.findAll('button').find(b => b.text().includes('Show purchased'))
    await showPurchased.trigger('click')

    await wrapper.find('input[aria-label="Mark Carrot purchased"]').trigger('click')
    expect(store.toggleItem).toHaveBeenCalledOnce()
    expect(store.toggleItem).toHaveBeenCalledWith('item-1', false)
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

  describe('hiding a Purchased item', () => {
    const carrot   = () => makeShoppingListItem({ id: 'carrot', displayName: 'Carrot' })
    const milk     = () => makeShoppingListItem({ id: 'milk',   displayName: 'Milk', category: 'DAIRY' })
    const title    = (wrapper, name) => wrapper.findAll('.v-list-item-title').find(t => t.text() === name)
    const checkbox = (wrapper, name) => wrapper.find(`input[aria-label="Mark ${name} purchased"]`)
    const toggleShowPurchased = wrapper =>
      wrapper.findAll('button').find(b => /(Show|Hide) purchased/.test(b.text())).trigger('click')

    async function mountWith(items) {
      const mounted = await mountView()
      mounted.store.list = makeShoppingList({ items })
      await mounted.wrapper.vm.$nextTick()
      api.put.mockImplementation((_, { isChecked }) => Promise.resolve({ data: { isChecked } }))
      return mounted
    }

    async function tick(wrapper, name) {
      await checkbox(wrapper, name).trigger('click')
      await flushPromises()
    }

    async function wait(ms) {
      vi.advanceTimersByTime(ms)
      await flushPromises()
    }

    beforeEach(() => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
    })

    afterEach(() => {
      vi.useRealTimers()
    })

    it('keeps a ticked item shown and struck through for 3 seconds, then hides it', async () => {
      const { wrapper } = await mountWith([carrot()])

      await tick(wrapper, 'Carrot')
      await wait(2999)
      expect(title(wrapper, 'Carrot').classes()).toContain('text-decoration-line-through')

      await wait(1)
      expect(title(wrapper, 'Carrot')).toBeUndefined()
    })

    it('keeps an item unticked during the delay, and a fresh tick starts a new 3 seconds', async () => {
      const { wrapper } = await mountWith([carrot()])

      await tick(wrapper, 'Carrot')
      await wait(2000)
      await tick(wrapper, 'Carrot')
      await wait(3000)
      expect(title(wrapper, 'Carrot').classes()).not.toContain('text-decoration-line-through')

      await tick(wrapper, 'Carrot')
      await wait(2999)
      expect(title(wrapper, 'Carrot').exists()).toBe(true)
      await wait(1)
      expect(title(wrapper, 'Carrot')).toBeUndefined()
    })

    it('hides each ticked item 3 seconds after its own tick', async () => {
      const { wrapper } = await mountWith([carrot(), milk()])

      await tick(wrapper, 'Carrot')
      await wait(2000)
      await tick(wrapper, 'Milk')
      await wait(1000)
      expect(title(wrapper, 'Carrot')).toBeUndefined()
      expect(title(wrapper, 'Milk').exists()).toBe(true)

      await wait(2000)
      expect(title(wrapper, 'Milk')).toBeUndefined()
    })

    it('hides a ticked item at once when Hide purchased is tapped during the delay', async () => {
      const { wrapper } = await mountWith([carrot()])

      await tick(wrapper, 'Carrot')
      await toggleShowPurchased(wrapper)
      await toggleShowPurchased(wrapper)

      expect(title(wrapper, 'Carrot')).toBeUndefined()
    })

    it('keeps a ticked item shown while purchased items are shown, and hides it at once on Hide purchased', async () => {
      const { wrapper } = await mountWith([carrot()])
      await toggleShowPurchased(wrapper)

      await tick(wrapper, 'Carrot')
      await wait(3000)
      expect(title(wrapper, 'Carrot').classes()).toContain('text-decoration-line-through')

      await tick(wrapper, 'Carrot')
      await tick(wrapper, 'Carrot')
      await toggleShowPurchased(wrapper)
      expect(title(wrapper, 'Carrot')).toBeUndefined()
    })
  })

  describe('Item Sources', () => {
    const row        = (wrapper, name) => wrapper.findAll('.shopping-row').find(r => r.text().includes(name))
    const sourceRows = wrapper => wrapper.findAllComponents({ name: 'VListItem' }).filter(i => i.classes('item-source'))

    async function mountWith(items, { showPurchased = false } = {}) {
      const mounted = await mountView()
      mounted.store.list = makeShoppingList({ items })
      await mounted.wrapper.vm.$nextTick()
      if (showPurchased) {
        await mounted.wrapper.findAll('button').find(b => b.text().includes('Show purchased')).trigger('click')
      }
      return mounted
    }

    const flour = (overrides = {}) => makeShoppingListItem({
      id: 'flour',
      displayName: 'Flour',
      amount: 270,
      unit: 'ml',
      sources: [
        makeItemSource({ recipeId: 'pancakes', recipeName: 'Pancakes', amount: 2, unit: 'tbsp', shareAmount: 30, shareUnit: 'ml', scheduledDate: '2026-06-03', servings: 8 }),
        makeItemSource({ recipeId: 'waffles', recipeName: 'Waffles', amount: 1, unit: 'cup', shareAmount: 240, shareUnit: 'ml' }),
      ],
      ...overrides,
    })

    it('shows a chevron on a row with sources and keeps them collapsed', async () => {
      const { wrapper } = await mountWith([flour()])
      expect(row(wrapper, 'Flour').html()).toContain('mdi-chevron-down')
      expect(wrapper.text()).not.toContain('Pancakes')
    })

    it('expands a row’s sources on tap and collapses them on a second tap', async () => {
      const { wrapper } = await mountWith([flour()])

      await row(wrapper, 'Flour').trigger('click')
      expect(sourceRows(wrapper).map(s => s.text())).toEqual([
        expect.stringContaining('Pancakes · 2 tbsp (30 ml)'),
        expect.stringContaining('Waffles · 1 cup (240 ml)'),
      ])

      await row(wrapper, 'Flour').trigger('click')
      await flushPromises()
      expect(sourceRows(wrapper)).toHaveLength(0)
    })

    it('shows each source’s Servings and its date when it has one', async () => {
      const { wrapper } = await mountWith([flour()])
      await row(wrapper, 'Flour').trigger('click')

      const [pancakes, waffles] = sourceRows(wrapper)
      expect(pancakes.findComponent(ServingsChip).text()).toBe('8 servings')
      expect(pancakes.text()).toContain('3 Jun')
      expect(waffles.findComponent(ServingsChip).text()).toBe('4 servings')
      expect(waffles.find('.v-list-item-subtitle').exists()).toBe(false)
    })

    it('shows no Servings for a source that has none', async () => {
      const { wrapper } = await mountWith([makeShoppingListItem({
        id: 'oats', displayName: 'Oats', sources: [makeItemSource({ recipeId: null, recipeName: 'Old porridge', servings: null })],
      })])
      await row(wrapper, 'Oats').trigger('click')

      expect(sourceRows(wrapper)[0].findComponent(ServingsChip).exists()).toBe(false)
    })

    it('shows an Unquantified source without an amount', async () => {
      const { wrapper } = await mountWith([makeShoppingListItem({
        id: 'salt', displayName: 'Salt', amount: null, unit: null,
        sources: [makeItemSource({ recipeName: 'Soup', amount: null, unit: null })],
      })])
      await row(wrapper, 'Salt').trigger('click')
      expect(sourceRows(wrapper)[0].find('.v-list-item-title').text()).toBe('Soup')
    })

    it('shows the recipe’s own amount alone when it is already in the row’s unit', async () => {
      const { wrapper } = await mountWith([makeShoppingListItem({
        id: 'oats', displayName: 'Oats', sources: [makeItemSource({ recipeName: 'Porridge', amount: 200, unit: 'g' })],
      })])
      await row(wrapper, 'Oats').trigger('click')
      expect(sourceRows(wrapper)[0].find('.v-list-item-title').text()).toBe('Porridge · 200 g')
    })

    it('shows a count and its sources without the unit', async () => {
      const { wrapper } = await mountWith([makeShoppingListItem({
        id: 'eggs', displayName: 'Eggs', amount: 6, unit: 'pcs',
        sources: [makeItemSource({ recipeName: 'Omelette', amount: 6, unit: 'pcs' })],
      })])
      expect(row(wrapper, 'Eggs').text()).not.toContain('pcs')

      await row(wrapper, 'Eggs').trigger('click')
      expect(sourceRows(wrapper)[0].find('.v-list-item-title').text()).toBe('Omelette · 6')
    })

    it('keeps several rows open at once', async () => {
      const { wrapper } = await mountWith([
        flour(),
        makeShoppingListItem({ id: 'oats', displayName: 'Oats', sources: [makeItemSource({ recipeName: 'Porridge' })] }),
      ])
      await row(wrapper, 'Flour').trigger('click')
      await row(wrapper, 'Oats').trigger('click')
      expect(sourceRows(wrapper)).toHaveLength(3)
    })

    it('expands a Purchased row', async () => {
      const { wrapper } = await mountWith([flour({ isChecked: true })], { showPurchased: true })
      await row(wrapper, 'Flour').trigger('click')
      expect(sourceRows(wrapper)).toHaveLength(2)
    })

    it('marks purchased from the checkbox without expanding the row', async () => {
      const { wrapper, store } = await mountWith([flour()])
      vi.spyOn(store, 'toggleItem').mockResolvedValue(undefined)

      await row(wrapper, 'Flour').find('.purchased-checkbox input').trigger('click')

      expect(store.toggleItem).toHaveBeenCalledWith('flour', true)
      expect(sourceRows(wrapper)).toHaveLength(0)
    })

    it('opens a source’s recipe at its Planned Meal’s Servings', async () => {
      const { wrapper } = await mountWith([flour()])
      await row(wrapper, 'Flour').trigger('click')

      const location = sourceRows(wrapper)[0].props('to')
      expect(location).toMatchObject({ name: 'recipe-detail', params: { id: 'pancakes' } })
      expect(servingsFromQuery(location.query, 4)).toBe(8)
    })

    it('does not link a source whose recipe has been deleted', async () => {
      const { wrapper } = await mountWith([makeShoppingListItem({
        id: 'oats', displayName: 'Oats', sources: [makeItemSource({ recipeId: null, recipeName: 'Old porridge' })],
      })])
      await row(wrapper, 'Oats').trigger('click')
      expect(sourceRows(wrapper)[0].props('to')).toBeUndefined()
    })

    it('does not expand a Custom Item', async () => {
      const { wrapper } = await mountWith([makeShoppingListItem({ id: 'bread', displayName: 'Bread', isCustom: true })])

      expect(row(wrapper, 'Bread').html()).not.toContain('mdi-chevron')
      await row(wrapper, 'Bread').trigger('click')
      expect(row(wrapper, 'Bread').attributes('aria-expanded')).toBeUndefined()
      expect(sourceRows(wrapper)).toHaveLength(0)
    })

    it('does not expand a Generated Item from before sources were recorded', async () => {
      const { wrapper } = await mountWith([makeShoppingListItem({ id: 'old', displayName: 'Rice', sources: [] })])
      expect(row(wrapper, 'Rice').html()).not.toContain('mdi-chevron')
      expect(row(wrapper, 'Rice').attributes('aria-expanded')).toBeUndefined()
    })
  })

  describe('List Refresh', () => {
    const carrot = (overrides = {}) => makeShoppingListItem({ id: 'carrot', displayName: 'Carrot', ...overrides })
    const bread  = (overrides = {}) => makeShoppingListItem({ id: 'bread', displayName: 'Bread', isCustom: true, ...overrides })
    const names  = wrapper => wrapper.findAll('.v-list-item-title').map(t => t.text())
    const sheet  = wrapper => wrapper.findComponent(SheetStub)

    /** Dispatches a pointer event anywhere on the page, which is all the touch hold watches. */
    function pointer(type, pointerId = 1) {
      const event = new Event(type, { bubbles: true })
      Object.defineProperty(event, 'pointerId', { value: pointerId })
      document.body.dispatchEvent(event)
    }

    /** Opens the edit sheet with a 1-second hold, then lets the touch settle. */
    async function holdToEdit(wrapper, name) {
      const row = wrapper.findAll('.shopping-row').find(r => r.text().includes(name))
      await row.trigger('pointerdown', { pointerType: 'touch', isPrimary: true, button: 0, clientX: 0, clientY: 0, pointerId: 1 })
      await wait(1000)
      await row.trigger('pointerup', { pointerId: 1 })
      await wait(1000)
    }

    function setVisibility(state) {
      Object.defineProperty(document, 'visibilityState', { value: state, configurable: true })
      document.dispatchEvent(new Event('visibilitychange'))
    }

    async function mountWith(items) {
      const mounted = await mountView()
      mounted.store.list = makeShoppingList({ items })
      await mounted.wrapper.vm.$nextTick()
      return mounted
    }

    function serverHas(items) {
      api.get.mockResolvedValue({ data: makeShoppingList({ items }) })
    }

    async function wait(ms) {
      vi.advanceTimersByTime(ms)
      await flushPromises()
    }

    beforeEach(() => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
    })

    afterEach(() => {
      vi.useRealTimers()
      delete document.visibilityState
    })

    it('shows the other phone’s changes when the page becomes visible again', async () => {
      const { wrapper } = await mountWith([carrot()])
      serverHas([carrot(), bread()])

      setVisibility('visible')
      await flushPromises()

      expect(api.get).toHaveBeenCalledWith('/shopping-lists/active', expect.anything())
      expect(names(wrapper)).toEqual(['Carrot', 'Bread'])
    })

    it('refreshes when the window gains focus', async () => {
      const { wrapper } = await mountWith([carrot()])
      serverHas([carrot(), bread()])

      window.dispatchEvent(new Event('focus'))
      await flushPromises()

      expect(names(wrapper)).toEqual(['Carrot', 'Bread'])
    })

    it('does not refresh on a timer, on scroll, or when the page is hidden', async () => {
      await mountWith([carrot()])
      serverHas([carrot()])

      await wait(10 * 60_000)
      window.dispatchEvent(new Event('scroll'))
      document.dispatchEvent(new Event('scroll'))
      setVisibility('hidden')
      await flushPromises()

      expect(api.get).not.toHaveBeenCalled()
    })

    it('fails silently in the background and keeps the current list', async () => {
      const { wrapper } = await mountWith([carrot()])
      api.get.mockRejectedValue({ status: 500, message: 'Server error' })

      window.dispatchEvent(new Event('focus'))
      await flushPromises()

      expect(names(wrapper)).toEqual(['Carrot'])
      expect(useUiStore().snackbar.show).toBe(false)
      expect(wrapper.text()).not.toContain('Server error')
    })

    it('applies only the newest refresh when an older one answers later', async () => {
      const { wrapper } = await mountWith([carrot()])
      let answerOlder
      api.get
        .mockReturnValueOnce(new Promise(resolve => { answerOlder = resolve }))
        .mockResolvedValueOnce({ data: makeShoppingList({ items: [carrot(), bread()] }) })

      window.dispatchEvent(new Event('focus'))
      setVisibility('visible')
      await flushPromises()
      answerOlder({ data: makeShoppingList({ items: [carrot()] }) })
      await flushPromises()

      expect(names(wrapper)).toEqual(['Carrot', 'Bread'])
    })

    describe('after the user’s own writes', () => {
      function deferred() {
        let resolve, reject
        const promise = new Promise((res, rej) => { resolve = res; reject = rej })
        return { promise, resolve, reject }
      }

      it('refreshes once the Pending Changes have all been sent, not alongside them', async () => {
        const { wrapper } = await mountWith([carrot()])
        const save = deferred()
        api.put.mockReturnValue(save.promise)
        serverHas([carrot({ isChecked: true }), bread()])

        await wrapper.find('input[aria-label="Mark Carrot purchased"]').trigger('click')
        await flushPromises()
        expect(api.get).not.toHaveBeenCalled()

        save.resolve({ data: carrot({ isChecked: true }) })
        await flushPromises()
        expect(api.get).toHaveBeenCalledOnce()
      })

      it('does not refresh while a change is still waiting', async () => {
        const { wrapper } = await mountWith([carrot()])
        api.put.mockRejectedValue({ status: 500, message: 'Server error' })

        await wrapper.find('input[aria-label="Mark Carrot purchased"]').trigger('click')
        await flushPromises()

        expect(api.get).not.toHaveBeenCalled()
      })

      it('refreshes after adding a Custom Item', async () => {
        const { wrapper } = await mountWith([carrot()])
        api.post.mockResolvedValue({ data: bread() })
        serverHas([carrot(), bread()])

        sheet(wrapper).vm.$emit('confirm', { name: 'Bread', amount: null, unit: null, category: 'OTHER' })
        await flushPromises()

        expect(api.get).toHaveBeenCalledOnce()
      })

      it('refreshes after removing a Custom Item', async () => {
        const { wrapper } = await mountWith([carrot(), bread()])
        api.delete.mockResolvedValue({})
        serverHas([carrot()])

        await wrapper.find('button[aria-label="Remove Bread"]').trigger('click')
        await flushPromises()

        expect(api.get).toHaveBeenCalledOnce()
      })

      it('refreshes after editing a Custom Item', async () => {
        const { wrapper } = await mountWith([bread()])
        api.put.mockResolvedValue({ data: bread({ displayName: 'Rye bread' }) })
        serverHas([bread({ displayName: 'Rye bread' })])
        await holdToEdit(wrapper, 'Bread')

        sheet(wrapper).vm.$emit('save', { name: 'Rye bread', amount: null, unit: null, category: 'OTHER' })
        await flushPromises()

        expect(api.get).toHaveBeenCalledOnce()
      })

      it('keeps a just-ticked item ticked through a refresh during its 3 seconds', async () => {
        const { wrapper, store } = await mountWith([carrot()])
        api.put.mockResolvedValue({ data: carrot({ isChecked: true }) })
        serverHas([carrot({ isChecked: false })])

        await wrapper.find('input[aria-label="Mark Carrot purchased"]').trigger('click')
        await flushPromises()

        expect(api.get).toHaveBeenCalledOnce()
        expect(store.list.items[0].isChecked).toBe(true)
        expect(wrapper.find('.v-list-item-title').classes()).toContain('text-decoration-line-through')
      })
    })

    describe('pull to refresh', () => {
      async function pull(wrapper) {
        const done = vi.fn()
        wrapper.findComponent({ name: 'VPullToRefresh' }).vm.$emit('load', { done })
        await flushPromises()
        return done
      }

      it('applies at once, even straight after a touch, and then stops its spinner', async () => {
        const { wrapper } = await mountWith([carrot()])
        serverHas([carrot(), bread()])
        pointer('pointerdown')

        const done = await pull(wrapper)

        expect(names(wrapper)).toEqual(['Carrot', 'Bread'])
        expect(done).toHaveBeenCalledOnce()
      })

      it('still applies at once when a background refresh starts while it loads', async () => {
        const { wrapper } = await mountWith([carrot()])
        let answerPull
        api.get
          .mockReturnValueOnce(new Promise(resolve => { answerPull = resolve }))
          .mockReturnValueOnce(new Promise(() => {}))
        const done = vi.fn()
        wrapper.findComponent({ name: 'VPullToRefresh' }).vm.$emit('load', { done })
        pointer('pointerdown')
        pointer('pointerup')

        window.dispatchEvent(new Event('focus'))
        answerPull({ data: makeShoppingList({ items: [carrot(), bread()] }) })
        await flushPromises()

        expect(names(wrapper)).toEqual(['Carrot', 'Bread'])
        expect(done).toHaveBeenCalledOnce()
      })

      it('never replaces a newer list with an older pull that answers late', async () => {
        const { wrapper } = await mountWith([carrot()])
        let answerPull
        api.get
          .mockReturnValueOnce(new Promise(resolve => { answerPull = resolve }))
          .mockResolvedValueOnce({ data: makeShoppingList({ items: [carrot(), bread()] }) })
        wrapper.findComponent({ name: 'VPullToRefresh' }).vm.$emit('load', { done: vi.fn() })

        window.dispatchEvent(new Event('focus'))
        await flushPromises()
        answerPull({ data: makeShoppingList({ items: [carrot()] }) })
        await flushPromises()

        expect(names(wrapper)).toEqual(['Carrot', 'Bread'])
      })

      it('shows the error snackbar and keeps the current list when it fails', async () => {
        const { wrapper } = await mountWith([carrot()])
        api.get.mockRejectedValue({ status: 500, message: 'Server error' })

        const done = await pull(wrapper)

        expect(names(wrapper)).toEqual(['Carrot'])
        expect(useUiStore().snackbar).toMatchObject({ show: true, message: 'Server error', color: 'error' })
        expect(done).toHaveBeenCalledOnce()
      })
    })

    describe('while the edit sheet is open', () => {
      async function openEditor(items) {
        const mounted = await mountWith(items)
        await holdToEdit(mounted.wrapper, 'Bread')
        return mounted
      }

      it('waits, then applies when the sheet closes', async () => {
        const { wrapper } = await openEditor([carrot(), bread()])
        expect(sheet(wrapper).props('modelValue')).toBe(true)
        serverHas([carrot()])

        window.dispatchEvent(new Event('focus'))
        await wait(5000)
        expect(names(wrapper)).toEqual(['Carrot', 'Bread'])

        sheet(wrapper).vm.$emit('update:modelValue', false)
        await flushPromises()
        expect(names(wrapper)).toEqual(['Carrot'])
      })

      it('says the item was removed when the other phone removed it before the save', async () => {
        const { wrapper } = await openEditor([carrot(), bread()])
        api.put.mockRejectedValue({ status: 404, message: 'Not Found' })
        serverHas([carrot()])

        // The real sheet closes itself as it emits the save.
        sheet(wrapper).vm.$emit('save', { name: 'Rye bread', amount: null, unit: null, category: 'OTHER' })
        sheet(wrapper).vm.$emit('update:modelValue', false)
        await flushPromises()

        expect(useUiStore().snackbar).toMatchObject({ message: '“Bread” was removed from the list', color: 'error' })
        expect(names(wrapper)).toEqual(['Carrot'])
      })
    })

    describe('while the user touches the screen', () => {
      it('waits while a pointer is down, then applies 1 second after it lifts', async () => {
        const { wrapper } = await mountWith([carrot()])
        serverHas([carrot(), bread()])

        pointer('pointerdown')
        window.dispatchEvent(new Event('focus'))
        await wait(5000)
        expect(names(wrapper)).toEqual(['Carrot'])

        pointer('pointerup')
        await wait(999)
        expect(names(wrapper)).toEqual(['Carrot'])

        await wait(1)
        expect(names(wrapper)).toEqual(['Carrot', 'Bread'])
      })

      it('waits when the refresh arrives within 1 second of the last touch', async () => {
        const { wrapper } = await mountWith([carrot()])
        serverHas([carrot(), bread()])

        pointer('pointerdown')
        pointer('pointerup')
        await wait(500)
        window.dispatchEvent(new Event('focus'))
        await flushPromises()
        expect(names(wrapper)).toEqual(['Carrot'])

        await wait(500)
        expect(names(wrapper)).toEqual(['Carrot', 'Bread'])
      })

      it('treats a cancelled pointer, as when a scroll takes over, like a lifted one', async () => {
        const { wrapper } = await mountWith([carrot()])
        serverHas([carrot(), bread()])

        pointer('pointerdown')
        window.dispatchEvent(new Event('focus'))
        pointer('pointercancel')
        await wait(1000)

        expect(names(wrapper)).toEqual(['Carrot', 'Bread'])
      })

      it('forgets a pointer whose pointerup never came once the page is shown again', async () => {
        const { wrapper } = await mountWith([carrot()])
        serverHas([carrot(), bread()])

        pointer('pointerdown')
        setVisibility('hidden')
        setVisibility('visible')
        await flushPromises()

        expect(names(wrapper)).toEqual(['Carrot', 'Bread'])
      })

      it('waits until every pointer is up', async () => {
        const { wrapper } = await mountWith([carrot()])
        serverHas([carrot(), bread()])

        pointer('pointerdown', 1)
        pointer('pointerdown', 2)
        window.dispatchEvent(new Event('focus'))
        pointer('pointerup', 1)
        await wait(2000)
        expect(names(wrapper)).toEqual(['Carrot'])

        pointer('pointerup', 2)
        await wait(1000)
        expect(names(wrapper)).toEqual(['Carrot', 'Bread'])
      })
    })

    it('stops refreshing once the Shopping List is left', async () => {
      const { wrapper } = await mountWith([carrot()])
      wrapper.unmount()

      window.dispatchEvent(new Event('focus'))
      setVisibility('visible')
      await flushPromises()

      expect(api.get).not.toHaveBeenCalled()
    })
  })

  describe('offline', () => {
    const milk = (overrides = {}) => makeShoppingListItem({ id: 'milk', displayName: 'Milk', category: 'DAIRY', ...overrides })
    const eggs = () => makeShoppingListItem({ id: 'eggs', displayName: 'Eggs', category: 'DAIRY' })
    const noResponse = { status: undefined, message: 'Network Error' }
    const banner = wrapper => wrapper.find('[data-test="pending-banner"]')
    const regenerateButton = wrapper => wrapper.findAll('button').find(b => b.text().includes('Regenerate'))

    async function mountWith(items, overrides = {}) {
      const mounted = await mountView()
      mounted.store.list = makeShoppingList({ items, ...overrides })
      await flushPromises()
      return mounted
    }

    async function tick(wrapper, name) {
      await wrapper.find(`input[aria-label="Mark ${name} purchased"]`).trigger('click')
      await flushPromises()
    }

    it('shows no banner while online with nothing waiting', async () => {
      const { wrapper } = await mountWith([milk()])
      expect(banner(wrapper).exists()).toBe(false)
    })

    it('counts the changes waiting while offline', async () => {
      const { wrapper } = await mountWith([milk(), eggs()])
      api.put.mockRejectedValue(noResponse)

      await tick(wrapper, 'Milk')
      expect(banner(wrapper).text()).toBe('Offline · 1 change waiting')
      await tick(wrapper, 'Eggs')
      expect(banner(wrapper).text()).toBe('Offline · 2 changes waiting')
    })

    it('says it is saving while online and sending, and clears once the queue is empty', async () => {
      const { wrapper } = await mountWith([milk(), eggs()])
      let answer
      api.put.mockReturnValue(new Promise(resolve => { answer = resolve }))
      api.get.mockResolvedValue({ data: makeShoppingList({ items: [milk(), eggs()] }) })

      await tick(wrapper, 'Milk')
      expect(banner(wrapper).text()).toBe('Saving 1 change…')

      answer({ data: milk({ isChecked: true }) })
      await flushPromises()
      expect(banner(wrapper).exists()).toBe(false)
    })

    it('says offline on the browser’s offline event even with nothing waiting', async () => {
      const { wrapper, store } = await mountWith([milk()])
      store.markOffline()
      await flushPromises()
      expect(banner(wrapper).text()).toBe('Offline')
    })

    it('disables Regenerate while offline, and while changes are waiting', async () => {
      const { wrapper, store } = await mountWith([milk()], { isStale: true })
      expect(regenerateButton(wrapper).attributes('disabled')).toBeUndefined()

      store.markOffline()
      await flushPromises()
      expect(regenerateButton(wrapper).attributes('disabled')).toBeDefined()

      store.offline = false
      api.put.mockRejectedValue({ status: 500, message: 'Server error' })
      await tick(wrapper, 'Milk')
      expect(store.offline).toBe(false)
      expect(regenerateButton(wrapper).attributes('disabled')).toBeDefined()
    })

    it('says so when offline and this phone has never saved the list', async () => {
      const { useShoppingListStore } = await import('@/stores/shoppingList')
      const store = useShoppingListStore()
      vi.spyOn(store, 'fetchActive').mockImplementation(async () => { store.markOffline() })
      const wrapper = mount(ShoppingView, {
        global: { plugins: [vuetify, pinia], stubs: { teleport: true, CustomItemSheet: SheetStub } },
      })
      await flushPromises()

      expect(wrapper.text()).toContain('You’re offline, and this list hasn’t been saved on this phone yet.')
      expect(wrapper.text()).not.toContain('Create a meal plan')
    })

    it('shows the snapshot saved on this phone at once, then refreshes it', async () => {
      const { writeSnapshot } = await import('@/utils/shoppingListCache')
      await writeSnapshot(makeShoppingList({ items: [milk()] }))
      api.get.mockResolvedValue({ data: makeShoppingList({ items: [milk(), eggs()] }) })

      const { wrapper, store } = await mountView()

      expect(store.fetchActive).not.toHaveBeenCalled()
      expect(api.get).toHaveBeenCalledOnce()
      expect(wrapper.text()).toContain('Eggs')
    })
  })

  describe('Aisle Order', () => {
    const groupHeadings = wrapper => wrapper.findAll('.text-overline').map(h => h.text())

    it('orders groups by Aisle Order on a fresh load, whatever order the items arrive in', async () => {
      const { wrapper, store } = await mountView()
      // A Custom Item alone in its Category arrives with DisplayOrder 1, ahead of Generated Items.
      store.list = makeShoppingList({
        items: [
          makeShoppingListItem({ id: 'milk',  displayName: 'Milk',  category: 'DAIRY',     displayOrder: 1 }),
          makeShoppingListItem({ id: 'soap',  displayName: 'Soap',  category: 'HOUSEHOLD', displayOrder: 1, isCustom: true }),
          makeShoppingListItem({ id: 'apple', displayName: 'Apple', category: 'PRODUCE',   displayOrder: 2, isCustom: true }),
          makeShoppingListItem({ id: 'bread', displayName: 'Bread', category: 'BAKERY',    displayOrder: 2 }),
        ],
      })
      await wrapper.vm.$nextTick()

      expect(groupHeadings(wrapper)).toEqual(['🥦 Produce', '🥛 Dairy', '🍞 Bakery', '🧻 Household'])
    })

    it('puts an unknown Category after every known one', async () => {
      const { wrapper, store } = await mountView()
      store.list = makeShoppingList({
        items: [
          makeShoppingListItem({ id: 'x', displayName: 'Mystery', category: 'UNKNOWN_AISLE' }),
          makeShoppingListItem({ id: 'o', displayName: 'Misc',    category: 'OTHER' }),
        ],
      })
      await wrapper.vm.$nextTick()

      expect(groupHeadings(wrapper)).toEqual(['📦 Other', 'UNKNOWN_AISLE'])
    })

    it('shows an added Custom Item in an otherwise empty Category in Aisle Order', async () => {
      const { wrapper, store } = await mountView()
      store.list = makeShoppingList({
        items: [makeShoppingListItem({ id: 'milk', displayName: 'Milk', category: 'DAIRY' })],
      })
      await wrapper.vm.$nextTick()
      api.post.mockResolvedValue({
        data: makeShoppingListItem({ id: 'apple', displayName: 'Apple', category: 'PRODUCE', isCustom: true }),
      })

      wrapper.findComponent(SheetStub).vm.$emit('confirm', { name: 'Apple', amount: null, unit: null, category: 'PRODUCE' })
      await flushPromises()

      expect(groupHeadings(wrapper)).toEqual(['🥦 Produce', '🥛 Dairy'])
    })
  })

  describe('editing a Custom Item', () => {
    const eggs   = () => makeShoppingListItem({ id: 'eggs',   displayName: 'Eggs',   category: 'OTHER',   isCustom: true, amount: 6, unit: 'pcs' })
    const carrot = () => makeShoppingListItem({ id: 'carrot', displayName: 'Carrot', category: 'PRODUCE' })

    const sheet = wrapper => wrapper.findComponent(SheetStub)
    const row   = (wrapper, name) => wrapper.findAll('.v-list-item').find(r => r.text().includes(name))

    const pointer = (overrides = {}) =>
      ({ pointerType: 'touch', isPrimary: true, button: 0, clientX: 100, clientY: 100, ...overrides })

    function contextMenu(element, pointerType) {
      const event = new MouseEvent('contextmenu', { bubbles: true, cancelable: true })
      Object.defineProperty(event, 'pointerType', { value: pointerType })
      element.dispatchEvent(event)
      return event
    }

    async function mountWith(items, options) {
      const mounted = await mountView(options)
      mounted.store.list = makeShoppingList({ items })
      await mounted.wrapper.vm.$nextTick()
      return mounted
    }

    async function hold(target, ms = 1000) {
      await target.trigger('pointerdown', pointer())
      vi.advanceTimersByTime(ms)
      await flushPromises()
    }

    beforeEach(() => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
    })

    afterEach(() => {
      vi.useRealTimers()
    })

    it('opens the sheet with the item after a 1-second hold', async () => {
      const { wrapper } = await mountWith([carrot(), eggs()])

      await hold(row(wrapper, 'Eggs').find('.v-list-item-title'), 999)
      expect(sheet(wrapper).props('modelValue')).toBe(false)

      vi.advanceTimersByTime(1)
      await flushPromises()
      expect(sheet(wrapper).props('modelValue')).toBe(true)
      expect(sheet(wrapper).props('item')).toEqual(eggs())
    })

    it('vibrates when the hold opens the sheet', async () => {
      const vibrate = vi.fn()
      Object.defineProperty(navigator, 'vibrate', { value: vibrate, configurable: true })
      try {
        const { wrapper } = await mountWith([eggs()])

        await hold(row(wrapper, 'Eggs'))

        expect(vibrate).toHaveBeenCalledWith(50)
      } finally {
        delete navigator.vibrate
      }
    })

    it.each([
      ['letting go', target => target.trigger('pointerup', pointer())],
      ['a pointercancel', target => target.trigger('pointercancel', pointer())],
      ['moving more than 10 px', target => target.trigger('pointermove', pointer({ clientX: 108, clientY: 108 }))],
    ])('does not open the sheet after %s before 1 second', async (_, interrupt) => {
      const { wrapper } = await mountWith([eggs()])
      const target = row(wrapper, 'Eggs')

      await target.trigger('pointerdown', pointer())
      vi.advanceTimersByTime(500)
      await interrupt(target)
      vi.advanceTimersByTime(1000)
      await flushPromises()

      expect(sheet(wrapper).props('modelValue')).toBe(false)
    })

    it('still opens the sheet after moving 10 px or less', async () => {
      const { wrapper } = await mountWith([eggs()])
      const target = row(wrapper, 'Eggs')

      await target.trigger('pointerdown', pointer())
      await target.trigger('pointermove', pointer({ clientX: 106, clientY: 108 }))
      vi.advanceTimersByTime(1000)
      await flushPromises()

      expect(sheet(wrapper).props('modelValue')).toBe(true)
    })

    it('opens the sheet from a held left mouse button but not the right', async () => {
      const { wrapper } = await mountWith([eggs()])

      await row(wrapper, 'Eggs').trigger('pointerdown', pointer({ pointerType: 'mouse', button: 2 }))
      vi.advanceTimersByTime(1000)
      await flushPromises()
      expect(sheet(wrapper).props('modelValue')).toBe(false)

      await row(wrapper, 'Eggs').trigger('pointerdown', pointer({ pointerType: 'mouse', button: 0 }))
      vi.advanceTimersByTime(1000)
      await flushPromises()
      expect(sheet(wrapper).props('modelValue')).toBe(true)
    })

    it.each([
      ['Purchased checkbox', 'input[aria-label="Mark Eggs purchased"]'],
      ['remove button', 'button[aria-label="Remove Eggs"]'],
    ])('does not open the sheet from a hold on the %s', async (_, selector) => {
      const { wrapper } = await mountWith([eggs()])

      await hold(wrapper.find(selector))

      expect(sheet(wrapper).props('modelValue')).toBe(false)
    })

    it('does nothing when a Generated Item is held', async () => {
      const { wrapper } = await mountWith([carrot()])

      await hold(row(wrapper, 'Carrot'))

      expect(sheet(wrapper).props('modelValue')).toBe(false)
    })

    it('edits a Purchased Custom Item while purchased items are shown', async () => {
      const { wrapper } = await mountWith([{ ...eggs(), isChecked: true }])
      await wrapper.findAll('button').find(b => b.text().includes('Show purchased')).trigger('click')

      await hold(row(wrapper, 'Eggs'))

      expect(sheet(wrapper).props('item')).toMatchObject({ id: 'eggs', isChecked: true })
    })

    it('swallows the click that follows a completed hold, and only that click', async () => {
      const { wrapper, store } = await mountWith([eggs()], { attachTo: document.body })
      vi.spyOn(store, 'toggleItem').mockResolvedValue(undefined)
      const onDocumentClick = vi.fn()
      document.addEventListener('click', onDocumentClick)
      try {
        const target = row(wrapper, 'Eggs')
        await hold(target)
        await target.trigger('pointerup', pointer())
        await wrapper.find('input[aria-label="Mark Eggs purchased"]').trigger('click')
        expect(onDocumentClick).not.toHaveBeenCalled()
        expect(store.toggleItem).not.toHaveBeenCalled()

        await target.trigger('pointerdown', pointer())
        await target.trigger('pointerup', pointer())
        await target.trigger('click')
        expect(onDocumentClick).toHaveBeenCalledOnce()
      } finally {
        document.removeEventListener('click', onDocumentClick)
      }
    })

    it('opens the sheet straight away on a mouse right-click on a Custom Item', async () => {
      const { wrapper } = await mountWith([eggs()])

      const event = contextMenu(row(wrapper, 'Eggs').element, 'mouse')
      await flushPromises()

      expect(event.defaultPrevented).toBe(true)
      expect(sheet(wrapper).props('modelValue')).toBe(true)
      expect(sheet(wrapper).props('item')).toEqual(eggs())
    })

    it('prevents and ignores a touch contextmenu on a Custom Item', async () => {
      const { wrapper } = await mountWith([eggs()])

      const event = contextMenu(row(wrapper, 'Eggs').element, 'touch')
      await flushPromises()

      expect(event.defaultPrevented).toBe(true)
      expect(sheet(wrapper).props('modelValue')).toBe(false)
    })

    it('leaves the browser menu alone on a Generated Item', async () => {
      const { wrapper } = await mountWith([carrot()])

      const event = contextMenu(row(wrapper, 'Carrot').element, 'mouse')
      await flushPromises()

      expect(event.defaultPrevented).toBe(false)
      expect(sheet(wrapper).props('modelValue')).toBe(false)
    })

    describe('saving', () => {
      const edit = { name: 'Duck eggs', amount: 12, unit: 'each', category: 'PRODUCE' }

      async function openEditor(items = [carrot(), makeShoppingListItem({ id: 'milk', displayName: 'Milk', category: 'DAIRY' }), eggs()]) {
        const mounted = await mountWith(items)
        await hold(row(mounted.wrapper, 'Eggs'))
        return mounted
      }

      it('saves and shows the row in its new Category\'s aisle position', async () => {
        const { wrapper } = await openEditor()
        api.put.mockResolvedValue({ data: { ...eggs(), displayName: 'Duck eggs', category: 'PRODUCE', amount: 12, unit: 'each' } })

        sheet(wrapper).vm.$emit('save', edit)
        await flushPromises()

        expect(api.put).toHaveBeenCalledWith('/shopping-lists/list-1/custom-items/eggs', edit, expect.anything())
        expect(useUiStore().snackbar).toMatchObject({ message: 'Saved “Duck eggs”', color: 'success' })
        const groups = wrapper.findAll('.v-card').map(card => card.findAll('.v-list-item-title').map(t => t.text()))
        expect(groups).toEqual([['Carrot', 'Duck eggs'], ['Milk']])
      })

      it('shows the edit at once, before the server answers', async () => {
        const { wrapper } = await openEditor()
        api.put.mockReturnValue(new Promise(() => {}))

        sheet(wrapper).vm.$emit('save', edit)
        await flushPromises()

        expect(wrapper.text()).toContain('Duck eggs')
        expect(useUiStore().snackbar).toMatchObject({ message: 'Saved “Duck eggs”', color: 'success' })
      })

      it('keeps the edit on screen, waiting, when it gets no response', async () => {
        const { wrapper, store } = await openEditor()
        api.put.mockRejectedValue({ status: undefined, message: 'Network Error' })

        sheet(wrapper).vm.$emit('save', edit)
        await flushPromises()

        expect(store.list.items.find(i => i.id === 'eggs')).toMatchObject({ displayName: 'Duck eggs' })
        expect(wrapper.text()).toContain('Offline · 1 change waiting')
      })
    })
  })
})
