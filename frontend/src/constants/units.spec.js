import { describe, it, expect } from 'vitest'
import {
  MEASUREMENT_UNITS,
  DEFAULT_UNIT,
  formatMeasurement,
  formatSourceMeasurement,
  hasAmount,
  toPayloadMeasurement,
} from './units'

describe('MEASUREMENT_UNITS', () => {
  it('mirrors MeasurementUnit.All on the backend, in the same order', () => {
    // Kept in lockstep with the backend enum, or the pickers offer units the API will reject.
    expect(MEASUREMENT_UNITS).toEqual(['g', 'kg', 'ml', 'L', 'pcs', 'tsp', 'tbsp', 'cup'])
  })

  it('includes cup as a first-class storable unit', () => {
    expect(MEASUREMENT_UNITS).toContain('cup')
  })

  it('offers a default that is itself storable', () => {
    expect(MEASUREMENT_UNITS).toContain(DEFAULT_UNIT)
  })
})

describe('formatMeasurement', () => {
  it('renders amount and unit', () => {
    expect(formatMeasurement(240, 'g')).toBe('240 g')
    expect(formatMeasurement(2, 'cup')).toBe('2 cup')
  })

  it('trims float noise from Batch Size scaling', () => {
    expect(formatMeasurement(0.30000000000000004, 'tsp')).toBe('0.3 tsp')
    expect(formatMeasurement(0.1 + 0.2, 'tsp')).toBe('0.3 tsp')
    expect(formatMeasurement(16.0835, 'g')).toBe('16.08 g')
  })

  it('renders nothing for an ingredient with no stated quantity', () => {
    // "salt" is stored with a null amount and unit, so it shows as its name alone and not "0 g Salt".
    expect(formatMeasurement(null, null)).toBeNull()
    expect(formatMeasurement(undefined, undefined)).toBeNull()
  })

  it('still renders a zero amount, because zero is a quantity', () => {
    // Zero is never how an absent quantity is stored, so it must not be hidden, since seeing "0 g" is how a fabricated amount becomes visible.
    expect(formatMeasurement(0, 'g')).toBe('0 g')
  })
})

describe('toPayloadMeasurement', () => {
  it('sends an entered amount with its unit', () => {
    expect(toPayloadMeasurement(240, 'g')).toEqual({ amount: 240, unit: 'g' })
    expect(toPayloadMeasurement('2.5', 'cup')).toEqual({ amount: 2.5, unit: 'cup' })
  })

  it('sends null for both when the amount field is empty', () => {
    // Never 0: the API rejects a half-set pair, and a zero would sum into a shopping list.
    expect(toPayloadMeasurement('', 'g')).toEqual({ amount: null, unit: null })
    expect(toPayloadMeasurement(null, 'g')).toEqual({ amount: null, unit: null })
    expect(toPayloadMeasurement(undefined, 'tsp')).toEqual({ amount: null, unit: null })
  })

  it('sends a null unit rather than undefined when none is selected', () => {
    expect(toPayloadMeasurement(100, null)).toEqual({ amount: 100, unit: null })
    expect(toPayloadMeasurement(100, undefined)).toEqual({ amount: 100, unit: null })
  })

  it('treats an unparseable amount as no stated quantity', () => {
    expect(toPayloadMeasurement('abc', 'g')).toEqual({ amount: null, unit: null })
  })

  it('treats a field holding only spaces as no stated quantity, never as 0', () => {
    expect(toPayloadMeasurement('   ', 'g')).toEqual({ amount: null, unit: null })
  })
})

describe('formatSourceMeasurement', () => {
  it('shows the source when it differs from the stored unit', () => {
    expect(formatSourceMeasurement(2, 'cups', 'g')).toBe('(2 cups)')
  })

  it('scales the source by the Batch Size multiplier, like the canonical amount', () => {
    expect(formatSourceMeasurement(2, 'cups', 'g', 2)).toBe('(4 cups)')
    expect(formatSourceMeasurement(2, 'cups', 'g', 0.5)).toBe('(1 cups)')
  })

  it('shows nothing when the source unit is the stored unit', () => {
    // Nothing was converted, so there is no second measurement worth the visual noise.
    expect(formatSourceMeasurement(500, 'g', 'g')).toBeNull()
  })

  it('shows nothing for hand-entered rows', () => {
    expect(formatSourceMeasurement(null, null, 'g')).toBeNull()
    expect(formatSourceMeasurement(undefined, undefined, 'cup')).toBeNull()
  })

  it('shows nothing when the source stated an amount but no unit', () => {
    expect(formatSourceMeasurement(2, null, 'g')).toBeNull()
  })

  it('shows the wording alone when the source stated a sizeless unit with no amount', () => {
    expect(formatSourceMeasurement(null, 'to taste', null)).toBe('(to taste)')
  })

  it('leaves a wording-only source unscaled, because there is no amount to scale', () => {
    expect(formatSourceMeasurement(null, 'pinch', null, 2)).toBe('(pinch)')
  })

  it('shows a sizeless source with its amount, scaled by the Batch Size', () => {
    expect(formatSourceMeasurement(1, 'pinch', null)).toBe('(1 pinch)')
    expect(formatSourceMeasurement(1, 'pinch', null, 2)).toBe('(2 pinch)')
  })
})

describe('hasAmount', () => {
  it('is true for an entered number, including a numeric string', () => {
    expect(hasAmount(240)).toBe(true)
    expect(hasAmount('2.5')).toBe(true)
  })

  it('is true for zero, because zero is a quantity', () => {
    expect(hasAmount(0)).toBe(true)
  })

  it('is false for an empty field', () => {
    expect(hasAmount('')).toBe(false)
    expect(hasAmount(null)).toBe(false)
    expect(hasAmount(undefined)).toBe(false)
  })

  it('is false for a field holding only spaces, which would otherwise read as 0', () => {
    expect(hasAmount('   ')).toBe(false)
  })

  it('is false for an unparseable amount', () => {
    expect(hasAmount('abc')).toBe(false)
  })
})
