import { setActivePinia, createPinia } from 'pinia'
import { useMealPlanStore } from './mealPlans'
import { makeMealPlanDetail, makeMealPlanListItem, makeSuggestion } from '@/test/factories'

vi.mock('@/services/api', () => ({
  default: {
    get:    vi.fn(),
    post:   vi.fn(),
    put:    vi.fn(),
    delete: vi.fn(),
  },
  assetUrl: vi.fn((path) => path ?? ''),
}))

import api, { assetUrl } from '@/services/api'

beforeEach(() => {
  setActivePinia(createPinia())
  vi.clearAllMocks()
})

// ── fetchActivePlan ───────────────────────────────────────────────────────────

describe('fetchActivePlan', () => {
  it('stores the active plan on success', async () => {
    const plan = makeMealPlanDetail({ isActive: true })
    api.get.mockResolvedValue({ data: plan })

    const store = useMealPlanStore()
    await store.fetchActivePlan()

    expect(store.activePlan).toMatchObject({ id: plan.id, isActive: true })
  })

  it('sets activePlan to null on 404 without setting error', async () => {
    api.get.mockRejectedValue({ status: 404, message: 'Not Found' })

    const store = useMealPlanStore()
    await store.fetchActivePlan()

    expect(store.activePlan).toBeNull()
    expect(store.error).toBeNull()
  })

  it('normalises recipeImageUrl on plan recipes', async () => {
    assetUrl.mockImplementation((p) => p ? `http://localhost:5000${p}` : '')
    const plan = makeMealPlanDetail({
      recipes: [{ id: 'mpr-1', recipeId: 'r1', recipeName: 'A', recipeImageUrl: '/uploads/a.jpg', recipeLastCookedAt: null, scheduledDate: null, portionSize: 'REGULAR', displayOrder: 1 }],
    })
    api.get.mockResolvedValue({ data: plan })

    const store = useMealPlanStore()
    await store.fetchActivePlan()

    expect(store.activePlan.recipes[0].recipeImageUrl).toBe('http://localhost:5000/uploads/a.jpg')
  })
})

// ── createPlan ────────────────────────────────────────────────────────────────

describe('createPlan', () => {
  it('calls POST /meal-plans with name', async () => {
    const plan = makeMealPlanDetail({ id: 'new-plan' })
    api.get.mockResolvedValue({ data: plan })
    api.post.mockResolvedValue({ data: plan })

    const store = useMealPlanStore()
    const id = await store.createPlan('My Plan')

    expect(api.post).toHaveBeenCalledWith('/meal-plans', { name: 'My Plan' })
    expect(id).toBe('new-plan')
  })

  it('sets activePlan and currentPlan on success', async () => {
    const plan = makeMealPlanDetail({ id: 'p1', isActive: true })
    api.get.mockResolvedValue({ data: plan })
    api.post.mockResolvedValue({ data: plan })

    const store = useMealPlanStore()
    await store.createPlan('Plan')

    expect(store.activePlan?.id).toBe('p1')
    expect(store.currentPlan?.id).toBe('p1')
  })
})

// ── addRecipe ─────────────────────────────────────────────────────────────────

describe('addRecipe', () => {
  it('calls POST /meal-plans/:id/recipes and refreshes plan', async () => {
    const plan = makeMealPlanDetail({ id: 'plan-1' })
    api.post.mockResolvedValue({ data: {} })
    api.get.mockResolvedValue({ data: plan })

    const store = useMealPlanStore()
    store.currentPlan = plan
    await store.addRecipe('plan-1', { recipeId: 'r1', scheduledDate: null, portionSize: 'REGULAR' })

    expect(api.post).toHaveBeenCalledWith('/meal-plans/plan-1/recipes', expect.any(Object))
    expect(api.get).toHaveBeenCalledWith('/meal-plans/plan-1')
  })
})

// ── updatePlanRecipe ──────────────────────────────────────────────────────────

describe('updatePlanRecipe', () => {
  it('calls PUT /meal-plans/:id/recipes/:mprId', async () => {
    const plan = makeMealPlanDetail({ id: 'plan-1' })
    api.put.mockResolvedValue({ data: {} })
    api.get.mockResolvedValue({ data: plan })

    const store = useMealPlanStore()
    store.currentPlan = plan
    await store.updatePlanRecipe('plan-1', 'mpr-1', { portionSize: 'DOUBLE' })

    expect(api.put).toHaveBeenCalledWith('/meal-plans/plan-1/recipes/mpr-1', { portionSize: 'DOUBLE' })
  })
})

// ── removePlanRecipe ──────────────────────────────────────────────────────────

describe('removePlanRecipe', () => {
  it('calls DELETE /meal-plans/:id/recipes/:mprId', async () => {
    const plan = makeMealPlanDetail({ id: 'plan-1' })
    api.delete.mockResolvedValue({})
    api.get.mockResolvedValue({ data: plan })

    const store = useMealPlanStore()
    store.currentPlan = plan
    await store.removePlanRecipe('plan-1', 'mpr-1')

    expect(api.delete).toHaveBeenCalledWith('/meal-plans/plan-1/recipes/mpr-1')
  })
})

// ── fetchSuggestions ──────────────────────────────────────────────────────────

describe('fetchSuggestions', () => {
  it('stores suggestions and normalises image URLs', async () => {
    assetUrl.mockImplementation((p) => p ? `http://localhost:5000${p}` : '')
    const suggestions = [makeSuggestion({ recipeImageUrl: '/uploads/s.jpg' })]
    api.get.mockResolvedValue({ data: suggestions })

    const store = useMealPlanStore()
    await store.fetchSuggestions('plan-1')

    expect(store.suggestions[0].recipeImageUrl).toBe('http://localhost:5000/uploads/s.jpg')
    expect(api.get).toHaveBeenCalledWith('/meal-plans/plan-1/suggestions')
  })

  it('sets suggestions to empty array on error', async () => {
    api.get.mockRejectedValue({ status: 404 })

    const store = useMealPlanStore()
    store.suggestions = [makeSuggestion()]
    await store.fetchSuggestions('unknown')

    expect(store.suggestions).toHaveLength(0)
  })
})
