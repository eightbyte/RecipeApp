---
status: proposed
date: 2026-11-06
issue: "#51"
---

# Planned Meals store Servings, not a Batch Size

A Planned Meal stores a **Batch Size**: Half, Regular or Double (`Enums/BatchSize.cs`), applied as
×0.5, ×1 or ×2 to the whole recipe. `CONTEXT.md` records that decision ("A Planned Meal's Batch
Size is Half, Regular or Double") and lists "custom servings" as a term to avoid. The recipe view
and Cooking Mode have meanwhile gained a −/+ Servings stepper that scales to any number of
servings, with ½×/1×/2× kept as shortcuts onto it. So the recipe view can show "6 servings of a
4-serving recipe", but a Planned Meal cannot store it, and the shopping list cannot buy for it.

We reverse the earlier decision. A Planned Meal stores how many **Servings** to make, a whole
number from 1 to 100. A new Planned Meal starts at the recipe's own Servings. Batch Size survives
only as the recipe view and Cooking Mode's ½×/1×/2× shortcut onto Servings, and is never stored.

## Context

Measured on 2026-11-06 on `develop` at `d5b1bbb`:

- **Batch Size is stored in two places.** `PlannedMeal.BatchSize` and
  `ShoppingListItemSource.BatchSize`, both `TEXT` holding `HALF`, `REGULAR` or `DOUBLE`, defaulting
  to `REGULAR`. The Item Source copy is a snapshot taken when the list is generated.
- **The shopping list multiplies by the Batch Size.** `ShoppingListService` applies
  `BatchSize.Multiplier` to each Recipe Ingredient of a Planned Meal. Unquantified Ingredients are
  partitioned out before any multiplication.
- **Links carry the Batch Size as `?batch=`.** `recipeRoute` and `cookingRoute` in
  `router/recipeRoutes.js` build it. `servingsFromQuery` reads `servings` first, then `batch`,
  then falls back to the recipe's Servings. The callers are `PlannedMealCard`, the builder's
  added-meal row and the shopping list's Item Source link.
- **A recipe's Servings is an int from 1 to 100** (`RecipeLimits.MinServings`/`MaxServings`,
  mirrored by `constants/servings.js`).
- **A recipe in a Planned Meal cannot be deleted** (`PlannedMeal.Recipe` is `Restrict`). An Item
  Source's recipe can be deleted after its Planned Meal is removed, which leaves the source's
  `RecipeId` null (`SetNull`) with only `RecipeName` left to name it.
- **Changing a Planned Meal touches its plan's `UpdatedAt`**, which is what makes the list Stale.

## Decision

### Data

- `PlannedMeal.BatchSize` becomes `PlannedMeal.Servings`: an int from 1 to 100, mirroring
  `RecipeLimits.MinServings`/`MaxServings`.
- `ShoppingListItemSource.BatchSize` becomes `ShoppingListItemSource.Servings`.
- **One migration converts existing rows** to the recipe's Servings × the old multiplier,
  **rounded up** to a whole number. Half of a 5-serving recipe becomes 3; Double of it becomes 10.
  An unrecognised value counts as Regular, as `BatchSize.Multiplier` does today. A result above
  100 (Double of a recipe with more than 50 Servings) is clamped to 100. Item Sources convert
  against their recipe's current Servings. After this, no stored data holds a Batch Size.
- **Item Source Servings is nullable.** A source whose recipe was deleted before the migration has
  no recipe Servings to convert against, so it migrates to null and shows no count. Generating a
  list always fills it.
- **A Planned Meal keeps its own count** when the recipe's Servings is edited later. If 6
  servings of a 4-serving recipe are planned and the recipe is edited to 8, the meal stays at 6 and
  scales by 6 ÷ 8.
- **The backend `Enums/BatchSize.cs` is deleted**, since nothing stored or sent holds a Batch Size.
  The migration carries its own multipliers so that it never changes when the code does.

### API

- The add and update Planned Meal requests carry `servings` in place of `batchSize`. The
  validators enforce 1 to 100 from `RecipeLimits`.
- If an add request leaves `servings` out, the meal takes the recipe's Servings.
- The Planned Meal and Item Source responses carry `servings`.

### Shopping list

- `ShoppingListService` scales each recipe by planned Servings ÷ recipe Servings. Unquantified
  Ingredients stay partitioned out before any multiplication, as all scaling does.
- Changing a Planned Meal's Servings makes the list Stale, as changing its Batch Size does now.
- Each Item Source shows an "N servings" chip in place of the Batch Size chip, and its link opens
  the recipe at that count.

### Frontend

- A new Planned Meal starts at the recipe's Servings, whether added from the builder, the
  suggestions panel or the Meal Plan tab. The frontend leaves `servings` out of the add request
  and the API fills it in.
- The recipe view's −/+ Servings stepper becomes a shared component. The builder's added-meal row
  replaces the ½×/1×/2× toggle with it: whole numbers, 1 to 100, starting at the meal's Servings.
  Each −/+ tap saves, as each toggle choice does today, and the row shows the new count at once so
  quick taps build on each other.
- "N servings" is an x-small chip in one neutral colour, where the Batch Size chip was.
- `PlannedMealCard` (Home and Meal Plan) shows the "N servings" chip. "Change batch" becomes
  "Change servings" and its dialog uses the same stepper.
- The Past Meal Plan detail view shows the "N servings" chip.
- Links to a recipe and to Cooking Mode carry only `servings`. The `batch` query parameter is
  removed from `router/recipeRoutes.js`. An old link that still carries `?batch=` opens at the
  recipe's own Servings.
- The recipe view and Cooking Mode keep their ½×/1×/2× buttons as shortcuts that set the
  servings stepper. They stay display-only. `constants/batchSizes.js` keeps only what those
  shortcuts need, and `BatchSizeChip` gives way to a Servings chip.

### Vocabulary

- **Planned Meal**: has its own Servings.
- **Batch Size**: only the recipe view and Cooking Mode's ½×/1×/2× shortcut onto Servings.
- "custom servings" comes off the avoid list.

## Considered options

**A. Keep Batch Size on Planned Meals.** *Rejected:* a Planned Meal can't hold the count the recipe
view already lets the user choose, so the shopping list buys for the wrong number of people
whenever ½×, 1× or 2× isn't what they need.

**B. Store a fractional multiplier** (for example 1.5) on the Planned Meal. *Rejected:* the user
thinks in people to feed, not multiples. A multiplier also changes meaning when the recipe's
Servings is edited, whereas a stored count stays what the user asked for.

**C. Store Servings. (Chosen.)** A whole count, the same unit and limits as a recipe's Servings.

## Consequences

- The migration rounds up, so a converted Half can buy slightly more than before (Half of 5 servings
  becomes 3, scaling by 0.6, not 0.5). Only a clamped Double buys less (below).
- Editing a recipe's Servings now changes how much its existing Planned Meals buy, in proportion.
  Before, a Batch Size kept buying the same multiple of the recipe.
- Bookmarked `?batch=` links stop scaling and open at the recipe's own Servings.
- `.claude/rules/measurements.md`, `CONTEXT.md`, `frontend/CLAUDE.md`, `backend/CLAUDE.md`,
  `CODING_STANDARDS.md` and `README.md` stop describing a Planned Meal's Batch Size.
- A migrated Double above 100 servings buys less than before, capped at 100.
- #50 (builder tweaks) landed first using Batch Size. This change switches its added-meal link and
  builder row to Servings.
