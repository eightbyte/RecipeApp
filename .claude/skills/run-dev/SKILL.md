---
name: run-dev
description: Run RecipeApp locally on a clone of the dev database, without the GPU, and screenshot routes. Use when starting the API or Vite, checking a change in the real app, or capturing a screenshot.
---

# Run the dev stack

Run each command in PowerShell from the repo root. Docker commands name `-p recipeapp` so they
reach only the dev project.

## 1. Postgres

```powershell
docker compose -p recipeapp up -d --no-recreate --wait postgres
```

Done when it prints `Container recipeapp-postgres Healthy`. `--no-recreate` keeps a running
container as it is. Production's updater re-pulls `postgres:16-alpine`, and a plain `up` would then
restart the dev database on the new image.

## 2. Clone the dev database

```powershell
docker compose -p recipeapp exec -T postgres psql -U recipeapp -d postgres -v ON_ERROR_STOP=1 -c "DROP DATABASE IF EXISTS recipeapp_run WITH (FORCE)" -c "CREATE DATABASE recipeapp_run TEMPLATE recipeapp"
```

Done on `CREATE DATABASE`. The **clone** takes what a run writes: creating a meal plan closes the
active one, and startup applies new migrations. `source database "recipeapp" is being accessed by
other users` means a process still holds a connection to `recipeapp`. Stop it and rerun.

## 3. API

Start it as a background task:

```powershell
Set-Location backend/RecipeApp.API; dotnet run --launch-profile http -e "Llm__Local__ModelPath=" -e "ConnectionStrings__DefaultConnection=Host=localhost;Port=5432;Database=recipeapp_run;Username=recipeapp;Password=devpassword"
```

Done when `curl.exe -s -o NUL -w "%{http_code}" http://localhost:5197/health/ready` prints `200`
and the log shows `Hosting environment: Development` and `LLM:Local:ModelPath is not configured`.

Every part of the command is load-bearing:

- `--launch-profile http` sets `ASPNETCORE_ENVIRONMENT=Development`, which loads
  `appsettings.Development.json` and its database password. With no profile the environment is
  Production, `appsettings.json` supplies `Password=CHANGE_ME`, and startup dies on
  `28P01: password authentication failed`. `dotnet run --environment` sets an environment
  variable, so it leaves the hosting environment unset.
- `-e "Llm__Local__ModelPath="` delivers an empty model path, so the GPU stays idle. It has to go
  through `-e`: PowerShell's `$env:Llm__Local__ModelPath = ""` deletes the variable, and the model
  path in `appsettings.Development.json` then loads the GPU.
- The connection string points at the clone. Image uploads still land in the shared
  `backend/RecipeApp.API/uploads/images/`.

## 4. Vite

Start it as a background task:

```powershell
Set-Location frontend; npm run dev -- --strictPort
```

Done when it prints `Local:   http://localhost:3000/`. `--strictPort` fails on a taken port
instead of moving to 3001. `.env.development` points the SPA at the API on 5197.

## 5. Screenshot a route

The Playwright CLI ships in the API build output, because the scraper references
`Microsoft.Playwright`. Step 3 builds it.

```powershell
$driver = "./backend/RecipeApp.API/bin/Debug/net10.0/.playwright"
& "$driver/node/win32_x64/node.exe" "$driver/package/cli.js" screenshot --viewport-size "390,844" --wait-for-selector ".v-main" --wait-for-timeout 1500 http://localhost:3000/recipes <scratchpad>/recipes.png
```

Done when you have opened the PNG with Read and it shows the route. Paths come from
`frontend/src/router/index.js`; 390×844 is a phone, and the app is mobile-first.

- **Blank image:** Vite reloaded the page mid-capture. The first visit to a route after Vite
  rebuilds its dependency cache optimizes that route's imports and reloads, and the Vite log says
  `optimized dependencies changed. reloading`. Take the screenshot again.
- **`Executable doesn't exist at ...ms-playwright\chromium_headless_shell-NNNN`:** the
  `Microsoft.Playwright` version moved past the installed Chromium. Ask the user before running
  `& "$driver/node/win32_x64/node.exe" "$driver/package/cli.js" install chromium`, which downloads
  about 300 MB.

## 6. Stop

Stop the API and Vite background tasks with TaskStop, which ends `dotnet run` and `npm` together
with their child processes. Leave Postgres running; step 2 replaces the clone next time.
