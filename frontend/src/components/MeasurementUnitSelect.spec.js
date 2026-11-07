import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import MeasurementUnitSelect from './MeasurementUnitSelect.vue'
import { MEASUREMENT_UNITS } from '@/constants/units'

const vuetify = createVuetify({ components, directives })

function mountSelect(props = {}) {
  return mount(MeasurementUnitSelect, {
    props: { modelValue: 'g', amount: 100, ...props },
    global: { plugins: [vuetify] },
  })
}

const select = wrapper => wrapper.findComponent({ name: 'VSelect' })

describe('MeasurementUnitSelect', () => {
  it('offers the storable units', () => {
    expect(select(mountSelect()).props('items')).toEqual(MEASUREMENT_UNITS)
  })

  it('shows the unit and can be changed while there is an amount', () => {
    const field = select(mountSelect())
    expect(field.props('modelValue')).toBe('g')
    expect(field.props('disabled')).toBe(false)
  })

  it('goes blank and disabled while the amount is empty, so the row reads as unquantified', () => {
    const field = select(mountSelect({ amount: '' }))
    expect(field.props('modelValue')).toBeNull()
    expect(field.props('disabled')).toBe(true)
  })

  it('brings the unit back when an amount is entered again', async () => {
    const wrapper = mountSelect({ amount: null })
    await wrapper.setProps({ amount: 2 })
    expect(select(wrapper).props('modelValue')).toBe('g')
  })

  it('reports the unit that is picked', async () => {
    const wrapper = mountSelect()
    select(wrapper).vm.$emit('update:modelValue', 'cup')
    expect(wrapper.emitted('update:modelValue')).toEqual([['cup']])
  })
})
