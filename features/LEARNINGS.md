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
