import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createRouter, createWebHashHistory } from 'vue-router'
import AdminView from './AdminView.vue'

const vuetify = createVuetify({ components, directives })

const Blank = { template: '<div/>' }

function tool(path, title, section) {
  return {
    path,
    name: `admin-${path}`,
    component: Blank,
    meta: { title, icon: 'mdi-wrench-outline', description: `${title} description`, section },
  }
}

async function mountAdmin(toolRoutes) {
  const router = createRouter({
    history: createWebHashHistory(),
    routes: [
      { path: '/', name: 'home', component: Blank, meta: { title: 'Home' } },
      {
        path: '/admin',
        meta: { admin: true },
        children: [
          { path: '', name: 'admin', component: AdminView, meta: { title: 'Admin' } },
          ...toolRoutes,
        ],
      },
    ],
  })
  await router.push('/admin')

  return mount(AdminView, { global: { plugins: [vuetify, router] } })
}

describe('AdminView', () => {
  it('shows the empty state when there are no Admin Tools', async () => {
    const wrapper = await mountAdmin([])
    expect(wrapper.text()).toContain('No admin tools yet')
    expect(wrapper.findAllComponents({ name: 'VListItem' })).toHaveLength(0)
  })

  it('lists an Admin Tool under its section, linking to its route', async () => {
    const wrapper = await mountAdmin([tool('costs', 'LLM costs', 'Reports')])

    expect(wrapper.findComponent({ name: 'VListSubheader' }).text()).toBe('Reports')
    const item = wrapper.findComponent({ name: 'VListItem' })
    expect(item.text()).toContain('LLM costs')
    expect(item.text()).toContain('LLM costs description')
    expect(item.props('to')).toEqual({ name: 'admin-costs' })
    expect(wrapper.text()).not.toContain('No admin tools yet')
  })

  it('orders sections Data, Appearance, Reports whatever the route order', async () => {
    const wrapper = await mountAdmin([
      tool('costs', 'LLM costs', 'Reports'),
      tool('themes', 'Themes', 'Appearance'),
      tool('ingredients', 'Ingredients', 'Data'),
      tool('categories', 'Categories', 'Data'),
    ])

    const headers = wrapper.findAllComponents({ name: 'VListSubheader' }).map((h) => h.text())
    expect(headers).toEqual(['Data', 'Appearance', 'Reports'])
    const titles = wrapper.findAllComponents({ name: 'VListItemTitle' }).map((t) => t.text())
    expect(titles).toEqual(['Ingredients', 'Categories', 'Themes', 'LLM costs'])
  })
})
