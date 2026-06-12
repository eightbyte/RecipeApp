import { mount, flushPromises } from '@vue/test-utils'
import { useWakeLock } from './useWakeLock'

// Mount a throwaway component so the composable runs inside a real setup()
// (onMounted/onUnmounted hooks need a component instance).
function mountWith() {
  let api
  const Comp = {
    template: '<div />',
    setup() {
      api = useWakeLock()
      return {}
    },
  }
  const wrapper = mount(Comp)
  return { wrapper, getApi: () => api }
}

function stubWakeLock(request) {
  Object.defineProperty(navigator, 'wakeLock', {
    value: { request },
    configurable: true,
  })
}

afterEach(() => {
  if ('wakeLock' in navigator) delete navigator.wakeLock
})

describe('useWakeLock', () => {
  it('acquires a screen wake lock when the API is supported', async () => {
    const request = vi.fn().mockResolvedValue({ release: vi.fn() })
    stubWakeLock(request)

    const { getApi } = mountWith()
    await flushPromises()

    expect(getApi().isSupported).toBe(true)
    expect(request).toHaveBeenCalledWith('screen')
  })

  it('is a silent no-op when the API is unavailable', async () => {
    expect('wakeLock' in navigator).toBe(false)

    const { getApi } = mountWith()
    await flushPromises()

    expect(getApi().isSupported).toBe(false)
  })

  it('releases the lock on unmount', async () => {
    const release = vi.fn().mockResolvedValue(undefined)
    stubWakeLock(vi.fn().mockResolvedValue({ release }))

    const { wrapper } = mountWith()
    await flushPromises()
    wrapper.unmount()
    await flushPromises()

    expect(release).toHaveBeenCalled()
  })

  it('re-acquires when the tab becomes visible again', async () => {
    const request = vi.fn().mockResolvedValue({ release: vi.fn() })
    stubWakeLock(request)
    Object.defineProperty(document, 'visibilityState', { value: 'visible', configurable: true })

    mountWith()
    await flushPromises()
    request.mockClear()

    document.dispatchEvent(new Event('visibilitychange'))
    await flushPromises()

    expect(request).toHaveBeenCalledWith('screen')
  })
})
