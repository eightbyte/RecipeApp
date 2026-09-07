# Phase 8.5.1 — Pre-Phase-9 Revisions

**Version:** 1.0
**Date:** 2026-09-06
**Status:** Draft — awaiting review
**Depends on:** Phase 8 (Local LLM) complete
**Blocks:** Phase 9 (Seed Recipe Library)

> A short corrective phase run **before** Phase 9 development begins. The headline item is
> **measurement handling**: making dry cup measurements a first-class, mathematically convertible
> unit by moving the cup→gram relationship out of the LLM prompt and into an **ingredient density**
> stored on the catalogue. Along the way it centralises the unit set (currently duplicated across
> five files), preserves the source measurement on every recipe ingredient, and fixes a latent
> shopping-list consolidation bug that already ships today.
>
> This phase is deliberately scoped as a container for revisions. §12 is reserved for further
> changes to be added before Phase 9 starts.

---

## 1. Overview

### 1.1 The core problem

Phase 9 §11.1 flags dry-cup conversion as *"the single most important thing to check"* and proposes
to solve it in the prompt:

> *"The prompt must instruct the model to emit mass units for dry bulk ingredients."*

That places a **per-ingredient physical property** — density — inside a **per-recipe generative
decision**. The consequences are structural, not stylistic:

| Problem | Consequence |
|---|---|
| The decision is re-made on every recipe | `2 cups flour` may normalise to `240 ml` in one recipe and `240 g` in the next |
| The decision is unverifiable | Nothing in the schema distinguishes a correct `g` from a hallucinated one |
| The decision is unfixable after the fact | The source text is discarded at normalisation; a wrong conversion is baked into every affected row |
| The decision is uncorrelated across the corpus | 1,072 recipes × ~10 ingredients = no practical way to audit |

Cups are not the problem. The problem is that **cup→gram is a property of the ingredient, and there
is currently nowhere to store it**. Give the catalogue a density and the conversion becomes
deterministic arithmetic — auditable, correctable by editing one row, and identical every time.

This also resolves the tension in the original framing: grams are preferred *because* they convert
mathematically. With a density on the ingredient, **cups convert mathematically too**, so cups can be
supported honestly rather than tolerated.

### 1.2 It also contradicts the existing schema

`RecipeSchemaJson` at
[RecipeScrapeService.cs:65-67](../backend/RecipeApp.API/Services/RecipeScrapeService.cs#L65-L67) already
instructs the model correctly:

```
"unit": {
  "type": "string",
  "description": "Unit of measurement as stated on the page. Preserve the original unit; do not convert."
}
```

Extraction preserves the source unit; `ConvertUnit` does the conversion mechanically. That division
of labour is right. Phase 9's proposal would require **reversing** this instruction and asking the
model to convert after all — regressing a design decision Phase 3 already got correct. Phase 8.5.1
keeps the schema as-is and strengthens the mechanical half instead.

### 1.3 Why before Phase 9, not after

The Phase 9 harvest is a one-time, 4–9 hour unattended run that writes ~1,072 recipes and grows the
ingredient catalogue substantially. Landing these changes afterwards means either re-running the
whole import or backfilling rows whose source measurements were never recorded. The columns in §6
must exist **before** the first seeded recipe is persisted.

---

## 2. Deliverable

Four workstreams, in dependency order:

1. **`Enums/MeasurementUnit.cs`** — one authoritative unit table; five duplicated hardcoded arrays
   deleted.
2. **`Ingredient.GramsPerMillilitre`** — nullable density on the catalogue, with a curated seed set
   for bulk dry goods.
3. **`RecipeIngredient.SourceAmount` / `SourceUnit`** — the measurement as originally stated,
   retained alongside the canonical amount.
4. **Cross-dimension shopping-list consolidation** — density-aware, and fixes the existing tsp/tbsp
   bug.

One EF migration. Backend + a small frontend change (unit picker and dual-unit display).

---

## 3. Current state — verified

### 3.1 The unit set is hardcoded in five places

`["g", "kg", "ml", "L", "pcs", "tsp", "tbsp"]` appears independently in:

| # | Location | Role |
|---|---|---|
| 1 | [RecipeValidators.cs:9](../backend/RecipeApp.API/Validators/RecipeValidators.cs#L9) | FluentValidation gate on `RecipeIngredientRequest.Unit` |
| 2 | [IngredientCatalogueSeeder.cs:39-40](../backend/RecipeApp.API/Services/IngredientCatalogueSeeder.cs#L39-L40) | Prompt text + `DefaultUnit` fallback to `"pcs"` |
| 3 | [JsonSchemaGrammar.cs:21-22](../backend/RecipeApp.API/Services/Llm/JsonSchemaGrammar.cs#L21-L22) | **Dead code — see §3.4** |
| 4 | [ShoppingListService.cs:15-22](../backend/RecipeApp.API/Services/ShoppingListService.cs#L15-L22) | Partial (`g`/`kg`/`ml`/`L` only) dimension table |
| 5 | [RecipeFormView.vue:316](../frontend/src/views/RecipeFormView.vue#L316) | `const units = [...]` for the form picker |

Adding `cup` today means editing five arrays that no compiler or test relates to one another. This is
squarely the *"avoid hard coded values"* convention in `CLAUDE.md`, and it is the reason workstream 1
comes first.

### 3.2 `cup` maps to 240 ml unconditionally

[RecipeScrapeService.cs:165-166](../backend/RecipeApp.API/Services/RecipeScrapeService.cs#L165-L166):

```csharp
["cup"]  = ("ml", 240.0),
["cups"] = ("ml", 240.0),
```

Applied identically to water and to flour. `ConvertUnit`
([RecipeScrapeService.cs:628](../backend/RecipeApp.API/Services/RecipeScrapeService.cs#L628)) is
`static` and takes only `(amount, unit)` — it has no access to the ingredient, so it *cannot*
consult a density even if one existed. Signature change required.

### 3.3 Shopping-list consolidation has a live bug

[ShoppingListService.cs:15-22](../backend/RecipeApp.API/Services/ShoppingListService.cs#L15-L22)
knows four units:

```csharp
["g"]  = ("mass",   1m),    ["kg"] = ("mass",   1000m),
["ml"] = ("volume", 1m),    ["L"]  = ("volume", 1000m),
```

Anything else falls to `$"__raw_{normalised.ToLowerInvariant()}"`
([ShoppingListService.cs:225](../backend/RecipeApp.API/Services/ShoppingListService.cs#L225)), a
bucket that groups by exact string only. Two consequences **already in production**:

- **`1 tbsp olive oil` + `15 ml olive oil` do not consolidate.** They are physically identical, but
  produce two separate list rows and set `NeedsReview = true`
  ([ShoppingListService.cs:268](../backend/RecipeApp.API/Services/ShoppingListService.cs#L268)).
  `tsp` and `tbsp` are exact volume units (5 ml, 15 ml); the table simply never says so.
- **Any ingredient measured two ways is flagged for manual review.** With cups added and no
  cross-dimension resolution, this would get materially worse across a 1,072-recipe library.

Phase 9 §14 acceptance criterion 11 ("shopping list generation … consolidates units correctly") would
fail on this today, independent of anything Phase 9 does.

### 3.4 `JsonSchemaGrammar.AllowedUnits` is dead code

[JsonSchemaGrammar.cs:21-22](../backend/RecipeApp.API/Services/Llm/JsonSchemaGrammar.cs#L21-L22)
declares `AllowedUnits` and **never references it**. The GBNF builder emits a generic `string` rule
for the `unit` field; units are not grammar-constrained at all.

This is an active trap: it reads as the enforcement point, so a developer adding `cup` would edit it,
observe no behaviour change, and be misled about where units are actually validated. **Delete it** as
part of workstream 1 rather than migrating it.

> Grammar-level unit constraint is **not** reintroduced. It would contradict the "preserve the
> original unit" instruction in §1.2 — the model must be free to emit `cup`, `ounce`, `pinch` or
> anything else the page states. Validation belongs after conversion (§5.5), not during decoding.

---

## 4. Workstream 1 — Centralise the unit set

### 4.1 `Enums/MeasurementUnit.cs`

One table, following the existing `IngredientCategory` static-class pattern
([IngredientCategory.cs](../backend/RecipeApp.API/Enums/IngredientCategory.cs)) — constants, an
`All` list, and an `IsValid` check — extended with dimension and base-unit factor.

```csharp
namespace RecipeApp.API.Enums;

public enum UnitDimension { Mass, Volume, Count }

/// <summary>
/// The authoritative set of storable measurement units.
/// Mass base unit is g; volume base unit is ml.
/// </summary>
public static class MeasurementUnit
{
    public const string Gram       = "g";
    public const string Kilogram   = "kg";
    public const string Millilitre = "ml";
    public const string Litre      = "L";
    public const string Piece      = "pcs";
    public const string Teaspoon   = "tsp";
    public const string Tablespoon = "tbsp";
    public const string Cup        = "cup";   // NEW

    /// <summary>
    /// Millilitres per cup. The app's canonical definition (US legal cup),
    /// matching the existing 240.0 factor in RecipeScrapeService.UnitConversions.
    /// </summary>
    public const decimal MillilitresPerCup = 240m;

    public static readonly IReadOnlyDictionary<string, (UnitDimension Dimension, decimal BaseFactor)>
        Table = new Dictionary<string, (UnitDimension, decimal)>(StringComparer.OrdinalIgnoreCase)
        {
            [Gram]       = (UnitDimension.Mass,   1m),
            [Kilogram]   = (UnitDimension.Mass,   1000m),
            [Millilitre] = (UnitDimension.Volume, 1m),
            [Litre]      = (UnitDimension.Volume, 1000m),
            [Teaspoon]   = (UnitDimension.Volume, 5m),      // was un-dimensioned
            [Tablespoon] = (UnitDimension.Volume, 15m),     // was un-dimensioned
            [Cup]        = (UnitDimension.Volume, MillilitresPerCup),
            [Piece]      = (UnitDimension.Count,  1m),
        };

    public static readonly IReadOnlyList<string> All = [.. Table.Keys];

    public static bool IsValid(string unit) => Table.ContainsKey(unit.Trim());

    public static UnitDimension? DimensionOf(string unit) =>
        Table.TryGetValue(unit.Trim(), out var e) ? e.Dimension : null;

    /// <summary>Converts an amount to the dimension's base unit (g or ml). Null if unknown.</summary>
    public static decimal? ToBase(decimal amount, string unit) =>
        Table.TryGetValue(unit.Trim(), out var e) ? amount * e.BaseFactor : null;
}
```

> **`MillilitresPerCup` is a constant, not configuration.** It is a definition, not a preference —
> changing it would silently reinterpret every already-stored value. The *policy* toggle in §10 is
> configurable; the physical constant is not. Treating "avoid hard coded values" as "make every
> number configurable" would be the wrong reading here.

### 4.2 Call sites to migrate

| Site | Change |
|---|---|
| [RecipeValidators.cs:9](../backend/RecipeApp.API/Validators/RecipeValidators.cs#L9) | Delete local array; `.Must(MeasurementUnit.IsValid)`, message from `MeasurementUnit.All` |
| [IngredientCatalogueSeeder.cs:39](../backend/RecipeApp.API/Services/IngredientCatalogueSeeder.cs#L39) | Delete local array; `unitsStr` from `MeasurementUnit.All`; fallback stays `MeasurementUnit.Piece` |
| [JsonSchemaGrammar.cs:21](../backend/RecipeApp.API/Services/Llm/JsonSchemaGrammar.cs#L21) | **Delete** — dead (§3.4) |
| [ShoppingListService.cs:15](../backend/RecipeApp.API/Services/ShoppingListService.cs#L15) | Delete local `UnitTable`; consume `MeasurementUnit.Table` (§7) |
| [RecipeFormView.vue:316](../frontend/src/views/RecipeFormView.vue#L316) | Add `'cup'`; see §9 |
| [RecipeIngredient.cs:16](../backend/RecipeApp.API/Models/RecipeIngredient.cs#L16) | Update the `<summary>` doc comment listing units |

**Behaviour change from this workstream alone:** `tsp`/`tbsp` gain the volume dimension, so §3.3's
consolidation bug is fixed. Existing `ShoppingListServiceTests` asserting the split-row behaviour
will need updating — that is the bug being fixed, not a regression.

---

## 5. Workstream 2 — Ingredient density

### 5.1 Model change

`Models/Ingredient.cs`:

```csharp
/// <summary>
/// Bulk density in grams per millilitre, used to convert volume measurements
/// (notably cups) to mass. Null means no reliable density is known for this
/// ingredient — volume measurements are then kept as stated. See Phase 8.5.1 §5.3.
/// </summary>
public decimal? GramsPerMillilitre { get; set; }
```

`Data/AppDbContext.cs`, in the existing `Ingredient` block
([AppDbContext.cs:24-29](../backend/RecipeApp.API/Data/AppDbContext.cs#L24-L29)):

```csharp
e.Property(i => i.GramsPerMillilitre).HasPrecision(8, 4);
```

Four decimal places covers the realistic 0.05–2.00 g/ml range with room to spare.

### 5.2 Why g/ml rather than g/cup

Storing grams-per-**cup** would work only for cups. Storing grams-per-**millilitre** is a true
physical property, so one value serves every volume unit in the table — cups now, and `tsp`/`tbsp`/
`ml` if the policy in §5.4 is ever widened. It also composes cleanly with
`MeasurementUnit.ToBase()`: convert any volume to ml, multiply by density, get grams.

**Derive densities by dividing published grams-per-cup by 240**, the app's own
`MillilitresPerCup`. Published tables (King Arthur, USDA) are based on the US customary cup of
~236.6 ml, so dividing by 236.6 and multiplying back by 240 would introduce a ~1.4% drift. Dividing
by our own constant makes the round trip exact: `120 g/cup ÷ 240 = 0.5 g/ml`, and
`1 cup × 240 ml × 0.5 = 120 g`.

### 5.3 `null` is load-bearing

**`GramsPerMillilitre == null` means "no reliable density — keep the volume unit as stated."**

This is what makes cups a supported unit rather than a tolerated one. For `1 cup chopped spinach`,
`2 cups mixed salad greens`, or `1 cup cubed bread`, packing dominates and any gram figure is fake
precision dressed as accuracy. The honest storage is `1 cup`, and the shopping list should say
`3 cups` — not a fabricated `72 g`.

So the rule splits cleanly:

- **Density known** → grams, because the conversion is real arithmetic.
- **Density unknown** → cups, because cups are the more truthful measurement.

Neither branch guesses.

### 5.4 Conversion policy — cups only

Density resolution applies to **`cup` only**. `tsp` and `tbsp` are left as stated.

Rationale: spoon measures are small, precise, universally available, and more useful to a cook as
spoons — `1 tsp salt` is better guidance than `6 g salt`. They also consolidate correctly on their
own once §4.1 gives them a dimension. Cups are the problem case precisely because the quantity is
large enough for density error to compound into a materially wrong recipe.

Encoded as `MeasurementUnit.MassResolvable = [Cup]`, so widening it later is a one-line change with
an obvious blast radius.

### 5.5 Pipeline changes

`ConvertUnit` becomes two stages. Stage A is the existing static customary→metric mapping,
unchanged. Stage B is new, ingredient-aware, and runs only after catalogue matching has resolved an
`IngredientId`:

```
LLM extraction        →  ("2", "cups")           source unit preserved (§1.2)
Stage A: ConvertUnit  →  (480m, "ml")            mechanical, no ingredient context
[catalogue match]     →  IngredientId resolved
Stage B: ResolveMass  →  (240m, "g")             480 ml × 0.5 g/ml   — density known
                      →  (480m, "ml")            unchanged           — density null
```

New `Services/MeasurementConverter.cs` — a small, dependency-free, unit-testable service holding
Stage B, consumed by `RecipeScrapeService.NormaliseAsync`, by the Phase 9 `RecipeLibrarySeeder`, and
by `ShoppingListService` (§7).

In [NormaliseAsync](../backend/RecipeApp.API/Services/RecipeScrapeService.cs#L436), the existing
`dbIngredients` projection at
[RecipeScrapeService.cs:445-448](../backend/RecipeApp.API/Services/RecipeScrapeService.cs#L445-L448)
must add `GramsPerMillilitre`. Stage B then applies in **both** the pass-1 exact-match branch and
the pass-2 semantic-match branch — pass 2 resolves an `IngredientId` later, so mass resolution has
to happen after it, not inline with the current `ConvertUnit` call at
[RecipeScrapeService.cs:461](../backend/RecipeApp.API/Services/RecipeScrapeService.cs#L461).

**Post-conversion validation** (replacing the grammar constraint that §3.4 showed never existed):
after Stage B, reject any ingredient whose unit is not `MeasurementUnit.IsValid`. This is the right
place for Phase 9 §11.3's unit check — it validates the pipeline's own output, which is mechanical
and deterministic, rather than the model's, which is neither.

### 5.6 Seed density table

**Curated reference data, reviewed by a human — not LLM-generated.** Asking the model to invent
densities relocates the guessing to a place where it looks authoritative, which is the exact failure
mode this phase exists to remove. The set that actually matters is small.

Delivered as a static table in `Data/IngredientDensitySeeder.cs`, applied by normalised ingredient
name, idempotent, and safe to re-run after Phase 9 grows the catalogue.

| Ingredient | g/cup | g/ml |
|---|---|---|
| all-purpose flour | 120 | 0.5000 |
| bread flour | 120 | 0.5000 |
| whole wheat flour | 113 | 0.4708 |
| granulated sugar | 200 | 0.8333 |
| brown sugar (packed) | 213 | 0.8875 |
| powdered sugar | 120 | 0.5000 |
| white rice (uncooked) | 185 | 0.7708 |
| rolled oats | 90 | 0.3750 |
| cornmeal | 138 | 0.5750 |
| cornstarch | 120 | 0.5000 |
| cocoa powder | 85 | 0.3542 |
| dry breadcrumbs | 108 | 0.4500 |
| dried lentils | 192 | 0.8000 |
| dried black beans | 194 | 0.8083 |
| butter | 227 | 0.9458 |
| water | 240 | 1.0000 |
| milk | 247 | 1.0292 |
| vegetable oil | 220 | 0.9167 |
| honey | 340 | 1.4167 |
| maple syrup | 322 | 1.3417 |
| peanut butter | 258 | 1.0750 |
| table salt | 292 | 1.2167 |
| grated parmesan | 100 | 0.4167 |
| shredded cheddar | 113 | 0.4708 |

Target ~40–60 entries covering flours, sugars, rices, grains, oats, legumes, nuts, dairy solids,
syrups and fats. Everything else stays `null` and keeps its stated unit — which is the correct
outcome, not a gap to be filled.

> The catalogue LLM seeder **may** propose a density for genuinely new ingredients, but only into a
> review queue or log — never written directly. Deferred; not in this phase.

---

## 6. Workstream 3 — Preserve the source measurement

### 6.1 Model change

`Models/RecipeIngredient.cs`:

```csharp
/// <summary>Amount exactly as stated by the source recipe, before conversion. Null for hand-entered rows.</summary>
public decimal? SourceAmount { get; set; }

/// <summary>Unit exactly as stated by the source recipe (e.g. "cup", "ounce"). Null for hand-entered rows.</summary>
public string? SourceUnit { get; set; }
```

`Data/AppDbContext.cs`, in the existing `RecipeIngredient` block
([AppDbContext.cs:40-53](../backend/RecipeApp.API/Data/AppDbContext.cs#L40-L53)):

```csharp
e.Property(ri => ri.SourceAmount).HasPrecision(10, 3);   // matches Amount
e.Property(ri => ri.SourceUnit).HasMaxLength(32);
```

`Amount`/`Unit` remain the single canonical value all arithmetic uses. `SourceAmount`/`SourceUnit`
are **provenance only** — never summed, never scaled.

### 6.2 Why

This follows the established portion-scaling convention in `CLAUDE.md` — *"never modify stored
`Amount` values; multiply in the query/DTO layer"* — applied one level earlier: don't destroy the
input, derive from it.

Three concrete payoffs:

1. **Auditability.** Phase 9 §14's trial run wants to verify dry-cup handling across a sample. Today
   that is impossible — by the time a row is written, the source text is gone and `240 ml` is
   indistinguishable from a correct conversion. With these columns it is one query:

   ```sql
   SELECT i.display_name, ri.source_amount, ri.source_unit, ri.amount, ri.unit, i.grams_per_millilitre
   FROM recipe_ingredients ri
   JOIN ingredients i ON i.id = ri.ingredient_id
   WHERE ri.source_unit ILIKE 'cup%';
   ```

2. **Retroactive correction.** If a density is wrong, the source measurement is still on record, so a
   backfill can recompute. Without it, the only recovery is re-running the 4–9 hour import.

3. **Display fidelity.** `2 cups flour (240 g)` — the recipe reads as written while the shopping list
   sums grams.

### 6.3 DTO changes

| DTO | Change |
|---|---|
| [RecipeIngredientResponse](../backend/RecipeApp.API/DTOs/Recipes/RecipeIngredientResponse.cs) | `+ decimal? SourceAmount, string? SourceUnit` |
| [ScrapePreviewIngredient](../backend/RecipeApp.API/DTOs/Scrape/ScrapePreviewResponse.cs) | `+ decimal? SourceAmount, string? SourceUnit` — lets the confirm screen show what was converted |
| [RecipeIngredientRequest](../backend/RecipeApp.API/DTOs/Recipes/CreateRecipeRequest.cs) | `+ decimal? SourceAmount, string? SourceUnit` (optional; null on hand entry) |
| [IngredientResponse](../backend/RecipeApp.API/DTOs/Ingredients/IngredientResponse.cs) | `+ decimal? GramsPerMillilitre` |
| [CreateIngredientRequest](../backend/RecipeApp.API/DTOs/Ingredients/CreateIngredientRequest.cs) / `UpdateIngredientRequest` | `+ decimal? GramsPerMillilitre` — lets a density be corrected without a migration |

Mapping updates in [Mappings.cs](../backend/RecipeApp.API/DTOs/Mappings.cs); validator rule
`GreaterThan(0).LessThanOrEqualTo(3)` on `GramsPerMillilitre` when non-null (nothing in a kitchen is
denser than ~2.2 g/ml).

### 6.4 Manual entry is not silently converted

Two paths with deliberately different behaviour:

- **Import** (scrape, Phase 9 seeder) — the source unit is incidental, whatever the page happened to
  use. Resolve to mass where density is known; record the source.
- **Manual entry** — the user deliberately chose a unit. **Store it as entered.** Overriding an
  explicit human choice would be the same category of error as the LLM guessing, just in the other
  direction.

Cross-dimension consolidation (§7) is what makes this safe: a manually entered `1 cup flour` and an
imported `240 g flour` still merge into one correct shopping-list row.

---

## 7. Workstream 4 — Density-aware consolidation

`ShoppingListService.BuildRecipeItemsAsync`
([ShoppingListService.cs:190-300](../backend/RecipeApp.API/Services/ShoppingListService.cs#L190-L300))
currently groups by dimension, sums within `mass` and `volume`, groups everything else by exact
string, and flags `NeedsReview` when more than one row survives
([ShoppingListService.cs:268](../backend/RecipeApp.API/Services/ShoppingListService.cs#L268)).

Three changes:

1. **Consume `MeasurementUnit.Table`** instead of the local four-entry table. `tsp`/`tbsp`/`cup` gain
   the volume dimension — §3.3 fixed.
2. **Cross-dimension resolution.** When an ingredient produces both a mass and a volume group *and*
   `GramsPerMillilitre` is non-null, convert the volume total to mass and emit **one** row. The
   `Ingredient` is already loaded via the existing `.ThenInclude(ri => ri.Ingredient)`, so no extra
   query.
3. **Presentation rule** (deterministic, no heuristics): if any contributing row is mass → present
   mass; else if all rows are volume → present volume; `pcs` always stays its own row. The existing
   `>= 1000` promotion to `kg`/`L` is unchanged.

`NeedsReview` then means what it should: genuinely irreconcilable units — e.g. `2 pcs onion` plus
`150 g onion`, where no density can help because `pcs` has no fixed size.

---

## 8. Migration

```bash
dotnet ef migrations add AddMeasurementDensityAndSource --output-dir Data/Migrations
```

Three nullable columns, no data movement, no backfill:

| Table | Column | Type |
|---|---|---|
| `ingredients` | `grams_per_millilitre` | `numeric(8,4) NULL` |
| `recipe_ingredients` | `source_amount` | `numeric(10,3) NULL` |
| `recipe_ingredients` | `source_unit` | `varchar(32) NULL` |

All existing rows read as "no density known / no source recorded", which degrades to exactly today's
behaviour. Density seeding runs separately and idempotently.

> **This invalidates Phase 9 §2's "no schema migration" claim.** See §11.

---

## 9. Frontend changes

Small, and the only reason this phase is not backend-only:

| File | Change |
|---|---|
| [RecipeFormView.vue:316](../frontend/src/views/RecipeFormView.vue#L316) | Add `'cup'` to `units` |
| Recipe detail / cooking mode | Show `2 cups flour (240 g)` when `SourceUnit` is present and differs from `Unit` — secondary styling, never replacing the canonical amount |
| Ingredient admin | Optional `GramsPerMillilitre` field, with helper text explaining that blank means "keep volume units as stated" |

The unit list stays hardcoded frontend-side for now; a `/api/v1/units` endpoint is deferred (§13).

---

## 10. Configuration

One new key under a `Measurement` section:

| Key | Default | Description |
|---|---|---|
| `Measurement:ResolveCupsToMassOnImport` | `true` | When false, imports keep cups as volume even where a density is known. Escape hatch for a Phase 9 trial run that needs to inspect raw conversions. |

`MillilitresPerCup` is deliberately **not** configurable (§4.1).

---

## 11. Impact on the Phase 9 spec

[phase-9-seed-recipe-library.md](phase-9-seed-recipe-library.md) needs these edits once this phase
lands:

| Section | Edit |
|---|---|
| §2 Deliverable | "no schema migration" → now depends on Phase 8.5.1's migration |
| §11.1 table | `cup` row: "240 ml, dry ingredients mass-dependent" → "240 ml, resolved to mass via `Ingredient.GramsPerMillilitre` where known" |
| §11.1 warning box | The dry-cup risk callout is largely retired — record that it is handled structurally, not by prompt |
| §11.3 validation gate | Unit check now runs post-conversion against `MeasurementUnit.IsValid` |
| §14 criterion 3 | "Dry ingredients use mass units" → verify the *density table* covers the corpus's bulk dry goods; spot-check via the §6.2 query |
| §14 criterion 11 | Now genuinely reachable — §3.3's bug would have failed it regardless |
| §17 tests | Parser tests must assert source units are preserved through to persistence |
| §19 Out of scope | Frontend work is no longer zero |

Net effect on Phase 9: **less** LLM prompt iteration (the §14 criteria expected to need tuning drop
from three to two), and a trial run that can actually be audited.

---

## 12. Further revisions

*Reserved. Additional pre-Phase-9 changes to be appended here before this spec is marked Ready.*

---

## 13. Out of scope

- **Grammar-constrained units.** Contradicts "preserve the original unit" (§1.2, §3.4).
- **LLM-generated densities.** Relocates guessing; may propose to a review queue later (§5.6).
- **Backfilling `SourceAmount`/`SourceUnit`** for pre-existing recipes — the data no longer exists.
- **Per-user display-unit preference** (view everything in cups / everything in grams). Now
  *possible* thanks to the density, but a separate feature.
- **Imperial output.** Storage stays metric per `CLAUDE.md`.
- **`GET /api/v1/units`.** Frontend keeps its own list this phase (§9).
- **Widening mass resolution to `tsp`/`tbsp`.** One-line change, deliberately deferred (§5.4).

---

## 14. Open questions for review

1. **Density seed size** — is ~40–60 curated entries the right initial scope, or start with the ~25
   in §5.6 and grow after the Phase 9 trial run reveals what the corpus actually uses?
2. **Packed vs. loose brown sugar** — 213 g/cup assumes packed. Model as two catalogue entries, or
   one entry with the assumption recorded in `Notes`?
3. **Manual-entry conversion** — §6.4 stores hand-entered cups as-is. Confirm that is preferred over
   converting with a "converted from 1 cup" hint.
4. **`NeedsReview` on `pcs` mixes** — should `2 pcs onion` + `150 g onion` stay flagged, or should the
   catalogue eventually carry an average piece weight? (The latter is a fifth workstream; not
   proposed here.)
5. **Trial-run ordering** — run the Phase 9 20-recipe trial *before* finalising the density table, so
   the real corpus drives which ingredients get densities?

---

## 15. Definition of Done

- [ ] `Enums/MeasurementUnit.cs` exists; all five hardcoded arrays (§3.1) deleted or migrated
- [ ] `JsonSchemaGrammar.AllowedUnits` deleted
- [ ] `Ingredient.GramsPerMillilitre` and `RecipeIngredient.SourceAmount`/`SourceUnit` added;
      migration applies cleanly to an existing populated database
- [ ] `Services/MeasurementConverter.cs` with unit tests covering: cup + known density → g;
      cup + null density → ml unchanged; tsp/tbsp untouched; unknown unit passthrough
- [ ] Density seeder is idempotent and re-runnable after catalogue growth
- [ ] `NormaliseAsync` resolves mass in **both** the exact-match and semantic-match branches
- [ ] Post-conversion unit validation rejects units outside `MeasurementUnit.All`
- [ ] `1 tbsp olive oil` + `15 ml olive oil` consolidate to `30 ml` in one row, `NeedsReview = false`
      (regression test for §3.3)
- [ ] `1 cup flour` + `120 g flour` consolidate to `240 g` in one row (cross-dimension, §7)
- [ ] `2 cups spinach` + `1 cup spinach` consolidate to `3 cups` — no fabricated mass (§5.3)
- [ ] `cup` selectable in the recipe form; detail view shows source alongside canonical
- [ ] Existing `ShoppingListServiceTests` updated where they asserted the §3.3 bug
- [ ] Full backend + frontend suites green
- [ ] Phase 9 spec updated per §11
