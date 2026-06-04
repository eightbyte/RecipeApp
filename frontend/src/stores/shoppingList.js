import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import api from '@/services/api'

export const useShoppingListStore = defineStore('shoppingList', () => {
  const list    = ref(null)  // ShoppingListResponse | null
  const loading = ref(false)
  const error   = ref(null)

  const isStale = computed(() => list.value?.isStale ?? false)

  // ── Actions ────────────────────────────────────────────────────────────────

  async function fetchActive() {
    loading.value = true
    error.value   = null
    try {
      const { data } = await api.get('/shopping-lists/active')
      list.value = data
    } catch (e) {
      if (e.status === 404) {
        list.value = null
      } else {
        error.value = e.message
      }
    } finally {
      loading.value = false
    }
  }

  async function regenerate() {
    loading.value = true
    error.value   = null
    try {
      const { data } = await api.post('/shopping-lists/active/generate')
      list.value = data
    } catch (e) {
      error.value = e.message
    } finally {
      loading.value = false
    }
  }

  async function toggleItem(itemId, isChecked) {
    if (!list.value) return

    // Optimistic flip
    const item = list.value.items.find(i => i.id === itemId)
    if (!item) return
    const previous = item.isChecked
    item.isChecked = isChecked

    try {
      const { data } = await api.put(
        `/shopping-lists/${list.value.id}/items/${itemId}`,
        { isChecked, amount: item.amount, unit: item.unit }
      )
      Object.assign(item, data)
    } catch {
      item.isChecked = previous
    }
  }

  async function updateItem(itemId, payload) {
    if (!list.value) return
    const { data } = await api.put(
      `/shopping-lists/${list.value.id}/items/${itemId}`,
      payload
    )
    const idx = list.value.items.findIndex(i => i.id === itemId)
    if (idx !== -1) list.value.items[idx] = data
    return data
  }

  async function addCustomItem(payload) {
    if (!list.value) return
    const { data } = await api.post(`/shopping-lists/${list.value.id}/items`, payload)
    list.value.items.push(data)
    return data
  }

  async function deleteItem(itemId) {
    if (!list.value) return
    await api.delete(`/shopping-lists/${list.value.id}/items/${itemId}`)
    list.value.items = list.value.items.filter(i => i.id !== itemId)
  }

  return {
    list, loading, error, isStale,
    fetchActive, regenerate, toggleItem, updateItem, addCustomItem, deleteItem,
  }
})
