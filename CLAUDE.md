# RecipeApp

Mobile-first web app for storing recipes, building meal plans, and generating shopping lists.

**Status:** v1 feature-complete. Phases 1–10 done. Phase 9 built a personal-use MyPlate library of
1,123 recipes (closed 2026-09-29). Phase 10 made the app distributable: a curated ingredient
catalogue and 12 original sample recipes seed every fresh database (closed 2026-10-12). Phase 11
(self-hosted deployment, `specs/phase-11-deployment.md`) is in progress.

## Where things are documented

| Need | Read |
|---|---|
| Feature requirements, data model | `SPEC.md` |
| A phase's design and results | `specs/phase-*.md` (the Phase 9 record is `specs/phase-9-seed-recipe-library.md` §21–25) |
| Seed-data licensing, recipe-pack format | `specs/phase-10-first-run-seed.md` (§1, §4.4) |
| Backend conventions, CLI, config | `backend/CLAUDE.md` |
| Frontend conventions | `frontend/CLAUDE.md` |
| First-run seeding, `import-recipes`, clean-start paths | `backend/CLAUDE.md` |
| MyPlate seed-import pipeline (personal use) | `backend/RecipeApp.API/Services/Seeding/CLAUDE.md` |
| Local LLM layer | `backend/RecipeApp.API/Services/Llm/CLAUDE.md` |
| Self-hosted deployment, CI, updater, rollback | `specs/phase-11-deployment.md` (§4, §5) |
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
deploy/                    Self-hosted PC updater (update.ps1) and its Task Scheduler installer
.github/workflows/         deploy.yml: on push to master, test, then push images to GHCR
docker-compose.yml         Local Postgres (+ full stack)
docker-compose.prod.yml    Production override layered on docker-compose.yml (reads .env)
global.json                .NET SDK pin, and opts `dotnet test` into Microsoft.Testing.Platform
```

## Stack

Vue 3 (`<script setup>`) + Vite · Pinia · Vue Router 4 · Vuetify 3 · Axios
— .NET 10 Minimal API · EF Core 10 + Npgsql · FluentValidation 12 · PostgreSQL 16 ·
LLamaSharp 0.27.0 (CUDA12, local GGUF model).

## Running locally

```bash
docker compose up -d postgres
cd backend/RecipeApp.API && dotnet run   # http://localhost:5197 (5000 under docker compose); migrates + seeds a fresh DB on start
cd frontend && npm run dev               # http://localhost:3000
```

API docs `/scalar/v1`, health `/health`, readiness `/health/ready`.

## Tests

```bash
cd backend && dotnet test RecipeApp.Tests/RecipeApp.Tests.csproj   # needs Docker running
cd frontend && npm test
powershell -NoProfile -Command "Invoke-Pester deploy\tests"        # updater decisions; Pester 3.4 syntax
```

CI (`.github/workflows/deploy.yml`) reads the SDK version from `global.json` and the Node version
from `frontend/.nvmrc`. Keep `.nvmrc` in step with the `node:` base image in `frontend/Dockerfile`.

## Git workflow

- `master` (releases) ← `develop` (integration) ← `phase-N-*` (feature branches off `develop`).
- `master` only receives merges from `develop` at release time. Never merge feature work directly
  into `master`, and never merge `master` back down as a routine flow.

## Project rules

- Use descriptive, clear names. Always ensure type safety. Avoid hard-coded values.
- Test your changes.
- Never assume. Ask for clarity.

## Agent skills

### Issue tracker

Issues are tracked in GitHub Issues on `eightbyte/RecipeApp` via the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Default vocabulary: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one root `CONTEXT.md` plus `docs/adr/`. See `docs/agents/domain.md`.
