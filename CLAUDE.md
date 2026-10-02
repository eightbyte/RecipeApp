# RecipeApp

Mobile-first web app for storing recipes, building meal plans, and generating shopping lists.

**Status:** v1 feature-complete. Phases 1–9 done (Phase 9, the seeded USDA MyPlate library of
1,123 recipes, closed 2026-09-29). No phase is in progress.

## Where things are documented

| Need | Read |
|---|---|
| Feature requirements, data model | `SPEC.md` |
| A phase's design and results | `specs/phase-*.md` (the Phase 9 record is `specs/phase-9-seed-recipe-library.md` §21–25) |
| Backend conventions, CLI, config | `backend/CLAUDE.md` |
| Frontend conventions | `frontend/CLAUDE.md` |
| Seed-import pipeline | `backend/RecipeApp.API/Services/Seeding/CLAUDE.md` |
| Local LLM layer | `backend/RecipeApp.API/Services/Llm/CLAUDE.md` |
| Units, amounts, density, portion scaling | `.claude/rules/measurements.md` |
| Ingredient categories / aisle order | `.claude/rules/ingredient-categories.md` |
| Past per-phase implementation notes | `docs/history/phase-notes.md` (archive; read only when history matters) |

New phase specs go in `specs/phase-N-<name>.md`. Record a phase's results there, not in a
`CLAUDE.md`. A `CLAUDE.md` holds only rules that still apply to future work.

## Repository layout

```
backend/RecipeApp.API/     .NET 10 Minimal API (Endpoints, Services, Models, DTOs, Validators, Data)
backend/RecipeApp.Tests/   xunit.v3 + Testcontainers
frontend/                  Vue 3 SPA (Vite, Pinia, Vuetify)
specs/                     Phase specifications and results
docs/                      Reference material and history
tools/seed-curator/        Throwaway Node tool used once to curate the Phase 9 harvest
docker-compose.yml         Local Postgres (+ full stack)
global.json                Opts `dotnet test` into Microsoft.Testing.Platform
```

## Stack

Vue 3 (`<script setup>`) + Vite · Pinia · Vue Router 4 · Vuetify 3 + Bootstrap 5 utilities · Axios
— .NET 10 Minimal API · EF Core 10 + Npgsql · FluentValidation 12 · PostgreSQL 16 ·
LLamaSharp 0.27.0 (CUDA12, local GGUF model).

## Running locally

```bash
docker compose up -d postgres
cd backend/RecipeApp.API && dotnet run   # http://localhost:5197 (5000 under docker compose); auto-migrates + seeds in Development
cd frontend && npm run dev               # http://localhost:3000
```

API docs `/scalar/v1`, health `/health`, readiness `/health/ready`.

## Tests

```bash
cd backend && dotnet test RecipeApp.Tests/RecipeApp.Tests.csproj   # needs Docker running
cd frontend && npm test
```

## Git workflow

- `master` (releases) ← `develop` (integration) ← `phase-N-*` (feature branches off `develop`).
- `master` only receives merges from `develop` at release time. Never merge feature work directly
  into `master`, and never merge `master` back down as a routine flow.

## Project rules

- Use descriptive, clear names. Always ensure type safety. Avoid hard-coded values.
- Test your changes.
- Never assume. Ask for clarity.
