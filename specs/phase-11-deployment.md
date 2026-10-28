# Phase 11 — Self-Hosted Deployment

**Version:** 0.1
**Date:** 2026-10-23
**Status:** Planned. Not started.
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
| D16 | Backups, uptime monitoring and LLM hosting are out of scope (§8) |

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
| `postgres` | `ports: !reset []`. Password from `${POSTGRES_PASSWORD}` |
| `api` | `image: ghcr.io/eightbyte/recipeapp-api:${RECIPEAPP_IMAGE_TAG:-latest}`. `build: !reset null`. `ports: !reset []`. `ASPNETCORE_ENVIRONMENT: Production`. Connection string built from `.env` values |
| `frontend` | `image: ghcr.io/eightbyte/recipeapp-frontend:${RECIPEAPP_IMAGE_TAG:-latest}`. `build: !reset null`. Ports reset to `127.0.0.1:${RECIPEAPP_HTTP_PORT:-80}:80` |

Binding the frontend to `127.0.0.1` keeps the app off the PC's LAN interface. `tailscale serve`
proxies to localhost, so tailnet access is unaffected. `!reset` needs Docker Compose 2.24 or later,
which current Docker Desktop includes.

The development `docker-compose.yml` is not changed.

### 4.3 Secrets — `.env` / `.env.example`

Commit a `.env.example` at the repo root listing every variable the override reads, with
placeholders. On the PC, copy it to `.env` and fill it in. `.env` is already git-ignored.

| Variable | Purpose |
|---|---|
| `POSTGRES_PASSWORD` | Production database password. Generated once, stored only in `.env` |
| `RECIPEAPP_IMAGE_TAG` | `latest` by default. Set to a commit SHA to pin or roll back (§4.8) |
| `RECIPEAPP_HTTP_PORT` | Host port `tailscale serve` targets. Default `80`. Change it if something on the PC already uses 80 |

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
- The .NET SDK and Node versions come from `global.json` and `frontend/package.json` (`engines`, or a
  `.nvmrc` if that is added), not hard-coded in the workflow.
- Images are built for `linux/amd64` only, to match the PC.

A failed run sends GitHub's default email (D15). A failed test job means nothing is pushed, so the
PC keeps running the last good image.

### 4.5 Updater on the PC — `deploy/update.ps1`

The PC holds a clone of the repo on `master`. Every 5 minutes (D12), a Task Scheduler task running as
the logged-in user does:

1. `git pull --ff-only` so changes to the compose files and the script itself reach the PC.
2. `docker compose -f docker-compose.yml -f docker-compose.prod.yml pull`
3. `docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --remove-orphans`. Compose
   recreates only the containers whose image changed.
4. If a container was recreated, poll `http://127.0.0.1:${RECIPEAPP_HTTP_PORT}/health` until it is
   healthy or a timeout passes.
5. Append one line per run that changed something (timestamp, image digests, health result) to a
   rotating log under `deploy/logs/` (git-ignored).

The script exits quietly if Docker Desktop is not running, which is the case before login (D14).
Old images are cleaned up with `docker image prune -f` after a successful update.

The task is registered by a one-off `deploy/install-update-task.ps1`, so the schedule is in the repo
and not set by hand.

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

## 5. One-time setup checklist (manual)

- [ ] Docker Desktop: WSL2 backend, **Start Docker Desktop when you sign in** turned on.
- [ ] Clone the repo on the PC and check out `master`.
- [ ] Create `.env` from `.env.example` and generate a strong `POSTGRES_PASSWORD`.
- [ ] After the first successful workflow run, set both GHCR packages to **Public** (new packages
      start private) (D10).
- [ ] Tailscale setup (§4.6) and machine share (§4.7).
- [ ] Data migration (§4.9).
- [ ] Register the update task (`deploy/install-update-task.ps1`).
- [ ] Install the PWA on both phones.

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
| Production mode | `/scalar/v1` returns 404. The log shows the LLM-not-loaded warning and no errors |
| Scraping | Importing a recipe from a URL that needs the Chromium fallback succeeds |
| Auto-deploy | A merge to `master` is running on the PC within about 5 minutes after the workflow finishes, and the updater log records it |
| Rollback | Pinning the previous SHA brings the previous version back (§4.8) |
| Reboot | After a reboot and login, the app comes back with no manual steps |

## 7. Implementation stages

1. **Production compose.** `docker-compose.prod.yml` and `.env.example`. Check locally with `build`
   still in place (temporarily) to confirm the port and environment hardening.
2. **CI workflow.** `.github/workflows/deploy.yml`. Merge to `master` through `develop`, check the
   images, make the packages public.
3. **PC bootstrap.** Docker Desktop, clone, `.env`, first `up`, Tailscale + `serve`.
4. **Data migration.** §4.9.
5. **Updater.** `deploy/update.ps1`, `deploy/install-update-task.ps1`, the `deploy/logs/` ignore rule.
6. **Phones.** Machine share, PWA install on both phones.
7. **Verification.** All of §6. Record the results in §10.

## 8. Out of scope

- **Backups.** Nightly `pg_dump` plus an `uploads/` copy to a synced cloud folder was proposed and
  deferred. Until it exists, the only copy of production data is on the PC's Docker volumes.
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

_To be filled in when the phase closes._
