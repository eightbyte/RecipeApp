import { toBatchSize } from '@/constants/batchSizes'

const BATCH_SIZE_QUERY_KEY = 'batch'

/** The route location that opens Cooking Mode for a recipe at the given Batch Size. */
export function cookingRoute(recipeId, batchSize) {
  return {
    name: 'recipe-cooking',
    params: { id: recipeId },
    query: { [BATCH_SIZE_QUERY_KEY]: batchSize },
  }
}

/** The Batch Size a Cooking Mode query carries, falling back to the default when it is missing or unknown. */
export function batchSizeFromCookingQuery(query) {
  return toBatchSize(query[BATCH_SIZE_QUERY_KEY])
}
