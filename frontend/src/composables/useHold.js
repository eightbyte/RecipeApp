import { onScopeDispose } from 'vue'

// Longer than the usual 500 ms long-press, so a hold is always deliberate.
export const HOLD_DURATION_MS = 1000
export const HOLD_MOVE_TOLERANCE_PX = 10
export const HOLD_VIBRATION_MS = 50

/** Calls onHold(target) after a still press of HOLD_DURATION_MS from any pointer type, built on pointer events because iOS Safari fires no contextmenu on a long-press. */
export function useHold(onHold) {
  let timer = null
  let origin = null

  function start(event, target) {
    if (!event.isPrimary || (event.pointerType === 'mouse' && event.button !== 0)) return
    cancel()
    origin = { x: event.clientX, y: event.clientY }
    timer = setTimeout(() => {
      timer = null
      swallowNextClick()
      navigator.vibrate?.(HOLD_VIBRATION_MS)
      onHold(target)
    }, HOLD_DURATION_MS)
  }

  function move(event) {
    if (timer === null) return
    if (Math.hypot(event.clientX - origin.x, event.clientY - origin.y) > HOLD_MOVE_TOLERANCE_PX) cancel()
  }

  function cancel() {
    clearTimeout(timer)
    timer = null
  }

  // The click that ends a completed hold would otherwise toggle the row or land on whatever the hold opened.
  function swallowNextClick() {
    window.addEventListener('click', swallow, { capture: true })
    // A long touch can end without any click, so the next press disarms the guard before it eats a real click.
    window.addEventListener('pointerdown', disarm, { capture: true })
  }

  function swallow(event) {
    event.stopPropagation()
    event.preventDefault()
    disarm()
  }

  function disarm() {
    window.removeEventListener('click', swallow, { capture: true })
    window.removeEventListener('pointerdown', disarm, { capture: true })
  }

  onScopeDispose(() => {
    cancel()
    disarm()
  })

  /** The listeners that make an element holdable, passing target to onHold. */
  function listeners(target) {
    return {
      pointerdown:   event => start(event, target),
      pointermove:   move,
      pointerup:     cancel,
      pointercancel: cancel,
      pointerleave:  cancel,
    }
  }

  return { listeners }
}
