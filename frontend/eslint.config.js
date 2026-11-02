import js from '@eslint/js'
import pluginVue from 'eslint-plugin-vue'
import globals from 'globals'

export default [
  { ignores: ['dist/', 'dev-dist/', 'coverage/'] },
  js.configs.recommended,
  // Essential catches errors only; the formatting rules in "recommended" would restyle the codebase.
  ...pluginVue.configs['flat/essential'],
  {
    languageOptions: {
      globals: { ...globals.browser },
    },
    rules: {
      // A rest sibling is how a test drops one property from a factory object.
      'no-unused-vars': ['error', { ignoreRestSiblings: true }],
      'vue/require-prop-types': 'error',
    },
  },
  {
    files: ['**/*.spec.js', 'src/test/**'],
    languageOptions: {
      globals: { ...globals.vitest, ...globals.node },
    },
  },
  {
    files: ['*.config.js'],
    languageOptions: {
      globals: { ...globals.node },
    },
  },
]
