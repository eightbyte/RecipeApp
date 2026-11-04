import { mount, flushPromises, DOMWrapper } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import CustomItemSheet from './CustomItemSheet.vue'
import { makeIngredient, makeShoppingListItem } from '@/test/factories'
import api from '@/services/api'

vi.mock('@/services/api', () => ({
  default: { get: vi.fn() },
}))

const vuetify = createVuetify({ components, directives })

// Listed out of order so the ranking has to come from the dialog.
const catalogue = [
  makeIngredient({ id: 'sugar', displayName: 'Sugar', category: 'BAKING_SPICES', aliases: ['white sugar'] }),
  makeIngredient({ id: 'bread', displayName: 'Bread Flour', category: 'BAKING_SPICES', aliases: ['strong flour'] }),
  makeIngredient({ id: 'tortillas', displayName: 'Flour Tortillas', category: 'BAKERY', aliases: ['flour wraps'] }),
  makeIngredient({ id: 'ap', displayName: 'All-Purpose Flour', category: 'BAKING_SPICES', aliases: ['plain flour', 'flour'] }),
]

beforeEach(() => {
  setActivePinia(createPinia())
  vi.clearAllMocks()
  api.get.mockResolvedValue({ data: catalogue })
})

// Vuetify teleports the suggestion menu to the document body.
const page = () => new DOMWrapper(document.body)

// The combobox opens its menu only while focused, which takes a real focus on an attached input.
async function typeName(wrapper, text) {
  const input = wrapper.find('input')
  input.element.focus()
  await input.setValue(text)
  await flushPromises()
}

async function pickSuggestion(title) {
  await page().findAll('.v-list-item').find(item => item.find('.v-list-item-title').text() === title).trigger('click')
  await flushPromises()
}

async function chooseCategory(wrapper, category) {
  await wrapper.findComponent({ name: 'VSelect' }).setValue(category)
}

async function confirmedItem(wrapper) {
  await wrapper.findAll('button').find(b => b.text() === 'Add').trigger('click')
  return wrapper.emitted('confirm')[0][0]
}

function suggestions() {
  return page().findAll('.v-list-item').map(item => [
    item.find('.v-list-item-title').text(),
    item.find('.v-list-item-subtitle').text(),
  ])
}

// Stub VBottomSheet to avoid teleport DOM isolation issues
const stubBottomSheet = {
  props: ['modelValue', 'persistent'],
  template: '<div v-if="modelValue" :data-persistent="String(!!persistent)"><slot /></div>',
  emits: ['update:modelValue'],
}

function mountSheet(modelValue = true, props = {}) {
  return mount(CustomItemSheet, {
    props: { modelValue, ...props },
    attachTo: document.body,
    global: {
      plugins: [vuetify],
      stubs: { VBottomSheet: stubBottomSheet },
    },
  })
}

describe('CustomItemSheet', () => {
  it('renders the item name field', async () => {
    const wrapper = mountSheet()
    await flushPromises()
    expect(wrapper.text()).toContain('Item name')
  })

  it('renders the category selector', async () => {
    const wrapper = mountSheet()
    await flushPromises()
    expect(wrapper.text()).toContain('Category')
  })

  it('Add button is disabled when name is empty', async () => {
    const wrapper = mountSheet()
    await flushPromises()

    const addButton = wrapper.findAll('button').find(b => b.text() === 'Add')
    expect(addButton?.attributes('disabled')).toBeDefined()
  })

  it('emits confirm with payload when Add is clicked with a name', async () => {
    const wrapper = mountSheet()
    await flushPromises()

    // Directly set form fields via the component instance
    wrapper.vm.form.name = 'Eggs'
    wrapper.vm.form.amount = 6
    wrapper.vm.form.unit = 'pcs'
    wrapper.vm.form.category = 'DAIRY'
    await wrapper.vm.$nextTick()

    const addButton = wrapper.findAll('button').find(b => b.text() === 'Add')
    await addButton?.trigger('click')

    const emitted = wrapper.emitted('confirm')
    expect(emitted).toBeTruthy()
    expect(emitted[0][0]).toEqual({
      name: 'Eggs',
      amount: 6,
      unit: 'pcs',
      category: 'DAIRY',
    })
  })

  it('emits update:modelValue false when Cancel is clicked', async () => {
    const wrapper = mountSheet()
    await flushPromises()

    const cancelBtn = wrapper.findAll('button').find(b => b.text() === 'Cancel')
    await cancelBtn?.trigger('click')

    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual([false])
  })

  it('lists DisplayName and alias rows for the typed text, ranked', async () => {
    const wrapper = mountSheet()
    await flushPromises()

    await typeName(wrapper, 'Flour')
    expect(suggestions()).toEqual([
      ['flour',             'All-Purpose Flour · Baking & Spices'],
      ['Flour Tortillas',   'Bakery'],
      ['flour wraps',       'Flour Tortillas · Bakery'],
      ['All-Purpose Flour', 'Baking & Spices'],
      ['Bread Flour',       'Baking & Spices'],
      ['plain flour',       'All-Purpose Flour · Baking & Spices'],
      ['strong flour',      'Bread Flour · Baking & Spices'],
    ])
  })

  it('shows no suggestions for fewer than 2 typed characters', async () => {
    const wrapper = mountSheet()
    await flushPromises()

    await typeName(wrapper, ' f ')

    expect(suggestions()).toEqual([])
  })

  it('sets the Category from a picked suggestion', async () => {
    const wrapper = mountSheet()
    await flushPromises()

    await typeName(wrapper, 'bread')
    await pickSuggestion('Bread Flour')

    expect(await confirmedItem(wrapper)).toMatchObject({ name: 'Bread Flour', category: 'BAKING_SPICES' })
  })

  it('leaves an unknown name at Other', async () => {
    const wrapper = mountSheet()
    await flushPromises()

    await typeName(wrapper, 'Dish soap')

    expect(await confirmedItem(wrapper)).toMatchObject({ name: 'Dish soap', category: 'OTHER' })
  })

  it('preselects nothing from typing alone, even an exact match', async () => {
    const wrapper = mountSheet()
    await flushPromises()

    await typeName(wrapper, 'Bread Flour')

    expect(await confirmedItem(wrapper)).toMatchObject({ name: 'Bread Flour', category: 'OTHER' })
  })

  it('keeps a hand-picked Category through further typing', async () => {
    const wrapper = mountSheet()
    await flushPromises()

    await chooseCategory(wrapper, 'DAIRY')
    await typeName(wrapper, 'Bread Flour')

    expect(await confirmedItem(wrapper)).toMatchObject({ category: 'DAIRY' })
  })

  it('overrides a hand-picked Category when a suggestion is picked', async () => {
    const wrapper = mountSheet()
    await flushPromises()

    await chooseCategory(wrapper, 'DAIRY')
    await typeName(wrapper, 'bread')
    await pickSuggestion('Bread Flour')

    expect(await confirmedItem(wrapper)).toMatchObject({ category: 'BAKING_SPICES' })
  })

  it('still adds typed text silently when the catalogue fails to load, and retries on the next open', async () => {
    api.get.mockRejectedValue(new Error('offline'))
    const wrapper = mountSheet()
    await flushPromises()

    await typeName(wrapper, 'flour')
    expect(suggestions()).toEqual([])
    expect(await confirmedItem(wrapper)).toMatchObject({ name: 'flour', category: 'OTHER' })

    await wrapper.setProps({ modelValue: false })
    await wrapper.setProps({ modelValue: true })
    expect(api.get).toHaveBeenCalledTimes(2)
  })

  describe('edit mode', () => {
    const eggs = makeShoppingListItem({
      id: 'eggs', displayName: 'Eggs', amount: 6, unit: 'pcs', category: 'DAIRY', isCustom: true,
    })

    const button = (wrapper, label) => wrapper.findAll('button').find(b => b.text() === label)

    async function mountEditor(props = {}) {
      const wrapper = mountSheet(true, { item: eggs, ...props })
      await flushPromises()
      return wrapper
    }

    function fieldValues(wrapper) {
      return {
        name:     wrapper.find('input').element.value,
        amount:   wrapper.find('input[type="number"]').element.value,
        unit:     wrapper.find('input[placeholder="g, ml, pcs…"]').element.value,
        category: wrapper.findComponent({ name: 'VSelect' }).props('modelValue'),
      }
    }

    it('opens filled in with the item, titled Edit item, with a Save button', async () => {
      const wrapper = await mountEditor()

      expect(wrapper.text()).toContain('Edit item')
      expect(button(wrapper, 'Save')).toBeTruthy()
      expect(button(wrapper, 'Add')).toBeUndefined()
      expect(fieldValues(wrapper)).toEqual({ name: 'Eggs', amount: '6', unit: 'pcs', category: 'DAIRY' })
    })

    it('emits save with every field and stays open', async () => {
      const wrapper = await mountEditor()

      await typeName(wrapper, 'Duck eggs')
      await wrapper.find('input[type="number"]').setValue('12')
      await wrapper.find('input[placeholder="g, ml, pcs…"]').setValue('each')
      await chooseCategory(wrapper, 'PRODUCE')
      await button(wrapper, 'Save').trigger('click')

      expect(wrapper.emitted('save')[0][0]).toEqual({ name: 'Duck eggs', amount: 12, unit: 'each', category: 'PRODUCE' })
      expect(wrapper.emitted('confirm')).toBeUndefined()
      expect(wrapper.emitted('update:modelValue')).toBeUndefined()
    })

    it('saves an emptied amount and unit as empty', async () => {
      const wrapper = await mountEditor()

      await wrapper.find('input[type="number"]').setValue('')
      await wrapper.find('input[placeholder="g, ml, pcs…"]').setValue('')
      await button(wrapper, 'Save').trigger('click')

      expect(wrapper.emitted('save')[0][0]).toMatchObject({ amount: null, unit: null })
    })

    it('opens an item without a quantity with empty Amount and Unit', async () => {
      const wrapper = await mountEditor({ item: { ...eggs, amount: null, unit: null } })

      expect(fieldValues(wrapper)).toMatchObject({ amount: '', unit: '' })
    })

    it('sets only the name from a picked suggestion', async () => {
      const wrapper = await mountEditor()

      await typeName(wrapper, 'bread')
      await pickSuggestion('Bread Flour')
      await button(wrapper, 'Save').trigger('click')

      expect(wrapper.emitted('save')[0][0]).toMatchObject({ name: 'Bread Flour', category: 'DAIRY' })
    })

    it('locks the sheet while saving', async () => {
      const wrapper = await mountEditor({ saving: true })

      expect(button(wrapper, 'Save').classes()).toContain('v-btn--loading')
      expect(button(wrapper, 'Cancel').attributes('disabled')).toBeDefined()
      expect(wrapper.find('[data-persistent]').attributes('data-persistent')).toBe('true')
    })

    it('can be dismissed when not saving', async () => {
      const wrapper = await mountEditor()

      expect(button(wrapper, 'Cancel').attributes('disabled')).toBeUndefined()
      expect(wrapper.find('[data-persistent]').attributes('data-persistent')).toBe('false')
    })

    it('keeps the edits when a save settles without closing the sheet', async () => {
      const wrapper = await mountEditor()

      await typeName(wrapper, 'Duck eggs')
      await button(wrapper, 'Save').trigger('click')
      await wrapper.setProps({ saving: true })
      await wrapper.setProps({ saving: false })

      expect(fieldValues(wrapper).name).toBe('Duck eggs')
    })

    it('discards the edits on Cancel', async () => {
      const wrapper = await mountEditor()

      await typeName(wrapper, 'Duck eggs')
      await button(wrapper, 'Cancel').trigger('click')
      expect(wrapper.emitted('update:modelValue')[0]).toEqual([false])

      await wrapper.setProps({ modelValue: false })
      await wrapper.setProps({ modelValue: true })
      await flushPromises()
      expect(fieldValues(wrapper).name).toBe('Eggs')
    })
  })

  it('resets form when dialog reopens', async () => {
    const wrapper = mountSheet()
    await flushPromises()

    wrapper.vm.form.name = 'Old Name'
    await wrapper.setProps({ modelValue: false })
    await wrapper.setProps({ modelValue: true })
    await wrapper.vm.$nextTick()

    expect(wrapper.vm.form.name).toBe('')
  })
})
