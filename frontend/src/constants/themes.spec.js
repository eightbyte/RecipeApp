import { createVuetify } from 'vuetify'
import { BUILT_IN_THEMES, DEFAULT_THEME_ID, THEME_TOKENS, VUETIFY_THEMES } from './themes'

// WCAG 2 relative luminance and contrast ratio, worked independently of the theme module.
function luminance(hex) {
  const [r, g, b] = [1, 3, 5]
    .map((i) => parseInt(hex.slice(i, i + 2), 16) / 255)
    .map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4))
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}

function contrast(a, b) {
  const [lighter, darker] = [luminance(a), luminance(b)].sort((x, y) => y - x)
  return (lighter + 0.05) / (darker + 0.05)
}

describe('built-in Themes', () => {
  it('are Light and Dark, with Light as the default', () => {
    expect(BUILT_IN_THEMES.map((t) => [t.name, t.dark])).toEqual([['Light', false], ['Dark', true]])
    expect(BUILT_IN_THEMES.find((t) => t.id === DEFAULT_THEME_ID).name).toBe('Light')
  })

  it('names the 12 colour tokens', () => {
    expect([...THEME_TOKENS].sort()).toEqual([
      'background', 'border', 'error', 'highlight', 'info', 'muted',
      'placeholder', 'primary', 'secondary', 'success', 'surface', 'warning',
    ])
  })

  it.each(BUILT_IN_THEMES.map((t) => [t.name, t]))('%s sets exactly the 12 tokens, each a hex colour', (_, theme) => {
    expect(Object.keys(theme.colors).sort()).toEqual([...THEME_TOKENS].sort())
    for (const value of Object.values(theme.colors)) expect(value).toMatch(/^#[0-9A-F]{6}$/)
  })

  it("sets Light's colours", () => {
    const light = BUILT_IN_THEMES.find((t) => t.name === 'Light')
    expect(light.colors).toEqual({
      primary: '#2E7D32', secondary: '#F57C00', surface: '#FFFFFF', background: '#F5F5F5',
      error: '#D32F2F', success: '#388E3C', warning: '#F57C00', info: '#1976D2',
      muted: '#9E9E9E', placeholder: '#EEEEEE', border: '#EBEBEB', highlight: '#EEF5EE',
    })
  })

  it.each(['primary', 'secondary', 'error', 'success', 'warning', 'info', 'muted'])(
    "Dark's %s meets WCAG AA (4.5:1) on surface and background",
    (token) => {
      const { colors } = BUILT_IN_THEMES.find((t) => t.name === 'Dark')
      expect(contrast(colors[token], colors.surface)).toBeGreaterThanOrEqual(4.5)
      expect(contrast(colors[token], colors.background)).toBeGreaterThanOrEqual(4.5)
    },
  )

  it("Dark's primary fill gets text from Vuetify that meets WCAG AA (top bar, buttons)", () => {
    const vuetify = createVuetify({ theme: { defaultTheme: 'recipeDark', themes: VUETIFY_THEMES } })
    const colors = vuetify.theme.computedThemes.value.recipeDark.colors
    const onPrimary = colors['on-primary'].replace(/^#(.)(.)(.)$/, '#$1$1$2$2$3$3')
    expect(contrast(colors.primary, onPrimary.toUpperCase())).toBeGreaterThanOrEqual(4.5)
  })
})
