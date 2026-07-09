# Phase 4 — Execution Progress (LAN Hosting & Security Gates)

**Feature:** Windows Desktop / Offline-LAN Deployment Mode — **Phase 4** (FR-E + AC-1.2a)
**Plan:** [../plan.md](../plan.md) (APPROVED · Challenged)
**Branch:** `feature/windows-desktop-app`

> Phases 1–3 are COMPLETE (archived under `../phase-1/`, `../phase-3/`). This file tracks Phase 4 only.

## Story Status

| Story | Layer | Name | Status |
|-------|-------|------|--------|
| 1 | BE | LAN hosting & security gates | **implemented** |

## Working tree note (start of session)

Only `features/windows-desktop-app/plan.md` and `features/windows-desktop-app/stories/` were untracked at
start — those are this phase's own pipeline artifacts, not unrelated work. Nothing was excluded.

## Slice-by-slice log

### Slice A — Auth release gate + Hangfire lockdown [US-1] ✅
- Extracted `AuthController.IsLocalRequest` → `ClinicManagement.Infrastructure.LocalRequest.IsLoopback`
  (verbatim body, null-`RemoteIpAddress` ⇒ true preserved — R-8). `AuthController` now calls it.
- `AuthorizationPolicies.ConfigurePolicies(options, isLocalMode)` sets a fail-closed
  `FallbackPolicy = RequireAuthenticatedUser()` in Local; null in Cloud. `Program.cs` passes `isLocalAuthMode`.
- `[AllowAnonymous]` added to the two Google OAuth **browser-redirect** endpoints (`authorize`, `callback`);
  the AJAX endpoints get no carve-out → covered by the Local fallback.
- `MedicalDocumentsController` gets explicit class-level `[Authorize]` (defense-in-depth; the one raw-fetch
  caller already sends the bearer — verified, no FE change).
- `HangfireAuthorizationFilter` now takes an `isLocalMode` ctor arg; returns loopback-only in Local
  (replaces `return true;`), unchanged in Cloud.
- AC-1.2a verified + documented: `/setup` localhost gating now routes through `LocalRequest.IsLoopback`,
  and the "any user exists" gate (`CreateClinicCommand`) still closes setup after the first admin — no behavior change.

### Slice B — Secure LAN connectivity (HTTPS + CORS + bind) [US-2] ✅
- `ClinicManagement.Infrastructure.CorsOrigins` helper: `Assemble(frontendUrl, additional)` +
  `FromConfiguration(config)` → deduped (case-insensitive), order-preserving, empty/whitespace dropped.
  `Program.cs` CORS now uses the assembled list; Cloud collapses to the single `FrontendUrl`.
- Config-driven HTTPS: when `Https:CertPath` is set and the file exists, Kestrel binds HTTP+HTTPS on
  `Hosting:HttpPort`/`HttpsPort` (all interfaces) using the PFX cert, and `AddHttpsRedirection` gets an
  explicit `HttpsPort`. `app.UseHttpsRedirection()` is **guarded** by the `httpsConfigured` flag (R-3) —
  skipped for plain-HTTP LAN. When no cert is set, an optional `Hosting:Urls` drives the bind (else host default).
- `appsettings.json` gains optional, documented, **secret-free** sections: `Cors:AllowedOrigins`,
  `Https:{CertPath,CertPassword}`, `Hosting:{Urls,HttpPort,HttpsPort}` — all inert in Cloud.

### Slice C — Google OAuth token store (no appsettings rewrite) [US-3] ✅
- `IGoogleTokenStore` (Application) + `FileGoogleTokenStore` (Infrastructure, **Singleton**): persists the
  refresh token to `.local/google-refresh-token` (atomic temp-file+move), serves an in-memory cache, falls
  back to `GoogleCalendar:RefreshToken` config (R-5). Cache updated on every save (staleness guard).
- `GoogleCalendarController.Callback` now writes via the store — the appsettings.json regex-rewrite AND the
  in-memory `_configuration[...]` set are **removed**. `status`/existing-token-fallback reads go through the store.
- `GoogleCalendarService` reads the refresh token via the store (config fallback). Registered `AddSingleton` in `Extensions.cs`.

## Deviations from the plan

No **significant** deviations — implementation followed the approved plan.

**Auto-approved (trivial) deviations:**

| Deviation | Classification | Reason |
|-----------|----------------|--------|
| Added optional `GoogleCalendar:RefreshTokenPath` config key to `FileGoogleTokenStore` | Trivial | Additive testability seam (lets unit tests point the store at a temp path); default is unchanged `.local/google-refresh-token`. No API/behavior change for real deployments. |
| HTTPS bind implemented via explicit `Kestrel.ListenAnyIP` (HTTP+HTTPS) when a cert is configured, `Hosting:Urls`→`UseUrls` otherwise | Trivial (design detail within the planned step) | Makes the redirect target port deterministic (avoids the "failed to determine https port" class of bug) while keeping HTTP the safe default. Matches AC (config-driven HTTPS endpoint + guarded redirect + config-driven bind). |
| CORS helper exposes both `Assemble(...)` and `FromConfiguration(...)` | Trivial | Plan allowed "Infrastructure static helper or inline"; `Assemble` is the unit-testable core, `FromConfiguration` is the Program.cs call-site convenience. |

## Test Regressions

None. Full solution builds 0 errors / 0 new warnings; all 122 unit tests pass.

## Quality Gate

- `dotnet build ClinicManagement.sln` → **0 errors, 0 new warnings** (pre-existing Domain CS8618 / MVC
  nullable warnings unchanged; the MedicalDocuments nullable warnings only shifted +7 lines from the added `[Authorize]`).
- `dotnet test ClinicManagement.UnitTests` → **Passed! Failed: 0, Passed: 122** (Smart App Control did **not**
  block this run; the new Phase 4 tests executed live).
- Frontend: **no FE files changed** (`git status` confirms no `web/` changes) — no FE gate needed this phase.

### New tests (xUnit + Moq)
- `AuthorizationPoliciesTests` — Local ⇒ fallback present + `DenyAnonymousAuthorizationRequirement`; Cloud ⇒ null; named policies in both.
- `LocalRequestTests` — loopback / `::1` / null-remote / same-machine ⇒ true; distinct LAN IP ⇒ false (mirrors original cases, R-8).
- `CorsOriginsTests` — single origin; union; case-insensitive dedup; empty/whitespace/null dropped (R-4).
- `FileGoogleTokenStoreTests` — round-trip; missing-file→config fallback; missing+no-config→null; writes to
  `.local/` not appsettings; quotes/backslashes survive; **read-after-write returns the NEW token**; empty save throws.
- `ControllerAuthorizationCoverageTests` — the `[AllowAnonymous]` set exactly equals the approved allow-list
  (`Auth.{GetMode,Login,Setup,Register}`, `Connectivity.Get`, `GoogleCalendar.{Authorize,Callback}`); a new
  unexpected anonymous endpoint (or a renamed/removed approved one) fails the test.

## Deferred manual verification (needs a running server — deferred, documented)

These require a live API + LAN client and are validated at Phase 5 / review time:
- Local: `GET /api/medical-documents` and `POST /api/googlecalendar/sync-from-google` **without** a bearer ⇒ 401;
  with a valid session ⇒ work. `GET /api/connectivity`, `GET /api/auth/mode` ⇒ still anonymous 200. OAuth
  `authorize`→`callback` round-trip still completes.
- `/hangfire` from the server PC ⇒ loads; from a LAN client ⇒ blocked.
- CORS: a LAN-origin request accepted when in `Cors:AllowedOrigins`, rejected otherwise.
- HTTPS: cert configured ⇒ API serves HTTPS + login works (scheme-aware cookie); no cert ⇒ HTTP-LAN still works (no redirect loop).
- OAuth callback persists to `.local/google-refresh-token`; `appsettings.json` is **not** modified; sync survives an API restart.

## Learnings

- **Grep for *all* symbols before dropping a `using`.** Removing `using System.Net;` from `AuthController`
  after extracting the loopback helper broke the build — `HttpStatusCode` (also `System.Net`) was still used.
  Check every type the namespace provides, not just the one you moved.
- **`DenyAnonymousAuthorizationRequirement`** lives in `Microsoft.AspNetCore.Authorization.Infrastructure`
  (not the root `Microsoft.AspNetCore.Authorization`) — that's how to assert `RequireAuthenticatedUser()`.
- **Test project referencing the Web-SDK API project needs an explicit `<FrameworkReference Include="Microsoft.AspNetCore.App" />`** —
  the API's implicit framework reference does not flow to a `Microsoft.NET.Sdk` test project, so ASP.NET
  types (`DefaultHttpContext`, MVC `ControllerBase`, authorization options) won't resolve without it.

## Next step

`/review-story` (Phase 4, Story 1).
