import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import SuggestionsPanel from './SuggestionsPanel.vue'
import { makeSuggestion } from '@/test/factories'

const vuetify = createVuetify({ components, directives })

function mountPanel(suggestions = []) {
  return mount(SuggestionsPanel, {
    props: { suggestions },
    global: {
      plugins: [vuetify],
      stubs: { teleport: true },
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

  it('emits add with suggestion when clicked (not recently cooked)', async () => {
    const suggestion = makeSuggestion({ recipeLastCookedAt: null })
    const wrapper = mountPanel([suggestion])

    // Click the card area
    await wrapper.find('.v-card').trigger('click')
    expect(wrapper.emitted('add')).toBeTruthy()
    expect(wrapper.emitted('add')[0][0]).toMatchObject({ recipeId: suggestion.recipeId })
  })
})
