/** The ingredient categories, mirroring `IngredientCategory.All` on the backend in the same order, which is the shopping list's aisle order. */
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

/** The label for a category, rendering an unrecognised value as itself rather than dropping it. */
export function categoryTitle(value) {
  return categoriesByValue.get(value)?.title ?? value
}

/** The heading a shopping list shows for a category, rendering an unrecognised value as itself rather than dropping it. */
export function formatCategory(value) {
  const category = categoriesByValue.get(value)
  return category ? `${category.icon} ${category.title}` : value
}
