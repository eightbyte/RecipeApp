import { describe, it, expect } from 'vitest'
import {
  MEASUREMENT_UNITS,
  DEFAULT_UNIT,
  formatIngredientMeasurement,
  formatMeasurement,
  formatSourceMeasurement,
  hasAmount,
  toPayloadMeasurement,
  ENTRY_ONLY_UNITS,
  UNIT_PICKER_ITEMS,
  toFormMeasurement,
  setIngredientUnit,
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

describe('ENTRY_ONLY_UNITS', () => {
  it('mirrors MeasurementUnit.EntryOnly on the backend, in the same order', () => {
    expect(ENTRY_ONLY_UNITS).toEqual(['oz', 'lb', 'fl oz', 'pint', 'quart'])
  })

  it('is never storable', () => {
    expect(ENTRY_ONLY_UNITS.filter(u => MEASUREMENT_UNITS.includes(u))).toEqual([])
  })
})

describe('UNIT_PICKER_ITEMS', () => {
  it('groups every unit the picker offers under Weight, Volume and Count', () => {
    expect(UNIT_PICKER_ITEMS).toEqual([
      { type: 'subheader', title: 'Weight' }, 'g', 'kg', 'oz', 'lb',
      { type: 'subheader', title: 'Volume' }, 'ml', 'L', 'tsp', 'tbsp', 'cup', 'fl oz', 'pint', 'quart',
      { type: 'subheader', title: 'Count' }, 'pcs',
    ])
  })

  it('offers every storable and entry-only unit exactly once', () => {
    const offered = UNIT_PICKER_ITEMS.filter(item => typeof item === 'string')
    expect([...offered].sort()).toEqual([...MEASUREMENT_UNITS, ...ENTRY_ONLY_UNITS].sort())
  })
})

describe('toFormMeasurement', () => {
  it('reopens a row in its entry-only unit at the source amount', () => {
    expect(toFormMeasurement({ amount: 113.398, unit: 'g', sourceAmount: 4, sourceUnit: 'ounces', entryUnit: 'oz' }))
      .toEqual({ amount: 4, unit: 'oz', entryUnit: 'oz' })
  })

  it('reopens any other row as stored', () => {
    expect(toFormMeasurement({ amount: 150, unit: 'g', sourceAmount: 4, sourceUnit: 'oz', entryUnit: null }))
      .toEqual({ amount: 150, unit: 'g', entryUnit: null })
    expect(toFormMeasurement({ amount: null, unit: null, sourceAmount: null, sourceUnit: 'to taste' }))
      .toEqual({ amount: null, unit: null, entryUnit: null })
  })

  it('rounds a row with no entry unit to 2 decimals', () => {
    expect(toFormMeasurement({ amount: 236.588, unit: 'ml', entryUnit: null }))
      .toEqual({ amount: 236.59, unit: 'ml', entryUnit: null })
  })

  it('keeps the source amount of a row opened in its entry unit unrounded', () => {
    expect(toFormMeasurement({ amount: 37.797, unit: 'g', sourceAmount: 1.3333, sourceUnit: 'oz', entryUnit: 'oz' }))
      .toEqual({ amount: 1.3333, unit: 'oz', entryUnit: 'oz' })
  })
})

describe('setIngredientUnit', () => {
  const reopenedInOz = () => ({ amount: 4, unit: 'oz', sourceAmount: 4, sourceUnit: 'ounces', entryUnit: 'oz' })

  it.each(['g', 'cup', null])('clears the source when a row opened in oz moves to %s', unit => {
    const ing = reopenedInOz()
    setIngredientUnit(ing, unit)
    expect(ing).toEqual({ amount: 4, unit, sourceAmount: null, sourceUnit: null, entryUnit: null })
  })

  it('keeps the source when a row opened in oz moves to another entry-only unit', () => {
    const ing = reopenedInOz()
    setIngredientUnit(ing, 'lb')
    expect(ing).toEqual({ ...reopenedInOz(), unit: 'lb' })
  })

  it('keeps a scraped or imported source on a row passing through oz and back', () => {
    const ing = { amount: 113.398, unit: 'g', sourceAmount: 4, sourceUnit: 'oz', entryUnit: null }
    setIngredientUnit(ing, 'oz')
    setIngredientUnit(ing, 'g')
    expect(ing).toEqual({ amount: 113.398, unit: 'g', sourceAmount: 4, sourceUnit: 'oz', entryUnit: null })
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

  it('hides pcs, so a count reads as "2 Eggs"', () => {
    expect(formatMeasurement(2, 'pcs')).toBe('2')
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

describe('formatIngredientMeasurement', () => {
  const row = overrides => ({ amount: 2, unit: 'pcs', sourceAmount: null, sourceUnit: null, ...overrides })

  it('shows a count without its unit', () => {
    expect(formatIngredientMeasurement(row())).toEqual({ amount: '2', source: null })
  })

  it('shows a counted row by its Count Word alone, scaled with servings', () => {
    // "(2 cloves) Garlic", not "2 (2 cloves) Garlic".
    const cloves = row({ sourceAmount: 2, sourceUnit: 'cloves' })
    expect(formatIngredientMeasurement(cloves)).toEqual({ amount: null, source: '(2 cloves)' })
    expect(formatIngredientMeasurement(cloves, 2)).toEqual({ amount: null, source: '(4 cloves)' })
  })

  it('shows both when a counted row was edited away from its source', () => {
    // "2 cloves" changed to 3: hiding the stored 3 would show a count that is not the one summed.
    const edited = row({ amount: 3, sourceAmount: 2, sourceUnit: 'cloves' })
    expect(formatIngredientMeasurement(edited)).toEqual({ amount: '3', source: '(2 cloves)' })
  })

  it('shows a converted row by its stored amount and its source', () => {
    const flour = row({ amount: 120, unit: 'g', sourceAmount: 1, sourceUnit: 'cup' })
    expect(formatIngredientMeasurement(flour, 2)).toEqual({ amount: '240 g', source: '(2 cup)' })
  })

  it('shows nothing for an unquantified row, never 0', () => {
    expect(formatIngredientMeasurement(row({ amount: null, unit: null }), 2))
      .toEqual({ amount: null, source: null })
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
