import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import AppSnackbar from './AppSnackbar.vue'
import { useUiStore } from '@/stores/ui'

const vuetify = createVuetify({ components, directives })

let pinia

beforeEach(() => {
  pinia = createPinia()
  setActivePinia(pinia)
})

function mountSnackbar() {
  return mount(AppSnackbar, {
    global: {
      plugins: [vuetify, pinia],
      stubs: { teleport: true },
    },
  })
}

describe('AppSnackbar', () => {
  it('is hidden until notify is called', () => {
    const w = mountSnackbar()
    const snack = w.findComponent({ name: 'VSnackbar' })
    expect(snack.props('modelValue')).toBe(false)
  })

  it('binds visibility and colour to the ui store', async () => {
    const w = mountSnackbar()
    const ui = useUiStore()
    ui.notify({ message: 'Saved', color: 'success' })
    await w.vm.$nextTick()
    const snack = w.findComponent({ name: 'VSnackbar' })
    expect(snack.props('modelValue')).toBe(true)
    expect(snack.props('color')).toBe('success')
  })

  it('renders the message in a polite live region', async () => {
    // v-snackbar teleports its content to <body>; mount attached so we can read it.
    const w = mount(AppSnackbar, {
      attachTo: document.body,
      global: { plugins: [vuetify, pinia] },
    })
    const ui = useUiStore()
    ui.notify({ message: 'List updated', color: 'info' })
    await flushPromises()

    expect(document.body.innerHTML).toContain('List updated')
    expect(document.body.innerHTML).toContain('role="status"')
    expect(document.body.innerHTML).toContain('aria-live="polite"')

    w.unmount()
  })

  it('dismisses via the ui store when toggled off', async () => {
    const w = mountSnackbar()
    const ui = useUiStore()
    ui.notify({ message: 'Hi' })
    await w.vm.$nextTick()
    const snack = w.findComponent({ name: 'VSnackbar' })
    snack.vm.$emit('update:modelValue', false)
    await w.vm.$nextTick()
    expect(ui.snackbar.show).toBe(false)
  })
})
