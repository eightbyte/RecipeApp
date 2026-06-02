import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import HomeView from './HomeView.vue'
import { makeMealPlanDetail, makeMealPlanRecipe } from '@/test/factories'

vi.mock('@/services/api', () => ({
  default: { get: vi.fn(), post: vi.fn() },
  assetUrl: vi.fn((p) => p ?? ''),
}))

const vuetify = createVuetify({ components, directives })

let pinia

beforeEach(() => {
  pinia = createPinia()
  setActivePinia(pinia)
  vi.clearAllMocks()
})

async function mountView() {
  const { useMealPlanStore } = await import('@/stores/mealPlans')
  const store = useMealPlanStore()
  vi.spyOn(store, 'fetchActivePlan').mockResolvedValue(undefined)

  const wrapper = mount(HomeView, {
    global: {
      plugins: [vuetify, pinia],
      stubs: {
        RouterLink: { template: '<a><slot/></a>' },
        'router-link': { template: '<a><slot/></a>' },
        teleport: true,
      },
    },
  })
  await flushPromises()
  return { wrapper, store }
}

describe('HomeView', () => {
  it('shows empty state when no active plan', async () => {
    const { wrapper, store } = await mountView()
    store.activePlan = null
    await wrapper.vm.$nextTick()
    expect(wrapper.text()).toContain('No active meal plan')
  })

  it('renders recipe cards from activePlan', async () => {
    const { wrapper, store } = await mountView()
    store.activePlan = makeMealPlanDetail({
      recipes: [
        makeMealPlanRecipe({ id: 'mpr-1', recipeName: 'Soup' }),
        makeMealPlanRecipe({ id: 'mpr-2', recipeName: 'Pasta' }),
      ],
    })
    await wrapper.vm.$nextTick()
    expect(wrapper.text()).toContain('Soup')
    expect(wrapper.text()).toContain('Pasta')
  })

  it('applies grayscale class to image of recently-cooked recipe', async () => {
    const { wrapper, store } = await mountView()
    const recentDate = new Date(Date.now() - 2 * 86400_000).toISOString()
    store.activePlan = makeMealPlanDetail({
      recipes: [
        makeMealPlanRecipe({ id: 'mpr-1', recipeName: 'Soup', recipeImageUrl: '/uploads/soup.jpg', recipeLastCookedAt: recentDate }),
      ],
    })
    await wrapper.vm.$nextTick()

    const img = wrapper.find('.v-img')
    expect(img.classes()).toContain('grayscale')
  })

  it('does NOT apply grayscale when recipe was not recently cooked', async () => {
    const { wrapper, store } = await mountView()
    const oldDate = new Date(Date.now() - 10 * 86400_000).toISOString()
    store.activePlan = makeMealPlanDetail({
      recipes: [
        makeMealPlanRecipe({ id: 'mpr-1', recipeName: 'Soup', recipeImageUrl: '/uploads/soup.jpg', recipeLastCookedAt: oldDate }),
      ],
    })
    await wrapper.vm.$nextTick()

    const img = wrapper.find('.v-img')
    expect(img.classes()).not.toContain('grayscale')
  })
})
