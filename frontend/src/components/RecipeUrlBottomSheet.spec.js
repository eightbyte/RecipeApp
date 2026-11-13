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

const push = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
}))

const vuetify = createVuetify({ components, directives })

function mountSheet(modelValue = true, props = {}) {
  const pinia = createPinia()
  setActivePinia(pinia)

  const wrapper = mount(RecipeUrlBottomSheet, {
    props: { modelValue, ...props },
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
    expect(wrapper.text()).toContain('Extracting recipe… this may take up to a minute.')
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

  describe('URL / Text toggle', () => {
    const RECIPE_TEXT = 'Honey Chicken\n\n• 2 chicken breasts\n• 1 tbsp honey'

    function textArea(wrapper) {
      return wrapper.findComponent({ name: 'VTextarea' })
    }

    async function chooseText(wrapper) {
      const textButton = wrapper.findAllComponents({ name: 'VBtn' }).find(b => b.text() === 'Text')
      await textButton.trigger('click')
    }

    it('opens on URL, with the URL field and no text field', async () => {
      const { wrapper } = mountSheet(true)
      await flushPromises()

      expect(wrapper.findComponent({ name: 'VTextField' }).exists()).toBe(true)
      expect(textArea(wrapper).exists()).toBe(false)
    })

    it('shows a multi-line field with a counter against the maximum in Text mode, in place of the URL field', async () => {
      const { wrapper } = mountSheet(true)
      await flushPromises()

      await chooseText(wrapper)

      expect(textArea(wrapper).exists()).toBe(true)
      expect(textArea(wrapper).props('counter')).toBe(30000)
      expect(wrapper.findComponent({ name: 'VTextField' }).exists()).toBe(false)
    })

    it('opens in Text mode when asked to', async () => {
      const { wrapper } = mountSheet(true, { initialMode: 'text' })
      await flushPromises()

      expect(textArea(wrapper).exists()).toBe(true)
    })

    it('opens on its initial mode every time, not the mode it was left in, with the text cleared', async () => {
      const { wrapper } = mountSheet(true)
      await flushPromises()
      await chooseText(wrapper)
      await textArea(wrapper).find('textarea').setValue(RECIPE_TEXT)

      await wrapper.setProps({ modelValue: false })
      await wrapper.setProps({ modelValue: true })
      await flushPromises()
      expect(textArea(wrapper).exists()).toBe(false)

      await chooseText(wrapper)
      expect(textArea(wrapper).find('textarea').element.value).toBe('')
    })

    it('enables Import for a pasted recipe', async () => {
      const { wrapper } = mountSheet(true, { initialMode: 'text' })
      await flushPromises()
      await textArea(wrapper).find('textarea').setValue(RECIPE_TEXT)

      expect(importButton(wrapper).props('disabled')).toBe(false)
    })

    it.each([
      ['empty',     ''],
      ['blank',     '   \n  '],
      ['over-long', 'a'.repeat(30001)],
    ])('disables Import for an %s text', async (_, text) => {
      const { wrapper } = mountSheet(true, { initialMode: 'text' })
      await flushPromises()
      await textArea(wrapper).find('textarea').setValue(text)

      expect(importButton(wrapper).props('disabled')).toBe(true)
    })

    it('scrapes the text and opens the preview on success', async () => {
      api.post.mockResolvedValue({ data: { name: 'Honey Chicken', sourceUrl: null, ingredients: [], steps: [] } })
      const { wrapper } = mountSheet(true, { initialMode: 'text' })
      await flushPromises()
      await textArea(wrapper).find('textarea').setValue(RECIPE_TEXT)

      await importButton(wrapper).trigger('click')
      await flushPromises()

      expect(api.post).toHaveBeenCalledWith('/recipes/scrape/text', { text: RECIPE_TEXT })
      expect(push).toHaveBeenCalledWith({ name: 'scrape-preview' })
    })

    it('keeps the text after a failed Scrape so it can be edited and retried', async () => {
      api.post.mockRejectedValue({ message: "Couldn't find a recipe in that text." })
      const { wrapper } = mountSheet(true, { initialMode: 'text' })
      await flushPromises()
      await textArea(wrapper).find('textarea').setValue(RECIPE_TEXT)

      await importButton(wrapper).trigger('click')
      await flushPromises()

      expect(wrapper.text()).toContain("Couldn't find a recipe in that text.")
      expect(textArea(wrapper).find('textarea').element.value).toBe(RECIPE_TEXT)
      expect(push).not.toHaveBeenCalled()
    })

    it('shows why Scraping is unavailable and disables Import in Text mode too', async () => {
      api.get.mockResolvedValue(statusResponse('offline'))
      const { wrapper } = mountSheet(true, { initialMode: 'text' })
      await flushPromises()
      await textArea(wrapper).find('textarea').setValue(RECIPE_TEXT)

      expect(wrapper.text()).toContain("the Model Server isn't running")
      expect(importButton(wrapper).props('disabled')).toBe(true)
    })
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
