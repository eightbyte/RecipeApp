import { defineStore } from 'pinia'
import { ref } from 'vue'
import api, { assetUrl } from '@/services/api'

function normalizeImageUrl(recipe) {
  if (!recipe) return recipe
  return { ...recipe, imageUrl: assetUrl(recipe.imageUrl) }
}

export const useRecipeStore = defineStore('recipes', () => {
  const currentRecipe = ref(null) // RecipeDetailResponse | null
  const loading       = ref(false)
  const error         = ref(null)

  // ── Scrape state ───────────────────────────────────────────────────────────
  const scrapePreview  = ref(null)  // ScrapePreviewResponse | null
  const scrapeError    = ref(null)  // string | null
  const scrapeLoading  = ref(false)
  const confirmLoading = ref(false)

  // ── Queries ────────────────────────────────────────────────────────────────

  /** One page of the recipe list as `{ items, totalCount }`, leaving the store untouched because each list keeps its own pages. */
  async function fetchRecipePage(params, { signal } = {}) {
    const { data } = await api.get('/recipes', { params, signal })
    return { items: data.items.map(normalizeImageUrl), totalCount: data.totalCount }
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
    return normalizeImageUrl(data)
  }

  async function updateRecipe(id, payload) {
    const { data } = await api.put(`/recipes/${id}`, payload)
    const normalized = normalizeImageUrl(data)
    if (currentRecipe.value?.id === id) currentRecipe.value = normalized
    return normalized
  }

  async function deleteRecipe(id) {
    await api.delete(`/recipes/${id}`)
    if (currentRecipe.value?.id === id) currentRecipe.value = null
  }

  async function uploadImage(id, file) {
    const form = new FormData()
    form.append('file', file)
    const { data } = await api.post(`/recipes/${id}/image`, form, {
      headers: { 'Content-Type': 'multipart/form-data' },
    })
    const absUrl = assetUrl(data.imageUrl)
    if (currentRecipe.value?.id === id) currentRecipe.value.imageUrl = absUrl
    return absUrl
  }

  async function markCooked(id) {
    const { data } = await api.post(`/recipes/${id}/cook`)
    const normalized = normalizeImageUrl(data)
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
      scrapeError.value = e.message ?? 'Failed to extract recipe.'
    } finally {
      scrapeLoading.value = false
    }
  }

  async function confirmScrape(payload) {
    confirmLoading.value = true
    try {
      const { data } = await api.post('/recipes/scrape/confirm', payload)
      return data.id
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
    currentRecipe, loading, error,
    fetchRecipePage, fetchRecipe,
    createRecipe, updateRecipe, deleteRecipe,
    uploadImage, markCooked, isRecentlyCooked,
    scrapePreview, scrapeError, scrapeLoading, confirmLoading,
    scrapeRecipe, confirmScrape, clearScrapePreview,
  }
})
