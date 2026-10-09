# Phase 10 — Distributable First-Run Seed

**Version:** 0.3
**Date:** 2026-10-05 (0.1: 2026-10-01, 0.2: 2026-10-04)
**Status:** Ready for implementation. Q1–Q4 are answered and folded in (§2).
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
The `.gitignore` comment claiming `normalised/` is committed is wrong (§5).

## 2. Decisions taken

| # | Question | Answer |
|---|---|---|
| D1 | Which environments seed on first run? | **All of them**, not just Development. `Testing` is the one exception (§4.2). |
| D2 | How often? | **Once per database.** Deleting a sample recipe must not bring it back on the next start. |
| D3 | Mark sample recipes as samples? | **No.** They are ordinary recipes that users can edit or delete. |
| D4 | The Phase 9 pipeline? | **Keep it.** It stays as a personal-use tool. The corpus is not distributed. |
| D5 | How many sample recipes? | **12.** |
| D6 | Migrate on start outside Development? (Q1) | **Yes**, in every environment except `Testing`. The worst case is rebuilding the database. |
| D7 | Do catalogue improvements reach existing installs? (Q2) | **No, not automatically.** Each step runs once. `import-catalogue` is the documented upgrade path. The ledger records the artefact version so an additive update can be added later. |
| D8 | MyPlate provenance in the shipped catalogue? (Q3) | **Removed.** The `source` block and per-entry corpus counts are dropped from the shipped file and kept only in the review list (§4.7). |
| D9 | Can curation delete a catalogue entry? | **No. Fold, never drop.** An entry is kept, renamed, or folded into another entry as an alias. Every MyPlate name must still resolve, or `seed-recipes --persist` fails for the recipes that use it (§4.7). |
| D10 | What must a clean start support? | **Two paths from an empty database:** the distributed set alone, or the distributed set plus the personal MyPlate library (§4.9). |
| D11 | Is the sample-recipe file a one-off? (Q4) | **No.** It is the first **recipe pack**, a general import format with an `import-recipes` CLI command (§4.4). MyPlate keeps its own `--persist` path. |

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
  ArtefactVersion int                  contentVersion of the seed file that was applied (§4.7)
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

1. `MigrateAsync` (D6). Under the published Docker image (`ASPNETCORE_ENVIRONMENT=Production`), this
   is the first time the schema gets created at all.
2. Ingredient catalogue, guarded by `"ingredient-catalogue"`
3. Densities: unchanged, idempotent, fills nulls only, so runs every start without a ledger entry
4. Sample recipes: the built-in pack through `RecipePackImporter` (§4.4), guarded by `"sample-recipes"`

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

### 4.4 Recipe packs (D11)

A **recipe pack** is a JSON file of recipes in one fixed format that any source can produce.
`seed-data/sample-recipes.json` is the first pack. It replaces the C# recipes in `DataSeeder`, is
committed, and ships with the app (§4.6). The `import-recipes` CLI command imports any other pack
(§4.4.3). Both use the same importer.

#### 4.4.1 Format

```jsonc
{
  "formatVersion": 1,
  "contentVersion": 1,
  "recipes": [
    {
      "name": "Spaghetti Bolognese",
      "description": "…",
      "servings": 4,
      "sourceUrl": null,                  // optional. Duplicate key on re-import (§4.4.2)
      "image": null,                      // optional. Path relative to the pack file
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

- **Ingredients are referenced by catalogue name.** They resolve by name, then alias, the same
  lookup `SeedCatalogueResolver` uses. The catalogue must be in the database first.
- **Steps reference ingredients by name, not index.** This avoids the positional-index fragility
  described in `Services/Seeding/CLAUDE.md`. A step name resolves through the same lookup, and then
  links every row in that recipe that resolved to the same ingredient. A step naming an ingredient
  the recipe doesn't list is an error. Step numbers come from array order.
- Units are canonical `MeasurementUnit` values. `Amount`/`Unit` follow the null-together rule. Zero
  is never used. `SourceAmount`/`SourceUnit` are left null. A pack states its amounts directly.
- The loader rejects an unknown `formatVersion` and accepts any `contentVersion`, the same split as
  the catalogue (§4.7).

#### 4.4.2 Import rules

- **Persistence goes through the real API path.** Each recipe maps to a `CreateRecipeRequest`, is
  checked by `CreateRecipeRequestValidator`, and is saved by `RecipeService.CreateAsync`. An
  imported recipe is therefore exactly what a user could have saved by hand.
- **All or nothing per pack.** The whole pack is resolved and validated before anything is
  written, and every error is reported, not just the first. The writes then happen in one
  transaction. Errors include an unresolved ingredient, a bad unit, a validator failure, a missing
  or disallowed image, and a `sourceUrl` repeated within the pack.
- **Unknown ingredients.** Rejected by default, listing every miss. With `--create-missing`, each
  miss becomes a new `Ingredient`: lowercase name, display name as written in the pack, category
  `OTHER`, no default unit. Users tidy them later in the ingredient screen. Creation happens inside
  the pack's transaction.
- **Duplicates.** A recipe with a `sourceUrl` that matches an existing recipe is skipped and
  reported. With `--force` the existing recipe is replaced. A recipe a meal plan uses is never
  replaced. It is reported as in use, the same guard `SeedRecipePersister` has, and that doesn't
  fail the pack. A recipe without a `sourceUrl` is always inserted, so re-importing such a pack
  duplicates it.
- **Images.** `image` is resolved relative to the pack file. The extension and size limits are
  checked up front against `ImageService`'s rules, and the file is copied with
  `ImageService.ImportAsync`. Files are copied before commit. If the transaction rolls back, the
  copied files are deleted, so a failed pack leaves no orphans.

#### 4.4.3 Entry points

| Entry point | Pack | `--create-missing` | `--force` | Ledger |
|---|---|---|---|---|
| First run (§4.2 step 4) | Built-in `sample-recipes.json` | Never | Never | `sample-recipes` |
| `dotnet run -- import-recipes <pack.json> [--create-missing] [--force]` | Any | Optional | Optional | Not used |

- **First run.** A failure aborts the step, logs every error, writes no ledger row and lets the app
  start. The step retries on the next start, after the artefact is fixed.
- **`import-recipes`.** Migrates, then runs a non-force `import-catalogue` before importing, as
  `--persist` does, so it works on an empty database. Exits 0 on success, including when recipes
  are skipped or in use, and 1 on any validation failure. Like `seed-recipes`, everything after the
  command token is withheld from host configuration in `Program.cs`. The existing global `--force`
  read for `import-catalogue` must not pick up this command's flag.
- **Paths and options.** The built-in pack's path lives in a `RecipePackOptions` section in
  `appsettings.json`, not inline, and resolves against `ContentRootPath` like the catalogue.

Working names: `RecipePackFile` (model), `RecipePackFileStore` (load), `RecipePackImporter`
(resolve, validate, write), `ImportRecipesCommand` (CLI).

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
config change. `RecipePackFileStore` follows the same pattern, with its path in `RecipePackOptions`
(§4.4.3). The sample pack has no images, so only the JSON file ships.

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

**Actions (D9).** Each flagged entry gets one of three actions:

| Action | Effect | Example |
|---|---|---|
| Keep | Unchanged | `acorn squash` |
| Rename | New `name`/`displayName`. The old name becomes an alias | `6 1/2-inch whole wheat pita pockets` → `whole wheat pita` |
| Fold | Entry removed. Its name and aliases become aliases of the surviving entry | `dry egg mix`, `egg mix` → aliases of `dried egg mix` |

There is no drop. `SeedCatalogueResolver` resolves every MyPlate name against the catalogue rows in
the database, and an unresolved name fails that recipe with `CatalogueMiss`. A dropped entry would
remove recipes from the personal library (D4). After a fold, the surviving entry's category and
default unit win.

**Process:**
1. Generate `seed-data/catalogue-review.md` (gitignored) from the current version-1 artefact, while
   it still carries `corpusRows`/`corpusRecipes`. List every flagged entry with its signal, its
   counts and a suggested action.
2. The project owner decides each one. No entry is renamed or folded on a suggestion alone.
3. Apply the decisions to `ingredient-catalogue.json` by hand. `IngredientCatalogueValidator` rules
   1–4 must still pass.
4. Reshape the file (D8) and raise its versions (below).

**Shipped file shape (D8).** The `source` block and per-entry `corpusRows`/`corpusRecipes` are
removed. `generatedAt` becomes `updatedAt`. Two version fields replace the single `version`:

```jsonc
{
  "formatVersion": 2,      // file shape. The loader rejects a format it can't read
  "contentVersion": 1,     // raised on every curated edit. Recorded in the ledger (D7)
  "updatedAt": "2026-10-…Z",
  "provenance": "curated",
  "entries": [ { "name": "…", "displayName": "…", "category": "…", "defaultUnit": "…", "aliases": [] } ]
}
```

Today one `version` field does both jobs: `IngredientCatalogueFileStore` rejects any value other
than `CurrentVersion`. A content edit must not make the loader refuse the file, so the two are
separated.

**Validator rules 5 and 6.**
- Rule 5 (every corpus name reachable) still matters, because of D9. It moves out of the builder
  into a test that runs only where the local MyPlate cache exists (`normalised/` is gitignored).
  On any other machine it reports itself as skipped, not passed.
- Rule 6 (corpus row counts add up) is deleted along with the counts.

**The builder no longer writes the shipped file.** `seed-recipes --build-catalogue` saves through
`IngredientCatalogueFileStore` today, so a re-run would overwrite the curated catalogue. Its output
moves to `seed-data/myplate/catalogue-build.json` (gitignored, in the version-1 shape with counts),
where it serves as a reference for later curation and never as the shipped artefact.

### 4.8 Retiring `DataSeeder`

`DataSeeder` and its `SampleIngredients` list are deleted. `DataSeederTests` is replaced by tests
for the new seeder (§6). The comment in `Program.cs` naming `DataSeeder` is updated.

### 4.9 Clean-start paths (D10)

Both paths start from an empty database. Locally, that means a scratch database:
`CREATE DATABASE recipeapp_clean;`, then point `ConnectionStrings__DefaultConnection` at it.

| Path | Steps | Result |
|---|---|---|
| A. Distributed | Start the app | Curated catalogue, densities, 12 sample recipes. Two `Applied` ledger rows |
| B. Personal | Path A, then `dotnet run -- seed-recipes --persist` | Path A plus the 1,123 MyPlate recipes and their photos |

Path B works because:
- `--persist` runs a non-force `import-catalogue` first. That is a no-op against a catalogue the
  first run has just loaded.
- D9 keeps every MyPlate name resolvable against the curated catalogue.
- Persist matches existing recipes on `SourceUrl`. The sample recipes have none, so the two sets
  never collide, and `--persist --force` never replaces a sample.

The order cannot be reversed. A database that receives MyPlate recipes before its first start
records `sample-recipes` as `SkippedExistingData` (§4.3), so the samples never appear. That is
correct for an upgraded install and is noted in `backend/CLAUDE.md` so nobody hits it by surprise
on a fresh one.

Both paths get an end-to-end check before the phase closes (§7 stage 6).

## 5. Licensing documentation corrections

These docs make public-domain claims that §1 shows are wrong. They are corrected in this phase so
the record doesn't mislead anyone later.

- `specs/phase-9-seed-recipe-library.md` lines 55, 156, 451 and 644: add a correction note pointing to
  this spec. Phase records are history, so the original text stays and the note is added next to it.
- `RecipeSeeding:SeedAttributionNote` (`"Source: USDA MyPlate Kitchen (public domain)"`): drop
  "(public domain)". This only affects personal databases built with `seed-recipes`.
- `.gitignore`: fix the comment claiming `manifest.json` and `normalised/` are committed.
- `Services/Seeding/CLAUDE.md` and `backend/CLAUDE.md`: state that the corpus is personal-use only
  and is never committed or published. Also record that the shipped catalogue is hand-curated,
  that `--build-catalogue` writes only to `catalogue-build.json` (§4.7), that curation folds and
  never drops (D9), and the clean-start paths and their order (§4.9).
- `backend/CLAUDE.md`: add `import-recipes` to the CLI command list, with a pointer to the pack
  format in §4.4.
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
- **§4.9 path B:** after a first run, persisting a small fixture corpus adds its recipes and leaves
  the 12 samples untouched.

Recipe-pack import (Testcontainers, fixture packs):

- A valid pack imports every recipe. Step links point at the right rows, including a step naming an
  ingredient by alias and a recipe listing the same ingredient twice.
- A pack with several faults writes nothing and reports **every** fault: an unknown ingredient, a
  bad unit, a validator failure, a missing image, and a `sourceUrl` repeated within the pack.
- Unknown ingredient: rejected without `--create-missing`. With it, created as `OTHER` with no
  default unit, and the recipe links to it.
- `sourceUrl` duplicates are skipped on re-import and replaced with `--force`. A recipe used by a
  meal plan is left in place and reported, and the rest of the pack still imports.
- A recipe without `sourceUrl` duplicates on re-import.
- Images are copied into `ImageStorage:BasePath` and set on the recipe. A pack that fails after
  copying leaves no copied files behind.
- `import-recipes` against an empty database migrates, loads the catalogue and imports. Its exit
  codes are 0 on success and 1 on a validation failure. Its `--force` doesn't trigger a catalogue
  force-import.
- An unknown `formatVersion` is rejected. Any `contentVersion` is accepted.

Artefact tests (no database):

- The shipped `sample-recipes.json` resolves fully against the shipped catalogue, and every recipe
  passes `CreateRecipeRequestValidator`.
- The §4.5 requirements: shared ingredients, mixed units, an unquantified row, and full food-aisle
  category coverage.
- Both artefacts are present in the build output directory.
- The shipped catalogue has no `source` block or corpus counts, and the loader rejects an unknown
  `formatVersion` but accepts any `contentVersion`.
- **Rule 5 (local only):** every name in the local `normalised/` corpus resolves against the
  shipped catalogue. Skipped, and reported as skipped, when the cache is absent.
- `--build-catalogue` writes to `catalogue-build.json` and leaves the shipped file untouched.

## 7. Implementation stages

1. **Catalogue review list.** Generate the list from the version-1 file. The owner decides each
   entry.
2. **Catalogue reshape.** Redirect `--build-catalogue` output first, so nothing can overwrite the
   curated file. Apply the decisions, move to the format-2 shape (§4.7), move rule 5 into its
   local-only test and delete rule 6.
3. **Ledger.** Entity, migration and constants.
4. **Recipe packs.** Model, `RecipePackFileStore`, `RecipePackImporter` and the `import-recipes`
   command (§4.4), tested against fixture packs.
5. **Sample pack and `FirstRunSeeder`.** Write the 12 recipes (§4.5). Replace the Development
   block, delete `DataSeeder`, add the csproj publish items.
6. **Tests** (§6), then run both §4.9 clean-start paths by hand against a scratch database.
7. **Docs** (§5) and a results section in this spec.

## 8. Out of scope

- Distributing any MyPlate recipe text, image or the 216 federal-origin recipes. Each "adapted
  from" credit would need checking one by one. That may be worth doing later; it isn't needed now.
- Shipping catalogue updates to existing installs automatically (D7).
- Ingredient deletion.
- Exporting MyPlate as a recipe pack (Q4 option b). `--persist` stays MyPlate's import path. This is
  a candidate for a later phase.
- Importing packs from the UI. `import-recipes` is CLI only.

## 9. Open questions

**Q1. Should Production auto-migrate on start?**
Today only Development migrates, so a Production database is never created or upgraded. "Seed on
first run for everyone" only works if the schema exists.
*Recommendation:* yes, migrate on start in every environment except `Testing`. This is a
single-instance self-hosted app, and there is no separate deploy step that could run migrations
instead.

*Answer (2026-10-04):* Yes. The worst case is rebuilding the database. The owner still wants the
MyPlate data for personal use and wants to run the whole process from a clean start. → D6, D10, §4.9.

**Q2. When a later release improves the catalogue, should existing installs receive it?**
D2 says each step runs once. Applied strictly to the catalogue, an install would keep its first
catalogue forever, and improvements would arrive only through a manual `import-catalogue`.
*Recommendation:* keep it strictly once for now (matching D2), with `import-catalogue` as the
documented upgrade path. `ArtefactVersion` is recorded in the ledger so an automatic additive
update (insert new entries only, never touch existing ones) can be added later without a schema
change.

*Answer (2026-10-04):* Recommendation accepted. → D7.

**Q3. Strip MyPlate provenance from the shipped catalogue?**
The `source` block and the per-entry `corpusRows`/`corpusRecipes` counts are facts and carry no
licensing risk. Once the catalogue is hand-curated, though, they no longer describe it accurately.
*Recommendation:* replace `source` with `"curated"` and a version, and drop the per-entry counts
from the shipped file. Keep them in the review list, where they help decisions.

*Answer (2026-10-04):* Recommendation accepted. → D8, §4.7. A follow-up found that curation could
not delete entries without breaking `--persist`. Answered as fold, never drop → D9.

**Q4. Should the §4.4 format become a general recipe-import format?**
The owner described "a process that could import any recipes if they are in the now established
format." Read that way, `sample-recipes.json` stops being a one-off. It becomes the first instance of
a **recipe pack**: a file any source can produce, imported through the same resolve → validate →
`RecipeService.CreateAsync` path. The ledger-guarded first-run step then runs only the built-in
pack.
- Answer: Yes, that is correct.

What that adds, beyond §4.4:
- A CLI command, e.g. `dotnet run -- import-recipes <path-to-pack.json>`, all or nothing per pack
  as in §4.4.
- **Ingredients outside the catalogue.** The sample pack must resolve fully. A third-party pack
  likely won't. *Recommendation:* reject by default and list the misses. A `--create-missing` flag
  creates them in category `OTHER` for later tidying.
  - answer: Agreed
- **Duplicates on re-import.** *Recommendation:* an optional `sourceUrl` per recipe. When present,
  an existing recipe with that `sourceUrl` is skipped (`--force` replaces it, with the meal-plan
  guard persist already has). When absent, re-importing duplicates the recipe.
  - answer: Agreed
- **Images.** *Recommendation:* an optional `image` path per recipe, relative to the pack file.
  - answer: Agreed.

*Answer (2026-10-05):* Yes to the reading and to all three sub-recommendations. Scope option (a).
→ D11, §4.4.

Scope choices: (Selected option: a)
- **(a) Format + `import-recipes` in Phase 10.** MyPlate keeps its own `--persist` path. Small
  increment, since §4.4 already designs the format. *Recommended.*
- **(b) As (a), plus exporting MyPlate to a pack** (`seed-recipes --export-pack`), so path B in
  §4.9 becomes `import-recipes myplate.json`. This unifies the two import paths, but description
  composition, photos and Stage 5's amount-dropping rule would all have to move into the export.
  Better as its own phase.
- **(c) Defer.** Ship §4.4 as a single-purpose file and design general import later.
