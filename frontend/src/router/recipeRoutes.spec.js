import { describe, it, expect } from 'vitest'
import {
  cookingRoute,
  cookingRouteAtServings,
  recipeRoute,
  recipeRouteAtServings,
  servingsFromQuery,
} from './recipeRoutes'

describe('recipeRoutes', () => {
  it('reads a Batch Size link back as that multiple of the recipe Servings', () => {
    expect(servingsFromQuery(cookingRoute('r1', 'HALF').query, 4)).toBe(2)
    expect(servingsFromQuery(cookingRoute('r1', 'REGULAR').query, 4)).toBe(4)
    expect(servingsFromQuery(cookingRoute('r1', 'DOUBLE').query, 4)).toBe(8)
  })

  it('targets Cooking Mode for the given recipe', () => {
    expect(cookingRoute('r1', 'DOUBLE')).toMatchObject({ name: 'recipe-cooking', params: { id: 'r1' } })
    expect(cookingRouteAtServings('r1', 6)).toMatchObject({ name: 'recipe-cooking', params: { id: 'r1' } })
  })

  it('targets the recipe view for the given recipe', () => {
    expect(recipeRoute('r1', 'DOUBLE')).toMatchObject({ name: 'recipe-detail', params: { id: 'r1' } })
    expect(recipeRouteAtServings('r1', 6)).toMatchObject({ name: 'recipe-detail', params: { id: 'r1' } })
  })

  it('carries the scale the same way to the recipe view as to Cooking Mode', () => {
    expect(servingsFromQuery(recipeRoute('r1', 'DOUBLE').query, 4)).toBe(8)
    expect(servingsFromQuery(recipeRouteAtServings('r1', 1.5).query, 3)).toBe(1.5)
  })

  it('reads a servings link back as the same servings', () => {
    expect(servingsFromQuery(cookingRouteAtServings('r1', 6).query, 4)).toBe(6)
    expect(servingsFromQuery(cookingRouteAtServings('r1', 1.5).query, 3)).toBe(1.5)
  })

  it('reads servings from the string a reloaded URL carries', () => {
    expect(servingsFromQuery({ servings: '1.5' }, 3)).toBe(1.5)
  })

  it('prefers servings over a Batch Size when the query carries both', () => {
    expect(servingsFromQuery({ servings: '6', batch: 'DOUBLE' }, 4)).toBe(6)
  })

  it.each([
    ['missing', undefined],
    ['zero', '0'],
    ['negative', '-2'],
    ['non-numeric', 'lots'],
    ['empty', ''],
    ['infinite', 'Infinity'],
  ])('falls back to the Batch Size when servings is %s', (_, servings) => {
    expect(servingsFromQuery({ servings, batch: 'DOUBLE' }, 4)).toBe(8)
  })

  it('falls back to Regular when the query carries neither servings nor a known Batch Size', () => {
    expect(servingsFromQuery({}, 4)).toBe(4)
    expect(servingsFromQuery({ servings: '0', batch: 'TRIPLE' }, 4)).toBe(4)
  })
})
