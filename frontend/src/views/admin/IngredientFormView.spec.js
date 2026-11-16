import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import IngredientFormView from './IngredientFormView.vue'
import { useUiStore } from '@/stores/ui'
import { makeIngredient } from '@/test/factories'

vi.mock('@/services/api', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
  assetUrl: vi.fn((path) => path ?? ''),
}))

import api from '@/services/api'

const vuetify = createVuetify({ components, directives })
const Blank = { template: '<div/>' }

const onion = makeIngredient({
  id: 'i1', name: 'onion', displayName: 'Onion', category: 'PRODUCE', defaultUnit: 'pcs',
  aliases: ['brown onion'], gramsPerMillilitre: null, isStaple: false, removedAt: null,
})

let router

beforeEach(() => {
  setActivePinia(createPinia())
  vi.clearAllMocks()
  api.get.mockImplementation((url) => {
    if (url === '/ingredients/i1/usage') return Promise.resolve({ data: { recipeCount: 4 } })
    return Promise.resolve({ data: onion })
  })
})

async function mountForm(id = 'i1', ingredient = onion) {
  api.get.mockImplementation((url) => {
    if (url === `/ingredients/${id}/usage`) return Promise.resolve({ data: { recipeCount: 4 } })
    return Promise.resolve({ data: ingredient })
  })
  router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/admin/ingredients', name: 'admin-ingredients', component: Blank },
      { path: '/admin/ingredients/new', name: 'admin-ingredient-new', component: IngredientFormView },
      { path: '/admin/ingredients/:id', name: 'admin-ingredient-edit', component: IngredientFormView, props: true },
    ],
  })
  await router.push(id ? `/admin/ingredients/${id}` : '/admin/ingredients/new')
  const wrapper = mount(IngredientFormView, {
    props: id ? { id } : {},
    global: { plugins: [vuetify, router] },
    attachTo: document.body,
  })
  await flushPromises()
  return wrapper
}

const field = (wrapper, testId) => wrapper.findComponent(`[data-test="${testId}"]`)

async function setField(wrapper, testId, value) {
  field(wrapper, testId).vm.$emit('update:modelValue', value)
  await flushPromises()
}

function button(wrapper, label) {
  return wrapper.findAllComponents({ name: 'VBtn' }).find((b) => b.text() === label)
}

// Dialog content teleports to the body, outside the wrapper.
function dialogButton(label) {
  return [...document.body.querySelectorAll('.v-overlay__content button')].find((b) => b.textContent.trim() === label)
}

// VForm validates, and a dialog opens, on a later task than the event, so one microtask flush is not enough.
async function settle() {
  await flushPromises()
  await new Promise((resolve) => setTimeout(resolve))
  await flushPromises()
}

async function save(wrapper) {
  await wrapper.find('form').trigger('submit')
  await settle()
}

describe('IngredientFormView', () => {
  describe('editing', () => {
    it('opens on the Ingredient\'s values', async () => {
      const wrapper = await mountForm()

      expect(field(wrapper, 'display-name').props('modelValue')).toBe('Onion')
      expect(field(wrapper, 'category').props('modelValue')).toBe('PRODUCE')
      expect(field(wrapper, 'default-unit').props('modelValue')).toBe('pcs')
      expect(field(wrapper, 'aliases').props('modelValue')).toEqual(['brown onion'])
    })

    it('saves every editable field, sending an empty density as null, and returns to the list', async () => {
      api.put.mockResolvedValue({ data: onion })
      const wrapper = await mountForm()

      await setField(wrapper, 'category', 'CANNED')
      await setField(wrapper, 'density', '')
      await setField(wrapper, 'staple', true)
      await save(wrapper)

      expect(api.put).toHaveBeenCalledWith('/ingredients/i1', {
        displayName: 'Onion', category: 'CANNED', defaultUnit: 'pcs',
        aliases: ['brown onion'], gramsPerMillilitre: null, isStaple: true,
      })
      expect(router.currentRoute.value.name).toBe('admin-ingredients')
      expect(useUiStore().snackbar.message).toBe('Onion saved')
    })

    it('sends a typed density as a number', async () => {
      api.put.mockResolvedValue({ data: onion })
      const wrapper = await mountForm()

      await setField(wrapper, 'density', '0.55')
      await save(wrapper)

      expect(api.put.mock.calls[0][1].gramsPerMillilitre).toBe(0.55)
    })

    it('offers the old name as an alias on rename, which can be removed before saving', async () => {
      api.put.mockResolvedValue({ data: onion })
      const wrapper = await mountForm()

      await setField(wrapper, 'display-name', 'Brown Onion ')
      expect(field(wrapper, 'aliases').props('modelValue')).toEqual(['brown onion', 'onion'])

      await setField(wrapper, 'aliases', ['brown onion'])
      await setField(wrapper, 'display-name', 'Yellow Onion')
      expect(field(wrapper, 'aliases').props('modelValue')).toEqual(['brown onion'])

      await save(wrapper)
      expect(api.put.mock.calls[0][1].aliases).toEqual(['brown onion'])
    })

    it('takes the offered alias back when the name returns to the old one', async () => {
      const wrapper = await mountForm()

      await setField(wrapper, 'display-name', 'Red Onion')
      await setField(wrapper, 'display-name', 'ONION')

      expect(field(wrapper, 'aliases').props('modelValue')).toEqual(['brown onion'])
    })

    it('shows a refused save\'s message and stays on the form', async () => {
      api.put.mockRejectedValue({ status: 409, message: "'shallot' is already used by Shallot." })
      const wrapper = await mountForm()

      await setField(wrapper, 'display-name', 'Shallot')
      await save(wrapper)

      expect(wrapper.text()).toContain("'shallot' is already used by Shallot.")
      expect(router.currentRoute.value.name).toBe('admin-ingredient-edit')
    })
  })

  describe('adding', () => {
    it('starts in Other with no Default Unit, and creates the Ingredient', async () => {
      api.post.mockResolvedValue({ data: makeIngredient({ displayName: 'Yuzu' }) })
      const wrapper = await mountForm(null)

      expect(field(wrapper, 'category').props('modelValue')).toBe('OTHER')
      expect(field(wrapper, 'default-unit').props('modelValue')).toBeNull()
      expect(button(wrapper, 'Remove')).toBeUndefined()

      await setField(wrapper, 'display-name', 'Yuzu')
      await save(wrapper)

      expect(api.post).toHaveBeenCalledWith('/ingredients', {
        displayName: 'Yuzu', category: 'OTHER', defaultUnit: null,
        aliases: [], gramsPerMillilitre: null, isStaple: false,
      })
      expect(router.currentRoute.value.name).toBe('admin-ingredients')
    })

    it('does not save without a display name', async () => {
      const wrapper = await mountForm(null)

      await save(wrapper)

      expect(api.post).not.toHaveBeenCalled()
    })
  })

  describe('removing and restoring', () => {
    it('confirms with how many recipes use it, then removes it', async () => {
      api.delete.mockResolvedValue({})
      const wrapper = await mountForm()

      await button(wrapper, 'Remove').trigger('click')
      await settle()

      expect(document.body.textContent).toContain("Used in 4 recipes — they'll keep showing it")

      dialogButton('Remove').click()
      await settle()

      expect(api.delete).toHaveBeenCalledWith('/ingredients/i1')
      expect(router.currentRoute.value.name).toBe('admin-ingredients')
    })

    it('offers Restore instead of Remove for a Removed Ingredient', async () => {
      const removed = { ...onion, removedAt: '2026-11-01T00:00:00Z' }
      api.post.mockResolvedValue({ data: { ...removed, removedAt: null } })
      const wrapper = await mountForm('i1', removed)

      expect(button(wrapper, 'Remove')).toBeUndefined()
      await button(wrapper, 'Restore').trigger('click')
      await flushPromises()

      expect(api.post).toHaveBeenCalledWith('/ingredients/i1/restore')
      expect(useUiStore().snackbar.message).toBe('Onion restored')
    })

    it('shows why a restore was refused', async () => {
      const removed = { ...onion, removedAt: '2026-11-01T00:00:00Z' }
      api.post.mockRejectedValue({ status: 409, message: "'onion' is already used by Onion." })
      const wrapper = await mountForm('i1', removed)

      await button(wrapper, 'Restore').trigger('click')
      await flushPromises()

      expect(wrapper.text()).toContain("'onion' is already used by Onion.")
      expect(button(wrapper, 'Restore')).toBeDefined()
    })
  })
})
