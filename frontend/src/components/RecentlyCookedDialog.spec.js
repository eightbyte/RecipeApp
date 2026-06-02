import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import RecentlyCookedDialog from './RecentlyCookedDialog.vue'

const vuetify = createVuetify({ components, directives })

// Stub VBottomSheet so its slot renders inline (avoids teleport DOM isolation issues)
const stubBottomSheet = {
  props: ['modelValue'],
  template: '<div v-if="modelValue"><slot /></div>',
  emits: ['update:modelValue'],
}

function mountDialog(props = {}) {
  return mount(RecentlyCookedDialog, {
    props: { modelValue: true, recipeName: 'Pasta Bolognese', ...props },
    global: {
      plugins: [vuetify],
      stubs: { VBottomSheet: stubBottomSheet, teleport: true },
    },
  })
}

describe('RecentlyCookedDialog', () => {
  it('renders the recipe name in body text', () => {
    const wrapper = mountDialog({ recipeName: 'Pasta Bolognese' })
    expect(wrapper.text()).toContain('Pasta Bolognese')
  })

  it('emits confirm when Add Anyway is clicked', async () => {
    const wrapper = mountDialog()
    const btn = wrapper.findAll('button').find(b => b.text().includes('Add Anyway'))
    await btn?.trigger('click')
    expect(wrapper.emitted('confirm')).toBeTruthy()
  })

  it('emits cancel when Cancel is clicked', async () => {
    const wrapper = mountDialog()
    const btn = wrapper.findAll('button').find(b => b.text().includes('Cancel'))
    await btn?.trigger('click')
    expect(wrapper.emitted('cancel')).toBeTruthy()
  })
})
