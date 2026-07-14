# Feature Specification: Cloud Deployment (Cloud mode + Auth0, single VPS)

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-12
**Scope:** Full (infra/config — Docker, reverse proxy, env, backups, docs; near-zero source change)
**Feature:** Package the existing app (Cloud mode + Auth0) to run on one Linux VPS via docker-compose behind Caddy (automatic Let's Encrypt TLS), so the doctor's laptop and the secretary's Android tablet both use it as a normal HTTPS website — removing the "no always-on machine at the clinic / laptop goes home / doctor needs access from home" problem.

## Overview
The always-on host moves off any clinic device and onto a small internet-reachable server. Every device is just a browser at `https://<domain>`. This uses the app's existing **Cloud mode** (Auth0 + MinIO + Postgres) — no rewrite; the work is a production docker-compose, a Caddy reverse proxy, secret externalization/rotation, and automated off-server backups. Provisioning the VPS/DNS/Auth0-prod and running the stack is operator work, guided by a new `DEPLOY.md`; this feature produces the artifacts and hardening in the repo.

## What Changes
- **Production compose** running `api` + `web` + `postgres` + `minio` + `caddy` on an internal Docker network; **only Caddy publishes ports (80/443)**. Postgres, MinIO, API, and web ports are **not** published to the host.
- **Caddy reverse proxy**, single domain, auto Let's Encrypt TLS: `/api/*` → `api:5000`, everything else → `web:3000` (same-origin front door; Cloud mode installs no YARP, so Caddy plays that role).
- **Web image build arg**: `NEXT_PUBLIC_API_URL` becomes a Docker build arg defaulting to the relative `/api` (baked at build time in Next) — no server IP/domain baked into the image.
- **Secret externalization**: remove real values from tracked `appsettings.json` and `docker-compose.yml`; supply every secret via a **gitignored** env file consumed by compose (ASP.NET `Section__Key` overrides); commit a `.env.example` listing all required keys with placeholders.
- **Rotate** every secret that was ever committed (Google client-secret + refresh token, HuggingFace key, Auth0 secret, DB password) and **replace default credentials** (`clinic_password`, `minioadmin`) with strong env-supplied values.
- **Automated off-server backup**: a nightly scheduled job runs `pg_dump` (custom format) + a MinIO data archive and uploads both to an operator-configured off-server destination; also runnable on demand.
- **`DEPLOY.md`** runbook: VPS prep, DNS A record, `.env` fill-in, Auth0 production callback/logout URLs (per `AUTH0_SETUP.md`), bring-up, backup/restore, and secret-rotation checklist.

## Acceptance Criteria
- **AC-1:** `docker compose -f <prod-compose> up -d` starts api, web, postgres, minio, caddy; from outside the host only **80/443** answer — 5432, 9000/9001, 5000, 3000 are unreachable from the LAN/internet.
- **AC-2:** `https://<domain>` serves the app over a valid Let's Encrypt certificate; a laptop browser and an Android tablet browser both load it with **no cert warning and no per-device setup** (no CA import, no IP).
- **AC-3:** The frontend calls the API **same-origin** at `https://<domain>/api`; the built web image contains no hardcoded server IP or domain.
- **AC-4:** No real secret value remains in any tracked file (`appsettings.json`, `docker-compose*.yml`); all secrets load from the gitignored env file, and `.env.example` enumerates every required key with placeholder values.
- **AC-5:** Postgres and MinIO run with non-default, env-supplied credentials (never `clinic_password` / `minioadmin`) in the production compose.
- **AC-6:** Auth0 login works end-to-end on the production domain (callback/logout URLs configured); an authenticated user reaches the dashboard, and both devices can log in.
- **AC-7:** The backup job produces a `pg_restore`-able DB dump + a MinIO data archive and uploads them off-server; a manual run succeeds and a test restore of the dump loads the schema+data.
- **AC-8:** On a fresh database, the API container auto-applies EF migrations at startup and comes up ready (empty DB → usable app after Auth0 setup).

## Data / Config Changes
- No schema changes. Config only: secrets move from tracked files → env (`ConnectionStrings__DefaultConnection`, `GoogleCalendar__ClientSecret`, `GoogleCalendar__RefreshToken`, `HuggingFace__ApiKey`, `Auth0__*`, `MinIO__AccessKey`/`SecretKey`, Postgres/MinIO container creds). `FrontendUrl` set to `https://<domain>` (OAuth success redirect). `Auth:Mode` stays `Cloud`.

## Out of Scope
- Local/offline (Phase 1–5) packaging — stays as-is for genuinely offline clinics.
- Migration to managed Postgres / managed S3 (MinIO stays containerized).
- CI/CD pipelines.
- **Git-history scrubbing** of the already-committed secrets — rotation is the fix here; a history rewrite is a separate optional follow-up.
- Actually buying/provisioning the VPS, DNS, and Auth0 production tenant — operator follows `DEPLOY.md` (I can't access their server).

## Edge Cases (critical only)
- **Next bakes `NEXT_PUBLIC_API_URL` at build, not runtime** — it must be a build arg; the relative `/api` default keeps one image usable on any domain and avoids CORS (browser → API is same-origin).
- **ACME cert issuance needs public reachability** — the domain's A record must point at the VPS and 80/443 must be open before Caddy can obtain the certificate.
- **Same-origin means no CORS is exercised by the browser**, but `FrontendUrl` must still equal the domain for the Auth0 post-login redirect to land correctly.
