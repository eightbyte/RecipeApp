import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import ServingsChip from './ServingsChip.vue'

const vuetify = createVuetify({ components, directives })

function mountChip(props) {
  return mount(ServingsChip, { props, global: { plugins: [vuetify] } })
}

describe('ServingsChip', () => {
  it('shows the number of servings', () => {
    expect(mountChip({ servings: 6 }).text()).toBe('6 servings')
  })

  it('reads "1 serving" for one', () => {
    expect(mountChip({ servings: 1 }).text()).toBe('1 serving')
  })

  it('looks the same whatever the count', () => {
    const colors = [1, 4, 100].map(servings => mountChip({ servings }).findComponent({ name: 'VChip' }).props('color'))
    expect(new Set(colors).size).toBe(1)
  })

  it('renders at the size it is given', () => {
    expect(mountChip({ servings: 4, size: 'x-small' }).findComponent({ name: 'VChip' }).props('size')).toBe('x-small')
  })
})
