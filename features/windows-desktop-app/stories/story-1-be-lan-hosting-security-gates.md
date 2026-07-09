# Story 1: [BE] LAN hosting & security gates

**Status:** APPROVED
**Story Status:** implemented
**Layer:** BE
**Depends On:** None
**Blocks:** None

> Backend/config-only story covering the plan's US-1 (auth release gate + Hangfire lockdown), US-2 (HTTPS serving + CORS for LAN origins + configurable bind), and US-3 (Google OAuth token store). Delivered as **one story** per user's explicit choice. Steps are grouped by slice and ordered so the auth gate (Slice A) lands first — it defines the `[AllowAnonymous]` carve-outs the token-store slice coexists with. There are **no frontend changes** in this phase (verified in the plan: the only cookie *set* is already scheme-aware; logout/change-password only clear cookies).

## Objective

Harden the Local (offline-LAN) install so it is safe to expose on a clinic network. Three backend outcomes: **(A)** every HTTP surface either requires an authenticated session or is explicitly, deliberately anonymous — the two anonymous-by-omission controllers (`GoogleCalendar` non-OAuth endpoints, `MedicalDocuments`) now require auth, any future controller that forgets `[Authorize]` fails **closed**, and the Hangfire dashboard is reachable only from the server PC; **(B)** a client PC on the clinic LAN can reach the server from its own origin (configurable CORS list) over HTTPS when a certificate is supplied, with a plain-HTTP LAN deployment still working when no cert is configured, and the bind address/ports are configuration-driven; **(C)** completing the Google OAuth flow persists the refresh token to a gitignored per-install file instead of regex-rewriting the committed `appsettings.json`. Everything is **additive and gated to Local mode** (`Auth:Mode = Local`) or behind inert defaults; **Cloud behavior is byte-for-byte unchanged**, consistent with Phases 1–3.

## Acceptance Criteria

_From spec:_

- [ ] **FR-E1** — In Local mode the API/web bind to the LAN so client PCs can connect; CORS allows the clinic's own origin(s) while keeping `AllowCredentials` (cannot use `AllowAnyOrigin` with credentials).
- [ ] **FR-E2** — LAN traffic is served over **HTTPS** (serving capability): Kestrel loads a server cert from a configurable path/password and binds an HTTPS endpoint when configured. *(Local CA + server-cert generation at install, and client-side CA import into the Windows trust store, are Phase 5.)*
- [ ] **FR-E3** — **Release gate:** in Local mode every API endpoint requires authentication; the two currently-anonymous controllers (Google Calendar non-OAuth actions, Medical Documents) are authenticated, and the Hangfire dashboard is locked down (server-PC-only in Local).
- [ ] **FR-E4** — Server bind address/ports are configurable (server side). *(Client-side storage of the server address — the WebView2 shell's "change server" flow — is Phase 5.)*
- [ ] **AC-1.2a** — First-run setup is reachable only from the server PC itself (localhost) and closes once the first admin exists. *(Already enforced backend-side since Phase 1 — this story **verifies + documents** it and reuses its logic via the extracted `LocalRequest` helper.)*

_Story-specific:_

- [ ] In **Local** mode, `AddAuthorization` sets `FallbackPolicy = RequireAuthenticatedUser()`; in **Cloud** the fallback stays null (named policies only) — asserted by a unit test.
- [ ] The two Google OAuth **browser-redirect** endpoints (`GET authorize`, `GET callback`) carry explicit `[AllowAnonymous]` so the Local fallback doesn't 401 a request that can't carry a bearer.
- [ ] `MedicalDocumentsController` carries an explicit class-level `[Authorize]` (defense-in-depth / self-documentation) beyond fallback coverage.
- [ ] The Hangfire dashboard authorization filter returns loopback-only in Local mode (replaces the current unconditional `return true;`); Cloud behavior unchanged.
- [ ] CORS allowed-origins is a configurable, deduped list defaulting to today's single `FrontendUrl` when unset (Cloud unchanged).
- [ ] `UseHttpsRedirection` is **guarded** — a no-op when no HTTPS endpoint is configured (so an HTTP-LAN deployment isn't broken by a "failed to determine https port" redirect).
- [ ] Google OAuth `callback` writes the refresh token via `IGoogleTokenStore` to a `.local/` file; `appsettings.json` is **not** modified at runtime; `GoogleCalendarService` reads via the store with `IConfiguration` fallback (no data loss on upgrade — R-5).
- [ ] The Google token store is registered **Singleton** and serves a read-after-write from an in-memory cache (preserves the old live-refresh-without-restart behavior).
- [ ] A reflection test asserts every `ClinicManagement.API` controller action is either fallback-covered/`[Authorize]` or on the explicit anonymous allow-list.

## Entry Criteria

Before starting this story, ensure:

- [ ] Phases 1, 2 & 3 are complete (auth-mode switch, Local-disk storage, connectivity awareness are functional). Phase 1 & 3 artifacts are archived under `../phase-1/` and `../phase-3/`.
- [ ] `dotnet build api/ClinicManagement.sln` is clean on the current branch (`feature/windows-desktop-app`).
- [ ] `web/` builds: `npm run build` succeeds and `npx tsc --noEmit` is clean (baseline — no FE work expected).
- [ ] Local-mode gating idiom is confirmed present: `LocalAuthConfig.IsLocalMode(IConfiguration)` (`api/ClinicManagement.Infrastructure/Auth/LocalAuthConfig.cs:29-30`).
- [ ] Current anonymous-by-omission surfaces confirmed: `GoogleCalendarController` and `MedicalDocumentsController` carry no class-level auth attribute; `HangfireAuthorizationFilter.Authorize` returns `true` (`Program.cs:255-262`); `AddAuthorization` has no `FallbackPolicy` (`Program.cs:135-144`).

## Steps

### Slice A — Auth release gate + Hangfire lockdown [US-1]

1. **Extract the loopback helper (Infrastructure, pure refactor)**
   - Create `api/ClinicManagement.Infrastructure/LocalRequest.cs` — a static `IsLoopback(HttpContext / RemoteIpAddress)` helper, body moved **verbatim** from `AuthController.IsLocalRequest` (`AuthController.cs:171-185`), preserving the null-`RemoteIpAddress` ⇒ `true` semantics (R-8).
   - **Must live in Infrastructure, not API:** `ClinicManagement.UnitTests` references Application + Infrastructure but **not** API, so an API-located helper would be unreachable from its unit test.
   - Update `AuthController` to call `LocalRequest.IsLoopback(...)` (behavior identical).

2. **Move the authorization-policy decision into the Application layer**
   - Extend `AuthorizationPolicies.ConfigurePolicies(options, isLocalMode)` in `api/ClinicManagement.Application/Common/Authorization/` to accept an `isLocalMode` flag and, when Local, set `options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()`. Cloud ⇒ leave `FallbackPolicy` null (named policies only). Keeps the decision unit-testable.
   - Modify `api/ClinicManagement.API/Program.cs` `AddAuthorization` block (`:135-144`) to pass `isLocalAuthMode` through to `ConfigurePolicies`.

3. **Carve out the OAuth browser-redirect endpoints + lock down Medical Documents**
   - Modify `api/ClinicManagement.API/Controllers/GoogleCalendarController.cs` — add explicit `[AllowAnonymous]` to `GET authorize` (`:170`) and `GET callback` (`:217`) only. The AJAX endpoints (`sync-from-google`, `redirect-uri`, `status`, `sync-appointment/{id}`) get **no** carve-out → the Local fallback now requires auth on them (they already flow through `client.ts` with a token since Phase 3).
   - Modify `api/ClinicManagement.API/Controllers/MedicalDocumentsController.cs` — add explicit class-level `[Authorize]` (`:13-15`) as defense-in-depth. Audit the one raw-fetch caller (`web/lib/api/medical-documents.ts:104`) confirms it already attaches the bearer (R-2 — no FE change).

4. **Lock down the Hangfire dashboard in Local mode**
   - Modify `HangfireAuthorizationFilter.Authorize` (`Program.cs:255-262`) — replace `return true;` with a Local-mode loopback-only check via `LocalRequest.IsLoopback(...)`; Cloud keeps current behavior. Pass mode into the filter as a ctor arg (it is constructed at `Program.cs:204-207`).

5. **Verify the deliberate anonymous exceptions still respond**
   - Confirm `GET /api/auth/mode`, `POST /api/auth/{login,setup,register}`, and `GET /api/connectivity` (the Phase 3 carry-forward — R-6 resolves here) remain reachable anonymously under the fallback.
   - Confirm AC-1.2a is still enforced: `/setup` localhost gating (`AuthController.IsLocalRequest` → now `LocalRequest.IsLoopback`) + the "any user exists" gate (`CreateClinicCommand.cs:253`) — verify + document, no behavior change.

### Slice B — Secure LAN connectivity (HTTPS serving + CORS + configurable bind) [US-2]

6. **Configurable, deduped CORS allowed-origins list**
   - Add a small CORS-origins parsing helper (Infrastructure static helper or inline config helper) — assembles `FrontendUrl` + an optional `Cors:AllowedOrigins` array (or `LanOrigins` CSV) into a deduped list; drops empty/whitespace entries; unit-testable.
   - Modify `Program.cs:169` — replace the single `.WithOrigins(frontendUrl)` with `.WithOrigins(<assembled list>)`, keeping `.AllowCredentials()`. Cloud falls back to the single `FrontendUrl` (unchanged).

7. **Config-driven HTTPS endpoint + guarded redirect + config-driven bind**
   - Configure a Kestrel HTTPS endpoint from config (cert path + password) **only when configured** (HTTP is the safe default when no cert is supplied — cert generation is Phase 5).
   - **Guard `app.UseHttpsRedirection()` (`Program.cs:185`)** so it is skipped when no HTTPS endpoint is configured (prevents the "failed to determine https port" redirect breaking HTTP-LAN clients) — drive it off an explicit "HTTPS configured" flag (R-3).
   - Make the bind address/port config-driven (respect `ASPNETCORE_URLS` / a `Hosting:Urls` value) so the LAN bind isn't hardcoded to localhost (FR-E4, server side).

8. **Document the new optional config keys (no secrets)**
   - Modify `api/ClinicManagement.API/appsettings.json` — add documented **optional** sections with defaults applied if absent, **no secrets**: `Cors:AllowedOrigins` (array), the `Kestrel`/`Https` cert path + password keys, and `Hosting:Urls`. Cloud ignores them / keeps current values.

### Slice C — Google OAuth token store (no appsettings rewrite) [US-3]

9. **Define the token-store seam (Application) + implement it (Infrastructure)**
   - Create `api/ClinicManagement.Application/Common/Interfaces/IGoogleTokenStore.cs` — `GetRefreshToken()` / `SaveRefreshTokenAsync(string)` (Application owns the interface; Infrastructure implements — Clean Architecture).
   - Create `api/ClinicManagement.Infrastructure/Services/FileGoogleTokenStore.cs` — persists the refresh token to a gitignored per-install file (`Path.Combine(ContentRoot, ".local", "google-refresh-token")`, atomic write; reuses the existing `.gitignore` `.local/` rule). **Singleton lifetime:** `SaveRefreshTokenAsync` writes atomically **and** updates an in-memory cached value; `GetRefreshToken` serves the cache (populated lazily from the file, falling back to `IConfiguration["GoogleCalendar:RefreshToken"]` for Cloud/back-compat — R-5). A first read that finds no token must not cache a permanent null that hides a later save.

10. **Wire the store through the OAuth callback and the read path + DI**
    - Modify `api/ClinicManagement.API/Controllers/GoogleCalendarController.cs` — replace the runtime `appsettings.json` regex-rewrite (`:324-347`) with a write through `IGoogleTokenStore`; update/remove the in-memory `_configuration[...]` set (`:350`) — the Singleton cache now provides the immediate-refresh behavior.
    - Modify `api/ClinicManagement.Infrastructure/Services/GoogleCalendarService.cs` — read the refresh token via `IGoogleTokenStore` (config fallback) instead of `_configuration["GoogleCalendar:RefreshToken"]` (`:30-32`).
    - Modify `api/ClinicManagement.Infrastructure/Extensions.cs` — register `IGoogleTokenStore` → `FileGoogleTokenStore` as **`AddSingleton`** (process-wide cache); safe to register unconditionally (only exercised when Google is used).

### Test step

11. **Backend unit tests + attribute-coverage guard**
    - Add unit tests (xUnit + Moq) in `ClinicManagement.UnitTests` (see Verification Steps for the list).
    - Add a `ClinicManagement.API` project reference to `ClinicManagement.UnitTests` (currently references Application + Infrastructure only) so the reflection scan can enumerate controller types.

## Files to Create/Modify

### Files to Create

| File | Purpose |
|------|---------|
| `api/ClinicManagement.Infrastructure/LocalRequest.cs` | Shared, unit-testable loopback check extracted from `AuthController.IsLocalRequest` (R-8) |
| `api/ClinicManagement.Application/Common/Interfaces/IGoogleTokenStore.cs` | `GetRefreshToken()` / `SaveRefreshTokenAsync(string)` seam (Application owns interface) |
| `api/ClinicManagement.Infrastructure/Services/FileGoogleTokenStore.cs` | Singleton token store: `.local/` file, atomic write, in-memory cache, config fallback |
| _(helper)_ CORS-origins parser | Deduped origin-list assembly (CSV/array → list); Infrastructure static helper or inline in a config helper — unit-testable |

### Files to Modify

| File | Changes |
|------|---------|
| `api/ClinicManagement.Application/Common/Authorization/AuthorizationPolicies.cs` | `ConfigurePolicies(options, isLocalMode)` sets `FallbackPolicy = RequireAuthenticatedUser()` in Local; null in Cloud |
| `api/ClinicManagement.API/Program.cs` | Pass `isLocalAuthMode` to `ConfigurePolicies` (`:135-144`); configurable CORS list (`:169`); config-driven Kestrel HTTPS endpoint + guarded `UseHttpsRedirection` (`:185`); config-driven bind; Hangfire filter → loopback-only in Local (`:255-262`, ctor arg `:204-207`) |
| `api/ClinicManagement.API/Controllers/GoogleCalendarController.cs` | `[AllowAnonymous]` on `authorize` (`:170`) + `callback` (`:217`); replace appsettings regex-rewrite (`:324-347`) + `_configuration[...]` set (`:350`) with `IGoogleTokenStore` write |
| `api/ClinicManagement.API/Controllers/MedicalDocumentsController.cs` | Explicit class-level `[Authorize]` (`:13-15`) |
| `api/ClinicManagement.API/Controllers/AuthController.cs` | Call `LocalRequest.IsLoopback(...)` instead of local `IsLocalRequest` (pure refactor) |
| `api/ClinicManagement.Infrastructure/Services/GoogleCalendarService.cs` | Read refresh token via `IGoogleTokenStore` (config fallback) instead of `_configuration[...]` (`:30-32`) |
| `api/ClinicManagement.Infrastructure/Extensions.cs` | Register `IGoogleTokenStore` → `FileGoogleTokenStore` as `AddSingleton` |
| `api/ClinicManagement.API/appsettings.json` | Optional documented sections (no secrets): `Cors:AllowedOrigins`, `Kestrel`/`Https` cert keys, `Hosting:Urls` |
| `api/ClinicManagement.UnitTests/*` | Add `ClinicManagement.API` project reference; new test classes (see below) |

### Frontend

None. Verified in the plan: the session cookie is set only in `web/app/api/auth/local-login/route.ts:41-43` (already scheme-aware — `secure` derives from `request.nextUrl.protocol`); `change-password/route.ts` and `local-logout/route.ts` only clear cookies (`maxAge: 0`, scheme-agnostic). No FE change.

## Verification Steps

After completing this story, verify:

- [ ] `dotnet build api/ClinicManagement.sln` — 0 errors / 0 new warnings.
- [ ] Backend unit tests pass (xUnit + Moq):
  - [ ] `AuthorizationPolicies.ConfigurePolicies` — Local ⇒ `FallbackPolicy` present and requires an authenticated user; Cloud ⇒ `FallbackPolicy` null.
  - [ ] `LocalRequest.IsLoopback` — loopback IPs / null `RemoteIpAddress` ⇒ true; a LAN IP ⇒ false (mirrors the original `IsLocalRequest` cases so behavior is provably preserved — R-8).
  - [ ] CORS origin-assembly helper — single `FrontendUrl` ⇒ one origin; `FrontendUrl` + list ⇒ deduped union; empty/whitespace dropped (R-4).
  - [ ] `FileGoogleTokenStore` — save-then-read round-trips; missing file ⇒ null/empty (fall back to config); write goes to `.local/` (not the appsettings path); a token containing `"`/`\` round-trips intact; **read-after-write on the same Singleton returns the NEW token** (cache-staleness guard).
  - [ ] Reflection/attribute test — every `ClinicManagement.API` controller action is fallback-covered/`[Authorize]` **or** on the explicit anonymous allow-list (`auth/mode`, `auth/login`, `auth/setup`, `auth/register`, `connectivity`, `googlecalendar/authorize`, `googlecalendar/callback`); a future anonymous-by-omission controller fails the test.
- [ ] `npx tsc --noEmit` clean; `npm run build` succeeds (no FE change expected; gate only if an incidental FE file is touched).
- [ ] **Cloud parity:** `Auth:Mode=Cloud` ⇒ no fallback policy (the two controllers behave exactly as today), CORS/HTTPS/bind unchanged, Hangfire filter unchanged, Google token read from config as before.
- [ ] **Manual (deferred, documented in progress.md):**
  - Local: `GET /api/medical-documents` and `POST /api/googlecalendar/sync-from-google` **without** a bearer ⇒ 401; **with** a valid session ⇒ work. `GET /api/connectivity`, `GET /api/auth/mode` ⇒ still anonymous 200. Google OAuth `authorize`→`callback` round-trip still completes.
  - `/hangfire` from the server PC ⇒ loads; from a LAN client ⇒ blocked.
  - CORS: a LAN-origin request is accepted when the origin is in `Cors:AllowedOrigins`, rejected otherwise.
  - HTTPS: with a cert configured ⇒ API serves HTTPS and login works (scheme-aware cookie); with **no** cert ⇒ HTTP-LAN still works (no redirect loop).
  - Google OAuth callback persists the token to `.local/google-refresh-token`; `appsettings.json` is **not** modified; sync still works after an API restart.

**Verification commands:**
```bash
# Backend build + unit tests
dotnet build api/ClinicManagement.sln
dotnet test api/ClinicManagement.sln

# Frontend types + build (no FE change expected)
cd web && npx tsc --noEmit && npm run build
```

> Note (from MEMORY): Smart App Control may block `dotnet test` at DLL load (0x800711C7) — environmental, not a defect. If it blocks, record the unit tests as author-verified-by-build and note it in progress.md (matches Phase 3).

## Exit Criteria

This story is complete when:

- [ ] In Local mode, an unauthenticated request to any clinic-data / medical-document / Google AJAX / Hangfire surface is rejected (401 / blocked); the deliberate anonymous exceptions (auth bootstrap, connectivity, OAuth browser-redirects) still work.
- [ ] The Hangfire dashboard is reachable only from the server PC in Local mode.
- [ ] A LAN client can be allowed via a configurable CORS origin list (still `AllowCredentials`); the server serves HTTPS when a cert is configured and plain HTTP when it isn't (no broken redirect); bind address/ports are config-driven.
- [ ] Google OAuth completes and persists the refresh token to a `.local/` file — `appsettings.json` is never rewritten at runtime; sync survives an API restart; existing config-based tokens still work (R-5).
- [ ] AC-1.2a is verified + documented (setup localhost-only, closes after first admin) — no behavior change.
- [ ] **Cloud is byte-for-byte unchanged** (no fallback policy, no CORS/HTTPS/bind/Hangfire/token-read change takes effect in Cloud).
- [ ] All verification steps pass; quality gate met (0 errors / 0 new warnings; tsc clean; build succeeds; unit tests pass).

## Notes

- **Cloud-safety principle (carried from Phases 1–3):** all wiring is behind `LocalAuthConfig.IsLocalMode(...)` or inert defaults. The identical pre-existing anonymous-PHI exposure in **Cloud** (the two attribute-less controllers) is **out of scope** and captured as R-6/R-7 follow-ups — Cloud stays unchanged.
- **Secrets scope (R-7):** Phase 4 stops *new* secret writes (Slice C) but does **not** remove the already-committed real secrets (`GoogleCalendar:*`, `HuggingFace:ApiKey` in `appsettings.json`) — purging them + rewriting git history is a separate follow-up (touches Cloud config + history).
- **Reverse-proxy caveat (R-9):** the Hangfire loopback filter also blocks access when the API sits behind a reverse proxy (proxy IP ≠ loopback). Acceptable for the v1 single-PC topology (Phase 5 runs the API directly, not behind a proxy); document that a proxy would need `ForwardedHeaders`.
- **No migration:** Phase 4 is hosting/authorization/config only — no schema changes.
- **Test plans:** all `test-plan-*.md` are skipped for this phase (no APPROVED E2E/API/integration plans; Postman/Newman not run per user preference). Automated coverage is backend unit tests; FE is type-check + build (no FE change).
