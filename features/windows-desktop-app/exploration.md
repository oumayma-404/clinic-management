# Exploration Findings — Windows Desktop App (keep web app)

**Date:** 2026-07-07
**Feature goal:** Package the clinic app as installable Windows software for a clinic LAN (several PCs), working offline for core features, with AI + Google Calendar available only when internet is present. **Keep the existing Auth0/cloud web deployment intact** — this is additive (dual-mode), not a replacement.

## Central design implication
"Keep the web app" ⇒ **dual-mode**: existing Auth0/cloud web build keeps working; add a Windows/LAN/offline mode. The natural seam is an **auth-mode switch** (Auth0 vs Local) on both backend and frontend, plus graceful offline degradation for internet features.

## 1. Auth — Backend (Auth0 today)
- JWT bearer configured in `Program.cs:71-103`, keyed on `Auth0:Domain`/`Audience` (Authority = `https://{domain}`). If unset, the whole block is skipped ⇒ **no auth scheme at all** ⇒ `[Authorize]` endpoints break. This `if` is the natural seam for an auth-mode selector.
- `IClinicContext` (`Application/Common/Services/ClinicContext.cs`) reads claims off `HttpContext.User`: `GetUserId()` = `ClaimTypes.NameIdentifier ?? "sub"`; role/clinic/email use Auth0-namespaced claim fallbacks (already forgiving).
- `User` entity PK **IS the Auth0 sub** (string, `User.cs`; `UserConfiguration.cs` HasKey). Lookup `UserRepository.GetByAuth0SubAsync` = `u.Id == sub`. **No password/credential column exists** — local auth must add one (or a separate credential store).
- `Auth0ManagementService` (Auth0 Mgmt API) pushes `app_metadata` (clinic_id/role) — best-effort, already swallows failures & self-skips if creds are placeholders. Behind `IAuth0ManagementService` — swap with a no-op/local impl.
- `RoleAuthorizationHandler` reads role claim with Auth0 fallbacks. Policies defined but **never applied** (no `[Authorize(Policy=)]` anywhere) — roles stored/displayed but not enforced today.
- Business logic re-resolves clinic/role from DB via `sub`, never trusts token claims alone ⇒ a local issuer only strictly needs to emit a stable `sub`.

## 2. Auth — Frontend (Auth0 today)
- `@auth0/nextjs-auth0` used in: `lib/auth0.ts` (server client), `middleware.ts` (session gate + mounts `/auth/*`), `app/api/auth/token/route.ts` (returns access token), `app/layout.tsx` (`Auth0Provider`), `lib/hooks/use-auth-token.ts`, `dashboard-header.tsx`, `login/setup/join` pages.
- Token flow: `client.ts` `getAccessToken()` → `GET /api/auth/token` (server route calls `auth0.getAccessToken()`) → `Authorization: Bearer`. **Two chokepoints for pluggability: `getAccessToken()` in `client.ts` + the `/api/auth/token` route.**
- `middleware.ts`: gates all routes on session; public = `/login`,`/setup`,`/join`,`/_next/*`,`/api/auth/*`. Clinic membership NOT checked here — deferred to client `ClinicGuard` + `useClinicAccess` (backend-driven `hasClinic`, auth-agnostic once a token exists).
- App **requires a Node server** (middleware + `runtime='nodejs'` token route + `output:'standalone'`); cannot be a pure static export. Offline build must replace/short-circuit the middleware gate + token route.

## 3. Config / environment
- Backend: standard `appsettings.json` (committed, real-looking secrets) + `appsettings.Development.json` (logging only) + env vars (double-underscore). No `IOptions` binding — ad-hoc `configuration["..."]`. Only env notion is `ASPNETCORE_ENVIRONMENT` (gates Swagger). **No feature-flag/mode system exists** — this feature introduces the first.
- Presence-based toggles already exist: MinIO registered only if configured; Auth0 only if configured. Natural switch points.
- Frontend: only public var is `NEXT_PUBLIC_API_URL` (fallback `http://localhost:5000/api`), **duplicated inline in 6 files** (`client.ts`, `clinics.ts`, `google-calendar.ts`, `medical-documents.ts`, `patient-files.ts`). Auth0 vars server-only. `web/.env.local` is committed with live secrets.
- `next.config.ts`: `output:'standalone'`, serverActions 2mb, `eslint.ignoreDuringBuilds:true` (TS errors still fail build). No rewrites/proxy — frontend calls API by absolute URL.
- Runtime write-back caution: `GoogleCalendarController` writes RefreshToken back into `appsettings.json` at runtime (bad for read-only install dir).

## 4. Internet-dependent features (need offline degradation)
- **AI chat (HuggingFace)**: `AIController` → `ChatCommandHandler` → `HuggingFaceAIService` (POST router.huggingface.co). Missing key and network drop both collapse to `Result.Failure` → 400 → generic toast. No distinction.
- **Google Calendar**: fires on appointment **update only** (fire-and-forget `Task.Run`, `UpdateAppointmentCommand.cs:216`), NOT on create. Failures swallowed everywhere (appointment flow never breaks). `status` endpoint **misclassifies network-down as healthy** (`catch { tokenValid = true }`).
- **Auth0 mgmt + Auth0 login** are the other outbound calls; login gates the whole app when online-only.
- **No connectivity primitive exists** anywhere (no `navigator.onLine`, no `/health`). Only signal: `client.ts` maps fetch `TypeError` → `ApiError(status:0)` (frontend→API hop only).

## 5. Packaging / deployment (all greenfield for desktop)
- Dockerfiles exist for API (`aspnet:8.0`, framework-dependent, `EXPOSE 5000`) and web (`node:20-alpine`, standalone, `EXPOSE 3000`). `docker-compose.yml` = infra only (postgres 5432, minio 9000/9001) with volumes + healthchecks; API/web run on host.
- **No self-contained publish / RID / single-file** anywhere. Would need `dotnet publish -r win-x64 --self-contained`.
- **No desktop shell** (no Electron/Tauri/Wails). Greenfield.
- `.claude/skills/start-clinic/` (SKILL.md + start.ps1/stop.ps1) is the launch blueprint: Docker → API(:5000, auto-migrate) → web(:3000). An installer's bootstrapper should replicate this order.
- Branch convention `feature/<kebab>`, Conventional Commits. No prior offline/desktop work in git.

## 6. DB / storage for LAN
- Postgres conn `appsettings.json:40-42` (`Host=localhost;...clinic_management;clinic_user/clinic_password`). Migrations auto-apply at startup (`Program.cs:165 context.Database.Migrate()`); fresh DB → full schema, **no seed data** (no `ClinicSeedData.cs`, no `HasData`).
- File storage: **real features use `IFileStorage` = MinIO only** (throws if unconfigured). `LocalFileStorageService`/`IFileStorageService` is registered but **injected nowhere** (dead) — not a working fallback. Offline file storage needs a real `LocalFileStorage : IFileStorage`.
- Hangfire uses the same Postgres DB (`hangfire` schema). Only on-demand PDF job active.
- For LAN: DB/MinIO can stay `localhost` on the server PC (only the API talks to them). **Must change:** CORS `FrontendUrl` (`WithOrigins(...).AllowCredentials()` blocks LAN origins), frontend `NEXT_PUBLIC_API_URL`, and `app.UseHttpsRedirection()` (breaks plain-HTTP LAN clients without a trusted cert).

## 7. Onboarding / first-run (for offline seeding)
- `/setup` (SetupWizard) → `POST /api/clinics` (CreateClinicCommand): creates Clinic (+6-char join `Code`), User (creator's role = chosen role, **never admin**), optional Doctor. `/join` (JoinWizard) → `POST /api/clinics/join` (JoinClinicCommand): looks up clinic by `Code`, attaches User.
- `hasClinic` = whether a `User` row exists for the sub (`GetUserStatusQuery`) — pure DB check, not a claim.
- Roles: free-text `User.Role` (doctor/secretary/admin); admin never assigned by any flow; policies unused.
- **No invitation system** — second users join via shared clinic `Code`.
- Offline seeding = insert a Clinic (with Code) + a User whose Id = locally-minted sub; that alone makes `hasClinic=true`. Create/join already work without a functioning Auth0 Mgmt API.

## 8. Spec conventions to match
- `features/<kebab>/spec.md` exists for `stock-persistence` and `live-dashboard`. House style: `# Feature Specification:`, metadata block (Status/Type/Created/Scope/Feature), `## Overview`, `## What Changes` (file paths, bolded behaviors), `## Acceptance Criteria` (AC-1..n), `## API Contract`, `## Data/Schema Changes`, `## Out of Scope`, `## Edge Cases (Critical only)`. Companion `progress.md` during impl.
- No prior offline/desktop/LAN docs — first of its kind.
