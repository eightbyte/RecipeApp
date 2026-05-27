import { createApp } from 'vue'
import { createPinia } from 'pinia'

import App     from './App.vue'
import router  from './router'
import vuetify from './plugins/vuetify'

// Bootstrap 5 CSS — utility classes complement Vuetify's layout
import 'bootstrap/dist/css/bootstrap.min.css'

// App-level styles (minimal overrides — let Vuetify drive component styling)
import './assets/main.css'

const app = createApp(App)

app.use(createPinia())
app.use(router)
app.use(vuetify)

app.mount('#app')
