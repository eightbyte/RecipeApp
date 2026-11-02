import {
  RECIPE_SORTS,
  SORT_DIRECTIONS,
  DEFAULT_RECIPE_LIST_SORT,
  RECIPE_LIST_SORT_STORAGE_KEY,
  toRecipeListSort,
  reverseSortDirection,
  sortDirectionLabel,
  sortDirectionIcon,
} from './recipeSorts'

describe('RECIPE_SORTS', () => {
  it('mirrors RecipeSort.All on the backend, in the same order', () => {
    // Kept in lockstep with the backend enum, or the dropdown offers values the API rejects with a 400.
    expect(RECIPE_SORTS.map(recipeSort => recipeSort.value)).toEqual(['NAME', 'DATE_ADDED', 'LAST_COOKED'])
  })

  it('titles each field for the dropdown', () => {
    expect(RECIPE_SORTS.map(recipeSort => recipeSort.title)).toEqual(['Name', 'Date added', 'Last cooked'])
  })
})

describe('SORT_DIRECTIONS', () => {
  it('mirrors SortDirection.All on the backend, in the same order', () => {
    expect(SORT_DIRECTIONS).toEqual(['ASC', 'DESC'])
  })
})

describe('DEFAULT_RECIPE_LIST_SORT', () => {
  it('is newest added first, the order the list had before sorting existed', () => {
    expect(DEFAULT_RECIPE_LIST_SORT).toEqual({ sort: 'DATE_ADDED', direction: 'DESC' })
  })
})

describe('RECIPE_LIST_SORT_STORAGE_KEY', () => {
  it('is namespaced to the app', () => {
    expect(RECIPE_LIST_SORT_STORAGE_KEY).toBe('recipeapp.recipeList.sort')
  })
})

describe('toRecipeListSort', () => {
  it('keeps a known field and direction', () => {
    expect(toRecipeListSort({ sort: 'LAST_COOKED', direction: 'ASC' })).toEqual({ sort: 'LAST_COOKED', direction: 'ASC' })
  })

  it('drops properties it does not know', () => {
    expect(toRecipeListSort({ sort: 'NAME', direction: 'DESC', extra: 1 })).toEqual({ sort: 'NAME', direction: 'DESC' })
  })

  it.each([
    ['an unknown field', { sort: 'RATING', direction: 'ASC' }],
    ['an unknown direction', { sort: 'NAME', direction: 'UP' }],
    ['a lower-case value', { sort: 'name', direction: 'asc' }],
    ['a missing direction', { sort: 'NAME' }],
    ['a string', 'NAME'],
    ['an array', ['NAME', 'ASC']],
    ['null', null],
    ['undefined', undefined],
  ])('falls back to the default for %s', (_description, storedValue) => {
    expect(toRecipeListSort(storedValue)).toEqual({ sort: 'DATE_ADDED', direction: 'DESC' })
  })

  it('returns a copy of the default, so changing the result cannot change the default', () => {
    const recipeListSort = toRecipeListSort(null)
    recipeListSort.sort = 'NAME'

    expect(DEFAULT_RECIPE_LIST_SORT.sort).toBe('DATE_ADDED')
  })
})

describe('reverseSortDirection', () => {
  it('flips each direction to the other', () => {
    expect(reverseSortDirection('ASC')).toBe('DESC')
    expect(reverseSortDirection('DESC')).toBe('ASC')
  })
})

describe('sortDirectionLabel', () => {
  it.each([
    ['NAME', 'ASC', 'A to Z'],
    ['NAME', 'DESC', 'Z to A'],
    ['DATE_ADDED', 'ASC', 'Oldest first'],
    ['DATE_ADDED', 'DESC', 'Newest first'],
    ['LAST_COOKED', 'ASC', 'Least recent first'],
    ['LAST_COOKED', 'DESC', 'Most recent first'],
  ])('describes %s %s as "%s"', (sort, direction, label) => {
    expect(sortDirectionLabel(sort, direction)).toBe(label)
  })
})

describe('sortDirectionIcon', () => {
  it('shows the direction the list runs', () => {
    expect(sortDirectionIcon('ASC')).toBe('mdi-sort-ascending')
    expect(sortDirectionIcon('DESC')).toBe('mdi-sort-descending')
  })
})
