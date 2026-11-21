import { onScopeDispose, watch } from 'vue'

// A row moving just after a touch is a row moving under the thumb's next tap.
const TOUCH_SETTLE_MS = 1000

/** A List Refresh for as long as the calling component is mounted: loads with `fetch` on chosen events and hands the result to `apply` once the user isn't touching the screen and `isHeld` (a getter) is false. */
export function useListRefresh({ fetch, apply, isHeld = () => false }) {
  const pointersDown = new Set()
  let settleTimer = null
  let pending = null  // { latest } waiting to apply
  let newest = 0

  /** Starts a background refresh, superseding any earlier one; it applies when the user is idle, and a failure keeps the current list without a word. */
  async function request() {
    const sequence = ++newest
    // A result from before this request can be older than a save that prompted it.
    pending = null
    let latest
    try {
      latest = await fetch()
    } catch {
      return
    }
    if (sequence !== newest) return
    pending = { latest }
    applyWhenIdle()
  }

  /** A refresh the user asked for: applies the moment it arrives and rejects on failure, so the caller can say so. */
  async function pull() {
    const sequence = ++newest
    pending = null
    const latest = await fetch()
    if (sequence === newest) apply(latest)
  }

  function isTouching() {
    return pointersDown.size > 0 || settleTimer !== null
  }

  function applyWhenIdle() {
    if (!pending || isTouching() || isHeld()) return
    const { latest } = pending
    pending = null
    apply(latest)
  }

  function onPointerDown(event) {
    pointersDown.add(event.pointerId)
  }

  function onPointerUp(event) {
    pointersDown.delete(event.pointerId)
    if (pointersDown.size) return
    clearTimeout(settleTimer)
    settleTimer = setTimeout(() => {
      settleTimer = null
      applyWhenIdle()
    }, TOUCH_SETTLE_MS)
  }

  function onVisibilityChange() {
    if (document.visibilityState !== 'visible') return
    // A touch can't outlast the page being hidden, and its pointerup may never have arrived.
    pointersDown.clear()
    request()
  }

  const listeners = [
    [document, 'visibilitychange', onVisibilityChange],
    [window, 'focus', request],
    [window, 'pointerdown', onPointerDown, { capture: true }],
    [window, 'pointerup', onPointerUp, { capture: true }],
    [window, 'pointercancel', onPointerUp, { capture: true }],
  ]
  for (const [target, type, listener, options] of listeners) target.addEventListener(type, listener, options)

  watch(isHeld, held => { if (!held) applyWhenIdle() })

  onScopeDispose(() => {
    for (const [target, type, listener, options] of listeners) target.removeEventListener(type, listener, options)
    clearTimeout(settleTimer)
  })

  return { request, pull }
}
