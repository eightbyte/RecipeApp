import { defineStore } from 'pinia'
import { ref } from 'vue'
import api from '@/services/api'

export const useIngredientStore = defineStore('ingredients', () => {
  const ingredients = ref([])
  const categories  = ref([])
  const loading     = ref(false)
  const error       = ref(null)

  // Kept apart from `ingredients`, which the recipe form's search overwrites.
  const catalogue        = ref([])
  let   catalogueRequest = null

  // Admin's list, the only one holding Removed Ingredients, which pickers never offer.
  const allIngredients = ref([])

  async function fetchIngredients(search = '') {
    loading.value = true
    error.value   = null
    try {
      const params = search ? { search } : {}
      const { data } = await api.get('/ingredients', { params })
      ingredients.value = data
    } catch (e) {
      error.value = e.message
    } finally {
      loading.value = false
    }
  }

  function fetchCatalogue() {
    catalogueRequest ??= api.get('/ingredients')
      .then(({ data }) => { catalogue.value = data })
      // Suggestions are optional, so a failed load is forgotten for the next call to retry.
      .catch(() => { catalogueRequest = null })
    return catalogueRequest
  }

  async function fetchAllIngredients() {
    const { data } = await api.get('/ingredients', { params: { includeRemoved: true } })
    allIngredients.value = data
  }

  async function fetchIngredient(id) {
    const { data } = await api.get(`/ingredients/${id}`)
    return data
  }

  async function fetchCategories() {
    if (categories.value.length) return
    const { data } = await api.get('/ingredients/categories')
    categories.value = data
  }

  async function fetchUsage(id) {
    const { data } = await api.get(`/ingredients/${id}/usage`)
    return data.recipeCount
  }

  async function createIngredient(payload) {
    const { data } = await api.post('/ingredients', payload)
    ingredients.value.push(data)
    catalogue.value.push(data)
    allIngredients.value.push(data)
    return data
  }

  async function updateIngredient(id, payload) {
    const { data } = await api.put(`/ingredients/${id}`, payload)
    replaceById(ingredients.value, data)
    replaceById(catalogue.value, data)
    replaceById(allIngredients.value, data)
    return data
  }

  async function removeIngredient(id) {
    await api.delete(`/ingredients/${id}`)
    ingredients.value = ingredients.value.filter(i => i.id !== id)
    catalogue.value   = catalogue.value.filter(i => i.id !== id)
    const removed = allIngredients.value.find(i => i.id === id)
    if (removed) replaceById(allIngredients.value, { ...removed, removedAt: new Date().toISOString() })
  }

  async function restoreIngredient(id) {
    const { data } = await api.post(`/ingredients/${id}/restore`)
    replaceById(allIngredients.value, data)
    if (catalogueRequest) catalogue.value.push(data)
    return data
  }

  function replaceById(list, ingredient) {
    const idx = list.findIndex(i => i.id === ingredient.id)
    if (idx !== -1) list[idx] = ingredient
  }

  return { ingredients, catalogue, allIngredients, categories, loading, error,
           fetchIngredients, fetchCatalogue, fetchAllIngredients, fetchIngredient, fetchCategories, fetchUsage,
           createIngredient, updateIngredient, removeIngredient, restoreIngredient }
})
