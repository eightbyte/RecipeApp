import { batchSizeMultiplier, toBatchSize } from '@/constants/batchSizes'

const BATCH_SIZE_QUERY_KEY = 'batch'
const SERVINGS_QUERY_KEY   = 'servings'

function scaledRoute(name, recipeId, queryKey, value) {
  return { name, params: { id: recipeId }, query: { [queryKey]: value } }
}

/** The route location that opens the recipe view at the given Batch Size. */
export function recipeRoute(recipeId, batchSize) {
  return scaledRoute('recipe-detail', recipeId, BATCH_SIZE_QUERY_KEY, batchSize)
}

/** The route location that opens the recipe view scaled to the given number of servings. */
export function recipeRouteAtServings(recipeId, servings) {
  return scaledRoute('recipe-detail', recipeId, SERVINGS_QUERY_KEY, servings)
}

/** The route location that opens Cooking Mode for a recipe at the given Batch Size. */
export function cookingRoute(recipeId, batchSize) {
  return scaledRoute('recipe-cooking', recipeId, BATCH_SIZE_QUERY_KEY, batchSize)
}

/** The route location that opens Cooking Mode for a recipe scaled to the given number of servings. */
export function cookingRouteAtServings(recipeId, servings) {
  return scaledRoute('recipe-cooking', recipeId, SERVINGS_QUERY_KEY, servings)
}

/** The servings a recipe view or Cooking Mode query asks for, falling back to its Batch Size, then Regular, applied to the recipe's Servings. */
export function servingsFromQuery(query, recipeServings) {
  const servings = Number(query[SERVINGS_QUERY_KEY])
  if (Number.isFinite(servings) && servings > 0) return servings
  return recipeServings * batchSizeMultiplier(toBatchSize(query[BATCH_SIZE_QUERY_KEY]))
}
