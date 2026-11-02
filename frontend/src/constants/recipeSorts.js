export const SORT_ASCENDING  = 'ASC'
export const SORT_DESCENDING = 'DESC'

/** The sort directions, mirroring `SortDirection.All` on the backend in the same order. */
export const SORT_DIRECTIONS = [SORT_ASCENDING, SORT_DESCENDING]

/** The fields the recipe list sorts by, mirroring `RecipeSort.All` on the backend in the same order, each labelling what its two directions look like. */
export const RECIPE_SORTS = [
  { value: 'NAME',        title: 'Name',        directionLabels: { [SORT_ASCENDING]: 'A to Z',             [SORT_DESCENDING]: 'Z to A' } },
  { value: 'DATE_ADDED',  title: 'Date added',  directionLabels: { [SORT_ASCENDING]: 'Oldest first',       [SORT_DESCENDING]: 'Newest first' } },
  { value: 'LAST_COOKED', title: 'Last cooked', directionLabels: { [SORT_ASCENDING]: 'Least recent first', [SORT_DESCENDING]: 'Most recent first' } },
]

const SORT_DIRECTION_ICONS = {
  [SORT_ASCENDING]:  'mdi-sort-ascending',
  [SORT_DESCENDING]: 'mdi-sort-descending',
}

/** Newest added first, the backend's order when no sort is sent. */
export const DEFAULT_RECIPE_LIST_SORT = Object.freeze({ sort: 'DATE_ADDED', direction: SORT_DESCENDING })

/** The device-local key holding the Recipes tab's `{ sort, direction }` choice. */
export const RECIPE_LIST_SORT_STORAGE_KEY = 'recipeapp.recipeList.sort'

const recipeSortsByValue = new Map(RECIPE_SORTS.map(recipeSort => [recipeSort.value, recipeSort]))

/** The given `{ sort, direction }` when both are known values, otherwise a copy of the default, so a stale or hand-edited stored value never reaches the API. */
export function toRecipeListSort(value) {
  const isKnown = recipeSortsByValue.has(value?.sort) && SORT_DIRECTIONS.includes(value?.direction)
  return isKnown
    ? { sort: value.sort, direction: value.direction }
    : { ...DEFAULT_RECIPE_LIST_SORT }
}

export function reverseSortDirection(direction) {
  return direction === SORT_ASCENDING ? SORT_DESCENDING : SORT_ASCENDING
}

/** What the order looks like for this field and direction, such as "Newest first". */
export function sortDirectionLabel(sort, direction) {
  return recipeSortsByValue.get(sort)?.directionLabels[direction]
}

export function sortDirectionIcon(direction) {
  return SORT_DIRECTION_ICONS[direction]
}
