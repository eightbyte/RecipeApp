import { ref, computed } from 'vue'
import { useRecipeStore } from '@/stores/recipes'

/** The pause after the last keystroke before a recipe list searches, shared so every list responds alike. */
export const SEARCH_DEBOUNCE_MS = 300

/**
 * One recipe list's pages: `reload` loads page 1, then each `loadMore` appends the next.
 * `buildParams` returns the list's current search, filter and sort params; the server chooses the page size.
 */
export function useRecipePages(buildParams) {
  const store = useRecipeStore()

  const items      = ref([])
  const totalCount = ref(0)
  const pageCount  = ref(0)
  const loading    = ref(false) // page 1 only; later pages show in v-infinite-scroll
  const error      = ref(null)  // why page 1 failed
  // Rows shift when a recipe is added between loads, so the count can stay short of the total; an empty page still ends the list.
  const reachedEmptyPage = ref(false)

  let params = {}
  // Aborted when a reload replaces the pages, which cancels their request in flight and marks its response stale.
  let controller = new AbortController()
  let pendingLoadMore = null

  const isComplete = computed(() =>
    pageCount.value > 0 && (reachedEmptyPage.value || items.value.length >= totalCount.value)
  )

  /**
   * Starts again from page 1 with the current params, keeping the shown recipes until page 1 replaces them.
   * Resolves false when a newer reload replaced this one.
   */
  async function reload() {
    controller.abort()
    controller = new AbortController()
    const { signal } = controller
    pendingLoadMore = null
    params = buildParams()
    error.value   = null
    loading.value = true
    try {
      const page = await store.fetchRecipePage({ ...params, page: 1 }, { signal })
      if (signal.aborted) return false
      replacePages(page.items, page.totalCount, 1)
    } catch (e) {
      if (signal.aborted) return false
      replacePages([], 0, 0)
      error.value = e.message
    }
    loading.value = false
    return true
  }

  function replacePages(newItems, newTotalCount, newPageCount) {
    items.value      = newItems
    totalCount.value = newTotalCount
    pageCount.value  = newPageCount
    reachedEmptyPage.value = false
  }

  /** Appends the next page and resolves the `v-infinite-scroll` status: `ok`, `empty` once every recipe is in, or `error`. */
  function loadMore() {
    if (!pendingLoadMore) {
      const pending = appendNextPage(controller.signal).finally(() => {
        if (pendingLoadMore === pending) pendingLoadMore = null
      })
      pendingLoadMore = pending
    }
    return pendingLoadMore
  }

  // A stale page resolves ok because the scroller that asked for it went away with the reload.
  async function appendNextPage(signal) {
    if (loading.value || pageCount.value === 0 || isComplete.value) return 'empty'
    try {
      const page = await store.fetchRecipePage({ ...params, page: pageCount.value + 1 }, { signal })
      if (signal.aborted) return 'ok'
      const loadedIds = new Set(items.value.map(recipe => recipe.id))
      items.value      = [...items.value, ...page.items.filter(recipe => !loadedIds.has(recipe.id))]
      totalCount.value = page.totalCount
      pageCount.value += 1
      reachedEmptyPage.value = page.items.length === 0
      return isComplete.value ? 'empty' : 'ok'
    } catch {
      return signal.aborted ? 'ok' : 'error'
    }
  }

  return { items, totalCount, pageCount, isComplete, loading, error, reload, loadMore }
}
