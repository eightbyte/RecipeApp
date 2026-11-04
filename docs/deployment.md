# Self-hosted deployment

RecipeApp runs on a Windows PC under Docker Desktop. It is reachable only over Tailscale, and a
push to `master` deploys itself within about five minutes. Nothing is exposed to the public internet,
so the app has no authentication.

```
Phone (Tailscale app)
   │  https://<machine-name>.<tailnet>.ts.net        tailnet only, WireGuard-encrypted
   ▼
Host PC ── tailscale serve (:443, TLS) ──► 127.0.0.1:${RECIPEAPP_HTTP_PORT}
              Docker Desktop (WSL2)
              ├─ frontend  nginx: SPA + proxies /api/, /uploads/, /health
              ├─ api       .NET 10, :8080, internal only
              └─ postgres  :5432, internal only
   ▲
   │  outbound only: git pull + docker pull, every 5 min (Task Scheduler)
GitHub Actions: test → build → push ──► ghcr.io/eightbyte/recipeapp-{api,frontend}
```

The PC never accepts inbound connections from GitHub, and no self-hosted runner is used.

## Files

| File | Role |
|---|---|
| `docker-compose.yml` | Development stack. Publishes Postgres on 5432 and the API on 5000, uses the committed password `devpassword` |
| `docker-compose.prod.yml` | Production override layered on the file above. Reads `.env` |
| `.env` / `.env.example` | Production secrets and settings. `.env` is git-ignored |
| `.github/workflows/deploy.yml` | Runs both test suites on push to `master`, then pushes the two images to GHCR |
| `deploy/update.ps1` | The updater the scheduled task runs |
| `deploy/RecipeAppUpdate.psm1` | The updater's decisions (dump or not, dump name, which dumps to prune). Tested with Pester |
| `deploy/install-update-task.ps1` | Registers the 5-minute Task Scheduler task |
| `frontend/nginx.conf` | Serves the SPA and proxies `/api/`, `/uploads/`, `/health` and `/health/ready` to `api:8080` |

Production commands always name both compose files:

```powershell
docker compose -f docker-compose.yml -f docker-compose.prod.yml <command>
```

## Production override

`docker-compose.prod.yml` makes these changes to the development stack:

- The project is named `recipeapp-prod`, and the fixed `container_name` values are removed. Compose
  names the containers `recipeapp-prod-<service>-1`. The updater and restore steps reach services
  through Compose, never by container name.
- The images come from GHCR (`ghcr.io/eightbyte/recipeapp-api` and `recipeapp-frontend`, tag
  `${RECIPEAPP_IMAGE_TAG}`), never built locally.
- Postgres and the API publish no ports. The frontend binds to `127.0.0.1:${RECIPEAPP_HTTP_PORT}`
  only, so the app is off the LAN. `tailscale serve` proxies to localhost.
- `ASPNETCORE_ENVIRONMENT=Production`, so Scalar and OpenAPI are not served. The database password
  comes from `.env`.
- `!reset` and `!override` need Docker Compose 2.24 or later, which current Docker Desktop includes.

The GHCR images are public. They hold only code that is already in the public repository, so the PC
pulls them without a token. New packages start private, so make both public after the first workflow
run.

## Settings (`.env`)

| Variable | Purpose |
|---|---|
| `POSTGRES_PASSWORD` | Production database password. Generated once. The `postgres_data` volume keeps the password it first started with, so changing it later needs `ALTER USER` inside the database |
| `RECIPEAPP_IMAGE_TAG` | `latest` follows releases. A commit SHA pins or rolls back |
| `RECIPEAPP_HTTP_PORT` | Host port that `tailscale serve` targets. Default `80` |
| `RECIPEAPP_BACKUP_KEEP` | How many pre-update dumps the updater keeps. Default `10` |

## CI (`deploy.yml`)

On every push to `master`: `backend-tests` and `frontend-tests` run in parallel, and `publish-images`
runs only if both pass. Images are `linux/amd64`, tagged with the commit SHA and `latest`, and carry
the OCI labels `org.opencontainers.image.revision` and `org.opencontainers.image.source`. A failed
run emails the owner through GitHub, and nothing is pushed, so the PC keeps the last good image.

The SDK version comes from `global.json` and the Node version from `frontend/.nvmrc`. Keep
`.nvmrc` in step with the `node:` base image in `frontend/Dockerfile`.

## The updater (`deploy/update.ps1`)

The host holds a clone of the repository on `master`. Every five minutes a Task Scheduler task runs
the updater as the logged-in user:

1. `git pull --ff-only`, so changes to the compose files and the script reach the host.
2. `docker compose pull`.
3. **Pre-update dump.** If the pulled API image differs from the running one, the updater dumps the
   database with `pg_dump --format=custom` inside the Postgres container and copies it out to
   `deploy/backups/recipeapp-<UTC timestamp>-<old image short SHA>.dump`. Only an API change
   triggers a dump, because only the API applies migrations. The oldest dumps beyond
   `RECIPEAPP_BACKUP_KEEP` are deleted. A failed dump cancels the update, leaves the old containers
   running and exits non-zero. The next run tries again.
4. `docker compose up -d --remove-orphans`, which recreates only the containers whose image changed.
5. If a container was recreated, it polls `/health/ready` through nginx until it returns 200. That
   endpoint also checks the database, and the API serves it only after startup migrations and
   seeding finish.
6. It appends one line per run that changed something to `deploy/logs/`, and prunes old images.

The updater exits quietly when Docker Desktop is not running. Docker Desktop starts at Windows
login, so after an unattended reboot the app stays down until someone logs in.

The task runs through `conhost.exe --headless` so no console window opens every five minutes. As a
result Task Scheduler's "Last Run Result" always reads 0, and failures are recorded in
`deploy/logs/`. `deploy/backups/` and `deploy/logs/` are git-ignored.

Pre-update dumps sit on the same disk as the Docker volumes. They guard against a bad migration or
release, not against losing the machine.

Run the updater's tests with `powershell -NoProfile -Command "Invoke-Pester deploy\tests"` (Pester
3.4, the version that ships with Windows).

## Setting up a host

1. Install Docker Desktop with the WSL2 backend and turn on **Start Docker Desktop when you sign in**.
2. Clone the repository and check out `master`. Nothing is edited in this clone, or
   `git pull --ff-only` fails.
3. Copy `.env.example` to `.env` and fill it in. `.env.example` explains how to generate the password.
4. Install Tailscale for Windows and sign in. It runs as a service, so it is up before login. In the
   Tailscale admin console, enable **MagicDNS** and **HTTPS certificates**.
5. Restore the data if migrating an existing database (see below) **before** the first full start.
6. Start the stack: `docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d`.
7. Run `tailscale serve --bg <RECIPEAPP_HTTP_PORT>`. The configuration persists across reboots.
8. Register the update task from this clone: `powershell -File deploy\install-update-task.ps1`.
   Re-running it replaces the task. Remove it with `Unregister-ScheduledTask -TaskName RecipeAppUpdate`.
9. On each phone, open the `ts.net` URL and choose **Add to Home Screen**.

The certificate is issued by Let's Encrypt, so the `<machine-name>.<tailnet>.ts.net` name appears in
public Certificate Transparency logs. Only the name is exposed. The app is not reachable from outside
the tailnet. Tailscale must be connected on a phone for the app to load, although pages and images the
PWA has already cached still open offline.

### Second user

Share the host from the Tailscale admin console (**Machines → … → Share**). The recipient installs
Tailscale on their phone, signs in with their own account and accepts the invite. They see only that
machine, not the rest of the tailnet.

### Migrating an existing database

Order matters. If the API starts against an empty database, it seeds the catalogue and sample recipes,
and a restored dump then clashes with them.

1. Take `pg_dump --format=custom` of the source `recipeapp` database (Postgres 16) and archive its
   `uploads/` directory.
2. Start only Postgres: `docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d postgres`.
3. `pg_restore --no-owner --role=recipeapp` into the empty `recipeapp` database. The dump includes the
   EF migration history and the first-run ledger, so the API sees nothing to seed.
4. Copy the uploads into the `recipe_uploads` volume with a throwaway container that mounts the
   volume at `/app/uploads`.
5. Start the full stack.

## Running beside the development stack

Production can run on the development machine, from its own clone.

- Keep the production clone on `master`, outside the development tree (for example
  `C:\deploy\RecipeApp`). The updater fast-forwards whichever clone it sits in, and the development
  tree is on `develop` with local changes.
- Keep `.env` only in the production clone. A production command run from the development tree by
  mistake then fails on the missing `POSTGRES_PASSWORD` before it touches anything.
- Set `RECIPEAPP_HTTP_PORT` to a port the dev stack does not use. The full dev stack publishes 80,
  5000 and 5432, Vite uses 3000, and `dotnet run` uses 5197. `tailscale serve` targets the same port.
- Register the update task from the production clone, so it runs that clone's `update.ps1`.
- Pausing Docker Desktop or letting the machine sleep stops production too.
- Cleaning up after development must not reach production. Do not run `docker compose down -v` in the
  production clone or `docker system prune --volumes`, and do not use Docker Desktop's **Clean / Purge
  data** or **Reset to factory defaults**. Scope every dev Docker command to the `recipeapp` project.

Moving to another machine means migrating from the production stack (a dump and the
`recipeapp-prod_recipe_uploads` volume), not from the development database. The `ts.net` name follows
the machine, so the URL changes unless the old machine is renamed or removed and the new one takes its
Tailscale name. Make the machine share again, and reinstall the PWA on each phone if the URL changed.

## Rollback

1. Find the last good commit SHA (the GitHub Actions run, or the GHCR package page).
2. Set `RECIPEAPP_IMAGE_TAG=<sha>` in `.env`.
3. Run `deploy/update.ps1`, or wait for the next scheduled run.

Set it back to `latest` to follow releases again.

Rolling back an image does **not** undo the EF Core migrations the newer image applied. If the older
image cannot run against the newer schema, also restore the pre-update dump the updater took before
that release:

1. Stop the API: `docker compose -f docker-compose.yml -f docker-compose.prod.yml stop api`.
2. Copy the dump into the Postgres container with `docker compose cp`.
3. Inside the container: `pg_restore --clean --if-exists --no-owner --role=recipeapp -d recipeapp <dump>`.
4. Pin the older SHA, so the API starts on the image that matches the restored schema.

Anything written to the database after that dump is lost.

## Checking a deployment

| Check | How |
|---|---|
| Postgres and the API are not on the host | `Test-NetConnection localhost -Port 5432` and `-Port 5000` both fail |
| The frontend is not on the LAN | Browsing to `http://<host-lan-ip>:<port>` from another device fails |
| Tailnet HTTPS works | The `ts.net` URL loads on a phone with Tailscale on, and fails with it off |
| The health check is real | With the API stopped, `http://127.0.0.1:<port>/health/ready` does not return 200. With it running it returns 200 and `Healthy`, not `index.html` |
| Production mode | `/scalar/v1` on the API returns 404 |
| Auto-deploy | A merge to `master` is running on the host within about five minutes of the workflow finishing, and the updater log records it |
| Pre-update dump | An update that changes the API image leaves a new file in `deploy/backups/` that `pg_restore --list` reads. A frontend-only update leaves none |

## Not covered

- Scheduled or off-machine backups. Losing the host's disk loses the database and the `uploads/`
  images.
- Uptime monitoring.
- The local LLM. The API image ships without a model (`Llm:Local:ModelPath` is empty), so recipe
  scraping from a URL is unavailable in production. Everything else works.
- Authentication. It would be needed before any public exposure.
