import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import EmptyState from './EmptyState.vue'

const vuetify = createVuetify({ components, directives })

function mountEmpty(props = {}) {
  return mount(EmptyState, {
    props,
    global: {
      plugins: [vuetify],
      stubs: {
        RouterLink: { template: '<a><slot/></a>' },
        'router-link': { template: '<a><slot/></a>' },
      },
    },
  })
}

describe('EmptyState', () => {
  it('renders the title and text', () => {
    const w = mountEmpty({ title: 'Nothing here', text: 'Add something first' })
    expect(w.text()).toContain('Nothing here')
    expect(w.text()).toContain('Add something first')
  })

  it('uses the default icon when none is provided', () => {
    const w = mountEmpty({ title: 'x' })
    expect(w.html()).toContain('mdi-information-outline')
  })

  it('renders a custom icon', () => {
    const w = mountEmpty({ title: 'x', icon: 'mdi-cart-outline' })
    expect(w.html()).toContain('mdi-cart-outline')
  })

  it('does not render a CTA without actionLabel', () => {
    const w = mountEmpty({ title: 'x' })
    expect(w.findComponent({ name: 'VBtn' }).exists()).toBe(false)
  })

  it('emits action when the CTA is clicked and no actionTo is given', async () => {
    const w = mountEmpty({ title: 'x', actionLabel: 'Do it' })
    await w.findComponent({ name: 'VBtn' }).trigger('click')
    expect(w.emitted('action')).toBeTruthy()
  })
})
