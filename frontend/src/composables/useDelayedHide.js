import { ref, onScopeDispose } from 'vue'

const HIDE_DELAY_MS = 3000

/** Keeps each delayed id shown for HIDE_DELAY_MS on its own timer. */
export function useDelayedHide() {
  const timers = ref(new Map())

  function delay(id) {
    cancel(id)
    timers.value.set(id, setTimeout(() => timers.value.delete(id), HIDE_DELAY_MS))
  }

  function cancel(id) {
    clearTimeout(timers.value.get(id))
    timers.value.delete(id)
  }

  function cancelAll() {
    for (const timer of timers.value.values()) clearTimeout(timer)
    timers.value.clear()
  }

  function isDelayed(id) {
    return timers.value.has(id)
  }

  onScopeDispose(cancelAll)

  return { delay, cancel, cancelAll, isDelayed }
}
