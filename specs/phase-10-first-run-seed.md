# Phase 10 — Distributable First-Run Seed

**Version:** 0.1 (draft)
**Date:** 2026-10-01
**Status:** Draft. Open questions in §9 need answers before implementation starts.
**Depends on:** Phase 9.3 (corpus-derived ingredient catalogue)
**Supersedes:** `DataSeeder`'s two hard-coded sample recipes, and the Development-only startup seed block in `Program.cs`
**Leaves alone:** the Phase 9 MyPlate pipeline (`seed-recipes`) as a personal-use tool

---

## 1. Why

The Phase 9 spec (lines 55, 156, 451 and 644) treats the whole MyPlate Kitchen library as a US federal
work, uncopyrighted under 17 USC §105. The data contradicts that. §105 covers only works created by
federal employees in their official duties, and the corpus's own `sourceCredit` field shows:

| Origin | Recipes (approx.) | §105 applies |
|---|---|---|
| Federal agencies (USDA CNPP/FNS, HHS/NHLBI, ODPHP, FDA) | ~216 | Yes |
| State universities and extension programs | ~650+ | No. States can hold copyright |
| Private organisations (Produce for Better Health Foundation, Food and Health Communications Inc., GOYA Foods, American Heart Association, National Pork Board, …) | ~150+ | No |
| No credit | 8 | Unverified |

USDA most likely republished these recipes with permission. That permission covers USDA's use. It
does not put them in the public domain or extend to this project. Ingredient lists are facts and not
copyrightable. Instructions, descriptions, notes and photos can be.

**Decision:** the recipe corpus is not distributed with the code. It stays a personal-use local
cache. What ships is:

1. **The ingredient catalogue**: names, display names, categories, default units and aliases. These
   are facts, curated by hand.
2. **Twelve original sample recipes**, written for this project, so a fresh install has something
   to cook, plan and shop from.

### 1.1 Exposure check

`git log --all` confirms no recipe text was ever committed. `normalised/`, `parsed/`, `raw/` and
`images/` have always been gitignored. The only tracked seed files are
`seed-data/ingredient-catalogue.json` and `seed-data/myplate/manifest.json`. Both are lists of facts.
The `.gitignore` comment claiming `normalised/` is committed is wrong (§7).

## 2. Decisions taken

| # | Question | Answer |
|---|---|---|
| D1 | Which environments seed on first run? | **All of them**, not just Development. `Testing` is the one exception (§4.2). |
| D2 | How often? | **Once per database.** Deleting a sample recipe must not bring it back on the next start. |
| D3 | Mark sample recipes as samples? | **No.** They are ordinary recipes that users can edit or delete. |
| D4 | The Phase 9 pipeline? | **Keep it.** It stays as a personal-use tool. The corpus is not distributed. |
| D5 | How many sample recipes? | **12.** |

## 3. Current state

- **Startup seeding is Development-only.** `Program.cs` migrates, imports the catalogue, applies
  densities and runs `DataSeeder` inside `if (app.Environment.IsDevelopment())`. A Production host
  does none of this, **including migrations**.
- **It runs on every start.** The catalogue import is idempotent (fill nulls, merge aliases), so
  re-running it is harmless today. `DataSeeder` guards on `db.Recipes.AnyAsync()`, so deleting
  every recipe brings the samples back. That breaks D2.
- **The catalogue is not published.** `RecipeApp.API.csproj` has no item for `seed-data/`, so
  `dotnet publish` and the Docker image leave it out. A Production container has no catalogue to
  load.
- **The sample recipes are C# code.** `DataSeeder` builds two recipes (Bolognese and Chicken
  Stir-Fry) with hard-coded amounts and step-to-ingredient indexes.
- **No endpoint deletes an ingredient.** Users can add and edit ingredients but not remove them.

## 4. Design

### 4.1 First-run ledger

Add a table that records each one-time seed step.

```
SeedLedgerEntry
  Key             text         PK      e.g. "ingredient-catalogue", "sample-recipes"
  Outcome         text                 "Applied" | "SkippedExistingData"
  ArtefactVersion int                  version field of the seed file that was applied
  AppliedAt       timestamptz
```

- One row per step, with the key held in a constants class (not inline strings).
- A step runs only when its key has no row. Running the step and writing its row happen in **one
  transaction**, so a crash halfway through leaves no row and the step re-runs cleanly next start.
- The primary key on `Key` also means two hosts starting against the same database at once can't
  both record the step. The loser's transaction fails and rolls back. The app is single-instance,
  so this is a safety net, not a design goal.

The ledger, not the data, is the evidence that a step ran. That is what D2 needs: an empty
`Recipes` table no longer means "never seeded".

### 4.2 Startup flow

`FirstRunSeeder.RunAsync` replaces the Development block in `Program.cs`, keeping the existing
order:

1. `MigrateAsync` (subject to Q1)
2. Ingredient catalogue, guarded by `"ingredient-catalogue"`
3. Densities: unchanged, idempotent, fills nulls only, so runs every start without a ledger entry
4. Sample recipes, guarded by `"sample-recipes"`

It runs in every environment except `Testing`. `RecipeAppFactory` uses `Testing` to keep startup
seeding out of integration tests, and that has to keep working.

The CLI commands are unchanged. `import-catalogue` and `import-catalogue --force` remain the way to
deliberately re-apply the catalogue. They don't consult or write the ledger.

### 4.3 Databases that already have data

The first start after upgrading sees no ledger rows. If it seeded anyway, a database holding the
1,123 MyPlate recipes would gain 12 more.

**Rule:** when a step's ledger row is missing but its target already holds data, record
`SkippedExistingData` and insert nothing.

| Step | "Already holds data" means |
|---|---|
| `ingredient-catalogue` | any `Ingredients` row exists |
| `sample-recipes` | any `Recipes` row exists |

A fresh database seeds both. An upgraded one records both as skipped and is left as it is.

### 4.4 Sample recipe artefact

`seed-data/sample-recipes.json` replaces the C# recipes in `DataSeeder`. It is committed and loaded
the same way as the catalogue.

```jsonc
{
  "version": 1,
  "recipes": [
    {
      "name": "Spaghetti Bolognese",
      "description": "…",
      "servings": 4,
      "ingredients": [
        { "ingredient": "ground beef", "amount": 500, "unit": "g", "notes": null },
        { "ingredient": "salt", "amount": null, "unit": null, "notes": "to taste" }
      ],
      "steps": [
        { "instruction": "…", "ingredients": ["olive oil", "onion"] }
      ]
    }
  ]
}
```

- **Ingredients are referenced by catalogue name.** They resolve by name, then alias, the same lookup
  `SeedCatalogueResolver` uses. The catalogue must load first. A sample recipe never creates an
  ingredient.
- **Steps reference ingredients by name, not index.** This avoids the positional-index fragility
  described in `Services/Seeding/CLAUDE.md`. Step numbers come from array order.
- **Persistence goes through the real API path.** Each recipe maps to a `CreateRecipeRequest`, is
  checked by `CreateRecipeRequestValidator`, and is saved by `RecipeService.CreateAsync`. A sample
  recipe is therefore exactly what a user could have saved by hand.
- **All or nothing.** The whole file is resolved and validated before anything is written. Any
  unresolved name, invalid unit or validator failure aborts the step, logs every error, writes no
  ledger row and lets the app start. The step retries on the next start, after the artefact is
  fixed.
- Units are canonical `MeasurementUnit` values. `Amount`/`Unit` follow the null-together rule. Zero
  is never used.

### 4.5 The twelve recipes

They are written for this project in my own words, as generic home-cooking dishes. The set is
chosen to exercise the app's features, not just to fill the list.

| # | Recipe | Meal | Serves | Exercises |
|---|---|---|---|---|
| 1 | Fluffy Pancakes | Breakfast | 4 | `cup` flour → density; ml milk; pcs egg |
| 2 | Vegetable Frittata | Breakfast | 4 | Vegetarian; shares onion and bell pepper |
| 3 | Overnight Oats | Breakfast | 2 | Frozen berries; yoghurt; honey |
| 4 | Greek Salad | Lunch | 4 | Vegetarian; olives (canned); vinegar |
| 5 | Red Lentil Soup | Lunch | 6 | Vegan; dry goods; broth |
| 6 | Tuna Salad Sandwiches | Lunch | 2 | Bakery; canned fish; mayonnaise |
| 7 | Spaghetti Bolognese | Dinner | 4 | Rewritten from the current C# sample |
| 8 | Chicken Stir-Fry | Dinner | 4 | Rewritten from the current C# sample; rice |
| 9 | Beef and Bean Chili | Dinner | 8 | Large batch; spices; canned beans |
| 10 | Sheet-Pan Salmon and Vegetables | Dinner | 4 | Seafood; lemon; potatoes |
| 11 | Black Bean Tacos | Dinner | 4 | Vegetarian; tortillas; frozen corn |
| 12 | Banana Bread | Snack/dessert | 10 | Baking; `cup` + g mixed; unquantified butter for greasing |

Requirements the set must meet, each checked by a test (§6):

- **Shared ingredients.** Onion, garlic, olive oil, bell pepper, rice and eggs each appear in at
  least two recipes, so a sample meal plan shows shopping-list consolidation.
- **Mixed units within one dimension**, for example `tbsp` olive oil in one recipe and `ml` in
  another, so consolidation merges across units.
- **At least one unquantified ingredient** (salt "to taste"), so the amount-less shopping path runs.
- **Every food aisle category used at least once:** `PRODUCE`, `MEAT_SEAFOOD`, `DAIRY`, `CANNED`,
  `SOUPS_BROTH`, `FROZEN`, `DRY_GOODS`, `GRAINS_RICE`, `PASTA_SAUCES`, `BAKING_SPICES`, `BAKERY`,
  `CONDIMENTS`, `JAM_NUT_BUTTER`. Non-food categories (`HOUSEHOLD`, `KITCHEN`, `BEVERAGES`,
  `COFFEE_TEA`, `OTHER`) are exempt.
- **Every ingredient resolves** against the shipped, curated catalogue.

There are no images. Users can add their own.

### 4.6 Shipping the artefacts

Add both files to `RecipeApp.API.csproj` with `CopyToOutputDirectory` and `CopyToPublishDirectory`
set to `PreserveNewest`. `IngredientCatalogueFileStore` already resolves its path against
`ContentRootPath`, which in the container is `/app`, so the published copy is found without a
config change. The sample-recipe store follows the same pattern with its own
`RecipeSeedingOptions`-style path setting.

Nothing under `seed-data/myplate/` is published.

### 4.7 Catalogue curation

The catalogue was derived from the MyPlate corpus and needs a human pass before it ships. A
profile of the 1,055 entries:

| Signal | Count | Examples |
|---|---|---|
| Used by only one corpus recipe | 533 | Not wrong in itself, but the main source of oddities |
| Sounds prepared or sub-recipe | 88 | `light italian dressing recipe`, `eating smart seasoning mix`, `cookie crust`, `basic soup and sauce mix`, `better baking mix` |
| Either/or entries | — | `cheddar or american cheese`, `light honey mustard or poppy seed dressing` |
| Size or format in the name | 5 | `6 1/2-inch whole wheat pita pockets` |
| Near-duplicates | — | `dried egg mix` / `dry egg mix` / `egg mix` |
| Category `OTHER` | 7 | Need a real category or removal |

**Process:**
1. Generate `seed-data/catalogue-review.md` (not committed) listing every flagged entry with its
   signal and a suggested action: keep, rename, fold into another entry as an alias, or drop.
2. The project owner decides each one. No entry is removed or merged on a suggestion alone.
3. Apply the decisions to `ingredient-catalogue.json` by hand. `IngredientCatalogueValidator` must
   still pass.
4. Bump the artefact's `version`.

The artefact's `source` block (`"corpus": "myplate"`, corpus counts) and the per-entry
`corpusRows`/`corpusRecipes` counts describe provenance only. See Q3.

### 4.8 Retiring `DataSeeder`

`DataSeeder` and its `SampleIngredients` list are deleted. `DataSeederTests` is replaced by tests
for the new seeder (§6). The comment in `Program.cs` naming `DataSeeder` is updated.

## 5. Licensing documentation corrections

These docs make public-domain claims that §1 shows are wrong. They are corrected in this phase so
the record doesn't mislead anyone later.

- `specs/phase-9-seed-recipe-library.md` lines 55, 156, 451 and 644: add a correction note pointing to
  this spec. Phase records are history, so the original text stays and the note is added next to it.
- `RecipeSeeding:SeedAttributionNote` (`"Source: USDA MyPlate Kitchen (public domain)"`): drop
  "(public domain)". This only affects personal databases built with `seed-recipes`.
- `.gitignore`: fix the comment claiming `manifest.json` and `normalised/` are committed.
- `Services/Seeding/CLAUDE.md` and `backend/CLAUDE.md`: state that the corpus is personal-use only
  and is never committed or published.
- Root `CLAUDE.md`: add Phase 10 to the status line and the docs table.

## 6. Tests

Integration tests (Testcontainers):

- A fresh database seeds the catalogue and all 12 recipes, and writes two `Applied` ledger rows.
- A second run is a no-op.
- **D2:** delete every recipe, run again, and no recipe reappears.
- **§4.3:** a database with existing recipes and no ledger gets `SkippedExistingData` and no new
  recipes. The same holds for ingredients.
- A sample artefact with an unresolvable ingredient writes nothing, records no ledger row, and does
  not throw out of startup.
- A failure after the catalogue step leaves the catalogue's ledger row in place, and the recipe
  step retries alone.
- `Testing` environment: startup seeds nothing.

Artefact tests (no database):

- The shipped `sample-recipes.json` resolves fully against the shipped catalogue, and every recipe
  passes `CreateRecipeRequestValidator`.
- The §4.5 requirements: shared ingredients, mixed units, an unquantified row, and full food-aisle
  category coverage.
- Both artefacts are present in the build output directory.

## 7. Implementation stages

1. **Catalogue review list.** Generate the list. The owner decides. Apply the decisions and bump the
   version.
2. **Ledger.** Entity, migration and constants.
3. **Sample artefact.** Write the 12 recipes and add their store and loader.
4. **`FirstRunSeeder`.** Replace the Development block, delete `DataSeeder`, add the csproj publish
   items.
5. **Tests** (§6).
6. **Docs** (§5) and a results section in this spec.

## 8. Out of scope

- Distributing any MyPlate recipe text, image or the 216 federal-origin recipes. Each "adapted
  from" credit would need checking one by one. That may be worth doing later; it isn't needed now.
- Shipping catalogue updates to existing installs. See Q2.
- Ingredient deletion.

## 9. Open questions

**Q1. Should Production auto-migrate on start?**
Today only Development migrates, so a Production database is never created or upgraded. "Seed on
first run for everyone" only works if the schema exists.
*Recommendation:* yes, migrate on start in every environment except `Testing`. This is a
single-instance self-hosted app, and there is no separate deploy step that could run migrations
instead.

answer: Yes, worst case can rebuild the database.  I still want the seed data for myself, but want us to be able to test the whole process too.

**Q2. When a later release improves the catalogue, should existing installs receive it?**
D2 says each step runs once. Applied strictly to the catalogue, an install would keep its first
catalogue forever, and improvements would arrive only through a manual `import-catalogue`.
*Recommendation:* keep it strictly once for now (matching D2), with `import-catalogue` as the
documented upgrade path. `ArtefactVersion` is recorded in the ledger so an automatic additive
update (insert new entries only, never touch existing ones) can be added later without a schema
change.

Answer: Yes to your recommendation 

**Q3. Strip MyPlate provenance from the shipped catalogue?**
The `source` block and the per-entry `corpusRows`/`corpusRecipes` counts are facts and carry no
licensing risk. Once the catalogue is hand-curated, though, they no longer describe it accurately.
*Recommendation:* replace `source` with `"curated"` and a version, and drop the per-entry counts
from the shipped file. Keep them in the review list, where they help decisions.

answer: agreed to the recommendation