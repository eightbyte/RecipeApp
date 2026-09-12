# RecipeApp — Project Conventions

## Project overview
Mobile-first web app for storing recipes, building meal plans, and generating shopping lists.

- **Spec:** `SPEC.md` — read this for feature requirements and data model definitions.
- **Current phase:** Phase 9 in progress (seed recipe library) — **v1 feature-complete**. Stages 1–3 (Wayback discovery, fetch, parse) done and Phase 9.1 (unquantified ingredients — the Stage 4 prerequisite) done. Stage 4 (LLM normalise) is **done: all 1,123 recipes normalised, zero failures**, after a run-abort bug and four quantity repairs took the success rate from 91.3% to 100%. Stage 5 (persist) next — its inputs are `normalised/*.json` plus 1,115 cached photos, and its real work is matching 1,475 distinct ingredient names to the catalogue.

## Repository structure
```
RecipeApp/
├── backend/RecipeApp.API/   .NET 10 Web API
│   ├── Data/                EF Core DbContext + Migrations
│   ├── Models/              Entity classes (one file per model)
│   ├── Endpoints/           Minimal API endpoint extension methods
│   ├── Filters/             (Phase 8.5.2) ValidationFilter.cs — endpoint validation filter
│   ├── DTOs/                Request/response DTOs (added from Phase 2)
│   ├── Services/            Business logic services (added from Phase 2)
│   │   └── Seeding/         (Phase 9) USDA MyPlate seed import — harvester, parser, cache, CLI
│   ├── seed-data/myplate/   (Phase 9) On-disk harvest cache; only manifest.json is committed
│   └── Enums/               Shared enum/constant classes
├── frontend/                Vue 3 SPA
│   └── src/
│       ├── assets/          Global CSS (main.css)
│       ├── components/      Shared components
│       │   ├── layout/      AppTopBar.vue, AppBottomNav.vue
│       │   ├── RecipeBrowser.vue          (Phase 4) searchable recipe picker
│       │   ├── RecentlyCookedDialog.vue   (Phase 4) bottom-sheet confirmation
│       │   ├── SuggestionsPanel.vue       (Phase 4) waste-reduction suggestions
│       │   ├── EmptyState.vue             (Phase 7) reusable empty state
│       │   ├── ErrorState.vue             (Phase 7) reusable error + Try again
│       │   └── AppSnackbar.vue            (Phase 7) global feedback snackbar
│       ├── composables/     (Phase 7) useWakeLock.js — Screen Wake Lock wrapper
│       ├── constants/       (Phase 8.5.1) units.js — storable units + measurement formatting
│       ├── plugins/         vuetify.js
│       ├── router/          index.js — all routes defined here
│       ├── services/        api.js — Axios instance
│       ├── stores/          Pinia stores (one file per domain; incl. ui.js — Phase 7)
│       └── views/           Top-level page components (incl. CookingModeView.vue — Phase 7)
├── specs/                   Phase detail specification documents
├── docker-compose.yml       Local dev environment
├── global.json              Opts `dotnet test` into the Microsoft.Testing.Platform runner
├── SPEC.md                  Full feature specification
└── CLAUDE.md                This file
```

## Technology stack

| Layer         | Choice                                | Notes                                          |
|---|---|---|
| Frontend      | Vue 3 (Composition API) + Vite        | `<script setup>` only — no Options API         |
| State         | Pinia                                 | One store file per domain                      |
| Routing       | Vue Router 4                          | Lazy-loaded views via `() => import(...)`      |
| UI Components | Vuetify 3                             | Primary component library                      |
| CSS Utilities | Bootstrap 5                           | Grid + utility classes; imported after Vuetify |
| HTTP          | Axios (`src/services/api.js`)         | Always use the pre-configured instance         |
| Backend       | .NET 10 Minimal API                   | Endpoint groups, not MVC controllers           |
| ORM           | EF Core 10 + Npgsql                   | Code-first; run migrations with `dotnet ef`    |
| Validation    | FluentValidation 12                   | One validator class per request DTO; invoked explicitly via `.WithValidation<T>()` |
| Mapping       | Manual (extension methods)            | `DTOs/Mappings.cs` — static `ToResponse()` / `ToDetail()` / `ToListItem()` |
| Database      | PostgreSQL 16                         | All timestamps stored as UTC                   |
| Local LLM     | LLamaSharp 0.27.0 + CUDA12 backend    | NuGet packages `LLamaSharp` + `LLamaSharp.Backend.Cuda12`; GGUF model via `Llm:Local:ModelPath` (Qwen3.5 9B Q6) |

## Running locally

```bash
# 1. Start PostgreSQL
docker compose up -d postgres

# 2. Run the .NET API (auto-migrates on startup in Development)
cd backend/RecipeApp.API
dotnet run

# 3. Run the Vue dev server
cd frontend
npm run dev
# → http://localhost:3000
```

API is at `http://localhost:5000`
Scalar API docs: `http://localhost:5000/scalar/v1`
Health check: `http://localhost:5000/health`
Readiness check: `http://localhost:5000/health/ready`

## CLI commands

Each detects its argument, runs, and exits without starting Kestrel. Run from `backend/RecipeApp.API`.

```bash
dotnet run -- seed-catalogue [count]   # LLM-generated starter ingredient catalogue (default 200)
dotnet run -- seed-densities           # Curated bulk densities; idempotent, only fills nulls

# Phase 9 — USDA MyPlate seed library. Stages 1-4 implemented; 5 pending.
dotnet run -- seed-recipes --report        # Print cached progress; no work, no network
dotnet run -- seed-recipes --discover      # Stage 1 — CDX query → manifest.json (~1,123 slugs)
dotnet run -- seed-recipes --harvest       # Stages 1-2 — cache pages + photos (~50 min, resumable)
dotnet run -- seed-recipes --harvest --limit 3
dotnet run -- seed-recipes --refresh-cache # Discard cached pages/photos, re-harvest
dotnet run -- seed-recipes --parse         # Stage 3 — cached HTML → parsed/*.json (offline, ~4 s)
dotnet run -- seed-recipes --parse --force # Discard parsed/ and re-derive from the cached pages
dotnet run -- seed-recipes --normalise     # Stage 4 — parsed/ → normalised/*.json (LLM, no DB)
dotnet run -- seed-recipes --normalise --limit 30
dotnet run -- seed-recipes --normalise --force  # Discard normalised/ and re-run the LLM pass
dotnet run -- seed-recipes --normalise --slug apple-carrot-soup   # One recipe; repeatable
```

`seed-recipes` needs no database — stages 1-2 touch only the archive and the local cache, stage 3
only the cache, and stage 4 the cache plus the local model. Ctrl+C stops it cooperatively;
re-running resumes from the last cached recipe.

`--slug` exists for prompt iteration: Stage 4's failures cluster by ingredient-line shape rather
than by manifest position, and re-running one named recipe is a twenty-second experiment instead
of a ten-minute one. It applies to `--parse` as well.

## Running tests

Backend tests run on **Microsoft.Testing.Platform**, not VSTest. xunit.v3 4.0 is MTP-only, and
the .NET 10 SDK errors out if an MTP test project is driven through the VSTest target — the
repo-root `global.json` (`test.runner: Microsoft.Testing.Platform`) is what makes `dotnet test`
work. Do not re-add `Microsoft.NET.Test.Sdk` or `xunit.runner.visualstudio`; both are VSTest-only
and no longer used.

```bash
docker compose up -d postgres          # Testcontainers needs Docker running
cd backend
dotnet test RecipeApp.Tests/RecipeApp.Tests.csproj
dotnet test RecipeApp.Tests/RecipeApp.Tests.csproj --coverage --coverage-output-format cobertura
```

## Git workflow

- **Branching model:** `master` (stable releases) → `develop` (integration) → `phase-N-*` (feature branches off `develop`).
- **Never merge "down" into `master`.** `master` only receives merges from `develop` at release time. Do not merge feature branches or arbitrary work directly into `master`, and never merge `master` back into a downstream branch as a routine flow.

## Key conventions

### Backend (.NET)
- **Endpoint registration:** extension methods in `Endpoints/` returning `WebApplication`. Register in `Program.cs` as `app.MapXxxEndpoints()`.
- **Validation:** never hand-write a validate-and-return block in a handler. Chain
  `.WithValidation<TRequest>()` (from `Filters/ValidationFilter.cs`) onto the route; it resolves
  the scoped `IValidator<TRequest>`, short-circuits with an RFC 7807 400, and declares that 400
  to OpenAPI. The handler then takes only what it needs to do the work.
- **DTOs:** suffix with `Request` (input) or `Response` (output). Keep in `DTOs/` subdirectory matching the feature area.
- **EF migrations:** `dotnet ef migrations add <Name> --output-dir Data/Migrations`
- **Timestamps:** always `DateTime.UtcNow` / `TIMESTAMPTZ` columns; never use local time.
- **Soft deletes:** not used in v1. Hard-delete is fine.
- **Portion scaling:** never modify stored `Amount` values. Multiply in the query/DTO layer.

### Frontend (Vue)
- **Always use `<script setup>`** — no Options API.
- **Axios:** import `api` from `@/services/api.js`. Never use raw `fetch` or `axios` directly.
- **Pinia stores:** `use<Domain>Store()` naming. Define actions for all API calls.
- **Vuetify components:** prefer Vuetify components over raw HTML where a component exists.
- **Bootstrap:** use only for utility classes (`d-flex`, `gap-*`, etc.) or responsive grid. Do not use Bootstrap JavaScript or Bootstrap components.
- **Grayscale rule:** apply CSS class `grayscale` to recipe images where `lastCookedAt` is within 7 days.
- **Route names** (defined in `router/index.js`): `home`, `recipes`, `meal-plan`, `shopping`.

## Measurements
- The storable unit set lives in **one place**: `Enums/MeasurementUnit.cs` (backend) mirrored by
  `src/constants/units.js` (frontend). Never hardcode a unit array — consume those.
  Current set: `g`, `kg`, `ml`, `L`, `pcs`, `tsp`, `tbsp`, `cup`.
- `MeasurementUnit.IsValid` is **strict** (canonical spelling only) and is what validators use;
  `MeasurementUnit.TryCanonicalise` is **lenient** ("teaspoons", "ML", "cups") and is what every
  path receiving a unit from outside calls *before* validating.
- All unit arithmetic lives in `Services/MeasurementConverter.cs`. Stage A converts customary to
  metric and canonicalises spelling; Stage B resolves `cup` against the matched ingredient's
  `GramsPerMillilitre`. Import policy toggle: `Measurement:ResolveCupsOnImport`.
- **`Ingredient.GramsPerMillilitre == null` means "no reliable density — do not invent a mass."**
  A cup of that ingredient is stored as `cup`, not as a fabricated gram figure. Liquids and
  packing-dominated ingredients are deliberately left null.
- **`RecipeIngredient.Amount == null` means "the source states no quantity" (Phase 9.1).**
  `salt`, `raisins`, `nonstick cooking spray` — a real property of home cooking, not a parse
  defect. `Unit` is null exactly when `Amount` is; both validators reject a half-set pair, and
  `RecipeIngredient.ToStoredMeasurement` holds the invariant at the entity boundary because
  `ConfirmAsync` has no validation of its own and a service-level caller bypasses the endpoint
  filter. **Never zero**: `0` renders as `0 g Salt` and sums into shopping lists. Unquantified rows
  are partitioned out of `ShoppingListService` consolidation and never summed; an ingredient the
  plan only ever names yields one amount-less item. On the frontend, `formatMeasurement` returns
  null for a null amount, and scaling must short-circuit before multiplying — `null * 2` is `0`.
  **"The source" means the whole published line, not the first span of it.** Stage 4 asks
  `SeedQuantityGate` about `ParsedIngredientLine.FullText`, because MyPlate renders an optional
  ingredient as the food in one span and the amount in a sibling `span.notes` —
  `orange peel, dried` + `(1 teaspoon, optional)`. Measured: 109 lines on 87 recipes (8.0%) state
  their quantity only there, every one a real measurement, none an incidental digit. Reading the
  item text alone calls all 109 unquantified and then rejects the model for reading them right.
- **Never destroy the input.** Imported rows record `RecipeIngredient.SourceAmount`/`SourceUnit`
  verbatim. These are provenance only — never summed, never used in consolidation — and every
  persistence path (scrape confirm, recipe create/update) must carry them through.
- Portion sizes: `HALF` (x0.5), `REGULAR` (x1.0), `DOUBLE` (x2.0) — applied at display time only.
  `SourceAmount` scales the same way as `Amount`.

## Environment variables

### Frontend (`.env.development`)
| Variable | Description |
|---|---|
| `VITE_APP_TITLE` | App name shown in title bar |
| `VITE_API_BASE_URL` | Full base URL for the API (dev: `http://localhost:5000/api/v1`) |

### Backend (`appsettings.Development.json` — gitignored)
| Key | Description |
|---|---|
| `ConnectionStrings:DefaultConnection` | PostgreSQL connection string |
| `ImageStorage:BasePath` | Local path for uploaded recipe images |
| `Llm:Local:ModelPath` | Absolute path to a GGUF model file. **Currently `Qwen3.5-9B-Q6_K.gguf`** — measured 96.8% vs 85.7% for Qwen2.5 7B on the Stage 4 benchmark, and faster in wall time because it needs far fewer retries. Model choice dominates prompt tuning here. |
| `Llm:Local:GpuLayerCount` | GPU layers to offload (default 999 = all); set 0 for CPU-only |
| `Llm:Local:ChatTemplate` | `chatml` (Qwen, Mistral), `llama3` or `gemma`. **Unrecognised values silently fall through to ChatML**, so a typo here is invisible until output degrades |
| `Llm:Local:Sampling:*` | `Temperature`, `TopK`, `TopP`, `MinP`. Defaults match LLamaSharp's own **as a measured result** — decoding colder for extraction made Stage 4 worse, see `LlmSamplingOptions` |
| `Measurement:ResolveCupsOnImport` | Resolve `cup` to grams on import where a density exists (default `true`) |

The `RecipeSeeding` section lives in the committed `appsettings.json` (nothing secret in it). Keys
worth knowing: `CacheDirectory`, `PreferredSnapshotYear`, `FetchDelayMilliseconds`,
`MinimumDiscoveredSlugs`, `DownloadImages`. See `Services/Seeding/RecipeSeedingOptions.cs`.

## Notes
- **No AutoMapper** — manual mapping in `DTOs/Mappings.cs` (extension methods on entity types). Simpler to trace, no reflection.
- FluentValidation validators are registered automatically via `AddValidatorsFromAssemblyContaining<Program>()` (scoped).
  **Do not add `FluentValidation.AspNetCore`** — it is deprecated (Legacy, frozen at 11.3.1), its
  auto-validation filter only ever hooked MVC, and there is no MVC here. Reference
  `FluentValidation` + `FluentValidation.DependencyInjectionExtensions` directly, and validate at
  the route with `.WithValidation<T>()` (see below).
- The `uploads/images/` directory is served as static files at `/uploads/images/`. Gitignored; mount as a Docker volume in production.
- Phase 3 added `Services/RecipeScrapeService.cs`, `Services/IRecipeScrapeService.cs`, `Endpoints/RecipeScrapeEndpoints.cs`, `DTOs/Scrape/`, `Validators/ScrapeValidators.cs`. Originally used `Anthropic.SDK`; replaced by LLamaSharp in Phase 8.
- Phase 4 added `Models/MealPlan.cs`, `Models/MealPlanRecipe.cs`, `Enums/PortionSize.cs`, `Services/MealPlanService.cs`, `Endpoints/MealPlanEndpoints.cs`, `DTOs/MealPlans/`, `Validators/MealPlanValidators.cs`, and migration `AddMealPlans`. Frontend: `stores/mealPlans.js`, `components/RecipeBrowser.vue`, `components/RecentlyCookedDialog.vue`, `components/SuggestionsPanel.vue`, `views/MealPlanBuilderView.vue`, `views/MealPlanDetailView.vue`, `views/PastPlansView.vue`.
- Phase 5 added `Models/ShoppingList.cs` (ShoppingList + ShoppingListItem), `Services/ShoppingListService.cs`, `Endpoints/ShoppingListEndpoints.cs`, `DTOs/ShoppingLists/`, `Validators/ShoppingListValidators.cs`, and migration `AddShoppingLists`. Also added `MealPlan.UpdatedAt` column for stale-list detection. Frontend: `stores/shoppingList.js`, `components/AddCustomItemDialog.vue`, updated `views/ShoppingView.vue`.
- Phase 7 (frontend-only; **no backend product code** — only pre-existing backend *test* fixes: validation endpoints return RFC 7807 `400` so those tests now assert 400, and a DbContext-sharing timestamp test was corrected) added **Cooking Mode** (`views/CookingModeView.vue`, `composables/useWakeLock.js`, `recipe-cooking` route, `fullscreen` route meta gated in `App.vue`), reusable states (`components/EmptyState.vue`, `components/ErrorState.vue`, `components/AppSnackbar.vue`, `stores/ui.js` global snackbar), and a **PWA** via `vite-plugin-pwa` (manifest + service worker + runtime caching of `/api/v1/recipes` and `/uploads/images/`; launcher icons in `public/icons/`). `nginx.conf` now proxies `/uploads/`, serves `sw.js`/`manifest.webmanifest` with `no-cache`, and gzips the manifest. Store `fetchRecipe`/`fetchPlan` treat 404 as "not found" (empty state) vs. error; `fetchActivePlan` now toggles `loading` for skeletons. Cooking step "done" state is ephemeral (component-local, not persisted).
- Phase 8.5.1 (pre-Phase-9 revisions) made measurement handling structural. Added
  `Enums/MeasurementUnit.cs` — the one authoritative unit table (dimension, base factor, strict
  `IsValid`, lenient `TryCanonicalise`), replacing five duplicated hardcoded arrays and deleting
  the dead `JsonSchemaGrammar.AllowedUnits`. Added `cup` as a storable unit. Added
  `Services/MeasurementConverter.cs` (both conversion stages, relocated from
  `RecipeScrapeService.ConvertUnit`; `cup`/`cups` removed from the customary table) and
  `Services/MeasurementOptions.cs` (`Measurement:ResolveCupsOnImport`). Added
  `Ingredient.GramsPerMillilitre`, `RecipeIngredient.SourceAmount`/`SourceUnit`, and migration
  `AddMeasurementDensityAndSource`. Added `Data/IngredientDensitySeeder.cs` — 50 curated,
  human-reviewed densities matched by catalogue-name alias, idempotent, never overwriting a
  hand-corrected value — plus a `seed-densities` CLI command (also run at Development startup).
  `ScrapeConfirmIngredientValidator` gained the unit whitelist it never had;
  `IngredientValidators` gained a density range and a `DefaultUnit` whitelist.
  `ShoppingListService` consolidation now consumes `MeasurementUnit.Table` (fixing the live
  tsp/tbsp bug where `1 tbsp` + `15 ml` produced two rows), resolves mass/volume mixes through
  density, and presents a single-unit group in that unit (`2 cup + 1 cup = 3 cup`). Frontend:
  `constants/units.js` consumed by both unit pickers, the scrape preview's free-text unit became
  a `v-select`, provenance round-trips through the recipe form, and the detail and cooking views
  show the source measurement as secondary text.
- Phase 8.5.2 (pre-Phase-9 revisions, backend-only) migrated off the deprecated
  `FluentValidation.AspNetCore` 11.3.1 to `FluentValidation` 12.1.1 +
  `FluentValidation.DependencyInjectionExtensions` 12.1.1, both referenced explicitly. Nothing
  used the removed package: its auto-validation filter hooks MVC, and this API is Minimal API
  throughout. The swap also closed a live version skew — the test project already referenced
  12.1.1, so the suite had been running the API's 11.x-compiled validators against a 12.1.1
  assembly. Added `Filters/ValidationFilter.cs`: `ValidationFilter<TRequest>` plus the
  `.WithValidation<TRequest>()` extension, which replaced the identical three-line
  validate-and-return block at the top of all twelve mutating endpoints, attaches
  `ProducesValidationProblem()` so `/scalar/v1` documents the 400 (declaring it does suppress
  ASP.NET's inferred default `200`; documenting accurate success/404/409 responses is a separate
  pass, see the phase spec §15), and always passes `HttpContext.RequestAborted`. Endpoint
  handlers no longer take `IValidator<T>`, and `using FluentValidation;` is gone from all five
  endpoint files. Tests: `Filters/ValidationFilterTests.cs` (scoped resolution through the
  filter, short-circuit, pass-through, and an OpenAPI assertion that discovers validated
  endpoints rather than listing them) and `Validators/ScrapeValidatorTests.cs` (the scrape
  validators had no direct coverage). No response shape changed; no existing test was edited.
- Phase 8 (backend-only) replaced `Anthropic.SDK` with **LLamaSharp 0.27.0** (llama.cpp .NET bindings, CUDA12 backend). Added `Services/Llm/` abstraction layer: `ILlmStructuredClient`, `LlmOptions`, `LlamaModelHolder` (singleton, owns GGUF model weights + `SemaphoreSlim` gate), `LLamaSharpStructuredClient` (GBNF grammar-constrained decoding), `JsonSchemaGrammar` (JSON Schema → GBNF converter). `RecipeScrapeService.NormaliseAsync` extended with two-pass semantic ingredient matching (exact lookup then batched LLM). Added `IngredientCatalogueSeeder` and `seed-catalogue` CLI command. Config: `Llm:Provider` (`"Local"`), `Llm:Local:ModelPath` (path to `.gguf`), `Llm:Local:ChatTemplate` (`"chatml"` or `"llama3"`). `RecipeScrapingOptions` lost Anthropic keys; gained `LlmTimeoutSeconds` (120) and `MatchConfidenceThreshold` (0.8). NpgSql health check changed to lazy `Func<IServiceProvider,string>` resolution; `RecipeAppFactory` injects Testcontainers connection string via `ConfigureAppConfiguration` so the health check uses the right DB in tests.
- Phase 9 stages 1–2 (backend-only, no new packages) added `Services/Seeding/`:
  `RecipeSeedingOptions.cs` (bound to `RecipeSeeding`), `SeedModels.cs` (manifest, state,
  `SeedStage`, `SeedHarvestException`), `SeedCacheStore.cs` (cache layout, atomic writes, a
  path-traversal slug guard, corrupt-state recovery), `IWaybackHarvester.cs` +
  `WaybackHarvester.cs` (CDX discovery, filter rules, snapshot selection, sequential fetch with
  backoff, image harvest), and `SeedRecipesCommand.cs` (the `seed-recipes` CLI). `Program.cs`
  registers these plus the `RecipeSeeder` named `HttpClient`, and withholds everything after the
  `seed-recipes` token from the host's command-line configuration provider so the command's own
  flags are not read as config keys. `RecipeJsonLdExtractor` gained a public `TryFindRecipeNode`
  (the harvester needs `image.url`; the Stage 3 parser will need the rest) — the existing
  script-scanning loop was extracted behind it, unchanged.
  Three deviations from the spec:
  **(0)** the cache lives at `seed-data/myplate`, not §5's `data/seed/myplate` — Windows paths are
  case-insensitive, so `data/` merges into the existing EF Core `Data/` folder and the harvest
  lands beside `AppDbContext.cs`. Verified: it did, before the rename.
  The other two were forced by the live archive:
  **(1)** the CDX query omits `collapse=urlkey` (spec §8). Collapse keeps the *first* capture per
  key, which makes §8's own preferred-year rule inert — measured, it pins 1,086 of 1,123 recipes
  to a 2024 capture, whereas the uncollapsed query resolves 1,121 to the 2025 captures taken just
  before the site was retired. Identical slug set; ~5 MB and ~35 s instead of ~9 s, once.
  `DiscoveryTimeoutSeconds` (300) exists for that one large request.
  **(2)** images are requested through the `im_` snapshot modifier and validated by magic bytes.
  Without `im_` the archive 302s to its HTML viewer, and it serves interstitial/error pages with
  a **200**, so ~11 KB of markup was being cached as `<slug>.jpg`. Real photos are 50–95 KB.
  **Harvest is complete**: 1,123/1,123 pages and 1,115 photos cached; `state.json` is uniformly
  `fetched` or later. It took **three** passes, not the two originally recorded here — each one
  left a residue of `HTTP 503 after 4 attempt(s)` (25 pages and 81 photos after the first,
  scattered across manifest positions 471–683), which is an archive load window rather than
  missing content, and simply re-running `--harvest` recovered every one. **The third pass ran
  after Stage 3 and Stage 4 had already started**, which is why the parse result below was once
  recorded against a denominator of 1,089: 34 pages were still outstanding at that point.
  Re-running `--harvest`, `--parse` and `--normalise` in order folded them in with no special
  handling — the resume logic is what makes a partially harvested corpus safe to work on.
  Zero image-validation rejections corpus-wide, so the `im_` fix holds. The 8 recipes with no
  cached photo genuinely carry no JSON-LD image node, and every recipe that *does* name an image
  has its file on disk.
  Tests: `Seeding/SeedCacheStoreTests.cs`, `Seeding/WaybackHarvesterTests.cs`, plus
  `Infrastructure/TestHostEnvironment.cs` and `Infrastructure/StubHttpClientFactory.cs`. No
  network in CI.
- Phase 9 Stage 3 (backend-only, no new packages) added `Services/Seeding/MyPlateRecipeParser.cs`
  (cached page → `ParsedSeedRecipe`, deterministic, no LLM), `Services/Seeding/SeedParseModels.cs`
  (`ParsedSeedRecipe`, `ParsedIngredientLine`, `MyPlateTemplate`, `SeedParseFailure`,
  `SeedParseResult`, `SeedParseException`), and `Services/Seeding/RecipeLibrarySeeder.cs`
  (`IRecipeLibrarySeeder.ParseAsync` — the manifest walk, per-slug failure isolation, resume).
  `SeedCacheStore` gained `HasParsed`/`WriteParsedAsync`/`TryLoadParsedAsync`/
  `ClearParsedContentAsync`; `RecipeSeedingOptions` gained `DefaultServings` (4);
  `SeedRecipesCommand` gained `--parse`, and `--force` now also means "re-derive" for it.
  **Result: 1,123/1,123 pages parsed, zero failures, ~4 s** — 8,882 ingredient lines and 6,857
  steps, 1,058 primary + 65 legacy templates. (Recorded as 1,089 until the last 34 pages were
  harvested; the hazard measurements below were taken on that first 1,089 and have not been
  re-run against the full corpus.)
- **What the corpus settled about Stage 3's hazards** (spec §10.3, all re-measured against the
  1,089 cached pages rather than the single research page they were observed on):
  - **Hazard 2 (mojibake) does not exist here.** Zero `U+FFFD` and zero undecoded entities across
    the corpus, so **no stripping logic was written**. The observation behind it —
    `165 degrees F<?>(3-5 minutes)` — is a literal `&nbsp;`, which AngleSharp decodes; NBSP is
    normalised to a space unconditionally (1,088 of 1,089 pages contain one).
  - **Hazard 1 (truncated descriptions) is real: 286 pages.** The parser prefers
    `.mp-recipe-full__description` and falls back to JSON-LD; the two agree on the other 797.
  - **Hazard 5 (notes bleeding into steps) is real but not marker-shaped: 30 pages.** Only 11 of
    those carry the `*` the spec said to split on; the rest are `Storage:` / `Create-a-Flavor
    Changes:` / bare prose. The rule used instead is structural — **nothing after the last
    `<ol>` is a step** — which covers all 30 and needs no marker.
  - **Hazard 6 (adapted-source credits): 1,081 of 1,089 pages** carry one; captured to
    `SourceCredit` via `.field__item`, which drops the `Source:` label without string-stripping.
  - **Hazard 4 (toolbar injection) is not a live risk, but scoping is kept anyway.** No recipe
    field class occurs outside `.mp-recipe-full` or more than once per page.
- **Two structural facts about the corpus that the spec did not predict**, both of which silently
  lose content if ignored:
  - **Every one of the 1,089 pages renders its directions as an `<ol>`** — steps are already
    segmented, so Stage 3 emits them directly and nothing has to be guessed. But **28 pages carry
    more than one list**, split by a section label (`Icing:`, `Make Dumplings:`). Taking only the
    first list drops the rest of the recipe. Labels are folded onto the step they introduce
    rather than becoming contentless steps.
  - **A nested sub-list inside a step must be counted once.** `QuerySelectorAll("li")` descends
    into it, so the sub-list's text appeared both inside its parent step and again as separate
    steps (caught on `black-bean-and-couscous-salad`). Both list walks take direct `<li>` children
    only.
- **Ingredient group headings are dropped, narrowly.** An `<li>` wholly wrapped in `<b>`/`<strong>`
  *and* ending in a colon is layout, not shopping — 86 such items on 80 pages (`For the Dressing:`).
  The colon is load-bearing: 40 other bolded items are real entries (`aluminum foil (10x12 inches
  square)`) and are kept, at the cost of ~40 colon-less headings surviving as ingredients.
- **Stage 4 risk measured from the parsed output: `Amount <= 0` cannot be a blanket rejection.**
  432 of 8,601 ingredient lines (5.0%) contain no digit at all — `salt`, `pepper`,
  `nonstick cooking spray`, `salt and pepper, to taste` — and they are spread across **313 of the
  1,089 recipes (28.7%)**. Spec §11.3's validation gate rejects any recipe with an ingredient
  whose `Amount <= 0`; applied literally that fails 28.7% of the corpus on its own and puts the
  §20 "≥ 95% persisted" bar out of reach before the LLM has made a single mistake. Stage 4 needs a
  deliberate policy for unquantified ingredients (a `to taste` convention, or exempting them from
  the gate) rather than inheriting the rule as written.
  **Resolved by Phase 9.1 (below), which is implemented.**
- Phase 9.1 (`specs/phase-9.1-unquantified-ingredients.md`, backend + frontend, no new packages)
  made "this ingredient has no stated quantity" representable — the prerequisite for Stage 4, not
  part of it. It is not a "to taste" convention: only 40 of the 432 lines say "to taste" and 49%
  are ordinary foods (`raisins`, `lemon zest`), so the predicate is *the source states no
  quantity*. Widened `RecipeIngredient.Amount`/`Unit` to nullable with migration
  `AllowUnquantifiedRecipeIngredients` (columns only; no row is rewritten, nothing backfills), and
  the same widening through `RecipeIngredientRequest`/`Response`, `ScrapeConfirmIngredient` and
  `ScrapePreviewIngredient`. Added `Services/Seeding/SeedQuantityGate.cs` —
  `HasStatedQuantity(text)` (ASCII digit, vulgar fraction, or a *leading* number word) plus
  `Check(text, amount, unit)` returning a `SeedQuantityVerdict` with the unit already canonicalised.
  Added `RecipeIngredient.ToStoredMeasurement`, used by both persistence paths.
  **Three things worth knowing:**
  - **The gate is stricter than the rule it replaces, not laxer.** The exemption is earned from the
    source text, never granted by the model's output, so a model inventing `1 tsp` for a line
    reading `salt` is now rejected where `Amount <= 0` waved it through. A model cannot opt itself
    out by returning null for a line that *does* state a quantity.
  - **The fix had to start at `RecipeSchemaJson`, not at the gate.** `amount` was a required
    number, so grammar-constrained decoding *forced* the model to invent one. It is now
    `["number", "null"]` and still required, so an absent quantity is an explicit assertion.
    `JsonSchemaGrammar` already emitted `(number | null)` for that form, so no grammar work was
    needed. `RecipeSchemaJson` became `internal` and `JsonSchemaGrammarTests` now reads it directly
    — the local copy in that test file had already drifted and would have kept passing.
  - **`null * 2` is `0` in JavaScript.** Guarding only `formatMeasurement` was not enough; each
    view's `formatAmount` short-circuits before applying the portion multiplier, or every
    unquantified ingredient renders as a bare `0`. A test caught this.
  Frontend: `formatMeasurement` returns null for a null amount, new `toPayloadMeasurement` sends
  null for both rather than coercing to `0`, and the detail and cooking views guard the amount
  span. `ShoppingView` needed no change — it was already guarded. **Blast radius is deliberate and
  wider than seeding**: hand-entered recipes may now omit an amount, and a scrape preview shows an
  empty field where it used to show a fabricated number.
- **Phase 9 Stage 4 (LLM normalise) — benchmark clears the §20 bar; full corpus pass still to
  run (2026-09-09).** Backend-only, no new
  packages. `seed-recipes --normalise` walks `parsed/`, runs each recipe through a
  grammar-constrained LLM pass, applies the §11.3 gate, and writes `normalised/{slug}.json`. No
  database: catalogue matching and `NormaliseAsync` are deliberately Stage 5's, so the artefact
  stays machine-independent. Added `Services/Seeding/SeedRecipeNormaliser.cs` and
  `SeedNormaliseModels.cs`; `RecipeLibrarySeeder` gained `NormaliseAsync`, `SeedCacheStore` gained
  the normalised artefact plus `TryComputeParsedFingerprintAsync`, and `SeedRecipesCommand` gained
  `--normalise` and `--slug`. Config: `LlmTimeoutSeconds` (180), `MaxLlmOutputTokens` (4096),
  `MaxConsecutiveLlmFailures` (5). Tests: `SeedRecipeNormaliserTests`, `SeedRecipesCommandTests`,
  `Infrastructure/StubLlmStructuredClient.cs`, `Services/Llm/LlmSamplingOptionsTests.cs` and
  `Services/Llm/LLamaSharpStructuredClientTests.cs`, plus Stage 4 cases on the existing files — 917
  suite-wide, no GPU and no network.
  **Where it stands: 30 of 31 recipes normalised (96.8%) on the 30-recipe benchmark, clearing the
  §20 bar.** Getting there took six measured runs; the trajectory was 56.6% → 66.7% → 85.7% →
  96.8%. The benchmark is `seed-recipes --normalise --force --limit 30`, which counts *successes*,
  so the attempt count is the denominator and the failure taxonomy is the result. **The full corpus
  has not been normalised yet** — the benchmark leaves 30 recipes in `normalised/`. Projected at
  9.2 s/recipe, a full pass is under 3 hours.
  **The single biggest lever was the model, not the prompt.** Swapping Qwen2.5 7B Q6 for
  **Qwen3.5 9B Q6** on an identical build took 85.7% to 96.8% and *reduced* wall time per recipe
  from 15.5 s to 9.2 s, because it stops needing retries (12 retried recipes became 1). Same ChatML
  template, so nothing but `Llm:Local:ModelPath` changed. Two prompt fixes were worth more than
  every other prompt change combined, and three were worth nothing or less than nothing — see
  below. If quality regresses, suspect the model file before the prompt.
  **Eight things worth knowing, each found by running the corpus rather than reasoning about it:**
  - **The extraction schema was the bug, not the prompt.** `JsonSchemaGrammar` omits non-required
    properties from the grammar entirely, so an optional property is not one the model may skip —
    it is one the model *cannot emit*. `notes` was unreachable, so preparation words went into
    `unit` (`"pound, chunks"`, `"teaspoon, optional"`), which then failed the storable-unit check
    and lost the recipe. `notes` and `description` are now required-and-nullable, exactly as Phase
    9.1 did to `amount`. This also means Phase 3 scraping could never return a description.
  - **"The source line" means both spans.** MyPlate renders an optional ingredient as the food in
    one span and the amount in a sibling `span.notes`. 109 lines on 87 recipes state their quantity
    only there. The gate now reads `ParsedIngredientLine.FullText`.
  - **The worked example taught the wrong rule.** With a bare `salt` as its null case the model
    learned *salt* was the exemption rather than *no number*, and began nulling `1 teaspoon salt`.
    The null case is now cheese, and salt appears with a quantity.
  - **The gate could not see a wrong amount at all.** Dry-run over output it had already approved:
    12 of 187 checkable rows carried a wrong quantity — `3 tablespoons brown sugar` read as `0.33`,
    `1/4 cup oil` as `1`. 83% of corpus lines state exactly one number, so
    `SeedQuantityGate.CheckAgainstStatedNumber` now verifies those arithmetically. Lines stating
    several (container sizes) are left alone rather than guessed at.
  - **±2 ingredient tolerance is gone.** The gate judges each row against its own source line, so
    rows pair to lines by position and the count must match exactly.
  - **Label the ingredient lines with the index you want back.** The user message numbered them
    from 1 while the prompt asked for 0-based positions, so the only 0-based number in the exchange
    was one the model had to derive. It did not: one answer mixed both bases, `[0]` for the first
    ingredient but `[8]` and `[9]` for the eighth and ninth of nine. **Only the overrun past the end
    was detectable**, so an unknown share of *accepted* linkage was silently off by one. Restating
    the valid range in words changed nothing, because the conflict was between two things the model
    could both read. Lines now carry `[0]`, `[1]`, `[2]` labels — brackets so they cannot be
    confused with step numbers, which still count from 1 because `step_number` does. That single
    change took `IngredientIndexOutOfRange` from 5 failures to 0.
  - **Never give the model a menu of candidate amounts.** 37.3% of ingredient lines state a
    fraction and the surviving quantity errors were all fractions read as an adjacent value, so the
    prompt was given the ten conversions it needed, computed by the gate's own parser so the two
    could not disagree. It made the class **nearly three times worse** (9 rejections in 44 recipes
    became 14 in 45): the model stopped reading the line and started choosing from the list. `1/8`
    came back as `1`, `1/2` as `0.125`, and `1 teaspoon salt`, which contains no fraction at all,
    came back as `0.25`. Removed, with a comment in the source and a test that fails if a run of
    decimals reappears outside the worked example, so it is not re-added.
  - **Temperature is the wrong lever for a systematic error, and lowering it removed the thing that
    was compensating.** Dropping to 0.2 looked obviously right for constrained extraction and
    measured worse (6 rejections in 45 became 9 in 44). The misreads reproduce, so they are the
    model's considered answer, not sampling noise — two attempts on the same recipe returned
    byte-identical output. At chat temperature a retry sometimes samples the correct digit and
    recovers the recipe; at 0.2 the retry budget is spent re-deriving a known failure. Sampling is
    now configurable (`Llm:Local:Sampling`), and the values are LLamaSharp's own **as a measured
    result**, not an oversight.
  - **The prompt was arguing with itself about units.** It listed only canonical spellings while its
    own worked example taught `teaspoon`, because the lenient alias table was private.
    `MeasurementUnit.AcceptedSpellings` now exposes it and the prompt is derived from that; a test
    asserts every unit the example teaches appears in the list.
  **One loose end.** Grammar-constrained decoding still lets an unparseable answer through at a low
  rate — the grammar's number rule admitted `00` and `012`, which is fixed, but that was **not** the
  cause of the observed failures (a leading zero produces a different parser message; I checked). The
  client now reports the text either side of the offending character, so the next occurrence names
  itself instead of only giving an offset.
- **Phase 9 Stage 4 — the first real corpus pass, and what it exposed (2026-09-10).** The run reached
  manifest position 352 (315 normalised, 30 failed) and then **every re-run died at position 77 with
  nothing normalised**, which is the headline finding:
  - **A resumed run failing everything it attempts is the expected state, not a symptom.** The abort
    guard counted consecutive failures among *attempted* recipes, and cached successes `continue`
    before the counter is reached. So on resume the only recipes it attempts below the high-water mark
    are the ones that already failed — and those reproduce byte-identically. The guard fired on the
    first five of a thirty-recipe backlog and stopped, leaving 744 recipes never tried, forever.
    `MaxConsecutiveLlmFailures` now counts only **systemic** failures
    (`SeedNormaliseFailureExtensions.IsSystemic`: `InvalidLlmOutput`, `LlmTimeout` — nothing came
    back). A content rejection means the model answered, the grammar held and the gate disagreed:
    evidence the pipeline works, never evidence it is broken. It leaves the counter alone rather than
    resetting it, so an interleaved bad recipe cannot mask a model that is genuinely unavailable.
  - **The gate now answers from the line where the line settles the question**, instead of rejecting.
    It already trusted its own arithmetic enough to discard a whole recipe on it, which is the same
    thing as trusting it to supply the number. **Four repairs, all source-derived, in this order in
    `BuildIngredients`. Measured together against the real model they recover 29 of the 30 backlog
    failures:**
    1. **A count the model declined to read.** `1 dash black pepper` resembles an unquantified
       seasoning and the model reads it as one. Phase 9 had already decided an unsized measure counts
       its things as `pcs`, but **that decision was unreachable**: the `CountWords` rename only ever
       ran inside `if (returned.Amount is { } …)`, so a null lost the recipe.
       `SeedQuantityGate.TryReadCountedQuantity` reads the count off the line. 44 lines on 43 recipes.
       Guarded by `StatesAUnitOfMeasurement`, so `1/2 cup onion, chopped into 4 slices` is never read
       as four pieces, and a range yields its low end (`4-6 handfuls` is 4).
    2. **An amount the model left out of a line stating exactly one number.** Closes an inconsistency
       repair 3 creates rather than granting new licence: on such a line the gate already *requires*
       the stored amount to equal the line's number, so a wrong one is corrected to it and only a null
       was treated differently. The class is MyPlate's optional ingredient, which states its quantity
       in a trailing note — `salt (optional, 1/4 teaspoon)` — and whose `optional` leads the model to
       report no quantity at all. 146 lines on 119 recipes. Phase 9.1's rule survives: the model still
       cannot opt itself out by returning null, because the source decides either way. `HasStatedQuantity`
       gates it, so an unquantified line and a cut-size-only line both stay unquantified.
    3. **A misread number on a line stating exactly one.** `SeedQuantityGate.TryReadSoleNumber`
       supplies it. Restricted to a *positive* model amount on purpose — a zero is not a digit read
       wrongly, and `NonPositiveQuantity` rejects it deliberately. All 15 measured contradictions
       were plausible positives (`1 tablespoon cinnamon` as `0.25`, `1/4 cup` as `0.125`).
    4. **A wrong unit — found only by shipping repair 3.** Fixing the amount alone *made one case
       worse*: `1 tablespoon cinnamon` came back as `1 cup`, sixteenfold, positive, storable, and in
       agreement with the line's only number, so every other rule admitted it. Rescuing a recipe can
       carry a wrong unit in with it. `SeedQuantityGate.TryReadSoleStatedUnit` takes the unit named
       immediately after a line's **sole** number. Measured before trusting it: it applies to 1,703
       rows of the normalised corpus and agrees with the model on 1,702 — the one disagreement was
       that error. Both guards are load-bearing: *exactly one number*, because 37 rows name a unit
       only inside a parenthetical equivalence (`12 large egg whites (about 1 1/2 cups)` is 12 pcs);
       *immediately after*, because the word after the count in `2 medium apples` is a size. It also
       recovers a unit the model drops, which used to be an `UnstorableUnit` rejection.
  - **The one backlog recipe still failing is genuinely ambiguous, and was left alone deliberately.**
    `chicken-and-dumplings` carries `1 dash black pepper (1/16 of a teaspoon)`, which states both a
    count and its equivalence. Repair 1 declines because the line names a real unit; repair 2 declines
    because the line states two numbers. Recovering it means letting a count word that follows the
    line's *first* number outrank a later unit — which also turns all 329 `1 can (14.5 ounces) …`
    lines into `1 pcs` when the model returns nothing, trading a protective rejection for one recipe.
    Not worth it without measuring that trade first.
  - **`fluid ounce` had no plural while every other customary unit had one**, and
    `ResolveCountUnit` only ever tested the *leading word*, so no multi-word customary unit could
    match at all (`us fluid ounces` reads as `us`). The model read `10 3/4 us fluid ounces` correctly
    and lost its recipe. `UnitConversions` gained the plural and the `us …` forms; both unit resolvers
    now try the longest leading phrase first, bounded by the longest spelling in the tables.
    15 lines on 14 recipes, 13 of them plural.
  - **An incidental digit is real, but only twice.** `carrot, sliced into 3 inch pieces` and
    `aluminum foil (10x12 inches square)` state nothing but a cut size, and counting it made the gate
    demand an amount the line never gave. `HasStatedQuantity` strips dimension phrases; the other 117
    inch-bearing lines carry a real quantity as well and are untouched. Deliberately **not** applied
    to `TryReadSoleNumber` — widening the one check that can reject a plausible answer is not
    justified by two lines of evidence.
  - **`SourceAmount`/`SourceUnit` record the repaired measurement, not the discarded misread.** A
    source measurement that does not convert to the stored one audits nothing. Every repair is logged
    at Information with the slug, the line, what the model said and what the line says, so the run log
    is the audit trail; `SourceText` keeps the line itself.
  - A failed recipe recorded `attempts: 0` in `state.json`, because the runner only ever added the
    attempt count of a recipe that *succeeded*. `SeedNormaliseException.Attempts` now carries it.
  - Suite: **996 tests**, up from 917, no GPU and no network.
  - **Superseded by the completed pass below.** At the time of writing, 344 of 1,089 recipes were
    normalised and the walked region had succeeded on only 91.3%.
- **Phase 9 Stage 4 — the full corpus pass is complete (2026-09-11). 1,123 of 1,123 recipes
  normalised, zero failures, zero stale fingerprints.** `seed-recipes --report` reads uniformly
  `Normalised 1123`. The four quantity repairs and the abort-guard fix above were built against a
  30-recipe backlog and **generalised**: 91.3% became 100%, clearing §20's 95% bar with no recipe
  left behind and none excluded. Retries stayed cheap — 1,008 recipes (89.8%) succeeded on the
  first attempt, 95 needed two, 20 needed three, and none exhausted its budget.
  **Re-verified from the artefacts rather than from the gate that wrote them**, because a gate
  cannot be its own evidence. Across all 8,882 ingredient rows and 6,857 steps: ingredient and step
  counts match `parsed/` exactly for every recipe, step numbers are contiguous, no
  `ingredient_indexes` entry points outside its array, every unit is storable, no amount is zero or
  negative, no amount/unit pair is half-set, and no name is blank. 98.0% of ingredients are
  referenced by at least one step, so Cooking Mode's linkage is dense rather than nominal.
  **Four things the finished corpus settles:**
  - **Phase 9.1's rule held corpus-wide, and the repairs shrank the class it governs.** 331 rows
    (3.7%) are unquantified, down from the 5.0% no-digit rate Phase 9.1 measured, because repairs 1
    and 2 now read the quantity off lines the model declined to read. Independently checked: **every
    one of the 331 sits on a line that states no number**, once dimension phrases are discounted. No
    model opted itself out of a line that did state one.
  - **Single-number lines are exact, by construction.** 7,448 rows (84% of the corpus) sit on a line
    stating exactly one number and **all 7,448 agree with it** inside the gate's 1% tolerance. The
    151 that are not bit-exact are decimal truncations of `1/3` and `1/16` (`0.33`, `0.333`,
    `0.062`), never a different number.
  - **The multi-number line is the whole residual error surface, and Stage 5 inherits it.** 1,180
    rows state two or more numbers, which is exactly the shape `TryReadSoleNumber` and
    `TryReadSoleStatedUnit` decline to judge. 59 of them store a clean count × pack-size product —
    `2 cans (15.5 ounces each)` → `31 ounces` — which is right and worth keeping. **31 rows (0.35%
    of the corpus, ~30 recipes) store a number that is neither stated on the line nor a product of
    two numbers on it**, and no current rule can see them. Three recurring shapes, all arithmetic
    rather than misreading: a percentage absorbed into the quantity (`1 pound 85% lean ground
    turkey` → `1.85 pound`), a parenthetical equivalence combined with the amount instead of
    replacing it (`1/16 cup orange juice (1 tablespoon)` appears on five recipes and is stored as
    `1.062`, `0.125` or `0.75` — never as `1/16`), and can-count slips (`3 cans (15.5 ounces each)`
    → `30`). The worst is
    `sunshine-salad`: `1/3 cup "lite" vinaigrette dressing (around 15 calories per tablespoon)` →
    `13.333 cups`. These are below any sensible corpus-quality bar but they are visible in a
    shopping list, so they are a Stage 5 decision, not a Stage 4 defect.
  - **`HasStatedQuantity` strips `10x12 inches` and `3 inch` but not the inch mark.**
    `6" bamboo skewers` is stored as `6 pcs`. One row in 8,882 — recorded rather than fixed,
    because widening a dimension filter on one line of evidence is how the fraction-menu mistake
    happened.
  **Quality does not vary by run cohort**, so nothing needs re-running: the 30 benchmark recipes
  written before the repairs, the 602 from the first corpus pass and the 491 from the completing
  pass carry unexplained-amount rates of 0.88%, 0.30% and 0.38% — noise at these counts.
  **What Stage 5 is walking into: 1,475 distinct ingredient names, 56.1% of them appearing exactly
  once.** Singular and plural still split (`tomato` 63 rows, `tomatoes` 66), so catalogue matching
  is the Stage 5 workload rather than a lookup. Metadata coverage is high: 8 recipes have no image
  (they carry no JSON-LD image node), 4 no description, 8 no source credit, 75 no notes.

---
## Project Notes
- Use descriptive variable and function names
- Always ensure type safety
- Avoid hard coded values
- ensure naming is clear 
- Test your changes
- never assume, ask for clarity.
