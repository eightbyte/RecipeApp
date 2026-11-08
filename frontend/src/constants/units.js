/** The storable measurement units, mirroring `MeasurementUnit.All` on the backend in the same order. */
export const MEASUREMENT_UNITS = ['g', 'kg', 'ml', 'L', 'pcs', 'tsp', 'tbsp', 'cup']

/** The entry-only units, mirroring `MeasurementUnit.EntryOnly` on the backend in the same order, which the API converts to `g` or `ml` on save so none is ever stored. */
export const ENTRY_ONLY_UNITS = ['oz', 'lb', 'fl oz', 'pint', 'quart']

/** Each unit's dimension, mirroring `MeasurementUnit.Table` for the storable units and the metric unit each entry-only unit converts to. */
const UNIT_DIMENSIONS = {
  g: 'Mass', kg: 'Mass', oz: 'Mass', lb: 'Mass',
  ml: 'Volume', L: 'Volume', tsp: 'Volume', tbsp: 'Volume', cup: 'Volume',
  'fl oz': 'Volume', pint: 'Volume', quart: 'Volume',
  pcs: 'Count',
}

const PICKER_GROUPS = [
  { title: 'Weight', dimension: 'Mass' },
  { title: 'Volume', dimension: 'Volume' },
  { title: 'Count',  dimension: 'Count' },
]

/** The Unit picker's items, every storable then entry-only unit under its dimension's subheader. */
export const UNIT_PICKER_ITEMS = PICKER_GROUPS.flatMap(({ title, dimension }) => [
  { type: 'subheader', title },
  ...[...MEASUREMENT_UNITS, ...ENTRY_ONLY_UNITS].filter(unit => UNIT_DIMENSIONS[unit] === dimension),
])

function isEntryOnlyUnit(unit) {
  return ENTRY_ONLY_UNITS.includes(unit)
}

/** The `{ amount, unit, entryUnit }` the recipe form opens a row with, at its source amount in the entry-only unit the API names when there is one. */
export function toFormMeasurement(ing) {
  return ing.entryUnit
    ? { amount: ing.sourceAmount, unit: ing.entryUnit, entryUnit: ing.entryUnit }
    : { amount: ing.amount, unit: ing.unit, entryUnit: null }
}

/** Sets a form row's unit, dropping the Source Measurement once a row opened in an entry-only unit moves to a storable one, because it is then entered by hand. */
export function setIngredientUnit(ing, unit) {
  if (ing.entryUnit && !isEntryOnlyUnit(unit)) {
    ing.sourceAmount = null
    ing.sourceUnit   = null
    ing.entryUnit    = null
  }
  ing.unit = unit
}

/** Default unit for a freshly added ingredient row. */
export const DEFAULT_UNIT = 'g'

/** The unit a count is stored in, mirroring `MeasurementUnit.Piece`, which an amount saved with no unit becomes and which is never displayed. */
export const COUNT_UNIT = 'pcs'

/** Formats an amount and unit for display, trimming float noise from scaling and hiding `pcs`, and returns `null` when there is no amount to show. */
export function formatMeasurement(amount, unit) {
  if (amount == null) return null
  const rounded = Math.round(amount * 100) / 100
  return unit && unit !== COUNT_UNIT ? `${rounded} ${unit}` : `${rounded}`
}

/** The scaled display parts of one recipe ingredient row, its stored `amount` and its `source` measurement, where a count its source states as the same number shows the source alone. */
export function formatIngredientMeasurement(ing, multiplier = 1) {
  const source = formatSourceMeasurement(ing.sourceAmount, ing.sourceUnit, ing.unit, multiplier)
  const sourceSaysTheCount = ing.unit === COUNT_UNIT && source && ing.amount === ing.sourceAmount
  // Short-circuits on a null amount, because `null * 2` is `0` in JavaScript.
  if (ing.amount == null || sourceSaysTheCount) return { amount: null, source }
  return { amount: formatMeasurement(ing.amount * multiplier, ing.unit), source }
}

/** True when a form's amount field holds a number, so an empty, blank or unreadable field counts as no amount, because `Number('  ')` is `0`. */
export function hasAmount(amount) {
  return amount != null && String(amount).trim() !== '' && !Number.isNaN(Number(amount))
}

/** The `{ amount, unit }` pair to send to the API for one ingredient row, where an empty amount becomes a null amount and unit and is never coerced to `0`. */
export function toPayloadMeasurement(amount, unit) {
  return hasAmount(amount)
    ? { amount: Number(amount), unit: unit ?? null }
    : { amount: null, unit: null }
}

/** Secondary text showing the measurement as an imported source stated it, scaled by the servings multiplier, or null when there is nothing worth showing. */
export function formatSourceMeasurement(sourceAmount, sourceUnit, unit, multiplier = 1) {
  if (!sourceUnit || sourceUnit === unit) return null
  // A Sizeless Unit such as "to taste" can arrive with no amount, and the word alone is still worth showing.
  if (sourceAmount == null) return `(${sourceUnit})`
  return `(${formatMeasurement(sourceAmount * multiplier, sourceUnit)})`
}
