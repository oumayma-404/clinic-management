# Implementation Plan: Windows Desktop / Offline-LAN — Phase 1 (Pluggable Auth + Local Accounts)

**Status:** APPROVED
**Challenged:** Yes
**Created:** 2026-07-08
**Spec:** [spec.md](./spec.md) (APPROVED · Challenged) — this plan covers **Phase 1 only** (FR-A + FR-B). Phases 2–5 get their own plans via `/next`.

## Overview

Add a **config-selected authentication mode** to the backend and frontend so the app can run either in **Cloud mode** (existing Auth0 — unchanged) or **Local mode** (offline email + password accounts). Local mode issues its own signed JWT from the API and reuses the existing `Authorization: Bearer` seam, so the rest of the app (clinic scoping, all features) works unchanged.

**Key approved decisions carried into this plan:**
- **Lightweight custom auth** — extend the existing `User` entity with credential fields; the API mints a locally-signed JWT on login. No ASP.NET Core Identity.
- **Bearer-token seam reuse** — local login returns a JWT; frontend attaches it exactly as the Auth0 token today.
- **Single clinic per Local install**; email unique per install; clinic code kept as a light self-registration gate.
- **First-run setup is localhost-only** and creates the clinic + first **admin**.
- **Cloud mode must remain fully working** — every change is additive and mode-gated.

**Mode selection:** backend config key `Auth:Mode = Cloud | Local` (default `Cloud`). The **Next.js server** reads its own `AUTH_MODE` env var so server-side code (`middleware.ts`, the token route) can branch per request without an API round-trip. The **browser client** learns the mode from a **public `GET /api/auth/mode`** endpoint at startup (for rendering the right login UI). The thin WebView2 shell needs no build-time flag.

**Local-mode session mechanism (mirrors the Auth0 cookie+token-route shape):**
- A Next.js **local-login route handler** posts credentials to the .NET `POST /api/auth/login`, receives the JWT, and sets it in an **HttpOnly session cookie**.
- `app/api/auth/token/route.ts` in Local mode reads that cookie and returns the JWT to `getAccessToken()`, which attaches `Authorization: Bearer` exactly as today.
- `middleware.ts` in Local mode gates protected routes on the **presence/validity of that cookie** (redirecting to the local login screen), instead of the Auth0 session. This keeps the client transport identical across modes.

## Files to Modify / Create

### Backend — modify
- `api/ClinicManagement.API/Program.cs` (~71–103) — branch JWT setup on `Auth:Mode`: Cloud = Auth0 authority (as today); Local = validate the app-issued JWT with the per-install signing key.
- `api/ClinicManagement.Domain/Entities/User.cs` — add `PasswordHash`, `IsActive`, `MustChangePassword`, optional `LastLoginAt` + failed-attempt/lockout fields; factory/methods for local users.
- `api/ClinicManagement.Infrastructure/Persistence/Configurations/UserConfiguration.cs` — map new columns; **partial unique index** on `Email` via `.HasIndex(u => u.Email).IsUnique().HasFilter("\"PasswordHash\" IS NOT NULL")` (local accounts are those with a password; Npgsql supports partial indexes). Cloud rows (null email / no password) are excluded and unaffected.
- `api/ClinicManagement.Infrastructure/Repositories/UserRepository.cs` — add `GetByEmailAsync` (local login lookup).
- `api/ClinicManagement.Infrastructure/Extensions.cs` — register local auth services and a **no-op `IAuth0ManagementService`** when `Auth:Mode = Local`.
- `api/ClinicManagement.Application/Common/Services/ClinicContext.cs` — no change expected (already reads `sub`/`role`/`email`/`clinic_id` with fallbacks); verify local JWT emits these claim types.
- `api/ClinicManagement.Application/Features/Clinics/Commands/CreateClinicCommand.cs` — first-run path sets the creator's role to **admin** and stores the password hash (Local mode).
- `api/ClinicManagement.Application/Features/Clinics/Commands/JoinClinicCommand.cs` — capture email + password on self-registration (Local mode).

### Backend — create
- `ILocalAuthService` (Application) + `LocalAuthService` (Infrastructure) — password hashing via `PasswordHasher<T>` (PBKDF2, with format/version metadata + rehash-on-upgrade support) and JWT issuance with the per-install signing key. **New dependency:** add the `Microsoft.Extensions.Identity.Core` package to Infrastructure (the API has JwtBearer but no ASP.NET Identity today). JWT issuance uses `JwtSecurityTokenHandler` (`System.IdentityModel.Tokens.Jwt`, available via the JwtBearer package).
- `AuthController` — `POST /api/auth/login` (email+password → JWT), `GET /api/auth/mode` (public), `POST /api/auth/change-password` (for forced change).
- MediatR: `LoginCommand`, `ChangePasswordCommand`, and user-management commands/queries (`ListUsersQuery`, `ResetUserPasswordCommand`, `SetUserActiveCommand`).
- A **first-run gate** (middleware or endpoint filter) restricting setup to `localhost` and to the "no admin exists yet" state.
- EF migration: additive User columns + filtered unique email index.
- Server-side **admin password-reset utility** (console entry / CLI in the API project) for lockout recovery.

### Frontend — modify
- `web/lib/api/client.ts` — `getAccessToken()` becomes mode-aware: Local mode reads the local JWT (from the token route / local session) instead of Auth0.
- `web/app/api/auth/token/route.ts` — return the local session token in Local mode.
- `web/middleware.ts` — in Local mode, gate on the local session and redirect to the **local login** route instead of `/auth/login`; skip Auth0 `/auth/*` mounting.
- `web/app/layout.tsx` — mount `Auth0Provider` only in Cloud mode; a local auth context in Local mode.
- `web/components/setup-wizard.tsx`, `join-wizard.tsx` — add email + password fields (Local mode).

### Frontend — create
- **Local-login route handler** (Next.js) that calls `POST /api/auth/login` and sets the HttpOnly session cookie (see Local-mode session mechanism above).
- Mode bootstrap for the browser (fetch `GET /api/auth/mode`, expose via a small provider/hook); `AUTH_MODE` env consumed by `middleware.ts`/token route server-side.
- Local **login** screen; **force-password-change** screen.
- **Admin user-management** page (list users, reset password, deactivate/reactivate) under settings.

## Implementation Stories

Vertical slices, strictly ordered. Each keeps **Cloud mode green** (regression check in every story).

### US-1: Fresh Local install → create first admin → log in offline
**Delivers:** On a server in Local mode, the person at the server PC runs first-run setup (localhost-only), creates the clinic + admin (email+password), and can then log in offline from a client and reach the dashboard.
- **Layers:** config (`Auth:Mode`), Domain (User credential fields + migration), Infra (`LocalAuthService`, JWT, no-op Auth0 mgmt), API (`/api/auth/login`, `/api/auth/mode`, first-run localhost gate, admin role on create), Frontend (mode bootstrap, local login screen, mode-aware middleware + `getAccessToken`, setup wizard password fields).
- **Spec ACs:** AC-1.2, AC-1.2a, AC-3.1, AC-3.2, AC-3.3, AC-7.1, AC-7.2, AC-7.3, FR-A1–A5, FR-B1–B3.
- **Notes:** Largest story (auth foundation lives here because login isn't demoable without an account). `/break-plan` should slice it into steps: (1) mode config + JWT validation, (2) User credential schema + migration, (3) login endpoint + `LocalAuthService`, (4) first-run localhost admin creation, (5) frontend mode bootstrap + login + middleware.

### US-2: Staff self-register with clinic code, then log in
**Delivers:** A staff member registers a Local account (email, password, full name, role, clinic code) and logs in.
- **Layers:** API/Application (`JoinClinicCommand` extended with credentials; reject invalid code / duplicate email per install), Frontend (`join-wizard` password fields; registration reachable from login screen).
- **Spec ACs:** AC-4.1–AC-4.5, FR-B4.
- **Depends on:** US-1.

### US-3: Admin manages users
**Delivers:** The admin sees the clinic user list and can reset a password (temp password shown; user forced to change at next login) and deactivate/reactivate users (deactivated users can't log in; records retained).
- **Layers:** Application (`ListUsersQuery`, `ResetUserPasswordCommand`, `SetUserActiveCommand`, `ChangePasswordCommand`), API (`AuthController`/settings endpoints, admin-only), Frontend (user-management page + force-change-password screen), Domain (`MustChangePassword`, `IsActive` behavior; login rejects inactive).
- **Spec ACs:** AC-3.4, AC-3.5, AC-3.6, AC-5.1–AC-5.4, FR-B5.
- **Depends on:** US-1.

### US-4: Admin lockout recovery utility
**Delivers:** A server-side utility runnable on the server PC resets the (sole) admin's password when locked out — the offline recovery path.
- **Layers:** API project console/CLI entry + `LocalAuthService` reuse.
- **Spec ACs:** FR-B6.
- **Depends on:** US-1.

## Testing Strategy

Formal E2E/API/Integration test *plans* were skipped for speed (can be added later via `/next`). Minimum tests to include with implementation:
- **Integration (xUnit):** login success/failure, inactive-user rejection, mode switch (Cloud path still validates an Auth0-style token; Local path validates app JWT), first-run creates admin + closes setup, self-registration code/duplicate-email rejection, admin reset → forced change.
- **Cloud regression:** a test asserting the Auth0 JWT-bearer configuration path is unchanged when `Auth:Mode = Cloud`.
- **Manual:** fresh Local DB → first-run → login from a second machine; Cloud deployment smoke test unaffected.

## Risk Register

| ID | Risk | Likelihood | Impact | Story | Mitigation |
|----|------|------------|--------|-------|------------|
| R-1 | Auth changes regress **Cloud/Auth0** login | Med | High | US-1 | `Auth:Mode` defaults to `Cloud`; Auth0 block untouched in that branch; add a Cloud-path regression test; every story re-verifies Cloud. |
| R-2 | Local user PK collides with / diverges from Auth0 `sub` scheme (PK is the sub) | Med | High | US-1 | Mint stable `local\|{guid}` ids; reuse `GetByAuth0SubAsync` unchanged; local login resolves via new `GetByEmailAsync`. |
| R-3 | Local JWT omits claims `ClinicContext`/`RoleAuthorizationHandler` expect | Med | High | US-1 | Issue the same claim types (`sub`, `email`, `role`, `clinic_id`); add a claims-contract test. |
| R-4 | Signing key committed / shared across installs (forgeable admin tokens) | Med | High | US-1 | Per-install key generated at first-run, stored outside source control (interim: user-secrets/local file; installer handles it in Phase 5). Never in `appsettings.json`. |
| R-5 | Email unique index breaks existing Cloud data (null/dup emails) | Low | Med | US-1 | Filtered unique index (local accounts only); Cloud `Email` stays nullable/non-unique. |
| R-6 | Frontend middleware/token-route branching leaves a route unprotected or a redirect loop | Med | Med | US-1 | Mode fetched once at bootstrap; explicit Cloud vs Local branches; test both gates. |
| R-7 | First-run reachable from a LAN client (wrong person becomes admin) | Low | High | US-1 | Enforce localhost-only + "no admin exists" gate server-side (AC-1.2a); test rejection from non-localhost. |

## Breaking Changes
- **None for Cloud mode** — all backend changes are additive and mode-gated; new User columns are nullable/defaulted and inert in Cloud mode.
- Local mode is entirely new behavior.

## Migrations
- One additive EF migration: `User.PasswordHash` (nullable), `IsActive` (default true), `MustChangePassword` (default false), optional `LastLoginAt` + lockout fields; **partial unique index** on `Email` with filter `"PasswordHash" IS NOT NULL` (local accounts only — Cloud rows with null/duplicate emails are excluded). Safe to apply to existing Cloud databases (auto-applied at startup as today).
