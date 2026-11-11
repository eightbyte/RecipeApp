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

  it('carries an action, and a later notify without one drops it', () => {
    const ui = useUiStore()
    const onClick = vi.fn()
    ui.notify({ message: 'Step removed', action: { label: 'Undo', onClick } })
    expect(ui.snackbar.action).toEqual({ label: 'Undo', onClick })

    ui.notify({ message: 'Saved' })
    expect(ui.snackbar.action).toBeNull()
  })

  it('runAction runs the action once and hides the snackbar', () => {
    const ui = useUiStore()
    const onClick = vi.fn()
    ui.notify({ message: 'Step removed', action: { label: 'Undo', onClick } })

    ui.runAction()
    ui.runAction()

    expect(onClick).toHaveBeenCalledTimes(1)
    expect(ui.snackbar.show).toBe(false)
  })

  it('gives each notification its own id, so a replacement starts a fresh timeout', () => {
    const ui = useUiStore()
    ui.notify({ message: 'One' })
    const first = ui.snackbar.id
    ui.notify({ message: 'Two' })
    expect(ui.snackbar.id).not.toBe(first)
  })

  it('dismiss hides the snackbar but keeps the last message', () => {
    const ui = useUiStore()
    ui.notify({ message: 'Bye', color: 'error' })
    ui.dismiss()
    expect(ui.snackbar.show).toBe(false)
    expect(ui.snackbar.message).toBe('Bye')
  })
})
