import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import ThemeView from './ThemeView.vue'
import { BUILT_IN_THEMES, THEME_STORAGE_KEY } from '@/constants/themes'

function makeVuetify(defaultTheme) {
  return createVuetify({
    components,
    directives,
    theme: {
      defaultTheme,
      themes: Object.fromEntries(BUILT_IN_THEMES.map(({ id, dark, colors }) => [id, { dark, colors }])),
    },
  })
}

function mountView(defaultTheme = 'recipeLight') {
  const vuetify = makeVuetify(defaultTheme)
  const wrapper = mount(ThemeView, { global: { plugins: [vuetify] } })
  return { wrapper, vuetify }
}

function radioFor(wrapper, name) {
  return wrapper.find(`input[type="radio"][aria-label="Use ${name} on this device"]`)
}

beforeEach(() => {
  window.localStorage.clear()
})

describe('ThemeView', () => {
  it('lists every Theme, with the one in use selected', () => {
    const { wrapper } = mountView('recipeDark')
    expect(wrapper.text()).toContain('Light')
    expect(wrapper.text()).toContain('Dark')
    expect(radioFor(wrapper, 'Light').element.checked).toBe(false)
    expect(radioFor(wrapper, 'Dark').element.checked).toBe(true)
  })

  it('shows a swatch for each of a Theme’s colours', () => {
    const { wrapper } = mountView()
    const swatches = wrapper.findAll('[data-test="swatch"]')
    expect(swatches).toHaveLength(BUILT_IN_THEMES.length * 12)
  })

  it('applies a chosen Theme at once and stores it for this device', async () => {
    const { wrapper, vuetify } = mountView()
    await radioFor(wrapper, 'Dark').setValue(true)

    expect(vuetify.theme.global.name.value).toBe('recipeDark')
    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBe('"recipeDark"')
    expect(radioFor(wrapper, 'Dark').element.checked).toBe(true)
  })

  it('applies a Theme when its row is tapped', async () => {
    const { wrapper, vuetify } = mountView('recipeDark')
    const lightRow = wrapper.findAllComponents({ name: 'VListItem' }).find((row) => row.text().includes('Light'))
    await lightRow.trigger('click')

    expect(vuetify.theme.global.name.value).toBe('recipeLight')
    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBe('"recipeLight"')
  })
})
