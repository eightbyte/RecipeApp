# Phase 11 — Self-Hosted Deployment

**Version:** 0.4
**Date:** 2026-10-31 (0.3: 2026-10-28, 0.2: 2026-10-28, 0.1: 2026-10-23)
**Status:** In progress. The repo work (stages 1, 2 and 5) is built. Stages 3, 4, 6 and 7 are manual steps on the PC.
Until the PC is ready, production runs on the development machine (D18, §4.10).
**Depends on:** Phase 10 (first-run seed), Phase 7 (PWA)
**Leaves alone:** the local LLM layer (Phase 8). It ships disabled and is planned separately.

---

## 1. Why

The app runs only on the development machine, so it cannot be used from a phone away from home.
The goal is to make it reachable from two people's phones, anywhere, for $0, with releases
deployed automatically.

Cloud hosting was considered and rejected for now:

| Option | Why not |
|---|---|
| Google Cloud e2-micro (free) | 1 GB RAM is too small for Postgres + .NET + headless Chromium without swap |
| Oracle Cloud Always Free | The free Arm allowance was reportedly cut in 2026, and idle free VMs can be reclaimed |
| Hetzner-class VPS | About €4–5 a month. The budget is $0 |
| Serverless split (Cloud Run + Neon + object storage) | Images are on local disk, so this needs code changes. The Chromium image makes cold starts slow |

The real obstacle to self-hosting was reaching a home machine from outside the home network.
Tailscale removes it: a private network with no router ports opened and no public exposure.

## 2. Decisions taken

| # | Decision |
|---|---|
| D1 | Host on the owner's always-on work PC (owned by the user, not an employer). Windows 11, Docker Desktop on WSL2, at least 12 GB RAM |
| D2 | Reachable only over Tailscale. Nothing is exposed to the public internet, so the app needs no authentication |
| D3 | HTTPS via `tailscale serve` at `https://<pc-name>.<tailnet>.ts.net`. The PWA needs HTTPS to install on a phone. No domain is bought |
| D4 | The second user reaches the PC through Tailscale **machine sharing**. They are not invited to the whole tailnet |
| D5 | Budget is $0 |
| D6 | The current development database (the 1,123-recipe MyPlate library) and its `uploads/` images are migrated once |
| D7 | The headless-Chromium scrape fallback stays in the API image |
| D8 | Pipeline: GitHub Actions tests, builds and pushes images to GitHub Container Registry (GHCR). The PC pulls them. The PC never accepts inbound connections, and no self-hosted runner is used (the repo is public, and a fork PR could run code on a self-hosted runner) |
| D9 | Deploy only on a push or merge to `master`, after both test suites pass |
| D10 | GHCR images are **public**. They hold only code already in the public repo, no secrets or data, so the PC pulls them without a token |
| D11 | Images are tagged with the commit SHA and `latest`. The PC follows `latest`. Rollback means pinning a SHA in `.env` |
| D12 | The PC checks for new images every 5 minutes |
| D13 | Production hardening: Postgres is not published on the host, `ASPNETCORE_ENVIRONMENT=Production`, and the database password lives in an untracked `.env` |
| D14 | Docker Desktop starts only at Windows login. After an unattended reboot the app stays down until the next login. Accepted |
| D15 | Failure alerting is GitHub's default email when a workflow run fails. No uptime monitoring |
| D16 | Scheduled and off-machine backups, uptime monitoring and LLM hosting are out of scope (§8) |
| D17 | Before the updater recreates the API container, it dumps the database (§4.5). A new API image may apply EF Core migrations, and rolling back the image does not undo them (§4.8), so the dump is the way back. A failed dump cancels the update |
| D18 | Until the always-on PC (D1) is ready, production runs on the development machine beside the dev stack, from its own clone on `master` (§4.10). The production Compose project has its own name and no fixed container names, so the two stacks never share containers or volumes |

## 3. Current state

- `docker-compose.yml` defines `postgres`, `api` and `frontend`. It is a development file: it publishes
  Postgres on 5432 and the API on 5000, sets `ASPNETCORE_ENVIRONMENT: Development`, and commits the
  password `devpassword`.
- `backend/RecipeApp.API/Dockerfile` builds a Production image with Playwright Chromium and its
  shared libraries. `frontend/Dockerfile` builds the SPA and serves it from nginx, which proxies
  `/api/` and `/uploads/` to `api:8080`, so everything is same-origin.
- Uploaded images are stored on disk at `/app/uploads` inside the API container, mounted from the
  `recipe_uploads` volume. Locally there are 1,116 files (101 MB).
- On start, the API migrates the database and runs `FirstRunSeeder` (Phase 10 §4.2). A database
  that already holds recipes records the seed steps as `SkippedExistingData` and gains nothing
  (Phase 10 §4.3).
- With `Llm:Local:ModelPath` empty (the `appsettings.json` default), the API starts and logs that
  the model is not loaded. LLM features are unavailable. Nothing else is affected.
- Scalar and OpenAPI are mapped only in Development, so production serves no API docs.
- There is no `.github/workflows/` directory. CI does not exist yet.
- `.gitignore` already ignores `.env` (and allows `.env.example`).
- The API maps `/health` and `/health/ready` at its root, not under `/api/`. nginx proxies only `/api/`
  and `/uploads/`, so a request for `/health` through the frontend hits the SPA fallback and returns
  `index.html` with status 200, whether or not the API is up.
- `global.json` sets only the test runner. It names no SDK version.
- `frontend/package.json` has no `engines` field and there is no `.nvmrc`. The only statement of the
  Node version is the `node:24-alpine` base image in `frontend/Dockerfile`.
- `backend/RecipeApp.API/.dockerignore` exists, but `frontend/` has none. A local
  `docker compose build frontend` copies the host's `node_modules` over the Linux one that `npm ci`
  installed in the image, so `vite build` loses its native binaries on a Windows host. A clean CI
  checkout has no `node_modules`, so CI is not affected.

## 4. Design

### 4.1 Topology

```
Phone (Tailscale app)
   │  https://<pc-name>.<tailnet>.ts.net        tailnet only, WireGuard-encrypted
   ▼
Work PC ── tailscale serve (:443, TLS) ──► 127.0.0.1:${RECIPEAPP_HTTP_PORT}
              Docker Desktop (WSL2)
              ├─ frontend  nginx: SPA + proxies /api/ and /uploads/
              ├─ api       .NET 10, :8080, internal only
              └─ postgres  :5432, internal only
   ▲
   │  outbound only: git pull + docker pull, every 5 min (Task Scheduler)
GitHub ── Actions: test → build → push ──► ghcr.io/eightbyte/recipeapp-{api,frontend}
```

### 4.2 Production compose override — `docker-compose.prod.yml`

The PC runs `docker compose -f docker-compose.yml -f docker-compose.prod.yml …`. The override:

| Service | Change |
|---|---|
| Project | `name: recipeapp-prod` |
| All three | `container_name: !reset null` |
| `postgres` | `ports: !reset []`. Password from `${POSTGRES_PASSWORD}` |
| `api` | `image: ghcr.io/eightbyte/recipeapp-api:${RECIPEAPP_IMAGE_TAG:-latest}`. `build: !reset null`. `ports: !reset []`. `ASPNETCORE_ENVIRONMENT: Production`. Connection string built from `.env` values |
| `frontend` | `image: ghcr.io/eightbyte/recipeapp-frontend:${RECIPEAPP_IMAGE_TAG:-latest}`. `build: !reset null`. Ports reset to `127.0.0.1:${RECIPEAPP_HTTP_PORT:-80}:80` |

Binding the frontend to `127.0.0.1` keeps the app off the PC's LAN interface. `tailscale serve`
proxies to localhost, so tailnet access is unaffected. `!reset` needs Docker Compose 2.24 or later,
which current Docker Desktop includes.

The project name and the container-name reset let production run on the development machine (D18).
Without the name, Compose names the project after the clone's folder, so a clone in a folder called
`RecipeApp` would share the dev project, `recipeapp`, and take over its containers and database
volume. `docker-compose.yml`'s fixed container names (`recipeapp-postgres` and so on) are unique per
machine, so under any project name production could not start while the dev Postgres runs. With them
reset, Compose names the production containers `recipeapp-prod-<service>-1`. The updater and the
restore steps (§4.8) reach services through Compose, never by container name. `COMPOSE_PROJECT_NAME`
or `-p` still overrides `name:`, which is how a scratch stack is tried beside a real one (§10.1,
§10.2).

The development `docker-compose.yml` is not changed.

### 4.2.1 Image build fixes

These apply to development and production alike.

- **`frontend/nginx.conf`** gains two exact-match locations, `location = /health` and
  `location = /health/ready`, that proxy to `api:8080` with the same headers as `/api/`. Exact matches
  leave every SPA route untouched. The updater (§4.5) can then check the API through the one port it
  can reach.
- **`frontend/.dockerignore`** excludes `node_modules/` and `dist/`, so a local build installs and
  builds inside the image only.

### 4.3 Secrets — `.env` / `.env.example`

Commit a `.env.example` at the repo root listing every variable the override reads, with
placeholders. On the PC, copy it to `.env` and fill it in. `.env` is already git-ignored.

| Variable | Purpose |
|---|---|
| `POSTGRES_PASSWORD` | Production database password. Generated once, stored only in `.env` |
| `RECIPEAPP_IMAGE_TAG` | `latest` by default. Set to a commit SHA to pin or roll back (§4.8) |
| `RECIPEAPP_HTTP_PORT` | Host port `tailscale serve` targets. Default `80`. Change it if something on the PC already uses 80 |
| `RECIPEAPP_BACKUP_KEEP` | How many pre-update dumps the updater keeps (§4.5). Default `10` |

The `postgres_data` volume is initialised with whatever `POSTGRES_PASSWORD` holds on first start.
Changing it later requires `ALTER USER` inside the database, not just editing `.env`.

### 4.4 CI/CD — `.github/workflows/deploy.yml`

Triggered on `push` to `master`.

| Job | Runs | Needs |
|---|---|---|
| `backend-tests` | `dotnet test RecipeApp.Tests/RecipeApp.Tests.csproj` on `ubuntu-latest` (Docker is available for Testcontainers) | — |
| `frontend-tests` | `npm ci` and `npm test` in `frontend/` | — |
| `publish-images` | Log in to GHCR with `GITHUB_TOKEN` (`packages: write`). Build and push both images with `docker/build-push-action`, tagged `${{ github.sha }}` and `latest` | both test jobs |

- The image names, `ghcr.io/eightbyte/recipeapp-api` and `ghcr.io/eightbyte/recipeapp-frontend`, are
  defined once as workflow-level `env` values.
- The .NET SDK and Node versions come from files in the repo, not from the workflow:
  - `global.json` gains `"sdk": { "version": "10.0.300", "rollForward": "latestFeature" }`.
    `actions/setup-dotnet` reads it through `global-json-file`. Local builds honour it too.
  - A new `frontend/.nvmrc` holds `24`. `actions/setup-node` reads it through `node-version-file`.
    The `node:24-alpine` tag in `frontend/Dockerfile` must match. A comment in each file points to
    the other.
- Images are built for `linux/amd64` only, to match the PC.
- Both images carry the OCI labels `org.opencontainers.image.revision` (the commit SHA) and
  `org.opencontainers.image.source` (the repo). The updater reads `revision` to name a dump after
  the image it replaces (§4.5). A container running `latest` records no SHA otherwise.

A failed run sends GitHub's default email (D15). A failed test job means nothing is pushed, so the
PC keeps running the last good image.

### 4.5 Updater on the PC — `deploy/update.ps1`

The PC holds a clone of the repo on `master`. Every 5 minutes (D12), a Task Scheduler task running as
the logged-in user does:

1. `git pull --ff-only` so changes to the compose files and the script itself reach the PC.
2. `docker compose -f docker-compose.yml -f docker-compose.prod.yml pull`
3. **Pre-update dump (D17).** If the API image just pulled differs from the one the running API
   container uses, dump the database before going further:
   - Compare the running container's image ID (`docker inspect <api container> --format '{{.Image}}'`)
     with the pulled image's ID (`docker image inspect <api image ref> --format '{{.Id}}'`). Only an API
     change triggers a dump, because only the API applies migrations. A frontend-only release does not.
   - Skip the dump if no API container or no Postgres container exists yet. That is the first
     bootstrap, when there is nothing to protect.
   - Run `pg_dump --format=custom` **inside** the Postgres container to a file there, then copy it
     out with `docker compose cp`. Do not redirect `pg_dump`'s stdout from PowerShell. Windows
     PowerShell re-encodes native output as text and corrupts a binary dump.
   - Save it as `deploy/backups/recipeapp-<UTC timestamp>-<old image short SHA>.dump`
     (git-ignored), then delete the oldest dumps beyond `RECIPEAPP_BACKUP_KEEP`.
   - If any part of the dump fails, log it, run nothing further and exit non-zero. The old containers
     keep running, and the next scheduled run tries again.
4. `docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --remove-orphans`. Compose
   recreates only the containers whose image changed.
5. If a container was recreated, poll `http://127.0.0.1:${RECIPEAPP_HTTP_PORT}/health/ready` until it
   returns 200 or a timeout passes. nginx proxies this path to the API (§4.2.1). `/health/ready` also
   checks the database, and the API serves it only after startup migrations and seeding finish.
6. Append one line per run that changed something (timestamp, image digests, dump file name if any,
   health result) to a rotating log under `deploy/logs/` (git-ignored).

The script exits quietly if Docker Desktop is not running, which is the case before login (D14).
Old images are cleaned up with `docker image prune -f` after a successful update.

The dumps sit on the same disk as the Docker volumes. They guard against a bad migration or a bad
release, not against losing the PC (§8).

The task is registered by a one-off `deploy/install-update-task.ps1`, so the schedule is in the repo
and not set by hand. It runs as the logged-in user (Interactive logon, no elevation needed) through
`conhost.exe --headless`, so no console window appears every 5 minutes. As a result, Task Scheduler's
"Last Run Result" always reads 0. Failures are recorded in `deploy/logs/`.

The updater's decisions (dump or not, dump name, which dumps to prune) live in
`deploy/RecipeAppUpdate.psm1` and are tested with Pester 3.4, the version inbox with Windows:
`Invoke-Pester deploy\tests`.

### 4.6 Tailscale

On the PC:

1. Install Tailscale for Windows and sign in. It runs as a Windows service, so it is up before login.
2. In the Tailscale admin console, enable **MagicDNS** and **HTTPS certificates**.
3. Run `tailscale serve --bg ${RECIPEAPP_HTTP_PORT}`. The config persists across reboots.
4. Optionally rename the machine to something short (e.g. `recipes`), because it becomes the hostname.

The certificate is issued by Let's Encrypt, so the `<pc-name>.<tailnet>.ts.net` name appears in public
Certificate Transparency logs. Only the name is exposed. The app is not reachable from outside the
tailnet.

### 4.7 Second user (D4)

Share the PC from the admin console (**Machines → … → Share**). The recipient installs Tailscale on
their phone, signs in with their own account and accepts the invite. They see only that machine.

Each phone opens the `ts.net` URL and uses **Add to Home Screen**. Tailscale must be connected on the
phone for the app to load. Pages and images the PWA has already cached still open offline.

### 4.8 Rollback (D11)

1. Find the last good commit SHA (GitHub Actions run, or the GHCR package page).
2. Set `RECIPEAPP_IMAGE_TAG=<sha>` in `.env`.
3. Run `deploy/update.ps1`, or wait for the next scheduled run.

To resume following releases, set it back to `latest`. Rolling back an image does **not** roll back
EF Core migrations the newer image applied. A release that adds a migration that can't be reversed
should say so in its notes.

If the older image can't run against the newer schema, also restore the pre-update dump the updater
took before that release (§4.5):

1. Stop the API: `docker compose -f docker-compose.yml -f docker-compose.prod.yml stop api`.
2. Copy the dump into the Postgres container with `docker compose cp`.
3. `pg_restore --clean --if-exists --no-owner --role=recipeapp -d recipeapp <dump>` inside the
   container.
4. Pin the older SHA (steps 2–3 above) so the API starts on the image that matches the restored
   schema.

Anything written to the database after that dump is lost.

### 4.9 Data migration (D6, one-time)

Order matters. If the API starts against an empty database, `FirstRunSeeder` seeds the catalogue and
12 sample recipes, and the restored dump would then clash with them. So:

1. On the development machine: `pg_dump --format=custom` of the `recipeapp` database (Postgres 16, the
   same major version the PC runs), and an archive of `backend/RecipeApp.API/uploads/`.
2. Copy both to the PC.
3. On the PC, start **only** Postgres: `docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d postgres`.
4. `pg_restore --no-owner --role=recipeapp` into the empty `recipeapp` database. The dump includes
   `__EFMigrationsHistory` and the Phase 10 first-run ledger, so the API sees nothing to seed.
5. Copy the uploads into the `recipe_uploads` volume, using a throwaway container that mounts the volume
   at `/app/uploads`.
6. Start the full stack and check (§6).

If the development database's migration history is behind the image's, the API applies the missing
migrations on first start, as it does in development.

### 4.10 Interim host: the development machine (D18)

Until the always-on PC is ready, production runs on the development machine, beside the dev stack.

- Production runs from its own clone on `master`, outside the development tree (e.g.
  `C:\deploy\RecipeApp`). The updater fast-forwards whichever clone it sits in, and the development
  tree is on `develop` with local changes. Nothing is edited in the production clone, or
  `git pull --ff-only` fails.
- `.env` lives only in the production clone. A production command run from the development tree by
  mistake then fails on the missing `POSTGRES_PASSWORD` before it touches anything.
- `RECIPEAPP_HTTP_PORT` is a port the dev stack does not use. The full dev stack publishes 80, 5000
  and 5432; Vite uses 3000 and `dotnet run` 5197. `tailscale serve` targets the same port.
- The data migration (§4.9) needs no copy between machines. The dump is taken inside the dev Postgres
  container (`recipeapp-postgres`) and copied out with `docker cp`, and the uploads come from
  `backend/RecipeApp.API/uploads/`.
- The update task is registered from the production clone, so it runs that clone's `update.ps1`.
- Pausing Docker Desktop or letting the machine sleep stops production too.
- Cleaning up after development must not reach the production stack: no `docker compose down -v` in
  the production clone, no `docker system prune --volumes`, and no Docker Desktop **Clean / Purge
  data** or **Reset to factory defaults**.

When production moves to the PC:

- Migrate from the production stack (a dump and the `recipeapp-prod_recipe_uploads` volume), not from
  the development database, which no longer matches it.
- The `ts.net` name follows the machine. The URL changes unless this machine is renamed or removed
  and the PC takes its Tailscale name. The machine share (§4.7) is made again for the PC. A PWA
  installed against a URL that changes has to be reinstalled.

## 5. One-time setup checklist (manual)

- [x] Docker Desktop: WSL2 backend, **Start Docker Desktop when you sign in** turned on.
- [ ] Clone the repo on the PC and check out `master`. On the development machine, a separate clone
      outside the development tree (§4.10).
- [x] Create `.env` from `.env.example` and generate a strong `POSTGRES_PASSWORD`.
- [ ] On the development machine: move `.env` into the production clone and set
      `RECIPEAPP_HTTP_PORT` to a port the dev stack does not use (§4.10).
- [x] After the first successful workflow run, set both GHCR packages to **Public** (new packages
      start private) (D10).
- [x] Tailscale install, MagicDNS and HTTPS certificates (§4.6 steps 1–2) and machine share (§4.7).
- [ ] Data migration (§4.9).
- [ ] `tailscale serve --bg <RECIPEAPP_HTTP_PORT>` (§4.6 step 3).
- [ ] Register the update task from the production clone (`deploy/install-update-task.ps1`).
- [ ] Install the PWA on both phones. While production is on the development machine, see §4.10 on
      the URL changing.

## 6. Verification

| Check | How |
|---|---|
| Workflow gates on tests | `publish-images` declares `needs:` on both test jobs. The first failing run on `master` (if one happens) shows it skipped |
| Images are pullable without a token | `docker logout ghcr.io` then `docker pull` both images |
| Postgres and API are not on the host | `Test-NetConnection localhost -Port 5432` and `-Port 5000` both fail |
| Frontend is not on the LAN | Browsing to `http://<pc-lan-ip>` from another device fails |
| Tailnet HTTPS works | The `ts.net` URL loads on a phone with Tailscale on, with a valid certificate |
| Not reachable off-tailnet | Same URL with Tailscale off fails |
| Second user | The shared user loads the app and can't see other tailnet machines |
| Migrated data | Recipe count is 1,123 (+ any added since). A recipe image loads. The startup log shows the seed steps as skipped |
| Production mode | `/scalar/v1` on the API returns 404 (e.g. `wget` from the frontend container to `http://api:8080/scalar/v1`; through nginx the path falls to the SPA). The log shows the LLM-not-loaded warning and no errors other than EF Core's probe of the missing `__EFMigrationsHistory` on an empty database |
| Scraping | Importing a recipe from a URL that needs the Chromium fallback succeeds |
| Health check is real | With the API stopped, `http://127.0.0.1:${RECIPEAPP_HTTP_PORT}/health/ready` does not return 200. With it running, it returns 200 and the text `Healthy`, not `index.html` |
| Local image build | `docker compose build frontend` succeeds on the Windows development machine |
| Auto-deploy | A merge to `master` is running on the PC within about 5 minutes after the workflow finishes, and the updater log records it |
| Pre-update dump | An update that changes the API image leaves a new file in `deploy/backups/`, and `pg_restore --list` reads it. A frontend-only update leaves none. Only `RECIPEAPP_BACKUP_KEEP` dumps remain |
| Failed dump cancels the update | With the dump forced to fail (e.g. Postgres stopped), the run logs the failure, exits non-zero and leaves the running API on its old image |
| Restore | Restoring a dump into a scratch database gives the same recipe count as the source |
| Rollback | Pinning the previous SHA brings the previous version back (§4.8) |
| Reboot | After a reboot and login, the app comes back with no manual steps |

## 7. Implementation stages

1. **Production compose.** `docker-compose.prod.yml`, `.env.example`, and the image build fixes
   (§4.2.1: nginx health locations, `frontend/.dockerignore`). Check locally with `build` still in place
   (temporarily) to confirm the port and environment hardening and the health check.
2. **CI workflow.** The `global.json` SDK pin, `frontend/.nvmrc`, and `.github/workflows/deploy.yml`.
   Merge to `master` through `develop`, check the images, make the packages public.
3. **PC bootstrap.** Docker Desktop, clone, `.env`, first `up`, Tailscale + `serve`.
4. **Data migration.** §4.9.
5. **Updater.** `deploy/update.ps1` (including the pre-update dump), `deploy/install-update-task.ps1`,
   and ignore rules for `deploy/logs/` and `deploy/backups/`.
6. **Phones.** Machine share, PWA install on both phones.
7. **Verification.** All of §6. Record the results in §10.

## 8. Out of scope

- **Scheduled and off-machine backups.** Nightly `pg_dump` plus an `uploads/` copy to a synced cloud
  folder was proposed and deferred. The pre-update dump (D17) is in scope, but it covers the database
  only, runs only when the API image changes, and stays on the same PC. Until a real backup exists,
  losing the PC's disk loses the production data, and `uploads/` has no copy at all.
- **Uptime monitoring.**
- **LLM hosting.** The intended direction is to keep the model on the GPU machine and reach it from the
  API over the tailnet. That needs its own design (remote provider vs. moving the model) in a later
  phase.
- **Authentication.** Not needed while access is tailnet-only (D2). It would be needed before any
  public exposure.
- **Docker as a system service.** Rejected in favour of D14.

## 9. Open questions

None at time of writing.

## 10. Results

§10.1 records the repo work, and §10.2 the change for the interim host. The results of the PC stages
and the full §6 run are added when the phase closes.

### 10.1 Repo work (stages 1, 2, 5), 2026-10-28

Built: `docker-compose.prod.yml`, `.env.example`, the nginx health locations, `frontend/.dockerignore`,
the `global.json` SDK pin, `frontend/.nvmrc`, `.github/workflows/deploy.yml`, `deploy/update.ps1`,
`deploy/RecipeAppUpdate.psm1` (+ Pester tests), `deploy/install-update-task.ps1`, and ignore rules for
`deploy/backups/` and `deploy/logs/`.

Checked on the development machine against a scratch compose project (own project name and volumes,
locally built images, `pull_policy: never`):

| §6 check | Result |
|---|---|
| Postgres and API are not on the host | Pass. Only the frontend is published, on `127.0.0.1` |
| Frontend is not on the LAN | Pass. The LAN IP on the published port does not answer |
| Production mode | Pass. `ASPNETCORE_ENVIRONMENT=Production`, `/scalar/v1` is 404 on the API, LLM-not-loaded warning logged |
| Health check is real | Pass. 200 `Healthy` through nginx; 504 with the API stopped |
| Local image build | Pass. `docker compose build frontend` on the Windows host |
| Pre-update dump | Pass. An API change wrote a dump `pg_restore --list` reads; a frontend-only change wrote none; `RECIPEAPP_BACKUP_KEEP=2` left two |
| Failed dump cancels the update | Pass. With Postgres stopped the run logged the failure, exited 1, and the API kept its old image. The next run, with Postgres back, dumped and updated |
| Restore | Pass. A dump restored into a scratch database gave the same recipe count |

Findings and deviations from the design above:

- `!reset` with a value discards it, so the frontend's port is replaced with `!override`, not `!reset`.
- Windows PowerShell 5.1 strips embedded double quotes from native arguments, so the updater reads
  image labels with `{{json .Config.Labels}}` rather than an `index` template.
- A `pull` also fetches newer `postgres:16-alpine` patch builds, so the updater can recreate Postgres
  on its own. That run takes no dump (only an API change does) and is logged like any other.
- `/health/ready` answers with plain text, so §6 now says `Healthy`, not a JSON body.
- The log records 12-character local image IDs (as `docker images` shows them), not registry digests.
  They are what the updater compares, and they stay available for images that were never pushed.
- `RECIPEAPP_HTTP_PORT` and `RECIPEAPP_BACKUP_KEEP` are read through `docker compose config
  --environment`, so `.env` has one parser. A value that is not a whole number of 1 or more fails the
  run before anything changes. The database name and user come from the compose config too.
- The workflow uses `actions/checkout@v5`, `setup-dotnet@v5` and `setup-node@v5`, the Node 24 majors.
- Not checked here: the workflow on GitHub, GHCR visibility, Tailscale, the data migration, the
  task on the PC, and "exits quietly if Docker Desktop is not running". These belong to stages 2–4, 6, 7.

### 10.2 Interim host on the development machine (D18), 2026-10-31

Found before bootstrapping production beside the dev stack:

- A production clone in a folder named `RecipeApp` would get the dev clone's Compose project name,
  `recipeapp`, so its `up` would take over the dev containers and the dev database volume.
- `docker-compose.yml`'s fixed container names are unique per machine, so production could not start
  while the dev Postgres runs, whatever its project name.
- The full dev stack publishes the frontend on 80, the default `RECIPEAPP_HTTP_PORT`.

`docker-compose.prod.yml` now sets `name: recipeapp-prod` and resets `container_name` on all three
services (§4.2). `.env.example` notes the port choice.

Checked on the development machine, with the dev Postgres (`recipeapp-postgres`) running:

| Check | Result |
|---|---|
| Merged config | Pass. Project `recipeapp-prod`, volumes `recipeapp-prod_postgres_data` and `recipeapp-prod_recipe_uploads`, no fixed container names. The dev config alone is unchanged |
| Runs beside the dev stack | Pass. A scratch stack (`-p recipeapp-prodcheck`, port 8091) pulled both GHCR images while logged out of `ghcr.io`, started as `recipeapp-prodcheck-<service>-1` and answered `Healthy` on `/health/ready`. The dev Postgres container kept its ID and start time |
| Updater | Pass. `update.ps1 -SkipGitPull` against the scratch stack exited 0 with nothing to change |
| Dump commands | Pass. The updater's `compose exec postgres pg_dump` and `compose cp` against the scratch stack gave a dump that `pg_restore --list` reads |
| Pester | Pass. 10 passed, 0 failed |
