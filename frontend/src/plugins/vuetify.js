/**
 * Vuetify 3 plugin configuration.
 * Components and directives are auto-imported by vite-plugin-vuetify.
 */
import { createVuetify } from 'vuetify'
import { aliases, mdi } from 'vuetify/iconsets/mdi'
import 'vuetify/styles'
import '@mdi/font/css/materialdesignicons.css'

export default createVuetify({
  icons: {
    defaultSet: 'mdi',
    aliases,
    sets: { mdi },
  },

  theme: {
    defaultTheme: 'recipeLight',
    themes: {
      recipeLight: {
        dark: false,
        colors: {
          primary:    '#2E7D32',   // Fresh green — primary actions
          secondary:  '#F57C00',   // Warm orange — accents / meal plan
          surface:    '#FFFFFF',
          background: '#F5F5F5',
          error:      '#D32F2F',
          success:    '#388E3C',
          warning:    '#F57C00',
          info:       '#1976D2',
        },
      },
    },
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
