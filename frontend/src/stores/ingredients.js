import { defineStore } from 'pinia'
import { ref } from 'vue'
import api from '@/services/api'

export const useIngredientStore = defineStore('ingredients', () => {
  const ingredients = ref([])
  const categories  = ref([])
  const loading     = ref(false)
  const error       = ref(null)

  // Kept apart from `ingredients`, which the recipe form's search overwrites.
  const catalogue       = ref([])
  let   catalogueLoaded = false

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

  async function fetchCatalogue() {
    if (catalogueLoaded) return
    try {
      const { data } = await api.get('/ingredients')
      catalogue.value = data
      catalogueLoaded = true
    } catch {
      // Suggestions are optional, so a failed load leaves the catalogue unloaded for the next call to retry.
    }
  }

  async function fetchCategories() {
    if (categories.value.length) return
    const { data } = await api.get('/ingredients/categories')
    categories.value = data
  }

  async function createIngredient(payload) {
    const { data } = await api.post('/ingredients', payload)
    ingredients.value.push(data)
    catalogue.value.push(data)
    return data
  }

  async function updateIngredient(id, payload) {
    const { data } = await api.put(`/ingredients/${id}`, payload)
    replaceById(ingredients.value, data)
    replaceById(catalogue.value, data)
    return data
  }

  function replaceById(list, ingredient) {
    const idx = list.findIndex(i => i.id === ingredient.id)
    if (idx !== -1) list[idx] = ingredient
  }

  return { ingredients, catalogue, categories, loading, error,
           fetchIngredients, fetchCatalogue, fetchCategories, createIngredient, updateIngredient }
})
