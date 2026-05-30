import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createRouter, createWebHashHistory } from 'vue-router'
import AppBottomNav from './AppBottomNav.vue'

const vuetify = createVuetify({ components, directives })

const routes = [
  { path: '/', name: 'home', component: { template: '<div/>' } },
  { path: '/recipes', name: 'recipes', component: { template: '<div/>' } },
  { path: '/meal-plan', name: 'meal-plan', component: { template: '<div/>' } },
  { path: '/shopping', name: 'shopping', component: { template: '<div/>' } },
]

// VBottomNavigation requires Vuetify's layout context (provided by VApp).
// Wrap in a root VApp component so the layout provider is available.
async function mountNav() {
  const router = createRouter({ history: createWebHashHistory(), routes })
  await router.push('/')

  const Root = {
    components: { AppBottomNav },
    template: '<v-app><AppBottomNav /></v-app>',
  }

  return mount(Root, {
    global: { plugins: [vuetify, router] },
  })
}

describe('AppBottomNav', () => {
  it('renders 4 navigation buttons', async () => {
    const wrapper = await mountNav()
    const btns = wrapper.findAllComponents({ name: 'VBtn' })
    expect(btns).toHaveLength(4)
  })

  it('recipes button has to prop pointing to recipes route', async () => {
    const wrapper = await mountNav()
    // navItems order: home(0), recipes(1), meal-plan(2), shopping(3)
    const btns = wrapper.findAllComponents({ name: 'VBtn' })
    expect(btns[1].props('to')).toEqual({ name: 'recipes' })
  })

  it('home button has to prop pointing to home route', async () => {
    const wrapper = await mountNav()
    const btns = wrapper.findAllComponents({ name: 'VBtn' })
    expect(btns[0].props('to')).toEqual({ name: 'home' })
  })
})
