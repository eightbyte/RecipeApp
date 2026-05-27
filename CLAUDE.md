# RecipeApp — Project Conventions

## Project overview
Mobile-first web app for storing recipes, building meal plans, and generating shopping lists.

- **Spec:** `SPEC.md` — read this for feature requirements and data model definitions.
- **Current phase:** Phase 1 complete (infrastructure scaffold). Working on Phase 2 (Recipe CRUD).

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
│       │   └── layout/      AppTopBar.vue, AppBottomNav.vue
│       ├── plugins/         vuetify.js
│       ├── router/          index.js — all routes defined here
│       ├── services/        api.js — Axios instance
│       ├── stores/          Pinia stores (one file per domain)
│       └── views/           Top-level page components
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
| Mapping       | AutoMapper                            | Profiles in `Profiles/` directory              |
| Database      | PostgreSQL 16                         | All timestamps stored as UTC                   |

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

## Notes for future phases
- Phase 2 adds `DTOs/`, `Services/`, and `Profiles/` directories to the backend.
- AutoMapper `AddAutoMapper(typeof(Program).Assembly)` will discover profiles automatically.
- FluentValidation validators are registered automatically via `AddValidatorsFromAssemblyContaining<Program>()`.
- The `uploads/images/` directory is served as static files and gitignored. Mount as a Docker volume in production.
