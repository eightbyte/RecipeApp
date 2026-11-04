import { mount, flushPromises, DOMWrapper } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import AddCustomItemDialog from './AddCustomItemDialog.vue'
import { makeIngredient } from '@/test/factories'
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
  props: ['modelValue'],
  template: '<div v-if="modelValue"><slot /></div>',
  emits: ['update:modelValue'],
}

function mountDialog(modelValue = true) {
  return mount(AddCustomItemDialog, {
    props: { modelValue },
    attachTo: document.body,
    global: {
      plugins: [vuetify],
      stubs: { VBottomSheet: stubBottomSheet },
    },
  })
}

describe('AddCustomItemDialog', () => {
  it('renders the item name field', async () => {
    const wrapper = mountDialog()
    await flushPromises()
    expect(wrapper.text()).toContain('Item name')
  })

  it('renders the category selector', async () => {
    const wrapper = mountDialog()
    await flushPromises()
    expect(wrapper.text()).toContain('Category')
  })

  it('Add button is disabled when name is empty', async () => {
    const wrapper = mountDialog()
    await flushPromises()

    const addButton = wrapper.findAll('button').find(b => b.text() === 'Add')
    expect(addButton?.attributes('disabled')).toBeDefined()
  })

  it('emits confirm with payload when Add is clicked with a name', async () => {
    const wrapper = mountDialog()
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
    const wrapper = mountDialog()
    await flushPromises()

    const cancelBtn = wrapper.findAll('button').find(b => b.text() === 'Cancel')
    await cancelBtn?.trigger('click')

    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual([false])
  })

  it('lists DisplayName and alias rows for the typed text, ranked', async () => {
    const wrapper = mountDialog()
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
    const wrapper = mountDialog()
    await flushPromises()

    await typeName(wrapper, ' f ')

    expect(suggestions()).toEqual([])
  })

  it('sets the Category from a picked suggestion', async () => {
    const wrapper = mountDialog()
    await flushPromises()

    await typeName(wrapper, 'bread')
    await pickSuggestion('Bread Flour')

    expect(await confirmedItem(wrapper)).toMatchObject({ name: 'Bread Flour', category: 'BAKING_SPICES' })
  })

  it('leaves an unknown name at Other', async () => {
    const wrapper = mountDialog()
    await flushPromises()

    await typeName(wrapper, 'Dish soap')

    expect(await confirmedItem(wrapper)).toMatchObject({ name: 'Dish soap', category: 'OTHER' })
  })

  it('preselects nothing from typing alone, even an exact match', async () => {
    const wrapper = mountDialog()
    await flushPromises()

    await typeName(wrapper, 'Bread Flour')

    expect(await confirmedItem(wrapper)).toMatchObject({ name: 'Bread Flour', category: 'OTHER' })
  })

  it('keeps a hand-picked Category through further typing', async () => {
    const wrapper = mountDialog()
    await flushPromises()

    await chooseCategory(wrapper, 'DAIRY')
    await typeName(wrapper, 'Bread Flour')

    expect(await confirmedItem(wrapper)).toMatchObject({ category: 'DAIRY' })
  })

  it('overrides a hand-picked Category when a suggestion is picked', async () => {
    const wrapper = mountDialog()
    await flushPromises()

    await chooseCategory(wrapper, 'DAIRY')
    await typeName(wrapper, 'bread')
    await pickSuggestion('Bread Flour')

    expect(await confirmedItem(wrapper)).toMatchObject({ category: 'BAKING_SPICES' })
  })

  it('still adds typed text silently when the catalogue fails to load, and retries on the next open', async () => {
    api.get.mockRejectedValue(new Error('offline'))
    const wrapper = mountDialog()
    await flushPromises()

    await typeName(wrapper, 'flour')
    expect(suggestions()).toEqual([])
    expect(await confirmedItem(wrapper)).toMatchObject({ name: 'flour', category: 'OTHER' })

    await wrapper.setProps({ modelValue: false })
    await wrapper.setProps({ modelValue: true })
    expect(api.get).toHaveBeenCalledTimes(2)
  })

  it('resets form when dialog reopens', async () => {
    const wrapper = mountDialog()
    await flushPromises()

    wrapper.vm.form.name = 'Old Name'
    await wrapper.setProps({ modelValue: false })
    await wrapper.setProps({ modelValue: true })
    await wrapper.vm.$nextTick()

    expect(wrapper.vm.form.name).toBe('')
  })
})
