// The built-in Themes, defined as data: `plugins/vuetify.js` registers them and the Theme Admin Tool lists them.
// A Theme sets the 12 tokens below; Vuetify derives each `on-<token>` text colour for contrast itself.

/** The colour tokens every Theme sets. CSS reads one as `rgb(var(--v-theme-<token>))`. */
export const THEME_TOKENS = [
  'primary', 'secondary', 'surface', 'background', 'error', 'success', 'warning', 'info',
  'muted',        // de-emphasised icons: empty and error states, bullets, placeholder icons
  'placeholder',  // the background behind a missing recipe image or avatar
  'border',       // hairline borders and soft shadows
  'highlight',    // a solid tint marking the current step or a highlighted ingredient
]

export const BUILT_IN_THEMES = [
  {
    id: 'recipeLight',
    name: 'Light',
    dark: false,
    colors: {
      primary:     '#2E7D32',   // Fresh green — primary actions
      secondary:   '#F57C00',   // Warm orange — accents / meal plan
      surface:     '#FFFFFF',
      background:  '#F5F5F5',
      error:       '#D32F2F',
      success:     '#388E3C',
      warning:     '#F57C00',
      info:        '#1976D2',
      muted:       '#9E9E9E',
      placeholder: '#EEEEEE',
      border:      '#EBEBEB',
      highlight:   '#EEF5EE',
    },
  },
  {
    id: 'recipeDark',
    name: 'Dark',
    dark: true,
    // Every text token meets WCAG AA (4.5:1) on surface and background.
    colors: {
      primary:     '#66BB6A',
      secondary:   '#FFB74D',
      surface:     '#1E1E1E',
      background:  '#121212',
      error:       '#EF5350',
      success:     '#81C784',
      warning:     '#FFB74D',
      info:        '#64B5F6',
      muted:       '#8A8A8A',
      placeholder: '#2C2C2C',
      border:      '#333333',
      highlight:   '#1F3321',
    },
  },
]

/** The Theme a device uses until it chooses one, and when its stored choice no longer exists. */
export const DEFAULT_THEME_ID = 'recipeLight'

/** The `utils/storage.js` key holding the id of the Theme this device uses. */
export const THEME_STORAGE_KEY = 'recipeapp.theme'
