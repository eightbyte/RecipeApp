/**
 * The storable measurement units, mirroring `MeasurementUnit.All` on the backend
 * (backend/RecipeApp.API/Enums/MeasurementUnit.cs).
 *
 * Kept in the same order the backend declares them so the pickers read consistently.
 * A `GET /api/v1/units` endpoint is deliberately deferred — when it lands, this file
 * becomes the fallback rather than the source (Phase 8.5.1 §9, §13).
 */
export const MEASUREMENT_UNITS = ['g', 'kg', 'ml', 'L', 'pcs', 'tsp', 'tbsp', 'cup']

/** Default unit for a freshly added ingredient row. */
export const DEFAULT_UNIT = 'g'

/**
 * Formats an amount and unit for display, trimming float noise from portion scaling.
 * Returns e.g. `240 g`, `2 cup`, `0.5 tsp`.
 */
export function formatMeasurement(amount, unit) {
  const rounded = Math.round(amount * 100) / 100
  return `${rounded} ${unit}`
}

/**
 * Secondary text showing the measurement as originally stated by an imported source,
 * or null when there is nothing worth showing — a hand-entered row, or a row whose
 * canonical unit is already the one the source used.
 *
 * The source amount is scaled by the same portion multiplier as the canonical amount,
 * so `2 cups` at DOUBLE reads `(4 cups)`.
 */
export function formatSourceMeasurement(sourceAmount, sourceUnit, unit, multiplier = 1) {
  if (sourceAmount == null || !sourceUnit || sourceUnit === unit) return null
  return `(${formatMeasurement(sourceAmount * multiplier, sourceUnit)})`
}
