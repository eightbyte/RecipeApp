import { defineComponent, h } from 'vue'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { usePendingChangeSending } from './usePendingChangeSending'
import { useShoppingListStore } from '@/stores/shoppingList'

async function mountApp() {
  const pinia = createPinia()
  setActivePinia(pinia)
  const store = useShoppingListStore()
  vi.spyOn(store, 'sendPending').mockResolvedValue()
  vi.spyOn(store, 'markOffline')
  const wrapper = mount(defineComponent({ setup: () => { usePendingChangeSending(); return () => h('div') } }), {
    global: { plugins: [pinia] },
  })
  // The app-start send waits on IndexedDB, so it is let through before each test makes its own.
  await vi.waitFor(() => expect(store.sendPending).toHaveBeenCalled())
  store.sendPending.mockClear()
  return { store, wrapper }
}

function setVisibility(state) {
  Object.defineProperty(document, 'visibilityState', { value: state, configurable: true })
  document.dispatchEvent(new Event('visibilitychange'))
}

afterEach(() => {
  delete document.visibilityState
})

describe('usePendingChangeSending', () => {
  it('sends the queue saved on this phone when the app starts', async () => {
    await mountApp()
  })

  it.each([
    ['the browser comes online', () => window.dispatchEvent(new Event('online'))],
    ['the window gains focus', () => window.dispatchEvent(new Event('focus'))],
    ['the page becomes visible', () => setVisibility('visible')],
  ])('sends the queue when %s', async (_, happen) => {
    const { store } = await mountApp()

    happen()

    expect(store.sendPending).toHaveBeenCalledTimes(1)
  })

  it('doesn’t send when the page is hidden', async () => {
    const { store } = await mountApp()

    setVisibility('hidden')

    expect(store.sendPending).not.toHaveBeenCalled()
  })

  it('marks the app offline when the browser says so', async () => {
    const { store } = await mountApp()
    window.dispatchEvent(new Event('offline'))
    expect(store.markOffline).toHaveBeenCalled()
  })

  it('stops listening once unmounted', async () => {
    const { store, wrapper } = await mountApp()
    wrapper.unmount()

    window.dispatchEvent(new Event('online'))

    expect(store.sendPending).not.toHaveBeenCalled()
  })
})
