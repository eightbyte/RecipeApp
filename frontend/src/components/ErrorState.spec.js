import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import ErrorState from './ErrorState.vue'

const vuetify = createVuetify({ components, directives })

function mountError(props = {}) {
  return mount(ErrorState, { props, global: { plugins: [vuetify] } })
}

describe('ErrorState', () => {
  it('renders the default message', () => {
    const w = mountError()
    expect(w.text()).toContain('Something went wrong.')
  })

  it('renders a custom message', () => {
    const w = mountError({ message: 'Could not reach the server' })
    expect(w.text()).toContain('Could not reach the server')
  })

  it('emits retry when Try again is clicked', async () => {
    const w = mountError()
    const btn = w.findAll('button').find(b => b.text().includes('Try again'))
    await btn.trigger('click')
    expect(w.emitted('retry')).toBeTruthy()
  })
})
