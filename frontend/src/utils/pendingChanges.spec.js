import { addPendingChange, applyPendingChanges, replaceItemId } from './pendingChanges'
import { makeShoppingListItem } from '@/test/factories'

let nextId = 0
const change = fields => ({ id: `change-${++nextId}`, listId: 'list-1', ...fields })
const tick   = (itemId, isChecked, fields = {}) => change({ kind: 'tick', itemId, isChecked, ...fields })
const add    = (itemId, payload) => change({ kind: 'add', itemId, payload })
const edit   = (itemId, payload) => change({ kind: 'edit', itemId, payload })
const remove = itemId => change({ kind: 'delete', itemId })

const bread     = { name: 'Bread', amount: null, unit: null, category: 'BAKERY' }
const ryeBread  = { name: 'Rye bread', amount: 1, unit: null, category: 'BAKERY' }

function queueOf(...changes) {
  return changes.reduce(addPendingChange, [])
}

const summary = queue => queue.map(c => [c.kind, c.itemId, c.isChecked ?? c.payload?.name])

describe('addPendingChange', () => {
  it('keeps only the last value of a tick then untick', () => {
    expect(summary(queueOf(tick('milk', true), tick('milk', false)))).toEqual([['tick', 'milk', false]])
  })

  it('keeps ticks of different items apart', () => {
    expect(summary(queueOf(tick('milk', true), tick('eggs', true))))
      .toEqual([['tick', 'milk', true], ['tick', 'eggs', true]])
  })

  it('turns an add then an edit into one add carrying the edited values', () => {
    expect(summary(queueOf(add('temp-1', bread), edit('temp-1', ryeBread)))).toEqual([['add', 'temp-1', 'Rye bread']])
  })

  it('cancels an add then a delete, along with every change for that item between them', () => {
    const queue = queueOf(tick('milk', true), add('temp-1', bread), tick('temp-1', true), remove('temp-1'))
    expect(summary(queue)).toEqual([['tick', 'milk', true]])
  })

  it('turns an edit then a delete into the delete alone, and drops a waiting tick too', () => {
    const queue = queueOf(edit('eggs', ryeBread), tick('eggs', true), remove('eggs'))
    expect(summary(queue)).toEqual([['delete', 'eggs', undefined]])
  })

  it('keeps only the last of two edits', () => {
    expect(summary(queueOf(edit('eggs', bread), edit('eggs', ryeBread)))).toEqual([['edit', 'eggs', 'Rye bread']])
  })

  it('never merges into a change that is being sent, so a reply to it can’t swallow a later one', () => {
    const sendingTick = { ...tick('milk', true), sending: true }
    const sendingAdd  = { ...add('temp-1', bread), sending: true }

    const queue = [tick('milk', false), edit('temp-1', ryeBread), remove('temp-1')]
      .reduce(addPendingChange, [sendingTick, sendingAdd])
    expect(summary(queue)).toEqual([
      ['tick', 'milk', true], ['add', 'temp-1', 'Bread'], ['tick', 'milk', false], ['delete', 'temp-1', undefined],
    ])
  })
})

describe('replaceItemId', () => {
  it('moves every change for a temporary id onto the server’s id', () => {
    const queue = replaceItemId([tick('temp-1', true), tick('milk', true)], 'temp-1', 'bread-1')
    expect(queue.map(c => c.itemId)).toEqual(['bread-1', 'milk'])
  })
})

describe('applyPendingChanges', () => {
  const milk = makeShoppingListItem({ id: 'milk', displayName: 'Milk', category: 'DAIRY' })
  const eggs = makeShoppingListItem({ id: 'eggs', displayName: 'Eggs', category: 'OTHER', isCustom: true, amount: 6, unit: 'pcs' })
  const soap = makeShoppingListItem({ id: 'soap', displayName: 'Soap', category: 'OTHER', isCustom: true })

  it('shows ticks, adds, edits and deletes on top of the list', () => {
    const items = applyPendingChanges([milk, eggs, soap], [
      tick('milk', true),
      add('temp-1', bread),
      edit('eggs', { name: 'Duck eggs', amount: 12, unit: 'pcs', category: 'OTHER' }),
      remove('soap'),
    ])

    expect(items).toEqual([
      { ...milk, isChecked: true },
      { ...eggs, displayName: 'Duck eggs', amount: 12 },
      makeShoppingListItem({
        id: 'temp-1', ingredientId: null, displayName: 'Bread', category: 'BAKERY',
        amount: null, unit: null, isCustom: true, displayOrder: 0,
      }),
    ])
  })

  it('moves an edited item to the end when its Category changes, where the server puts it', () => {
    const items = applyPendingChanges([eggs, milk, soap], [edit('eggs', { ...bread, name: 'Eggs', category: 'DAIRY' })])
    expect(items.map(i => i.id)).toEqual(['milk', 'soap', 'eggs'])
  })

  it('applies a change for an item the list lacks as nothing, and leaves the given items unchanged', () => {
    const items = [milk]
    expect(applyPendingChanges(items, [tick('gone', true), edit('gone', bread), remove('gone')])).toEqual([milk])
    applyPendingChanges(items, [tick('milk', true)])
    expect(items[0].isChecked).toBe(false)
  })

  it('adds a waiting add only once, so applying the queue twice changes nothing', () => {
    const once = applyPendingChanges([milk], [add('temp-1', bread)])
    expect(applyPendingChanges(once, [add('temp-1', bread)]).map(i => i.id)).toEqual(['milk', 'temp-1'])
  })
})
