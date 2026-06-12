# RecipeApp — Project Conventions

## Project overview
Mobile-first web app for storing recipes, building meal plans, and generating shopping lists.

- **Spec:** `SPEC.md` — read this for feature requirements and data model definitions.
- **Current phase:** Phase 7 complete (Polish, UX & Cooking Mode) — **v1 feature-complete**.

## Repository structure
```
RecipeApp/
├── backend/RecipeApp.API/   .NET 10 Web API
│   ├── Data/                EF Core DbContext + Migrations
│   ├── Models/              Entity classes (one file per model)
│   ├── Endpoints/           Minimal API endpoint extension methods
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
│       ├── plugins/         vuetify.js
│       ├── router/          index.js — all routes defined here
│       ├── services/        api.js — Axios instance
│       ├── stores/          Pinia stores (one file per domain; incl. ui.js — Phase 7)
│       └── views/           Top-level page components (incl. CookingModeView.vue — Phase 7)
├── specs/                   Phase detail specification documents
├── docker-compose.yml       Local dev environment
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
| Validation    | FluentValidation                      | One validator class per request DTO            |
| Mapping       | Manual (extension methods)            | `DTOs/Mappings.cs` — static `ToResponse()` / `ToDetail()` / `ToListItem()` |
| Database      | PostgreSQL 16                         | All timestamps stored as UTC                   |
| Claude SDK    | Anthropic.SDK v5.10.0 (community, by tghamm) | NuGet package `Anthropic.SDK`; documentation at `https://platform.claude.com/docs/en/api/sdks/csharp` |

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

## Git workflow

- **Branching model:** `master` (stable releases) → `develop` (integration) → `phase-N-*` (feature branches off `develop`).
- **Never merge "down" into `master`.** `master` only receives merges from `develop` at release time. Do not merge feature branches or arbitrary work directly into `master`, and never merge `master` back into a downstream branch as a routine flow.

## Key conventions

### Backend (.NET)
- **Endpoint registration:** extension methods in `Endpoints/` returning `WebApplication`. Register in `Program.cs` as `app.MapXxxEndpoints()`.
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
- All ingredient amounts stored in **metric** units: `g`, `kg`, `ml`, `L`, `pcs`, `tsp`, `tbsp`.
- Portion sizes: `HALF` (x0.5), `REGULAR` (x1.0), `DOUBLE` (x2.0) — applied at display time only.

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

## Notes
- **No AutoMapper** — manual mapping in `DTOs/Mappings.cs` (extension methods on entity types). Simpler to trace, no reflection.
- FluentValidation validators are registered automatically via `AddValidatorsFromAssemblyContaining<Program>()`.
- The `uploads/images/` directory is served as static files at `/uploads/images/`. Gitignored; mount as a Docker volume in production.
- Phase 3 added `Services/RecipeScrapeService.cs`, `Services/IRecipeScrapeService.cs`, `Endpoints/RecipeScrapeEndpoints.cs`, `DTOs/Scrape/`, `Validators/ScrapeValidators.cs`, and integrates `Anthropic.SDK` (NuGet) for AI-powered recipe extraction.
- Phase 4 added `Models/MealPlan.cs`, `Models/MealPlanRecipe.cs`, `Enums/PortionSize.cs`, `Services/MealPlanService.cs`, `Endpoints/MealPlanEndpoints.cs`, `DTOs/MealPlans/`, `Validators/MealPlanValidators.cs`, and migration `AddMealPlans`. Frontend: `stores/mealPlans.js`, `components/RecipeBrowser.vue`, `components/RecentlyCookedDialog.vue`, `components/SuggestionsPanel.vue`, `views/MealPlanBuilderView.vue`, `views/MealPlanDetailView.vue`, `views/PastPlansView.vue`.
- Phase 5 added `Models/ShoppingList.cs` (ShoppingList + ShoppingListItem), `Services/ShoppingListService.cs`, `Endpoints/ShoppingListEndpoints.cs`, `DTOs/ShoppingLists/`, `Validators/ShoppingListValidators.cs`, and migration `AddShoppingLists`. Also added `MealPlan.UpdatedAt` column for stale-list detection. Frontend: `stores/shoppingList.js`, `components/AddCustomItemDialog.vue`, updated `views/ShoppingView.vue`.
- Phase 7 (frontend-only; **no backend product code** — only pre-existing backend *test* fixes: validation endpoints return RFC 7807 `400` so those tests now assert 400, and a DbContext-sharing timestamp test was corrected) added **Cooking Mode** (`views/CookingModeView.vue`, `composables/useWakeLock.js`, `recipe-cooking` route, `fullscreen` route meta gated in `App.vue`), reusable states (`components/EmptyState.vue`, `components/ErrorState.vue`, `components/AppSnackbar.vue`, `stores/ui.js` global snackbar), and a **PWA** via `vite-plugin-pwa` (manifest + service worker + runtime caching of `/api/v1/recipes` and `/uploads/images/`; launcher icons in `public/icons/`). `nginx.conf` now proxies `/uploads/`, serves `sw.js`/`manifest.webmanifest` with `no-cache`, and gzips the manifest. Store `fetchRecipe`/`fetchPlan` treat 404 as "not found" (empty state) vs. error; `fetchActivePlan` now toggles `loading` for skeletons. Cooking step "done" state is ephemeral (component-local, not persisted).

---
## Project Notes
- Use descriptive variable and function names
- Always ensure type safety
- Avoid hard coded values
- ensure naming is clear 
- Test your changes
- never assume, ask for clarity.