# Phase 8.5.1 — Pre-Phase-9 Revisions

**Version:** 1.1
**Date:** 2026-09-06
**Status:** Draft — awaiting review
**Depends on:** Phase 8 (Local LLM) complete
**Blocks:** Phase 9 (Seed Recipe Library)

> A short corrective phase run **before** Phase 9 development begins. The headline item is
> **measurement handling**: making dry cup measurements a first-class, mathematically convertible
> unit by moving the cup→gram relationship out of the LLM prompt and into an **ingredient density**
> stored on the catalogue. Along the way it centralises the unit set (currently duplicated across
> five files, with a sixth that validates nothing), preserves the source measurement on every recipe
> ingredient, and fixes a latent shopping-list consolidation bug that already ships today.
>
> This phase is deliberately scoped as a container for revisions. §12 is reserved for further
> changes to be added before Phase 9 starts.

### Changes in v1.1 (review against the codebase)

The v1.0 diagnosis and approach stand. These corrections make the design internally consistent and
complete the change list:

1. **Stage A no longer converts `cup` → `ml`.** v1.0 kept the existing 240 ml conversion in Stage A,
   so an ingredient with no density ended up as `480 ml`, contradicting §5.3 ("keep the volume unit
   as stated") and the DoD line `2 cups spinach + 1 cup spinach → 3 cups`. §5.5 and §15 corrected.
2. **Unit alias canonicalisation added** (`MeasurementUnit.TryCanonicalise`). `ConvertUnit` passes
   unconverted units through verbatim, so `teaspoon`, `tablespoons`, `cups`, `pieces` would all have
   failed the post-conversion gate. §4.1, §5.5.
3. **Cup resolution is three-way** (density → g; volume-natural ingredient → ml; otherwise keep
   `cup`) and **liquids are removed from the density table**. v1.0 would have turned `1 cup milk`
   into `247 g milk`. §5.4, §5.6.
4. **Shopping-list presentation gains a same-unit rule.** Without it `2 cup + 1 cup` presents as
   `720 ml`. §7.
5. **Missing change sites added:** `ScrapeConfirmIngredient` + `ConfirmAsync` (the path that
   actually persists a scrape), `ScrapeConfirmIngredientValidator` (has **no unit whitelist today**),
   `IngredientsEndpoints` POST/PUT, the recipe-form edit round-trip (editing a scraped recipe would
   otherwise wipe its provenance), and the density seeder's need to match real catalogue names
   (`plain flour` ≠ `all-purpose flour`). §3.1, §5.6, §6.3, §9.
6. **SQL and column names corrected** to the schema's actual EF-default PascalCase. §6.2, §8.
7. **"Ingredient admin" does not exist** in the frontend; reworded. §9.
8. **Phase 9 impact references corrected** (the "no migration" claim is in §1/§13/§19, not §2; the
   §11 example has the LLM converting ounces; `ConvertUnit` is public, not private). §11.
9. **Validation moved out of `NormaliseAsync`** — throwing there would break the interactive scrape
   preview, which today lets the user fix an unrecognised unit. §5.5.

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

1. **`Enums/MeasurementUnit.cs`** — one authoritative unit table with alias canonicalisation; five
   duplicated hardcoded arrays deleted and one missing validator closed.
2. **`Ingredient.GramsPerMillilitre`** — nullable density on the catalogue, with a curated seed set
   for bulk dry goods.
3. **`RecipeIngredient.SourceAmount` / `SourceUnit`** — the measurement as originally stated,
   retained alongside the canonical amount and carried through every persistence path.
4. **Cross-dimension shopping-list consolidation** — density-aware, and fixes the existing tsp/tbsp
   bug.

One EF migration. Backend plus a small frontend change: a shared unit list, `cup` in both unit
pickers, source-measurement round-trip in the recipe form, and secondary source display on the
detail and cooking views.

---

## 3. Current state — verified

### 3.1 The unit set is hardcoded in five places — and absent from a sixth

`["g", "kg", "ml", "L", "pcs", "tsp", "tbsp"]` appears independently in:

| # | Location | Role |
|---|---|---|
| 1 | [RecipeValidators.cs:8-9](../backend/RecipeApp.API/Validators/RecipeValidators.cs#L8-L9) | FluentValidation gate on `RecipeIngredientRequest.Unit` (case-sensitive) |
| 2 | [IngredientCatalogueSeeder.cs:39-40](../backend/RecipeApp.API/Services/IngredientCatalogueSeeder.cs#L39-L40) | Prompt text + `DefaultUnit` fallback to `"pcs"` at line 121 |
| 3 | [JsonSchemaGrammar.cs:21-22](../backend/RecipeApp.API/Services/Llm/JsonSchemaGrammar.cs#L21-L22) | **Dead code — see §3.4** |
| 4 | [ShoppingListService.cs:15-22](../backend/RecipeApp.API/Services/ShoppingListService.cs#L15-L22) | Partial (`g`/`kg`/`ml`/`L` only) dimension table |
| 5 | [RecipeFormView.vue:316](../frontend/src/views/RecipeFormView.vue#L316) | `const units = [...]` for the form picker |
| 6 | [ScrapeValidators.cs:50](../backend/RecipeApp.API/Validators/ScrapeValidators.cs#L50) | **No whitelist at all.** `RuleFor(x => x.Unit).NotEmpty().MaximumLength(20)` — a confirmed scrape can persist `clove`, `teaspoon` or `Tbsp` today, while the identical row typed by hand is rejected by #1. The preview's unit input ([RecipeScrapePreviewView.vue:84-89](../frontend/src/views/RecipeScrapePreviewView.vue#L84-L89)) is a free-text field, so nothing upstream prevents it either. |

The set is also documented in prose at `SPEC.md:109` and `:155`, `CLAUDE.md:105`, and the
`RecipeIngredient.Unit` doc comment
([RecipeIngredient.cs:16](../backend/RecipeApp.API/Models/RecipeIngredient.cs#L16)).

Adding `cup` today means editing five arrays that no compiler or test relates to one another, and
still leaves the confirm path unguarded. This is squarely the *"avoid hard coded values"* convention
in `CLAUDE.md`, and it is the reason workstream 1 comes first.

### 3.2 `cup` maps to 240 ml unconditionally

[RecipeScrapeService.cs:165-166](../backend/RecipeApp.API/Services/RecipeScrapeService.cs#L165-L166):

```csharp
["cup"]  = ("ml", 240.0),
["cups"] = ("ml", 240.0),
```

Applied identically to water and to flour. `ConvertUnit`
([RecipeScrapeService.cs:628](../backend/RecipeApp.API/Services/RecipeScrapeService.cs#L628)) is
`public static` and takes only `(double rawAmount, string unit)` — it has no access to the
ingredient, so it *cannot* consult a density even if one existed. Signature change required.

`ConvertUnit` also passes every unit it does not recognise through **verbatim** (line 636). The test
`ConvertUnit_MetricUnitsPassThrough` documents this for `piece`, `pieces`, `clove` and `pinch`. Long
spellings like `teaspoon` or `tablespoons` therefore reach persistence as-is — see §3.1 row 6.

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
> anything else the page states. Validation belongs at persistence (§5.5), not during decoding.

---

## 4. Workstream 1 — Centralise the unit set

### 4.1 `Enums/MeasurementUnit.cs`

One table, following the existing `IngredientCategory` static-class pattern
([IngredientCategory.cs](../backend/RecipeApp.API/Enums/IngredientCategory.cs)) — constants, an
`All` list, and an `IsValid` check — extended with dimension, base-unit factor, and alias
canonicalisation.

```csharp
namespace RecipeApp.API.Enums;

public enum UnitDimension { Mass, Volume, Count }

/// <summary>
/// The authoritative set of storable measurement units.
/// Mass base unit is g; volume base unit is ml. Stored values are always the
/// canonical spelling below — use TryCanonicalise on anything from outside.
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
    /// matching the 240.0 factor RecipeScrapeService.UnitConversions used until this phase.
    /// </summary>
    public const decimal MillilitresPerCup = 240m;

    /// <summary>Canonical spellings in display order. This is the storable set.</summary>
    public static readonly IReadOnlyList<string> All =
        [Gram, Kilogram, Millilitre, Litre, Piece, Teaspoon, Tablespoon, Cup];

    public static readonly IReadOnlyDictionary<string, (UnitDimension Dimension, decimal BaseFactor)>
        Table = new Dictionary<string, (UnitDimension, decimal)>(StringComparer.Ordinal)
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

    /// <summary>Units whose stored form may be resolved by ingredient context on import (§5.4).</summary>
    public static readonly IReadOnlyList<string> ImportResolvable = [Cup];

    /// <summary>
    /// Spellings the scraper/LLM commonly emits, mapped to the canonical unit. Case-insensitive.
    /// Deliberately excludes units with no fixed size (clove, pinch, can, slice, bunch) — those
    /// cannot be canonicalised and must be corrected by a human or rejected by the seeder gate.
    /// </summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gram"] = Gram,             ["grams"] = Gram,
        ["kilogram"] = Kilogram,     ["kilograms"] = Kilogram,   ["kilo"] = Kilogram,
        ["millilitre"] = Millilitre, ["millilitres"] = Millilitre,
        ["milliliter"] = Millilitre, ["milliliters"] = Millilitre,
        ["litre"] = Litre,           ["litres"] = Litre,   ["liter"] = Litre,   ["liters"] = Litre,
        ["piece"] = Piece,           ["pieces"] = Piece,   ["pc"] = Piece,      ["each"] = Piece,
        ["teaspoon"] = Teaspoon,     ["teaspoons"] = Teaspoon,
        ["tablespoon"] = Tablespoon, ["tablespoons"] = Tablespoon, ["tbs"] = Tablespoon,
        ["cups"] = Cup,
    };

    /// <summary>Strict: true only for a canonical spelling. Validators use this.</summary>
    public static bool IsValid(string unit) => Table.ContainsKey(unit.Trim());

    /// <summary>
    /// Resolves canonical spellings in any case ("ML", "Tbsp") and known aliases ("teaspoons")
    /// to the canonical unit. Returns false for anything else; the caller decides what that means.
    /// </summary>
    public static bool TryCanonicalise(string unit, out string canonical)
    {
        var trimmed = unit.Trim();
        var exact   = All.FirstOrDefault(u => string.Equals(u, trimmed, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) { canonical = exact; return true; }
        return Aliases.TryGetValue(trimmed, out canonical!);
    }

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

> **`IsValid` is strict, `TryCanonicalise` is lenient — on purpose.** The existing manual-entry
> validator is case-sensitive; keeping `IsValid` strict preserves that and guarantees stored units
> are always the canonical spelling, which is what lets `ShoppingListService` group by plain
> equality. Every path that receives a unit from outside (scrape, seeder, API) canonicalises first
> and validates second.

### 4.2 Call sites to migrate

| Site | Change |
|---|---|
| [RecipeValidators.cs:8-9](../backend/RecipeApp.API/Validators/RecipeValidators.cs#L8-L9) | Delete local array; `.Must(MeasurementUnit.IsValid)`, message from `MeasurementUnit.All` |
| [ScrapeValidators.cs:50](../backend/RecipeApp.API/Validators/ScrapeValidators.cs#L50) | **Add** `.Must(MeasurementUnit.IsValid)` — closes the gap in §3.1 row 6 |
| [IngredientCatalogueSeeder.cs:39](../backend/RecipeApp.API/Services/IngredientCatalogueSeeder.cs#L39) | Delete local array; `unitsStr` from `MeasurementUnit.All`; fallback stays `MeasurementUnit.Piece`; accept the model's `default_unit` via `TryCanonicalise` rather than exact match |
| [JsonSchemaGrammar.cs:21](../backend/RecipeApp.API/Services/Llm/JsonSchemaGrammar.cs#L21) | **Delete** — dead (§3.4) |
| [ShoppingListService.cs:15](../backend/RecipeApp.API/Services/ShoppingListService.cs#L15) | Delete local `UnitTable`; consume `MeasurementUnit.Table` (§7) |
| [RecipeScrapeService.cs:152-176](../backend/RecipeApp.API/Services/RecipeScrapeService.cs#L152-L176) | Remove the `cup`/`cups` rows from `UnitConversions` (§5.5); move `UnitConversions` + `ConvertUnit` into `MeasurementConverter` so all unit arithmetic lives in one file. The `ConvertUnit_*` tests in `RecipeScrapeServiceTests` move with it. |
| [RecipeFormView.vue:316](../frontend/src/views/RecipeFormView.vue#L316) | Replace with shared frontend constant; see §9 |
| [RecipeScrapePreviewView.vue:84-89](../frontend/src/views/RecipeScrapePreviewView.vue#L84-L89) | Free-text unit → `v-select` over the shared constant; see §9 |
| [RecipeIngredient.cs:16](../backend/RecipeApp.API/Models/RecipeIngredient.cs#L16) | Update the `<summary>` doc comment listing units |
| `SPEC.md:109`, `SPEC.md:155`, `CLAUDE.md:105` | Add `cup` to the documented unit set; note that `cup` is storable and resolved per §5.4 |

**Tests touched by this workstream alone:**

- `RecipeValidatorTests.cs:55-56` — add `[InlineData("cup")]`; add a case-variant rejection
  (`"Cup"`) to pin the strictness of `IsValid`.
- `RecipeScrapeServiceTests.cs:78-79` — the `cup`/`cups` rows currently assert `→ 240 ml`. They
  now assert canonical passthrough (`cups` → `cup`, amount unchanged).
- `ShoppingListServiceTests` — `tsp`/`tbsp` gain the volume dimension, so §3.3's consolidation bug
  is fixed by this workstream. Any test asserting the split-row behaviour is asserting the bug, not
  a contract.
- New `MeasurementUnitTests` — `TryCanonicalise` for every alias and case variant; `IsValid`
  rejects non-canonical spellings; `DimensionOf`/`ToBase` for every row.

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
`ml` in shopping-list consolidation (§7) from day one. It also composes cleanly with
`MeasurementUnit.ToBase()`: convert any volume to ml, multiply by density, get grams.

**Derive densities by dividing published grams-per-cup by 240**, the app's own
`MillilitresPerCup`. Published tables vary between the US customary cup (~236.6 ml) and the legal
cup (240 ml); dividing by our own constant makes the round trip exact either way
(`120 g/cup ÷ 240 = 0.5 g/ml`, and `1 cup × 240 ml × 0.5 = 120 g`), and the ~1.4% difference
between cup definitions is well below the row-to-row variation between published sources.

### 5.3 `null` is load-bearing

**`GramsPerMillilitre == null` means "no reliable density — do not invent a mass."**

This is what makes cups a supported unit rather than a tolerated one. For `1 cup chopped spinach`,
`2 cups mixed salad greens`, or `1 cup cubed bread`, packing dominates and any gram figure is fake
precision dressed as accuracy. The honest storage is `1 cup`, and the shopping list should say
`3 cup` — not a fabricated `72 g`.

So the rule splits cleanly:

- **Density known** → grams, because the conversion is real arithmetic.
- **Density unknown** → the cup is kept (or, for liquids, resolved to the *exact* `ml` equivalent —
  §5.4), because those are the more truthful measurements.

Neither branch guesses.

### 5.4 Conversion policy — cups only, three outcomes

Stage B (§5.5) resolves **`cup` only**. `tsp` and `tbsp` are left as stated.

Rationale: spoon measures are small, precise, universally available, and more useful to a cook as
spoons — `1 tsp salt` is better guidance than `6 g salt`. They also consolidate correctly on their
own once §4.1 gives them a dimension. Cups are the problem case precisely because the quantity is
large enough for density error to compound into a materially wrong recipe.

For a `cup` row, three deterministic outcomes, checked in order:

| # | Condition | Stored result | Why it is honest |
|---|---|---|---|
| 1 | `Ingredient.GramsPerMillilitre` is non-null | grams: `amount × 240 × density` | Real arithmetic on a curated constant |
| 2 | Else, `Ingredient.DefaultUnit` is one of `ml`, `L`, `tsp`, `tbsp` | millilitres: `amount × 240` | The cup *is* a volume unit. For something naturally measured by volume the conversion is exact and needs no density |
| 3 | Else | `cup`, as stated | Packing-dominated or unknown; any other figure is invented |

Rule 2 is what keeps liquids metric **without** putting them in the density table (§5.6). Its
failure mode is benign: a wrong `DefaultUnit` yields `240 ml` instead of `1 cup` — two spellings of
the same exact quantity — whereas a wrong density yields a wrong recipe. That asymmetry is why
liquids do *not* get densities. `cup` is deliberately excluded from rule 2's list: a catalogue entry
whose natural unit is the cup is exactly the packing case rule 3 exists for.

Encoded as `MeasurementUnit.ImportResolvable = [Cup]` so widening it later is a one-line change
with an obvious blast radius. This is an **import-time storage policy**; shopping-list consolidation
(§7) is a separate concern and uses density for any volume unit.

### 5.5 Pipeline changes

`ConvertUnit` becomes two stages. Stage A is the existing static customary→metric mapping plus
alias canonicalisation, with no ingredient context. Stage B is new, ingredient-aware, and runs only
after catalogue matching has resolved an `IngredientId`:

```
LLM extraction          →  (2, "cups")       source unit preserved (§1.2)
Stage A: Canonicalise   →  (2, "cup")        alias → canonical; oz/lb/fl oz/pt/qt/gal → g/ml/L as today.
                                             cup is NO LONGER converted here (v1.0 had → 480 ml)
[catalogue match]       →  IngredientId resolved (pass 1 exact, or pass 2 semantic)
Stage B: ResolveCup     →  (240, "g")        density 0.5 known                 — rule 1
                        →  (480, "ml")       DefaultUnit is ml/L/tsp/tbsp      — rule 2
                        →  (2,   "cup")      neither                           — rule 3
Source recorded         →  SourceAmount 2, SourceUnit "cups"   verbatim, on the preview row (§6)
```

**Stage A** — the `cup`/`cups` rows are removed from `UnitConversions`
([RecipeScrapeService.cs:165-166](../backend/RecipeApp.API/Services/RecipeScrapeService.cs#L165-L166)).
Every other row stays: ounces and pounds are mass, fluid ounces/pints/quarts/gallons are
overwhelmingly liquid, and none carries the dry/wet ambiguity worth modelling. Stage A then applies
`TryCanonicalise` to whatever it did not convert. A unit that neither converts nor canonicalises
(`clove`, `pinch`, `can`) passes through verbatim, as today.

**Stage B** — new `Services/MeasurementConverter.cs`: a small, dependency-free, unit-testable
service holding both stages (Stage A relocated from `RecipeScrapeService`), consumed by
`RecipeScrapeService.NormaliseAsync`, by the Phase 9 `RecipeLibrarySeeder`, and by
`ShoppingListService` (§7). It takes `MeasurementOptions` (§10) so the import policy can be
switched off.

In [NormaliseAsync](../backend/RecipeApp.API/Services/RecipeScrapeService.cs#L436), the existing
`dbIngredients` projection at
[RecipeScrapeService.cs:445-448](../backend/RecipeApp.API/Services/RecipeScrapeService.cs#L445-L448)
must add `GramsPerMillilitre` **and `DefaultUnit`** (rule 2 needs it). Stage B then applies in
**both** the pass-1 exact-match branch and the pass-2 semantic-match branch — pass 2 resolves an
`IngredientId` later ([lines 543-548](../backend/RecipeApp.API/Services/RecipeScrapeService.cs#L543-L548)),
so cup resolution has to happen after it, not inline with the current `ConvertUnit` call at
[RecipeScrapeService.cs:461](../backend/RecipeApp.API/Services/RecipeScrapeService.cs#L461). An
ingredient that stays `IsNew` has no catalogue row, so it takes rule 3 by construction.

Every preview row records `SourceAmount`/`SourceUnit` from the extracted values before Stage A
touches them.

**Where validation happens** (replacing the grammar constraint that §3.4 showed never existed):

`NormaliseAsync` does **not** throw on an unrecognised unit. It serves the interactive scrape
preview, which today lets the user correct a `clove` before confirming; failing the whole scrape for
one unit would be a regression. Enforcement sits at the two places a row is actually persisted:

- `ScrapeConfirmIngredientValidator` gains `.Must(MeasurementUnit.IsValid)` (§4.2). The preview's
  unit control becomes a select (§9), so in practice the user cannot submit anything else.
- Phase 9's `RecipeLibrarySeeder` gate (Phase 9 §11.3) rejects the recipe before it reaches
  `ConfirmAsync`, as that spec already intends — with the check now running against **canonicalised**
  units, so `teaspoon` no longer fails it while `clove` still does.

This validates the pipeline's own output, which is mechanical and deterministic, rather than the
model's, which is neither.

### 5.6 Seed density table

**Curated reference data, reviewed by a human — not LLM-generated.** Asking the model to invent
densities relocates the guessing to a place where it looks authoritative, which is the exact failure
mode this phase exists to remove. The set that actually matters is small.

Delivered as a static table in `Data/IngredientDensitySeeder.cs`, idempotent, and safe to re-run
after Phase 9 grows the catalogue. **Each row is keyed by a list of normalised catalogue names**,
not one name: the existing `DataSeeder` uses `plain flour`, `rice`, `breadcrumbs`, `salt`,
`parmesan cheese` and `cheddar cheese`, none of which equals the reference name. A seeder keyed on
`all-purpose flour` alone would give the app's own starter catalogue zero densities. Each row also
carries a source citation string, so a disputed value can be traced.

| Density row | Applies to catalogue names | g/cup | g/ml |
|---|---|---|---|
| all-purpose flour | all-purpose flour, plain flour, flour | 120 | 0.5000 |
| bread flour | bread flour | 120 | 0.5000 |
| whole wheat flour | whole wheat flour, wholemeal flour | 113 | 0.4708 |
| granulated sugar | granulated sugar, sugar, white sugar | 200 | 0.8333 |
| brown sugar (packed) | brown sugar | 213 | 0.8875 |
| powdered sugar | powdered sugar, icing sugar, confectioners sugar | 120 | 0.5000 |
| white rice (uncooked) | white rice, rice, long grain rice | 185 | 0.7708 |
| rolled oats | rolled oats, oats | 90 | 0.3750 |
| cornmeal | cornmeal, polenta | 138 | 0.5750 |
| cornstarch | cornstarch, cornflour | 120 | 0.5000 |
| cocoa powder | cocoa powder, cocoa | 85 | 0.3542 |
| dry breadcrumbs | dry breadcrumbs, breadcrumbs | 108 | 0.4500 |
| dried lentils | dried lentils, lentils | 192 | 0.8000 |
| dried black beans | dried black beans | 194 | 0.8083 |
| butter | butter | 227 | 0.9458 |
| honey | honey | 340 | 1.4167 |
| maple syrup | maple syrup | 322 | 1.3417 |
| peanut butter | peanut butter | 258 | 1.0750 |
| table salt | table salt, salt | 292 | 1.2167 |
| grated parmesan | grated parmesan, parmesan cheese, parmesan | 100 | 0.4167 |
| shredded cheddar | shredded cheddar, cheddar cheese, cheddar | 113 | 0.4708 |

Target ~40–60 entries covering flours, sugars, rices, grains, oats, legumes, nuts, dairy solids,
syrups and fats. Everything else stays `null` — which is the correct outcome, not a gap to be filled.

> **Not in the table, by design: water, milk, cream, stock, oil, juice, vinegar.** These are
> naturally volume-measured and reach `ml` through §5.4 rule 2 with no density at all. v1.0 listed
> `water 1.0`, `milk 1.029`, `vegetable oil 0.917`; with them, `1 cup milk` would have been stored
> as `247 g milk` — correct physics, useless cooking — and every consolidated milk row would then
> have been dragged into grams by §7. Density exists to reach the dimension an ingredient is
> **bought** in; liquids are bought by volume.

> The catalogue LLM seeder **may** propose a density for genuinely new ingredients, but only into a
> review queue or log — never written directly. Deferred; not in this phase.

---

## 6. Workstream 3 — Preserve the source measurement

### 6.1 Model change

`Models/RecipeIngredient.cs`:

```csharp
/// <summary>Amount exactly as stated by the source recipe, before conversion. Null for hand-entered rows.</summary>
public decimal? SourceAmount { get; set; }

/// <summary>Unit exactly as stated by the source recipe (e.g. "cups", "ounce"). Null for hand-entered rows.</summary>
public string? SourceUnit { get; set; }
```

`Data/AppDbContext.cs`, in the existing `RecipeIngredient` block
([AppDbContext.cs:40-53](../backend/RecipeApp.API/Data/AppDbContext.cs#L40-L53)):

```csharp
e.Property(ri => ri.SourceAmount).HasPrecision(10, 3);   // matches Amount
e.Property(ri => ri.SourceUnit).HasMaxLength(32);
```

`Amount`/`Unit` remain the single canonical value all arithmetic uses. `SourceAmount`/`SourceUnit`
are **provenance only** — never summed, never used in consolidation. They are scaled by the portion
multiplier at display time only, exactly like `Amount`, so `2 cups` at DOUBLE reads `4 cups`.

### 6.2 Why

This follows the established portion-scaling convention in `CLAUDE.md` — *"never modify stored
`Amount` values; multiply in the query/DTO layer"* — applied one level earlier: don't destroy the
input, derive from it.

Three concrete payoffs:

1. **Auditability.** Phase 9 §14's trial run wants to verify dry-cup handling across a sample. Today
   that is impossible — by the time a row is written, the source text is gone and `240 ml` is
   indistinguishable from a correct conversion. With these columns it is one query (the schema uses
   EF's default quoted PascalCase identifiers — there is no snake-case naming convention in this
   project):

   ```sql
   SELECT i."DisplayName", ri."SourceAmount", ri."SourceUnit",
          ri."Amount", ri."Unit", i."GramsPerMillilitre", i."DefaultUnit"
   FROM "RecipeIngredients" ri
   JOIN "Ingredients" i ON i."Id" = ri."IngredientId"
   WHERE ri."SourceUnit" ILIKE 'cup%';
   ```

2. **Retroactive correction.** If a density is wrong, the source measurement is still on record, so a
   backfill can recompute. Without it, the only recovery is re-running the 4–9 hour import.

3. **Display fidelity.** `240 g flour (2 cups)` — the recipe reads as written while the shopping list
   sums grams.

### 6.3 DTO, endpoint and service changes

| Item | Change |
|---|---|
| [RecipeIngredientResponse](../backend/RecipeApp.API/DTOs/Recipes/RecipeIngredientResponse.cs) | `+ decimal? SourceAmount, string? SourceUnit` |
| [ScrapePreviewIngredient](../backend/RecipeApp.API/DTOs/Scrape/ScrapePreviewResponse.cs) | `+ decimal? SourceAmount, string? SourceUnit` — lets the confirm screen show what was converted |
| [ScrapeConfirmIngredient](../backend/RecipeApp.API/DTOs/Scrape/ScrapeConfirmRequest.cs) | `+ decimal? SourceAmount, string? SourceUnit` — **this is the DTO that persists a scrape**; v1.0 omitted it. Phase 9 §12 maps preview → confirm through it too. |
| [ConfirmAsync](../backend/RecipeApp.API/Services/RecipeScrapeService.cs#L232-L241) | Write `SourceAmount`/`SourceUnit` onto the `RecipeIngredient` |
| [RecipeIngredientRequest](../backend/RecipeApp.API/DTOs/Recipes/CreateRecipeRequest.cs) | `+ decimal? SourceAmount, string? SourceUnit` (optional; null on hand entry). **Required, not optional, for round-trip:** `RecipeService.UpdateAsync` ([RecipeService.cs:78-79](../backend/RecipeApp.API/Services/RecipeService.cs#L78-L79)) deletes and recreates every ingredient row, so without these fields editing a scraped recipe in the form silently destroys its provenance. `CreateIngredients` (line 129-136) copies them through. |
| [IngredientResponse](../backend/RecipeApp.API/DTOs/Ingredients/IngredientResponse.cs) | `+ decimal? GramsPerMillilitre` |
| [CreateIngredientRequest](../backend/RecipeApp.API/DTOs/Ingredients/CreateIngredientRequest.cs) / [UpdateIngredientRequest](../backend/RecipeApp.API/DTOs/Ingredients/UpdateIngredientRequest.cs) | `+ decimal? GramsPerMillilitre` — lets a density be corrected without a migration |
| [IngredientsEndpoints.cs](../backend/RecipeApp.API/Endpoints/IngredientsEndpoints.cs#L57-L65) POST / [PUT](../backend/RecipeApp.API/Endpoints/IngredientsEndpoints.cs#L88-L90) | Assign `GramsPerMillilitre` from the request — the handlers copy fields explicitly |
| [Mappings.cs](../backend/RecipeApp.API/DTOs/Mappings.cs) | `ToResponse(Ingredient)` and `ToResponse(RecipeIngredient)` carry the new fields |
| [IngredientValidators.cs](../backend/RecipeApp.API/Validators/IngredientValidators.cs) | `RuleFor(x => x.GramsPerMillilitre).InclusiveBetween(0.01m, 3m).When(x => x.GramsPerMillilitre.HasValue)` — nothing in a kitchen is denser than ~2.2 g/ml. `DefaultUnit`, when supplied, must pass `MeasurementUnit.IsValid` (today it is only length-checked). |

The positional-record DTOs mean `MappingsTests` and any test constructing these records by position
will need the new arguments.

### 6.4 Manual entry is not silently converted

Two paths with deliberately different behaviour:

- **Import** (scrape, Phase 9 seeder) — the source unit is incidental, whatever the page happened to
  use. Resolve per §5.4; record the source.
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
   `GramsPerMillilitre` is non-null, convert the volume total (in ml) to mass and emit **one** row.
   Density applies here to **any** volume unit — `tsp`, `tbsp`, `cup`, `ml` — unlike the import
   policy in §5.4: a shopping total is a purchase quantity, and `1 tsp salt` + `10 g salt` is one
   item. The `Ingredient` is already loaded via the existing `.ThenInclude(ri => ri.Ingredient)`
   ([line 196](../backend/RecipeApp.API/Services/ShoppingListService.cs#L196)), so no extra query.
3. **Presentation rule** (deterministic, no heuristics):
   - `pcs` is always its own row.
   - Within a dimension, if every contributing row uses the **same unit string**, present in that
     unit: `2 cup + 1 cup = 3 cup`, `1 tbsp + 2 tbsp = 3 tbsp`. This is the rule v1.0 was missing —
     without it the spinach example presents as `720 ml`.
   - Otherwise sum in the base unit and present in base: `1 tbsp + 15 ml = 30 ml`,
     `500 g + 1 kg = 1.5 kg`.
   - After cross-dimension resolution the row is mass.
   - In every case a `g`/`ml` result ≥ 1000 promotes to `kg`/`L`, as today. Other units never
     promote.

`NeedsReview` then means what it should: genuinely irreconcilable units — `2 pcs onion` plus
`150 g onion`, where no density can help because `pcs` has no fixed size; or a volume/mass mix on an
ingredient with no density, such as `1 cup spinach` plus `100 g spinach`.

---

## 8. Migration

```bash
dotnet ef migrations add AddMeasurementDensityAndSource --output-dir Data/Migrations
```

Three nullable columns, no data movement, no backfill. Names follow the existing EF-default quoted
PascalCase (`"RecipeIngredients"`, `"DisplayName"`), not snake_case:

| Table | Column | Type |
|---|---|---|
| `"Ingredients"` | `"GramsPerMillilitre"` | `numeric(8,4) NULL` |
| `"RecipeIngredients"` | `"SourceAmount"` | `numeric(10,3) NULL` |
| `"RecipeIngredients"` | `"SourceUnit"` | `character varying(32) NULL` |

All existing rows read as "no density known / no source recorded", which degrades to exactly today's
behaviour. Density seeding runs separately and idempotently.

**Pre-flight check.** Because the confirm path has never validated units (§3.1 row 6), an existing
database may hold non-canonical values (`Tbsp`, `teaspoon`, `clove`). Run
`SELECT DISTINCT "Unit" FROM "RecipeIngredients"` before deploying and canonicalise or hand-fix any
stragglers; the stricter `MeasurementUnit.Table` will otherwise route them to a raw bucket rather
than a dimension.

> **This invalidates Phase 9's "no schema migration" claim** (Phase 9 §1, §13 and §19). See §11.

---

## 9. Frontend changes

Small, and the only reason this phase is not backend-only:

| File | Change |
|---|---|
| `src/constants/units.js` (new) | One frontend unit list `['g','kg','ml','L','pcs','tsp','tbsp','cup']`, mirroring `MeasurementUnit.All`, consumed by both views below. Still hardcoded frontend-side; a `GET /api/v1/units` endpoint is deferred (§13). |
| [RecipeFormView.vue:316](../frontend/src/views/RecipeFormView.vue#L316) | Replace the local `units` array with the shared constant. Round-trip `sourceAmount`/`sourceUnit` through `populateForm` ([lines 361-367](../frontend/src/views/RecipeFormView.vue#L361-L367)) and the submit payload ([lines 480-483](../frontend/src/views/RecipeFormView.vue#L480-L483)) so editing a scraped recipe keeps its provenance (§6.3). |
| [RecipeScrapePreviewView.vue:84-89](../frontend/src/views/RecipeScrapePreviewView.vue#L84-L89) | Unit becomes a `v-select` over the shared constant, so only storable units can be confirmed. A scraped unit not in the list (`clove`) shows unselected and must be picked. Carry `sourceAmount`/`sourceUnit` from the preview ([lines 213-223](../frontend/src/views/RecipeScrapePreviewView.vue#L213-L223)) into the confirm payload ([lines 256-265](../frontend/src/views/RecipeScrapePreviewView.vue#L256-L265)). |
| [RecipeDetailView.vue](../frontend/src/views/RecipeDetailView.vue#L220-L225) / [CookingModeView.vue](../frontend/src/views/CookingModeView.vue#L218-L222) `formatAmount` | When `sourceUnit` is present and differs from `unit`, show the source (portion-scaled) as secondary text: `240 g` **`(2 cups)`**. Never replaces the canonical amount. |
| Create-ingredient dialog in [RecipeFormView.vue:270-295](../frontend/src/views/RecipeFormView.vue#L270-L295) | Optional "Density (g/ml)" number field → `gramsPerMillilitre` on the create payload, with helper text: *"Leave blank to keep volume measurements as stated."* |
| Ingredient editing | **There is no ingredient admin/edit screen in the app** — v1.0 assumed one. The `ingredients` store already has `updateIngredient`, but no view calls it. This phase exposes density on `PUT /api/v1/ingredients/{id}` (§6.3) and corrections are made through Scalar in development. An edit UI is out of scope (§13). |
| [ShoppingView.vue:242](../frontend/src/views/ShoppingView.vue#L242) | No change — `cup` renders like any other unit. |

---

## 10. Configuration

One new section, bound as `Services/MeasurementOptions.cs` (`SectionName = "Measurement"`) beside
the existing `RecipeScrapingOptions`, registered in `Program.cs` and injected into
`MeasurementConverter`:

| Key | Default | Description |
|---|---|---|
| `Measurement:ResolveCupsOnImport` | `true` | When false, Stage B is skipped entirely and imports keep `cup` as stated regardless of density or default unit. Escape hatch for a Phase 9 trial run that needs to inspect raw conversions. |

`MillilitresPerCup` is deliberately **not** configurable (§4.1).

---

## 11. Impact on the Phase 9 spec

[phase-9-seed-recipe-library.md](phase-9-seed-recipe-library.md) needs these edits once this phase
lands:

| Section | Edit |
|---|---|
| §1 Overview (line 16), §13 ("No schema change"), §19 ("Any EF migration") | All three say no migration. Phase 9 now depends on this phase's migration, which must be applied before the first `seed-recipes` run. (v1.0 of this spec cited §2; §2 is the CLI deliverable and says nothing about migrations.) |
| §4.1 table | `ConvertUnit` is listed as "(private)" — it is `public static` ([RecipeScrapeService.cs:628](../backend/RecipeApp.API/Services/RecipeScrapeService.cs#L628)), and after this phase it lives in `MeasurementConverter`. |
| §11 example (line 325) | Shows the LLM emitting `{ amount: 411, unit: "g" }` for "1 can (14.5 ounces)". That is the model converting, which §1.2 forbids. Should read `{ amount: 14.5, unit: "ounces" }`; Stage A produces `411 g`. |
| §11.1 table | `cup` row: "240 ml, dry ingredients mass-dependent" → "resolved per Phase 8.5.1 §5.4: density → g, volume-natural → ml, else kept as `cup`" |
| §11.1 warning box | The dry-cup risk callout is largely retired — record that it is handled structurally, not by prompt |
| §11.3 validation gate | Unit check runs post-canonicalisation against `MeasurementUnit.IsValid`, so `teaspoon` passes and `clove` still fails |
| §14 trial bullets + criterion 3 | "Baking (dry-cup → grams)" and "Dry ingredients use mass units" → verify the *density table* covers the corpus's bulk dry goods; spot-check via the §6.2 query |
| §14 criterion 11 | Now genuinely reachable — §3.3's bug would have failed it regardless |
| §17.3 seeder tests | Assert `SourceAmount`/`SourceUnit` are persisted; assert `2 cups flour` → `240 g` with a seeded density and `2 cups spinach` → `2 cup` without one |
| §19 Out of scope | Only the migration line changes. Phase 9 itself still has zero frontend work — the frontend changes belong to this phase, not that one (v1.0 got this backwards). |

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
- **Imperial output.** Storage stays metric (plus `cup`, per this spec) per `CLAUDE.md`.
- **`GET /api/v1/units`.** Frontend keeps its own list this phase (§9).
- **An ingredient admin/edit screen.** Density corrections go through the API this phase (§9).
- **Unit validation on shopping-list custom items.** `AddCustomItemRequest.Unit` stays free text —
  that is by design for "1 bag", "1 bunch".
- **Widening import resolution to `tsp`/`tbsp`.** One-line change, deliberately deferred (§5.4).

---

## 14. Open questions for review

1. **Density seed size** — is ~40–60 curated entries the right initial scope, or start with the ~21
   in §5.6 and grow after the Phase 9 trial run reveals what the corpus actually uses?
   - 40 to 60 curated sounds like it would give us more distinct data for starting
2. **Packed vs. loose brown sugar** — 213 g/cup assumes packed. Model as two catalogue entries, or
   one entry with the assumption recorded in the seeder's citation string?
   - 2 catalogue entries
3. **Manual-entry conversion** — §6.4 stores hand-entered cups as-is. Confirm that is preferred over
   converting with a "converted from 1 cup" hint.
   - store hand-entered cups as-is
4. **`NeedsReview` on `pcs` mixes** — should `2 pcs onion` + `150 g onion` stay flagged, or should the
   catalogue eventually carry an average piece weight? (The latter is a fifth workstream; not
   proposed here.)
   - stay flagged
5. **Trial-run ordering** — run the Phase 9 20-recipe trial *before* finalising the density table, so
   the real corpus drives which ingredients get densities?
   - a step can be added into Phase 9 to review density table in regards to a real corpus which then validates this.
6. **Rule 2 relies on `DefaultUnit`** (§5.4). Its failure mode is benign (`240 ml` vs `1 cup`), but
   `DefaultUnit` is LLM-suggested for catalogue-seeded entries. Accept, or drop rule 2 and keep every
   density-less cup as `cup`? The latter is simpler but leaves imported liquids as `1 cup milk`.
   - keep every density-less cup as 'cup'
7. **Syrups and spreads** — honey, maple syrup and peanut butter are in the density table because
   they are sold by weight, but their catalogue `DefaultUnit` is `tbsp`. Keep them (→ grams), or
   remove them so rule 2 sends them to `ml`?
   - remove them so rule 2 sends them to 'ml'

---

## 15. Definition of Done

- [ ] `Enums/MeasurementUnit.cs` exists; all five hardcoded arrays (§3.1) deleted or migrated;
      `ScrapeConfirmIngredientValidator` enforces `MeasurementUnit.IsValid`
- [ ] `TryCanonicalise` covers every alias in §4.1 plus case variants; `IsValid` rejects
      non-canonical spellings; both unit-tested
- [ ] `JsonSchemaGrammar.AllowedUnits` deleted
- [ ] `cup`/`cups` removed from `UnitConversions`; `ConvertUnit_*` tests updated (cup rows assert
      canonical passthrough, not `240 ml`)
- [ ] `Ingredient.GramsPerMillilitre` and `RecipeIngredient.SourceAmount`/`SourceUnit` added;
      migration applies cleanly to an existing populated database; §8 pre-flight documented
- [ ] `Services/MeasurementConverter.cs` with unit tests covering: cup + density → g;
      cup + no density + volume `DefaultUnit` → ml; cup + neither → `cup`; tsp/tbsp untouched;
      alias canonicalisation; unknown unit passthrough; `ResolveCupsOnImport = false` keeps `cup`
- [ ] Density seeder is idempotent, matches by alias names, and gives every dry-goods entry in the
      existing `DataSeeder` (`plain flour`, `rice`, `breadcrumbs`, `salt`, `butter`,
      `parmesan cheese`, `cheddar cheese`) a density
- [ ] `NormaliseAsync` resolves cups in **both** the exact-match and semantic-match branches, and
      records `SourceAmount`/`SourceUnit` on every preview row
- [ ] `ConfirmAsync` persists `SourceAmount`/`SourceUnit`; `RecipeService` create/update carries
      them through
- [ ] `IngredientsEndpoints` POST/PUT accept and persist `GramsPerMillilitre`
- [ ] `1 tbsp olive oil` + `15 ml olive oil` consolidate to `30 ml` in one row, `NeedsReview = false`
      (regression test for §3.3)
- [ ] `1 cup flour` + `120 g flour` consolidate to `240 g` in one row (cross-dimension, §7)
- [ ] `1 tsp salt` + `10 g salt` consolidate to one mass row (density applies to spoons in
      consolidation, §7)
- [ ] `2 cup spinach` + `1 cup spinach` consolidate to `3 cup` — no fabricated mass, no `ml` (§5.3, §7)
- [ ] `1 cup spinach` + `100 g spinach` (no density) → two rows, `NeedsReview = true`
- [ ] Frontend: shared unit constant; `cup` selectable in the recipe form and the scrape preview;
      preview unit is a select; detail and cooking views show source alongside canonical; editing a
      scraped recipe preserves `sourceAmount`/`sourceUnit`; create-ingredient dialog accepts density
- [ ] `SPEC.md` (§measurements, data model) and `CLAUDE.md` (Measurements, Notes) updated
- [ ] Existing `ShoppingListServiceTests`, `RecipeValidatorTests`, `RecipeScrapeServiceTests`,
      `MappingsTests` updated where they asserted the old unit set or the §3.3 bug
- [ ] Full backend + frontend suites green
- [ ] Phase 9 spec updated per §11
