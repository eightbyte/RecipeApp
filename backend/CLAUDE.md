# Backend — .NET 10 Minimal API

Measurement and category rules live in `.claude/rules/` (repo root). Read them before touching
units, amounts, shopping-list consolidation or ingredient categories.

Read `CODING_STANDARDS.md` (repo root) before writing or reviewing code: comments, naming, nullability, hard-coded values, DTOs and mapping.

## Layout (`RecipeApp.API/`)

| Folder | Holds |
|---|---|
| `Endpoints/` | Minimal API endpoint groups, one extension method per area |
| `Filters/` | `ValidationFilter.cs`, which provides `.WithValidation<T>()` |
| `DTOs/<Area>/` | Request/response DTOs. `DTOs/Mappings.cs` holds all mapping |
| `Validators/` | One FluentValidation validator per request DTO. `RecipeLimits.cs` holds the shared length and range limits |
| `Models/` | EF entities, one per file |
| `Enums/` | `MeasurementUnit`, `SizelessUnit`, `IngredientCategory`, `BatchSize`, `RecipeSort`, `SortDirection` (the single source of truth for each) |
| `Services/` | Business logic; `Llm/` Model Server client, `Seeding/` MyPlate import pipeline, `RecipePacks/` recipe-pack import |
| `Data/` | `AppDbContext`, migrations, `FirstRunSeeder` and its ledger keys, catalogue/density seeders |
| `seed-data/` | `ingredient-catalogue.json` and `sample-recipes.json` (committed, shipped artefacts) and the `myplate/` cache (personal use; only `manifest.json` committed) |

## Conventions

- **Endpoints:** extension methods in `Endpoints/` returning `WebApplication`, registered in
  `Program.cs` as `app.MapXxxEndpoints()`. No MVC controllers.
- **Validation:** never hand-write validate-and-return in a handler. Chain
  `.WithValidation<TRequest>()` onto the route. It resolves the scoped `IValidator<TRequest>`,
  short-circuits with an RFC 7807 400, and declares the 400 to OpenAPI. Handlers do not take
  `IValidator<T>`.
- **FluentValidation packages:** `FluentValidation` + `FluentValidation.DependencyInjectionExtensions`
  12.x. Validators are auto-registered (scoped) via `AddValidatorsFromAssemblyContaining<Program>()`.
  **Never add `FluentValidation.AspNetCore`.** It is deprecated and only ever hooked MVC.
- **Service-level writes bypass validation.** `RecipeScrapeService.ConfirmAsync` validates nothing. Any
  caller that is not an endpoint must check against `RecipeLimits`, or it can persist a recipe the
  UI then cannot save.
- **Migrations:** `dotnet ef migrations add <Name> --output-dir Data/Migrations`.
- **Timestamps:** `DateTime.UtcNow` and `TIMESTAMPTZ` only. Never local time.
- Hard deletes. No soft deletes in v1.
- `uploads/images/` is served at `/uploads/images/`. It is gitignored; mount it as a volume in production.

## Tests (`RecipeApp.Tests/`)

- Runs on **Microsoft.Testing.Platform**, enabled by the repo-root `global.json`. xunit.v3 is
  MTP-only. **Do not re-add `Microsoft.NET.Test.Sdk` or `xunit.runner.visualstudio`.**
- Integration tests use Testcontainers (Docker must be running) through `Infrastructure/RecipeAppFactory`
  and `DatabaseFixture`. The suite needs no GPU, no Model Server and no network. Use
  `StubLlmStructuredClient`, `StubModelServerStatusProbe`, `StubHttpClientFactory` and
  `TestHostEnvironment` instead of the real ones.
- One class or method: `dotnet test --project RecipeApp.Tests/RecipeApp.Tests.csproj -- --filter-class "*RecipesEndpointTests"`
  (or `--filter-method "*List_Sort*"`). A filter that matches nothing exits 9 (`--minimum-expected-tests 1`
  in the csproj), so read the exit code, not just `failed: 0`.
- `Conventions/` holds source-tree guards: the DTO suffix rule and the no-citation comment rule.
  A failure there names the rule it enforces.
- Coverage: `dotnet test RecipeApp.Tests/RecipeApp.Tests.csproj --coverage --coverage-output-format cobertura`.

## CLI commands

Run from `RecipeApp.API/`. Each one runs and exits without starting Kestrel.

```bash
dotnet run -- import-catalogue          # Committed catalogue + densities. Idempotent, no LLM/network
dotnet run -- import-catalogue --force  # Re-apply artefact over existing rows (discards hand corrections)
dotnet run -- seed-densities            # Curated densities; only fills nulls
dotnet run -- seed-recipes [flags]      # MyPlate pipeline — see Services/Seeding/CLAUDE.md
dotnet run -- import-recipes <pack.json> [--create-missing] [--force]   # Import a recipe pack
```

`seed-catalogue` is retired. It exits 1 and names its replacement.

`import-recipes` imports a **recipe pack**, the JSON format in `docs/recipe-packs.md`. It migrates
and runs a non-force `import-catalogue` first, so it works on an empty database. A pack is all or nothing, and every error is reported. Unknown ingredients are rejected
unless `--create-missing` adds them as `OTHER`. A recipe whose `sourceUrl` already exists is
skipped unless `--force` replaces it, and a recipe a meal plan uses is never replaced. Exit code
0 on success, 1 on any validation failure. `--force` here never force-imports the catalogue.

**Ordering is load-bearing: catalogue → densities → recipes.** A density needs its ingredient row,
and a recipe's `cup` needs the density. `import-catalogue` and `FirstRunSeeder` both enforce
this order.

## First-run seeding (`FirstRunSeeder`)

Startup migrates and seeds in **every environment except `Testing`**, Production included. The
order is migrate → catalogue → densities → sample recipes (`seed-data/sample-recipes.json`).

- **Each one-time step runs once per database.** A `SeedLedger` row, not the data, records that
  it ran, so deleting the sample recipes does not bring them back. Keys live in `SeedLedgerKeys`.
- **A database that already has data is left alone.** A step with no ledger row whose target
  already has rows (any ingredient, any recipe) records `SkippedExistingData` and inserts nothing.
- **A failed step never stops the app.** It logs, rolls back, writes no ledger row and is retried
  on the next start.
- Catalogue updates do not reach an existing install on their own. `import-catalogue` is the
  upgrade path. The CLI commands neither read nor write the ledger.
- **Only the two shipped artefacts are published.** The csproj removes everything under
  `seed-data/` from Content and adds back those two, because the Web SDK would otherwise publish
  the MyPlate cache. Keep that, and keep `seed-data/myplate/` in `.dockerignore`.

### Clean-start paths

From an empty database (for example `CREATE DATABASE recipeapp_clean;` with
`ConnectionStrings__DefaultConnection` pointed at it):

| Path | Steps | Result |
|---|---|---|
| A. Distributed | Start the app | Curated catalogue, densities, 12 sample recipes |
| B. Personal | Path A, then `dotnet run -- seed-recipes --persist` | Path A plus the 1,123 MyPlate recipes and photos |

**Order matters.** A database that receives MyPlate recipes before its first start records
`sample-recipes` as `SkippedExistingData`, and the samples never appear. The two sets never
collide afterwards: the samples have no `SourceUrl`, so `--persist --force` never replaces one.

## Configuration

`appsettings.Development.json` is gitignored:

| Key | Purpose |
|---|---|
| `ConnectionStrings:DefaultConnection` | PostgreSQL |
| `ImageStorage:BasePath` | Local path for recipe images |
| `Llm:ModelServer:BaseUrl` | The Model Server, `http://localhost:9931` with the start script's defaults. Empty disables Scraping. See `Services/Llm/CLAUDE.md` |
| `Llm:ModelServer:ApiKey` | Bearer key, matching the server's `ApiKey` setting. Empty for a loopback server with no key |
| `Measurement:ResolveCupsOnImport` | Resolve `cup` to grams on import where a density exists (default `true`) |

`RecipeScraping`, `RecipeSeeding` (seed pipeline), `RecipePacks` (sample pack path) and `Llm`
defaults live in the committed `appsettings.json`. Nothing secret goes there.

## Running against a real database

- **Starting the web host migrates and seeds** a fresh database (see First-run seeding).
- **To see a change in the running app, use the `run-dev` skill.** It starts the API on a clone of
  the dev database. Scraping and the seed pipeline's model stages also need the Model Server
  (`tools/llm-server/start.ps1`); everything else runs without it.
- **Databases that predate the curated catalogue hold legacy ingredient rows.** Examples are
  `onions`, `tomato` and `plain flour`. A name outranks an alias, so these rows capture names the
  catalogue meant as aliases, which splits shopping-list lines and hides densities. A non-force
  `import-catalogue` leaves them in place. A fresh database does not have them.
