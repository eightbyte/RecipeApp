---
paths:
  - "backend/RecipeApp.API/Enums/MeasurementUnit.cs"
  - "backend/RecipeApp.API/Enums/SizelessUnit.cs"
  - "backend/RecipeApp.API/Enums/CountWords.cs"
  - "backend/RecipeApp.API/Services/MeasurementConverter.cs"
  - "backend/RecipeApp.API/Services/MeasurementOptions.cs"
  - "backend/RecipeApp.API/Services/MealPlanService.cs"
  - "backend/RecipeApp.API/Services/ShoppingListService.cs"
  - "backend/RecipeApp.API/Services/RecipeScrapeService.cs"
  - "backend/RecipeApp.API/Services/RecipeService.cs"
  - "backend/RecipeApp.API/Services/Seeding/SeedQuantityGate.cs"
  - "backend/RecipeApp.API/Models/Ingredient.cs"
  - "backend/RecipeApp.API/Models/RecipeIngredient.cs"
  - "backend/RecipeApp.API/Data/IngredientDensitySeeder.cs"
  - "backend/RecipeApp.API/DTOs/**/*.cs"
  - "backend/RecipeApp.API/Validators/**/*.cs"
  - "frontend/src/constants/units.js"
  - "frontend/src/constants/batchSizes.js"
  - "frontend/src/constants/servings.js"
  - "frontend/src/router/recipeRoutes.js"
  - "frontend/src/components/BatchSizeToggle.vue"
  - "frontend/src/components/MeasurementUnitSelect.vue"
  - "frontend/src/components/ServingsStepper.vue"
  - "frontend/src/views/RecipeDetailView.vue"
  - "frontend/src/views/RecipeFormView.vue"
  - "frontend/src/views/CookingModeView.vue"
  - "frontend/src/views/ShoppingView.vue"
  - "frontend/src/views/RecipeScrapePreviewView.vue"
---

# Measurements

## One unit table

- The storable unit set lives **only** in `Enums/MeasurementUnit.cs`, mirrored by
  `frontend/src/constants/units.js`. The set is `g`, `kg`, `ml`, `L`, `pcs`, `tsp`, `tbsp`, `cup`.
  Never hard-code a unit array. Consume these files.
- `MeasurementUnit.IsValid` is **strict** (canonical spelling only) and is what validators use.
  `MeasurementUnit.TryCanonicalise` is **lenient** (`teaspoons`, `ML`, `cups`). Every path that
  receives a unit from outside calls it *before* validating. `AcceptedSpellings` exposes the
  lenient table. Derive LLM prompts from it rather than listing spellings by hand.

## Conversion

- All unit arithmetic lives in `Services/MeasurementConverter.cs`. **Stage A** converts customary
  units to metric and canonicalises spelling. **Stage B** resolves `cup` against the matched
  ingredient's `GramsPerMillilitre`. `Measurement:ResolveCupsOnImport` toggles Stage B on import.
- **`Ingredient.GramsPerMillilitre == null` means "no reliable density — do not invent a mass."**
  A cup of such an ingredient is stored as `cup`. Liquids and packing-dominated ingredients are
  null on purpose.
- Curated densities come from `Data/IngredientDensitySeeder.cs`. They are human-reviewed, matched
  by catalogue name or alias, idempotent, and never overwrite a hand-corrected value.

## Unquantified ingredients

- **`RecipeIngredient.Amount == null` means "the source states no quantity"** (`salt`, `raisins`,
  `nonstick cooking spray`). This is a real property of home cooking, not a parse defect.
- `Unit` is null exactly when `Amount` is. Validators reject a unit with no amount.
  **An amount with no unit means `pcs`**: validators accept it and
  `RecipeIngredient.ToStoredMeasurement` stores it as `pcs`, which also enforces the invariant at the
  entity boundary. Every persistence path uses it, because service-level callers bypass the
  endpoint filter. A missing unit records no Source Measurement, because nothing is lost.
- A scraped Sizeless Unit (`pinch`, `dash`, `handful`, "to taste", …) is stored unquantified,
  with the stated amount and unit kept as the Source Measurement. With no stated amount the
  Source Measurement is the word alone, shown as "(to taste)". Those words live **only** in
  `Enums/SizelessUnit.cs`.
- A scraped Count Word (`clove`, `sprig`, `slice`, …) is stored as `pcs`, with its wording kept as the
  Source Measurement. A size word (`small`, `large`, …) in the unit slot is a missing unit: the row
  becomes `pcs` and the word moves into its notes. Count Words and size words live **only** in
  `Enums/CountWords.cs`. Words with a real size (`can`, `stick`, `jar`, `bunch`, …) are in neither
  table, so the scrape preview still asks for a unit.
- Frontend: `pcs` is never displayed (`formatMeasurement` drops it), and a `pcs` row with a Source
  Measurement shows the source alone (`formatIngredientMeasurement`), as "(2 cloves) Garlic". The
  Unit picker still offers `pcs`, and shows it as the placeholder once cleared.
- **Never store zero.** `0` renders as `0 g Salt` and sums into shopping lists.
- `ShoppingListService` partitions unquantified rows out of consolidation and never sums them. An
  ingredient the plan only ever names yields one amount-less item.
- Frontend: `formatMeasurement` returns null for a null amount, and `toPayloadMeasurement` sends
  null for both fields rather than `0`. **Scaling must short-circuit before multiplying**, because
  in JavaScript `null * 2` is `0`.

## Provenance

- **Never destroy the input.** Imported rows record `RecipeIngredient.SourceAmount`/`SourceUnit`
  verbatim. These fields are provenance only: never summed and never used in consolidation.
  Every persistence path (scrape confirm, recipe create/update) must carry them through, and the
  frontend round-trips them through the recipe form.
- When an amount is repaired on import, the source fields record the repaired measurement, not the
  discarded misread.

## Servings scaling and consolidation

- Every scaling multiplies the whole recipe by servings wanted ÷ the recipe's Servings, at display
  or query time only. **Never modify a stored `Amount`.** `SourceAmount` scales the same way.
- A Planned Meal stores its own Servings, an int within `RecipeLimits.MinServings`/`MaxServings`,
  and starts at the recipe's Servings. `ShoppingListService` scales by that count ÷ the recipe's
  current Servings, multiplying before dividing so a whole result stays exact.
- The recipe view and Cooking Mode read the servings wanted from the URL's `servings` query
  (`router/recipeRoutes.js`), falling back to the recipe's Servings. That count can be fractional.
- Batch Size is only the recipe view's ½×/1×/2× shortcut onto Servings
  (`frontend/src/constants/batchSizes.js`). It is never stored or sent to the API.
- Shopping-list consolidation groups by dimension via `MeasurementUnit.Table`, so `1 tbsp` +
  `15 ml` makes one row. It resolves mass/volume mixes through density, and it presents a
  single-unit group in that unit (`2 cup + 1 cup = 3 cup`).
- Item Sources record each contribution in the recipe's unit plus its share in the row's unit (the
  base unit when the row is promoted to `kg`/`L`). They are never summed back into totals.
