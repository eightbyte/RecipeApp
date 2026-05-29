import { defineStore } from 'pinia'
import { ref } from 'vue'
import api from '@/services/api'

export const useRecipeStore = defineStore('recipes', () => {
  const recipes      = ref([])   // RecipeListItemResponse[]
  const currentRecipe = ref(null) // RecipeDetailResponse | null
  const loading      = ref(false)
  const error        = ref(null)

  // ── Queries ────────────────────────────────────────────────────────────────

  async function fetchRecipes(params = {}) {
    loading.value = true
    error.value   = null
    try {
      const { data } = await api.get('/recipes', { params })
      recipes.value = data
    } catch (e) {
      error.value = e.message
    } finally {
      loading.value = false
    }
  }

  async function fetchRecipe(id) {
    loading.value   = true
    error.value     = null
    currentRecipe.value = null
    try {
      const { data } = await api.get(`/recipes/${id}`)
      currentRecipe.value = data
      return data
    } catch (e) {
      error.value = e.message
      return null
    } finally {
      loading.value = false
    }
  }

  // ── Mutations ──────────────────────────────────────────────────────────────

  async function createRecipe(payload) {
    const { data } = await api.post('/recipes', payload)
    recipes.value.unshift(data)
    return data
  }

  async function updateRecipe(id, payload) {
    const { data } = await api.put(`/recipes/${id}`, payload)
    const idx = recipes.value.findIndex(r => r.id === id)
    if (idx !== -1) recipes.value[idx] = data
    if (currentRecipe.value?.id === id) currentRecipe.value = data
    return data
  }

  async function deleteRecipe(id) {
    await api.delete(`/recipes/${id}`)
    recipes.value = recipes.value.filter(r => r.id !== id)
    if (currentRecipe.value?.id === id) currentRecipe.value = null
  }

  async function uploadImage(id, file) {
    const form = new FormData()
    form.append('file', file)
    const { data } = await api.post(`/recipes/${id}/image`, form, {
      headers: { 'Content-Type': 'multipart/form-data' },
    })
    // Update imageUrl on the cached list item and detail
    const listItem = recipes.value.find(r => r.id === id)
    if (listItem) listItem.imageUrl = data.imageUrl
    if (currentRecipe.value?.id === id) currentRecipe.value.imageUrl = data.imageUrl
    return data.imageUrl
  }

  async function markCooked(id) {
    const { data } = await api.post(`/recipes/${id}/cook`)
    const idx = recipes.value.findIndex(r => r.id === id)
    if (idx !== -1) recipes.value[idx].lastCookedAt = data.lastCookedAt
    if (currentRecipe.value?.id === id) currentRecipe.value = data
    return data
  }

  // ── Helpers ────────────────────────────────────────────────────────────────

  function isRecentlyCooked(recipe, days = 7) {
    if (!recipe?.lastCookedAt) return false
    const cutoff = Date.now() - days * 24 * 60 * 60 * 1000
    return new Date(recipe.lastCookedAt).getTime() > cutoff
  }

  return {
    recipes, currentRecipe, loading, error,
    fetchRecipes, fetchRecipe,
    createRecipe, updateRecipe, deleteRecipe,
    uploadImage, markCooked, isRecentlyCooked,
  }
})
