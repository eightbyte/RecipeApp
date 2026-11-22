import { defineStore } from 'pinia'
import { ref } from 'vue'
import { readUnitSystemChoice, writeUnitSystemChoice } from '@/utils/unitSystemChoice'

/** Global UI store: a feedback channel whose notify() raises the single snackbar, kept out of the Axios interceptor to avoid a circular dependency, and this device's Unit System. */
export const useUiStore = defineStore('ui', () => {
  const snackbar = ref({ id: 0, show: false, message: '', color: 'success', action: null })

  // Shared so Cooking Mode and the step chips follow the recipe view's toggle.
  const unitSystem = ref(readUnitSystemChoice())

  /** Reads every recipe in `chosen` from now on, remembered on this device. */
  function chooseUnitSystem(chosen) {
    unitSystem.value = chosen
    writeUnitSystemChoice(chosen)
  }

  /** Raises the snackbar, replacing any showing; `action` ({ label, onClick }) adds a button such as Undo, which a replacement takes away. */
  function notify({ message, color = 'success', action = null }) {
    snackbar.value = { id: snackbar.value.id + 1, show: true, message, color, action }
  }

  function dismiss() {
    snackbar.value = { ...snackbar.value, show: false }
  }

  /** Runs the showing snackbar's action and hides it, so a second press does nothing. */
  function runAction() {
    const { show, action } = snackbar.value
    if (!show || !action) return
    dismiss()
    action.onClick()
  }

  // Whether a downloaded new version is being offered; not part of `snackbar`, because each notify() replaces that.
  const updateReady = ref(false)
  let installUpdate = null

  /** Offers the downloaded version; `install` swaps it in and reloads the page. */
  function offerUpdate(install) {
    installUpdate = install
    updateReady.value = true
  }

  function dismissUpdate() {
    updateReady.value = false
  }

  function applyUpdate() {
    installUpdate?.()
  }

  return {
    snackbar, notify, dismiss, runAction,
    unitSystem, chooseUnitSystem,
    updateReady, offerUpdate, dismissUpdate, applyUpdate,
  }
})
