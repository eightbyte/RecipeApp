export const makeRecipe = (overrides = {}) => ({
  id: 'test-recipe-1',
  name: 'Test Recipe',
  description: null,
  imageUrl: null,
  servings: 4,
  ingredientCount: 2,
  lastCookedAt: null,
  createdAt: '2026-01-01T00:00:00Z',
  ...overrides,
})

export const makeIngredient = (overrides = {}) => ({
  id: 'test-ingredient-1',
  name: 'flour',
  displayName: 'Flour',
  category: 'DRY_GOODS',
  defaultUnit: 'g',
  createdAt: '2026-01-01T00:00:00Z',
  ...overrides,
})

export const makeScrapePreview = (overrides = {}) => ({
  name:        'Test Imported Recipe',
  description: 'Imported from the web',
  servings:    4,
  sourceUrl:   'https://example.com/recipe',
  ingredients: [],
  steps:       [],
  ...overrides,
})

export const makeMealPlanListItem = (overrides = {}) => ({
  id: 'plan-1',
  name: 'Week of 2 June',
  isActive: false,
  recipeCount: 2,
  firstScheduledDate: '2026-06-02',
  lastScheduledDate: '2026-06-05',
  createdAt: '2026-06-01T00:00:00Z',
  closedAt: null,
  ...overrides,
})

export const makeMealPlanRecipe = (overrides = {}) => ({
  id: 'mpr-1',
  recipeId: 'test-recipe-1',
  recipeName: 'Test Recipe',
  recipeImageUrl: null,
  recipeLastCookedAt: null,
  scheduledDate: null,
  portionSize: 'REGULAR',
  displayOrder: 1,
  ...overrides,
})

export const makeMealPlanDetail = (overrides = {}) => ({
  id: 'plan-1',
  name: 'Week of 2 June',
  isActive: true,
  createdAt: '2026-06-01T00:00:00Z',
  closedAt: null,
  recipes: [makeMealPlanRecipe()],
  ...overrides,
})

export const makeSuggestion = (overrides = {}) => ({
  recipeId: 'suggested-recipe-1',
  recipeName: 'Suggested Recipe',
  recipeImageUrl: null,
  recipeLastCookedAt: null,
  overlapCount: 2,
  overlappingIngredients: ['onion', 'carrot'],
  ...overrides,
})

export const makeRecipeDetail = (overrides = {}) => ({
  id: 'test-recipe-1',
  name: 'Test Recipe',
  description: null,
  imageUrl: null,
  sourceUrl: null,
  servings: 4,
  lastCookedAt: null,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  ingredients: [
    {
      id: 'ri-1',
      ingredientId: 'test-ingredient-1',
      ingredientName: 'flour',
      ingredientDisplayName: 'Flour',
      category: 'DRY_GOODS',
      amount: 200,
      unit: 'g',
      notes: null,
      displayOrder: 0,
    },
  ],
  steps: [
    {
      id: 'step-1',
      stepNumber: 1,
      instruction: 'Mix ingredients',
      recipeIngredientIds: ['ri-1'],
    },
  ],
  ...overrides,
})
