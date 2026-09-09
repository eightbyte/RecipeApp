# Phase 9.1 — Unquantified Ingredients

**Version:** 1.0
**Date:** 2026-09-07
**Status:** Proposed. Blocks Phase 9 Stage 4.
**Depends on:** Phase 9 Stages 1–3 complete (the parsed corpus is the evidence base)
**Supersedes:** Phase 9 §11.3 bullet 2 (`Any ingredient has Amount <= 0`)

> **Why this is its own spec and not an edit to Phase 9.** The correction cannot be contained
> inside Stage 4. `RecipeIngredient.Amount` is a non-nullable `decimal` enforced by two
> FluentValidation validators, consumed by shopping-list consolidation and rendered unguarded by
> four frontend views. Making "this ingredient has no quantity" representable is a schema change
> with a migration and a cross-cutting blast radius on a v1-feature-complete app. That is a
> different kind of change from a seeding stage, and it should be reviewable on its own.

---

## 1. The proposal under review

From §22.1 item 2, carried into the review request:

> Stage 4 needs a deliberate policy — the likely shape being a `to taste` convention that stores
> the ingredient with a null or zero amount and exempts it from the gate, keeping the gate's teeth
> for an ingredient that *states* a quantity the model then got wrong.

**Verdict: the diagnosis is exactly right and the remedy is directionally right, but as written it
is not sufficient to ship.** Three gaps, each measured rather than argued.

### 1.1 It is not a "to taste" convention — the name mismatches the population

Phrase-matching `to taste` / `as needed` / `optional` catches **40 of the 432** affected lines
(9.3%). Categorising all 432:

| Category | Lines | Share | Examples |
|---|---:|---:|---|
| Ordinary food, no stated quantity | 212 | 49.1% | `raisins`, `nuts`, `lemon zest`, `sunflower seeds`, `other vegetable toppings` |
| Seasoning / genuinely "to taste" | 154 | 35.6% | `salt`, `black pepper`, `salt and pepper, to taste`, `garlic powder` |
| Cooking spray | 35 | 8.1% | `nonstick cooking spray`, `sprays of nonstick vegetable spray` |
| Equipment, not food | 18 | 4.2% | `bamboo skewers`, `aluminum foil`, `ice cube tray or small paper cups` |
| Group heading (§22.1 item 3) | 12 | 2.8% | `For the Dressing`, `Topping`, `Salad` |
| **Has a quantity, no ASCII digit** | **1** | 0.2% | `¼ cup sliced almonds (optional)` |

Nearly half are ordinary foods that simply state no amount. A convention keyed on the *words*
"to taste" would leave ~64% of the population still failing the gate. **The predicate must be
"the source line states no quantity", not "the source line says to taste."**

### 1.2 The gate is not the only place `Amount > 0` is enforced

Three further enforcement points, none mentioned in the proposal:

| Where | Rule | Consequence of a zero/absent amount |
|---|---|---|
| [ScrapeValidators.cs:49](../backend/RecipeApp.API/Validators/ScrapeValidators.cs#L49) | `RuleFor(x => x.Amount).GreaterThan(0)` | — |
| [RecipeValidators.cs:12](../backend/RecipeApp.API/Validators/RecipeValidators.cs#L12) | `RuleFor(x => x.Amount).GreaterThan(0)` | applies to create **and** update |
| [RecipeIngredient.cs:16](../backend/RecipeApp.API/Models/RecipeIngredient.cs#L16) | `public decimal Amount` — non-nullable | null is not representable at all |

`ConfirmAsync` performs **no validation of its own** — it maps the request straight onto entities
([RecipeScrapeService.cs:210](../backend/RecipeApp.API/Services/RecipeScrapeService.cs#L210)).
Validation lives only in the endpoint filter (`.WithValidation<T>()`). Stage 5 calls `ConfirmAsync`
as a service method, so it **bypasses the validator entirely**.

That produces the trap: seeding would silently persist 313 recipes that the API's own validators
reject. They would display fine and then return `400` the first time a user opened one in the edit
form and pressed save — on a row they never touched. A seeded library whose recipes cannot be
edited is worse than the failure it was avoiding, and nothing in the pipeline would report it.

### 1.3 Exempting the gate does not tell the model what to emit

`RecipeSchemaJson` ([RecipeScrapeService.cs:31](../backend/RecipeApp.API/Services/RecipeScrapeService.cs#L31))
declares `amount` as `{"type": "number"}` and lists it in `required`. Decoding is
grammar-constrained via `JsonSchemaGrammar`, so for `salt` the model **cannot** emit "no amount" —
the grammar will not let it. It is forced to invent: `0`, or `1` with a fabricated unit
(`pinch`, `dash`) that then trips the *unit* rule instead.

So the gate exemption is downstream of the actual problem. **The fix has to start at the schema.**

One piece of good news: `JsonSchemaGrammar` already supports nullable unions
(`JsonSchemaGrammar.cs:52–60` handles `"type": ["T", "null"]` and emits `(number | null)`), so
expressing this costs one schema edit and one prompt line, not a grammar change.

### 1.4 What the proposal gets right

The core judgement is correct and is adopted unchanged: this is **a real property of home cooking,
not a parse defect**, the blanket rule must go, and the gate must stay armed for a stated quantity
the model got wrong. Everything below is in service of that.

---

## 2. Measured evidence

Measured across all 1,089 files in `seed-data/myplate/parsed/`.

| | Result |
|---|---:|
| Recipes parsed | 1,089 |
| Ingredient lines | 8,601 |
| Lines with no stated quantity | **432 (5.02%)** |
| Recipes touched | **313 (28.74%)** |
| Distinct line texts among them | 235 |
| Recipes *entirely* unquantified | **0** |
| Lines with a vulgar fraction and no ASCII digit | 1 |
| Lines matching `to taste` / `as needed` / `optional` | 40 |

Distribution across the 313 affected recipes:

| Unquantified lines | Recipes |
|---:|---:|
| 1 | 228 |
| 2 | 64 |
| 3 | 15 |
| 4–7 | 6 |

**Two facts from this that shape the design:**

1. **No recipe is entirely unquantified** — the worst case is 7 of its lines. So exempting these
   never yields a recipe with no measurable ingredient, the `Ingredients.NotEmpty()` invariant is
   untouched, and every recipe retains lines the gate still fully polices. The exemption is a
   narrow hole, not a bypass.
2. **`¼ cup sliced almonds (optional)` proves a naive `\d` test is unsafe.** A predicate that keys
   on ASCII digits alone would classify a real quarter-cup as unquantified and the model would be
   *instructed* to discard it. That is silent data loss, which this project treats as worse than a
   loud failure.

---

## 3. Design

### 3.1 The convention

> **An unquantified ingredient is one whose source line states no quantity. It is stored with
> `Amount = null` and `Unit = null`.**

Null means "there is no amount", which is the true statement. This is the same convention
`Ingredient.GramsPerMillilitre == null` already uses for "no reliable density — do not invent a
mass" (CLAUDE.md), applied to the quantity itself.

`Unit` goes null alongside `Amount` deliberately. `salt` has no unit either; storing
`Amount = null, Unit = "g"` would fabricate a unit to satisfy a column, which is the same class of
error as fabricating a gram figure from an unknown density.

**There is direct precedent in this codebase for exactly this shape.**
[ShoppingList.cs:23–24](../backend/RecipeApp.API/Models/ShoppingList.cs#L23-L24) already declares:

```csharp
public decimal? Amount { get; set; }
public string?  Unit   { get; set; }
```

and [ShoppingView.vue:86](../frontend/src/views/ShoppingView.vue#L86) already renders it with
`v-if="item.amount"`. The shopping list has always been able to say "this item has no amount".
Phase 9.1 gives the recipe the same vocabulary.

#### Options considered

| | Approach | Verdict |
|---|---|---|
| **A** | `Amount = 0`, exempt from gate | **Rejected.** Cheap only in appearance. It still requires relaxing both validators (§1.2), still requires the display guard (`0 g Salt` otherwise renders in recipe detail and cooking mode), and still requires a consolidation guard (`0 g Salt` otherwise appears as a shopping row). Same blast radius as B, and it leaves a magic sentinel that reads as a legitimate decimal everywhere it is not specifically handled. `Unit` would still have to be fabricated. |
| **B** | `Amount = null`, `Unit = null` | **Recommended.** Semantically true, matches the existing `ShoppingListItem` precedent and the `GramsPerMillilitre` convention, and makes the illegal state unrepresentable rather than merely unlikely. Costs one migration. |
| **C** | A `to taste` pseudo-unit in `MeasurementUnit` | **Rejected.** `MeasurementUnit.Table` is a dimension/base-factor arithmetic table; a non-arithmetic member breaks `ToBase`, `DimensionOf` and consolidation, and it would surface in every unit picker in the frontend as a selectable option for hand-entered recipes. |

### 3.2 The revised gate

Stage 4 computes, per ingredient line, whether **the source** stated a quantity — from
`ParsedIngredientLine.Text`, which Stage 3 already carries verbatim. No re-parse, no new cache
field.

```
HasStatedQuantity(text) :=
       text contains an ASCII digit [0-9]
    or text contains a Unicode vulgar fraction (U+00BC–U+00BE, U+2150–U+215E)
    or text's leading token is a number word (one…twelve, half, quarter, dozen)
```

The leading-token restriction on number words is deliberate: it catches `Two cloves garlic` without
misreading `cut into one-inch pieces`. Measured, zero lines in this corpus reach that clause — it
is there so the rule is correct rather than merely sufficient for one corpus.

The gate then becomes, replacing §11.3's `Any ingredient has Amount <= 0`:

| Source line | Model must emit | Gate rejects if |
|---|---|---|
| **States a quantity** | `amount > 0`, valid `unit` | `amount` is null, `<= 0`, or the unit fails `IsValid` after `TryCanonicalise` — **unchanged from §11.3** |
| **States no quantity** | `amount: null`, `unit: null` | the model emits a **non-null** amount |

**This is strictly stronger than the rule it replaces, not weaker.** The blanket rule had nothing
to say about a model inventing `1 tsp` for a line that said `salt` — it would sail through as a
positive amount. The revised gate rejects it. The exemption is *earned* by evidence from the source
text, never granted by the model's own output, so the model cannot opt itself out by emitting zero.

Every other §11.3 bullet is unchanged.

### 3.3 LLM schema and prompt

In `RecipeSchemaJson`:

```jsonc
"amount": {
  "type": ["number", "null"],
  "description": "Numeric quantity as stated on the page. Null if the line states no quantity (e.g. 'salt', 'salt and pepper to taste')."
},
"unit": {
  "type": ["string", "null"],
  "description": "Unit as stated on the page. Preserve the original unit; do not convert. Null when amount is null."
}
```

`amount` and `unit` stay in `required` — the model must *state* null, not omit the key, so a
missing quantity is always an explicit assertion. `JsonSchemaGrammar` already emits
`(number | null)` for this form, so no grammar work is needed.

The Stage 4 prompt gains one instruction: **never invent a quantity; if the line does not state
one, emit null.** This is the same discipline the existing prompt already applies to units
("preserve the original unit; do not convert").

> **Note for the Phase 3 scrape path.** This schema is shared with interactive scraping, which is
> the point — a scraped page saying `salt to taste` has always had the same problem, and the
> preview screen has always shown a fabricated number for it. Phase 9.1 fixes both. See §5.

### 3.4 Display

`formatMeasurement` returns `null` for a null amount; each call site already has a `v-if` idiom to
follow ([ShoppingView.vue:86](../frontend/src/views/ShoppingView.vue#L86)). An unquantified ingredient
renders as its name alone — `Salt`, not `0 g Salt`.

In the recipe form and the scrape preview, the amount field is left empty and the unit select shows
no selection; saving that row round-trips null rather than coercing to `0`.

> Whether an unquantified row should carry a visible affordance — a muted `to taste` chip — is
> §9 Q2. The default assumed here is no: the name alone is how a printed recipe renders it.

### 3.5 Shopping-list consolidation

An unquantified row must **never** be summed. `0` is not a quantity, and adding it silently claims
the recipe needs none.

In `ShoppingListService.GenerateItems`, unquantified rows are partitioned out before grouping
([ShoppingListService.cs:194–206](../backend/RecipeApp.API/Services/ShoppingListService.cs#L194-L206)).
An ingredient that appears **only** unquantified across the plan emits one amount-less
`ShoppingListItem` (`Amount = null, Unit = null`) — the shape a custom item already takes, which
`ShoppingView` already renders. An ingredient that appears quantified in one recipe and
unquantified in another consolidates the quantified rows exactly as today and drops the
unquantified one, because "500 g flour and also some flour" is not more useful than "500 g flour".

`ConsolidateRows` and `Present` keep their non-nullable `decimal` row type — they never see an
unquantified row. **`MeasurementConverter` is not touched at all**: a null amount is never passed
to `ToCanonical` or `ResolveForImport`, it flows through Stage A and Stage B untouched.

---

## 4. Work inventory

### Backend

| File | Change |
|---|---|
| `Models/RecipeIngredient.cs` | `Amount` → `decimal?`, `Unit` → `string?` |
| `Data/Migrations/` | New migration `AllowUnquantifiedRecipeIngredients` — two columns to nullable. No data change; no existing row is affected |
| `DTOs/Recipes/CreateRecipeRequest.cs` | `RecipeIngredientRequest.Amount` → `decimal?`, `Unit` → `string?` |
| `DTOs/Recipes/RecipeIngredientResponse.cs` | same |
| `DTOs/Scrape/ScrapeConfirmRequest.cs` | `ScrapeConfirmIngredient.Amount`/`Unit` → nullable |
| `DTOs/Scrape/ScrapePreviewResponse.cs` | `ScrapePreviewIngredient.Amount`/`Unit` → nullable |
| `Validators/RecipeValidators.cs:12` | `GreaterThan(0)` → `.When(x => x.Amount.HasValue)`; add the paired rule that `Amount` and `Unit` are both null or both set |
| `Validators/ScrapeValidators.cs:49` | same |
| `DTOs/Mappings.cs:66,126` | pass the nullable through |
| `Services/RecipeService.cs:135` | assign nullable |
| `Services/RecipeScrapeService.cs:31` | `RecipeSchemaJson` — nullable `amount`/`unit` (§3.3) |
| `Services/RecipeScrapeService.cs:210` | assign nullable in `ConfirmAsync` |
| `Services/RecipeScrapeService.cs:438,454` | skip Stage A/B conversion when amount is null |
| `Services/RecipeScrapeService.cs:638` | `double Amount` → `double?` on the LLM DTO |
| `Services/ShoppingListService.cs:194–206` | partition unquantified rows out of consolidation (§3.5) |
| `Services/Seeding/` (Stage 4, new) | `HasStatedQuantity` + the revised gate (§3.2) |

`MeasurementConverter`, `MeasurementUnit`, `JsonSchemaGrammar` and every endpoint file: **unchanged.**

### Frontend

| File | Change |
|---|---|
| `constants/units.js:18` | `formatMeasurement` returns null for a null amount |
| `views/RecipeDetailView.vue:115,225` | guard the amount span |
| `views/CookingModeView.vue:112,221` | guard the amount span |
| `views/RecipeFormView.vue:125,380,511` | allow an empty amount; stop coercing `Number(ing.amount)` to `0` |
| `views/RecipeScrapePreviewView.vue:75,222,275` | same |
| `views/ShoppingView.vue` | **no change** — already guarded |

**Scale: ~16 backend files, 5 frontend files, 1 migration, plus tests.** No endpoint contract
changes shape; two fields widen from required to nullable.

---

## 5. Blast radius outside Phase 9

This changes the interactive scrape and hand-entry paths, not only seeding. That is intended — the
same defect exists there — but it must be called out for review rather than smuggled in under a
seeding phase:

- **Hand-entered recipes may now omit an amount.** A user can add `salt` with no quantity. This is
  a small product change, and a desirable one, but it is a change.
- **A scrape preview may now show an empty amount** where it previously showed a fabricated number.
  Better, and more honest, but visibly different.
- **Existing rows are unaffected.** The migration only widens the columns; every current row keeps
  its value, and nothing backfills. No seeded or user data is rewritten.

---

## 6. What this does *not* fix

**The ~40 colon-less group headings (§22.1 item 3) still reach Stage 4** — `Dressing`, `Topping`,
`Salad`. 12 of them fall in the unquantified population, so the exemption would usher them into the
database as amount-less ingredients rather than rejecting them. They are a separate problem with a
separate fix (semantic, at Stage 4, where the information to judge them exists) and Phase 9.1
deliberately does not solve it. It is worth noting that Phase 9.1 makes them *quieter*: a heading
that previously would have failed the gate now passes it. Tracked as §9 Q3.

**Equipment lines** (`bamboo skewers`, `aluminum foil` — 18 lines) are likewise out of scope. They
will persist as ingredients with no amount and land in the `Other` category.

---

## 7. Testing

### Backend — new

| Test | Asserts |
|---|---|
| `HasStatedQuantityTests` | ASCII digits, each vulgar fraction in range, leading number words, `one-inch` *not* matched mid-line, and the literal corpus line `¼ cup sliced almonds (optional)` reads as quantified |
| `Stage4GateTests` | unquantified source + null amount → **accept**; unquantified source + non-null amount → **reject** (the new tooth); quantified source + null amount → **reject**; quantified source + `0` → **reject**; quantified source + valid amount → **accept** |
| `ShoppingListServiceTests` | an ingredient only ever unquantified → one amount-less row; quantified + unquantified across two recipes → the quantified total alone, `0` never added; `NeedsReview` unaffected |
| `RecipeIngredientRequestValidatorTests` | null amount + null unit passes; null amount + non-null unit fails; non-null amount + null unit fails; `0` still fails |

### Backend — existing suites to re-green

`ScrapeValidatorTests`, `ValidationFilterTests`, `RecipeServiceTests`, `RecipeScrapeServiceTests`,
`ShoppingListServiceTests` — all touch `Amount` and will need the nullable signature.

### Frontend

| Test | Asserts |
|---|---|
| `constants/units.spec.js` | `formatMeasurement(null, null)` → null; existing cases unchanged |
| `RecipeDetailView.spec.js` | an unquantified ingredient renders its name with no `0 g` |
| `RecipeFormView.spec.js` | an empty amount round-trips as null, not `0` |

### Corpus verification (not a unit test)

After Stage 4 runs, assert corpus-wide: **432 rows with a null amount, 8,169 with a positive one,
and zero rows with `Amount = 0`.** A zero anywhere means a fabricated quantity got through.

---

## 8. Definition of done

- [ ] Migration applied; `Amount`/`Unit` nullable on `RecipeIngredient`
- [ ] Both validators accept null-and-null, reject a half-set pair, still reject `0`
- [ ] `RecipeSchemaJson` declares nullable `amount`/`unit`; grammar verified to emit `null`
- [ ] Stage 4 gate implements the §3.2 table, including the new reject-on-invented-quantity case
- [ ] Consolidation never sums an unquantified row; an only-unquantified ingredient yields one amount-less item
- [ ] No view renders `0 g` for an unquantified ingredient; form and preview round-trip null
- [ ] Full backend and frontend suites green
- [ ] Corpus check: 432 null, 0 zero
- [ ] Phase 9 §11.3 amended to point here; §22.1 item 2 marked resolved
- [ ] `CLAUDE.md` measurement section records the null-amount convention

**Consequence for Phase 9 §20.** With this in place the 28.7% is no longer a floor on failure, and
the "≥ 95% persisted" bar becomes a measurement of the model's accuracy — which is what it was
meant to measure.

---

## 9. Open questions

1. **Nullable `Unit` alongside nullable `Amount` — confirm.** §3.1 argues both, on the grounds that
   a fabricated unit is the same error as a fabricated mass. The cheaper alternative is a nullable
   `Amount` with `Unit` left non-nullable and set to `""`. Recommend **both nullable**; it is one
   extra column in the same migration and it matches `ShoppingListItem` exactly.
2. **Should an unquantified ingredient show a `to taste` affordance in the UI?** §3.4 assumes no.
   Note that only 35.6% of the population are seasonings, so a literal `to taste` label would
   misdescribe `raisins` and `bamboo skewers`. If an affordance is wanted, "as needed" is the
   honest wording.
3. **Group headings (§6) — fix in Phase 9 Stage 4, or defer?** They are ~40 lines across the
   corpus and Phase 9.1 makes them pass the gate rather than fail it. Recommend fixing in Stage 4,
   tracked separately, not folded in here.
4. **Does the trial run (§14) need a stratum for unquantified ingredients?** Recommend **yes** —
   at least 3 of the 20 pinned slugs should be drawn from the 313, and one from the 6 recipes
   carrying 4+ unquantified lines.
