import { ref, onMounted, onUnmounted } from 'vue'

/**
 * Wrapper around the Screen Wake Lock API that keeps the screen awake while a
 * component is mounted. Guarded for unsupported browsers (notably iOS Safari
 * < 16.4) so it degrades to a no-op rather than throwing.
 *
 * @see https://developer.mozilla.org/docs/Web/API/Screen_Wake_Lock_API
 */
export function useWakeLock() {
  const isSupported = typeof navigator !== 'undefined' && 'wakeLock' in navigator
  const sentinel = ref(null)

  async function acquire() {
    if (!isSupported) return
    try {
      sentinel.value = await navigator.wakeLock.request('screen')
    } catch {
      // A missing user gesture or power-save mode can reject; non-fatal —
      // the screen simply may sleep.
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
