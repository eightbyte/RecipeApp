import { defineStore } from 'pinia'
import { ref } from 'vue'
import api, { assetUrl } from '@/services/api'
import { METRIC_UNIT_SYSTEM, US_UNIT_SYSTEM } from '@/constants/unitSystems'

function normalizeImageUrl(recipe) {
  if (!recipe) return recipe
  return { ...recipe, imageUrl: assetUrl(recipe.imageUrl) }
}

export const useRecipeStore = defineStore('recipes', () => {
  const currentRecipe = ref(null) // RecipeDetailResponse | null
  // The current recipe's US Measurements at one servings count: { recipeId, servings, byIngredientId } | null
  const usMeasurements = ref(null)
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

  /** Fetches a recipe, and in the US Unit System its US Measurements at `servings` (the recipe's own when omitted), so it paints with no Metric flash. */
  async function fetchRecipe(id, { servings = null, unitSystem = METRIC_UNIT_SYSTEM } = {}) {
    loading.value   = true
    error.value     = null
    currentRecipe.value = null
    usMeasurements.value = null
    const inUs = unitSystem === US_UNIT_SYSTEM
    try {
      const params = inUs ? { system: US_UNIT_SYSTEM, ...(servings == null ? {} : { servings }) } : undefined
      const { data } = await api.get(`/recipes/${id}`, { params })
      currentRecipe.value = normalizeImageUrl(data)
      if (inUs) {
        const rows = data.ingredients
          .filter(ing => ing.usMeasurement)
          .map(ing => ({ recipeIngredientId: ing.id, ...ing.usMeasurement }))
        setUsMeasurements(id, servings ?? data.servings, rows)
      }
      return currentRecipe.value
    } catch (e) {
      // 404 → genuine "not found" (empty state); other failures → error state.
      if (e.status !== 404) error.value = e.message
      return null
    } finally {
      loading.value = false
    }
  }

  /** Each row's US Measurement at `servings`, as `[{ recipeIngredientId, amount, unit, fromSource }]`, leaving the store untouched so the caller applies only the latest. */
  async function fetchUsMeasurements(id, servings, { signal } = {}) {
    const { data } = await api.get(`/recipes/${id}/measurements`, {
      params: { servings, system: US_UNIT_SYSTEM },
      signal,
    })
    return data
  }

  /** Records the US Measurements the current recipe reads in at `servings`; rows that read the same in every Unit System are absent. */
  function setUsMeasurements(recipeId, servings, rows) {
    usMeasurements.value = {
      recipeId,
      servings,
      byIngredientId: Object.fromEntries(rows.map(({ recipeIngredientId, ...us }) => [recipeIngredientId, us])),
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
    // The edited amounts make any US Measurements stale, so the recipe view fetches them again.
    if (usMeasurements.value?.recipeId === id) usMeasurements.value = null
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

  function scrapeRecipe(url) {
    return scrape('/recipes/scrape', { url })
  }

  function scrapeRecipeText(text) {
    return scrape('/recipes/scrape/text', { text })
  }

  async function scrape(route, body) {
    scrapeLoading.value = true
    scrapeError.value   = null
    try {
      const { data } = await api.post(route, body)
      scrapePreview.value = data
    } catch (e) {
      scrapeError.value = e.message ?? 'Failed to extract recipe.'
    } finally {
      scrapeLoading.value = false
    }
  }

  /** Saves the preview; resolves with the new recipe's `id` and `photoSkipped`, true when its photo could not be downloaded. */
  async function confirmScrape(payload) {
    confirmLoading.value = true
    try {
      const { data } = await api.post('/recipes/scrape/confirm', payload)
      return { id: data.recipe.id, photoSkipped: data.photoSkipped }
    } finally {
      confirmLoading.value = false
    }
  }

  /** Scraping's LLM provider as `{ provider, state, model }`, read fresh each time because it can start or stop at any moment; throws if the API is unreachable. */
  async function fetchLlmStatus() {
    const { data } = await api.get('/llm/status')
    return data
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
    currentRecipe, loading, error, usMeasurements,
    fetchRecipePage, fetchRecipe, fetchUsMeasurements, setUsMeasurements,
    createRecipe, updateRecipe, deleteRecipe,
    uploadImage, markCooked, isRecentlyCooked,
    scrapePreview, scrapeError, scrapeLoading, confirmLoading,
    scrapeRecipe, scrapeRecipeText, confirmScrape, clearScrapePreview, fetchLlmStatus,
  }
})
