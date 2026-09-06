# RecipeApp

A mobile-first web app for storing recipes, building meal plans, and generating shopping lists. It emphasises three things: a clean step-by-step cooking view, meal-planning that turns straight into a grouped shopping list, and importing recipes straight from a URL using a locally-run LLM (no cloud AI calls, no per-request API cost).

**Status:** v1 feature-complete (Phase 8 of the build — see [SPEC.md](SPEC.md) and [CLAUDE.md](CLAUDE.md) for full history and conventions).

## Features

- **Recipe management** — create, edit, and browse recipes with ingredient lists and numbered steps; portion scaling (½× / 1× / 2×) applied at display time only.
- **Recipe scraping** — paste a URL and a local LLM extracts the name, ingredients, and steps into a preview you can edit before saving. Ingredients are matched against your catalogue automatically.
- **Meal planning** — build a plan from your recipe collection, assign dates and portion sizes, and see waste-reduction suggestions (recipes that use up a leftover partial ingredient).
- **Shopping lists** — auto-generated from the active meal plan, grouped by category, with unit consolidation and support for custom (non-recipe) items.
- **Cooking mode** — full-screen, large-type, step-by-step view with screen wake-lock so your phone doesn't sleep mid-recipe.
- **PWA** — installable, with offline caching of recipes you've already viewed.

Food waste *tracking* (as opposed to the reduction suggestions above) is specified but deferred — see [specs/phase-6-food-waste-tracking.md](specs/phase-6-food-waste-tracking.md).

## Tech stack

| Layer | Choice |
|---|---|
| Frontend | Vue 3 (Composition API, `<script setup>`) + Vite + Pinia + Vue Router + Vuetify 3 |
| Backend | .NET 10 Minimal API |
| ORM | EF Core 10 + Npgsql |
| Database | PostgreSQL 16 |
| Recipe scraping | AngleSharp (HTML parsing) + LLamaSharp (local LLM inference, GGUF models) |
| Validation | FluentValidation |

See [CLAUDE.md](CLAUDE.md) for the full repository structure and coding conventions.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) 20+
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for PostgreSQL, and for the optional full-container run)
- A GGUF-format instruction-tuned LLM model (e.g. Qwen2.5-7B-Instruct, Gemma) for recipe scraping — see [LLM model setup](#llm-model-setup) below. The app runs fine without one; only URL scraping is disabled.
- An NVIDIA GPU + CUDA 12 drivers are recommended for fast LLM inference (`LLamaSharp.Backend.Cuda12`). CPU-only inference works but is much slower — set `Llm:Local:GpuLayerCount` to `0` to force it.

## Quick start (Docker Compose)

This runs PostgreSQL, the API, and an Nginx-served production build of the frontend.

```bash
docker compose up -d
```

- Frontend: http://localhost
- API: http://localhost:5000 (health: `/health`, readiness: `/health/ready`, docs: `/scalar/v1`)

To enable recipe scraping in this mode, mount your GGUF model into the `api` container and set `Llm__Local__ModelPath` accordingly (see [docker-compose.yml](docker-compose.yml)).

## Running locally (for development)

**1. Start PostgreSQL**

```bash
docker compose up -d postgres
```

**2. Configure the backend**

Create `backend/RecipeApp.API/appsettings.Development.json` (gitignored — not committed) with at least a connection string:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=recipeapp;Username=recipeapp;Password=devpassword"
  },
  "Llm": {
    "Provider": "Local",
    "Local": {
      "ModelPath": "C:\\path\\to\\your-model.gguf",
      "ContextSize": 8192,
      "GpuLayerCount": 999,
      "ChatTemplate": "chatml"
    }
  }
}
```

`ChatTemplate` should match your model's expected prompt format — `chatml` (Qwen, Mistral, etc.), `llama3`, or `gemma`. Leave `Llm:Local:ModelPath` empty to run without scraping support.

**3. Run the API**

```bash
cd backend/RecipeApp.API
dotnet run
```

Migrations run automatically on startup in Development, along with light sample-data seeding. The API listens on **http://localhost:5197** (see `Properties/launchSettings.json`).

- Scalar API docs: http://localhost:5197/scalar/v1
- Health check: http://localhost:5197/health
- Readiness check: http://localhost:5197/health/ready

**4. Run the frontend**

```bash
cd frontend
npm install
npm run dev
```

→ http://localhost:3000 (already configured via `frontend/.env.development` to call the API at `http://localhost:5197/api/v1`).

## LLM model setup

Recipe scraping and ingredient catalogue seeding require a local GGUF model. Any reasonably capable instruction-tuned chat model works (7B+ parameters recommended for reliable structured extraction):

1. Download a `.gguf` model file (e.g. from Hugging Face — search for `Qwen2.5-7B-Instruct-GGUF` or similar).
2. Set `Llm:Local:ModelPath` to its absolute path in `appsettings.Development.json`.
3. Set `Llm:Local:ChatTemplate` to match the model family (`chatml`, `llama3`, or `gemma`).
4. Set `Llm:Local:GpuLayerCount` to `0` for CPU-only inference, or leave at `999` to offload all layers to a CUDA GPU.

Without a configured model, the app starts normally and logs a warning — every feature except "Import from URL" and catalogue seeding works as usual.

### Optional: seed the ingredient catalogue

Populates the ingredient catalogue with common ingredients using the configured LLM:

```bash
cd backend/RecipeApp.API
dotnet run -- seed-catalogue [count]   # count defaults to 200
```

## Running tests

**Backend** (requires Docker running — tests spin up a real PostgreSQL container via Testcontainers):

```bash
cd backend/RecipeApp.API
dotnet test ../RecipeApp.Tests/RecipeApp.Tests.csproj
```

**Frontend:**

```bash
cd frontend
npm test                 # run once
npm run test:watch       # watch mode
npm run test:coverage    # with coverage report
```

## Further reading

- [SPEC.md](SPEC.md) — full feature specification and data model
- [CLAUDE.md](CLAUDE.md) — repository structure, conventions, and per-phase change log
- [specs/](specs/) — detailed per-phase specification documents
