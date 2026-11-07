# RecipeApp

A mobile-first web app for storing recipes, building meal plans, and generating shopping lists. It emphasises three things: a clean step-by-step cooking view, meal planning that turns straight into a grouped shopping list, and importing recipes from a URL using a locally run LLM (no cloud AI calls, no per-request cost).

**Status:** v1 is feature-complete and self-hosted. A push to `master` runs the tests, publishes Docker images, and updates the host PC within about five minutes. The app is reached from phones over [Tailscale](https://tailscale.com/), with nothing exposed to the public internet. A fresh install starts with a curated ingredient catalogue and 12 sample recipes.

## Features

- **Recipe management** — create, edit, and browse recipes with ingredient lists and numbered steps. Batch Size scaling (½× / 1× / 2× the whole recipe) is applied at display time only.
- **Recipe scraping** — paste a URL and a local LLM, running in the Model Server, extracts the name, ingredients, and steps into a preview you can edit before saving. Ingredients are matched against your catalogue automatically.
- **Meal planning** — build a plan from your recipe collection, assign each Planned Meal a date and a Batch Size, and see waste-reduction suggestions (recipes that use up a leftover partial ingredient).
- **Shopping lists** — generated from the active meal plan, grouped by category in aisle order, with unit consolidation and support for custom (non-recipe) items.
- **Cooking mode** — full-screen, large-type, step-by-step view with a screen wake-lock so your phone doesn't sleep mid-recipe.
- **PWA** — installable, with offline caching of recipes you've already viewed.

Food waste *tracking* (as opposed to the reduction suggestions above) is not built.

## Tech stack

| Layer | Choice |
|---|---|
| Frontend | Vue 3 (Composition API, `<script setup>`) + Vite + Pinia + Vue Router + Vuetify 3 |
| Backend | .NET 10 Minimal API |
| ORM / database | EF Core 10 + Npgsql, PostgreSQL 16 |
| Recipe scraping | AngleSharp (HTML parsing), Playwright Chromium (render fallback), llama.cpp `llama-server` (local GGUF model, called over HTTP) |
| Validation | FluentValidation |
| Hosting | Docker Compose on a Windows PC, images from GitHub Container Registry, access over Tailscale |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download). `global.json` pins the version.
- [Node.js](https://nodejs.org/) 24. `frontend/.nvmrc` holds the version, which must match `frontend/Dockerfile`.
- [Docker Desktop](https://www.docker.com/products/docker-desktop/), for PostgreSQL, the backend tests, and the optional full-container run.
- Optional, for URL scraping only: a GGUF instruction-tuned chat model and an NVIDIA GPU with CUDA 12 drivers, to run the [Model Server](#model-server). The app runs without either. Only "Import from URL" is unavailable.

## Running locally (development)

**1. Start PostgreSQL**

```bash
docker compose up -d postgres
```

**2. Configure the backend**

Create `backend/RecipeApp.API/appsettings.Development.json`. It is gitignored. The connection string matches the password in `docker-compose.yml`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=recipeapp;Username=recipeapp;Password=devpassword"
  },
  "Llm": {
    "ModelServer": {
      "BaseUrl": "http://localhost:9931"
    }
  }
}
```

Leave `Llm:ModelServer:BaseUrl` empty (the default) to run without the [Model Server](#model-server).

**3. Run the API**

```bash
cd backend/RecipeApp.API
dotnet run
```

The API listens on **http://localhost:5197**. On every start it applies migrations, and an empty database also receives the [starter data](#starter-data).

- API docs (Scalar): http://localhost:5197/scalar/v1
- Health: http://localhost:5197/health, readiness: http://localhost:5197/health/ready

**4. Run the frontend**

```bash
cd frontend
npm install
npm run dev
```

Open http://localhost:3000. The committed `frontend/.env.development` points it at the API on port 5197.

## Running everything in Docker

```bash
docker compose up -d
```

This builds and starts PostgreSQL, the API and the nginx-served frontend. The app is at http://localhost and the API at http://localhost:5000. To enable scraping in this mode, set `Llm__ModelServer__BaseUrl` on the `api` service to `http://host.docker.internal:9931` and start the [Model Server](#model-server).

## Model Server

Recipe scraping runs on the **Model Server**: llama.cpp's `llama-server`, a separate process you start when you want to import from a URL and stop when you're done. Any capable instruction-tuned chat model in GGUF format works. The project has been tuned against Qwen3.5-9B (Q6_K), and 7B+ parameters are recommended for reliable structured extraction.

1. Download a `.gguf` model file, for example from Hugging Face.
2. Copy `tools/llm-server/settings.example.psd1` to `settings.psd1` and set `ModelPath` to the file's absolute path.
3. Run `powershell -File tools\llm-server\start.ps1`. The first run downloads the pinned llama.cpp release.
4. Run `powershell -File tools\llm-server\stop.ps1` when you're done, to free the GPU.

The URL sheet shows whether the Model Server is ready. Without it every feature except "Import from URL" works. [tools/llm-server/README.md](tools/llm-server/README.md) covers the settings, running it on another machine and running it by hand on another OS.

## Starter data

The first time the API starts against an empty database it loads:

- **An ingredient catalogue** of about 640 common ingredients, with shopping categories, default units, alternative names and the densities used to convert cups to grams.
- **12 sample recipes**, written for this project, so there is something to cook, plan and shop from straight away.

Each step runs once per database. Deleting the sample recipes does not bring them back, and a database that already has ingredients or recipes is left as it is. No LLM or network access is needed.

Other data commands, run from `backend/RecipeApp.API`:

```bash
dotnet run -- import-catalogue                                        # Re-apply the shipped catalogue (fills gaps, keeps your edits)
dotnet run -- import-recipes <pack.json> [--create-missing] [--force] # Import a recipe pack
```

A **recipe pack** is a JSON file of recipes, described in [docs/recipe-packs.md](docs/recipe-packs.md). [sample-recipes.json](backend/RecipeApp.API/seed-data/sample-recipes.json) is an example. [backend/CLAUDE.md](backend/CLAUDE.md) lists every CLI command and flag.

A library of USDA MyPlate recipes can also be imported for personal use. It is not distributed, because most of its recipes are not public domain. See [Services/Seeding/CLAUDE.md](backend/RecipeApp.API/Services/Seeding/CLAUDE.md) if you have your own local copy.

## Running tests

**Backend** (Docker must be running, because the tests start a real PostgreSQL container through Testcontainers):

```bash
cd backend
dotnet test RecipeApp.Tests/RecipeApp.Tests.csproj
```

**Frontend:**

```bash
cd frontend
npm test                 # run once
npm run test:watch       # watch mode
npm run test:coverage    # with coverage report
```

**Deployment updater** (Pester 3.4, which ships with Windows):

```powershell
Invoke-Pester deploy\tests
```

**Model Server scripts** (Pester 3.4):

```powershell
Invoke-Pester tools\llm-server\tests
```

## Deployment

`master` is the release branch. Pushing to it runs both test suites in GitHub Actions and, if they pass, publishes the API and frontend images to GHCR. The host PC pulls `latest` every five minutes, dumps the database before any API update, and restarts what changed. The app is served only over Tailscale, so it needs no authentication.

[docs/deployment.md](docs/deployment.md) covers setting up a host, the Tailscale and phone setup, rollback and restore, and running production beside the development stack on one machine.

## Repository guide

- [CLAUDE.md](CLAUDE.md) — repository layout and conventions, with [backend/CLAUDE.md](backend/CLAUDE.md), [frontend/CLAUDE.md](frontend/CLAUDE.md) and [.claude/rules/](.claude/rules/)
- [CONTEXT.md](CONTEXT.md) — the domain vocabulary
- [docs/](docs/) — deployment, recipe packs, architecture decision records and archived implementation notes
