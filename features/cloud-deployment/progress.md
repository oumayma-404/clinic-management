# Progress: Cloud Deployment (Cloud mode + Auth0, single VPS)

**Started:** 2026-07-12
**Type:** Small
**Branch:** feature/windows-desktop-app (user chose to reuse current branch)

## Status
- [x] Implementation
- [x] Quality checks (infra/config — see below)
- [x] Tests (validation gates — infra/config feature has no xUnit surface; see Test Plan below)

## Test Plan (infra/config feature → AC-to-gate map, per /test-small-feature)
This feature is manifests/config (compose, Caddyfile, backup scripts, env externalization) with
near-zero source change, so the "test pass" is **validation gates**, not test classes. Each AC is
either a *static gate* (scriptable here) or a *runtime AC* needing a live VPS (DEFERRED, with the
exact command to run it).

| AC | Kind | Gate | Result |
|----|------|------|--------|
| AC-1 (only 80/443 published) | Static | Only `caddy` declares `ports:`; postgres/minio/api/web have none | **PASS** |
| AC-2 (valid LE cert, devices load) | Runtime | Caddyfile wiring (ACME email + `{$DOMAIN}` site) present; live issuance needs VPS+DNS | **PASS (static)** / DEFERRED (live) |
| AC-3 (same-origin `/api`, no baked host) | Static | `web/Dockerfile` `ARG NEXT_PUBLIC_API_URL=/api`; `client.ts` localhost fallback is dead once the arg bakes `/api` | **PASS** / runtime bundle-scan DEFERRED |
| AC-4 (no real secret in tracked files; env-only; `.env.example` complete) | Static | 3 old committed secrets absent from tracked files; `deploy/.env` + `rclone/*.conf` gitignored; every `${VAR}` in prod compose enumerated in `.env.example` (0 missing) | **PASS** |
| AC-5 (non-default DB/MinIO creds) | Static | No `clinic_password`/`minioadmin` in prod compose; `.env.example` placeholders differ | **PASS** |
| AC-6 (Auth0 login end-to-end) | Runtime | All `AUTH0_*` wired in compose; callback/logout URLs documented in DEPLOY.md; end-to-end needs live tenant+domain | **PASS (static)** / DEFERRED (live) |
| AC-7 (restorable dump + MinIO archive, off-server; manual run + restore) | Runtime | `backup.sh`/`entrypoint.sh` valid POSIX sh; custom-format `pg_dump` + tar + rclone + fail-loud; live run needs the running stack | **PASS (static)** / DEFERRED (live) |
| AC-8 (fresh DB auto-migrates at startup) | Static | `Program.cs:424` `context.Database.Migrate()` runs unconditionally on startup (the `when` filter only shapes Local error handling) | **PASS** |

### Coverage notes (ACs with no static-only proof)
- **AC-2 / AC-6 / AC-7** are genuine *runtime* ACs — they need a provisioned VPS, live DNS, an Auth0
  production tenant, and a running stack, none of which exist in this session. Their static wiring is
  verified PASS; the live verification is DEFERRED to operator bring-up (commands below), consistent
  with the spec's Out-of-Scope ("Actually buying/provisioning the VPS, DNS, and Auth0 … operator
  follows DEPLOY.md — I can't access their server").
- **`docker compose -f docker-compose.prod.yml config`** was validated **at implementation time**
  (progress.md above records it VALID with Docker present); Docker is **not available in this test
  session**, so it was re-verified indirectly via the `${VAR}`→`.env.example` contract gate (0 missing).

## Tests Run (validation gates)
| Gate | Command | Result |
|------|---------|--------|
| Shell syntax | `sh -n deploy/backup/backup.sh` + `entrypoint.sh` | PASS (both valid) |
| JSON validity | parse `api/.../appsettings.json` | PASS (valid JSON) |
| Env contract (AC-4) | every `${VAR}` in prod compose ∈ `.env.example` | PASS (0 missing, 22 vars) |
| Secret scrub (AC-4) | `git grep` 3 old committed secrets in tracked files | PASS (all absent) |
| Gitignore (AC-4) | `git check-ignore deploy/.env`, `deploy/rclone/rclone.conf` | PASS (both ignored) |
| Default creds (AC-5) | grep prod compose + `.env.example` for `clinic_password`/`minioadmin` | PASS (absent) |
| Migration (AC-8) | `Database.Migrate()` present + unconditional in `Program.cs` | PASS (line 424) |
| Same-origin URL (AC-3) | `web/Dockerfile` ARG default = `/api` | PASS |

### Deferred runtime verifications (run during operator bring-up, per DEPLOY.md §7)
- **AC-1 / AC-2** — from outside the VPS: `nmap -Pn <domain>` → only 80/443 open, `<domain>` loads
  with a valid padlock on laptop + Android tablet.
- **AC-6** — Auth0 login reaches the dashboard from both devices.
- **AC-7** — `docker compose -f docker-compose.prod.yml run --rm backup /usr/local/bin/backup.sh`
  produces the dump + archive and uploads them; then a dry-run `pg_restore` of the dump into a
  throwaway DB loads schema+data.
- **AC-8** — on a fresh DB, `docker compose ... logs -f api` shows migrations applied and the app ready.

## Escalation check
NOT escalated. This is an infra/config feature (compose + reverse proxy + backup + env
externalization), not new C#/TS feature surface — no user flow to E2E, no xUnit test classes.
The small-feature validation-gate path is the correct fit (see /test-small-feature "Config /
infra-only features").

## Quality checks (infra/config feature — dotnet/pnpm replaced by target-system tools)
- `sh -n backup/backup.sh` / `entrypoint.sh` → OK (valid POSIX sh).
- `appsettings.json` → valid JSON (only edit was blanking two secret values).
- `docker compose --env-file .env.example -f docker-compose.prod.yml config` → **VALID** (Docker present).
- Contract check: every `${VAR}` referenced in the prod compose is enumerated in `.env.example`
  (missing set is empty).
- Secret scrub verified: `git grep` finds none of the three old committed secrets in tracked files;
  `web/.env.local` (real Auth0 secrets) confirmed gitignored/untracked; `appsettings.json` is tracked
  so the scrub is effective.
- No C#/TS source logic changed (web/Dockerfile ARG + appsettings value blanking only), so no
  `dotnet build` / `pnpm typecheck` needed.

## Working tree note (start of session)
- Only untracked path at start: `features/cloud-deployment/` (this feature's spec). No unrelated
  uncommitted files. All commits will stage this feature's paths explicitly.

## Design decisions
- **Backup is infra-only (a compose container), not a C# Hangfire job.** The spec is scoped
  "infra/config … near-zero source change". The existing `PgDumpBackupService` (Phase 5) is
  Local-mode-specific (bundled `pg_dump.exe`, copies the local `Files/` folder, admin HTTP endpoint)
  and does not fit a Cloud/Docker deployment. A dedicated `backup` container (pg_dump custom-format +
  tar of the MinIO data volume + `rclone` off-site upload, on a cron schedule and runnable on demand)
  is the idiomatic Docker approach and keeps source untouched.
- **AC-8 already satisfied by existing code** — `Program.cs` runs `context.Database.Migrate()` on
  startup; `MinioFileStorage` auto-creates the bucket. No change needed.
- **appsettings.json secret scrub scope.** Scrubbed the genuinely sensitive committed external-service
  secrets (Google `ClientSecret` + `RefreshToken`, HuggingFace `ApiKey`) to empty strings. Left the
  localhost dev fixtures (`clinic_password`, `minioadmin`, dev connection string / MinIO keys) in the
  *dev* `appsettings.json` + `docker-compose.yml` — they are well-known localhost-only dev defaults,
  the production stack never uses them (every prod value comes from the gitignored `deploy/.env`), and
  removing them breaks `dotnet run`/`docker compose up` local dev. Non-secret identifiers (Auth0
  Domain/Audience, Google ClientId/RedirectUri) also left in place.

## Files Changed
- `deploy/docker-compose.prod.yml` (new) — api + web + postgres + minio + caddy + backup; internal
  network; only caddy publishes 80/443.
- `deploy/Caddyfile` (new) — single-domain reverse proxy, auto Let's Encrypt, `/api/*`→api, else→web.
- `deploy/.env.example` (new) — every required secret/config key with placeholders.
- `deploy/backup/Dockerfile` (new) — postgres:16-alpine + rclone.
- `deploy/backup/entrypoint.sh` (new) — installs cron schedule, runs crond in foreground.
- `deploy/backup/backup.sh` (new) — pg_dump (custom fmt) + MinIO tar + rclone off-site + retention prune.
- `deploy/rclone/.gitkeep` (new) — placeholder for operator's gitignored `rclone.conf`.
- `web/Dockerfile` (edit) — `NEXT_PUBLIC_API_URL` becomes a build `ARG` defaulting to relative `/api`.
- `api/ClinicManagement.API/appsettings.json` (edit) — scrub Google ClientSecret/RefreshToken + HF key.
- `.gitignore` (edit) — ignore `deploy/.env` and `deploy/rclone/*.conf`.
- `DEPLOY.md` (new, repo root) — operator runbook.

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Backup implemented as a compose container, not a Hangfire recurring job | Spec is infra-only/near-zero source change; existing C# backup is Local-mode-only and unfit for Docker/Cloud. Compose container is the idiomatic Docker approach and matches the spec's "nightly scheduled job … also runnable on demand". |

## Significant Deviations
(none)
