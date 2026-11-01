# RecipeApp

Mobile-first web app for storing recipes, building meal plans, and generating shopping lists.

**Status:** v1 feature-complete and self-hosted. A push to `master` tests, builds and publishes
images, and the host PC updates itself over Tailscale. A fresh database is seeded with a curated
ingredient catalogue and 12 original sample recipes. A personal-use library of 1,123 MyPlate
recipes can be imported on top.

## Where things are documented

| Need | Read |
|---|---|
| Domain vocabulary (use these terms) | `CONTEXT.md` |
| Backend conventions, CLI, config | `backend/CLAUDE.md` |
| Frontend conventions | `frontend/CLAUDE.md` |
| First-run seeding, `import-recipes`, clean-start paths | `backend/CLAUDE.md` |
| Recipe-pack file format and import rules | `docs/recipe-packs.md` |
| MyPlate seed-import pipeline and licensing (personal use) | `backend/RecipeApp.API/Services/Seeding/CLAUDE.md` |
| Local LLM layer | `backend/RecipeApp.API/Services/Llm/CLAUDE.md` |
| Self-hosted deployment, CI, updater, rollback | `docs/deployment.md` |
| Units, amounts, density, portion scaling | `.claude/rules/measurements.md` |
| Ingredient categories / aisle order | `.claude/rules/ingredient-categories.md` |
| Architecture decisions | `docs/adr/` |
| Past per-phase implementation notes | `docs/history/phase-notes.md` (archive; read only when history matters) |

A `CLAUDE.md` holds only rules that still apply to future work.

## Repository layout

```
backend/RecipeApp.API/     .NET 10 Minimal API (Endpoints, Services, Models, DTOs, Validators, Data)
backend/RecipeApp.Tests/   xunit.v3 + Testcontainers
frontend/                  Vue 3 SPA (Vite, Pinia, Vuetify)
docs/                      Deployment, recipe packs, ADRs, history
tools/seed-curator/        Throwaway Node tool used once to curate the MyPlate harvest
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

Production may be running on this machine beside the dev stack, from its own clone
(`docs/deployment.md`, "Running beside the development stack"). Its Compose project is
`recipeapp-prod`. Scope every Docker command to the dev project (`recipeapp`) or a scratch project
you name with `-p`, and leave every `recipeapp-prod` container and volume as it is. Machine-wide
cleanup (`docker system prune`, `docker volume prune`) can delete production data, so it is off
limits.

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
- Comments are at most one sentence and say why, not what. Clean code describes itself, and extra
  comments cost context. Never cite a phase, spec section or decision number.
- Test your changes.
- Never assume. Ask for clarity.

## Agent skills

### Issue tracker

Issues are tracked in GitHub Issues on `eightbyte/RecipeApp` via the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Default vocabulary: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one root `CONTEXT.md` plus `docs/adr/`. See `docs/agents/domain.md`.
