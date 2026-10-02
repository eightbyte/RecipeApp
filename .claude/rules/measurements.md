---
paths:
  - "backend/RecipeApp.API/Enums/MeasurementUnit.cs"
  - "backend/RecipeApp.API/Enums/PortionSize.cs"
  - "backend/RecipeApp.API/Services/MeasurementConverter.cs"
  - "backend/RecipeApp.API/Services/MeasurementOptions.cs"
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
- `Unit` is null exactly when `Amount` is. Validators reject a half-set pair.
  `RecipeIngredient.ToStoredMeasurement` enforces the invariant at the entity boundary. Every
  persistence path uses it, because service-level callers bypass the endpoint filter.
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

## Portions and consolidation

- Portion sizes are `HALF` (×0.5), `REGULAR` (×1.0) and `DOUBLE` (×2.0), applied at display or
  query time only. **Never modify a stored `Amount`.** `SourceAmount` scales the same way.
- Shopping-list consolidation groups by dimension via `MeasurementUnit.Table`, so `1 tbsp` +
  `15 ml` makes one row. It resolves mass/volume mixes through density, and it presents a
  single-unit group in that unit (`2 cup + 1 cup = 3 cup`).
