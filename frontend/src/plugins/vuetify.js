/** Vuetify 3 plugin configuration, with components and directives auto-imported by vite-plugin-vuetify. */
import { createVuetify } from 'vuetify'
import { aliases, mdi } from 'vuetify/iconsets/mdi'
import 'vuetify/styles'
import '@mdi/font/css/materialdesignicons.css'
import { BUILT_IN_THEMES } from '@/constants/themes'
import { readThemeChoice } from '@/utils/themeChoice'

export default createVuetify({
  icons: {
    defaultSet: 'mdi',
    aliases,
    sets: { mdi },
  },

  theme: {
    // Read synchronously, so the first paint is already in this device's Theme.
    defaultTheme: readThemeChoice(),
    themes: Object.fromEntries(BUILT_IN_THEMES.map(({ id, dark, colors }) => [id, { dark, colors }])),
  },

  defaults: {
    VBtn: {
      rounded: 'lg',
      elevation: 0,
    },
    VCard: {
      rounded: 'xl',
      elevation: 1,
    },
    VTextField: {
      variant: 'outlined',
      density: 'comfortable',
    },
    VSelect: {
      variant: 'outlined',
      density: 'comfortable',
    },
  },
})
