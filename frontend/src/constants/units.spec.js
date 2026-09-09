import { describe, it, expect } from 'vitest'
import {
  MEASUREMENT_UNITS,
  DEFAULT_UNIT,
  formatMeasurement,
  formatSourceMeasurement,
  toPayloadMeasurement,
} from './units'

describe('MEASUREMENT_UNITS', () => {
  it('mirrors MeasurementUnit.All on the backend, in the same order', () => {
    // Kept in lockstep with backend/RecipeApp.API/Enums/MeasurementUnit.cs. If this list
    // drifts, the pickers offer units the API will reject (or hide ones it accepts).
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

  it('trims float noise from portion scaling', () => {
    expect(formatMeasurement(0.30000000000000004, 'tsp')).toBe('0.3 tsp')
    expect(formatMeasurement(0.1 + 0.2, 'tsp')).toBe('0.3 tsp')
    expect(formatMeasurement(16.0835, 'g')).toBe('16.08 g')
  })

  it('renders nothing for an ingredient with no stated quantity', () => {
    // "salt" is stored with a null amount and a null unit (Phase 9.1 §3.1). Every call site
    // guards on the return value, so the ingredient shows as its name alone, not "0 g Salt".
    expect(formatMeasurement(null, null)).toBeNull()
    expect(formatMeasurement(undefined, undefined)).toBeNull()
  })

  it('still renders a zero amount, because zero is a quantity', () => {
    // Zero is never how an absent quantity is stored, so it must not be silently hidden —
    // seeing "0 g" is how a fabricated amount that got through becomes visible.
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
})

describe('formatSourceMeasurement', () => {
  it('shows the source when it differs from the stored unit', () => {
    expect(formatSourceMeasurement(2, 'cups', 'g')).toBe('(2 cups)')
  })

  it('scales the source by the portion multiplier, like the canonical amount', () => {
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

  it('shows nothing when only one half of the pair is present', () => {
    expect(formatSourceMeasurement(2, null, 'g')).toBeNull()
    expect(formatSourceMeasurement(null, 'cups', 'g')).toBeNull()
  })
})
