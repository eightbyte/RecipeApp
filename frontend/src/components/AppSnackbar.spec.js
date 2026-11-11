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

  describe('action button', () => {
    // v-snackbar teleports its content to <body>; mount attached so we can read it.
    const mountAttached = () => mount(AppSnackbar, {
      attachTo: document.body,
      global: { plugins: [vuetify, pinia] },
    })
    const buttonTexts = () => [...document.querySelectorAll('.v-snackbar .v-btn')].map(b => b.textContent.trim())

    it('runs the action and closes', async () => {
      mountAttached()
      const ui = useUiStore()
      const onClick = vi.fn()
      ui.notify({ message: 'Ingredient removed', action: { label: 'Undo', onClick } })
      await flushPromises()

      const undo = [...document.querySelectorAll('.v-snackbar .v-btn')].find(b => b.textContent.trim() === 'Undo')
      undo.click()

      expect(onClick).toHaveBeenCalledTimes(1)
      expect(ui.snackbar.show).toBe(false)
    })

    it('is absent from a plain notification', async () => {
      mountAttached()
      useUiStore().notify({ message: 'Saved' })
      await flushPromises()

      expect(buttonTexts()).toEqual([''])
    })
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
