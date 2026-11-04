import { effectScope } from 'vue'
import { useDelayedHide } from './useDelayedHide'

let scope

function start() {
  scope = effectScope()
  return scope.run(() => useDelayedHide())
}

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
})

afterEach(() => {
  scope?.stop()
  vi.useRealTimers()
})

describe('useDelayedHide', () => {
  it('keeps an item shown for 3 seconds after it is delayed', () => {
    const delayedHide = start()

    delayedHide.delay('milk')
    vi.advanceTimersByTime(2999)
    expect(delayedHide.isDelayed('milk')).toBe(true)

    vi.advanceTimersByTime(1)
    expect(delayedHide.isDelayed('milk')).toBe(false)
  })

  it('times each item on its own, so delaying another does not restart the first', () => {
    const delayedHide = start()

    delayedHide.delay('milk')
    vi.advanceTimersByTime(2000)
    delayedHide.delay('eggs')
    vi.advanceTimersByTime(1000)

    expect(delayedHide.isDelayed('milk')).toBe(false)
    expect(delayedHide.isDelayed('eggs')).toBe(true)
  })

  it('stops a cancelled item’s timer and leaves the others running', () => {
    const delayedHide = start()

    delayedHide.delay('milk')
    delayedHide.delay('eggs')
    vi.advanceTimersByTime(1000)
    delayedHide.cancel('milk')
    expect(delayedHide.isDelayed('milk')).toBe(false)

    delayedHide.delay('milk')
    vi.advanceTimersByTime(2000)
    expect(delayedHide.isDelayed('eggs')).toBe(false)
    expect(delayedHide.isDelayed('milk')).toBe(true)

    vi.advanceTimersByTime(1000)
    expect(delayedHide.isDelayed('milk')).toBe(false)
  })

  it('cancels every item at once', () => {
    const delayedHide = start()

    delayedHide.delay('milk')
    delayedHide.delay('eggs')
    delayedHide.cancelAll()

    expect(delayedHide.isDelayed('milk')).toBe(false)
    expect(delayedHide.isDelayed('eggs')).toBe(false)
    expect(vi.getTimerCount()).toBe(0)
  })

  it('clears its timers when its owner is unmounted', () => {
    const delayedHide = start()

    delayedHide.delay('milk')
    scope.stop()

    expect(vi.getTimerCount()).toBe(0)
  })
})
