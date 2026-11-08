import { flushPromises, mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import MeasurementUnitSelect from './MeasurementUnitSelect.vue'
import { UNIT_PICKER_ITEMS } from '@/constants/units'

const vuetify = createVuetify({ components, directives })

function mountSelect(props = {}) {
  return mount(MeasurementUnitSelect, {
    props: { modelValue: 'g', amount: 100, ...props },
    global: { plugins: [vuetify] },
  })
}

const select = wrapper => wrapper.findComponent({ name: 'VSelect' })

describe('MeasurementUnitSelect', () => {
  it('offers every storable and entry-only unit, grouped under Weight, Volume and Count', () => {
    expect(select(mountSelect()).props('items')).toEqual(UNIT_PICKER_ITEMS)
  })

  it('renders each group under its subheader when opened', async () => {
    const wrapper = mount(MeasurementUnitSelect, {
      props: { modelValue: 'g', amount: 100 },
      global: { plugins: [vuetify] },
      attachTo: document.body,
    })
    await wrapper.find('.v-field').trigger('mousedown')
    await flushPromises()
    const texts      = selector => [...document.querySelectorAll(selector)].map(el => el.textContent.trim())
    const subheaders = texts('.v-list-subheader')
    const options    = texts('.v-list-item')
    expect(subheaders).toEqual(['Weight', 'Volume', 'Count'])
    expect(options).toEqual(['g', 'kg', 'oz', 'lb', 'ml', 'L', 'tsp', 'tbsp', 'cup', 'fl oz', 'pint', 'quart', 'pcs'])
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
    expect(field.props('placeholder')).toBeUndefined()
  })

  it('brings the unit back when an amount is entered again', async () => {
    const wrapper = mountSelect({ amount: null })
    await wrapper.setProps({ amount: 2 })
    expect(select(wrapper).props('modelValue')).toBe('g')
  })

  it('can be cleared, and then reads as a count', async () => {
    // An amount with no unit saves as pcs.
    const wrapper = mountSelect({ modelValue: null })
    const field   = select(wrapper)
    expect(field.props('clearable')).toBe(true)
    expect(field.props('placeholder')).toBe('pcs')
    expect(field.props('persistentPlaceholder')).toBe(true)
  })

  it('offers no count placeholder while a unit must be picked', () => {
    // "1 can tomatoes": pcs would lose the can's size, so the empty field must not suggest it.
    const field = select(mountSelect({ modelValue: null, requiresUnit: true }))
    expect(field.props('placeholder')).toBeUndefined()
  })

  it('reports a cleared unit as null', async () => {
    const wrapper = mountSelect()
    select(wrapper).vm.$emit('update:modelValue', null)
    expect(wrapper.emitted('update:modelValue')).toEqual([[null]])
  })

  it('reports the unit that is picked', async () => {
    const wrapper = mountSelect()
    select(wrapper).vm.$emit('update:modelValue', 'cup')
    expect(wrapper.emitted('update:modelValue')).toEqual([['cup']])
  })
})
