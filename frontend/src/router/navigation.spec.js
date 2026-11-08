import { describe, it, expect } from 'vitest'
import { scrollPosition, viewKey } from './navigation'

const flagged = { keepOnQueryChange: true }

function route(path, query, meta = {}) {
  const search = new URLSearchParams(query).toString()
  return { path, fullPath: search ? `${path}?${search}` : path, meta }
}

describe('viewKey', () => {
  it('keeps a flagged route’s key when only its query changes', () => {
    const before = route('/recipes/r1', { servings: 4 }, flagged)
    const after  = route('/recipes/r1', { servings: 5 }, flagged)
    expect(viewKey(after)).toBe(viewKey(before))
  })

  it('changes a flagged route’s key when its path changes', () => {
    const before = route('/recipes/r1', { servings: 4 }, flagged)
    const after  = route('/recipes/r2', { servings: 4 }, flagged)
    expect(viewKey(after)).not.toBe(viewKey(before))
  })

  it('changes an unflagged route’s key when only its query changes', () => {
    const before = route('/meal-plan/new', {})
    const after  = route('/meal-plan/new', { plan: 'p1' })
    expect(viewKey(after)).not.toBe(viewKey(before))
  })
})

describe('scrollPosition', () => {
  const top = { top: 0 }

  it('keeps the scroll position when only a flagged route’s query changes', () => {
    const from = route('/recipes/r1', { servings: 4 }, flagged)
    const to   = route('/recipes/r1', { servings: 5 }, flagged)
    expect(scrollPosition(to, from)).toBe(false)
  })

  it('scrolls to the top when a flagged route’s path changes', () => {
    const from = route('/recipes/r1', { servings: 4 }, flagged)
    const to   = route('/recipes/r2', { servings: 4 }, flagged)
    expect(scrollPosition(to, from)).toEqual(top)
  })

  it('scrolls to the top when only an unflagged route’s query changes', () => {
    const from = route('/meal-plan/new', {})
    const to   = route('/meal-plan/new', { plan: 'p1' })
    expect(scrollPosition(to, from)).toEqual(top)
  })

  it('scrolls to the top when arriving at a flagged route from another route', () => {
    const from = route('/recipes/r1/cooking', { servings: 4 }, { fullscreen: true })
    const to   = route('/recipes/r1', { servings: 4 }, flagged)
    expect(scrollPosition(to, from)).toEqual(top)
  })
})
