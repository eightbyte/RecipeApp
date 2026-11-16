/** Vuetify 3 plugin configuration, with components and directives auto-imported by vite-plugin-vuetify. */
import { createVuetify } from 'vuetify'
import { aliases, mdi } from 'vuetify/iconsets/mdi'
import 'vuetify/styles'
import '@mdi/font/css/materialdesignicons.css'
import { VUETIFY_THEMES } from '@/constants/themes'
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
    themes: VUETIFY_THEMES,
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
