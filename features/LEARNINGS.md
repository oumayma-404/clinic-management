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

### A service constructed before `builder.Build()` has no DI — give it a real logger, and don't leave a dead registration
**Discovered in:** windows-desktop-app (Phase 5, S3 + review Finding 17)
**Context:** The HTTPS `CertificateProvisioner` must run *before* `builder.Build()` (Kestrel needs the cert before the DI container exists), so it's `new`-ed directly in `Program.cs`.
**Learning:** A pre-Build service can't be resolved from DI, so it was constructed with a `NullLogger` — which silently swallowed its own "generated vs reused certificate" log lines. It was *also* left `AddSingleton`-registered "for completeness," a dead registration nothing ever resolves. Both are traps: invisible logs and misleading DI wiring.
**Recommendation:** For anything built pre-Build, pass a real logger (e.g. a Serilog-backed `SerilogLoggerFactory(Log.Logger).CreateLogger<T>()`, since Serilog is configured first) rather than `NullLogger`, and do **not** DI-register a type that is only ever constructed manually — the registration reads as wired-up when it isn't.

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

### A reverse-proxy/loopback hop makes request-scheme-derived security decisions on the *internal* leg
**Discovered in:** windows-desktop-app (Phase 5, story review)
**Context:** A Next BFF login handler sets the auth cookie `Secure` flag from `request.nextUrl.protocol`, while the browser reaches it over the Kestrel HTTPS front door which proxies to the Node server on a plain-HTTP `localhost` hop.
**Problem:** The handler runs on the *internal* HTTP hop, so `protocol` is `http:` and it derives `Secure=false` — even though the browser transport is HTTPS. Any request-scheme-derived decision (cookie `Secure`, redirect scheme) behind a front door is made on the wrong leg.
**Recommendation:** Behind a reverse proxy / loopback front door, don't derive transport-security decisions from the local request scheme. Use an explicit override (here `AUTH_COOKIE_SECURE=true`, set by the service registration) or trusted `X-Forwarded-Proto` handling.

### A multi-step operation must delete its partial output on failure
**Discovered in:** windows-desktop-app (Phase 5, review Finding 1)
**Context:** A backup creates a timestamped folder, then dumps the DB, then copies files — any step can throw.
**Problem:** The folder was created before the dump/copy and never removed on failure, leaving a partial `clinic-backup-<ts>/` that *looks* complete — an operator could restore from a truncated dump, the opposite of "no silent partial success."
**Recommendation:** Wrap the artifact-producing steps in try/catch that best-effort deletes the partial output before rethrowing (catch bare so cancellation cleans up too), so only complete artifacts remain on disk.

### Make a loopback-only guarantee a property of the bind, not a firewall rule
**Discovered in:** windows-desktop-app (Phase 5, review Finding 2)
**Context:** In Local mode the API's plain-HTTP port was bound with `ListenAnyIP` (all interfaces); the only thing keeping LAN clients off the cleartext API (incl. `POST /login`) was one `netsh` firewall rule.
**Problem:** If that rule is removed/reordered or the firewall is disabled, the entire cleartext API is LAN-reachable — and the request body is on the wire before any HTTPS redirect can act. A single breakable control guarded a security boundary.
**Recommendation:** When a port must be loopback-only, bind it to loopback (`ListenLocalhost`) so the guarantee is structural. Keep the LAN-facing surface to the intended (TLS) endpoint only; never let a firewall rule be the sole thing enforcing a network boundary.

### An orchestration script must check every external step's exit code and abort loudly
**Discovered in:** windows-desktop-app (Phase 5, review Finding 5)
**Context:** An installer procedure ran `initdb` / `sc start` / `pg_isready` / `CREATE ROLE` / `CREATE DATABASE` (the last with `ON_ERROR_STOP=0`) then returned `True` unconditionally.
**Problem:** Any failed step (DB service won't start, role/DB creation fails) was swallowed; the installer reported "completed successfully" while the app then failed at boot against a missing role/DB. The install failed *open* and silent.
**Recommendation:** In a script orchestrating external commands, capture and check each exit code, guard idempotent DDL (`\gexec` / existence check + `ON_ERROR_STOP=1`), and abort with a clear message on any hard failure — never return success unconditionally.

### Don't declare a hard dependency on a resource created later or conditionally in the same routine
**Discovered in:** windows-desktop-app (Phase 5, review Finding 6)
**Context:** A Windows service was `sc create`d with `depend= DbSvc/WebSvc`, but the Web service was registered *afterward* in the same procedure and only if an optional tool (`nssm.exe`) was present.
**Problem:** When the tool was absent, the API service depended on a Web service that was never created → `sc start` fails with 1068 (dependency missing), leaving the API dead while the installer reported success.
**Recommendation:** Create a dependency's target *before* the dependent, and make the dependency **conditional** on the target actually having been created (drop it, or fail the install, when the optional component is missing).

### Guard browser globals (`window`) in any module importable server-side
**Discovered in:** windows-desktop-app (Phase 5, review Finding 11)
**Context:** A shared API client built a URL with `new URL(path, window.location.origin)` — needed only for the relative same-origin base.
**Problem:** `window.location.origin` is evaluated unconditionally, so any SSR render pass, `generateMetadata`, or Node unit test importing the module throws `ReferenceError: window is not defined` before the URL is built — latent only because all current callers live in `useEffect`/handlers.
**Recommendation:** In modules that can be imported on the server (Next.js client components still render server-side), guard browser globals: `const base = typeof window !== "undefined" ? window.location.origin : undefined;`. Don't rely on "it only runs client-side today."

### Enforce DB password auth at init — never rely on `-A trust` / network isolation on a shared host
**Discovered in:** windows-desktop-app (Phase 5, review Finding 10)
**Context:** The bundled PostgreSQL cluster was initialized with `initdb -A trust`, with a generated `clinic_user` password baked into the connection string but never actually enforced.
**Problem:** With `trust`, any OS user/process on the server PC can connect as any role — including `postgres` superuser — with no password, reaching all PHI. On a shared/multi-account Windows host that's local privilege escalation to the full database; the "random per-install password" was illusory.
**Recommendation:** Initialize with `scram-sha-256` (or `md5`) so generated passwords are enforced, and supply the superuser password via `--pwfile`, then bootstrap with a temporary `pgpass.conf` (deleted after). Don't treat "loopback-only" as a substitute for authentication on a machine other accounts can use.

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
**Recommendation:** Default to `RandomNumberGenerator` for generated secrets/codes; consider length adequate to the brute-force surface. Extract one shared generator rather than copy-pasting the logic (review Finding 10). **Extends to installer/build scripts:** Inno Setup's Pascal `Random` is non-cryptographic *and* unseeded (no `Randomize` ⇒ identical value across installs). For a generated per-install secret, source bytes from the OS CSPRNG (`BCryptGenRandom@bcrypt.dll`) rather than `Random` (Phase 5 Finding 12).

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

### A store-deletion keyed on a literal must match the exact value the generator produces — pin both to one source of truth
**Discovered in:** windows-desktop-app (Phase 5, review Finding 3)
**Context:** The client uninstaller ran `certutil -delstore Root "Clinic Management CA"`, but the server's `CertificateProvisioner` generates the CA with subject CN **`Clinic Management Local CA`**.
**Learning:** `certutil -delstore` matches by (substring of the) subject name; the literals didn't match, so uninstall removed nothing and the self-signed root CA stayed permanently trusted on every staff PC — a lingering trust anchor. Two hand-written copies of a cross-artifact identifier drifted.
**Recommendation:** When one artifact deletes/looks up what another produces by a literal name, both must reference the same value (a shared constant, or delete by thumbprint), and a test should assert the generator's actual output (as `CertificateProvisionerTests` pins the CN). Never hand-copy a cross-artifact identifier into a second file.

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

### `Microsoft.Extensions.Hosting.WindowsServices` 8.0.1 pins `System.Diagnostics.EventLog` 8.0.1
**Discovered in:** windows-desktop-app (Phase 5, S2)
**Context:** Adding Windows-service hosting + Event Log startup diagnostics to the API.
**Learning:** `Microsoft.Extensions.Hosting.WindowsServices` 8.0.1 transitively pins `System.Diagnostics.EventLog` to 8.0.1; adding `EventLog` at 8.0.0 trips `NU1605` (package downgrade treated as error) and fails the build.
**Recommendation:** When adding a package that a Windows-service/hosting dependency already pins transitively, match the transitive version (here 8.0.1) rather than the repo's default 8.0.0.

### PowerShell 5.1 `Set-Content -Encoding UTF8` writes a UTF-8 **BOM**
**Discovered in:** windows-desktop-app (Phase 5, review Finding 14)
**Context:** A publish script rewrote the scrubbed `appsettings.json` with `Set-Content -Encoding UTF8` under Windows PowerShell 5.1 (`powershell.exe`).
**Learning:** On WinPS 5.1, `-Encoding UTF8` emits a BOM. ASP.NET Core's stream-based config provider tolerates it, but any consumer that reads the file as a *string* into `System.Text.Json` throws on the leading BOM, and a BOM in `appsettings.json` is fragile/non-idiomatic.
**Recommendation:** Write BOM-less UTF-8 explicitly — `[System.IO.File]::WriteAllText($path, $json, (New-Object System.Text.UTF8Encoding($false)))` — for any file other tooling reads as text. (pwsh 7's `-Encoding utf8` is already BOM-less, but don't assume the script runs there.)
