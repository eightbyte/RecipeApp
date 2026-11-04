import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import ShoppingView from './ShoppingView.vue'
import { makeShoppingList, makeShoppingListItem } from '@/test/factories'
import { useUiStore } from '@/stores/ui'
import api from '@/services/api'

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

const SheetStub = {
  name: 'CustomItemSheet',
  props: { modelValue: Boolean, item: { type: Object, default: null }, saving: Boolean },
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

      it('saves, closes the sheet, and shows the row in its new Category\'s aisle position', async () => {
        const { wrapper } = await openEditor()
        api.put.mockResolvedValue({ data: { ...eggs(), displayName: 'Duck eggs', category: 'PRODUCE', amount: 12, unit: 'each' } })

        sheet(wrapper).vm.$emit('save', edit)
        await flushPromises()

        expect(api.put).toHaveBeenCalledWith('/shopping-lists/list-1/custom-items/eggs', edit, expect.anything())
        expect(sheet(wrapper).props('modelValue')).toBe(false)
        expect(useUiStore().snackbar).toMatchObject({ message: 'Saved “Duck eggs”', color: 'success' })
        const groups = wrapper.findAll('.v-card').map(card => card.findAll('.v-list-item-title').map(t => t.text()))
        expect(groups).toEqual([['Carrot', 'Duck eggs'], ['Milk']])
      })

      it('shows the sheet as saving until the server answers', async () => {
        const { wrapper } = await openEditor()
        let answer
        api.put.mockReturnValue(new Promise(resolve => { answer = resolve }))

        sheet(wrapper).vm.$emit('save', edit)
        await flushPromises()
        expect(sheet(wrapper).props('saving')).toBe(true)
        expect(sheet(wrapper).props('modelValue')).toBe(true)

        answer({ data: { ...eggs(), displayName: 'Duck eggs' } })
        await flushPromises()
        expect(sheet(wrapper).props('saving')).toBe(false)
      })

      it('keeps the sheet open and the item unchanged when the save fails', async () => {
        const { wrapper, store } = await openEditor()
        api.put.mockRejectedValue({ status: 500, message: 'Server error', timedOut: false })

        sheet(wrapper).vm.$emit('save', edit)
        await flushPromises()

        expect(sheet(wrapper).props('modelValue')).toBe(true)
        expect(sheet(wrapper).props('saving')).toBe(false)
        expect(useUiStore().snackbar).toMatchObject({ message: 'Server error', color: 'error' })
        expect(store.list.items.find(i => i.id === 'eggs')).toEqual(eggs())
      })

      it('says the save timed out, keeps the sheet open, and does not refetch', async () => {
        const { wrapper, store } = await openEditor()
        api.put.mockRejectedValue({ message: 'timeout of 10000ms exceeded', timedOut: true })

        sheet(wrapper).vm.$emit('save', edit)
        await flushPromises()

        expect(sheet(wrapper).props('modelValue')).toBe(true)
        expect(useUiStore().snackbar).toMatchObject({ message: 'Saving “Eggs” timed out. Try again.', color: 'error' })
        expect(store.list.items.find(i => i.id === 'eggs')).toEqual(eggs())
        expect(store.fetchActive).toHaveBeenCalledOnce()
      })
    })
  })
})
