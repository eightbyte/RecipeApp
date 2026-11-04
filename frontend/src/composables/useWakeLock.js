import { ref, onMounted, onUnmounted } from 'vue'

/** Keeps the screen awake while a component is mounted via the Screen Wake Lock API, degrading to a no-op on unsupported browsers. */
export function useWakeLock() {
  const isSupported = typeof navigator !== 'undefined' && 'wakeLock' in navigator
  const sentinel = ref(null)

  async function acquire() {
    if (!isSupported) return
    try {
      sentinel.value = await navigator.wakeLock.request('screen')
    } catch {
      // A missing user gesture or power-save mode can reject, which is non-fatal because the screen simply may sleep.
      sentinel.value = null
    }
  }

  async function release() {
    try { await sentinel.value?.release() } catch { /* ignore */ }
    sentinel.value = null
  }

  // The lock auto-releases when the tab is hidden; re-acquire on return.
  function onVisibility() {
    if (document.visibilityState === 'visible') acquire()
  }

  onMounted(() => {
    acquire()
    document.addEventListener('visibilitychange', onVisibility)
  })

  onUnmounted(() => {
    document.removeEventListener('visibilitychange', onVisibility)
    release()
  })

  return { isSupported, acquire, release }
}
