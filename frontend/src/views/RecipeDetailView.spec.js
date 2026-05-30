import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createPinia, setActivePinia } from 'pinia'
import { useRecipeStore } from '@/stores/recipes'
import RecipeDetailView from './RecipeDetailView.vue'
import { makeRecipeDetail } from '@/test/factories'

vi.mock('@/services/api', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
  assetUrl: vi.fn((p) => p ?? ''),
}))

vi.mock('vue-router', async (importActual) => {
  const actual = await importActual()
  return {
    ...actual,
    useRoute: () => ({ params: { id: 'r1' } }),
    useRouter: () => ({ push: vi.fn() }),
  }
})

const vuetify = createVuetify({ components, directives })

let pinia
let store

beforeEach(() => {
  pinia = createPinia()
  setActivePinia(pinia)
  store = useRecipeStore()
  // Prevent onMounted's fetchRecipe from clearing our pre-set currentRecipe
  vi.spyOn(store, 'fetchRecipe').mockResolvedValue(null)
})

async function mountView(recipeOverrides = {}) {
  store.currentRecipe = makeRecipeDetail(recipeOverrides)
  store.loading = false

  const wrapper = mount(RecipeDetailView, {
    props: { id: 'r1' },
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
  return wrapper
}

describe('RecipeDetailView', () => {
  it('renders the recipe name', async () => {
    const wrapper = await mountView({ name: 'Chocolate Cake' })
    expect(wrapper.text()).toContain('Chocolate Cake')
  })

  it('defaults portion selector to Regular', async () => {
    const wrapper = await mountView()
    expect(wrapper.vm.portionSize).toBe('REGULAR')
  })

  it('doubles ingredient amounts when switching to Double', async () => {
    const wrapper = await mountView()
    wrapper.vm.portionSize = 'DOUBLE'
    await wrapper.vm.$nextTick()
    expect(wrapper.vm.portionMultiplier).toBe(2)
    expect(wrapper.vm.formatAmount(100, 'g')).toBe('200 g')
  })

  it('applies grayscale class when isRecentlyCooked returns true', async () => {
    vi.spyOn(store, 'isRecentlyCooked').mockReturnValue(true)
    const wrapper = await mountView({ lastCookedAt: new Date(Date.now() - 2 * 86400_000).toISOString() })

    const img = wrapper.findComponent({ name: 'VImg' })
    expect(img.classes()).toContain('grayscale')
  })

  it('does not apply grayscale class when not recently cooked', async () => {
    vi.spyOn(store, 'isRecentlyCooked').mockReturnValue(false)
    const wrapper = await mountView({ lastCookedAt: new Date(Date.now() - 30 * 86400_000).toISOString() })

    const img = wrapper.findComponent({ name: 'VImg' })
    expect(img.classes()).not.toContain('grayscale')
  })

  it('calls store.markCooked when markCooked() is invoked', async () => {
    vi.spyOn(store, 'markCooked').mockResolvedValue(makeRecipeDetail())
    const wrapper = await mountView()

    await wrapper.vm.markCooked()

    expect(store.markCooked).toHaveBeenCalledWith(store.currentRecipe.id)
  })

  it('renders steps in the order provided by the store (sorting is the service layer\'s job)', async () => {
    // The service sorts steps before storing them; the component renders in store order.
    const wrapper = await mountView({
      steps: [
        { id: 's1', stepNumber: 1, instruction: 'First step', recipeIngredientIds: [] },
        { id: 's2', stepNumber: 2, instruction: 'Second step', recipeIngredientIds: [] },
        { id: 's3', stepNumber: 3, instruction: 'Third step', recipeIngredientIds: [] },
      ],
    })

    const text = wrapper.text()
    const firstIdx = text.indexOf('First step')
    const secondIdx = text.indexOf('Second step')
    const thirdIdx = text.indexOf('Third step')

    expect(firstIdx).toBeLessThan(secondIdx)
    expect(secondIdx).toBeLessThan(thirdIdx)
  })
})
