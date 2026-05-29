import { defineStore } from 'pinia'
import { ref } from 'vue'
import api from '@/services/api'

export const useIngredientStore = defineStore('ingredients', () => {
  const ingredients = ref([])
  const categories  = ref([])
  const loading     = ref(false)
  const error       = ref(null)

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

  async function fetchCategories() {
    if (categories.value.length) return
    const { data } = await api.get('/ingredients/categories')
    categories.value = data
  }

  async function createIngredient(payload) {
    const { data } = await api.post('/ingredients', payload)
    ingredients.value.push(data)
    return data
  }

  async function updateIngredient(id, payload) {
    const { data } = await api.put(`/ingredients/${id}`, payload)
    const idx = ingredients.value.findIndex(i => i.id === id)
    if (idx !== -1) ingredients.value[idx] = data
    return data
  }

  return { ingredients, categories, loading, error,
           fetchIngredients, fetchCategories, createIngredient, updateIngredient }
})
