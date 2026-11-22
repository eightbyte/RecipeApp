import { watchForAppUpdates, UPDATE_CHECK_INTERVAL_MS } from './appUpdates'

function setVisibility(state) {
  Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => state })
  document.dispatchEvent(new Event('visibilitychange'))
}

function setOnline(online) {
  Object.defineProperty(navigator, 'onLine', { configurable: true, get: () => online })
}

/** Watches through a fake registerSW, so each test can drive its callbacks and the registration directly. */
function startWatching() {
  const ui = { offerUpdate: vi.fn() }
  const updateSW = vi.fn()
  let options
  const registerSW = vi.fn(opts => {
    options = opts
    return updateSW
  })
  const registration = { waiting: null, update: vi.fn().mockResolvedValue(undefined) }

  watchForAppUpdates(registerSW, ui)
  options.onRegisteredSW('/sw.js', registration)

  return { ui, updateSW, options, registration }
}

describe('watchForAppUpdates', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] })
    setOnline(true)
  })

  afterEach(() => {
    vi.useRealTimers()
    setVisibility('visible')
  })

  it('offers a downloaded version, installing it through registerSW', () => {
    const { ui, updateSW, options } = startWatching()

    options.onNeedRefresh()
    expect(ui.offerUpdate).toHaveBeenCalledTimes(1)

    ui.offerUpdate.mock.calls[0][0]()
    expect(updateSW).toHaveBeenCalledTimes(1)
  })

  it('checks for a new version when the app returns to the foreground', () => {
    const { registration } = startWatching()

    setVisibility('hidden')
    expect(registration.update).not.toHaveBeenCalled()

    setVisibility('visible')
    expect(registration.update).toHaveBeenCalledTimes(1)
  })

  it('offers a still-waiting version again when the app returns to the foreground', () => {
    const { ui, updateSW, registration } = startWatching()
    registration.waiting = {}

    setVisibility('visible')

    expect(ui.offerUpdate).toHaveBeenCalledTimes(1)
    ui.offerUpdate.mock.calls[0][0]()
    expect(updateSW).toHaveBeenCalledTimes(1)
  })

  it('offers nothing on return when no version is waiting', () => {
    const { ui } = startWatching()

    setVisibility('visible')

    expect(ui.offerUpdate).not.toHaveBeenCalled()
  })

  it('checks for a new version hourly', () => {
    const { registration } = startWatching()

    vi.advanceTimersByTime(UPDATE_CHECK_INTERVAL_MS)
    expect(registration.update).toHaveBeenCalledTimes(1)
  })

  it('skips the check while offline', () => {
    const { registration } = startWatching()
    setOnline(false)

    setVisibility('visible')
    vi.advanceTimersByTime(UPDATE_CHECK_INTERVAL_MS)

    expect(registration.update).not.toHaveBeenCalled()
  })

  it('swallows a failed check', async () => {
    // An unhandled rejection here would fail the Vitest run.
    const { registration } = startWatching()
    registration.update.mockRejectedValue(new Error('network'))

    setVisibility('visible')
    await Promise.resolve()

    expect(registration.update).toHaveBeenCalledTimes(1)
  })

  it('does nothing more when registration failed', () => {
    const registerSW = vi.fn(opts => {
      opts.onRegisteredSW('/sw.js', undefined)
      return vi.fn()
    })

    expect(() => watchForAppUpdates(registerSW, { offerUpdate: vi.fn() })).not.toThrow()
  })
})
