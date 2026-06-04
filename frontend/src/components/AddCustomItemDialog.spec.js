import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import AddCustomItemDialog from './AddCustomItemDialog.vue'

vi.mock('@/services/api', () => ({
  default: { get: vi.fn() },
}))

const vuetify = createVuetify({ components, directives })

// Stub VBottomSheet to avoid teleport DOM isolation issues
const stubBottomSheet = {
  props: ['modelValue'],
  template: '<div v-if="modelValue"><slot /></div>',
  emits: ['update:modelValue'],
}

function mountDialog(modelValue = true) {
  return mount(AddCustomItemDialog, {
    props: { modelValue },
    global: {
      plugins: [vuetify],
      stubs: { VBottomSheet: stubBottomSheet, teleport: true },
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
