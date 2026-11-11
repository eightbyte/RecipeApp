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
  - "backend/RecipeApp.API/Services/StepAmountTotaller.cs"
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
- `MeasurementUnit.IsValid` is **strict** (canonical spelling only). Validators use
  `IsAccepted`, which is `IsValid` plus the Entry-only Units, just as strict.
  `MeasurementUnit.TryCanonicalise` is **lenient** (`teaspoons`, `ML`, `cups`). Every path that
  receives a unit from outside calls it *before* validating. `AcceptedSpellings` exposes the
  lenient table. Derive LLM prompts from it rather than listing spellings by hand.

## Entry-only Units

- The Unit picker also offers the Entry-only Units `oz`, `lb`, `fl oz`, `pint` and `quart`. They live
  **only** in `MeasurementUnit.EntryOnly`, mirrored by `ENTRY_ONLY_UNITS` in `units.js`, and are
  **never stored**. `oz` is always weight. `fl oz` is the separate volume option.
- Validators accept them through `MeasurementUnit.IsAccepted`. Every persistence path (recipe
  create/update, so recipe packs too, and scrape confirm) stores a row through
  `MeasurementConverter.ToStored`. That converts an Entry-only Unit by Stage A to `g`/`ml` and keeps the
  typed measurement as the Source Measurement. A given source stating the same quantity in another
  spelling (`4 ounces` for a typed `4 oz`) is kept instead, so a reopened row saves unchanged.
- **The recipe form and the scrape preview open a row in an Entry-only Unit only through the
  response's `entryUnit`** (`MeasurementConverter.EntryUnitOf`, on `RecipeIngredientResponse` and
  `ScrapePreviewIngredientResponse`), both via `toFormMeasurement`. It is set only while the source
  still converts to the stored amount, so a row edited by hand after import is never reverted to
  its source. A row with no entry unit opens rounded to 2 decimals (`236.588` → `236.59`), and the
  rounded amount is what saves.
- Frontend: the picker's items are `UNIT_PICKER_ITEMS`, derived from the two unit mirrors. Change a
  form row's unit only through `setIngredientUnit`. It drops the Source Measurement only for a row
  opened in an Entry-only Unit, so a scraped or imported source survives a pass through `oz`.

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
- A scraped Count Word (`clove`, `sprig`, `slice`, `bunch`, …) is stored as `pcs`, with its wording kept
  as the Source Measurement, including a size word leading it (`½ small bunch`). `stick` is a Count Word
  except for butter and margarine, whose stick has a real size. A size word (`small`, `large`, …) alone
  in the unit slot is a missing unit: the row becomes `pcs` and the word moves into its notes. Count
  Words and size words live **only** in `Enums/CountWords.cs`. Words with a real size (`can`, `jar`,
  `pkg`, …) are in neither table, so the scrape preview still asks for a unit.
- A scraped package with a stated size (`1 (6 oz) pkg`, `½ (16 oz) pkg`) is stored as the total it holds
  (`170.097 g`, `226.796 g`). The total in the stated unit (`6 oz`, `8 oz`) is the Source Measurement, and
  the package count (`1 pkg`, `½ pkg`) leads the notes. The model sometimes moves the size into the notes
  (`pkg` + `6 oz` or `(6 oz)`), and that reads the same way. `MeasurementConverter.ReadSizedPackage` parses
  the size.
- Frontend: `pcs` is never displayed (`formatMeasurement` drops it), and a `pcs` row with a Source
  Measurement stating the same count shows the source alone (`formatIngredientMeasurement`), as "(2 cloves) Garlic". The
  Unit picker still offers `pcs`, and shows it as the placeholder for an amount with no unit. The
  picker has no clear button, because its icon squeezed the label on a phone.
- A scraped row whose list line states nothing, not even a Sizeless Unit, is filled from the amounts its steps
  state (`stated_amounts`, a scrape-only schema field). `StepAmountTotaller` adds them up in code, never the model:
  sized mentions sum, a Sizeless Unit is ignored beside a sized one, and mass meets volume only through the matched
  Ingredient's density. A shared unit keeps the total as the Source Measurement; mixed units keep none. Filling runs
  after matching and before Stage B. `amountFromSteps` and `stepAmounts` are preview-only and never saved.
- **Never store zero.** `0` renders as `0 g Salt` and sums into shopping lists.
- `ShoppingListService` partitions unquantified rows out of consolidation and never sums them. An
  ingredient the plan only ever names yields one amount-less item.
- Frontend: `formatMeasurement` returns null for a null amount, and `toPayloadMeasurement` sends
  null for both fields rather than `0`. **Scaling must short-circuit before multiplying**, because
  in JavaScript `null * 2` is `0`.

## Provenance

- **Never destroy the input.** Imported rows, and rows typed in an Entry-only Unit, record
  `RecipeIngredient.SourceAmount`/`SourceUnit` verbatim. These fields are provenance only: never summed and never used in consolidation.
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
