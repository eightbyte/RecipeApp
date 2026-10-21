/**
 * The ingredient categories, mirroring `IngredientCategory.All` on the backend
 * (backend/RecipeApp.API/Enums/IngredientCategory.cs).
 *
 * **The order is the shopping list's aisle order** — the backend sorts a generated list by it —
 * so it must match the backend's exactly. `GET /api/v1/ingredients/categories` returns the same
 * values in the same order; this file adds what a picker or a list heading shows for each.
 */
export const INGREDIENT_CATEGORIES = [
  { value: 'PRODUCE',        title: 'Produce',              icon: '🥦' },
  { value: 'MEAT_SEAFOOD',   title: 'Meat & Seafood',       icon: '🥩' },
  { value: 'DAIRY',          title: 'Dairy',                icon: '🥛' },
  { value: 'CANNED',         title: 'Canned Goods',         icon: '🥫' },
  { value: 'SOUPS_BROTH',    title: 'Soups & Broth',        icon: '🍲' },
  { value: 'FROZEN',         title: 'Frozen',               icon: '🧊' },
  { value: 'DRY_GOODS',      title: 'Dry Goods',            icon: '🥜' },
  { value: 'GRAINS_RICE',    title: 'Grains & Rice',        icon: '🌾' },
  { value: 'PASTA_SAUCES',   title: 'Pasta & Sauces',       icon: '🍝' },
  { value: 'BAKING_SPICES',  title: 'Baking & Spices',      icon: '🧂' },
  { value: 'BAKERY',         title: 'Bakery',               icon: '🍞' },
  { value: 'CONDIMENTS',     title: 'Condiments & Oils',    icon: '🫙' },
  { value: 'JAM_NUT_BUTTER', title: 'Jams & Nut Butters',   icon: '🍯' },
  { value: 'BEVERAGES',      title: 'Beverages',            icon: '🧃' },
  { value: 'COFFEE_TEA',     title: 'Coffee & Tea',         icon: '☕' },
  { value: 'KITCHEN',        title: 'Kitchen Supplies',     icon: '🍴' },
  { value: 'HOUSEHOLD',      title: 'Household',            icon: '🧻' },
  { value: 'OTHER',          title: 'Other',                icon: '📦' },
]

/** Category for an item nobody has categorised — the backend's column default. */
export const DEFAULT_CATEGORY = 'OTHER'

/** The category values alone, in aisle order. */
export const INGREDIENT_CATEGORY_VALUES = INGREDIENT_CATEGORIES.map(category => category.value)

const categoriesByValue = new Map(INGREDIENT_CATEGORIES.map(category => [category.value, category]))

/**
 * The heading a shopping list shows for a category, e.g. `🧂 Baking & Spices`.
 *
 * An unrecognised value renders as itself rather than disappearing — a list stored before a
 * category was renamed must still show its items under some heading.
 */
export function formatCategory(value) {
  const category = categoriesByValue.get(value)
  return category ? `${category.icon} ${category.title}` : value
}
