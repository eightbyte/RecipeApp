/**
 * The Pending Change queue's pure rules: how a new change merges into the waiting ones, and how the
 * waiting ones show on top of a list. A change is { id, kind, listId, itemId, ... } with kind one of
 * `tick` ({ isChecked }), `add` ({ payload }), `edit` ({ payload }) or `delete`. A change marked
 * `sending` is on its way to the server and is never merged into.
 */

export const PendingChangeKind = Object.freeze({
  TICK:   'tick',
  ADD:    'add',
  EDIT:   'edit',
  DELETE: 'delete',
})

const waitingFor = (itemId, kinds) => c => !c.sending && c.itemId === itemId && kinds.includes(c.kind)

/** The queue with `change` merged in, so the server only ever receives each item's net change. */
export function addPendingChange(queue, change) {
  const { itemId } = change
  const waitingAdd = queue.find(waitingFor(itemId, [PendingChangeKind.ADD]))

  switch (change.kind) {
    case PendingChangeKind.TICK:
      return [...queue.filter(c => !waitingFor(itemId, [PendingChangeKind.TICK])(c)), change]

    case PendingChangeKind.EDIT:
      if (waitingAdd) return queue.map(c => c === waitingAdd ? { ...c, payload: change.payload } : c)
      return [...queue.filter(c => !waitingFor(itemId, [PendingChangeKind.EDIT])(c)), change]

    case PendingChangeKind.DELETE: {
      const rest = queue.filter(c => c.sending || c.itemId !== itemId)
      // An item the server has never heard of needs nothing sent to remove it.
      return waitingAdd ? rest : [...rest, change]
    }

    default:
      return [...queue, change]
  }
}

/** The queue with every change for `fromId` (an added item's temporary id) moved onto the server's `toId`. */
export function replaceItemId(queue, fromId, toId) {
  return queue.map(c => c.itemId === fromId ? { ...c, itemId: toId } : c)
}

/** A Custom Item as it shows before the server has seen its add. */
export function pendingCustomItem(itemId, payload) {
  return {
    id:           itemId,
    ingredientId: null,
    displayName:  payload.name,
    category:     payload.category,
    amount:       payload.amount,
    unit:         payload.unit,
    isChecked:    false,
    isCustom:     true,
    needsReview:  false,
    displayOrder: 0,
    sources:      [],
  }
}

/** An item with a Custom Item edit applied, moved last when its Category changes, where the server's DisplayOrder puts it. */
export function editedItems(items, itemId, edited) {
  const idx = items.findIndex(i => i.id === itemId)
  if (idx === -1) return items
  if (items[idx].category === edited.category) return items.map((item, i) => i === idx ? edited : item)
  return [...items.slice(0, idx), ...items.slice(idx + 1), edited]
}

/** The items as they show with every waiting change applied on top, leaving `items` itself unchanged. */
export function applyPendingChanges(items, queue) {
  return queue.reduce((shown, c) => {
    const item = shown.find(i => i.id === c.itemId)
    switch (c.kind) {
      case PendingChangeKind.TICK:
        return item ? shown.map(i => i === item ? { ...i, isChecked: c.isChecked } : i) : shown
      case PendingChangeKind.ADD:
        return item ? shown : [...shown, pendingCustomItem(c.itemId, c.payload)]
      case PendingChangeKind.EDIT: {
        if (!item) return shown
        const { name, amount, unit, category } = c.payload
        return editedItems(shown, item.id, { ...item, displayName: name, amount, unit, category })
      }
      case PendingChangeKind.DELETE:
        return shown.filter(i => i.id !== c.itemId)
      default:
        return shown
    }
  }, items)
}
