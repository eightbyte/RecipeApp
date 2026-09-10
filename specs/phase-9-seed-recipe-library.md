# Phase 9 — Seed Recipe Library (USDA MyPlate)

**Version:** 1.2  
**Date:** 2026-09-07  
**Status:** Stages 1–3 implemented. The corpus is harvested and fully parsed. Stages 4–5 not started.  
**Depends on:** Phase 8 (Local LLM) complete — requires a working `ILlmStructuredClient` and a seeded ingredient catalogue

> **Revision 1.2 (2026-09-07)** records what Stage 3 found in the cached corpus. §10.3's
> hazards were each observed on a single research page; all six have now been counted across all
> 1,089 pages, and two of them were wrong in a way that would have lost content — see §10.2a and
> §22. The corpus was also curated down from 1,123 to 1,089 by hand before Stage 3 ran.
>
> **Revision 1.1 (2026-09-07)** records what the live archive actually did. Sections carrying an
> **`✅ As built`** or **`⚠️ Revised`** note have been reconciled against the implementation;
> everything else is still the original plan. Three prescriptions were wrong in practice and are
> corrected in place, with the original text and the measurement that overturned it kept alongside
> — §5 (cache path), §8 (CDX collapse) and §9.1 (image URL form). §10.1 gains a template variant
> the research sample did not contain. See §21 for the harvest results.

---

## 1. Overview

A fresh RecipeApp install is empty. The user must scrape or hand-enter every recipe before meal planning or shopping lists become useful, which makes first-run evaluation of the app impossible without an hour of data entry.

Phase 9 ships a **one-time offline import** that populates the database with a public-domain North American recipe library sourced from **USDA MyPlate Kitchen** (~1,072 recipes — *the harvest found 1,123; see §21*). The import runs as a CLI command against the local LLM, exactly like the existing `seed-catalogue` command, and caches every network artefact on disk so the harvest is performed **once, ever**.

This is backend-only. No API surface and no frontend work. **It does depend on Phase 8.5.1's
migration** (`AddMeasurementDensityAndSource`), which must be applied before the first
`seed-recipes` run — the source-measurement columns have to exist before the first seeded recipe
is persisted, or the harvest's conversions become unauditable.

### 1.1 Why this source

USDA MyPlate Kitchen is a US federal government work and therefore uncopyrighted under **17 USC §105** — free for commercial use with no licence agreement, no attribution requirement, and no redistribution restriction. Every alternative corpus of comparable size (Recipe1M+, RecipeNLG, RecipeDB, Recipe Box) is scraped from AllRecipes/Epicurious/Food Network and carries a non-commercial research licence, which is a contractual restriction binding regardless of the copyright status of the underlying ingredient lists.

The content is also a good fit on the merits: everyday North American home cooking — skillet dinners, casseroles, chili, baked chicken, sheet-pan vegetables — at realistic family serving counts.

### 1.2 Source availability constraint

`myplate.gov` was retired on **2026-01-07** and now returns `403`. `snaped.fns.usda.gov/recipes` returns `404`.

The third-party mirror at `myplate.food/api/v1` is live and keyless, but is **not a viable channel**: it is rate-limited to 100 full recipe fetches per day per IP, and its terms explicitly prohibit replicating the catalogue into a third-party database. Roughly eleven days of polling *and* a terms violation.

**The import therefore sources from the Internet Archive Wayback Machine**, which holds complete snapshots of the original `myplate.gov` recipe pages. The archived content is the USDA's own public-domain work, so extraction is unencumbered. A CDX query for `myplate.gov/recipes/*` returns 7,992 archived URLs, which reduce to ~1,000–1,100 distinct recipe slugs after filtering query-string variants and non-slug paths.

> **Measured in 1.1:** the CDX query returns **49,256** rows uncollapsed (4,022 collapsed), not
> 7,992 — the research figure matches neither, so it appears to have come from a differently-scoped
> query. The reduction step holds up well: **1,123 distinct slugs**, just above the estimate. See §8.

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
| `MeasurementConverter.ToCanonical(...)` | `Services/MeasurementConverter.cs` (`public static`) | US customary → metric, plus unit alias canonicalisation (Stage A) |
| `MeasurementConverter.ResolveForImport(...)` | `Services/MeasurementConverter.cs` | Resolves `cup` against the matched ingredient's density (Stage B) |
| `ConfirmAsync(ScrapeConfirmRequest, ct)` | `RecipeScrapeService.cs:194` | Writes `Recipe` + `RecipeIngredient` + `RecipeStep` + `RecipeStepIngredient` |

`NormaliseAsync` and the `Extracted*` records are currently `internal`. They stay `internal` — the new seeder lives in the same assembly. `InternalsVisibleTo` for the test project is already configured for Phase 8 tests.

**Consequence:** step→ingredient linkage (`IngredientIndexes`, which drives Cooking Mode) comes free, because `NormaliseAsync` already produces it.

---

## 5. Local Cache Design

Root: `backend/RecipeApp.API/seed-data/myplate/` — configurable via `RecipeSeeding:CacheDirectory`.

```
seed-data/myplate/
├── manifest.json            Stage 1 — discovered slugs + chosen Wayback timestamps
├── raw/<slug>.html          Stage 2 — archived HTML, verbatim
├── parsed/<slug>.json       Stage 3 — deterministic extraction
├── normalised/<slug>.json   Stage 4 — LLM output, metric + ordered steps
├── images/<slug>.jpg        Stage 2b — public-domain recipe photos (also .png)
└── state.json               Per-slug stage progress + failure reasons
```

> **⚠️ Revised in 1.1 — the path was `data/seed/myplate/`.** Windows paths are case-insensitive,
> so `data/` resolves to the project's existing EF Core `Data/` folder and the harvest lands
> beside `AppDbContext.cs` and `Data/Migrations/`. This is not hypothetical: the first discovery
> run wrote `manifest.json` into `backend/RecipeApp.API/Data/seed/myplate/`, and 112 MB of
> archival HTML would have followed it there. `seed-data/` collides with nothing.
>
> A consequence worth noting: `.gitignore` patterns for this tree must match the real directory
> name. The rules are `backend/RecipeApp.API/seed-data/**/raw/` and siblings.

**Image extension.** `<slug>.jpg` was the assumption; the corpus is 968 JPEG and 147 PNG. The
cache stores whichever the payload's magic bytes say it is, and lookup globs `<slug>.*`.

### 5.1 What is committed to git

| Path | Committed? | Rationale |
|---|---|---|
| `raw/` | **No** — gitignored | Estimated ~100 MB; **measured 112 MB** across 1,123 pages |
| `images/` | **No** — gitignored | **Measured 101 MB**, 1,115 files; belongs in blob storage, not source control |
| `parsed/` | **No** — gitignored | Derivable from `raw/`; intermediate only |
| `manifest.json` | **Yes** | Pins exact Wayback timestamps → reproducible re-harvest. **188 KB as built** |
| `normalised/` | **Yes** (recommended) | ~2–4 MB of public-domain JSON |
| `state.json` | **No** — gitignored | Bookkeeping; rebuildable by scanning the cache directories |

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

**✅ As built.** Section in the committed `appsettings.json` — nothing in it is secret, so it does
not belong in the gitignored `appsettings.Development.json`.

```json
"RecipeSeeding": {
  "CacheDirectory": "seed-data/myplate",
  "WaybackCdxUrl": "http://web.archive.org/cdx/search/cdx",
  "WaybackContentUrl": "http://web.archive.org/web",
  "SourceUrlPattern": "myplate.gov/recipes/*",
  "RecipePathPrefix": "/recipes/",
  "PreferredSnapshotYear": 2025,
  "FetchTimeoutSeconds": 60,
  "DiscoveryTimeoutSeconds": 300,
  "FetchDelayMilliseconds": 1500,
  "MaxFetchRetries": 3,
  "RetryBackoffMilliseconds": 5000,
  "MaxLlmRetries": 2,
  "TrialSize": 20,
  "MinimumDiscoveredSlugs": 800,
  "DownloadImages": true,
  "ImageExtensions": [ ".jpg", ".jpeg", ".png", ".webp" ],
  "DefaultImageExtension": ".jpg",
  "SeedAttributionNote": "Source: USDA MyPlate Kitchen (public domain)",
  "UserAgent": "RecipeApp/1.0 (recipe library seeder)"
}
```

| Key | Description | Default |
|---|---|---|
| `CacheDirectory` | Root of the on-disk cache, relative to content root | `seed-data/myplate` |
| `WaybackCdxUrl` | CDX index endpoint for slug discovery | *(as above)* |
| `WaybackContentUrl` | Archived-content base URL | *(as above)* |
| `SourceUrlPattern` | CDX wildcard for recipe pages | `myplate.gov/recipes/*` |
| `RecipePathPrefix` | Path prefix a URL must carry to count as a recipe page (§8 rule 3) | `/recipes/` |
| `PreferredSnapshotYear` | Snapshot year to prefer; falls back to latest available | `2025` |
| `FetchTimeoutSeconds` | Per-page Wayback timeout — the archive is slow | `60` |
| `DiscoveryTimeoutSeconds` | Budget for the single large CDX query (§8) | `300` |
| `FetchDelayMilliseconds` | Politeness delay between archive requests | `1500` |
| `MaxFetchRetries` | Retries per page on 429/5xx/timeout, in addition to the first attempt | `3` |
| `RetryBackoffMilliseconds` | Base backoff, doubled per attempt; a `Retry-After` header wins | `5000` |
| `MaxLlmRetries` | Retries per recipe when LLM output fails validation | `2` |
| `TrialSize` | Recipes imported by `--trial` | `20` |
| `MinimumDiscoveredSlugs` | Discovery aborts below this yield (§8, §16) | `800` |
| `DownloadImages` | Whether to harvest recipe photos | `true` |
| `ImageExtensions` | Extensions a harvested photo may be stored under | `.jpg .jpeg .png .webp` |
| `DefaultImageExtension` | Fallback when the payload's format is unrecognised but declared an image | `.jpg` |
| `SeedAttributionNote` | Provenance text appended to `Recipe.Description` | *(as above)* |
| `UserAgent` | Sent to the Internet Archive | *(as above)* |

Six keys were added during implementation. `RecipePathPrefix`, `MinimumDiscoveredSlugs`,
`RetryBackoffMilliseconds`, `ImageExtensions` and `DefaultImageExtension` existed as hardcoded
constants in the first draft and were lifted to config to satisfy the repo's "avoid hard coded
values" rule; `MinimumDiscoveredSlugs` also lets the unit tests exercise discovery without
tripping the 800-slug abort. `DiscoveryTimeoutSeconds` is a genuine requirement — see §8.

`UserAgent` is deliberately descriptive rather than browser-impersonating, unlike the
`RecipeScraper` client's UA. The archive is a co-operative service whose rate limiter is built
around clients identifying themselves honestly.

Bound as `RecipeSeedingOptions` in `Services/Seeding/` alongside the existing `RecipeScrapingOptions`.

Add to `.gitignore`:

```
backend/RecipeApp.API/seed-data/**/raw/
backend/RecipeApp.API/seed-data/**/parsed/
backend/RecipeApp.API/seed-data/**/normalised/
backend/RecipeApp.API/seed-data/**/images/
backend/RecipeApp.API/seed-data/**/state.json
backend/RecipeApp.API/seed-data/**/*.tmp
```

`normalised/` is ignored **for now**, not on principle: §18 Q1's recommendation to commit it still
stands, but a prompt change rewrites all 1,089 files, so committing it before the prompt settles
would put a thousand-file churn in every review.

The `*.tmp` rule covers the temporary files the cache's atomic writes move into place.

---

## 7. New Files

```
backend/RecipeApp.API/
├── Services/Seeding/
│   ├── RecipeSeedingOptions.cs        ✅ Bound config
│   ├── SeedModels.cs                  ✅ Manifest, state, stage enum, results, exception
│   ├── SeedCacheStore.cs              ✅ Cache + state.json read/write
│   ├── IWaybackHarvester.cs           ✅ Added — the seam that keeps CI off the network
│   ├── WaybackHarvester.cs            ✅ Stages 1, 2, 2b — CDX discovery + fetch
│   ├── SeedRecipesCommand.cs          ✅ Added — CLI arg parsing + run modes
│   ├── MyPlateRecipeParser.cs        ✅ Stage 3 — HTML → ParsedSeedRecipe
│   ├── SeedParseModels.cs            ✅ Added — parsed recipe, template, failure, result
│   └── RecipeLibrarySeeder.cs        ◐ Stage 3 orchestration done; stages 4, 5 pending
└── (Program.cs — register services + wire the CLI command)  ✅
```

Two files were added beyond the original list. `IWaybackHarvester.cs` is the stubbing seam §17.3
already assumed existed. `SeedRecipesCommand.cs` holds the CLI's argument parsing and run modes,
which §15 had implied would sit inline in `Program.cs` following the `seed-catalogue` pattern —
seven flags and four run modes is more than that pattern carries comfortably, and `Program.cs`
keeps a five-line block that delegates.

One existing file changed: `Services/RecipeJsonLdExtractor.cs` gained a public
`TryFindRecipeNode(IDocument)`. Stage 2b needs `image.url` and Stage 3 will need the rest of the
node, and neither should re-implement the script-scanning-and-lenient-parsing loop. The existing
loop was extracted behind it with no behavioural change.

Tests (note: the project is `RecipeApp.Tests`, not `RecipeApp.API.Tests`):

```
backend/RecipeApp.Tests/
├── Seeding/
│   ├── SeedCacheStoreTests.cs         ✅ 29 tests
│   ├── WaybackHarvesterTests.cs       ✅ 50 tests
│   ├── MyPlateRecipeParserTests.cs    ✅ 32 tests
│   ├── RecipeLibrarySeederTests.cs    ✅ 23 tests — added; the Stage 3 runner
│   └── Fixtures/myplate/*.html        ✅ 4 committed pages, verbatim from the harvest
└── Infrastructure/
    ├── TestHostEnvironment.cs         ✅ Added — IHostEnvironment for the cache's content root
    └── StubHttpClientFactory.cs       ✅ Added — per-request scripted archive responses
```

`Fixtures/myplate/*.html` are drawn from the harvested corpus and include one page of each
template variant — see §10.1. They are committed whole, Wayback toolbar included, because a
trimmed excerpt would not catch a selector-scoping mistake.

---

## 8. Stage 1 — Discovery

`WaybackHarvester.DiscoverAsync(ct)` queries the CDX API:

```
GET {WaybackCdxUrl}?url={SourceUrlPattern}&output=json
    &fl=original,timestamp,statuscode&filter=statuscode:200
```

> **⚠️ Revised in 1.1 — `&collapse=urlkey` was in the original query and has been removed.**
>
> CDX `collapse` keeps the **first** capture of each key, not the most recent. That makes rule 5
> below — the whole preferred-year mechanism — inert for any slug with more than one capture,
> which is nearly all of them.
>
> Measured against the live archive on 2026-09-07:
>
> | | Collapsed | Uncollapsed |
> |---|---|---|
> | CDX rows returned | 4,022 | 49,256 |
> | Response size / time | ~0.4 MB, ~9 s | ~5 MB, ~35 s |
> | **Slugs discovered** | **1,123** | **1,123** |
> | Pinned to a 2025 snapshot | 37 | **1,121** |
> | Pinned to a 2024 snapshot | 1,086 | 2 |
>
> The slug set is *identical*. The only thing collapse changes is which capture each recipe is
> pinned to, and it pins 97% of them to a year-older snapshot. The 2025 captures are the last
> ones taken before the site was retired in January 2026 — the final published version of each
> page, which is what rule 5 was asking for in the first place.
>
> The cost is one 5 MB response, once, ever. `DiscoveryTimeoutSeconds` (300) exists because that
> single request does not fit the 60-second per-page budget.
>
> Note also that the raw-row figure in the original draft (7,992, quoted in §1.2) matches neither
> measurement, so it appears to have come from a differently-scoped query during research.

Filtering rules applied to the raw results:

1. **Drop query strings** — `?page=1`, `?ajax_form=1`, `?utm_*`, `?hss_channel=*` are pagination and tracking duplicates of the same recipe.
2. **Drop numeric slugs** — `/recipes/111.57`, `/recipes/23.85` are calorie-filter artefacts, not recipes.
3. **Drop non-recipe paths** — `/recipes-cookbooks-and-menus`, `/recipes` (index pages), and malformed entries containing `%5Cr%5Cn` or `.While`. Implemented as a positive test against `RecipePathPrefix`, which also excludes the localised variants (`/es/recipes/...`) §19 puts out of scope.
4. **Normalise host** — collapse `myplate.gov` and `www.myplate.gov` to one canonical slug key. Slugs are also lowercased, so `/recipes/Apple-Oatmeal-Bars` keys with `/recipes/apple-oatmeal-bars`.
5. **Select snapshot** — per slug, prefer the latest `200` snapshot within `PreferredSnapshotYear`; otherwise the latest available.

A sixth rule was needed in practice: **reject any slug that is not safe as a filename.** Slugs
come from a remote index and become paths in the cache, so they are validated against
`^[a-z0-9]+(?:[-_][a-z0-9]+)*$` before use. One row in the live corpus was dropped by this rule.

**Measured drop breakdown** (uncollapsed, 49,256 rows → 1,123 slugs):

| Reason | Rows |
|---|---|
| `nonRecipePath` | 23,714 |
| `queryString` | 6,764 |
| `unusableSlug` | 1 |
| `numericSlug` | 0 — the numeric artefacts carry a `.`, so the filename rule catches them first |

The breakdown is reported by `--discover` and is the diagnostic that distinguishes "the archive
changed" from "a filter rule is too greedy" if the count ever comes back short.

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

**✅ Actual yield: 1,123 slugs**, slightly above the estimate and comfortably above the abort
floor. All 1,123 resolved to a `www.myplate.gov` original URL; the manifest is 188 KB.

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

**Measured: a median of 1.9 s per page**, which vindicates the ~2.5 s estimate. Wall-clock duration
for the full run is *not* reported here, because the machine slept part-way through it and the
elapsed timings are contaminated (one page logs 6.3 hours). The p90 is 8.7 s and 39 pages exceeded
30 s, all of which is retry and backoff rather than transfer time.

**✅ As built**, with three details the draft did not specify:

- **A `Retry-After` header wins over the backoff schedule.** The archive knows its own load better
  than a doubling curve does.
- **Non-transient statuses are not retried.** A `404` will not become a `200`; only `408`, `429`
  and `5xx` are treated as worth another attempt. This is what keeps a genuinely-missing page from
  costing four requests.
- **The cached file is the authority, not `state.json`.** A page present in `raw/` is skipped and
  its state corrected upward if it lags. A state entry claiming `fetched` with no file on disk is
  re-fetched. This is what makes a hand-copied or partially-flushed cache self-heal, and it is why
  a corrupt `state.json` is merely discarded (§5.2) rather than being a crisis.

Cancellation is cooperative: `Ctrl+C` is trapped, the in-flight page finishes or aborts, and state
is flushed. Re-running resumes from the last cached page.

### 9.1 Stage 2b — Images

The original image host (`myplate-prod.azureedge.us`) is dead, but Wayback archived the images. The JSON-LD `image.url` field on each page already carries a rewritten Wayback URL:

```
http://web.archive.org/web/{timestamp}/https://myplate-prod.azureedge.us/.../Chicken%20Creole.jpg
```

When `DownloadImages` is true, fetch that URL into `images/<slug>.jpg`. At persist time the file is copied into the app's configured `ImageStorage:BasePath` and `Recipe.ImageUrl` is set to `/uploads/images/<filename>`, matching how uploaded images are already served.

Images are public domain along with the rest of the USDA work. A failed image fetch is **non-fatal** — the recipe imports with `ImageUrl = null`.

> **⚠️ Revised in 1.1 — fetching that URL as written does not return an image.**
>
> Two archive behaviours defeat the naive approach, and they compound:
>
> 1. **The URL must carry the `im_` snapshot modifier.** The JSON-LD URL has the plain
>    `/web/{timestamp}/` form. Requested as-is, the archive `302`s to its **HTML viewer** for that
>    image, and following the redirect yields a page, not a photo. The raw bytes require
>    `/web/{timestamp}im_/`. The harvester rewrites the modifier in place — matching
>    `/web/(\d{14})([a-z_]*)/` so an already-modified URL is normalised too — and only falls back
>    to composing a URL from scratch when the reference escaped rewriting entirely.
> 2. **The archive serves its interstitial and error pages with HTTP `200`.** A successful status
>    is therefore not evidence that the bytes are an image. Every payload is validated by magic
>    bytes (JPEG / PNG / WEBP / GIF), and the stored extension comes from the *signature*, not from
>    the remote path — a remote URL does not get to name a local file.
>
> This was caught because the first sample harvest produced three ~11 KB `.jpg` files that were
> all byte-identical Wayback markup. Without validation the failure is silent: the import "succeeds"
> and every recipe card renders a broken image. The corpus-wide run afterwards had **zero**
> validation rejections across 1,003 photos.

**✅ Measured**: 1,115 of 1,123 recipes have a cached photo — 968 JPEG and 147 PNG, 101 MB total,
typically 50–95 KB each. The 8 without one carry no `image` node in their JSON-LD at all; that was
verified against the markup, not inferred from a failure count.

---

## 10. Stage 3 — Parse

**✅ As built.** `MyPlateRecipeParser.Parse(string html, string slug, string originalUrl)`

Deterministic, no LLM, no database, no network. Runs over the whole corpus in about four seconds.

> **⚠️ Revised in 1.2 — the return type is `ParsedSeedRecipe`, not `ExtractedRecipe`.**
>
> `ExtractedRecipe` cannot hold notes, the source credit, the image URL or the published yield
> string, and §12 needs all four at persist time to compose `Recipe.Description` and
> `Recipe.ImageUrl`. Folding them together in Stage 3 would mean Stage 5 could not take them
> apart. `parsed/<slug>.json` therefore caches the richer, lossless shape, and the provisional
> `ExtractedRecipe` projection (§10.2) is Stage 4's to build when it assembles its prompt — it is
> a lossy view of this, not the artefact worth caching.
>
> **Result: 1,089 of 1,089 pages parsed, zero failures** — 8,601 ingredient lines, 6,639 steps,
> 1,024 primary and 65 legacy templates. Verified corpus-wide for the things a green run does not
> prove: no `U+FFFD`, no NBSP, no undecoded entities, no leaked markup, no `web.archive.org` URL
> in any persisted field, and every recipe carrying a name, ingredients, steps and a serving count.

### 10.1 Field extraction

| Field | Source | Selector / key |
|---|---|---|
| `Name` | JSON-LD | `$.@graph[?@type=Recipe].name` |
| `Description` | JSON-LD | `.description` (often truncated with `…` — see 10.3) |
| `Servings` | JSON-LD | `.recipeYield`, e.g. `"8 servings"` → `8` |
| Image URL | JSON-LD | `.image.url` |
| Ingredients | HTML | `.field--name-field-mp-ingredients`, **or** `.field--name-field-ingredients` — see below |
| Instructions | HTML | `.field--name-field-instructions` |
| Notes | HTML | `.field--name-field-notes` |
| Attribution | HTML | `.field--name-field-source` |

The JSON-LD block carries `name`, `description`, `recipeYield`, `cookTime`, `image` and full `nutrition` — but **not** `recipeIngredient` or `recipeInstructions`. Those two must come from the Drupal field markup.

> **⚠️ Revised in 1.1 — "whose class names are stable across the archive" is not true of the
> ingredient block.** The corpus uses **two page templates**. Every one of the 1,123 harvested
> pages has both an ingredient block and an instruction block, but the ingredient block carries
> one of two Drupal class names:
>
> | Class | Pages | Share |
> |---|---|---|
> | `field--name-field-mp-ingredients` | 1,058 | 94.2% |
> | `field--name-field-ingredients` | 65 | 5.8% |
>
> Selecting only the class this section originally named would mark **65 recipes `ParseFailed`**
> for no reason, which alone puts the §20 "≥ 95% of discovered recipes persisted" bar at risk
> before the LLM has been asked to do anything.
>
> The 65 legacy pages diverge in three other fields, all of which Stage 3 must accept:
>
> | Field class | Primary template | Legacy template |
> |---|---|---|
> | `field--name-field-mp-ingredients` | 100% | 0% |
> | `field--name-field-ingredients` | 0% | 100% |
> | `field--name-field-media-image` | 0% | 57% |
> | `field--name-field-recipe-image` | 0% | 100% |
> | `field--name-field-recipe-serving-size` | 0% | 88% |
> | `field--name-comment-body` | 1% | 89% |
>
> `field--name-field-instructions`, `field--name-field-notes` and `field--name-field-source` are
> common to both, so only the ingredient and image selectors need widening.
>
> The legacy pages also carry **no `recipeIngredient` in their JSON-LD** either, so there is no
> structured fallback — the markup is the only source. A representative example is
> `apple-tuna-sandwiches`.
>
> This was invisible during spec research because it was verified "against a live archived page"
> — singular. It is exactly the class of assumption that only survives contact with the whole
> corpus, which is now on disk and cheap to re-interrogate.

### 10.2 Ingredient parsing

The ingredient block yields clean per-item strings with prep notes already parenthesised:

```
1 tablespoon vegetable oil (or cooking oil of choice)
1 can (14.5 ounces) no salt added diced tomatoes
2 medium celery stalks (chopped)
1/4 teaspoon cayenne pepper
```

The parser splits each `<li>` into a raw string and a parenthetical note, then emits a **provisional** `ExtractedIngredient` with `Amount = 0`, `Unit = ""` and the full text in `Name`. **It does not attempt regex quantity parsing.** Vulgar fractions (`1/4`), nested container quantities (`1 can (14.5 ounces)`), and descriptive counts (`2 medium celery stalks`) are exactly the cases regex handles badly and the LLM handles well. Stage 4 owns quantity extraction.

### 10.2a Step structure — *added in 1.2*

The draft assumed §11 would receive "the single `directions` prose blob" and segment it with the
LLM. **It is not a blob.** All 1,089 pages render their directions as an `<ol>`, so the steps are
already segmented by the source and Stage 3 emits them directly. Two shapes complicate that, and
both lose content if ignored:

| Shape | Pages | Handling |
|---|---|---|
| One `<ol>`, nothing else | 1,059 | Each `<li>` is a step |
| More than one `<ol>`, split by a section label | 28 | **Every** list contributes; the label folds onto the step it introduces |
| Content after the last `</ol>` | 30 | Not a step — routed to `Notes` (§10.3 hazard 5) |
| A nested sub-list inside a step | 1 | Counted once, as part of its parent step |

**Taking only the first `<ol>` would silently truncate 28 recipes.** `frosted-cake` is the clear
case: eight steps for the cake, a `<p><strong>Icing:</strong></p>`, then three more. The label
becomes `"Icing: Cream together cream cheese and milk until smooth."` rather than a contentless
step of its own, which reads correctly in Cooking Mode.

**The nested sub-list is the subtle one.** `QuerySelectorAll("li")` descends into it, so
`black-bean-and-couscous-salad` produced its four preparation tasks twice — once inside step 2's
text and again as steps 3–6. Both list walks take direct `<li>` children only.

**Consequence for §11.** Stage 4 should *keep* this segmentation rather than re-derive it. The
model's remaining job is ingredient quantity extraction and `IngredientIndexes` linkage; asking it
to re-split steps it did not segment risks losing the source's own ordering for no gain.

### 10.2b Ingredient group headings — *added in 1.2*

80 pages render group headings as list items — `<li><b>For the Dressing:</b></li>`. These are
layout, not shopping, and feeding them to Stage 4 as ingredients produces junk rows.

The rule is deliberately narrow: **an `<li>` wholly wrapped in `<b>`/`<strong>` *and* ending in a
colon is dropped.** That is 86 items. The colon is load-bearing — 40 other bolded items are real
entries (`aluminum foil (10x12 inches square)`, `Note: "Minced" means cut up into tiny pieces.`)
and are kept. The trade is accepted knowingly: ~40 colon-less headings (`Dressing`, `Topping`)
survive as ingredient lines, which Stage 4 can drop on semantics. Widening the rule to catch them
would start dropping real ingredients, and a missing ingredient is worse than a junk one.

### 10.3 Known data hazards

Each of these was observed in the archived sample and must be handled:

1. **Truncated descriptions** — JSON-LD `description` is cut at ~150 chars with a trailing `…`. The full text is in `.mp-recipe-full__description`; prefer the HTML value and fall back to JSON-LD.

   > **✅ Confirmed in 1.2 — real, on 286 of 1,089 pages (26%).** The HTML field is present on
   > every page; on the other 797 the two agree exactly once entities are decoded, and it is never
   > the shorter of the two. Four pages have neither, and import with a null description.
2. **Mojibake** — archived pages contain `U+FFFD` replacement characters (observed: `165 degrees F<?>(3-5 minutes)`), from non-breaking spaces mis-decoded during archival. Strip `U+FFFD` and normalise `&nbsp;` to a regular space before handing text to the LLM.

   > **⚠️ Resolved in 1.2 — this hazard does not exist in this corpus, and no stripping logic
   > was written.** Measured across all 1,089 cached pages: **zero `U+FFFD`** and zero undecoded
   > HTML entities.
   >
   > The observation behind the hazard was a rendering artefact. The markup at that position is a
   > literal `&nbsp;` — `165 degrees F&nbsp;(3-5 minutes)` — which any HTML parser decodes to
   > U+00A0; whatever displayed it as `<?>` was not decoding entities. `&nbsp;` normalisation *is*
   > done, unconditionally, and it is the whole of what this hazard needed: 1,088 of 1,089 pages
   > contain at least one.
   >
   > Writing replacement-character stripping anyway would have been untestable against real input
   > and would have implied a decoding problem the cache does not have.
3. **Wayback URL rewriting** — the archive rewrites *all* absolute URLs, including `@context` (`"http://web.archive.org/web/…/https://schema.org"`) and `@id`. The parser must unwrap the `/web/{timestamp}/` prefix before treating any URL as canonical, and must not assume `@context` equals `https://schema.org`.
4. **Wayback toolbar injection** — the archive injects its own banner markup and scripts. Scope all selectors beneath the page's own content root; never take the first matching element document-wide.

   > **✅ Done in 1.2, though measurement says it was never load-bearing.** Everything is scoped
   > beneath `.mp-recipe-full`, which is present on all 1,089 pages. Checked corpus-wide: no
   > recipe field class occurs outside that root, and none occurs more than once on a page — so
   > the naive document-wide selector would in fact have worked. The scoping is kept because it
   > costs nothing and the guarantee is worth having explicitly rather than by luck.
5. **Notes bleed into instructions** — the instructions field frequently ends with a footnoted `*` aside (`* Store bought chili sauce can be high in sodium…`) that is a note, not a step. Split on the footnote marker and route the remainder to `Notes`.

   > **⚠️ Revised in 1.2 — real on 30 pages, but "split on the footnote marker" would catch only
   > 11 of them.** The trailing content takes three forms: `*`-prefixed asides (11), labelled
   > paragraphs such as `Storage:` / `Create-a-Flavor Changes:` / `Oven Instructions:` (11), and
   > bare prose (8).
   >
   > The rule used instead is structural and needs no marker: **anything after the last `</ol>` is
   > not a step.** It covers all 30, and it cannot misfire on a step that merely happens to contain
   > an asterisk — `"Add tomatoes with juice, chili sauce*, green pepper…"` stays a step, which a
   > marker-based split would have broken.
6. **Adapted-source credits** — many recipes carry `Source: Adapted from: Food Hero, Oregon State University Cooperative Extension`. These are land-grant extension programs; the recipes remain USDA-published federal works. Capture the credit into `Recipe.Description` alongside `SeedAttributionNote` for provenance, and **do not discard it**.

   > **✅ Confirmed in 1.2 — 1,081 of 1,089 pages carry one**, in a single consistent shape:
   > `<span class="field--name-field-source"><span>Source:</span><span class="field__item">…</span></span>`.
   > Selecting `.field__item` drops the `Source:` label structurally, so no string-stripping is
   > needed. Held on `ParsedSeedRecipe.SourceCredit` for Stage 5 to compose, not merged into the
   > description by Stage 3.

### 10.4 Parse failure

A recipe missing a name, an ingredient block, or an instruction block is marked `failed` with reason `ParseFailed` and skipped. Failures are reported in the run summary, not thrown — one bad page must not abort a 1,000-recipe harvest.

**✅ As built**, with the reason narrowed to a `SeedParseFailure` enum so the summary says *which*
part was missing rather than only that something was: `NoContentRoot`, `NoName`,
`NoIngredientBlock`, `NoIngredients`, `NoInstructionBlock`, `NoSteps`. The parser throws
`SeedParseException`; the stage runner catches it per slug, records `ParseFailed: <reason>` in
`state.json`, and continues. **Zero pages in the corpus reach any of these.**

---

## 11. Stage 4 — Normalise (LLM)

> **✅ As built (2026-09-08).** Implemented as `Services/Seeding/SeedRecipeNormaliser.cs` plus
> `RecipeLibrarySeeder.NormaliseAsync`, behind `seed-recipes --normalise`. Four things below turned
> out differently and are corrected in place, with the measurement that overturned each kept
> alongside: the model no longer writes the steps (§22.1 point 1, now enforced rather than merely
> noted), the ±2 ingredient-count tolerance is gone (§11.3), `NormaliseAsync` moved to Stage 5
> (below), and the extraction schema needed a fix of its own before any prompt could work
> (§23.1). See §23 for results.
>
> **`normalised/{slug}.json` holds the LLM pass only; `RecipeScrapeService.NormaliseAsync` is
> Stage 5's.** Catalogue matching resolves ingredient rows to `Ingredient.Id` values that are
> local to one database, so folding it into Stage 4 would make the cached artefact
> machine-specific and defeat §18 Q1's reason for committing it. Stage 4 is therefore the only
> stage that needs a GPU, and the only one that does not need Postgres.

`RecipeLibrarySeeder` feeds each parsed recipe to a grammar-constrained LLM pass, then to the existing `NormaliseAsync`.

The MyPlate parse leaves two jobs that only the LLM can do well:

1. **Ingredient quantity extraction** — `"1 can (14.5 ounces) no salt added diced tomatoes"` → `{ name: "diced tomatoes", amount: 14.5, unit: "ounces", notes: "no salt added, canned" }`. The model preserves the unit as stated; Stage A mechanically produces `411 g`. Asking the model to convert would contradict the extraction schema's own instruction (Phase 8.5.1 §1.2).
2. ~~**Step segmentation** — the single `directions` prose blob → an ordered `RecipeStep` list~~
   → **step→ingredient linkage only.** Every page publishes an ordered `<ol>` (§22.1 point 1), so
   Stage 3 already segmented the steps. Stage 4 keeps the model's `ingredient_indexes` and
   **overwrites every `instruction` with the parsed page's own wording**. The model is still asked
   for the text — quoting the step it is linking grounds the indexes, and the schema requires it
   — but its version never reaches the cache, so a paraphrase, a truncation or a leaked footnote
   cannot enter the library through Stage 4. The same applies to `name`, `description` and
   `servings`, all of which Stage 3 derives deterministically

Both are already expressed by `RecipeSchemaJson` at `RecipeScrapeService.cs:31`, which defines `ingredients` and an ordered `steps` array and is enforced via `JsonSchemaGrammar` GBNF. Phase 9 reuses that schema verbatim — a second, divergent schema would be a maintenance trap.

The prompt differs from the Phase 3 scrape prompt in one respect: input is **already-clean structured text**, not stripped page HTML, so the extraction instruction is narrower and `MaxHtmlCharacters` truncation does not apply.

Output is written to `normalised/<slug>.json`, then passed to `NormaliseAsync` for metric conversion and catalogue matching.

### 11.1 Unit conversion

`ConvertUnit` in `RecipeScrapeService` already handles the customary → metric mapping. Verify it covers the full range MyPlate uses, and extend if gaps are found:

| Customary | Metric | Note |
|---|---|---|
| cup | *resolved per Phase 8.5.1 §5.4* | Density known → `g` (`2 cups flour` → `240 g`); density unknown → kept as `cup`. Never a blind 240 ml. |
| fluid ounce | 30 ml | |
| ounce (mass) | 28 g | |
| pound | 454 g | |
| tablespoon | `tbsp` | Kept as-is; already an allowed unit |
| teaspoon | `tsp` | Kept as-is |
| quart / pint | 950 ml / 470 ml | |
| "1 can (14.5 oz)" | 411 g | Container quantity resolves to net mass |
| "1 medium onion" | 1 `pcs` | Descriptive size → `pcs`, size retained in `Notes` |

> **Dry-cup ambiguity is handled structurally, not by prompt.** Phase 8.5.1 moved the cup→gram
> relationship out of the generative step and onto the ingredient catalogue as
> `GramsPerMillilitre`, so the conversion is deterministic arithmetic on a curated constant —
> identical on every recipe, auditable after the fact via `SourceAmount`/`SourceUnit`, and
> correctable by editing one catalogue row rather than re-running the import. The prompt must
> **not** ask the model to emit mass units; it preserves the unit as stated.
>
> What remains to verify in the trial run is **coverage**: whether
> `Data/IngredientDensitySeeder.cs` carries a density for the bulk dry goods this corpus actually
> uses. Anything it misses is stored as `cup` — honest, but less useful. See §14.

### 11.2 Throughput

Single-threaded through `LlamaModelHolder`'s `SemaphoreSlim` gate, ~10–30 s per recipe on a CUDA12 backend. Full library ≈ **4–9 hours** unattended. Because stage 4 output is cached per slug, an interrupted run resumes at the next unprocessed recipe.

### 11.3 Validation gate

> **⚠️ Superseded in part by [Phase 9.1](phase-9.1-unquantified-ingredients.md), which is
> implemented (2026-09-08).** The `Amount <= 0` bullet below **must not be implemented as
> written** — measured on the parsed corpus it fails 313 of 1,089 recipes (28.7%) before the model
> makes a single mistake, because 432 ingredient lines state no quantity at all. Phase 9.1
> replaces that one bullet with a source-evidenced rule that is *stronger*, not weaker: an
> unquantified source line must produce a null amount, and a model that invents a quantity for one
> is rejected. Stage 4 consumes it as `SeedQuantityGate.Check` in
> `Services/Seeding/SeedQuantityGate.cs`, which returns the measurement already canonicalised.
> Every other bullet here stands unchanged.

LLM output is rejected and retried (up to `MaxLlmRetries`) when:

- Any ingredient's unit fails `MeasurementUnit.IsValid` **after** `TryCanonicalise` — so
  `teaspoon` and `cups` pass (they canonicalise to `tsp` and `cup`) while `clove` and `pinch`
  still fail. `SeedQuantityGate.Check` applies this, and it is the whole unit rule: the storable
  set is `MeasurementUnit.All` and is never restated as a literal list (CLAUDE.md)
- ~~Any ingredient has `Amount <= 0`~~ → **see Phase 9.1 §3.2**
- ~~Any unit is outside `g, kg, ml, L, pcs, tsp, tbsp`~~ → duplicated the bullet above with a
  stale list that predates `cup` becoming storable in Phase 8.5.1
- `Steps` is empty, or step numbers are not contiguous from 1
- Any `IngredientIndexes` entry is out of range
- ~~Ingredient count differs from the parsed count by more than 2~~ → **the count must match
  exactly.** The tolerance predates Phase 9.1. The gate now judges each row against *its own*
  source line, which means knowing which line each row came from; rows are paired to lines by
  position, and a ±2 tolerance makes that pairing a guess on exactly the recipes where the model
  has already shown it is confused. Exact pairing is the stricter reading, and it is what the
  prompt asks for: one object per numbered line, in order. A mismatch is retried, then excluded.
  Measured cost: the ~40 colon-less group headings of §22.1 point 3 are the recipes this loses,
  because dropping `For the Dressing` is the sensible thing to do and the structurally wrong one.
  The prompt therefore tells the model to keep heading lines as amount-less rows
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
- **No schema change of its own.** Seeded recipes are identified by their `myplate.gov` `SourceUrl` prefix, which needs no new column. Phase 8.5.1's migration is a prerequisite (§1).
- `--force` re-normalises and re-persists, deleting the prior row first (cascade removes children).
- `--refresh-cache` discards `raw/` and re-harvests from Wayback. Not needed in normal operation.
- Deleting `normalised/<slug>.json` and re-running re-does only the LLM pass for that recipe — the intended loop for prompt iteration.

---

## 14. Trial Run Protocol

`dotnet run -- seed-recipes --trial` imports `TrialSize` (20) recipes.

Selection is **stratified and deterministic**, not the first 20 alphabetically — the sample must exercise the hard cases. The 20 slugs are pinned in source so the trial is reproducible across machines, covering:

- **Baking** (dry cups: flour, sugar, oats) — verifies *density-table coverage*, not the model's
  arithmetic. Any cup that stays a cup names a catalogue entry needing a curated density.
- **Canned goods** (`1 can (14.5 ounces)` container quantities)
- **Whole produce** (`2 medium celery stalks` → `pcs`)
- **Long instructions** (≥ 8 steps, to test segmentation)
- **Very short instructions** (2–3 sentences, to test over-splitting)
- **Recipes with footnoted notes** (the `*` aside hazard, §10.3)
- **Adapted-source credits** (provenance capture)
- **Fractional quantities** (`1/4`, `1 1/2`)
- **Unquantified ingredients** (Phase 9.1 §9 Q4, answered yes): **at least 3** of the 20 slugs
  drawn from the 313 recipes carrying an unquantified line, and **at least 1** from the 6 carrying
  four or more. This stratum tests the half of the gate that has no other coverage — a model
  inventing a quantity for `salt` is rejected only here, and it is the failure mode most likely to
  pass silently, because an invented `1 tsp` looks exactly like a correct extraction.
- **A vulgar fraction with no ASCII digit** — `¼ cup sliced almonds (optional)` is the one corpus
  line where a naive digit test would classify a real quarter-cup as unquantified and instruct the
  model to discard it (Phase 9.1 §2)

### 14.0 Density-table review (run before the full harvest)

The trial's 20 recipes are the first real evidence of what the corpus actually measures in cups.
Before authorising the full run:

1. Query the trial's imported rows for anything still stored in cups:

   ```sql
   SELECT i."Name", COUNT(*) AS occurrences
   FROM "RecipeIngredients" ri
   JOIN "Ingredients" i ON i."Id" = ri."IngredientId"
   WHERE ri."Unit" = 'cup'
   GROUP BY i."Name"
   ORDER BY occurrences DESC;
   ```

2. For each row, decide deliberately: is this a **bulk dry good that should have a density**
   (add it to `Data/IngredientDensitySeeder.cs` with a citation), or is it genuinely
   **packing-dominated or liquid** (leave it — `cup` is the honest storage)?

3. Re-run `dotnet run -- seed-densities` (idempotent; it only fills nulls and never overwrites a
   hand-corrected value), then re-run the trial so the affected recipes reimport with the new
   densities.

This closes the loop the density table was curated blind: the table is validated against the
corpus rather than guessed at in advance (Phase 8.5.1 §14 Q5).

### 14.1 Acceptance criteria

Reviewed by hand against the archived source pages before authorising the full run:

| # | Criterion |
|---|---|
| 1 | ≥ 18 of 20 import without validation failure |
| 2 | Every ingredient amount is plausible for the stated serving count |
| 3 | The density table covers the corpus's bulk dry goods — **explicitly verify flour, sugar, rice, oats resolved to `g` rather than staying `cup`**. Spot-check with the Phase 8.5.1 §6.2 query; a miss is fixed by adding a row to `IngredientDensitySeeder` and re-running `seed-densities`, not by re-importing. |
| 4 | Steps are correctly ordered and semantically complete vs. the source directions |
| 5 | No footnote text (`* Store bought chili sauce…`) leaked into a step |
| 6 | `IngredientIndexes` correctly identifies ingredients used per step (drives Cooking Mode) |
| 7 | No `U+FFFD` or `&nbsp;` artefacts in any persisted text |
| 8 | Catalogue matches are semantically right — no `"chili sauce"` → `"chili powder"` |
| 9 | `SourceUrl` is the myplate.gov URL, never a `web.archive.org` URL |
| 10 | Images render, and `ImageUrl` resolves under `/uploads/images/` |
| 11 | Shopping list generation from a plan of 3 seeded recipes consolidates units correctly (now genuinely reachable — the tsp/tbsp dimension bug that would have failed this regardless was fixed in Phase 8.5.1 §3.3) |

Criteria 3, 5 and 8 are the ones expected to need prompt iteration. Criterion 11 is the genuine end-to-end check — it exercises Phase 5 consolidation against real imported data, which is where unit errors actually surface.

---

## 15. CLI Commands

Wired in `Program.cs` following the existing `seed-catalogue` pattern at `Program.cs:97` — detect the arg, build the host, run, exit without starting Kestrel.

```bash
dotnet run -- seed-recipes --discover        # ✅ Stage 1 only — build manifest, report count
dotnet run -- seed-recipes --harvest         # ✅ Stages 1-2 — download and cache, no LLM
dotnet run -- seed-recipes --harvest --limit 3   # ✅ Cap the pages fetched this run
dotnet run -- seed-recipes --refresh-cache   # ✅ Discard raw/ + images/ and re-harvest
dotnet run -- seed-recipes --report          # ✅ Print state.json summary, no work
dotnet run -- seed-recipes --parse           # ✅ Stage 3 — cached HTML → parsed/, offline
dotnet run -- seed-recipes --parse --force   # ✅ Discard parsed/ and re-derive
dotnet run -- seed-recipes --normalise       # ✅ Stage 4 — parsed/ → normalised/, LLM, no database
dotnet run -- seed-recipes --normalise --force        # ✅ Discard normalised/ and re-run the pass
dotnet run -- seed-recipes --normalise --slug apple-carrot-soup   # ✅ One recipe; repeatable
dotnet run -- seed-recipes --trial           # ⬜ Full pipeline, 20 stratified recipes
dotnet run -- seed-recipes                   # ⬜ Full pipeline, entire library
dotnet run -- seed-recipes --limit 50        # ⬜ Full pipeline, first 50 unprocessed
dotnet run -- seed-recipes --force           # ⬜ Re-import recipes already in the database
```

The unimplemented modes are parsed and rejected with an explicit "stage 5 is not implemented
yet" message and exit code `2`, rather than silently doing part of the job. Usage errors exit `1`;
the implemented modes exit `0`. A Stage 4 run that stops on `MaxConsecutiveLlmFailures` exits `1`:
everything it did is cached, but it did not finish and must not report that it did.

**`--slug <name>` was added beyond this list** (repeatable, and it narrows `--parse` too). §13
names deleting `normalised/<slug>.json` and re-running as the prompt-iteration loop, which without
a filter means walking the manifest to reach the recipe you care about. Stage 4's failures cluster
by ingredient-line shape rather than by manifest position, so iterating on one named recipe is a
twenty-second experiment instead of a ten-minute one — and every prompt fix in §23.1 was found
that way.

Progress is logged per recipe (`[142/1123] apple-oatmeal-bars → fetched (1.3s)`), with a closing
summary of fetched / skipped / failed counts and per-slug failure reasons.

**One `Program.cs` change the draft did not anticipate.** `WebApplication.CreateBuilder(args)`
feeds `args` to the command-line configuration provider, which would read this command's own flags
as configuration keys — `--limit 50` becomes `Limit=50`. Everything from the `seed-recipes` token
onward is therefore withheld from the host and handed to the command instead:

```csharp
var seedRecipesIndex = SeedRecipesCommand.IndexIn(args);
var builder = WebApplication.CreateBuilder(
    seedRecipesIndex >= 0 ? args[..seedRecipesIndex] : args);
```

`seed-recipes` also **returns before any database access** — stages 1–2 touch only the archive and
the local cache, so no migration runs and no connection string is needed. That is deliberate: the
harvest should not require Postgres to be up.

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

### 17.1 Unit — `MyPlateRecipeParserTests` ✅ (32 tests) + `RecipeLibrarySeederTests` ✅ (29 tests)

Against four committed fixtures in `Fixtures/myplate/`, copied verbatim out of the harvest —
Wayback toolbar and all, because a tidied excerpt would pass a scoping bug that a real page
catches. Each was chosen for a hazard: `20-minute-chicken-creole` (truncated description, `*`
aside, `&nbsp;`, prep notes, adapted credit), `apple-tuna-sandwiches` (legacy template),
`cuban-salad` (two step lists, bolded group headings), `black-bean-and-couscous-salad` (nested
sub-list). The `U+FFFD` case from the draft list below is asserted as an *absence*, per §10.3.

`RecipeLibrarySeederTests` covers the runner separately: resume, `--force`, `--limit`,
per-slug failure isolation, an unharvested manifest entry, an unsafe slug recorded rather than
thrown, and `ClearParsedContentAsync` rewinding only as far as the artefacts justify.

Original draft list, all covered:

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

### 17.2 Unit — `SeedCacheStoreTests` ✅ (29 tests)

- Round-trips manifest and state
- Resumes correctly from partial state
- Treats a corrupt `state.json` as empty rather than crashing
- Rejects slugs containing path separators (**path traversal guard** — slugs come from a remote index and are used as filenames)

Added beyond the draft: `RootPath` resolution for relative vs. absolute cache directories; stage
values serialise in the lower-case form §5.2 specifies; a failed slug reports `HasReached(...)`
as false despite `Failed` sorting last in the enum; atomic writes leave no `.tmp` behind; images
are found whatever extension they were stored under; and `--refresh-cache` drops pages and photos
and rewinds progress while **keeping** the manifest.

### 17.2a Unit — `WaybackHarvesterTests` ✅ (50 tests) — *added in 1.1*

Not in the original plan, which stubbed the harvester and never tested it. The §8 filter rules and
the §9 retry loop are where this phase's real complexity lives, and both are pure functions of a
scripted HTTP response.

- Every §8 filter rule, positive and negative, including the localised-variant and
  filename-safety rejections, and the drop reason each rule reports
- Snapshot preference: latest inside `PreferredSnapshotYear`, else latest overall — as a unit and
  end-to-end through `DiscoverAsync`
- **The CDX query carries no `collapse` parameter** — a regression guard on the §8 revision
- Discovery aborts below `MinimumDiscoveredSlugs`, and on an unreachable or empty index, without
  writing a partial manifest
- `EnsureManifestAsync` reuses a cached manifest without touching the network
- Fetch: requests the pinned snapshot URL, skips cached pages, corrects lagging state, honours
  `--limit`, retries transient failures, does **not** retry a `404`, and isolates a failed page
  from the rest of the run
- Images: requested through the `im_` modifier from either URL form, magic-byte detection,
  **an archive error page served as `200` is rejected**, failures are non-fatal, and photos are
  left alone when `DownloadImages` is false

Both files use `StubHttpClientFactory` (per-request scripted responses, recorded call log) and
`TestHostEnvironment`. Delays and backoff are configured to zero so the suite asserts on behaviour
rather than on waiting; the whole seeding namespace runs in about a second.

### 17.2b Unit — `SeedRecipeNormaliserTests` ✅ (55 tests) + `SeedRecipesCommandTests` ✅ (22 tests)
*added in 1.3*

Stage 4 against `StubLlmStructuredClient`, whose answer is scripted per call — CI has no GPU, so
nothing in the suite reaches real inference.

- Every §11.3 rejection: ingredient- and step-count mismatch, non-contiguous step numbers, an
  out-of-range `ingredient_indexes` entry, a nameless ingredient, a non-positive amount, an
  unstorable unit, and both halves of the Phase 9.1 gate (an invented quantity for an unquantified
  line, and a null for a line that states one)
- Units are judged **after** Stage A, so `teaspoons` → `tsp`, `ounces` → `g` and `cloves` fails
- A quantity stated only in the note span is accepted, and one invented where neither span states
  a number is not — the two halves of the `FullText` correction (§23.1)
- The parsed page's step wording, name, servings and description survive the model's versions
- `Servings <= 0` fails **without calling the model**, because no retry can change what the page said
- A rejected answer is retried and a corrected one accepted; retries stop at `MaxLlmRetries`;
  run cancellation unwinds rather than counting as a rejection
- The prompt's unit vocabulary is derived from `MeasurementUnit.All` and
  `MeasurementConverter.ConvertibleUnits` rather than restated — asserted by parsing it back out
  of the prompt, so the two lists cannot drift apart silently

`RecipeLibrarySeederTests` gained the Stage 4 runner: resume, `--force`, `--limit`, per-recipe
failure isolation, the retry counter, the consecutive-failure abort, and the fingerprint check that
re-normalises a recipe whose parsed input changed underneath it.

`SeedRecipesCommandTests` is new — the CLI had no coverage at all. It exercises argument parsing
(including `--slug`, which is rejected unless it passes the cache's own filename guard) and the
manifest narrowing `--slug` drives.

### 17.3 Integration — `RecipeLibrarySeederTests`

Against a Testcontainers Postgres, with a **stubbed `ILlmStructuredClient`** returning canned structured output — no GPU in CI:

- Persists a recipe with correct ingredients, ordered steps, and step→ingredient links
- Is idempotent: running twice yields one recipe
- `--force` replaces rather than duplicating
- Validation gate rejects out-of-range units, zero amounts, non-contiguous steps — with units
  checked **after** canonicalisation, so `teaspoon` passes and `clove` fails
- `SourceAmount`/`SourceUnit` are persisted verbatim on every seeded ingredient row
- `2 cups flour` resolves to `240 g` when the catalogue entry carries a density, and
  `2 cups spinach` stays `2 cup` when it does not
- Reuses existing catalogue entries instead of creating duplicates
- `SourceUrl` is the myplate.gov URL, not the Wayback URL

Network calls to Wayback are **not** exercised in CI — `WaybackHarvester` is behind an interface and stubbed.

---

## 18. Open Questions for Review

1. **Commit `normalised/` to the repo?** Recommended yes (§5.1) — makes seeding instant, GPU-free and deterministic for every other developer and for CI. ~2–4 MB of public-domain JSON. The alternative is that each developer runs a multi-hour local LLM pass.
2. ~~**Harvest images?**~~ **Resolved — yes, and done.** 1,115 photos, 101 MB, gitignored. The
   real figures: 968 JPEG and 147 PNG (so the cache cannot assume `.jpg`), and 8 recipes have no
   photo available at source. Still to do at persist time: copy into `ImageStorage:BasePath` and
   set `Recipe.ImageUrl`.
3. **Import all ~1,123, or curate?** *(count corrected from ~1,072)* The full set includes narrow institutional items (bulk-quantity cafeteria recipes, single-ingredient preparations). A quality filter — minimum 3 ingredients, minimum 2 steps — would trim to perhaps 850 stronger recipes. Recommend **filter on**, thresholds confirmed after the trial. Note the interaction with §20's "≥ 95% persisted" bar: a deliberate quality filter and a parse failure both reduce the persisted count, so the two need to be counted separately or the bar becomes unmeasurable.
4. **Should seeded recipes be distinguishable in the UI?** Currently they are identifiable only by `SourceUrl` prefix. If a "reset to starter library" feature is ever wanted, a `Recipe.IsSeeded` flag and migration would be needed. Recommend **deferring** — `SourceUrl` is sufficient for v1 and this is speculative.
5. **Trial size of 20?** Enough to catch systematic conversion errors, small enough to review by hand in ~30 minutes. Raise to 40 if the first trial shows scattered rather than systematic problems.

---

## 19. Out of Scope

- Any frontend work — no seeding UI, no admin screen
- Any API endpoint — CLI only
- Any EF migration of its own — Phase 8.5.1's `AddMeasurementDensityAndSource` is a
  **prerequisite**, not part of this phase
- Nutrition data — MyPlate provides a full nutrition table, but `Recipe` has no nutrition fields and adding them is a separate phase
- Non-USDA sources (Wikibooks CC BY-SA, TheMealDB) — evaluated and rejected in §1.1
- Localised recipes — MyPlate has Spanish/French/Korean/German variants; English only for v1
- Automatic re-harvest or scheduled refresh — the source is a retired, frozen site

---

## 20. Definition of Done

- [x] `seed-recipes --discover` reports ≥ 800 slugs and writes `manifest.json` — **1,123**
- [x] `seed-recipes --harvest` caches raw HTML and images with resumable state — **1,123/1,123 pages, 1,115 photos**
- [x] `seed-recipes --parse` turns every cached page into a recipe — **1,089/1,089, zero failures**
- [x] `seed-recipes --normalise` turns parsed recipes into gate-approved LLM output — see §23
- [ ] `seed-recipes --trial` imports 20 stratified recipes
- [ ] All 11 trial acceptance criteria (§14.1) pass on manual review
- [ ] Full run completes with ≥ 95% of discovered recipes persisted — *Stage 4 clears the bar on
      the 30-recipe benchmark at 96.8% (§23); the full-corpus pass has not been run*
- [ ] Re-running `seed-recipes` is a no-op
- [x] Unit tests pass; CI needs no GPU and no network — **297 seeding tests, 917 suite-wide**
- [ ] Integration tests (`RecipeLibrarySeederTests`) — blocked on stages 4–5
- [x] `CLAUDE.md` updated with the new files, config keys and CLI command
- [x] `.gitignore` updated for cache directories

---

## 21. Harvest Results (2026-09-07)

The one-time harvest is complete. Stages 3–5 can now be developed entirely offline against the
cached corpus — no network, no rate limit, no risk of the archive changing underneath.

| | Result |
|---|---|
| Slugs discovered | 1,123 (floor 800; estimate was 1,000–1,100) |
| Pages cached | **1,123 / 1,123** — 112 MB |
| Photos cached | 1,115 — 101 MB (968 JPEG, 147 PNG) |
| Recipes with no photo at source | 8 |
| `state.json` | uniformly `fetched` |

**It took two passes, and that is the interesting part.** The first run left 25 pages and 81 photos
on `HTTP 503 after 4 attempt(s)`. Those failures were scattered across manifest positions 471–683
rather than clustered on particular slugs — an archive load window part-way through the run, not
missing content. There were 653 retry warnings during the run, so the backoff was working hard
throughout.

Recovery required nothing special: re-running `--harvest` skipped the 1,098 cached pages and
re-requested only the 131 missing artefacts — 25 pages plus 106 photos, the latter being the 81
that had failed outright plus the 25 belonging to pages that never got as far as an image request.
All 131 succeeded. The resume path therefore got exercised for real,
unrehearsed, on genuine mid-run failure — better evidence than a test for it.

**Practical consequence for §16.** "Page fetch 429/5xx → mark failed, continue" is right, but the
failure is usually about *when* the request was made rather than *what* was requested. The
operational advice is simply to re-run `--harvest` once after any large run and check the summary
reports zero failures, as it now does.

### 21.1 What the corpus changed about the plan

| § | Draft said | Corpus says |
|---|---|---|
| 5 | Cache at `data/seed/myplate` | Collides with EF Core `Data/` on Windows → `seed-data/myplate` |
| 5 | `images/<slug>.jpg` | 147 of 1,115 are PNG |
| 8 | `collapse=urlkey` | Defeats the preferred-year rule; 1,086 recipes pinned a year early |
| 8 | 7,992 CDX rows | 49,256 uncollapsed / 4,022 collapsed |
| 9.1 | Fetch the JSON-LD `image.url` | Needs the `im_` modifier, and `200` ≠ image |
| 10.1 | Ingredient class names "stable across the archive" | Two templates: 1,058 / 65 |
| 18 Q3 | ~1,072 recipes | 1,123 |

### 21.2 Open items for Stage 3 — **all closed in 1.2**

1. ~~**Handle both templates** (§10.1).~~ **Done** — 1,024 primary and 65 legacy parse through one
   code path; the template is recorded per recipe and reported per run.
2. ~~**Confirm the `U+FFFD` hazard.**~~ **Done, and it does not exist** — zero across all 1,089
   pages, so no stripping logic was written. See §10.3 hazard 2.
3. ~~**Draw fixtures from the real corpus.**~~ **Done** — four pages, including
   `apple-tuna-sandwiches` for the legacy template. See §17.1.
4. ~~**Re-check hazards 1, 5 and 6.**~~ **Done, and hazard 5 was wrong** — the footnote marker it
   prescribed covers only 11 of the 30 affected pages. See §10.3.

Two things the corpus revealed that were not on this list at all, both of which silently lose
content: **multi-list instructions** (28 pages) and **nested sub-lists inside a step** (1 page).
See §10.2a — the second was caught only by reading the parsed output, not by the run succeeding.

---

## 22. Stage 3 Results (2026-09-07)

The corpus is fully parsed. Stage 4 can be developed against `parsed/` with no HTML in sight.

| | Result |
|---|---|
| Pages in the manifest | 1,089 *(curated down from 1,123 by hand before Stage 3)* |
| **Parsed** | **1,089 / 1,089 — zero failures**, ~4 s |
| Templates | 1,024 primary, 65 legacy |
| Ingredient lines | 8,601 (86 group headings dropped) |
| Steps | 6,639 |
| Recipes with a description | 1,085 (286 recovered from the HTML field, not JSON-LD) |
| Recipes with a source credit | 1,081 |
| Recipes with notes | 1,015 |
| Recipes with an image URL | 1,081 |

**A green run is not the evidence that matters.** The parser not throwing says only that four
selectors matched. The output was checked corpus-wide for the failures that pass silently: zero
`U+FFFD`, zero `U+00A0`, zero undecoded entities, zero leaked markup, zero `web.archive.org` URLs
in any field, every `SourceUrl` canonical, and no empty or duplicated step anywhere. That last
check is what caught the nested sub-list bug — the run had reported 1,089 successes with it in
place.

### 22.1 What Stage 4 inherits

1. **Steps are already segmented and should stay that way.** §11 assumed a prose blob needing LLM
   segmentation; the source provides an ordered list on every page (§10.2a). The model's remaining
   jobs are ingredient quantity extraction and `IngredientIndexes` linkage.

2. **§11.3's `Amount <= 0` rule cannot be applied as written.** Measured on the parsed output:
   **432 of 8,601 ingredient lines (5.0%) contain no quantity at all** — `salt`, `pepper`,
   `nonstick cooking spray`, `salt and pepper, to taste` — and they are spread across **313 of the
   1,089 recipes (28.7%)**, not concentrated in a few.

   > Rejecting any recipe with a zero-amount ingredient therefore fails 28.7% of the corpus before
   > the model has made a single mistake, which alone puts the §20 "≥ 95% persisted" bar out of
   > reach. This is a real property of home-cooking recipes, not a parse defect: "salt to taste"
   > has no amount because there isn't one.
   >
   > Stage 4 needs a deliberate policy — the likely shape being a `to taste` convention that
   > stores the ingredient with a null or zero amount and exempts it from the gate, keeping the
   > gate's teeth for an ingredient that *states* a quantity the model then got wrong. That is the
   > case the gate was written for, and it stays fully armed.

   **Resolved by [Phase 9.1](phase-9.1-unquantified-ingredients.md) (2026-09-07).** The diagnosis
   held; the proposed remedy needed three corrections. It is not a `to taste` convention — only 40
   of the 432 lines say "to taste", and 49% are ordinary foods (`raisins`, `lemon zest`), so the
   predicate is *the source states no quantity*. Zero and null are not interchangeable: `0` renders
   as `0 g Salt` and sums into shopping lists, so the storage is `Amount = null, Unit = null`,
   which needs a migration. And the exemption cannot start at the gate — `RecipeSchemaJson` makes
   `amount` a required number, so the grammar *forces* the model to invent one. See Phase 9.1.

3. **~40 colon-less group headings survive as ingredient lines** (`Dressing`, `Topping`,
   `For the Dressing`). Deliberate — see §10.2b. Stage 4 can drop them on semantics, where the
   information to do so safely actually exists.

4. **Notes are held separately from the description.** `ParsedSeedRecipe` carries `Notes` and
   `SourceCredit` as distinct fields so §12 can compose `Recipe.Description` from the description,
   the credit and `SeedAttributionNote`. Stage 3 deliberately does not merge them.

---

## 23. Stage 4 Results (2026-09-09)

**30 of 31 recipes normalised — 96.8%**, against §20's ≥ 95% bar. The full-corpus pass has **not**
been run; `normalised/` holds the 30 recipes of the benchmark.

### 23.1 The benchmark

`seed-recipes --normalise --force --limit 30`. `--limit` counts *successes*, so the run continues
until 30 recipes are cached and **the attempt count is the denominator**. That makes the headline
number stable across runs while the failure taxonomy carries the signal — a better prompt reaches
30 in fewer attempts. Every run below walks the same manifest prefix, so the recipe sets overlap
almost completely.

| Run | Change under test | Normalised / attempted | Rate |
|---|---|---|---|
| 6 | Count-word rename broadened (from the previous session) | 30 / 45 | 66.7% |
| 7 | + grammar number rule, index range stated, temperature 0.2 | 30 / 44 | 68.2% |
| 8 | + fraction conversion table, `divided` rule | 30 / 45 | 66.7% |
| 9 | Bracket labels; fraction table, `divided` rule and cold sampling **reverted** | 30 / 35 | 85.7% |
| 10 | Same build, **Qwen3.5 9B Q6** in place of Qwen2.5 7B Q6 | 30 / 31 | **96.8%** |

Failure taxonomy across the same runs:

| Failure | Run 6 | Run 7 | Run 8 | Run 9 | Run 10 |
|---|---|---|---|---|---|
| `QuantityRejected` | 6 | 9 | 14 | 4 | 1 |
| `IngredientIndexOutOfRange` | 5 | 4 | 0 | 0 | 0 |
| `InvalidLlmOutput` | 3 | 0 | 1 | 1 | 0 |
| `IngredientCountMismatch` | 1 | 0 | 0 | 0 | 0 |
| `StepCountMismatch` | 0 | 1 | 0 | 0 | 0 |

### 23.2 The model dominated every prompt change

Runs 9 and 10 differ only in `Llm:Local:ModelPath`. Swapping Qwen2.5 7B Q6 for **Qwen3.5 9B Q6**
took 85.7% to 96.8% and *reduced* wall time from 15.5 s to 9.2 s per recipe, because it stops
needing retries — 12 recipes needed a second attempt under the old model, 1 under the new one. A
full corpus pass projects to **under 3 hours**.

> Both are ChatML, so nothing but the weights changed. Note the quantisation: the 9B is Q6, and a
> larger model at Q4 is not obviously better for a task whose whole content is reproducing an exact
> digit. Two other local models were considered and rejected — Gemma 4 12B (hybrid reasoning model,
> template does not match `LlamaModelHolder`'s Gemma branch, 9.1 GiB leaves little room for context)
> and the 12B Mistral-Nemo finetunes (roleplay models).

**Four measured runs of prompt work moved the rate by 19 points; one config line moved it by 11.**
When Stage 4 quality regresses, check the model file before rewriting the prompt.

### 23.3 What worked, and what backfired

**Worked — label the ingredient lines with the index you want back.** The user message numbered
ingredient lines from 1 while the prompt asked for 0-based `ingredient_indexes`, so the only 0-based
number in the exchange was one the model had to derive. It did not. A captured answer mixed both
bases in one response: `[0]` for the first ingredient, `[8]` and `[9]` for the eighth and ninth of
nine. **Only the overrun past the end was ever detectable**, which means an unknown share of
*accepted* linkage in earlier runs was silently off by one. Restating the valid range in words
changed nothing, because the conflict was between two things the model could both read. Lines now
carry `[0]`, `[1]`, `[2]` labels — brackets so they cannot be confused with step numbers, which
still count from 1 because `step_number` does. `IngredientIndexOutOfRange` went from 5 to 0.

**Backfired — a table of fraction conversions.** 37.3% of ingredient lines state a fraction and
every surviving quantity error was a fraction read as an adjacent value, so the prompt was handed
the ten conversions it needed, computed through `SeedQuantityGate`'s own parser so prompt and gate
could not disagree. Quantity rejections went from 9 in 44 to **14 in 45**. The model stopped reading
the line and started choosing from the list: `1/8` came back as `1`, `1/2` as `0.125`, and
`1 teaspoon salt` — which contains no fraction at all — came back as `0.25`.

> **A list of plausible answers is a list of things to guess from.** Removed, with a comment at the
> point of temptation and a test that fails if a run of decimals reappears outside the worked
> example.

**Backfired — lowering the sampling temperature.** Decoding at 0.2 rather than llama.cpp's 0.75
looks obviously right for constrained extraction and measured worse: 6 rejections in 45 became 9 in
44. The misreads reproduce, so they are the model's considered answer rather than sampling noise —
two attempts at the same recipe returned byte-identical output. At chat temperature a retry
sometimes samples the correct digit and recovers the recipe; at 0.2 the retry budget is spent
re-deriving a known failure. Sampling is now configurable under `Llm:Local:Sampling`, and the
defaults match the library's **as a measured result**, recorded in `LlmSamplingOptions`.

**Fixed regardless — the grammar admitted invalid JSON.** `JsonSchemaGrammar` wrote numbers as an
unbounded digit run, which permits `00` and `012`. JSON forbids both, so the decode was
unparseable. This is a real defect in a guard whose entire purpose is that unparseable output is
unreachable. It was **not**, however, the cause of the observed `InvalidLlmOutput` failures: .NET's
parser reports a leading zero with a different message than the one the runs produced.

### 23.4 Two loose ends

1. **One recipe in 31 still misreads a quantity** (`1 tablespoon cinnamon` returned as `0.25`).
   Contained: `SeedQuantityGate.CheckAgainstStatedNumber` catches it and the recipe is excluded
   rather than stored wrong.

2. **Grammar-constrained decoding still lets an unparseable answer through at a low rate.**
   Diagnosis was blocked by the client discarding the answer at exactly the moment parsing failed,
   so a failure 4,700 bytes into a decode reported an offset and nothing else.
   `LLamaSharpStructuredClient` now reports the text either side of the offending character. If it
   recurs, `DefaultSamplingPipeline.GrammarOptimization` is the first suspect — it defaults to
   `Extended`, which checks the grammar against the top-K tokens rather than the full vocabulary,
   and `None` trades speed for certainty.

### 23.5 Also changed

- `MeasurementUnit.AcceptedSpellings` — the prompt listed only canonical spellings while its own
  worked example taught `teaspoon`, because the lenient alias table was private. The prompt is now
  derived from the full accepted set, and a test asserts every unit the example teaches appears in
  the list.
- `Llm:Local:ChatTemplate` silently falls through to ChatML for an unrecognised value. The local
  config read `chatlm`, which worked only by that accident. Fixed; **hardening the selector to fail
  fast is still open**.
