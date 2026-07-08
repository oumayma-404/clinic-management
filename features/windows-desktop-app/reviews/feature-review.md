# Feature Review: windows-desktop-app (Phase 1 — Pluggable Auth + Local Accounts)

**Status:** COMPLETE
**Challenged:** Yes
**Date:** 2026-07-08
**Challenged Date:** 2026-07-08
**Parent Branch:** main
**Merge Base:** 9798b95d31f55ee07f2ad5e0af5550c4c2831022
**Files Reviewed:** 88 changed files (+5,482 / -168 reviewable lines).

**Scope note:** Phase 1 covers **FR-A + FR-B** only. HTTPS on LAN, CORS, "auth on all endpoints", and Hangfire lockdown are **Phase 4 (FR-E)**; local-disk file storage is **Phase 2 (FR-C)** — findings in those areas were not raised. Verified sound during the challenge: password hashing uses ASP.NET `PasswordHasher` (PBKDF2-HMAC-SHA256, per-user salt, constant-time verify — `LocalAuthService`); the JWT signing key is a 512-bit CSPRNG per-install key in a gitignored `.local/signing-key`, never in `appsettings.json` (`LocalAuthConfig.ResolveSigningKey`); token issuer/audience/lifetime/signature all validated (HS256); temp passwords + the setup localhost gate (`IPAddress.IsLoopback` on the real `RemoteIpAddress`, no `ForwardedHeaders` middleware) are solid; the admin-recovery service is console-only and deliberately not DI-registered; the EF migration is additive with a partial unique email index.

## Challenge Summary

| Metric | Count |
|--------|-------|
| Original findings | 28 |
| Confirmed | 21 |
| Confirmed (adjusted) | 3 |
| Dismissed (false positive) | 2 |
| Dismissed (pre-existing) | 0 |
| **Final findings** | 26 |

**Dismissed during challenge:**
- *(orig. Finding 15) cookie `expires` may expire immediately* — **False positive.** `LoginResultDto.ExpiresAt` is a `DateTime`; System.Text.Json serializes it as an ISO 8601 string, so `new Date(expiresAt)` in `local-login/route.ts` produces a valid future date. The "epoch-number → 1970" failure scenario cannot occur against the actual contract.
- *(orig. Finding 26) `/api/auth/token` hands the raw JWT to client JS* — **Dismissed (pre-existing / by-design).** The Local branch replicates the app's established bearer-token seam, which FR-A4 explicitly mandates reusing, and mirrors the unchanged Cloud/Auth0 branch in the same file. The finding itself stated it was "not newly introduced." Not an actionable finding for this feature.

**Severity adjusted during challenge:** Findings 1 and 5 (Major → Minor) and Finding 24 (name-claim premise corrected) — see the `Challenge note` on each.

## Findings

### Finding 1
- **Severity:** Major
- **Category:** Security / Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Auth/Commands/LoginCommand.cs
- **Line:** 75
- **Anchor:** `LoginCommandHandler.Handle`
- **Comment:** The "must change password" requirement (AC-5.2, after an admin reset or CLI recovery) is not enforced server-side. Login issues a full, 12h, fully-privileged JWT even when `user.MustChangePassword == true` — it only copies the flag into `LoginResultDto` (line 84). The sole enforcement is the frontend `local_must_change_password` cookie checked in `web/middleware.ts`, which is user-deletable, and the bearer token from `/api/auth/token` works against every API directly. A user handed a temporary password can skip the forced change and keep operating with the admin-known credential. Verified: `ClinicContext` reads only JWT claims (no per-request DB load), and no endpoint gates on `MustChangePassword`. Also confirmed: an already-logged-in user whose password an admin resets is never forced to change it (the cookie is stamped only at login). Fix: enforce server-side — embed a `must_change` claim and reject all endpoints except `change-password`, or return a restricted/short-lived token until the password is changed.

### Finding 2
- **Severity:** Major
- **Category:** Business Logic / Security
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Auth/Commands/LoginCommand.cs
- **Line:** 55
- **Anchor:** `LoginCommandHandler.Handle`
- **Comment:** The Local session is a stateless JWT valid for 12h (`LocalAuthConfig.DefaultTokenLifetimeMinutes = 720`) with no server-side revocation and no per-request account re-check. Confirmed against source: `ClinicContext` (`GetClinicId`/`GetUserRole`/`GetUserId`/`GetUserEmail`) reads **only JWT claims** — it never loads the `User`, so `User.Deactivate()` (which sets `IsActive = false`) has no effect on an already-issued token. `LoginCommand` checks `IsActive` only at the login gate (line 55). Result: deactivating a user (AC-5.3) or resetting their password does not cut off their existing token — a technical user who extracted the bearer token retains full API access to patient data for up to 12h (the 30-min frontend inactivity logout does not bound direct API calls). The literal AC-5.3 ("cannot log in") is met, but given the spec's Non-Functional security hint (medical data on a directly-reachable LAN), the missing session revocation is a real access-control gap. Fix: per-request `IsActive` check (and optionally a token-version claim), or a much shorter token lifetime.

### Finding 3
- **Severity:** Major
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** web/app/api/auth/local-login/route.ts
- **Line:** 40
- **Anchor:** `POST` — `SESSION_COOKIE` set (also `MUST_CHANGE_COOKIE` at line 49)
- **Comment:** `secure: process.env.NODE_ENV === 'production'` will break login for a Phase-1 install served over plain HTTP on the LAN (HTTPS is Phase 4). A production build (`NODE_ENV === 'production'`) over HTTP sets `secure: true`, so the browser silently refuses to store/send the session cookie and the user bounces back to `/login` after a successful auth — with no error to diagnose. The spec ships phases independently, so a Local install before Phase 4 is a realistic HTTP scenario. Fix: drive `secure` off an explicit config flag (defaulting false in Local mode) or off the request's actual scheme, not `NODE_ENV`. Same fix for `MUST_CHANGE_COOKIE` (line 49).

### Finding 4
- **Severity:** Minor
- **Category:** Breaking Change / Security
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** api/ClinicManagement.API/Controllers/AuthController.cs
- **Line:** 46
- **Anchor:** `AuthController.Login`
- **Comment:** `POST /api/auth/login` is `[AllowAnonymous]` but — unlike `Setup` (line 75) and `Register` (line 116), which both begin with `if (!LocalAuthConfig.IsLocalMode(_configuration)) return NotFound();` — has no mode guard, so it stays live and anonymous in a Cloud deployment. Gate it identically (return `NotFound()` when not in Local mode) for consistency with the spec's "local login is Local-only" (FR-A).
- **Challenge note:** Lowered Major → Minor. Verified real impact in Cloud is low: `GetByEmailAsync` filters `PasswordHash IS NOT NULL`, and Cloud never creates local accounts, so the endpoint always returns the generic `InvalidCredentialsError` — no auth bypass (Cloud validates Auth0-signed tokens, not the local key), no user enumeration (uniform response), no data exposure. Residual concern is only an unintended anonymous DB-touching endpoint, consistent with the app's existing `[AllowAnonymous]` controllers. A hardening/consistency fix, not a Major break.

### Finding 5
- **Severity:** Minor
- **Category:** Security
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Clinics/Commands/RegenerateClinicCodeCommand.cs
- **Line:** 96
- **Anchor:** `RegenerateClinicCodeCommandHandler.GenerateClinicCode` (same defect at `CreateClinicCommand.cs:314`)
- **Comment:** The clinic self-registration code is minted with `new Random()`, a non-cryptographic, time-seeded PRNG. In Local mode this 6-char (~31-bit) code is the sole gate for the anonymous, LAN-reachable `POST /api/auth/register` (`AuthController.Register` is `[AllowAnonymous]`), which creates real doctor/secretary accounts with patient-data access. The brute-force lockout (`User.MaxFailedLoginAttempts`) applies only to per-account login, not to code-guessing on `register`. Fix: mint the code with `RandomNumberGenerator` (as `LocalAuthService.GenerateTemporaryPassword` already does) and consider lengthening it.

### Finding 6
- **Severity:** Minor
- **Category:** Security
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Auth/Commands/LoginCommand.cs
- **Line:** 57
- **Anchor:** `LoginCommandHandler.Handle` (IsActive / IsLockedOut branches)
- **Comment:** The handler returns a generic `InvalidCredentialsError` for unknown-email and wrong-password (good, per its own comment), but the deactivated (line 57) and locked-out (line 62) branches — which run **before** password verification (line 65) — return distinct messages that confirm the account exists, defeating the stated anti-enumeration intent. There is also a timing oracle (unknown email returns before any hash work; existing user runs PBKDF2). Low impact under the trusted-LAN threat model, but it contradicts the code's own design. Fix: keep pre-authentication responses uniform (disclose active/locked state only after a correct password), optionally run a dummy hash for unknown emails.

### Finding 7
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Common/Maintenance/AdminPasswordRecoveryService.cs
- **Line:** 81
- **Anchor:** `AdminPasswordRecoveryService.ResetAdminPasswordAsync`
- **Comment:** The offline recovery utility (FR-B6, the only documented recovery path) calls `admin.SetPassword(hash, mustChangePassword: true)`, which clears lockout/failed-attempt state but does not set `IsActive = true` (confirmed: `User.SetPassword` at User.cs:89 leaves `IsActive` untouched). Because `LoginCommand` rejects inactive accounts before the password check, a deactivated admin still cannot log in after a "successful" reset. Reachable in a multi-admin clinic (one admin can deactivate another — `SetUserActiveCommand` only blocks self-deactivation, not deactivating a peer admin). The recovery utility should defensively `Activate()` the admin so it is a complete, guaranteed recovery path.

### Finding 8
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Clinics/Commands/JoinClinicCommand.cs
- **Line:** 237
- **Anchor:** `JoinClinicCommandHandler.RegisterLocalUserAsync`
- **Comment:** The `User` is created via `User.CreateLocalUser(...)` (line 225), which trims + lower-cases the email (User.cs:66), but the linked `Doctor` row is created with the raw `request.Email` (line 237). The doctor's contact email can therefore differ in case/whitespace from the account email — two records diverging for the same person. Fix: normalize the email once and pass the normalized value to both `User` and `Doctor`.

### Finding 9
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Clinics/Commands/CreateClinicCommand.cs
- **Line:** 270
- **Anchor:** `CreateClinicCommandHandler.CreateLocalFirstRunAsync`
- **Comment:** The minimum-password length `8` is a magic literal here (`request.Password!.Length < 8`) and again in `JoinClinicCommand.RegisterLocalUserAsync` (line 194), while `ChangePasswordCommandHandler` already defines `private const int MinPasswordLength = 8` (line 22). Three enforcement sites, two hardcoded — the policy can drift. Promote a single shared constant and reference it from all three.

### Finding 10
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Clinics/Commands/RegenerateClinicCodeCommand.cs
- **Line:** 92
- **Anchor:** `RegenerateClinicCodeCommandHandler.GenerateClinicCode`
- **Comment:** `GenerateClinicCode()` (lines 92-99) is copy-pasted verbatim from `CreateClinicCommandHandler.GenerateClinicCode()` (`CreateClinicCommand.cs:310-317`) — same alphabet, same 6-char logic, same `new Random()`. DRY violation across two handlers in the same folder. Extract one shared generator (a static helper in the Clinics namespace or on the `Clinic` domain type) and fold the CSPRNG fix from Finding 5 into it.

### Finding 11
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Auth/Commands/ChangePasswordCommand.cs
- **Line:** 78
- **Anchor:** `ChangePasswordCommandHandler.Handle` (catch block)
- **Comment:** The catch returns `$"Error changing password: {ex.Message}"`, echoing the internal exception message to the caller — inconsistent with the sibling `LoginCommandHandler`, which deliberately swallows the detail ("do not echo internal exception details to the caller", LoginCommand.cs:100). For an authentication surface, mirror `LoginCommand`: return a generic message and rely on logging for the detail.

### Finding 12
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Auth/LocalAuthConfig.cs
- **Line:** 47
- **Anchor:** `LocalAuthConfig.ResolveSigningKey`
- **Comment:** `SecurityKey()` → `ResolveSigningKey()` does synchronous disk I/O (`File.Exists` + `File.ReadAllText`, line 77) on every call, and `LocalAuthService.GenerateToken` calls it on every login/token issuance. The key is per-install and immutable at runtime — cache the resolved bytes once (memoize, or register `SymmetricSecurityKey` as a singleton). Also: the existing-file branch calls `Convert.FromBase64String(existing)` (line 80) without the try/catch guard the configured-value branch has (line 55) — a corrupted key file throws an unhandled `FormatException` at token time.

### Finding 13
- **Severity:** Minor
- **Category:** Breaking Change / Frontend
- **Verdict:** Confirmed
- **File:** web/lib/auth/session.tsx
- **Line:** 48
- **Anchor:** `CloudBridge`
- **Comment:** In Cloud mode `CloudBridge` rebuilds the context `value` — including a new `user` object literal (lines 49-51) — on every render, and the `Provider value` is not memoized. Confirmed that `useAuthToken` (use-auth-token.ts:7,41) now reads `useSession()` with effect deps `[user, isLoading]`, so every `CloudBridge` re-render yields a new `user` identity → a redundant `/api/auth/token` refetch and extra consumer re-renders on the (unchanged) Cloud path. Not correctness-breaking, but a perf regression versus Auth0's previously reference-stable `useUser`. Fix: `useMemo` the `value`/`user` keyed on the primitive fields.

### Finding 14
- **Severity:** Minor
- **Category:** Frontend / Security
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** web/app/login/page.tsx
- **Line:** 81
- **Anchor:** `LocalLoginForm.handleSubmit`
- **Comment:** Open-redirect: `returnTo.startsWith('/')` accepts protocol-relative URLs like `//evil.com`, which `window.location.href` treats as an absolute off-site navigation. Fix: reject `//` (and `/\`): `returnTo && returnTo.startsWith('/') && !returnTo.startsWith('//') ? returnTo : '/'`.
- **Challenge note:** Lowered Major → Minor. Verified the code path is local-mode only (`LocalLoginForm`, gated at login/page.tsx:34); the redirect leaks no token (the JWT stays in the HttpOnly cookie, not the URL); and the threat model is a trusted, offline LAN where an external `//evil.com` target may not even resolve. A real bug with a trivial fix, but low exploitability — Minor, not Major.

### Finding 15
- **Severity:** Minor
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** web/app/join/page.tsx
- **Line:** 25
- **Anchor:** `JoinClinicPage` — mount `useEffect` / `checkUserStatus` (same class of issue in `web/components/user-management.tsx:53,71`)
- **Comment:** Two recurring React issues, confirmed in both files: (1) `join/page.tsx`'s effect (lines 25-27) calls `checkUserStatus` — whose body branches on `mode` (line 31) — but omits `mode` and the callback from its deps; `user-management.tsx`'s mount effect (line 71-73) calls `loadData` with `[]` deps. (2) Both do `setState` after awaited fetches (`setIsChecking`; `setLoading/setUsers/setError`) with no unmount guard, so unmounting mid-load sets state on an unmounted component. Note `setup/page.tsx` was refactored in this same feature to add a `cancelled` flag — apply the same `useCallback`/guard pattern here.

### Finding 16
- **Severity:** Minor
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** web/components/setup-wizard.tsx
- **Line:** 191
- **Anchor:** `SetupWizard.handleComplete` (local branch)
- **Comment:** In Local mode the clinic contact email collected and required in step 1 (`email` state, enforced by `isStep1Valid` at line 160) is discarded — the `/auth/setup` payload (lines 191-198) sets `email: adminEmail.trim()` (the admin account email) and never sends the clinic `email`. The clinic record ends up storing the admin's email as its contact email, and the separately-collected clinic email goes nowhere. Either send the clinic email as a distinct field or drop it from the Local step-1 requirement.

### Finding 17
- **Severity:** Suggestion
- **Category:** Code Quality / Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Clinics/Commands/CreateClinicCommand.cs
- **Line:** 67
- **Anchor:** `CreateClinicCommandHandler.Handle`
- **Comment:** `Handle` uses the presence of `request.Password` as an implicit discriminator to branch into `CreateLocalFirstRunAsync` (an unauthenticated bootstrap that skips the JWT/clinic-context path) vs the normal authenticated Cloud create, with no handler-level `IsLocalMode` re-check — it trusts that only `AuthController.Setup` (which gates on both mode and localhost) ever sets `Password`. If any future caller sets `Password` in Cloud mode it would silently mint a password-backed admin bypassing Auth0. Fix: add a defensive `LocalAuthConfig.IsLocalMode` guard in the Local branch, or split into a dedicated `SetupClinicCommand`.

### Finding 18
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Users/Commands/ResetUserPasswordCommand.cs
- **Line:** 44
- **Anchor:** `ResetUserPasswordCommandHandler.Handle` (repeated in `SetUserActiveCommand`, `ListUsersQuery`, `RegenerateClinicCodeCommand`)
- **Comment:** The "resolve current user → fail if no id → fail if not found → fail if `!IsAdmin()`" block is repeated almost verbatim across four handlers (confirmed identical in `SetUserActiveCommand.cs:39-55` and `RegenerateClinicCodeCommand.cs:40-56`), and is redundant with the `[Authorize(Policy = AdminOnly)]` now on `UsersController` (class-level) and `ClinicsController.RegenerateCode`. Extract a shared resolve-current-admin helper returning `Result<User>`, and decide explicitly whether the in-handler re-check is intended defense-in-depth (document it) or dead duplication of the policy.

### Finding 19
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Auth/Commands/LoginCommand.cs
- **Line:** 66
- **Anchor:** `LoginCommandHandler.Handle`
- **Comment:** `PasswordVerificationOutcome.SuccessNeedsRehash` is defined in `ILocalAuthService` (enum member) and produced by `LocalAuthService.VerifyPassword` (line 47), but no caller acts on it — both `LoginCommand` (line 66) and `ChangePasswordCommand` (line 63) only check `== Failed` and treat everything else as success, never re-hashing. The rehash-on-verify capability is dead. Either handle it (on `SuccessNeedsRehash`, re-hash and persist during the existing save) or drop the enum member so it doesn't imply behavior that doesn't exist.

### Finding 20
- **Severity:** Suggestion
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/Controllers/UsersController.cs
- **Line:** 45
- **Anchor:** `UsersController.ResetPassword` / `SetStatus`
- **Comment:** All handler-level failures map to `BadRequest` (400) — lines 35, 53, 69 — including "User not found" and cross-clinic/not-found cases (`SetUserActiveCommand` returns failure for a target in another clinic, SetUserActiveCommand.cs:71) and the defensive "Only admins can…" checks. These would be more correct as 404 (not found) and 403 (forbidden). Low priority since the `AdminOnly` policy already blocks non-admins at the framework level.

### Finding 21
- **Severity:** Suggestion
- **Category:** Security
- **Verdict:** Confirmed
- **File:** web/app/api/auth/session/route.ts
- **Line:** 19
- **Anchor:** `GET` / `decodeJwtPayload`
- **Comment:** The session route base64-decodes the JWT payload and returns `email`/`role` (lines 19-24, `decodeJwtPayload` at 27) without verifying the signature; these drive UI authorization (the admin-only sidebar link + `/users` gate). A user could hand-craft a `local_session` cookie with `role: "admin"` and be shown the admin UI. Not a real privilege escalation — the .NET API validates the signed token and every admin action is `[Authorize(AdminOnly)]`, so forged tokens 401 on any real action — so this is defense-in-depth only. Treat the client-side role as cosmetic (it already is at the API layer).

### Finding 22
- **Severity:** Suggestion
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** web/lib/auth/session.tsx
- **Line:** 48
- **Anchor:** `CloudBridge`
- **Comment:** `CloudBridge` never populates `role` on the mapped `SessionUser` (lines 49-51 map only `name`/`email`/`picture`), so in Cloud mode `user.role` is always `undefined`. Currently masked because the only role-gated surfaces also require `mode === 'local'`, but the seam silently diverges from Local (where `role` is present). Either map the Auth0 role claim here or document that `role` is Local-only, so future Cloud role-based UI doesn't fail silently.

### Finding 23
- **Severity:** Suggestion
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** web/lib/auth/session.tsx
- **Line:** 91
- **Anchor:** `LocalSessionProvider` — session fetch effect
- **Comment:** `fetch("/api/auth/session")` (lines 91-101) treats any non-ok (including a 401 for an expired JWT) as "no user" but leaves the stale `SESSION_COOKIE` in place. `useAuthToken`/`client.ts` will still read that cookie via `/api/auth/token` and attach an expired Bearer token, so API calls 401 with no clear recovery. Clear the cookie (call the logout route) when `/api/auth/session` returns 401 so state and cookie stay consistent.

### Finding 24
- **Severity:** Suggestion
- **Category:** Frontend
- **Verdict:** Confirmed (adjusted — premise corrected)
- **File:** web/app/api/auth/session/route.ts
- **Line:** 24
- **Anchor:** `GET` — response payload
- **Comment:** The route returns only `{ email, role }` (line 24), but the dashboard header renders a user name/initials, so in Local mode the header falls back to a generic label. To fix, the display name must be surfaced to the Local session.
- **Challenge note:** The original finding said "the JWT is decoded here anyway — include the name claim (the backend adds fullName/name to the token)". That premise is wrong: `LocalAuthService.GenerateToken` (lines 57-68) emits only `sub`, `clinic_id`, `role`, `jti`, and `email` — no name claim. So the fix is two-part: first add a name/`fullName` claim in `GenerateToken`, then return it from this route. Severity unchanged (Suggestion).

### Finding 25
- **Severity:** Suggestion
- **Category:** Breaking Change
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/Controllers/UsersController.cs
- **Line:** 14
- **Anchor:** `UsersController` (class-level `[Authorize(Policy = AdminOnly)]`)
- **Comment:** The controller-level attribute changed from `[Authorize]` (any authenticated user) to `[Authorize(Policy = AdminOnly)]` (line 14), and `GET /users` now returns `ClinicUserDto` instead of `UserDto` (line 28). Contract change to a pre-existing endpoint. Verified low risk: no frontend consumer of `GET /users` existed before (`web/lib/api/users.ts` is new in this feature); the old `GetUsersQuery` already rejected non-admins; the DTO change is additive (adds `IsActive`/`MustChangePassword`/`LastLoginAt`, drops nothing). Flagging only to confirm no external/Cloud API client depends on the old status code or `UserDto` shape.

### Finding 26
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/Controllers/AuthController.cs
- **Line:** 82
- **Anchor:** `AuthController.Setup`
- **Comment:** Returns `StatusCode(403, ...)` with a magic literal while `System.Net` is already imported (line 1). Use `StatusCode((int)HttpStatusCode.Forbidden, ...)` (or `Forbid()`) for a self-documenting status.

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| Major | 3 |
| Minor | 13 |
| Suggestion | 10 |
| **Total** | 26 |

**Headline (post-challenge):** No Critical findings; the crypto/hashing/key-handling core and the setup localhost gate are verified sound. The three confirmed Majors are all real and cluster on two root causes: **server-side session enforcement is client-side-trusting** — the stateless 12h JWT means neither the forced password change (Finding 1) nor user deactivation/reset (Finding 2) takes effect until the token expires — and a **cookie `secure` flag that breaks login over an HTTP LAN** (Finding 3). Two findings were dismissed (a cookie-expiry false positive and a by-design pre-existing token-exposure pattern), and two Majors were down-graded to Minor after verifying low real impact in the offline/trusted-LAN threat model.
