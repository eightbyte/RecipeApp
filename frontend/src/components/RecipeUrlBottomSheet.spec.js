import { flushPromises, mount } from '@vue/test-utils'
import api from '@/services/api'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import { useRecipeStore } from '@/stores/recipes'
import RecipeUrlBottomSheet from './RecipeUrlBottomSheet.vue'

vi.mock('@/services/api', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
  assetUrl: vi.fn((p) => p ?? ''),
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn() }),
}))

const vuetify = createVuetify({ components, directives })

function mountSheet(modelValue = true) {
  const pinia = createPinia()
  setActivePinia(pinia)

  const wrapper = mount(RecipeUrlBottomSheet, {
    props: { modelValue },
    global: {
      plugins: [vuetify, pinia],
      stubs: {
        // Render sheet content directly so we can find child components in tests
        VBottomSheet: { template: '<div><slot /></div>' },
        teleport: true,
      },
    },
  })
  return { wrapper, store: useRecipeStore() }
}

const STATUS_URL = '/model-server/status'

function statusResponse(state, model = null) {
  return { data: { state, model } }
}

function importButton(wrapper) {
  return wrapper.findAllComponents({ name: 'VBtn' }).find(b => b.text().includes('Import'))
}

describe('RecipeUrlBottomSheet', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.get.mockResolvedValue(statusResponse('ready', 'Qwen3.5-9B-Q6_K.gguf'))
  })

  it('renders a URL text field when open', async () => {
    const { wrapper } = mountSheet(true)
    await wrapper.vm.$nextTick()
    const input = wrapper.findComponent({ name: 'VTextField' })
    expect(input.exists()).toBe(true)
  })

  it('shows progress linear while scrapeLoading is true', async () => {
    const { wrapper, store } = mountSheet(true)
    store.scrapeLoading = true
    await wrapper.vm.$nextTick()
    const progress = wrapper.findComponent({ name: 'VProgressLinear' })
    expect(progress.exists()).toBe(true)
  })

  it('shows loading message while scrapeLoading is true', async () => {
    const { wrapper, store } = mountSheet(true)
    store.scrapeLoading = true
    await wrapper.vm.$nextTick()
    expect(wrapper.text()).toContain('Extracting recipe')
  })

  it('displays scrapeError message when set', async () => {
    const { wrapper, store } = mountSheet(true)
    store.scrapeError = 'URL unreachable'
    await wrapper.vm.$nextTick()
    expect(wrapper.text()).toContain('URL unreachable')
  })

  it('Import button is disabled when URL is empty', async () => {
    const { wrapper } = mountSheet(true)
    await wrapper.vm.$nextTick()
    const buttons = wrapper.findAllComponents({ name: 'VBtn' })
    const importBtn = buttons.find(b => b.text().includes('Import'))
    expect(importBtn?.props('disabled')).toBe(true)
  })

  it('Import button is disabled while loading', async () => {
    const { wrapper, store } = mountSheet(true)
    store.scrapeLoading = true
    await wrapper.vm.$nextTick()
    const buttons = wrapper.findAllComponents({ name: 'VBtn' })
    const importBtn = buttons.find(b => b.text().includes('Import'))
    expect(importBtn?.props('disabled')).toBe(true)
  })

  describe('Model Server status', () => {
    afterEach(() => vi.useRealTimers())

    it('asks for the status when the sheet opens', async () => {
      mountSheet(true)
      await flushPromises()

      expect(api.get).toHaveBeenCalledWith(STATUS_URL)
    })

    it('enables Import for a URL once the Model Server is ready, with no status message', async () => {
      const { wrapper } = mountSheet(true)
      await flushPromises()
      await wrapper.find('input').setValue('https://example.com/soup')

      expect(importButton(wrapper).props('disabled')).toBe(false)
      expect(wrapper.text()).not.toContain('Scraping is unavailable')
    })

    it.each([
      ['offline',      "the Model Server isn't running"],
      ['loading',      'the Model Server is still loading the model'],
      ['unauthorized', 'the Model Server rejected the API key'],
    ])('shows why Scraping is unavailable and disables Import when the state is %s', async (state, message) => {
      api.get.mockResolvedValue(statusResponse(state))
      const { wrapper } = mountSheet(true)
      await flushPromises()
      await wrapper.find('input').setValue('https://example.com/soup')

      expect(wrapper.text()).toContain(message)
      expect(importButton(wrapper).props('disabled')).toBe(true)
    })

    it('keeps the URL field usable while Scraping is unavailable', async () => {
      api.get.mockResolvedValue(statusResponse('offline'))
      const { wrapper } = mountSheet(true)
      await flushPromises()

      expect(wrapper.findComponent({ name: 'VTextField' }).props('disabled')).toBe(false)
    })

    it('checks again every 3 seconds while the model is loading, and enables Import once it is ready', async () => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
      api.get.mockResolvedValue(statusResponse('loading'))
      const { wrapper } = mountSheet(true)
      await flushPromises()
      await wrapper.find('input').setValue('https://example.com/soup')
      expect(api.get).toHaveBeenCalledTimes(1)

      vi.advanceTimersByTime(2999)
      await flushPromises()
      expect(api.get).toHaveBeenCalledTimes(1)

      api.get.mockResolvedValue(statusResponse('ready', 'model.gguf'))
      vi.advanceTimersByTime(1)
      await flushPromises()

      expect(api.get).toHaveBeenCalledTimes(2)
      expect(wrapper.text()).not.toContain('still loading')
      expect(importButton(wrapper).props('disabled')).toBe(false)

      vi.advanceTimersByTime(9000)
      await flushPromises()
      expect(api.get).toHaveBeenCalledTimes(2)
    })

    it('stops checking when the state leaves loading for anything else', async () => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
      api.get.mockResolvedValue(statusResponse('loading'))
      const { wrapper } = mountSheet(true)
      await flushPromises()

      api.get.mockResolvedValue(statusResponse('offline'))
      vi.advanceTimersByTime(3000)
      await flushPromises()
      vi.advanceTimersByTime(9000)
      await flushPromises()

      expect(api.get).toHaveBeenCalledTimes(2)
      expect(wrapper.text()).toContain("the Model Server isn't running")
    })

    it('stops checking when the sheet closes', async () => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
      api.get.mockResolvedValue(statusResponse('loading'))
      const { wrapper } = mountSheet(true)
      await flushPromises()

      await wrapper.setProps({ modelValue: false })
      vi.advanceTimersByTime(9000)
      await flushPromises()

      expect(api.get).toHaveBeenCalledTimes(1)
    })

    it('asks again each time the sheet opens, so a server started mid-session needs no reload', async () => {
      api.get.mockResolvedValue(statusResponse('offline'))
      const { wrapper } = mountSheet(true)
      await flushPromises()

      await wrapper.setProps({ modelValue: false })
      api.get.mockResolvedValue(statusResponse('ready', 'model.gguf'))
      await wrapper.setProps({ modelValue: true })
      await flushPromises()
      await wrapper.find('input').setValue('https://example.com/soup')

      expect(api.get).toHaveBeenCalledTimes(2)
      expect(wrapper.text()).not.toContain('Scraping is unavailable')
      expect(importButton(wrapper).props('disabled')).toBe(false)
    })

    it('shows no status and leaves Import enabled when the status request itself fails, so the Scrape reports the real error', async () => {
      api.get.mockRejectedValue({ status: undefined, message: 'Network Error' })
      const { wrapper } = mountSheet(true)
      await flushPromises()
      await wrapper.find('input').setValue('https://example.com/soup')

      expect(wrapper.text()).not.toContain('Scraping is unavailable')
      expect(wrapper.text()).not.toContain('Network Error')
      expect(importButton(wrapper).props('disabled')).toBe(false)
    })
  })
})
