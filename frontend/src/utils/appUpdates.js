export const UPDATE_CHECK_INTERVAL_MS = 60 * 60 * 1000

/** Registers the service worker and checks for a new version on each return to the foreground and hourly, because an installed app resumed from the background makes none of the navigations the browser checks on. */
export function watchForAppUpdates(registerSW, ui) {
  const offer = () => ui.offerUpdate(() => updateSW())

  const updateSW = registerSW({
    onNeedRefresh: offer,
    onRegisteredSW: (_swUrl, registration) => {
      if (!registration) return

      const check = () => {
        // Offline the check can only fail, and the cached version keeps running either way.
        if (!navigator.onLine) return
        registration.update().catch(() => {})
      }

      setInterval(check, UPDATE_CHECK_INTERVAL_MS)
      document.addEventListener('visibilitychange', () => {
        if (document.visibilityState !== 'visible') return
        // A version waiting since Not now raises no new event, so offer it again on each return.
        if (registration.waiting) offer()
        check()
      })
    },
  })
}
