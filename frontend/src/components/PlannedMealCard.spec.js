import { mount, flushPromises, DOMWrapper } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import PlannedMealCard from './PlannedMealCard.vue'
import { makeMealPlanDetail, makePlannedMeal } from '@/test/factories'
import { servingsFromQuery } from '@/router/recipeRoutes'
import { useMealPlanStore } from '@/stores/mealPlans'
import { useUiStore } from '@/stores/ui'
import { runInTimeZone, WEST_OF_UTC } from '@/test/timeZone'
import api from '@/services/api'

const pushMock = vi.fn()

vi.mock('@/services/api', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
  assetUrl: vi.fn((p) => p ?? ''),
}))

vi.mock('vue-router', async (importActual) => {
  const actual = await importActual()
  return { ...actual, useRouter: () => ({ push: pushMock }) }
})

const vuetify = createVuetify({ components, directives })

beforeEach(() => {
  setActivePinia(createPinia())
  vi.clearAllMocks()
})

function mountCard(plannedMealOverrides = {}) {
  return mount(PlannedMealCard, {
    props: { plannedMeal: makePlannedMeal(plannedMealOverrides), planId: 'plan-1' },
    global: { plugins: [vuetify] },
  })
}

// Vuetify teleports menus and dialogs to the document body.
const page = () => new DOMWrapper(document.body)

function leafTexts(wrapper) {
  return wrapper.findAll('*').filter(el => el.element.childElementCount === 0).map(el => el.text())
}

async function chooseMealOption(wrapper, title) {
  await wrapper.find('button[aria-label="Meal options"]').trigger('click')
  await flushPromises()
  await page().findAll('.v-list-item').find(item => item.text() === title).trigger('click')
  await flushPromises()
}

describe('PlannedMealCard', () => {
  describe('west of UTC', () => {
    runInTimeZone(WEST_OF_UTC)

    it('shows the recipe name, the scheduled day and month as stored, and the Batch Size', () => {
      const wrapper = mountCard({ recipeName: 'Soup', scheduledDate: '2026-06-03', batchSize: 'DOUBLE' })

      expect(leafTexts(wrapper)).toEqual(expect.arrayContaining(['Soup', '3', 'Jun']))
      expect(wrapper.find('[aria-label="Batch 2×"]').exists()).toBe(true)
    })
  })

  it('marks an Unscheduled meal with a question-mark calendar instead of a date', () => {
    const wrapper = mountCard({ scheduledDate: null })

    expect(wrapper.find('.mdi-calendar-question').exists()).toBe(true)
  })

  describe('recipe image', () => {
    const daysAgo = (days) => new Date(Date.now() - days * 86400_000).toISOString()

    it('is greyscale when the recipe is Recently Cooked', () => {
      const wrapper = mountCard({ recipeImageUrl: '/uploads/soup.jpg', recipeLastCookedAt: daysAgo(2) })

      expect(wrapper.find('.v-img').classes()).toContain('grayscale')
    })

    it('keeps its colour when the recipe was cooked over a week ago', () => {
      const wrapper = mountCard({ recipeImageUrl: '/uploads/soup.jpg', recipeLastCookedAt: daysAgo(10) })

      expect(wrapper.find('.v-img').classes()).not.toContain('grayscale')
    })

    it('falls back to a food icon when the recipe has no image', () => {
      const wrapper = mountCard({ recipeImageUrl: null })

      expect(wrapper.find('.v-img').exists()).toBe(false)
      expect(wrapper.find('.mdi-food').exists()).toBe(true)
    })
  })

  describe('tapping the card', () => {
    const tapRecipeName = (wrapper, name) =>
      wrapper.findAll('div').find(el => el.text() === name).trigger('click')

    it('opens the recipe at the Planned Meal’s Batch Size', async () => {
      const wrapper = mountCard({ recipeId: 'r9', recipeName: 'Soup', batchSize: 'HALF' })

      await tapRecipeName(wrapper, 'Soup')

      const [location] = pushMock.mock.calls[0]
      expect(location).toMatchObject({ name: 'recipe-detail', params: { id: 'r9' } })
      // A Half Batch Size of a 4-serving recipe shows 2 servings.
      expect(servingsFromQuery(location.query, 4)).toBe(2)
    })

    it('only closes the Meal options menu while it is open', async () => {
      const wrapper = mountCard({ recipeName: 'Soup' })
      await wrapper.find('button[aria-label="Meal options"]').trigger('click')
      await flushPromises()

      await tapRecipeName(wrapper, 'Soup')

      expect(pushMock).not.toHaveBeenCalled()
    })
  })

  describe('Meal options', () => {
    it('stay on the card when one that opens a dialog is chosen', async () => {
      const wrapper = mountCard()

      await chooseMealOption(wrapper, 'Change batch')

      expect(pushMock).not.toHaveBeenCalled()
    })

    it('Start cooking opens Cooking Mode at the Planned Meal’s Batch Size', async () => {
      const wrapper = mountCard({ recipeId: 'r9', batchSize: 'DOUBLE' })

      await chooseMealOption(wrapper, 'Start cooking')

      expect(pushMock).toHaveBeenCalledTimes(1)
      const [location] = pushMock.mock.calls[0]
      expect(location).toMatchObject({ name: 'recipe-cooking', params: { id: 'r9' } })
      // A Double Batch Size of a 4-serving recipe cooks 8 servings.
      expect(servingsFromQuery(location.query, 4)).toBe(8)
    })

    it('Mark as cooked marks the recipe, refreshes the plan and confirms', async () => {
      api.post.mockResolvedValue({ data: { id: 'r9', lastCookedAt: new Date().toISOString() } })
      api.get.mockResolvedValue({ data: makeMealPlanDetail({ id: 'plan-1' }) })
      const wrapper = mountCard({ recipeId: 'r9', recipeName: 'Stew' })

      await chooseMealOption(wrapper, 'Mark as cooked')

      expect(api.post).toHaveBeenCalledWith('/recipes/r9/cook')
      expect(api.get).toHaveBeenCalledWith('/meal-plans/active')
      expect(useUiStore().snackbar).toMatchObject({ show: true, color: 'success', message: 'Marked “Stew” as cooked' })
    })

    it('Mark as cooked still confirms when only the plan refresh fails', async () => {
      api.post.mockResolvedValue({ data: { id: 'r9', lastCookedAt: new Date().toISOString() } })
      api.get.mockRejectedValue(new Error('Server unavailable'))
      const wrapper = mountCard({ recipeId: 'r9', recipeName: 'Stew' })

      await chooseMealOption(wrapper, 'Mark as cooked')

      expect(useUiStore().snackbar).toMatchObject({ show: true, color: 'success', message: 'Marked “Stew” as cooked' })
      expect(useMealPlanStore().error).toBe('Server unavailable')
    })

    it('Mark as cooked reports a failure', async () => {
      api.post.mockRejectedValue(new Error('Server unavailable'))
      const wrapper = mountCard()

      await chooseMealOption(wrapper, 'Mark as cooked')

      expect(useUiStore().snackbar).toMatchObject({ show: true, color: 'error', message: 'Server unavailable' })
    })

    describe('editing', () => {
      const plannedMealPath = '/meal-plans/plan-1/planned-meals/planned-meal-1'

      beforeEach(() => {
        api.put.mockResolvedValue({ data: {} })
        api.delete.mockResolvedValue({ data: {} })
        api.get.mockResolvedValue({ data: [] })
      })

      function clickButton(label) {
        return page().findAll('button').find(button => button.text() === label).trigger('click')
      }

      it('Change date saves the new date and keeps the Batch Size', async () => {
        const wrapper = mountCard({ scheduledDate: '2026-06-03', batchSize: 'HALF' })

        await chooseMealOption(wrapper, 'Change date')
        await page().find('input[type="date"]').setValue('2026-06-05')
        await clickButton('Save')
        await flushPromises()

        expect(api.put).toHaveBeenCalledWith(plannedMealPath, { scheduledDate: '2026-06-05', batchSize: 'HALF' })
      })

      it('Change batch saves the new Batch Size and keeps the date', async () => {
        const wrapper = mountCard({ scheduledDate: '2026-06-03', batchSize: 'HALF' })

        await chooseMealOption(wrapper, 'Change batch')
        await clickButton('2×')
        await clickButton('Save')
        await flushPromises()

        expect(api.put).toHaveBeenCalledWith(plannedMealPath, { scheduledDate: '2026-06-03', batchSize: 'DOUBLE' })
      })

      it('Remove deletes the Planned Meal from its plan', async () => {
        const wrapper = mountCard()

        await chooseMealOption(wrapper, 'Remove')

        expect(api.delete).toHaveBeenCalledWith(plannedMealPath)
      })
    })
  })
})
