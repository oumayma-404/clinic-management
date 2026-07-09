# Implementation Plan: Windows Desktop / Offline-LAN — Phase 4 (LAN Hosting & Security Gates)

**Status:** APPROVED
**Challenged:** Yes
**Created:** 2026-07-09
**Spec:** [spec.md](./spec.md) (APPROVED · Challenged) — this plan covers **Phase 4 only** (FR-E + AC-1.2a). Phases 1 (pluggable auth), 2 (local-disk storage) and 3 (connectivity awareness) are **COMPLETE**; their planning artifacts are archived under [phase-1/](./phase-1/) and [phase-3/](./phase-3/) (Phase 2 was a single feat commit, no pipeline archive). Phase 5 (packaging/installers/backup) gets its own plan via `/next`.

## Overview

Harden the Local (offline-LAN) install so it is safe to expose on a clinic network: **every HTTP surface requires authentication or is server-PC-only**, **LAN clients can connect over HTTPS from their own origin**, and the **Google OAuth token stops being written back into a committed, install-directory config file**. All behavior is additive and **gated to Local mode** (`Auth:Mode = Local`); Cloud stays byte-for-byte unchanged, consistent with Phases 1–3.

This is the spec's **release gate** phase: Local mode puts patient data on a directly-reachable LAN, so "auth on all endpoints", "HTTPS on LAN", and "no bundled/rewritten secrets" are treated as requirements, not nice-to-haves (spec Non-Functional Hints).

### Scope boundary with Phase 5 (confirmed with user)

- **HTTPS (FR-E2):** Phase 4 delivers the **serving capability** — Kestrel loads a server certificate from a configurable path/password and binds an HTTPS endpoint on the LAN; `UseHttpsRedirection` is guarded so a plain-HTTP LAN deployment isn't broken. The **generation** of the local CA + server certificate at install, and the **client-side import of the CA into the Windows trust store**, are **Phase 5** (installer / CA-trust provisioning). Phase 4 works with any cert the operator/installer supplies (config-driven; HTTP is the safe default when no cert is configured).
- **Configurable bind & server address (FR-E4):** Phase 4 makes the **server** bind address/ports config-driven. The **client**-side storage of the server address (the WebView2 shell's "change server" flow) is **Phase 5** (client installer/shell) — there is no desktop shell in the repo yet.
- **Auth scope:** the gate is applied in **Local mode only**; Cloud is untouched. The identical pre-existing anonymous-PHI exposure that exists in **Cloud** (the two attribute-less controllers) is **out of scope** and captured as a documented risk (R-6) / follow-up, per the "Cloud byte-for-byte unchanged" principle carried through Phases 1–3.
- **Secrets:** Phase 4 replaces the runtime **appsettings.json rewrite** with a gitignored per-install token store. **Removing the already-committed real secrets** (Google/HuggingFace) from `appsettings.json` and purging git history is a **separate follow-up** (touches Cloud config + history rewrite), captured as R-7.

### Central mechanism — fail *closed*, gated to Local

Today auth is **opt-in per controller** via `[Authorize]`, and there is **no `FallbackPolicy`** (`Program.cs:135-144`, `AuthorizationPolicies.cs:13-30`). Two controllers — `GoogleCalendarController` and `MedicalDocumentsController` — are anonymous **by omission** (no attribute at all), exposing OAuth token handling and patient medical documents / PDFs. The gate installs a **`FallbackPolicy = RequireAuthenticatedUser()` in Local mode**, so any endpoint lacking explicit `[AllowAnonymous]` fails **closed** (401) — this fixes the two controllers *and* any future controller that forgets `[Authorize]`. The deliberate anonymous exceptions keep working because they already carry `[AllowAnonymous]`; the two OAuth **browser-redirect** endpoints (which cannot carry a bearer token) get an explicit `[AllowAnonymous]` carve-out.

**Why Local-only:** in Cloud, Auth0 is the auth scheme and the fallback would newly 401 the two controllers → a Cloud behavior change we're deliberately avoiding. Gating the fallback to Local keeps Cloud identical.

### What we deliberately do NOT change

- **Cloud behavior** — no `FallbackPolicy`, no CORS change, no HTTPS change, no controller-attribute change takes effect in Cloud. All Phase 4 wiring is behind `LocalAuthConfig.IsLocalMode(...)` or inert defaults. (Cloud-unchanged principle, Phases 1–3.)
- **The committed secrets in `appsettings.json`** (`GoogleCalendar:*`, `HuggingFace:ApiKey`, `appsettings.json:66-73`) — flagged as R-7 follow-up, not touched here (see boundary above).
- **Role/policy enforcement** — roles remain stored/displayed, not enforced (spec Out of Scope). The gate only requires *authentication*, not role authorization, on the previously-anonymous endpoints.
- **The `[AllowAnonymous]` on `GET /api/connectivity`, `GET /api/auth/mode`, `POST /api/auth/{login,setup,register}`** — these are the intended exceptions (R-6 from Phase 3 resolves here: connectivity stays anonymous by design, now formally reviewed under the gate).

### Mode-gating mechanism (reused from Phases 1 & 3)

- **Backend:** `LocalAuthConfig.IsLocalMode(IConfiguration)` (`Infrastructure/Auth/LocalAuthConfig.cs:29-30`). New config values follow the same static-accessor / `const`-default idiom (`LocalAuthConfig` per-install key resolution; `ConnectivityConfig` parallel helper — the sanctioned house pattern).
- **Frontend:** **no change needed** (verified). The session cookie is *set* only in `local-login/route.ts:41-43`, which is already **scheme-aware** (`secure` derives from `request.nextUrl.protocol === 'https:'`, with `AUTH_COOKIE_SECURE` as an explicit override — not an OR), so login already works over HTTP *and* HTTPS. `change-password/route.ts` and `local-logout/route.ts` only **clear** cookies (`maxAge: 0`), and a delete matches by name/path regardless of `secure` — nothing to align. Phase 4 is entirely backend/config work.

---

## Files to Modify / Create

### Backend — modify

- `api/ClinicManagement.API/Program.cs`
  - **Authorization gate (US-1):** in the `AddAuthorization` block (`Program.cs:135-144`), when `isLocalAuthMode` set `options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()`. (Cloud: leave as today — named policies only.) Prefer routing this through `AuthorizationPolicies.ConfigurePolicies(options, isLocalMode)` so the logic is unit-testable in the Application layer (see Testing Strategy).
  - **Hangfire lockdown (US-1):** replace the `HangfireAuthorizationFilter.Authorize` body (`Program.cs:255-262`, currently `return true;`) with a loopback-only check in Local mode (reuse the extracted `LocalRequest.IsLoopback(...)` helper — see below); in Cloud keep current behavior. Pass mode into the filter (ctor arg) since it's constructed at `Program.cs:204-207`.
  - **CORS LAN origins (US-2):** replace the single `.WithOrigins(frontendUrl)` (`Program.cs:169`) with a configurable **list** of origins (still `.AllowCredentials()` — cannot use `AllowAnyOrigin` with credentials). Read `frontendUrl` + an optional `Cors:AllowedOrigins` array (or `LanOrigins` CSV) via a small helper; de-dupe; Cloud unchanged (falls back to the single `FrontendUrl`).
  - **HTTPS serving + guard (US-2):** configure a Kestrel HTTPS endpoint from config (cert path + password) **only when configured**; **guard `app.UseHttpsRedirection()` (`Program.cs:185`)** so it is skipped when no HTTPS endpoint is configured (prevents the "failed to determine https port" redirect breaking HTTP-LAN clients). Make the bind address/port config-driven (respect `ASPNETCORE_URLS` / a `Hosting:Urls` value) so the LAN bind is not hardcoded to localhost.
- `api/ClinicManagement.API/Controllers/GoogleCalendarController.cs`
  - **US-1:** add explicit `[AllowAnonymous]` to the two **browser-redirect** OAuth endpoints — `GET authorize` (`:170`) and `GET callback` (`:217`) — so the Local `FallbackPolicy` doesn't 401 them (they can't carry a bearer). The AJAX endpoints (`sync-from-google`, `redirect-uri`, `status`, `sync-appointment/{id}`) intentionally get **no** carve-out → the fallback now requires auth on them in Local (they already flow through `client.ts` with a token since Phase 3).
  - **US-3:** replace the runtime `appsettings.json` regex-rewrite (`:324-347`) with a write through the new token-store seam; also update the in-memory `_configuration[...]` set (`:350`) via the store or keep as a cache-refresh.
- `api/ClinicManagement.API/Controllers/MedicalDocumentsController.cs`
  - **US-1:** no attribute needed if the `FallbackPolicy` covers it — but add an explicit **`[Authorize]`** at class level (`:13-15`) as defense-in-depth and self-documentation (so the controller is safe even in a future Cloud fallback). Verify Local frontend callers send a token (they route through `client.ts`; audit the one raw-fetch path in `medical-documents.ts:104`).
- `api/ClinicManagement.Infrastructure/Services/GoogleCalendarService.cs`
  - **US-3:** read the refresh token through the new token-store seam (falling back to `IConfiguration` for Cloud/back-compat) instead of `_configuration["GoogleCalendar:RefreshToken"]` (`:30-32`).
- `api/ClinicManagement.Infrastructure/Extensions.cs`
  - **US-3:** register the token store (`IGoogleTokenStore` → `FileGoogleTokenStore`) as **`AddSingleton`** (so its in-memory cache is process-wide — a scoped/transient instance would defeat the save→read-after-write cache) — safe to register unconditionally; only exercised when Google is used.
- `api/ClinicManagement.API/appsettings.json`
  - Document new **optional** sections (defaults apply if absent, **no secrets**): `Cors:AllowedOrigins` (array), `Kestrel`/`Https` cert path+password keys, `Hosting:Urls`. (Cloud ignores them / keeps current values.)

### Backend — create

- `api/ClinicManagement.Application/Common/Authorization/` — extend `AuthorizationPolicies.ConfigurePolicies` to accept an `isLocalMode` flag and set the fallback policy (keeps the policy decision in the unit-testable Application layer; `Program.cs` just passes the flag).
- `api/ClinicManagement.Application/Common/Interfaces/IGoogleTokenStore.cs` — `GetRefreshToken()` / `SaveRefreshTokenAsync(string)` seam (Application owns the interface; Infrastructure implements — Clean Architecture).
- `api/ClinicManagement.Infrastructure/Services/FileGoogleTokenStore.cs` — persists the refresh token to a gitignored per-install file (mirror `LocalAuthConfig` `.local/` pattern: `Path.Combine(ContentRoot, ".local", "google-refresh-token")`, `RandomNumberGenerator`-free — it stores an externally-issued token; atomic write). Reuses the existing `.gitignore` `.local/` rule.
  - **Lifetime + caching (avoids read-after-write staleness):** registered **Singleton**; `SaveRefreshTokenAsync` writes the file atomically **and** updates an in-memory cached value; `GetRefreshToken` serves the cache (populated lazily from the file, falling back to `IConfiguration` for Cloud/back-compat — R-5). This preserves the old `:350` behavior where the running process sees a freshly-refreshed token **immediately, without a restart** — dropping the `_configuration[...]` set at `:350` without this would *regress* live-refresh. A first read that finds no token must not cache a permanent null in a way that hides a later save (cache the value written by `Save`, or re-check on miss).
- `api/ClinicManagement.Infrastructure/` — a small **`LocalRequest`** static helper extracted from `AuthController.IsLocalRequest` (`AuthController.cs:171-185`) so the loopback check is shared between the setup gate and the Hangfire filter and is unit-testable. **Must live in Infrastructure (not API)** — the `ClinicManagement.UnitTests` project references Application + Infrastructure but **not** API, so an API-located helper would be unreachable from the R-8 unit test. (Refactor-in-place; behavior identical.) `AuthController` and the Hangfire filter both call `LocalRequest.IsLoopback(...)`.
- A small **CORS-origins parsing helper** (Infrastructure static helper or inline in a config helper) — unit-testable origin-list assembly (CSV/array → deduped list).

### Frontend — none

No frontend changes. Verified: the session cookie is set only in `web/app/api/auth/local-login/route.ts:41-43` (already scheme-aware); `change-password/route.ts` and `local-logout/route.ts` only clear cookies (`maxAge: 0`), which is scheme-agnostic. Nothing to modify.

*(No new UI. The `/setup` localhost exposure control (AC-1.2a) is already enforced backend-side since Phase 1 — `AuthController.IsLocalRequest`, `CreateClinicCommand.cs:253` "any user exists" gate — so Phase 4 only **verifies + documents** it, and reuses its logic via the extracted `LocalRequest` helper.)*

---

## Implementation Stories

Vertical slices, strict dependency order. US-1 lands the gate first (it defines the `[AllowAnonymous]` carve-outs the other stories coexist with). US-2 (hosting) is independent of US-3 (token store); both build on the gate.

### US-1: No unauthenticated access to clinic data (auth release gate + Hangfire lockdown)
**Value:** In a Local install, any attempt to reach a patient-data, medical-document, or job-dashboard surface without a valid session is rejected — the two anonymous-by-omission controllers (`GoogleCalendar` non-OAuth endpoints, `MedicalDocuments`) now require auth, the Hangfire dashboard is reachable only from the server PC, and any future endpoint that forgets `[Authorize]` fails closed. The deliberate anonymous exceptions (auth bootstrap, connectivity, OAuth browser-redirects) keep working.

- **Backend:** `FallbackPolicy = RequireAuthenticatedUser()` in Local mode (via `AuthorizationPolicies.ConfigurePolicies(options, isLocalMode)`); `[AllowAnonymous]` carve-outs on `GoogleCalendarController` `authorize`/`callback`; explicit `[Authorize]` on `MedicalDocumentsController`; Hangfire filter → loopback-only in Local (via extracted `LocalRequest` helper). Verify the anonymous exceptions still respond (mode/login/setup/register/connectivity).
- **AC covered:** FR-E3, AC-1.2a (verified — setup already localhost-gated), R-6 resolution (connectivity anonymous exception formally reviewed).
- **Depends on:** — (first slice).

### US-2: Secure LAN connectivity (HTTPS serving + CORS for LAN origins + configurable bind)
**Value:** A client PC on the clinic LAN can reach the server from its own origin without a CORS rejection, over HTTPS when a certificate is supplied, and the server's bind address/ports are configuration-driven rather than hardcoded to localhost — while a plain-HTTP LAN deployment (no cert yet) still works.

- **Backend:** configurable CORS allowed-origins list (keep `AllowCredentials`); config-driven Kestrel HTTPS endpoint (cert path/password) applied only when configured; **guard `UseHttpsRedirection`** to no-op without an HTTPS endpoint; config-driven bind (`ASPNETCORE_URLS`/`Hosting:Urls`); document the new config keys (no secrets).
- **Frontend:** none — the only cookie *set* (`local-login`) is already scheme-aware; logout/change-password only delete cookies (scheme-agnostic). No FE change (verified).
- **AC covered:** FR-E1, FR-E2 (serving capability; cert gen/trust = Phase 5), FR-E4 (server side; client-side = Phase 5).
- **Depends on:** — (independent of US-1; ordered after for a clean diff).

### US-3: Google OAuth token persists without rewriting appsettings
**Value:** Completing the Google Calendar OAuth flow persists the refresh token to a gitignored per-install file instead of regex-rewriting the committed `appsettings.json` — so Google sync survives on a read-only install directory (a Phase 5 packaging blocker), can't corrupt the JSON, and stops writing a live secret into a tracked file.

- **Backend:** `IGoogleTokenStore` (Application) + `FileGoogleTokenStore` (Infrastructure, `.local/` file); `GoogleCalendarController.Callback` writes via the store (removing the `appsettings.json` regex-rewrite at `:324-347`); `GoogleCalendarService` reads via the store with config fallback; DI registration.
- **AC covered:** FR-E supporting hardening (read-only-install readiness for Phase 5); addresses the CLAUDE.md OAuth-rewrite security debt.
- **Depends on:** US-1 (shares `GoogleCalendarController`; US-1's `[AllowAnonymous]` on `callback` must be in place first).

---

## Testing Strategy

Manual-testing-first for the runtime/hosting behaviors (no cert, live LAN, or Docker guaranteed in-session), matching Phases 1–3. `test-plan-*.md` are all **skipped** for this phase (no APPROVED E2E/API/integration plans; Postman/Newman not run per user preference). The automated layer is **backend xUnit + Moq** unit tests, kept in the Application layer where the `UnitTests` project can reference them.

- **Backend unit tests (xUnit + Moq):**
  - `AuthorizationPolicies.ConfigurePolicies(options, isLocalMode)`: Local ⇒ `FallbackPolicy` present and requires an authenticated user; Cloud ⇒ `FallbackPolicy` null (named policies only). (Application-layer, directly testable.)
  - `LocalRequest.IsLoopback(...)`: loopback IPs / null `RemoteIpAddress` ⇒ true; a LAN IP ⇒ false (mirrors the existing `IsLocalRequest` cases so behavior is provably preserved after extraction).
  - CORS origin-assembly helper: single `FrontendUrl` only ⇒ one origin; `FrontendUrl` + list ⇒ deduped union; empty/whitespace entries dropped.
  - `FileGoogleTokenStore`: save-then-read round-trips; missing file ⇒ null/empty (fall back to config); write goes to `.local/` (not the appsettings path); a token containing `"`/`\` round-trips intact (fixes the JSON-corruption bug of the old regex path); **read-after-write on the same Singleton instance returns the NEW token** (guards the cache-staleness regression — a read that returned null before a save must return the saved value after).
- **Attribute/coverage assertions (guard against regressions):** a reflection test asserting **every controller action** in `ClinicManagement.API` either carries `[Authorize]`/is covered by the Local fallback **or** carries an explicit `[AllowAnonymous]` on the known allow-list (`auth/mode`, `auth/login`, `auth/setup`, `auth/register`, `connectivity`, `googlecalendar/authorize`, `googlecalendar/callback`) — so a future anonymous-by-omission controller fails the test. **Requires adding a `ClinicManagement.API` project reference to `ClinicManagement.UnitTests`** (currently it references Application + Infrastructure only); the reflection scan enumerates controller types from the API assembly. Add this reference as part of US-1's test step.
- **Frontend:** no FE changes in this phase (verified — see Frontend section). No unit-test runner / no ESLint in `web/` (Phases 1–3 learning); if any incidental FE file is touched, gate is `npx tsc --noEmit` + `npm run build` (both clean).
- **Manual (deferred, documented in progress.md):**
  - Local mode: `GET /api/medical-documents` and `POST /api/googlecalendar/sync-from-google` **without** a bearer ⇒ 401; **with** a valid session ⇒ work. `GET /api/connectivity`, `GET /api/auth/mode` ⇒ still anonymous 200. Google OAuth `authorize`→`callback` round-trip still completes (anonymous redirect).
  - `/hangfire` from the server PC ⇒ loads; from a LAN client ⇒ blocked.
  - CORS: a LAN-origin browser request is accepted when the origin is in `Cors:AllowedOrigins`, rejected otherwise.
  - HTTPS: with a cert configured, the API serves HTTPS and login works (scheme-aware cookie); with **no** cert, HTTP-LAN still works (no redirect loop).
  - Google OAuth callback persists the refresh token to `.local/google-refresh-token`; `appsettings.json` is **not** modified; sync still works after an API restart.
  - **Cloud parity:** `Auth:Mode=Cloud` — no fallback policy (the two controllers behave exactly as today), CORS/HTTPS/bind unchanged, Hangfire filter unchanged, Google token read from config as before.

Quality gate (per project policy): `dotnet build ClinicManagement.sln` 0 errors / 0 new warnings; all unit tests pass; `tsc --noEmit` clean; `npm run build` succeeds.

---

## Risk Register

| ID | Risk | Likelihood | Impact | Story | Mitigation |
|----|------|------------|--------|-------|------------|
| R-1 | `FallbackPolicy` in Local mode 401s a **browser-redirect** OAuth endpoint (`authorize`/`callback`) that can't carry a bearer ⇒ Google Calendar setup breaks | Med | High | US-1 | Add explicit `[AllowAnonymous]` to `authorize` + `callback` **in the same slice** that adds the fallback; manual-verify the OAuth round-trip; unit-test asserts these two are on the anonymous allow-list. |
| R-2 | `FallbackPolicy` unintentionally 401s a **frontend** call that doesn't attach a token, breaking a Local feature | Low | High | US-1 | **Verified low:** every function in `medical-documents.ts` routes through `client.ts` (token attached) **except** `generatePdfForDownload` (`:104`), which uses raw `fetch` only for the binary blob response but **already attaches the bearer** (fetches the same mode-aware `/api/auth/token` + `Authorization` header + `credentials: 'include'`) — so it will not 401 and needs **no refactor**. Mitigation is a **confirmation audit** of Local callers of the two controllers (not a rewrite) + manual-verify each affected screen in Local. |
| R-3 | Guarding/removing `UseHttpsRedirection` wrong ⇒ either HTTP-LAN clients hit a broken redirect, or an HTTPS deployment silently serves HTTP | Med | Med | US-2 | Drive redirect off an explicit "HTTPS endpoint configured" flag; default OFF (HTTP-LAN safe); when a cert **is** configured, enable redirect + bind the HTTPS endpoint. Manual-verify both cert / no-cert cases. |
| R-4 | CORS `AllowCredentials` + a mis-assembled origins list ⇒ either LAN clients blocked or an over-broad origin allowed | Med | Med | US-2 | Never combine `AllowCredentials` with `AllowAnyOrigin`; assemble an explicit, deduped, configurable list; unit-test the assembly; default to today's single `FrontendUrl` when unset (Cloud unchanged). |
| R-5 | Token-store migration loses an **existing** refresh token already sitting in `appsettings.json` ⇒ Google sync silently stops after upgrade | Low | Med | US-3 | `FileGoogleTokenStore.Get` falls back to `IConfiguration["GoogleCalendar:RefreshToken"]` when the `.local/` file is absent; first successful `callback` writes it to the store. No data loss on upgrade. |
| R-6 | Anonymous `GET /api/connectivity` (Phase 3 carry-forward) violates the "auth on all endpoints" gate | Low | Low | US-1 | **Resolved here:** it returns only a non-sensitive boolean, is Local-only (404 in Cloud), is polled before login by LAN clients, and is a deliberate `[AllowAnonymous]` exception — like `GET /api/auth/mode`. Kept on the reviewed allow-list. |
| R-7 | Real secrets remain committed in `appsettings.json` (`GoogleCalendar:*`, `HuggingFace:ApiKey`) after Phase 4 | High (already true) | Med | — | **Out of scope (documented):** removing them + purging git history is a separate follow-up affecting Cloud config; Phase 4 stops *new* secret writes (US-3) but does not purge the existing ones. Capture as a follow-up item. |
| R-8 | Extracting `IsLocalRequest` → shared `LocalRequest` helper subtly changes the setup gate's behavior | Low | High | US-1 | Pure refactor: move the method body verbatim, keep the null-`RemoteIpAddress`⇒true semantics; unit-test the extracted helper against the original cases; `AuthController` calls the helper. |
| R-9 | Hangfire loopback filter also blocks legitimate access when the API sits behind a reverse proxy (proxy IP ≠ loopback) | Low | Low | US-1 | Acceptable for v1 single-PC topology (no proxy); document that behind a proxy the dashboard needs `ForwardedHeaders` or disabling. Phase 5 packaging runs the API directly, not behind a proxy. |

## Breaking Changes

None in **Cloud** (all gating is Local-only or inert defaults; the two controllers, CORS, HTTPS, Hangfire, and Google token read all behave exactly as today in Cloud). In **Local mode**, this is an intentional tightening: endpoints previously reachable anonymously now require authentication, and `/hangfire` is server-PC-only — that is the point of the release gate. The Google token-store change is backward-compatible (config fallback, R-5).

## Migrations

None. No schema changes — Phase 4 is hosting/authorization/config only.
