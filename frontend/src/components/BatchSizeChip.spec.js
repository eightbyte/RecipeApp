import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import BatchSizeChip from './BatchSizeChip.vue'

const vuetify = createVuetify({ components, directives })

function mountChip(props) {
  return mount(BatchSizeChip, { props, global: { plugins: [vuetify] } })
}

describe('BatchSizeChip', () => {
  it('shows the Batch Size label', () => {
    expect(mountChip({ batchSize: 'DOUBLE' }).text()).toBe('2×')
  })

  it('is named for screen readers as a Batch', () => {
    expect(mountChip({ batchSize: 'HALF' }).attributes('aria-label')).toBe('Batch ½×')
  })

  it('takes the colour of its Batch Size', () => {
    expect(mountChip({ batchSize: 'DOUBLE' }).findComponent({ name: 'VChip' }).props('color')).toBe('warning')
  })

  it('renders at the size it is given', () => {
    expect(mountChip({ batchSize: 'REGULAR', size: 'x-small' }).findComponent({ name: 'VChip' }).props('size')).toBe('x-small')
  })
})
