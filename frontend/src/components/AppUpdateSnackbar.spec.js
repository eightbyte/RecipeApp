import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import AppUpdateSnackbar from './AppUpdateSnackbar.vue'
import { useUiStore } from '@/stores/ui'

const vuetify = createVuetify({ components, directives })

let pinia

beforeEach(() => {
  pinia = createPinia()
  setActivePinia(pinia)
})

// v-snackbar teleports its content to <body>; mount attached so we can read it.
const mountAttached = () => mount(AppUpdateSnackbar, {
  attachTo: document.body,
  global: { plugins: [vuetify, pinia] },
})
const button = label => [...document.querySelectorAll('.v-snackbar .v-btn')]
  .find(b => b.textContent.trim() === label || b.getAttribute('aria-label') === label)

describe('AppUpdateSnackbar', () => {
  it('is hidden until a new version is offered', () => {
    const w = mountAttached()
    expect(w.findComponent({ name: 'VSnackbar' }).props('modelValue')).toBe(false)
  })

  it('stays open until the user chooses', async () => {
    const w = mountAttached()
    useUiStore().offerUpdate(vi.fn())
    await flushPromises()

    const snack = w.findComponent({ name: 'VSnackbar' })
    expect(snack.props('modelValue')).toBe(true)
    expect(snack.props('timeout')).toBe(-1)
    expect(document.body.innerHTML).toContain('A new version of RecipeApp is ready.')
  })

  it('installs the new version on Reload', async () => {
    mountAttached()
    const install = vi.fn()
    useUiStore().offerUpdate(install)
    await flushPromises()

    button('Reload').click()

    expect(install).toHaveBeenCalledTimes(1)
  })

  it('closes without installing on Not now', async () => {
    mountAttached()
    const ui = useUiStore()
    const install = vi.fn()
    ui.offerUpdate(install)
    await flushPromises()

    button('Not now').click()

    expect(ui.updateReady).toBe(false)
    expect(install).not.toHaveBeenCalled()
  })

  it('outlasts a feedback notification', async () => {
    const w = mountAttached()
    const ui = useUiStore()
    ui.offerUpdate(vi.fn())
    ui.notify({ message: 'Saved' })
    await flushPromises()

    expect(w.findComponent({ name: 'VSnackbar' }).props('modelValue')).toBe(true)
  })
})
