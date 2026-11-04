import { defineStore } from 'pinia'
import { ref } from 'vue'

/** Global UI store: a feedback channel whose notify() raises the single snackbar, kept out of the Axios interceptor to avoid a circular dependency. */
export const useUiStore = defineStore('ui', () => {
  const snackbar = ref({ show: false, message: '', color: 'success' })

  function notify({ message, color = 'success' }) {
    snackbar.value = { show: true, message, color }
  }

  function dismiss() {
    snackbar.value = { ...snackbar.value, show: false }
  }

  return { snackbar, notify, dismiss }
})
