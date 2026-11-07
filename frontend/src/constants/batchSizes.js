/** The recipe view's Batch Size shortcuts onto Servings, each a multiple of the whole recipe. */
export const BATCH_SIZES = [
  { value: 'HALF',    title: '½×', multiplier: 0.5 },
  { value: 'REGULAR', title: '1×', multiplier: 1 },
  { value: 'DOUBLE',  title: '2×', multiplier: 2 },
]

const batchSizesByValue = new Map(BATCH_SIZES.map(batchSize => [batchSize.value, batchSize]))

/** How many times the recipe's amounts are multiplied, falling back to 1 for an unknown value. */
export function batchSizeMultiplier(value) {
  return batchSizesByValue.get(value)?.multiplier ?? 1
}
