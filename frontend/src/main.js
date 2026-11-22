import { createApp } from 'vue'
import { createPinia } from 'pinia'
import { registerSW } from 'virtual:pwa-register'

import App     from './App.vue'
import router  from './router'
import vuetify from './plugins/vuetify'
import { useUiStore } from './stores/ui'
import { watchForAppUpdates } from './utils/appUpdates'

// App-level styles (minimal overrides — Vuetify is the only styling framework)
import './assets/main.css'

const app = createApp(App)
const pinia = createPinia()

app.use(pinia)
app.use(router)
app.use(vuetify)

app.mount('#app')

watchForAppUpdates(registerSW, useUiStore(pinia))
