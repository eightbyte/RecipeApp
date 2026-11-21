import { onScopeDispose } from 'vue'
import { useShoppingListStore } from '@/stores/shoppingList'

/** Sends the Shopping List's Pending Changes from whichever page is open: once at app start, and whenever the phone may have signal again. */
export function usePendingChangeSending() {
  const store = useShoppingListStore()
  const send = () => store.sendPending()

  function onVisibilityChange() {
    if (document.visibilityState === 'visible') send()
  }

  const listeners = [
    [window, 'online', send],
    [window, 'focus', send],
    [window, 'offline', () => store.markOffline()],
    [document, 'visibilitychange', onVisibilityChange],
  ]
  for (const [target, type, listener] of listeners) target.addEventListener(type, listener)

  store.restore().then(send)

  onScopeDispose(() => {
    for (const [target, type, listener] of listeners) target.removeEventListener(type, listener)
  })
}
