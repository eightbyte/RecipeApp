import { describe, it, expect } from 'vitest'
import { cookingRoute, recipeRoute, servingsFromQuery } from './recipeRoutes'

describe('recipeRoutes', () => {
  it('targets Cooking Mode for the given recipe', () => {
    expect(cookingRoute('r1', 6)).toMatchObject({ name: 'recipe-cooking', params: { id: 'r1' } })
  })

  it('targets the recipe view for the given recipe', () => {
    expect(recipeRoute('r1', 6)).toMatchObject({ name: 'recipe-detail', params: { id: 'r1' } })
  })

  it('carries only the servings', () => {
    expect(recipeRoute('r1', 6).query).toEqual({ servings: 6 })
    expect(cookingRoute('r1', 6).query).toEqual({ servings: 6 })
  })

  it('reads a link back as the same servings', () => {
    expect(servingsFromQuery(cookingRoute('r1', 6).query, 4)).toBe(6)
    expect(servingsFromQuery(recipeRoute('r1', 1.5).query, 3)).toBe(1.5)
  })

  it('reads servings from the string a reloaded URL carries', () => {
    expect(servingsFromQuery({ servings: '1.5' }, 3)).toBe(1.5)
  })

  it.each([
    ['missing', undefined],
    ['zero', '0'],
    ['negative', '-2'],
    ['non-numeric', 'lots'],
    ['empty', ''],
    ['infinite', 'Infinity'],
  ])('falls back to the recipe Servings when servings is %s', (_, servings) => {
    expect(servingsFromQuery({ servings }, 4)).toBe(4)
  })

  it('ignores a Batch Size left in an old link', () => {
    expect(servingsFromQuery({ batch: 'DOUBLE' }, 4)).toBe(4)
  })
})
