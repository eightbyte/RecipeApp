import { describe, it, expect } from 'vitest'
import {
  MEASUREMENT_UNITS,
  DEFAULT_UNIT,
  formatMeasurement,
  formatSourceMeasurement,
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
