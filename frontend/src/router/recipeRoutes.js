const SERVINGS_QUERY_KEY = 'servings'

function scaledRoute(name, recipeId, servings) {
  return { name, params: { id: recipeId }, query: { [SERVINGS_QUERY_KEY]: servings } }
}

/** The route location that opens the recipe view scaled to the given number of servings. */
export function recipeRoute(recipeId, servings) {
  return scaledRoute('recipe-detail', recipeId, servings)
}

/** The route location that opens Cooking Mode for a recipe scaled to the given number of servings. */
export function cookingRoute(recipeId, servings) {
  return scaledRoute('recipe-cooking', recipeId, servings)
}

/** The servings a recipe view or Cooking Mode query asks for, falling back to the recipe's own Servings. */
export function servingsFromQuery(query, recipeServings) {
  const servings = Number(query[SERVINGS_QUERY_KEY])
  return Number.isFinite(servings) && servings > 0 ? servings : recipeServings
}
