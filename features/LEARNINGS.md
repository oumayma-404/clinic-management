# Project Learnings

Patterns and insights discovered during feature development.

---

## Patterns

### Pluggable auth via mode-gated providers + a unified session seam
**Discovered in:** windows-desktop-app
**Context:** Adding an offline "Local" auth mode alongside the existing Auth0 "Cloud" mode without forking the frontend.
**Learning:** Rather than branch every consumer on the auth backend, mount either a `CloudSessionProvider` (bridges Auth0 `useUser`) or a `LocalSessionProvider` (reads an HttpOnly cookie) at the layout level, and expose a single `useSession()` seam. All consumers read the unified context; the mode is delivered to the browser via SSR (`useSession().mode`) instead of a bootstrap fetch. `Auth0Provider` is mounted only in Cloud.
**Recommendation:** When adding a second implementation of a cross-cutting concern (auth, storage, telemetry), introduce one seam and gate the *provider*, not each call site. Keep the seam SSR-tolerant (return a loading default instead of throwing when no provider is in scope).

### Discriminated command branch to add a mode without duplicating the command
**Discovered in:** windows-desktop-app
**Context:** `CreateClinicCommand` / `JoinClinicCommand` needed a Local (password-backed) path in addition to the Cloud (Auth0) path.
**Learning:** A single command discriminated by the presence of an optional field (`Password` present → Local branch) keeps the Cloud path byte-for-byte unchanged while adding the new behavior. Only the mode-gated endpoint ever sets the discriminating field.
**Recommendation:** Prefer an internal discriminated branch over a parallel command when the Cloud path must stay untouched — but add a defensive `IsLocalMode` re-check inside the branch so a future caller can't silently trigger the bootstrap path in the wrong mode (see review Finding 17).

### Testable maintenance/CLI logic belongs in the Application layer
**Discovered in:** windows-desktop-app
**Context:** An offline admin-recovery console command (`reset-admin-password`) intercepted in `Program.cs` before the web host boots.
**Learning:** The `UnitTests` project references **only `Application`** (not API/Infrastructure). Putting the orchestration core (`AdminPasswordRecoveryService`) in Application made it unit-testable; the API layer holds only the thin CLI wrapper (config + DI + mode guard + printing). The core service is deliberately **not** DI-registered so it can never be injected into an HTTP handler (no unauthenticated reset path).
**Recommendation:** Keep use-case orchestration in Application even for console/maintenance entry points; reserve the host project for wiring. Leave dangerous services out of the DI container when there must be no HTTP-reachable path to them.

### Judge internet reachability at the server, not the browser, in offline-LAN topologies
**Discovered in:** windows-desktop-app (Phase 3)
**Context:** Degrading internet-dependent features (AI chat, Google Calendar) gracefully when the internet is down, on a LAN where client PCs may only reach the server.
**Learning:** The .NET **server** (not the browser) makes the outbound AI/Google calls, so "internet reachable" must reflect the server's egress. A dedicated `GET /api/connectivity` endpoint probes a configurable URL (short timeout, briefly cached) and returns `{ internetReachable }`; the frontend polls it. This also yields two separable signals: the poll getting *any* HTTP response = server up; the body bit = internet up. Client-side `navigator.onLine` would have measured the wrong thing.
**Recommendation:** When the thing that depends on connectivity runs server-side, probe connectivity server-side and let clients poll it. Make the probe URL configurable (captive-portal / firewalled-endpoint false positives) and cache it behind a herd-guard (Singleton + `IMemoryCache` + `SemaphoreSlim`) so N polling clients collapse to one probe per TTL.

### Route every HTTP call through one client wrapper to unify offline/error handling
**Discovered in:** windows-desktop-app (Phase 3)
**Context:** Google-calendar client calls historically used raw `fetch` (throwing a plain `Error`), while the rest of the app went through `client.ts` (throwing `ApiError`).
**Learning:** Routing the raw-`fetch` calls through the shared `client.ts` wrapper is the single seam that lets *all* network paths share the same offline treatment — `client.ts` maps a dropped connection to `ApiError(status: 0)`, so both the AI and calendar paths can special-case it with one retryable "connexion perdue" message.
**Recommendation:** Send all HTTP through one wrapper so cross-cutting concerns (offline detection, auth token, error shape) live in exactly one place. But see the Conventions note — unifying the wrapper can *change* the error message a legacy caller saw, so audit the server error shapes it parses.

### Reflection-based allow-list test as an authorization regression net
**Discovered in:** windows-desktop-app (Phase 4)
**Context:** A release-gate phase adding a fail-closed `FallbackPolicy` in Local mode, where the only way an auth hole reappears is a new `[AllowAnonymous]` on some endpoint.
**Learning:** `ControllerAuthorizationCoverageTests` reflects over all controllers and asserts the set of `[AllowAnonymous]` endpoints **exactly equals** an approved allow-list (`Auth.{GetMode,Login,Setup,Register}`, `Connectivity.Get`, `GoogleCalendar.{Authorize,Callback}`). Any new/renamed/removed anonymous endpoint fails the build until it is consciously reviewed onto the list. This converts "did we accidentally expose something?" from a manual audit into a compile-time-ish gate.
**Recommendation:** When a security invariant is "only this explicit set may be anonymous/public," write a reflection test that pins the *exact* set, not a "contains" check — so both additions and removals trip it. The same shape works for any allow-list invariant (public routes, unauthenticated endpoints, exported symbols).

### Gate mode-invariant guards on the *mode* flag, not a *capability* flag
**Discovered in:** windows-desktop-app (Phase 4, story review + review Finding 4)
**Context:** HTTPS/Kestrel bind and `UseHttpsRedirection` were guarded on `httpsConfigured` (a cert path being present), under a phase invariant of "Cloud byte-for-byte unchanged."
**Learning:** `httpsConfigured` is never set in Cloud, so guarding on it silently changed Cloud behavior: `UseHttpsRedirection` went from *always* (Cloud's prior state) to *never*. The fix was `!isLocalAuthMode || httpsConfigured`. Symmetrically, a Cloud deploy that *did* set a cert path would have had Kestrel `ListenAnyIP` override its `ASPNETCORE_URLS` bind.
**Recommendation:** When the invariant is "mode X is unchanged," gate new behavior on the **mode** flag itself, not on a capability/feature flag that merely *correlates* with the mode. A capability flag can be toggled in the protected mode later and silently break the invariant.

---

## Pitfalls

### Stateless JWT means server-side state changes don't take effect until expiry
**Discovered in:** windows-desktop-app (review Findings 1 & 2)
**Context:** Local sessions are a stateless 12h JWT; `ClinicContext` reads only JWT claims and never re-loads the `User` per request.
**Problem:** Deactivating a user, resetting their password, or setting "must change password" has **no effect on an already-issued token**. Client-side enforcement (a `local_must_change_password` cookie checked in middleware) is user-deletable, and the raw bearer token works directly against every API. `IsActive`/`MustChangePassword` are only checked at the login gate, not per request.
**Recommendation:** For access-control that must revoke or restrict *live* sessions, enforce server-side — a per-request `IsActive` check, a token-version/`must_change` claim, or a much shorter token lifetime. Do not rely on a frontend cookie as the sole gate.

### `secure` cookie keyed on `NODE_ENV` breaks login over plain HTTP
**Discovered in:** windows-desktop-app (review Finding 3)
**Context:** Session cookie set with `secure: process.env.NODE_ENV === 'production'` on a LAN install served over HTTP (HTTPS is a later phase).
**Problem:** A production build over HTTP sets `secure: true`, so the browser silently refuses to store/send the cookie — the user bounces back to `/login` with no error to diagnose.
**Recommendation:** Drive the cookie `secure` flag off an explicit config flag (or the request's actual scheme), never off `NODE_ENV`, when the deployment may legitimately run over HTTP.

### Legacy `JwtSecurityTokenHandler` can't re-read its own `iss` claim on .NET 8
**Discovered in:** windows-desktop-app
**Context:** Offline verification of the local JWT issue→validate round-trip.
**Problem:** ASP.NET Core 8 `JwtBearer` validates with `JsonWebTokenHandler` by default (`UseSecurityTokenValidators = false`). The legacy `JwtSecurityTokenHandler` in Microsoft.IdentityModel 7.1.2 fails to read its own `iss` claim on re-parse (returns empty). Production is unaffected because it uses the modern handler.
**Recommendation:** Verify JWT round-trips with `JsonWebTokenHandler` (what the runtime actually uses). **Do not** set `JwtBearerOptions.UseSecurityTokenValidators = true` — local-token issuer validation would break.

### A debounce window shorter than the poll interval suppresses nothing
**Discovered in:** windows-desktop-app (Phase 3, review Finding 1)
**Context:** A 3s debounce (`DEBOUNCE_MS`) meant to coalesce flapping connectivity, driven by a 15s poll (`POLL_INTERVAL_MS`).
**Problem:** Two differing readings can never arrive within a 3s window when polls are 15s apart, so the debounce's clear/re-arm branch is unreachable — it only *delays* every genuine transition by 3s and provides **zero** flap suppression. A server alternating reachable/unreachable each poll still toggles the UI (and fires a toast) every 15s.
**Recommendation:** To actually suppress flapping, require N consecutive stable readings before applying a transition (or use a confirmation window ≥ the poll interval). A debounce only helps when the event source can fire faster than the window.

### Space-based UI gating can hide a required affordance entirely
**Discovered in:** windows-desktop-app (Phase 3, review Finding 2)
**Context:** A "not synced" badge + "Push to Google" action (required by AC-6.6 for *any* unsynced appointment) rendered only when `!isVerySmall` (card taller than ~24 min of duration).
**Problem:** Every short appointment (common 15-/20-min slots) rendered neither control, and no alternative surface (e.g. the edit dialog) offered the action — so a whole class of items could never satisfy a mandatory requirement. "Hide when there's no room" silently became "feature doesn't exist here."
**Recommendation:** When an affordance is *required*, never let a layout/size heuristic be its only gate. Provide a compact (icon-only) fallback or a second stable surface (dialog/menu) so the capability always exists regardless of available space.

### Blanket `catch → return/cache false` masks real faults and can poison a shared cache
**Discovered in:** windows-desktop-app (Phase 3, review Finding 3)
**Context:** `InternetProbe.ProbeAsync` wraps the outbound probe in `catch (Exception) { return false; }`, and the result is cached for the whole TTL in a shared singleton `IMemoryCache`.
**Problem:** (a) A genuine config/programming fault (e.g. a malformed `ProbeUrl` → `UriFormatException`) is silently reported as "no internet" and only logged at `Debug`, hiding a real bug behind a plausible state. (b) If a caller cancellation token is ever wired, an `OperationCanceledException` gets swallowed as `false` and **cached for the full TTL — every LAN client then shows "offline" until it expires.**
**Recommendation:** Narrow the catch to the expected transport exceptions (`HttpRequestException`/`TaskCanceledException`/`TimeoutException`); re-throw (and never cache) when the caller's own token requested cancellation; and don't let a swallowed exception silently populate a *shared* cache. Log unexpected failures above `Debug`.

### Security/transport config must fail closed and loud, not silently degrade
**Discovered in:** windows-desktop-app (Phase 4, review Findings 2 & 7)
**Context:** `httpsConfigured = !IsNullOrWhiteSpace(certPath) && File.Exists(certPath)` folded "no cert wanted" and "cert wanted but file missing" into one silent `false`; separately, `LocalRequest.IsLoopback` returns `true` when `RemoteIpAddress` is null (a security gate defaulting to *allow*).
**Problem:** When an operator *sets* `Https:CertPath` but the file is missing (typo/wrong CWD/not-yet-provisioned), the app binds plain HTTP, skips redirection, logs **nothing** — PHI travels in cleartext while the operator believes TLS is on. Transport failed *open*. Likewise a gate that allows on missing information is fragile if the hosting topology ever changes (proxy leaving peer IP unset). A bad-password cert, by contrast, throws and fails loud — the safe behavior.
**Recommendation:** For security-relevant config, an *explicitly-requested-but-unsatisfiable* setting should warn loudly or refuse to start — never silently fall back to the less-secure path. Log the chosen posture on startup (transport + ports) so it is observable. Default security gates to *deny* on missing/ambiguous input.

### Anonymous OAuth callback + unvalidated `state` = shared-token hijack
**Discovered in:** windows-desktop-app (Phase 4, review Finding 1)
**Context:** `GoogleCalendar.Authorize`/`Callback` are both `[AllowAnonymous]` (Google redirects the browser back with no bearer), the callback overwrites the clinic's single shared refresh token, and the generated `state` was accepted but never validated.
**Problem:** On a LAN, any unauthenticated user can hit `/authorize`, consent with **their own** Google account, and the callback silently overwrites the shared token — after which the app pushes patient data to an attacker's calendar. The missing `state` check also leaves the flow open to OAuth CSRF.
**Recommendation:** An OAuth callback legitimately can't require a bearer, so protect it differently: (a) generate `state` bound to an authenticated initiator/session and **reject the callback on mismatch**, and (b) gate the `authorize` *initiation* behind an authenticated session. Never leave a generated `state` unvalidated — an accepted-and-ignored anti-CSRF token is worse than none (it looks protected).

### Shared-Singleton "atomic" file writes need a unique temp path per write
**Discovered in:** windows-desktop-app (Phase 4, review Finding 6)
**Context:** A thread-safe Singleton token store did `WriteAllTextAsync(fixedTmp)` + `File.Move` using a **fixed shared** temp path (`_filePath + ".tmp"`), with the file I/O running *outside* the lock that guarded the in-memory cache.
**Problem:** Two concurrent saves write to and move the *same* `.tmp` file → `IOException` or interleaved/corrupt writes. The stage-then-move idiom is only atomic if each in-flight write has its **own** staging file.
**Recommendation:** Make the temp name unique per write (`_filePath + "." + Guid.NewGuid().ToString("N") + ".tmp"`) before `File.Move`. If a type documents itself as thread-safe, ensure the guarantee covers the *whole* operation, not just the cheap in-memory tail under the lock.

### Grep for *every* symbol a namespace provides before dropping its `using`
**Discovered in:** windows-desktop-app (Phase 4)
**Context:** After extracting a loopback helper out of `AuthController`, `using System.Net;` looked unused and was removed.
**Problem:** The build broke — `HttpStatusCode` (also in `System.Net`) was still used elsewhere in the file. Namespaces provide many types; the one you moved is not necessarily the only one in use.
**Recommendation:** Before deleting a `using`, search the file for *all* types that namespace could supply, not just the symbol you refactored. Rely on the compiler/`tsc`/build as the final check, but don't assume "I moved the obvious user" means the import is dead.

---

## Conventions

### Per-install signing key: never in appsettings, resolved through one shared config
**Discovered in:** windows-desktop-app
**Context:** Signing key for locally-issued JWTs.
**Learning:** The key resolves via `LocalAuthConfig` — explicit `Auth:Local:SigningKey`, else a generated 512-bit CSPRNG key persisted to a gitignored `.local/signing-key`. The **same** config path is used by both the issuer and the validator so they can never drift.
**Recommendation:** Never commit signing keys or place them in `appsettings.json`. Share one resolution path between issuer and validator. Cache the resolved bytes once — don't do disk I/O on every token issuance (review Finding 12).

### Use a CSPRNG for anything security-relevant, never `new Random()`
**Discovered in:** windows-desktop-app (review Finding 5)
**Context:** Clinic self-registration code — the sole gate for the anonymous, LAN-reachable `POST /api/auth/register`.
**Learning:** `new Random()` is non-cryptographic and time-seeded. Any value that gates access (codes, temp passwords, tokens) must use `RandomNumberGenerator` (as `LocalAuthService.GenerateTemporaryPassword` already does).
**Recommendation:** Default to `RandomNumberGenerator` for generated secrets/codes; consider length adequate to the brute-force surface. Extract one shared generator rather than copy-pasting the logic (review Finding 10).

### One shared constant for policy values enforced in multiple places
**Discovered in:** windows-desktop-app (review Finding 9)
**Context:** Minimum password length (`8`) enforced at three sites, hardcoded at two.
**Learning:** Duplicated policy literals drift. `ChangePasswordCommandHandler` already had `MinPasswordLength`, but the two clinic commands hardcoded `8`.
**Recommendation:** Promote a single shared constant and reference it from every enforcement site.

### The central error parser must cover every server error shape
**Discovered in:** windows-desktop-app (Phase 3, review Finding 4)
**Context:** Moving raw-`fetch` calls onto `client.ts`, whose `handleResponse` reads only `errorData.title || .message || .errors`. `GoogleCalendarController` returns failures as `{ error: "..." }`.
**Learning:** `client.ts` never inspects `.error`, so the specific, actionable server message (e.g. "Google Calendar is not configured…") was silently dropped to a generic `HTTP 4xx: <statusText>` for the existing Cloud caller — a behavior regression from routing calls through the wrapper.
**Recommendation:** When a shared client parses server errors, make it read *every* shape the backend actually returns (add `.error` to the fallback chain), or standardize the backend on ProblemDetails (`title`/`detail`). Auditing the server's real error bodies is part of the cost of unifying calls onto one wrapper.

### Handle in-flight connectivity loss uniformly across every network call site
**Discovered in:** windows-desktop-app (Phase 3, review Finding 5)
**Context:** AI chat and "Push to Google" special-case `ApiError.status === 0` with a retryable "connexion perdue" toast; `handleSyncFromGoogle` did not and fell through to a generic `alert`.
**Learning:** Gating a button on connectivity does not cover a *mid-request* drop — that still hits the call site's catch. Inconsistent handling across sites yields a polished message on one path and a raw error on another for the same failure.
**Recommendation:** Once you adopt an offline-loss signal (`ApiError(status: 0)`), apply the same branch at *all* network call sites, ideally via a shared helper/toast rather than per-site copies. Prefer toasts over `alert`.

### Distinguish "not configured" from "network unavailable" for each gated feature
**Discovered in:** windows-desktop-app (Phase 3, review Finding 6, FR-D3)
**Context:** AI chat gates purely on `internetReachable`; `ConnectivityStatusDto` carries no AI-config flag. Google Calendar *does* distinguish this via its pre-existing `getStatus().isConfigured`.
**Learning:** When internet is up but the feature is unconfigured server-side, an internet-only gate leaves the widget looking fully enabled and produces a generic failure on use — conflating two distinct states the spec required to be separable.
**Recommendation:** Every feature that can be both "unconfigured" and "offline" needs *both* signals surfaced to the UI (a config/status flag alongside the connectivity flag), so each state gets its own affordance.

### Don't add secret-bearing keys to tracked `appsettings.json` — even empty ones
**Discovered in:** windows-desktop-app (Phase 4, review Finding 5)
**Context:** An empty `Https:CertPassword` slot was added to the committed `appsettings.json` in the same phase that *removed* the committed-secret anti-pattern for the Google refresh token (the `IGoogleTokenStore` + `.local/` rationale).
**Learning:** An empty secret slot in tracked config invites an operator to paste the real secret (here, a PFX private-key password) straight into version control — reintroducing exactly the debt the phase eliminated. A leaked PFX password compromises the server's TLS key.
**Recommendation:** Source secrets from the gitignored `.local/` store, environment variables, or user-secrets — the same path already used for the signing key and refresh token. Don't add a secret *key* to committed appsettings at all; if a slot must exist, document that it may only be set in an untracked override. Extends the "per-install signing key: never in appsettings" convention above to *all* secret-bearing config.

---

## Tools & Libraries

### `JwtBearer` package is only referenced by the API project
**Discovered in:** windows-desktop-app
**Context:** `LocalAuthService` lives in Infrastructure and needs to issue JWTs.
**Learning:** The plan assumed `JwtSecurityTokenHandler` was transitively available in Infrastructure via `JwtBearer`, but `JwtBearer` is referenced only by the API project. `System.IdentityModel.Tokens.Jwt` had to be added explicitly to Infrastructure.
**Recommendation:** When moving token-issuance code into Infrastructure, add `System.IdentityModel.Tokens.Jwt` there explicitly — don't rely on transitive JwtBearer references from the API project.

### `web/` has no unit-test runner and no ESLint; the FE quality gate is `tsc --noEmit` + `npm run build`
**Discovered in:** windows-desktop-app (Phases 1–3, recurring)
**Context:** Verifying frontend changes for the desktop-app phases.
**Learning:** There is no vitest/jest in `web/`, and ESLint is not installed (`next build` has lint disabled via `next.config.ts`). So the effective, only frontend gate is `npx tsc --noEmit` + `npm run build` (both must be clean). FE-behavior acceptance criteria are covered by implementation + deferred manual verification, not automated tests.
**Recommendation:** Use `tsc --noEmit` + `npm run build` as the FE gate and don't count the absent test/lint runners as a coverage gap for these phases. If a later phase needs automated FE coverage (e.g. of connectivity/gating logic), standing up a test runner is a prerequisite — plan for it explicitly.

### A test project exercising a Web-SDK API needs an explicit ASP.NET `FrameworkReference`
**Discovered in:** windows-desktop-app (Phase 4)
**Context:** Unit-testing `Program.cs`-adjacent types (authorization options, `DefaultHttpContext`, MVC `ControllerBase`) that live in the `Microsoft.NET.Sdk.Web` API project, from a `Microsoft.NET.Sdk` test project.
**Learning:** The API's *implicit* `FrameworkReference Include="Microsoft.AspNetCore.App"` (from the Web SDK) does **not** flow transitively to a plain `Microsoft.NET.Sdk` test project. Without adding it explicitly, ASP.NET types won't resolve even though the test project references the API project.
**Recommendation:** Add `<FrameworkReference Include="Microsoft.AspNetCore.App" />` to any non-Web test project that touches ASP.NET types. Relatedly: `DenyAnonymousAuthorizationRequirement` (used to assert `RequireAuthenticatedUser()`) lives in `Microsoft.AspNetCore.Authorization.Infrastructure`, not the root `Microsoft.AspNetCore.Authorization`.
