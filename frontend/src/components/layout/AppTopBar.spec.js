import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createRouter, createWebHashHistory } from 'vue-router'
import AppTopBar from './AppTopBar.vue'

const vuetify = createVuetify({ components, directives })

const Blank = { template: '<div/>' }

const routes = [
  { path: '/', name: 'home', component: Blank, meta: { title: 'Home' } },
  { path: '/recipes/:id', name: 'recipe-detail', component: Blank, meta: { title: 'Recipe' } },
  {
    path: '/admin',
    meta: { admin: true },
    children: [
      { path: '', name: 'admin', component: Blank, meta: { title: 'Admin' } },
      { path: 'costs', name: 'admin-costs', component: Blank, meta: { title: 'Costs', section: 'Reports' } },
    ],
  },
]

// VAppBar requires Vuetify's layout context, so wrap it in a root VApp.
async function mountTopBar(path) {
  const router = createRouter({ history: createWebHashHistory(), routes })
  await router.push(path)

  const Root = {
    components: { AppTopBar },
    template: '<v-app><AppTopBar /></v-app>',
  }

  return mount(Root, { global: { plugins: [vuetify, router] } })
}

const gear = (wrapper) =>
  wrapper.findAllComponents({ name: 'VBtn' }).find((btn) => btn.props('icon') === 'mdi-cog-outline')

describe('AppTopBar', () => {
  it('shows a gear that opens Admin on a top-level route', async () => {
    const wrapper = await mountTopBar('/')
    expect(gear(wrapper).props('to')).toEqual({ name: 'admin' })
  })

  it('shows the gear on a nested route', async () => {
    const wrapper = await mountTopBar('/recipes/r1')
    expect(gear(wrapper)).toBeDefined()
  })

  it('hides the gear on the Admin landing list', async () => {
    const wrapper = await mountTopBar('/admin')
    expect(gear(wrapper)).toBeUndefined()
  })

  it('hides the gear inside an Admin Tool', async () => {
    const wrapper = await mountTopBar('/admin/costs')
    expect(gear(wrapper)).toBeUndefined()
  })
})
