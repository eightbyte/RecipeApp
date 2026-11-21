import { get, set, update } from 'idb-keyval'

export const PENDING_CHANGES_KEY = 'recipeapp.shoppingList.pendingChanges'
export const SNAPSHOT_KEY        = 'recipeapp.shoppingList.snapshot'

// IndexedDB can be missing or refuse to open (a private window), and the list must still work for this session.
const memory = new Map()

// A reactive proxy can't be structured-cloned into IndexedDB.
const plain = value => value === undefined ? undefined : JSON.parse(JSON.stringify(value))

async function read(key) {
  try {
    return await get(key)
  } catch {
    return memory.get(key)
  }
}

/** Replaces `key` with `change(current)` in one IndexedDB transaction, so two tabs never lose each other's update, and resolves to the new value. */
async function change(key, changeValue) {
  let next
  try {
    await update(key, current => (next = plain(changeValue(current))))
  } catch {
    next = plain(changeValue(memory.get(key)))
    memory.set(key, next)
  }
  return next
}

export async function readPendingChanges() {
  return (await read(PENDING_CHANGES_KEY)) ?? []
}

/** Replaces the stored Pending Changes with `changeQueue(queue)`, resolving to the new queue. */
export function updatePendingChanges(changeQueue) {
  return change(PENDING_CHANGES_KEY, queue => changeQueue(queue ?? []))
}

/** The last list the server sent, or undefined when this phone has never saved one. */
export function readSnapshot() {
  return read(SNAPSHOT_KEY)
}

export async function writeSnapshot(list) {
  try {
    await set(SNAPSHOT_KEY, plain(list))
  } catch {
    memory.set(SNAPSHOT_KEY, plain(list))
  }
}

/** Replaces the stored snapshot with `changeList(list)`, leaving no snapshot alone. */
export function updateSnapshot(changeList) {
  return change(SNAPSHOT_KEY, list => list && changeList(list))
}
