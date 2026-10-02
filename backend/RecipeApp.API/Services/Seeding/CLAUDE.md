# Seeding — USDA MyPlate seed library (Phase 9)

This folder imports the archived MyPlate corpus (1,123 recipes) into the database. It is
**complete**: every recipe is harvested, parsed, normalised and persisted, and a re-run is a no-op.
Full design and results: `specs/phase-9-seed-recipe-library.md` (§21–25), plus
`specs/phase-9.1-*` (unquantified ingredients) and `specs/phase-9.3-*` (catalogue).

## Stages

| Stage | Class | Input → output | Needs |
|---|---|---|---|
| 1 Discover | `WaybackHarvester` | Wayback CDX → `manifest.json` | network |
| 2 Harvest | `WaybackHarvester` | pages + photos → cache | network |
| 3 Parse | `MyPlateRecipeParser` | HTML → `parsed/*.json` | nothing (deterministic) |
| 4 Normalise | `SeedRecipeNormaliser` + `SeedQuantityGate` | `parsed/` → `normalised/*.json` | LLM |
| 4.5 Catalogue | `IngredientCatalogueBuilder` | `normalised/` → `seed-data/ingredient-catalogue.json` | LLM (run once, reviewed, committed) |
| 5 Persist | `SeedRecipePersister` + `SeedCatalogueResolver` | `normalised/` → database | Postgres, no LLM |

`RecipeLibrarySeeder` walks the manifest for each stage. It isolates failures per slug and
resumes from the cache (`SeedCacheStore`, whose progress lives in `state.json`). Ctrl+C stops
cooperatively.

The cache is `seed-data/myplate`, not `data/`. Windows paths are case-insensitive, so `data/`
would merge into EF's `Data/`. Only `manifest.json` is committed.

## Commands (from `backend/RecipeApp.API`)

```bash
dotnet run -- seed-recipes --report            # progress; no work
dotnet run -- seed-recipes                     # every stage in order, each resuming
dotnet run -- seed-recipes --discover | --harvest [--limit N] | --refresh-cache
dotnet run -- seed-recipes --parse [--force]
dotnet run -- seed-recipes --normalise [--force] [--limit N] [--slug <slug>]...
dotnet run -- seed-recipes --persist [--trial] [--force]
dotnet run -- seed-recipes --build-catalogue [--batch N]...   # --batch previews, writes nothing
```

`--slug` re-runs one recipe (about 20 s) and also applies to `--parse`. `--force` on persist
re-imports, but it **never deletes a recipe a meal plan uses** (it reports `RecipeInUse`).
`Program.cs` withholds everything after `seed-recipes` from host configuration.

## Artefact invariants: never hand-edit `parsed/` or `normalised/`

Four invariants couple the two files, so a hand edit silently corrupts them. **Re-run the slug
instead.**
- `normalised/` rows pair to `parsed/` lines **by position**, and the counts must match exactly.
- `ingredientIndexes` are positional. Deleting a row re-points every index above it, and only an
  overrun past the end is detectable.
- `parsedFingerprint` is a SHA of the parsed file taken at normalise time.
- Stage 5 re-checks fingerprint, row count and index range **before** removing any row.

## Quantity rules (`SeedQuantityGate`)

- **"The source" is the whole published line** (`ParsedIngredientLine.FullText`). MyPlate puts an
  optional ingredient's amount in a sibling `span.notes`.
- The unquantified exemption is **earned from the source text, never granted by the model**. A
  null amount for a line that states a number is repaired or rejected, and an invented amount for
  a line that states none is rejected.
- Four source-derived repairs run in `BuildIngredients`: count read, missing sole number, misread
  sole number, and wrong sole unit. They apply only where the line settles the answer (exactly one
  number, with the unit immediately after it). **Lines stating several numbers are deliberately
  not judged** by Stage 4.
- Stage 5 drops an amount its multi-number line cannot explain. The amount must be neither a
  number on the line nor the product of two numbers on it. The row becomes unquantified and the
  line is appended to `Notes`. A percentage never explains an amount.
- **Do not widen a rule on one or two lines of evidence.** Giving the model a menu of candidate
  fractions made errors about three times worse, and a test guards against its return.
- The abort guard (`MaxConsecutiveLlmFailures`) counts only **systemic** failures (`IsSystemic`:
  nothing came back). A content rejection proves the pipeline works.

## Stage 5 specifics

- Group headings (`For the Dressing`) are skipped from the reviewed list in
  `SeedNonIngredientRows`, keyed on slug + verbatim line. No rule can find them. A list entry
  that matches nothing is logged as stale.
- `Recipe.Description` = description + page notes (minus MyPlate's `Learn more about` link list)
  + source credit + attribution. `Recipe` has no notes column.
- `ConfirmAsync` validates nothing, so the persister checks `RecipeLimits` itself.
- `DeferredLlmStructuredClient` stops persist from loading the model. `LlamaModelHolder` loads
  weights in its constructor.
- Photos are copied as `myplate-{slug}{ext}` into `ImageStorage:BasePath`.

## Catalogue (`ingredient-catalogue.json`)

- Stage 5 resolves names by dictionary lookup (`SeedCatalogueResolver`), and a name always
  outranks an alias. Validation: `IngredientCatalogueValidator`. There are no alias collisions,
  and every entry must be reachable.
- The builder asks **one object per name** (never one per group) and batches by family with
  plurals folded. `CatalogueMergeVocabulary` is a closed list of words a merge may differ by.
  **Adding a word is a review decision.** `ground`, `dried`, `white` and `cooked` are absent on
  purpose.
- After any full build, grep the log for `were still unanswered after`: a whole batch can fail by
  sampling. Review merges with the `merged:` / `kept apart:` log lines and hand-labelled
  `--batch` previews. A count is not a review.
- Small corrections (aliases, a category) can be hand-edited in the artefact and re-imported with
  `import-catalogue --force`.

## Harvest facts

- Images are fetched with the `im_` snapshot modifier and validated by magic bytes. The archive
  serves error pages with HTTP 200.
- The CDX query omits `collapse=urlkey`, which would defeat the preferred-year rule.
- HTTP 503 residue is an archive load window. Re-run `--harvest`.
