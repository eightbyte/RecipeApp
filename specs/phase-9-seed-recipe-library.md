# Phase 9 — Seed Recipe Library (USDA MyPlate)

**Version:** 1.0  
**Date:** 2026-09-06  
**Status:** Draft — awaiting review  
**Depends on:** Phase 8 (Local LLM) complete — requires a working `ILlmStructuredClient` and a seeded ingredient catalogue

---

## 1. Overview

A fresh RecipeApp install is empty. The user must scrape or hand-enter every recipe before meal planning or shopping lists become useful, which makes first-run evaluation of the app impossible without an hour of data entry.

Phase 9 ships a **one-time offline import** that populates the database with a public-domain North American recipe library sourced from **USDA MyPlate Kitchen** (~1,072 recipes). The import runs as a CLI command against the local LLM, exactly like the existing `seed-catalogue` command, and caches every network artefact on disk so the harvest is performed **once, ever**.

This is backend-only. No API surface, no frontend work, no schema migration.

### 1.1 Why this source

USDA MyPlate Kitchen is a US federal government work and therefore uncopyrighted under **17 USC §105** — free for commercial use with no licence agreement, no attribution requirement, and no redistribution restriction. Every alternative corpus of comparable size (Recipe1M+, RecipeNLG, RecipeDB, Recipe Box) is scraped from AllRecipes/Epicurious/Food Network and carries a non-commercial research licence, which is a contractual restriction binding regardless of the copyright status of the underlying ingredient lists.

The content is also a good fit on the merits: everyday North American home cooking — skillet dinners, casseroles, chili, baked chicken, sheet-pan vegetables — at realistic family serving counts.

### 1.2 Source availability constraint

`myplate.gov` was retired on **2026-01-07** and now returns `403`. `snaped.fns.usda.gov/recipes` returns `404`.

The third-party mirror at `myplate.food/api/v1` is live and keyless, but is **not a viable channel**: it is rate-limited to 100 full recipe fetches per day per IP, and its terms explicitly prohibit replicating the catalogue into a third-party database. Roughly eleven days of polling *and* a terms violation.

**The import therefore sources from the Internet Archive Wayback Machine**, which holds complete snapshots of the original `myplate.gov` recipe pages. The archived content is the USDA's own public-domain work, so extraction is unencumbered. A CDX query for `myplate.gov/recipes/*` returns 7,992 archived URLs, which reduce to ~1,000–1,100 distinct recipe slugs after filtering query-string variants and non-slug paths.

---

## 2. Deliverable

> `dotnet run -- seed-recipes --trial` imports 20 stratified recipes for quality review.  
> `dotnet run -- seed-recipes` imports the full library.  
> Both are resumable, idempotent, and re-run entirely from local cache after the first execution.

---

## 3. NuGet Packages

**None.** `AngleSharp` 1.4.0 is already referenced for Phase 3 HTML parsing and is sufficient. `Microsoft.Playwright` is *not* needed — archived Wayback pages are server-rendered static HTML with no client-side hydration.

---

## 4. Pipeline Architecture

Five stages, each independently re-runnable and each writing its output to the local cache before the next stage reads it. A failure in a late stage never forces a re-download.

```
Stage 1  DISCOVER   Wayback CDX API  ──►  slug manifest (JSON)
Stage 2  FETCH      Wayback content  ──►  raw HTML cache (one file per slug)
Stage 3  PARSE      AngleSharp       ──►  parsed JSON cache (deterministic, no LLM)
Stage 4  NORMALISE  Local LLM        ──►  normalised JSON cache (metric + ordered steps)
Stage 5  PERSIST    EF Core          ──►  PostgreSQL
```

The stage boundary that matters is **3 → 4**. Stage 3 is deterministic and cheap; stage 4 is GPU-bound and slow. Caching between them means the expensive LLM pass is never repeated for a recipe that already succeeded, and prompt tuning can be re-run against stage 3 output without touching the network.

### 4.1 Reuse of existing Phase 3/8 code

The import deliberately introduces **no new normalisation or persistence logic**. It builds the existing intermediate type and hands it to the existing pipeline:

| Existing member | Location | Role in Phase 9 |
|---|---|---|
| `ExtractedRecipe` / `ExtractedIngredient` / `ExtractedStep` | `RecipeScrapeService.cs:641` | Target shape produced by the new MyPlate parser |
| `NormaliseAsync(ExtractedRecipe, url, ct)` | `RecipeScrapeService.cs:436` | Metric conversion, two-pass catalogue matching, `ScrapePreviewResponse` |
| `ConvertUnit(...)` | `RecipeScrapeService` (private) | US customary → metric |
| `ConfirmAsync(ScrapeConfirmRequest, ct)` | `RecipeScrapeService.cs:194` | Writes `Recipe` + `RecipeIngredient` + `RecipeStep` + `RecipeStepIngredient` |

`NormaliseAsync` and the `Extracted*` records are currently `internal`. They stay `internal` — the new seeder lives in the same assembly. `InternalsVisibleTo` for the test project is already configured for Phase 8 tests.

**Consequence:** step→ingredient linkage (`IngredientIndexes`, which drives Cooking Mode) comes free, because `NormaliseAsync` already produces it.

---

## 5. Local Cache Design

Root: `backend/RecipeApp.API/data/seed/myplate/` — configurable via `RecipeSeeding:CacheDirectory`.

```
data/seed/myplate/
├── manifest.json            Stage 1 — discovered slugs + chosen Wayback timestamps
├── raw/<slug>.html          Stage 2 — archived HTML, verbatim
├── parsed/<slug>.json       Stage 3 — deterministic extraction
├── normalised/<slug>.json   Stage 4 — LLM output, metric + ordered steps
├── images/<slug>.jpg        Stage 2b — public-domain recipe photos
└── state.json               Per-slug stage progress + failure reasons
```

### 5.1 What is committed to git

| Path | Committed? | Rationale |
|---|---|---|
| `raw/` | **No** — gitignored | ~95 KB × 1,072 ≈ 100 MB of archival HTML noise |
| `images/` | **No** — gitignored | ~1,072 JPEGs; belongs in blob storage, not source control |
| `parsed/` | **No** — gitignored | Derivable from `raw/`; intermediate only |
| `manifest.json` | **Yes** | Pins exact Wayback timestamps → reproducible re-harvest |
| `normalised/` | **Yes** (recommended) | ~2–4 MB of public-domain JSON |

> **Committing `normalised/` is the key decision.** It means every other developer — and CI — can run `seed-recipes` with **no GPU, no network, and no LLM**, completing in seconds instead of hours. The data is public domain, so there is no licensing obstacle to vendoring it. The LLM is then only required for the *original* harvest and for re-normalisation after prompt changes.
>
> Flagged as **Open Question 1** (§18) — this is a reviewer call, not an implementation detail.

### 5.2 `state.json`

Tracks the furthest stage each slug has reached, so an interrupted run resumes rather than restarts:

```json
{
  "version": 1,
  "updatedAt": "2026-09-06T14:22:00Z",
  "slugs": {
    "20-minute-chicken-creole": { "stage": "normalised", "attempts": 1 },
    "3-can-chili":              { "stage": "parsed", "attempts": 2,
                                  "lastError": "LlmTimeout: exceeded 120s" },
    "apple-oatmeal-bars":       { "stage": "failed", "attempts": 3,
                                  "lastError": "ParseFailed: no ingredient block" }
  }
}
```

Stage values: `discovered` → `fetched` → `parsed` → `normalised` → `persisted`, plus terminal `failed`.

---

## 6. Configuration

New section in `appsettings.json`:

```json
"RecipeSeeding": {
  "CacheDirectory": "data/seed/myplate",
  "WaybackCdxUrl": "http://web.archive.org/cdx/search/cdx",
  "WaybackContentUrl": "http://web.archive.org/web",
  "SourceUrlPattern": "myplate.gov/recipes/*",
  "PreferredSnapshotYear": 2025,
  "FetchTimeoutSeconds": 60,
  "FetchDelayMilliseconds": 1500,
  "MaxFetchRetries": 3,
  "MaxLlmRetries": 2,
  "TrialSize": 20,
  "DownloadImages": true,
  "SeedAttributionNote": "Source: USDA MyPlate Kitchen (public domain)"
}
```

| Key | Description | Default |
|---|---|---|
| `CacheDirectory` | Root of the on-disk cache, relative to content root | `data/seed/myplate` |
| `WaybackCdxUrl` | CDX index endpoint for slug discovery | *(as above)* |
| `WaybackContentUrl` | Archived-content base URL | *(as above)* |
| `SourceUrlPattern` | CDX wildcard for recipe pages | `myplate.gov/recipes/*` |
| `PreferredSnapshotYear` | Snapshot year to prefer; falls back to nearest available | `2025` |
| `FetchTimeoutSeconds` | Per-page Wayback timeout — the archive is slow | `60` |
| `FetchDelayMilliseconds` | Politeness delay between archive requests | `1500` |
| `MaxFetchRetries` | Retries per page on 5xx/timeout, exponential backoff | `3` |
| `MaxLlmRetries` | Retries per recipe when LLM output fails validation | `2` |
| `TrialSize` | Recipes imported by `--trial` | `20` |
| `DownloadImages` | Whether to harvest recipe photos | `true` |
| `SeedAttributionNote` | Provenance text appended to `Recipe.Description` | *(as above)* |

Bound as `RecipeSeedingOptions` in `Services/` alongside the existing `RecipeScrapingOptions`.

Add to `.gitignore`:

```
backend/RecipeApp.API/data/seed/**/raw/
backend/RecipeApp.API/data/seed/**/parsed/
backend/RecipeApp.API/data/seed/**/images/
backend/RecipeApp.API/data/seed/**/state.json
```

---

## 7. New Files

```
backend/RecipeApp.API/
├── Services/Seeding/
│   ├── RecipeSeedingOptions.cs        Bound config
│   ├── SeedCacheStore.cs              Cache + state.json read/write
│   ├── WaybackHarvester.cs            Stages 1, 2, 2b — CDX discovery + fetch
│   ├── MyPlateRecipeParser.cs         Stage 3 — HTML → ExtractedRecipe
│   ├── RecipeLibrarySeeder.cs         Stages 4, 5 — orchestration
│   └── SeedModels.cs                  Manifest, state, cache DTOs
└── (Program.cs — register services + wire the CLI command)
```

Tests:

```
backend/RecipeApp.API.Tests/Seeding/
├── MyPlateRecipeParserTests.cs
├── SeedCacheStoreTests.cs
└── Fixtures/myplate/*.html            Committed sample pages
```

---

## 8. Stage 1 — Discovery

`WaybackHarvester.DiscoverAsync(ct)` queries the CDX API:

```
GET {WaybackCdxUrl}?url={SourceUrlPattern}&output=json
    &fl=original,timestamp,statuscode&collapse=urlkey&filter=statuscode:200
```

Filtering rules applied to the 7,992 raw results:

1. **Drop query strings** — `?page=1`, `?ajax_form=1`, `?utm_*`, `?hss_channel=*` are pagination and tracking duplicates of the same recipe.
2. **Drop numeric slugs** — `/recipes/111.57`, `/recipes/23.85` are calorie-filter artefacts, not recipes.
3. **Drop non-recipe paths** — `/recipes-cookbooks-and-menus`, `/recipes` (index pages), and malformed entries containing `%5Cr%5Cn` or `.While`.
4. **Normalise host** — collapse `myplate.gov` and `www.myplate.gov` to one canonical slug key.
5. **Select snapshot** — per slug, prefer the latest `200` snapshot within `PreferredSnapshotYear`; otherwise the latest available.

Output `manifest.json`:

```json
{
  "version": 1,
  "harvestedAt": "2026-09-06T14:00:00Z",
  "sourcePattern": "myplate.gov/recipes/*",
  "recipes": [
    { "slug": "20-minute-chicken-creole",
      "timestamp": "20251231013807",
      "originalUrl": "https://www.myplate.gov/recipes/20-minute-chicken-creole" }
  ]
}
```

Expected yield: **~1,000–1,100 slugs**. If the count falls below 800 the command aborts with a diagnostic — that indicates the CDX filter rules have drifted, not a genuinely small archive.

---

## 9. Stage 2 — Fetch & Cache

For each manifest entry not already in `raw/`:

```
GET {WaybackContentUrl}/{timestamp}/{originalUrl}
```

- Sequential, with `FetchDelayMilliseconds` between requests. **Do not parallelise** — the Wayback Machine aggressively rate-limits and will start returning `429`/`503`.
- On `429`/`5xx`/timeout: exponential backoff, up to `MaxFetchRetries`, then mark `failed` and continue.
- Write response bytes verbatim to `raw/<slug>.html`.
- Update `state.json` after each page so an interrupted run resumes cleanly.

Expected duration: ~1,000 pages × ~2.5 s ≈ **45 minutes**, once, ever.

### 9.1 Stage 2b — Images

The original image host (`myplate-prod.azureedge.us`) is dead, but Wayback archived the images. The JSON-LD `image.url` field on each page already carries a rewritten Wayback URL:

```
http://web.archive.org/web/{timestamp}/https://myplate-prod.azureedge.us/.../Chicken%20Creole.jpg
```

When `DownloadImages` is true, fetch that URL into `images/<slug>.jpg`. At persist time the file is copied into the app's configured `ImageStorage:BasePath` and `Recipe.ImageUrl` is set to `/uploads/images/<filename>`, matching how uploaded images are already served.

Images are public domain along with the rest of the USDA work. A failed image fetch is **non-fatal** — the recipe imports with `ImageUrl = null`.

---

## 10. Stage 3 — Parse

`MyPlateRecipeParser.Parse(string html, string slug, string originalUrl) → ExtractedRecipe`

Deterministic, no LLM. Verified against a live archived page during spec research.

### 10.1 Field extraction

| Field | Source | Selector / key |
|---|---|---|
| `Name` | JSON-LD | `$.@graph[?@type=Recipe].name` |
| `Description` | JSON-LD | `.description` (often truncated with `…` — see 10.3) |
| `Servings` | JSON-LD | `.recipeYield`, e.g. `"8 servings"` → `8` |
| Image URL | JSON-LD | `.image.url` |
| Ingredients | HTML | `.field--name-field-mp-ingredients` |
| Instructions | HTML | `.field--name-field-instructions` |
| Notes | HTML | `.field--name-field-notes` |
| Attribution | HTML | `.field--name-field-source` |

The JSON-LD block carries `name`, `description`, `recipeYield`, `cookTime`, `image` and full `nutrition` — but **not** `recipeIngredient` or `recipeInstructions`. Those two must come from the Drupal field markup, whose class names are stable across the archive.

### 10.2 Ingredient parsing

The ingredient block yields clean per-item strings with prep notes already parenthesised:

```
1 tablespoon vegetable oil (or cooking oil of choice)
1 can (14.5 ounces) no salt added diced tomatoes
2 medium celery stalks (chopped)
1/4 teaspoon cayenne pepper
```

The parser splits each `<li>` into a raw string and a parenthetical note, then emits a **provisional** `ExtractedIngredient` with `Amount = 0`, `Unit = ""` and the full text in `Name`. **It does not attempt regex quantity parsing.** Vulgar fractions (`1/4`), nested container quantities (`1 can (14.5 ounces)`), and descriptive counts (`2 medium celery stalks`) are exactly the cases regex handles badly and the LLM handles well. Stage 4 owns quantity extraction.

### 10.3 Known data hazards

Each of these was observed in the archived sample and must be handled:

1. **Truncated descriptions** — JSON-LD `description` is cut at ~150 chars with a trailing `…`. The full text is in `.mp-recipe-full__description`; prefer the HTML value and fall back to JSON-LD.
2. **Mojibake** — archived pages contain `U+FFFD` replacement characters (observed: `165 degrees F<?>(3-5 minutes)`), from non-breaking spaces mis-decoded during archival. Strip `U+FFFD` and normalise `&nbsp;` to a regular space before handing text to the LLM.
3. **Wayback URL rewriting** — the archive rewrites *all* absolute URLs, including `@context` (`"http://web.archive.org/web/…/https://schema.org"`) and `@id`. The parser must unwrap the `/web/{timestamp}/` prefix before treating any URL as canonical, and must not assume `@context` equals `https://schema.org`.
4. **Wayback toolbar injection** — the archive injects its own banner markup and scripts. Scope all selectors beneath the page's own content root; never take the first matching element document-wide.
5. **Notes bleed into instructions** — the instructions field frequently ends with a footnoted `*` aside (`* Store bought chili sauce can be high in sodium…`) that is a note, not a step. Split on the footnote marker and route the remainder to `Notes`.
6. **Adapted-source credits** — many recipes carry `Source: Adapted from: Food Hero, Oregon State University Cooperative Extension`. These are land-grant extension programs; the recipes remain USDA-published federal works. Capture the credit into `Recipe.Description` alongside `SeedAttributionNote` for provenance, and **do not discard it**.

### 10.4 Parse failure

A recipe missing a name, an ingredient block, or an instruction block is marked `failed` with reason `ParseFailed` and skipped. Failures are reported in the run summary, not thrown — one bad page must not abort a 1,000-recipe harvest.

---

## 11. Stage 4 — Normalise (LLM)

`RecipeLibrarySeeder` feeds each parsed recipe to a grammar-constrained LLM pass, then to the existing `NormaliseAsync`.

The MyPlate parse leaves two jobs that only the LLM can do well:

1. **Ingredient quantity extraction** — `"1 can (14.5 ounces) no salt added diced tomatoes"` → `{ name: "diced tomatoes", amount: 411, unit: "g", notes: "no salt added, canned" }`
2. **Step segmentation** — the single `directions` prose blob → an ordered `RecipeStep` list

Both are already expressed by `RecipeSchemaJson` at `RecipeScrapeService.cs:31`, which defines `ingredients` and an ordered `steps` array and is enforced via `JsonSchemaGrammar` GBNF. Phase 9 reuses that schema verbatim — a second, divergent schema would be a maintenance trap.

The prompt differs from the Phase 3 scrape prompt in one respect: input is **already-clean structured text**, not stripped page HTML, so the extraction instruction is narrower and `MaxHtmlCharacters` truncation does not apply.

Output is written to `normalised/<slug>.json`, then passed to `NormaliseAsync` for metric conversion and catalogue matching.

### 11.1 Unit conversion

`ConvertUnit` in `RecipeScrapeService` already handles the customary → metric mapping. Verify it covers the full range MyPlate uses, and extend if gaps are found:

| Customary | Metric | Note |
|---|---|---|
| cup | 240 ml | Liquid; dry ingredients are mass-dependent — the LLM should emit `g` for flour/sugar/rice |
| fluid ounce | 30 ml | |
| ounce (mass) | 28 g | |
| pound | 454 g | |
| tablespoon | `tbsp` | Kept as-is; already an allowed unit |
| teaspoon | `tsp` | Kept as-is |
| quart / pint | 950 ml / 470 ml | |
| "1 can (14.5 oz)" | 411 g | Container quantity resolves to net mass |
| "1 medium onion" | 1 `pcs` | Descriptive size → `pcs`, size retained in `Notes` |

> **Dry-cup ambiguity is the main quality risk.** 1 cup of flour is 120 g, of granulated sugar 200 g, of rice 185 g. Blindly mapping every cup to 240 ml produces recipes that are wrong in a way that is not obvious on inspection. The prompt must instruct the model to emit mass units for dry bulk ingredients. **This is the single most important thing to check in the trial run** (§14).

### 11.2 Throughput

Single-threaded through `LlamaModelHolder`'s `SemaphoreSlim` gate, ~10–30 s per recipe on a CUDA12 backend. Full library ≈ **4–9 hours** unattended. Because stage 4 output is cached per slug, an interrupted run resumes at the next unprocessed recipe.

### 11.3 Validation gate

LLM output is rejected and retried (up to `MaxLlmRetries`) when:

- Any ingredient has `Amount <= 0`
- Any unit is outside `g, kg, ml, L, pcs, tsp, tbsp`
- `Steps` is empty, or step numbers are not contiguous from 1
- Any `IngredientIndexes` entry is out of range
- Ingredient count differs from the parsed count by more than 2 (indicates hallucinated or dropped items)
- `Servings <= 0`

After the final retry the recipe is marked `failed` and excluded. **A wrong recipe is worse than a missing one** — an implausible quantity silently corrupts every shopping list it appears in.

---

## 12. Stage 5 — Persist

For each normalised recipe, map `ScrapePreviewResponse` → `ScrapeConfirmRequest` and call the existing `ConfirmAsync`. This writes `Recipe`, `RecipeIngredient`, `RecipeStep` and `RecipeStepIngredient` through the same code path a user-confirmed scrape uses — no parallel persistence logic.

Field mapping:

| Target | Value |
|---|---|
| `Recipe.SourceUrl` | Original `https://www.myplate.gov/recipes/<slug>` — **not** the Wayback URL |
| `Recipe.Description` | Parsed description + adapted-source credit + `SeedAttributionNote` |
| `Recipe.ImageUrl` | `/uploads/images/<filename>` if the image was harvested, else `null` |
| `Recipe.Servings` | From `recipeYield` |
| `Recipe.LastCookedAt` | `null` — seeded recipes are never pre-marked as cooked |

`SourceUrl` pointing at the canonical myplate.gov URL preserves provenance and doubles as the idempotency key (§13).

### 12.1 Ingredient catalogue growth

`NormaliseAsync` creates catalogue entries for unmatched ingredients. Importing ~1,000 recipes will add a substantial number of new `Ingredient` rows beyond the ~200 from `seed-catalogue`.

**Run `seed-catalogue` before `seed-recipes`.** A well-populated catalogue means more stage-1 exact matches, fewer LLM matching calls, and less near-duplicate drift (`"green pepper"` vs `"bell pepper, green"`). The command warns if the catalogue has fewer than 50 entries.

---

## 13. Idempotency & Re-runs

- **Natural key:** `Recipe.SourceUrl`. Before persisting, query for an existing recipe with that URL; skip if present.
- **No schema change.** Seeded recipes are identified by their `myplate.gov` `SourceUrl` prefix, which needs no new column.
- `--force` re-normalises and re-persists, deleting the prior row first (cascade removes children).
- `--refresh-cache` discards `raw/` and re-harvests from Wayback. Not needed in normal operation.
- Deleting `normalised/<slug>.json` and re-running re-does only the LLM pass for that recipe — the intended loop for prompt iteration.

---

## 14. Trial Run Protocol

`dotnet run -- seed-recipes --trial` imports `TrialSize` (20) recipes.

Selection is **stratified and deterministic**, not the first 20 alphabetically — the sample must exercise the hard cases. The 20 slugs are pinned in source so the trial is reproducible across machines, covering:

- **Baking** (dry-cup → grams: flour, sugar, oats) — the highest-risk conversion
- **Canned goods** (`1 can (14.5 ounces)` container quantities)
- **Whole produce** (`2 medium celery stalks` → `pcs`)
- **Long instructions** (≥ 8 steps, to test segmentation)
- **Very short instructions** (2–3 sentences, to test over-splitting)
- **Recipes with footnoted notes** (the `*` aside hazard, §10.3)
- **Adapted-source credits** (provenance capture)
- **Fractional quantities** (`1/4`, `1 1/2`)

### 14.1 Acceptance criteria

Reviewed by hand against the archived source pages before authorising the full run:

| # | Criterion |
|---|---|
| 1 | ≥ 18 of 20 import without validation failure |
| 2 | Every ingredient amount is plausible for the stated serving count |
| 3 | Dry ingredients use mass units, not `ml` — **explicitly verify flour, sugar, rice, oats** |
| 4 | Steps are correctly ordered and semantically complete vs. the source directions |
| 5 | No footnote text (`* Store bought chili sauce…`) leaked into a step |
| 6 | `IngredientIndexes` correctly identifies ingredients used per step (drives Cooking Mode) |
| 7 | No `U+FFFD` or `&nbsp;` artefacts in any persisted text |
| 8 | Catalogue matches are semantically right — no `"chili sauce"` → `"chili powder"` |
| 9 | `SourceUrl` is the myplate.gov URL, never a `web.archive.org` URL |
| 10 | Images render, and `ImageUrl` resolves under `/uploads/images/` |
| 11 | Shopping list generation from a plan of 3 seeded recipes consolidates units correctly |

Criteria 3, 5 and 8 are the ones expected to need prompt iteration. Criterion 11 is the genuine end-to-end check — it exercises Phase 5 consolidation against real imported data, which is where unit errors actually surface.

---

## 15. CLI Commands

Wired in `Program.cs` following the existing `seed-catalogue` pattern at `Program.cs:97` — detect the arg, build the host, run, exit without starting Kestrel.

```bash
dotnet run -- seed-recipes --discover        # Stage 1 only — build manifest, report count
dotnet run -- seed-recipes --harvest         # Stages 1-2 — download and cache, no LLM
dotnet run -- seed-recipes --trial           # Full pipeline, 20 stratified recipes
dotnet run -- seed-recipes                   # Full pipeline, entire library
dotnet run -- seed-recipes --limit 50        # Full pipeline, first 50 unprocessed
dotnet run -- seed-recipes --force           # Re-import recipes already in the database
dotnet run -- seed-recipes --refresh-cache   # Discard raw/ and re-harvest
dotnet run -- seed-recipes --report          # Print state.json summary, no work
```

Progress is logged per recipe (`[142/1072] apple-oatmeal-bars → normalised (14.2s)`), with a closing summary of persisted / skipped / failed counts and failure reasons grouped by category.

---

## 16. Error Handling

| Failure | Behaviour |
|---|---|
| CDX unreachable | Abort with diagnostic; no partial manifest written |
| Discovery yields < 800 slugs | Abort — filter rules have drifted |
| Page fetch `429`/`5xx` | Exponential backoff ×`MaxFetchRetries`, then mark `failed`, continue |
| Parse failure | Mark `failed` with reason, continue |
| LLM timeout / invalid output | Retry ×`MaxLlmRetries`, then mark `failed`, continue |
| Validation gate rejection | Retry, then exclude — never persist a suspect recipe |
| Recipe already exists | Skip (or replace under `--force`) |
| Image fetch failure | Non-fatal; import proceeds with `ImageUrl = null` |
| Empty ingredient catalogue | Warn, continue — every ingredient becomes a new entry |

The guiding rule: **one bad recipe never aborts the run**, and **no suspect recipe is ever persisted**.

---

## 17. Testing

Following the existing xunit.v3 + Testcontainers suite.

### 17.1 Unit — `MyPlateRecipeParserTests`

Against committed HTML fixtures in `Fixtures/myplate/`, each capturing a real archived page:

- Extracts name, description, servings, image from JSON-LD
- Extracts ingredient list and instruction blob from Drupal field markup
- Unwraps Wayback `/web/{timestamp}/` prefixes from `@context`, `@id` and `image.url`
- Ignores Wayback toolbar markup
- Strips `U+FFFD` and normalises `&nbsp;`
- Prefers full HTML description over truncated JSON-LD description
- Splits footnoted notes out of the instruction text
- Parses `"8 servings"` → `8`; handles `"Makes: 4 servings"` and missing yield (default 4)
- Captures adapted-source credit
- Throws/marks failed on a page with no ingredient block

### 17.2 Unit — `SeedCacheStoreTests`

- Round-trips manifest and state
- Resumes correctly from partial state
- Treats a corrupt `state.json` as empty rather than crashing
- Rejects slugs containing path separators (**path traversal guard** — slugs come from a remote index and are used as filenames)

### 17.3 Integration — `RecipeLibrarySeederTests`

Against a Testcontainers Postgres, with a **stubbed `ILlmStructuredClient`** returning canned structured output — no GPU in CI:

- Persists a recipe with correct ingredients, ordered steps, and step→ingredient links
- Is idempotent: running twice yields one recipe
- `--force` replaces rather than duplicating
- Validation gate rejects out-of-range units, zero amounts, non-contiguous steps
- Reuses existing catalogue entries instead of creating duplicates
- `SourceUrl` is the myplate.gov URL, not the Wayback URL

Network calls to Wayback are **not** exercised in CI — `WaybackHarvester` is behind an interface and stubbed.

---

## 18. Open Questions for Review

1. **Commit `normalised/` to the repo?** Recommended yes (§5.1) — makes seeding instant, GPU-free and deterministic for every other developer and for CI. ~2–4 MB of public-domain JSON. The alternative is that each developer runs a multi-hour local LLM pass.
2. **Harvest images?** ~1,072 JPEGs, gitignored, copied into `uploads/images/` at persist time. Adds ~30 min to the harvest and meaningful bulk to a dev machine. Default assumed **yes** (`DownloadImages: true`) — the recipe browser looks poor without images, and the Phase 4 grayscale rule is only visible with them.
3. **Import all ~1,072, or curate?** The full set includes narrow institutional items (bulk-quantity cafeteria recipes, single-ingredient preparations). A quality filter — minimum 3 ingredients, minimum 2 steps — would trim to perhaps 850 stronger recipes. Recommend **filter on**, thresholds confirmed after the trial.
4. **Should seeded recipes be distinguishable in the UI?** Currently they are identifiable only by `SourceUrl` prefix. If a "reset to starter library" feature is ever wanted, a `Recipe.IsSeeded` flag and migration would be needed. Recommend **deferring** — `SourceUrl` is sufficient for v1 and this is speculative.
5. **Trial size of 20?** Enough to catch systematic conversion errors, small enough to review by hand in ~30 minutes. Raise to 40 if the first trial shows scattered rather than systematic problems.

---

## 19. Out of Scope

- Any frontend work — no seeding UI, no admin screen
- Any API endpoint — CLI only
- Any EF migration — no schema change
- Nutrition data — MyPlate provides a full nutrition table, but `Recipe` has no nutrition fields and adding them is a separate phase
- Non-USDA sources (Wikibooks CC BY-SA, TheMealDB) — evaluated and rejected in §1.1
- Localised recipes — MyPlate has Spanish/French/Korean/German variants; English only for v1
- Automatic re-harvest or scheduled refresh — the source is a retired, frozen site

---

## 20. Definition of Done

- [ ] `seed-recipes --discover` reports ≥ 800 slugs and writes `manifest.json`
- [ ] `seed-recipes --harvest` caches raw HTML and images with resumable state
- [ ] `seed-recipes --trial` imports 20 stratified recipes
- [ ] All 11 trial acceptance criteria (§14.1) pass on manual review
- [ ] Full run completes with ≥ 95% of discovered recipes persisted
- [ ] Re-running `seed-recipes` is a no-op
- [ ] Unit and integration tests pass; CI needs no GPU and no network
- [ ] `CLAUDE.md` updated with the new files, config keys and CLI command
- [ ] `.gitignore` updated for cache directories
