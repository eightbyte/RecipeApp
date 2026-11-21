import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import api from '@/services/api'

const SAVE_TIMEOUT_MS = 10_000

export const useShoppingListStore = defineStore('shoppingList', () => {
  const list    = ref(null)  // ShoppingListResponse | null
  const loading = ref(false)
  const error   = ref(null)

  const isStale = computed(() => list.value?.isStale ?? false)

  // Items with unsaved changes on this phone, counted per id because one item can have several saves in flight.
  const unsavedItemIds = new Map()

  async function whileUnsaved(itemId, save) {
    unsavedItemIds.set(itemId, (unsavedItemIds.get(itemId) ?? 0) + 1)
    try {
      return await save()
    } finally {
      const remaining = unsavedItemIds.get(itemId) - 1
      if (remaining) unsavedItemIds.set(itemId, remaining)
      else unsavedItemIds.delete(itemId)
    }
  }

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

  /** Loads the active list for a List Refresh without touching what is on screen, so the caller decides when to apply it. */
  async function fetchRefresh() {
    try {
      const { data } = await api.get('/shopping-lists/active')
      return data
    } catch (e) {
      if (e.status === 404) return null
      throw e
    }
  }

  /** Applies a List Refresh, keeping on-screen any item with unsaved changes or matched by `keep`; an item the server no longer has is dropped even then. */
  function applyRefresh(latest, { keep = () => false } = {}) {
    if (!latest || !list.value) {
      list.value = latest
      return
    }
    const onScreen = new Map(list.value.items.map(i => [i.id, i]))
    const isKept = id => onScreen.has(id) && (unsavedItemIds.has(id) || keep(id))
    list.value = {
      ...latest,
      items: latest.items.map(i => isKept(i.id) ? onScreen.get(i.id) : i),
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

  /** Flips Purchased at once and saves it, resolving to whether the save succeeded; a failed save flips it back. */
  async function toggleItem(itemId, isChecked) {
    if (!list.value) return false

    // Optimistic flip
    const item = list.value.items.find(i => i.id === itemId)
    if (!item) return false
    const previous = item.isChecked
    item.isChecked = isChecked

    try {
      const { data } = await whileUnsaved(itemId, () => api.put(
        `/shopping-lists/${list.value.id}/items/${itemId}`,
        { isChecked, amount: item.amount, unit: item.unit }
      ))
      Object.assign(item, data)
      return true
    } catch {
      item.isChecked = previous
      return false
    }
  }

  async function updateItem(itemId, payload) {
    if (!list.value) return
    const { data } = await whileUnsaved(itemId, () => api.put(
      `/shopping-lists/${list.value.id}/items/${itemId}`,
      payload
    ))
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

  // The edit sheet locks until the save settles, so it gets a timeout the global client doesn't have.
  async function updateCustomItem(itemId, payload) {
    if (!list.value) return
    const { data } = await whileUnsaved(itemId, () => api.put(
      `/shopping-lists/${list.value.id}/custom-items/${itemId}`,
      payload,
      { timeout: SAVE_TIMEOUT_MS }
    ))
    const idx = list.value.items.findIndex(i => i.id === itemId)
    if (idx === -1) return data

    // A changed Category puts the item last in that Category, where the server's DisplayOrder puts it on reload.
    if (list.value.items[idx].category === data.category) {
      list.value.items[idx] = data
    } else {
      list.value.items.splice(idx, 1)
      list.value.items.push(data)
    }
    return data
  }

  async function deleteItem(itemId) {
    if (!list.value) return
    await whileUnsaved(itemId, () => api.delete(`/shopping-lists/${list.value.id}/items/${itemId}`))
    list.value.items = list.value.items.filter(i => i.id !== itemId)
  }

  return {
    list, loading, error, isStale,
    fetchActive, fetchRefresh, applyRefresh, regenerate, toggleItem, updateItem, addCustomItem, updateCustomItem, deleteItem,
  }
})
