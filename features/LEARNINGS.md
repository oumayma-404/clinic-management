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

---

## Tools & Libraries

### `JwtBearer` package is only referenced by the API project
**Discovered in:** windows-desktop-app
**Context:** `LocalAuthService` lives in Infrastructure and needs to issue JWTs.
**Learning:** The plan assumed `JwtSecurityTokenHandler` was transitively available in Infrastructure via `JwtBearer`, but `JwtBearer` is referenced only by the API project. `System.IdentityModel.Tokens.Jwt` had to be added explicitly to Infrastructure.
**Recommendation:** When moving token-issuance code into Infrastructure, add `System.IdentityModel.Tokens.Jwt` there explicitly — don't rely on transitive JwtBearer references from the API project.
