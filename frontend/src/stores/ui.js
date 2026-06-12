import { defineStore } from 'pinia'
import { ref } from 'vue'

/**
 * Global UI store — a lightweight feedback channel shared across views.
 *
 * `notify()` raises a single snackbar (rendered once by AppSnackbar in App.vue).
 * Keeping this out of the Axios interceptor avoids the interceptor → store
 * circular dependency (see services/api.js); views/stores call notify() directly.
 */
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
