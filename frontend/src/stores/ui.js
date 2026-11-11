import { defineStore } from 'pinia'
import { ref } from 'vue'

/** Global UI store: a feedback channel whose notify() raises the single snackbar, kept out of the Axios interceptor to avoid a circular dependency. */
export const useUiStore = defineStore('ui', () => {
  const snackbar = ref({ id: 0, show: false, message: '', color: 'success', action: null })

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

  return { snackbar, notify, dismiss, runAction }
})
