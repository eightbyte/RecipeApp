---
paths:
  - "backend/RecipeApp.API/Enums/IngredientCategory.cs"
  - "backend/RecipeApp.API/Services/RecipeScrapeService.cs"
  - "backend/RecipeApp.API/Services/ShoppingListService.cs"
  - "backend/RecipeApp.API/Services/Seeding/IngredientCatalogueBuilder.cs"
  - "backend/RecipeApp.API/seed-data/ingredient-catalogue.json"
  - "frontend/src/constants/categories.js"
  - "frontend/src/views/ShoppingView.vue"
  - "frontend/src/components/AddCustomItemDialog.vue"
  - "frontend/src/views/RecipeScrapePreviewView.vue"
---

# Ingredient categories

- The category set lives **only** in `Enums/IngredientCategory.cs`, mirrored by
  `frontend/src/constants/categories.js`, which also holds the display labels. Never hand-copy the list.
- **The order of `IngredientCategory.All` is the shopping list's aisle order.** `ShoppingListService`
  sorts by index. `Category` is a string column, so adding a category needs no migration.

## Adding or changing a category

1. Put it in `IngredientCategory.All` **beside its nearest relative**, not at the end. Mirror the
   change in `categories.js` with a label.
2. Add a line to `IngredientCatalogueBuilder.CategoryAisleRules`. **The line carries the boundary,
   not the name, and the most-missed item goes first.** When salt was listed last, the model filed
   every salt under DRY_GOODS.
3. **Restate the boundary of every aisle it was carved from.** A catch-all aisle absorbs whatever
   the new line does not explicitly name. For example, CANNED hands back broth, stock and soup,
   and DAIRY hands back cream-of soups.
4. Add keywords to `RecipeScrapeService.CategoryPriority`. **That table is first-match-wins**, so a
   category whose names are built from other aisles' words must lead it. For example,
   `chicken broth` would otherwise match MEAT_SEAFOOD. Add the collision cases to its test theory.
5. Preview with `seed-recipes --build-catalogue --batch N` before any full rebuild. Existing
   databases keep their old categories until `import-catalogue --force`.

## Boundary decisions (the user's, keep them)

- BAKING_SPICES is the whole baking-and-spices aisle: flour, sugar, leaveners, extracts, cocoa,
  spices, dried herbs, **salt and pepper**. Spices are never CONDIMENTS or PRODUCE.
- **Cooking spray is CONDIMENTS**, beside the oils.
- JAM_NUT_BUTTER includes **honey and syrups**.
- Plain canned tomato sauce is CANNED. Only pasta/pizza sauce is PASTA_SAUCES.
- SOUPS_BROTH covers broth, stock and bouillon in any packaging, plus condensed, ready-to-eat and
  mix soups.
- KITCHEN is non-food a recipe uses (foil, skewers). HOUSEHOLD is non-food a recipe never uses
  (reached through custom shopping items).
