import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import api from '@/services/api'
import { useUiStore } from '@/stores/ui'
import {
  PendingChangeKind, addPendingChange, applyPendingChanges, editedItems, pendingCustomItem, replaceItemId,
} from '@/utils/pendingChanges'
import {
  readPendingChanges, readSnapshot, updatePendingChanges, updateSnapshot, writeSnapshot,
} from '@/utils/shoppingListCache'

// A request in a shop with poor signal can hang rather than fail, and a hung send would hold up the whole queue.
const REQUEST_TIMEOUT_MS = 10_000

export const RETRY_FIRST_MS = 5_000
export const RETRY_MAX_MS   = 60_000

// The Web Lock that lets only one tab on this phone send the queue at a time.
const SEND_LOCK = 'recipeapp.shoppingList.send'

const isNoResponse = e => e?.status === undefined
const isServerError = e => e?.status >= 500
const isResponse = e => !isNoResponse(e) && !isServerError(e)

const plural = (n, one, many) => n === 1 ? one : many

export const useShoppingListStore = defineStore('shoppingList', () => {
  const ui = useUiStore()

  const list    = ref(null)  // ShoppingListResponse | null
  const loading = ref(false)
  const error   = ref(null)

  /** Set when a write or refresh gets no response, or the browser says it is offline; cleared by the next response. */
  const offline = ref(false)
  /** This tab's copy of the Pending Changes, the writes made on this phone that the server hasn't confirmed yet. */
  const pendingChanges = ref([])
  const sending = ref(false)

  const isStale      = computed(() => list.value?.isStale ?? false)
  const pendingCount = computed(() => pendingChanges.value.length)

  function noteResponse() {
    offline.value = false
  }

  function noteFailure(e) {
    if (isNoResponse(e)) offline.value = true
    else if (isResponse(e)) noteResponse()
  }

  function markOffline() {
    offline.value = true
  }

  // ── Loading ────────────────────────────────────────────────────────────────

  /** The list as it shows on this phone: `latest` with this phone's Pending Changes for it applied on top. */
  function withPendingChanges(latest) {
    const forList = pendingChanges.value.filter(c => c.listId === latest.id)
    return { ...latest, items: applyPendingChanges(latest.items, forList) }
  }

  /** Shows the snapshot saved on this phone with the Pending Changes on top, at once; resolves to whether there was one. */
  async function restore() {
    pendingChanges.value = await readPendingChanges()
    if (list.value) return true
    const snapshot = await readSnapshot()
    if (!snapshot || list.value) return !!list.value
    list.value = withPendingChanges(snapshot)
    return true
  }

  function getActive() {
    return api.get('/shopping-lists/active', { timeout: REQUEST_TIMEOUT_MS })
  }

  async function fetchActive() {
    loading.value = true
    error.value   = null
    try {
      const { data } = await getActive()
      noteResponse()
      writeSnapshot(data)
      list.value = withPendingChanges(data)
    } catch (e) {
      noteFailure(e)
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
      const { data } = await getActive()
      noteResponse()
      return data
    } catch (e) {
      noteFailure(e)
      if (e.status === 404) return null
      throw e
    }
  }

  /** Applies a List Refresh, keeping on-screen any item with Pending Changes or matched by `keep`; an item the server no longer has is dropped even then. */
  function applyRefresh(latest, { keep = () => false } = {}) {
    writeSnapshot(latest)
    if (!latest || !list.value) {
      list.value = latest && withPendingChanges(latest)
      return
    }
    const unsavedIds = new Set(pendingChanges.value.map(c => c.itemId))
    const onScreen = new Map(list.value.items.map(i => [i.id, i]))
    const isKept = id => onScreen.has(id) && (unsavedIds.has(id) || keep(id))
    list.value = withPendingChanges({
      ...latest,
      items: latest.items.map(i => isKept(i.id) ? onScreen.get(i.id) : i),
    })
  }

  async function regenerate() {
    loading.value = true
    error.value   = null
    try {
      const { data } = await api.post('/shopping-lists/active/generate')
      noteResponse()
      writeSnapshot(data)
      list.value = withPendingChanges(data)
    } catch (e) {
      noteFailure(e)
      error.value = e.message
    } finally {
      loading.value = false
    }
  }

  // ── Writes ─────────────────────────────────────────────────────────────────

  /** Queues `change` (merged with any waiting change for its item) and starts sending; resolves once it is stored on this phone. */
  async function queue(change) {
    const pending = { id: crypto.randomUUID(), listId: list.value.id, ...change }
    // Counted at once, so a List Refresh landing before IndexedDB answers still keeps the item.
    pendingChanges.value = addPendingChange(pendingChanges.value, pending)
    pendingChanges.value = await updatePendingChanges(q => addPendingChange(q, pending))
    sendPending()
  }

  function findItem(itemId) {
    return list.value?.items.find(i => i.id === itemId)
  }

  async function toggleItem(itemId, isChecked) {
    const item = findItem(itemId)
    if (!item) return
    item.isChecked = isChecked
    await queue({
      kind: PendingChangeKind.TICK, itemId, isChecked,
      ingredientId: item.ingredientId, isCustom: item.isCustom, displayName: item.displayName,
    })
  }

  async function addCustomItem(payload) {
    if (!list.value) return
    // The server gives a Custom Item its id, so until the add is sent it has this phone's own.
    const itemId = `pending-${crypto.randomUUID()}`
    list.value.items.push(pendingCustomItem(itemId, payload))
    await queue({ kind: PendingChangeKind.ADD, itemId, payload })
  }

  async function updateCustomItem(itemId, payload) {
    const item = findItem(itemId)
    if (!item) return
    const { name, amount, unit, category } = payload
    list.value.items = editedItems(list.value.items, itemId, { ...item, displayName: name, amount, unit, category })
    await queue({ kind: PendingChangeKind.EDIT, itemId, payload, displayName: item.displayName })
  }

  async function deleteItem(itemId) {
    const item = findItem(itemId)
    if (!item) return
    list.value.items = list.value.items.filter(i => i.id !== itemId)
    await queue({ kind: PendingChangeKind.DELETE, itemId, displayName: item.displayName })
  }

  // ── Sending ────────────────────────────────────────────────────────────────

  function request(change) {
    const base = `/shopping-lists/${change.listId}`
    const config = { timeout: REQUEST_TIMEOUT_MS }
    switch (change.kind) {
      case PendingChangeKind.TICK:
        return api.put(`${base}/items/${change.itemId}`, { isChecked: change.isChecked }, config)
      case PendingChangeKind.ADD:
        return api.post(`${base}/items`, change.payload, config)
      case PendingChangeKind.EDIT:
        return api.put(`${base}/custom-items/${change.itemId}`, change.payload, config)
      case PendingChangeKind.DELETE:
        return api.delete(`${base}/items/${change.itemId}`, config)
      default:
        throw new Error(`Unknown Pending Change kind: ${change.kind}`)
    }
  }

  async function storeQueue(changeQueue) {
    pendingChanges.value = await updatePendingChanges(changeQueue)
    return pendingChanges.value
  }

  const without = changeId => q => q.filter(c => c.id !== changeId)
  const marked = (changeId, fields) => q => q.map(c => c.id === changeId ? { ...c, ...fields } : c)

  const isShowing = listId => list.value?.id === listId

  /** Gives an added item the server's id on screen, or drops this phone's copy when a List Refresh already brought the server's. */
  function showServerId(change, serverId) {
    if (!isShowing(change.listId)) return
    const items = list.value.items
    list.value.items = items.some(i => i.id === serverId)
      ? items.filter(i => i.id !== change.itemId)
      : items.map(i => i.id === change.itemId ? { ...i, id: serverId } : i)
  }

  /** Shows the server's copy of a confirmed item, unless a later Pending Change for it is still waiting. */
  function showConfirmed(change, data) {
    if (!isShowing(change.listId) || change.kind === PendingChangeKind.DELETE) return
    if (pendingChanges.value.some(c => c.itemId === data.id)) return
    list.value.items = editedItems(list.value.items, data.id, data)
  }

  function snapshotConfirmed(change, data) {
    return updateSnapshot(snapshot => {
      if (snapshot.id !== change.listId) return snapshot
      switch (change.kind) {
        case PendingChangeKind.ADD:
          // A List Refresh saved since the add may already hold it.
          return snapshot.items.some(i => i.id === data.id)
            ? { ...snapshot, items: editedItems(snapshot.items, data.id, data) }
            : { ...snapshot, items: [...snapshot.items, data] }
        case PendingChangeKind.DELETE:
          return { ...snapshot, items: snapshot.items.filter(i => i.id !== change.itemId) }
        default:
          return { ...snapshot, items: editedItems(snapshot.items, data.id, data) }
      }
    })
  }

  async function confirm(change, data) {
    let settle = without(change.id)
    if (change.kind === PendingChangeKind.ADD) {
      // Renamed on screen before the queue is, so every change made from here on already carries the server's id.
      showServerId(change, data.id)
      settle = q => replaceItemId(without(change.id)(q), change.itemId, data.id)
    }
    await storeQueue(settle)
    await snapshotConfirmed(change, data)
    showConfirmed(change, data)
  }

  /** A tick whose Generated Item a regeneration replaced moves onto the one new Generated Item for the same Ingredient, if there is exactly one. */
  async function retargetTick(change) {
    const latest = await fetchRefresh()
    const matches = latest?.id === change.listId
      ? latest.items.filter(i => !i.isCustom && i.ingredientId === change.ingredientId)
      : []
    if (matches.length !== 1) return false
    await storeQueue(marked(change.id, { itemId: matches[0].id, sending: false }))
    return true
  }

  /** Settles a change the server answered with a 4xx, by the conflict rules; it never goes back on the queue except as a moved tick. */
  async function resolveRejected(change, e, outcome) {
    if (e.status === 410) {
      const forList = pendingChanges.value.filter(c => c.listId === change.listId)
      outcome.earlierList += forList.length
      await storeQueue(q => q.filter(c => c.listId !== change.listId))
      return
    }
    if (e.status === 404 && change.kind === PendingChangeKind.TICK && !change.isCustom) {
      if (await retargetTick(change)) return
      outcome.orphanedTicks++
    } else if (e.status === 404 && change.kind === PendingChangeKind.DELETE) {
      // Already gone is what the delete wanted.
    } else if (e.status === 404 && change.kind !== PendingChangeKind.ADD) {
      outcome.messages.push(`“${change.displayName}” was removed from the list`)
    } else {
      outcome.messages.push(e.message ?? 'A change to the list couldn’t be saved.')
    }
    await storeQueue(without(change.id))
  }

  /** Sends the queue's head until it is empty or a send gets no answer; resolves to whether the queue emptied. */
  async function sendRound(outcome) {
    for (;;) {
      const [change] = await storeQueue(q => q)
      if (!change) return true
      await storeQueue(marked(change.id, { sending: true }))
      try {
        const { data } = await request(change)
        noteResponse()
        await confirm(change, data)
      } catch (e) {
        noteFailure(e)
        try {
          if (isResponse(e)) {
            await resolveRejected(change, e, outcome)
            continue
          }
        } catch (retargetFailure) {
          // Moving a tick needs the new list, and without it the tick waits like any unanswered change.
          noteFailure(retargetFailure)
        }
        await storeQueue(marked(change.id, { sending: false }))
        return false
      }
    }
  }

  function report(outcome) {
    const { earlierList: n, orphanedTicks: t } = outcome
    if (n) {
      outcome.messages.push(`${n} offline ${plural(n, 'change was', 'changes were')} for an earlier list and ${plural(n, 'wasn’t', 'weren’t')} saved`)
    }
    if (t) {
      outcome.messages.push(t === 1
        ? 'A ticked item left the list when it was regenerated, so its tick wasn’t saved'
        : `${t} ticked items left the list when it was regenerated, so their ticks weren’t saved`)
    }
    for (const message of outcome.messages) ui.notify({ message, color: 'error' })
  }

  function withSendLock(send) {
    return navigator.locks ? navigator.locks.request(SEND_LOCK, send) : send()
  }

  let retryTimer = null
  let retryDelay = 0

  function retryLater() {
    clearTimeout(retryTimer)
    retryDelay = retryDelay ? Math.min(retryDelay * 2, RETRY_MAX_MS) : RETRY_FIRST_MS
    retryTimer = setTimeout(sendPending, retryDelay)
  }

  function stopRetrying() {
    clearTimeout(retryTimer)
    retryTimer = null
    retryDelay = 0
  }

  let round = null
  let again = false

  /** Sends the Pending Changes, from whichever page is open; a call during a send joins it and sends once more after it. */
  function sendPending() {
    if (round) {
      again = true
      return round
    }
    round = (async () => {
      sending.value = true
      const outcome = { earlierList: 0, orphanedTicks: 0, messages: [] }
      let emptied
      try {
        do {
          again = false
          emptied = await withSendLock(() => sendRound(outcome))
        } while (again && emptied)
      } catch {
        emptied = false
      } finally {
        sending.value = false
        round = null
      }
      report(outcome)
      if (emptied) stopRetrying()
      else retryLater()
    })()
    return round
  }

  return {
    list, loading, error, isStale, offline, pendingChanges, pendingCount, sending,
    restore, fetchActive, fetchRefresh, applyRefresh, regenerate,
    toggleItem, addCustomItem, updateCustomItem, deleteItem,
    sendPending, markOffline,
  }
})
