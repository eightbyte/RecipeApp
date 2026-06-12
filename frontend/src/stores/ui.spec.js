import { setActivePinia, createPinia } from 'pinia'
import { useUiStore } from './ui'

beforeEach(() => {
  setActivePinia(createPinia())
})

describe('ui store', () => {
  it('starts hidden', () => {
    const ui = useUiStore()
    expect(ui.snackbar.show).toBe(false)
  })

  it('notify sets the snackbar visible with message and colour', () => {
    const ui = useUiStore()
    ui.notify({ message: 'Saved', color: 'success' })
    expect(ui.snackbar.show).toBe(true)
    expect(ui.snackbar.message).toBe('Saved')
    expect(ui.snackbar.color).toBe('success')
  })

  it('defaults colour to success when omitted', () => {
    const ui = useUiStore()
    ui.notify({ message: 'Hi' })
    expect(ui.snackbar.color).toBe('success')
  })

  it('dismiss hides the snackbar but keeps the last message', () => {
    const ui = useUiStore()
    ui.notify({ message: 'Bye', color: 'error' })
    ui.dismiss()
    expect(ui.snackbar.show).toBe(false)
    expect(ui.snackbar.message).toBe('Bye')
  })
})
