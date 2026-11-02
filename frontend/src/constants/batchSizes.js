/** The Batch Sizes, mirroring `BatchSize.All` on the backend in the same order, each a multiple of the whole recipe. */
export const BATCH_SIZES = [
  { value: 'HALF',    title: '½×', multiplier: 0.5, color: 'info' },
  { value: 'REGULAR', title: '1×', multiplier: 1,   color: 'success' },
  { value: 'DOUBLE',  title: '2×', multiplier: 2,   color: 'warning' },
]

/** Batch Size for a newly Planned Meal — the backend's column default. */
export const DEFAULT_BATCH_SIZE = 'REGULAR'

const batchSizesByValue = new Map(BATCH_SIZES.map(batchSize => [batchSize.value, batchSize]))

/** How many times the recipe's amounts are multiplied, falling back to 1 for an unknown value as `BatchSize.Multiplier` does. */
export function batchSizeMultiplier(value) {
  return batchSizesByValue.get(value)?.multiplier ?? 1
}

/** The label a Batch Size shows, rendering an unrecognised value as itself rather than dropping it. */
export function formatBatchSize(value) {
  return batchSizesByValue.get(value)?.title ?? value
}

/** The chip colour for a Batch Size. */
export function batchSizeColor(value) {
  return batchSizesByValue.get(value)?.color ?? 'default'
}

/** The given value when it is a Batch Size, otherwise the default. */
export function toBatchSize(value) {
  return batchSizesByValue.has(value) ? value : DEFAULT_BATCH_SIZE
}
