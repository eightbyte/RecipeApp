import { defineStore } from 'pinia'
import { ref } from 'vue'
import api, { assetUrl } from '@/services/api'

function normalizePlanRecipe(mpr) {
  if (!mpr) return mpr
  return { ...mpr, recipeImageUrl: assetUrl(mpr.recipeImageUrl) }
}

function normalizePlanDetail(plan) {
  if (!plan) return plan
  return {
    ...plan,
    recipes: (plan.recipes ?? []).map(normalizePlanRecipe),
  }
}

function normalizeSuggestion(s) {
  if (!s) return s
  return { ...s, recipeImageUrl: assetUrl(s.recipeImageUrl) }
}

export const useMealPlanStore = defineStore('mealPlans', () => {
  const plans      = ref([])   // MealPlanListItemResponse[]
  const activePlan = ref(null) // MealPlanDetailResponse | null
  const currentPlan = ref(null) // MealPlanDetailResponse | null (builder/detail view)
  const suggestions = ref([])  // SuggestionResponse[]
  const loading    = ref(false)
  const error      = ref(null)

  // ── Queries ────────────────────────────────────────────────────────────────

  async function fetchPlans() {
    loading.value = true
    error.value   = null
    try {
      const { data } = await api.get('/meal-plans')
      plans.value = data
    } catch (e) {
      error.value = e.message
    } finally {
      loading.value = false
    }
  }

  async function fetchActivePlan() {
    loading.value = true
    error.value   = null
    try {
      const { data } = await api.get('/meal-plans/active')
      activePlan.value = normalizePlanDetail(data)
    } catch (e) {
      // 404 means no active plan — treat as null, not an error
      if (e.status === 404) {
        activePlan.value = null
      } else {
        error.value = e.message
      }
    } finally {
      loading.value = false
    }
  }

  async function fetchPlan(id) {
    loading.value = true
    error.value   = null
    currentPlan.value = null
    try {
      const { data } = await api.get(`/meal-plans/${id}`)
      currentPlan.value = normalizePlanDetail(data)
    } catch (e) {
      // 404 → genuine "not found" (empty state); other failures → error state.
      if (e.status !== 404) error.value = e.message
    } finally {
      loading.value = false
    }
  }

  async function fetchSuggestions(planId) {
    try {
      const { data } = await api.get(`/meal-plans/${planId}/suggestions`)
      suggestions.value = data.map(normalizeSuggestion)
    } catch (e) {
      suggestions.value = []
    }
  }

  // ── Mutations — Plans ──────────────────────────────────────────────────────

  async function createPlan(name) {
    const { data } = await api.post('/meal-plans', { name })
    const plan = normalizePlanDetail(data)
    activePlan.value = plan
    currentPlan.value = plan
    plans.value.unshift(data)
    return data.id
  }

  async function renamePlan(id, name) {
    const { data } = await api.put(`/meal-plans/${id}`, { name })
    const plan = normalizePlanDetail(data)
    if (activePlan.value?.id === id) activePlan.value = plan
    if (currentPlan.value?.id === id) currentPlan.value = plan
    const idx = plans.value.findIndex(p => p.id === id)
    if (idx !== -1) plans.value[idx] = { ...plans.value[idx], name }
    return plan
  }

  async function deletePlan(id) {
    await api.delete(`/meal-plans/${id}`)
    plans.value = plans.value.filter(p => p.id !== id)
    if (activePlan.value?.id === id) activePlan.value = null
    if (currentPlan.value?.id === id) currentPlan.value = null
  }

  // ── Mutations — Plan Recipes ───────────────────────────────────────────────

  async function addRecipe(planId, payload) {
    await api.post(`/meal-plans/${planId}/recipes`, payload)
    await _refreshPlan(planId)
    await fetchSuggestions(planId)
  }

  async function updatePlanRecipe(planId, mprId, payload) {
    await api.put(`/meal-plans/${planId}/recipes/${mprId}`, payload)
    await _refreshPlan(planId)
  }

  async function removePlanRecipe(planId, mprId) {
    await api.delete(`/meal-plans/${planId}/recipes/${mprId}`)
    await _refreshPlan(planId)
    await fetchSuggestions(planId)
  }

  async function _refreshPlan(planId) {
    const { data } = await api.get(`/meal-plans/${planId}`)
    const plan = normalizePlanDetail(data)
    if (activePlan.value?.id === planId) activePlan.value = plan
    if (currentPlan.value?.id === planId) currentPlan.value = plan
  }

  return {
    plans, activePlan, currentPlan, suggestions, loading, error,
    fetchPlans, fetchActivePlan, fetchPlan, fetchSuggestions,
    createPlan, renamePlan, deletePlan,
    addRecipe, updatePlanRecipe, removePlanRecipe,
  }
})
