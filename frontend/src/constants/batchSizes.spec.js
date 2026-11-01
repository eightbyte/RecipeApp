import { describe, it, expect } from 'vitest'
import {
  BATCH_SIZES,
  DEFAULT_BATCH_SIZE,
  batchSizeMultiplier,
  formatBatchSize,
  batchSizeColor,
  toBatchSize,
} from './batchSizes'

describe('BATCH_SIZES', () => {
  it('mirrors BatchSize.All on the backend, in the same order', () => {
    // Kept in lockstep with the backend enum, or the toggles offer values the API will reject.
    expect(BATCH_SIZES.map(batchSize => batchSize.value)).toEqual(['HALF', 'REGULAR', 'DOUBLE'])
  })

  it('gives every Batch Size a distinct colour', () => {
    const colors = BATCH_SIZES.map(batchSize => batchSize.color)
    expect(new Set(colors).size).toBe(colors.length)
  })

  it('offers a default that is itself a Batch Size', () => {
    expect(BATCH_SIZES.map(batchSize => batchSize.value)).toContain(DEFAULT_BATCH_SIZE)
  })
})

describe('batchSizeMultiplier', () => {
  it('multiplies the whole recipe', () => {
    expect(batchSizeMultiplier('HALF')).toBe(0.5)
    expect(batchSizeMultiplier('REGULAR')).toBe(1)
    expect(batchSizeMultiplier('DOUBLE')).toBe(2)
  })

  it('falls back to 1 for an unknown value, like BatchSize.Multiplier', () => {
    expect(batchSizeMultiplier('TRIPLE')).toBe(1)
    expect(batchSizeMultiplier(undefined)).toBe(1)
  })
})

describe('formatBatchSize', () => {
  it('labels each Batch Size as a multiple of the recipe', () => {
    expect(formatBatchSize('HALF')).toBe('½×')
    expect(formatBatchSize('REGULAR')).toBe('1×')
    expect(formatBatchSize('DOUBLE')).toBe('2×')
  })

  it('renders an unrecognised value as itself rather than dropping it', () => {
    expect(formatBatchSize('TRIPLE')).toBe('TRIPLE')
  })
})

describe('batchSizeColor', () => {
  it('uses the neutral colour for an unrecognised value', () => {
    expect(batchSizeColor('TRIPLE')).toBe('default')
  })
})

describe('toBatchSize', () => {
  it('keeps a known Batch Size', () => {
    expect(toBatchSize('DOUBLE')).toBe('DOUBLE')
  })

  it('falls back to the default for anything else, such as a hand-edited query string', () => {
    expect(toBatchSize('TRIPLE')).toBe('REGULAR')
    expect(toBatchSize(undefined)).toBe('REGULAR')
    expect(toBatchSize(['HALF'])).toBe('REGULAR')
  })
})
