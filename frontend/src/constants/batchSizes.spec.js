import { describe, it, expect } from 'vitest'
import { BATCH_SIZES, batchSizeMultiplier } from './batchSizes'

describe('BATCH_SIZES', () => {
  it('offers half, the whole recipe and double, in that order', () => {
    expect(BATCH_SIZES.map(batchSize => batchSize.title)).toEqual(['½×', '1×', '2×'])
  })
})

describe('batchSizeMultiplier', () => {
  it('multiplies the whole recipe', () => {
    expect(batchSizeMultiplier('HALF')).toBe(0.5)
    expect(batchSizeMultiplier('REGULAR')).toBe(1)
    expect(batchSizeMultiplier('DOUBLE')).toBe(2)
  })

  it('falls back to 1 for an unknown value', () => {
    expect(batchSizeMultiplier('TRIPLE')).toBe(1)
    expect(batchSizeMultiplier(undefined)).toBe(1)
  })
})
