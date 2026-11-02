import { ref, computed, getCurrentScope, onScopeDispose } from 'vue'
import { useRecipeStore } from '@/stores/recipes'

/** The pause after the last keystroke before a recipe list searches, shared so every list responds alike. */
export const SEARCH_DEBOUNCE_MS = 300

/** Pages one recipe list for `v-infinite-scroll`, with `buildParams` returning its current search, filter and sort params. */
export function useRecipePages(buildParams) {
  const store = useRecipeStore()

  const items        = ref([])
  const totalCount   = ref(0)
  const pageCount    = ref(0)
  const loading      = ref(false)
  const error        = ref(null)
  const loadedParams = ref(null)
  // Rows shift when a recipe is added between loads, so the count can stay short of the total; an empty page still ends the list.
  const reachedEmptyPage = ref(false)

  let params = {}
  // Aborted when a reload replaces the pages, which cancels their request in flight and marks its response stale.
  let controller = new AbortController()
  let pendingLoadMore = null
  let typingTimer = null

  const isComplete = computed(() =>
    pageCount.value > 0 && (reachedEmptyPage.value || items.value.length >= totalCount.value)
  )

  // Unit tests call this outside a component, where there is no scope to clean up with.
  if (getCurrentScope()) {
    onScopeDispose(() => {
      clearTimeout(typingTimer)
      controller.abort()
    })
  }

  /** Starts again from page 1, keeping the shown recipes until page 1 replaces them so the list never flashes empty. */
  async function reload() {
    clearTimeout(typingTimer)
    controller.abort()
    controller = new AbortController()
    const { signal } = controller
    pendingLoadMore = null
    params = buildParams()
    error.value   = null
    loading.value = true
    try {
      const page = await store.fetchRecipePage({ ...params, page: 1 }, { signal })
      if (signal.aborted) return
      replacePages(page.items, page.totalCount, 1)
    } catch (e) {
      if (signal.aborted) return
      replacePages([], 0, 0)
      error.value = e.message
    }
    loadedParams.value = params
    loading.value = false
  }

  function reloadAfterTyping() {
    clearTimeout(typingTimer)
    typingTimer = setTimeout(reload, SEARCH_DEBOUNCE_MS)
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

  return {
    items, totalCount, pageCount, isComplete, loading, error, loadedParams,
    reload, reloadAfterTyping, loadMore,
  }
}
