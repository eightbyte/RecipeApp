import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import ServingsStepper from './ServingsStepper.vue'

const vuetify = createVuetify({ components, directives })

function mountStepper(servings) {
  return mount(ServingsStepper, {
    props: { modelValue: servings, 'onUpdate:modelValue': value => wrapper.setProps({ modelValue: value }) },
    global: { plugins: [vuetify] },
  })
}

let wrapper

function button(label) {
  return wrapper.get(`button[aria-label="${label}"]`)
}

function value() {
  return wrapper.get('[aria-live="polite"]').text()
}

describe('ServingsStepper', () => {
  it('shows the servings it is given', () => {
    wrapper = mountStepper(6)
    expect(value()).toBe('6')
  })

  it('steps up and down by one serving', async () => {
    wrapper = mountStepper(6)
    await button('More servings').trigger('click')
    expect(wrapper.emitted('update:modelValue').at(-1)).toEqual([7])
    await button('Fewer servings').trigger('click')
    await button('Fewer servings').trigger('click')
    expect(wrapper.emitted('update:modelValue').at(-1)).toEqual([5])
  })

  it('steps from a fractional count to the next whole number either way', async () => {
    wrapper = mountStepper(2.5)
    await button('More servings').trigger('click')
    expect(wrapper.emitted('update:modelValue').at(-1)).toEqual([3])

    wrapper = mountStepper(2.5)
    await button('Fewer servings').trigger('click')
    expect(wrapper.emitted('update:modelValue').at(-1)).toEqual([2])
  })

  it('stops at one serving', () => {
    wrapper = mountStepper(1)
    expect(button('Fewer servings').attributes('disabled')).toBeDefined()
    expect(button('More servings').attributes('disabled')).toBeUndefined()
  })

  it('stops at a hundred servings', () => {
    wrapper = mountStepper(100)
    expect(button('More servings').attributes('disabled')).toBeDefined()
    expect(button('Fewer servings').attributes('disabled')).toBeUndefined()
  })
})
