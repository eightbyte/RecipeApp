/** The storable measurement units, mirroring `MeasurementUnit.All` on the backend in the same order. */
export const MEASUREMENT_UNITS = ['g', 'kg', 'ml', 'L', 'pcs', 'tsp', 'tbsp', 'cup']

/** Default unit for a freshly added ingredient row. */
export const DEFAULT_UNIT = 'g'

/** Formats an amount and unit for display, trimming float noise from Batch Size scaling, and returns `null` when there is no amount to show. */
export function formatMeasurement(amount, unit) {
  if (amount == null) return null
  const rounded = Math.round(amount * 100) / 100
  return unit ? `${rounded} ${unit}` : `${rounded}`
}

/** The `{ amount, unit }` pair to send to the API for one ingredient row, where an empty amount becomes a null amount and unit and is never coerced to `0`. */
export function toPayloadMeasurement(amount, unit) {
  const parsed = amount === '' || amount == null ? Number.NaN : Number(amount)
  return Number.isNaN(parsed)
    ? { amount: null, unit: null }
    : { amount: parsed, unit: unit ?? null }
}

/** Secondary text showing the measurement as an imported source stated it, scaled by the Batch Size multiplier, or null when there is nothing worth showing. */
export function formatSourceMeasurement(sourceAmount, sourceUnit, unit, multiplier = 1) {
  if (sourceAmount == null || !sourceUnit || sourceUnit === unit) return null
  return `(${formatMeasurement(sourceAmount * multiplier, sourceUnit)})`
}
