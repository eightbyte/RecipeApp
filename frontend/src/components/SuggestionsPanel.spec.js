import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import SuggestionsPanel from './SuggestionsPanel.vue'
import { makeSuggestion } from '@/test/factories'

const pushMock = vi.fn()

vi.mock('vue-router', async (importActual) => {
  const actual = await importActual()
  return { ...actual, useRouter: () => ({ push: pushMock }) }
})

const vuetify = createVuetify({ components, directives })

beforeEach(() => vi.clearAllMocks())

function mountPanel(suggestions = []) {
  return mount(SuggestionsPanel, {
    props: { suggestions },
    global: {
      plugins: [vuetify],
      stubs: { teleport: true, RecentlyCookedDialog: true },
    },
  })
}

describe('SuggestionsPanel', () => {
  it('shows empty state message when no suggestions', () => {
    const wrapper = mountPanel([])
    expect(wrapper.text()).toContain('Add more recipes to see suggestions')
  })

  it('renders suggestion cards with ingredient label', () => {
    const suggestion = makeSuggestion({ recipeName: 'Stew', overlappingIngredients: ['onion', 'carrot'] })
    const wrapper = mountPanel([suggestion])

    expect(wrapper.text()).toContain('Stew')
    expect(wrapper.text()).toContain('onion')
    expect(wrapper.text()).toContain('carrot')
  })

  it('shows fallback label when overlappingIngredients is empty', () => {
    const suggestion = makeSuggestion({ overlappingIngredients: [] })
    const wrapper = mountPanel([suggestion])
    expect(wrapper.text()).toContain('Uses ingredients already in your plan')
  })

  describe('a suggestion card', () => {
    const addButton = (wrapper) => wrapper.find('.v-card .v-btn')
    const recentlyCookedDialog = (wrapper) => wrapper.findComponent({ name: 'RecentlyCookedDialog' })

    it('"+" adds a suggestion not Recently Cooked without opening it', async () => {
      const wrapper = mountPanel([makeSuggestion({ recipeId: 'r1', recipeLastCookedAt: null })])

      await addButton(wrapper).trigger('click')

      expect(wrapper.emitted('add')).toEqual([[expect.objectContaining({ recipeId: 'r1' })]])
      expect(pushMock).not.toHaveBeenCalled()
    })

    it('"+" asks before adding a Recently Cooked suggestion', async () => {
      const wrapper = mountPanel([makeSuggestion({ recipeLastCookedAt: new Date().toISOString() })])

      await addButton(wrapper).trigger('click')

      expect(wrapper.emitted('add')).toBeUndefined()
      expect(recentlyCookedDialog(wrapper).props('modelValue')).toBe(true)
    })

    it('opens the recipe when tapped anywhere but "+"', async () => {
      const wrapper = mountPanel([makeSuggestion({ recipeId: 'r1', recipeName: 'Stew' })])

      await wrapper.findAll('div').find(el => el.text() === 'Stew').trigger('click')

      expect(pushMock).toHaveBeenCalledWith({ name: 'recipe-detail', params: { id: 'r1' } })
      expect(wrapper.emitted('add')).toBeUndefined()
    })
  })
})
