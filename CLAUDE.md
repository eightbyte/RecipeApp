# RecipeApp — Project Conventions

## Project overview
Mobile-first web app for storing recipes, building meal plans, and generating shopping lists.

- **Spec:** `SPEC.md` — read this for feature requirements and data model definitions.
- **Current phase:** Phase 8.5.2 complete (FluentValidation 12 migration) — **v1 feature-complete**. Next: Phase 9 (seed recipe library).

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
| Local LLM     | LLamaSharp 0.27.0 + CUDA12 backend    | NuGet packages `LLamaSharp` + `LLamaSharp.Backend.Cuda12`; GGUF model via `Llm:Local:ModelPath` |

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
| `Llm:Local:ModelPath` | Absolute path to a GGUF model file (e.g. `qwen2.5-7b-instruct-q4_k_m.gguf`) |
| `Llm:Local:GpuLayerCount` | GPU layers to offload (default 999 = all); set 0 for CPU-only |
| `Measurement:ResolveCupsOnImport` | Resolve `cup` to grams on import where a density exists (default `true`) |

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

---
## Project Notes
- Use descriptive variable and function names
- Always ensure type safety
- Avoid hard coded values
- ensure naming is clear 
- Test your changes
- never assume, ask for clarity.