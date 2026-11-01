import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import BatchSizeToggle from './BatchSizeToggle.vue'

const vuetify = createVuetify({ components, directives })

function mountToggle(props = {}) {
  return mount(BatchSizeToggle, {
    props: { modelValue: 'REGULAR', ...props },
    global: { plugins: [vuetify] },
  })
}

describe('BatchSizeToggle', () => {
  it('offers every Batch Size in order', () => {
    const buttonLabels = mountToggle().findAll('button').map(button => button.text())
    expect(buttonLabels).toEqual(['½×', '1×', '2×'])
  })

  it('reports the Batch Size that is picked', async () => {
    const wrapper = mountToggle()
    await wrapper.findAll('button')[2].trigger('click')
    expect(wrapper.emitted('update:modelValue')).toEqual([['DOUBLE']])
  })

  it('shows the current Batch Size as selected', () => {
    const selected = mountToggle({ modelValue: 'HALF' }).findAll('button.v-btn--active').map(button => button.text())
    expect(selected).toEqual(['½×'])
  })

  it('is labelled Batch for screen readers', () => {
    expect(mountToggle().attributes('aria-label')).toBe('Batch')
  })

  it('renders its buttons at the size it is given', () => {
    const buttonSizes = mountToggle({ buttonSize: 'small' }).findAllComponents({ name: 'VBtn' }).map(button => button.props('size'))
    expect(buttonSizes).toEqual(['small', 'small', 'small'])
  })

  it('shares the full width between its buttons when stretched', () => {
    const wrapper = mountToggle({ stretch: true })
    expect(wrapper.classes()).toContain('d-flex')
    expect(wrapper.findAll('button').every(button => button.classes('flex-grow-1'))).toBe(true)
  })

  it('keeps its natural width by default', () => {
    const wrapper = mountToggle()
    expect(wrapper.classes()).not.toContain('d-flex')
    expect(wrapper.findAll('button').some(button => button.classes('flex-grow-1'))).toBe(false)
  })
})
