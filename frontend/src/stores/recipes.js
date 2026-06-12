import { defineStore } from 'pinia'
import { ref } from 'vue'
import api, { assetUrl } from '@/services/api'

function normalizeImageUrl(recipe) {
  if (!recipe) return recipe
  return { ...recipe, imageUrl: assetUrl(recipe.imageUrl) }
}

export const useRecipeStore = defineStore('recipes', () => {
  const recipes       = ref([])   // RecipeListItemResponse[]
  const currentRecipe = ref(null) // RecipeDetailResponse | null
  const loading       = ref(false)
  const error         = ref(null)

  // ── Scrape state ───────────────────────────────────────────────────────────
  const scrapePreview  = ref(null)  // ScrapePreviewResponse | null
  const scrapeError    = ref(null)  // string | null
  const scrapeLoading  = ref(false)
  const confirmLoading = ref(false)

  // ── Queries ────────────────────────────────────────────────────────────────

  async function fetchRecipes(params = {}) {
    loading.value = true
    error.value   = null
    try {
      const { data } = await api.get('/recipes', { params })
      recipes.value = data.map(normalizeImageUrl)
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
      currentRecipe.value = normalizeImageUrl(data)
      return currentRecipe.value
    } catch (e) {
      // 404 → genuine "not found" (empty state); other failures → error state.
      if (e.status !== 404) error.value = e.message
      return null
    } finally {
      loading.value = false
    }
  }

  // ── Mutations ──────────────────────────────────────────────────────────────

  async function createRecipe(payload) {
    const { data } = await api.post('/recipes', payload)
    const normalized = normalizeImageUrl(data)
    recipes.value.unshift(normalized)
    return normalized
  }

  async function updateRecipe(id, payload) {
    const { data } = await api.put(`/recipes/${id}`, payload)
    const normalized = normalizeImageUrl(data)
    const idx = recipes.value.findIndex(r => r.id === id)
    if (idx !== -1) recipes.value[idx] = normalized
    if (currentRecipe.value?.id === id) currentRecipe.value = normalized
    return normalized
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
    const absUrl = assetUrl(data.imageUrl)
    const listItem = recipes.value.find(r => r.id === id)
    if (listItem) listItem.imageUrl = absUrl
    if (currentRecipe.value?.id === id) currentRecipe.value.imageUrl = absUrl
    return absUrl
  }

  async function markCooked(id) {
    const { data } = await api.post(`/recipes/${id}/cook`)
    const normalized = normalizeImageUrl(data)
    const idx = recipes.value.findIndex(r => r.id === id)
    if (idx !== -1) recipes.value[idx].lastCookedAt = data.lastCookedAt
    if (currentRecipe.value?.id === id) currentRecipe.value = normalized
    return normalized
  }

  // ── Scrape actions ─────────────────────────────────────────────────────────

  async function scrapeRecipe(url) {
    scrapeLoading.value = true
    scrapeError.value   = null
    try {
      const { data } = await api.post('/recipes/scrape', { url })
      scrapePreview.value = data
    } catch (e) {
      scrapeError.value = e.response?.data?.detail ?? e.message ?? 'Failed to extract recipe.'
    } finally {
      scrapeLoading.value = false
    }
  }

  async function confirmScrape(payload) {
    confirmLoading.value = true
    try {
      const { data } = await api.post('/recipes/scrape/confirm', payload)
      const normalized = normalizeImageUrl(data)
      recipes.value.unshift(normalized)
      return normalized.id
    } finally {
      confirmLoading.value = false
    }
  }

  function clearScrapePreview() {
    scrapePreview.value = null
    scrapeError.value   = null
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
    scrapePreview, scrapeError, scrapeLoading, confirmLoading,
    scrapeRecipe, confirmScrape, clearScrapePreview,
  }
})
