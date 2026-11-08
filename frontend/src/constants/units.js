/** The storable measurement units, mirroring `MeasurementUnit.All` on the backend in the same order. */
export const MEASUREMENT_UNITS = ['g', 'kg', 'ml', 'L', 'pcs', 'tsp', 'tbsp', 'cup']

/** Default unit for a freshly added ingredient row. */
export const DEFAULT_UNIT = 'g'

/** The unit a count is stored in, mirroring `MeasurementUnit.Piece`. An amount saved with no unit becomes this, and it is never displayed. */
export const COUNT_UNIT = 'pcs'

/** Formats an amount and unit for display, trimming float noise from scaling and hiding `pcs`, and returns `null` when there is no amount to show. */
export function formatMeasurement(amount, unit) {
  if (amount == null) return null
  const rounded = Math.round(amount * 100) / 100
  return unit && unit !== COUNT_UNIT ? `${rounded} ${unit}` : `${rounded}`
}

/**
 * The display parts of one recipe ingredient row, scaled by the servings multiplier: its stored `amount` and its `source`
 * measurement, either of which may be null. A counted row with a Source Measurement shows the source alone, because its
 * count is the same number.
 */
export function formatIngredientMeasurement(ing, multiplier = 1) {
  const source = formatSourceMeasurement(ing.sourceAmount, ing.sourceUnit, ing.unit, multiplier)
  // Short-circuits on a null amount, because `null * 2` is `0` in JavaScript.
  if (ing.amount == null || (ing.unit === COUNT_UNIT && source)) return { amount: null, source }
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
