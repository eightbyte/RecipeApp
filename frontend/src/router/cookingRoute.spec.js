import { describe, it, expect } from 'vitest'
import { cookingRoute, batchSizeFromCookingQuery } from './cookingRoute'

describe('cookingRoute', () => {
  it('reads a link it built back as the same Batch Size', () => {
    for (const batchSize of ['HALF', 'REGULAR', 'DOUBLE']) {
      expect(batchSizeFromCookingQuery(cookingRoute('r1', batchSize).query)).toBe(batchSize)
    }
  })

  it('targets Cooking Mode for the given recipe', () => {
    expect(cookingRoute('r1', 'DOUBLE')).toMatchObject({ name: 'recipe-cooking', params: { id: 'r1' } })
  })

  it('falls back to Regular when the query carries no Batch Size or an unknown one', () => {
    expect(batchSizeFromCookingQuery({})).toBe('REGULAR')
    expect(batchSizeFromCookingQuery(cookingRoute('r1', 'TRIPLE').query)).toBe('REGULAR')
  })
})
