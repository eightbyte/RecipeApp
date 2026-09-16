import { describe, it, expect } from 'vitest'
import {
  INGREDIENT_CATEGORIES,
  INGREDIENT_CATEGORY_VALUES,
  DEFAULT_CATEGORY,
  formatCategory,
} from './categories'

describe('INGREDIENT_CATEGORIES', () => {
  it('mirrors IngredientCategory.All on the backend, in the same order', () => {
    // Kept in lockstep with backend/RecipeApp.API/Enums/IngredientCategory.cs. The order is the
    // shopping list's aisle order, so a reordering here is a visible change, not a cosmetic one.
    expect(INGREDIENT_CATEGORY_VALUES).toEqual([
      'PRODUCE', 'MEAT_SEAFOOD', 'DAIRY', 'CANNED', 'FROZEN',
      'DRY_GOODS', 'GRAINS_RICE', 'PASTA_SAUCES', 'BAKING_SPICES', 'BAKERY',
      'CONDIMENTS', 'JAM_NUT_BUTTER', 'BEVERAGES', 'COFFEE_TEA', 'KITCHEN',
      'HOUSEHOLD', 'OTHER',
    ])
  })

  it('gives every category a distinct title and an icon', () => {
    const titles = INGREDIENT_CATEGORIES.map(category => category.title)
    expect(new Set(titles).size).toBe(titles.length)
    for (const category of INGREDIENT_CATEGORIES) expect(category.icon).toBeTruthy()
  })

  it('offers a default that is itself a category', () => {
    expect(INGREDIENT_CATEGORY_VALUES).toContain(DEFAULT_CATEGORY)
  })
})

describe('formatCategory', () => {
  it('renders the icon and title', () => {
    expect(formatCategory('BAKING_SPICES')).toBe('🧂 Baking & Spices')
    expect(formatCategory('PRODUCE')).toBe('🥦 Produce')
  })

  it('renders an unrecognised value as itself rather than hiding its items', () => {
    expect(formatCategory('RETIRED_CATEGORY')).toBe('RETIRED_CATEGORY')
  })
})
