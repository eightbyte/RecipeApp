import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import MealPlanBuilderView from './MealPlanBuilderView.vue'
import { makeMealPlanDetail } from '@/test/factories'

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
  vi.spyOn(store, 'createPlan').mockResolvedValue('new-plan-id')
  vi.spyOn(store, 'fetchSuggestions').mockResolvedValue(undefined)

  const wrapper = mount(MealPlanBuilderView, {
    global: {
      plugins: [vuetify, pinia],
      stubs: {
        RouterLink: { template: '<a><slot/></a>' },
        'router-link': { template: '<a><slot/></a>' },
        RecipeBrowser: { template: '<div class="stub-browser" />' },
        SuggestionsPanel: { template: '<div class="stub-suggestions" />' },
        teleport: true,
      },
    },
  })
  await flushPromises()
  return { wrapper, store }
}

describe('MealPlanBuilderView', () => {
  it('shows plan name entry phase initially', async () => {
    const { wrapper } = await mountView()
    expect(wrapper.text()).toContain('Plan name')
    expect(wrapper.text()).toContain('Create plan')
  })

  it('calls createPlan when Create plan button is clicked', async () => {
    const { wrapper, store } = await mountView()

    const input = wrapper.find('input[type="text"], input:not([type])')
    await input.setValue('Week of June')

    const btn = wrapper.findAll('button').find(b => b.text().includes('Create plan'))
    await btn?.trigger('click')
    await flushPromises()

    expect(store.createPlan).toHaveBeenCalledWith('Week of June')
  })

  it('shows builder phase after plan is created', async () => {
    const { wrapper, store } = await mountView()
    store.currentPlan = makeMealPlanDetail({ name: 'My Plan' })
    await wrapper.vm.$nextTick()

    expect(wrapper.text()).toContain('My Plan')
    expect(wrapper.find('.stub-browser').exists()).toBe(true)
  })
})
