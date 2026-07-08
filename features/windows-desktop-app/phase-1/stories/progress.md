# Progress — Windows Desktop / Offline-LAN, Phase 1

**Feature:** windows-desktop-app (Phase 1 — Pluggable Auth + Local Accounts)
**Branch:** `feature/windows-desktop-app`
**Plan:** [../plan.md](../plan.md) (APPROVED · Challenged)

## Story Status

| Story | Layer | Name | Status |
|-------|-------|------|--------|
| 1 | BE | Local auth mode + login API | done |
| 2 | BE | First-run clinic + admin creation | done (review skipped by user) |
| 3 | FE | Local login + first-run setup UI | done (review skipped by user) |
| 4 | BE | Staff self-registration API | done (review skipped by user) |
| 5 | FE | Staff registration UI | done (review skipped by user) |
| 6 | BE | Admin user-management API | done (review skipped by user) |
| 7 | FE | Admin user-management UI | reviewed |
| 8 | BE | Admin lockout-recovery utility | done (review skipped by user) |

## Working tree note (start of session)
- `web/components/document-editor-content.tsx` — pre-existing modified file, **unrelated** to this backend story. Excluded from this story's commits (staged by explicit path only).

## Story 1 — Per-story test execution (all auto-skipped → done)
- `/story-e2e` — ⊘ skipped (Layer: BE, no user-facing flow)
- `/story-api-tests` — ⊘ skipped (Postman/Newman never run per user preference)
- `/story-integration-tests` — ⊘ skipped (no `test-plan-integration.md` APPROVED in this phase)

## Story 1 — Steps

- [x] 1. `Auth:Mode` config + mode-branched JWT setup in `Program.cs`
- [x] 2. Extend `User` (Domain) with credential fields + local-user factory
- [x] 3. EF config + migration (columns + partial unique email index) — `20260708094305_AddLocalAuthUserFields`
- [x] 4. `ILocalAuthService` / `LocalAuthService` (hashing + JWT issuance + per-install signing key)
- [x] 5. `LoginCommand` + `AuthController` (`POST /api/auth/login`, `GET /api/auth/mode`)
- [x] 6. No-op `IAuth0ManagementService` (Local) + `UserRepository.GetByEmailAsync`
- [x] 7. Verify `ClinicContext` resolves local user from JWT claims (reads `sub`/`clinic_id`/`role`/`email`; clinic scoping resolves via `GetByAuth0SubAsync(user.Id)` where `Id = local|{guid}`) — no change needed

## Story 1 — Verification
- **Build:** `dotnet build ClinicManagement.sln` → 0 errors; 58 warnings, all pre-existing (none in new files).
- **Unit tests:** 32/32 pass (14 new: `LoginCommandHandlerTests` ×6, `UserLocalAuthTests` ×8; 18 pre-existing).
- **JWT issue→validate round-trip** (offline scratchpad, real `LocalAuthService` + the `JsonWebTokenHandler` ASP.NET Core 8 uses): hash/verify OK; token validated; claim contract (`sub`,`role`,`clinic_id`,`email`) resolves correctly; forged-key token rejected.
- **Deferred to manual (no Docker/Postgres this session, per plan's manual testing strategy):** fresh Local DB → first-run → login from a second machine; migration applied to a live DB. Migration inspected: all additive + defaulted + filtered index → safe for existing Cloud DBs.

## Story 2 — Steps
- [x] 1. `CreateClinicCommand` Local first-run branch: accept `Password`+`FullName`, hash via `LocalAuthService`, role=admin, `local|{guid}` id. Discriminated by `Password` present; Cloud path untouched.
- [x] 2. First-run gate: `POST /api/auth/setup` (anonymous) — Local-mode-only (404 in Cloud) + localhost-only (`IsLocalRequest`, 403 otherwise); "no admin exists yet" gate in the handler (AC-1.2a).
- [x] 3. Clinic `Code` generated + persisted for later staff self-registration.
- [x] 4. Cloud-mode `CreateClinicCommand` unchanged (Password null → existing Auth0 flow).

## Story 2 — Verification
- **Build:** 0 errors (58 pre-existing warnings, 0 in changed files).
- **Unit tests:** 37/37 (5 new in `CreateClinicLocalSetupTests`: create admin, setup-closed gate, short-password (FR-B2), missing email/full-name).
- **FR-B2** (password ≥8) now enforced at the API in the first-run path — the deferral noted in Story 1 is resolved here.
- **No migration** — reuses the User/Clinic schema + Story 1 columns.
- **Deferred to manual (no Docker this session):** fresh DB → setup from localhost → login from a LAN client; non-localhost → 403.

## Story 2 — Review / test execution
- `/review-story` — **skipped by user request** (`/next ... skip review move to next step`, 2026-07-08).
- `/story-e2e` — ⊘ auto-skipped (Layer: BE).
- `/story-api-tests` — ⊘ auto-skipped (Postman never run).
- `/story-integration-tests` — ⊘ auto-skipped (no `test-plan-integration.md` APPROVED).

## Story 3 — Steps (FE: Local login + first-run setup UI)
- [x] 1. Mode plumbing: server reads `AUTH_MODE` (`lib/auth/local-auth.ts`); `layout.tsx` mounts `CloudSessionProvider` (Auth0) or `LocalSessionProvider` (cookie) accordingly. Mode reaches the browser via `useSession().mode` (SSR-provided) — no bootstrap fetch needed.
- [x] 2. Local-login route handler `POST /api/auth/local-login` → calls .NET `/auth/login` → sets HttpOnly `local_session` cookie. `POST /api/auth/local-logout` clears it.
- [x] 3. `app/api/auth/token/route.ts` (Local): returns the JWT from the cookie; Cloud path unchanged.
- [x] 4. `middleware.ts` (Local): gates protected routes on the cookie, redirects to `/login`; skips Auth0. Cloud path unchanged.
- [x] 5. `layout.tsx`: `Auth0Provider` only in Cloud (inside `CloudSessionProvider`); `LocalSessionProvider` in Local. Unified `useSession()` seam replaces all 5 direct `useUser` consumers.
- [x] 6. Local login screen (`/login` email+password form); setup wizard admin-account fields (Local) → `POST /auth/setup`; inactivity auto-logout (30 min) + logout (preserves server address) in `LocalSessionProvider`.

## Story 3 — Verification
- **Typecheck:** `npx tsc --noEmit` clean.
- **Build:** `npm run build` succeeds; all 16 routes + 4 new `/api/auth/*` handlers compile. Only warning is the pre-existing Auth0 edge-runtime `crypto` notice (unchanged from before).
- **No frontend unit-test runner** in the repo (no vitest); E2E deferred (`/story-e2e` auto-skips — no `test-plan-e2e.md`).
- **Cloud parity:** every Cloud path (middleware, token route, layout provider, header, login, setup, join, setup-wizard) preserved behaviorally; Local behavior is additive and gated on `AUTH_MODE`.
- **Deferred to manual (no dev server/API this session):** `AUTH_MODE=Local` end-to-end — setup (localhost) → login from a client → dashboard with Bearer token; inactivity logout; Cloud `AUTH_MODE` unchanged.

## Story 3 — Review / test execution
- `/review-story` — **skipped by user request** (`/next ... move to story 4`, 2026-07-08).
- `/story-e2e` — ⊘ auto-skipped (no `test-plan-e2e.md` APPROVED).
- `/story-api-tests` / `/story-integration-tests` — ⊘ auto-skipped (Layer: FE).

## Story 4 — Steps (BE: Staff self-registration API)
- [x] 1. `JoinClinicCommand` Local branch (discriminated by `Password`): email + password + full name + role + optional doctor info + clinic code. Cloud path unchanged.
- [x] 2. Validations: valid clinic code (else reject), email unique per install (`GetByEmailAsync`), role ∈ {doctor, secretary} — **admin rejected**; doctor requires doctor info.
- [x] 3. Creates local `User` (hashed pw, `local|{guid}`, active) + linked `Doctor` when role=doctor.
- [x] 4. `POST /api/auth/register` (anonymous, Local-mode-only → 404 in Cloud). **Not** localhost-gated — the clinic code is the gate (any LAN client can self-register, AC-4).

## Story 4 — Verification
- **Build:** 0 errors (58 pre-existing warnings, 0 in changed files).
- **Unit tests:** 44/44 (7 new in `JoinClinicLocalRegisterTests`: create account, reject admin role, invalid code, duplicate email, short password, doctor→linked Doctor, doctor requires info).
- **No migration** — reuses the User/Doctor/Clinic schema.
- **Deferred to manual (no Docker this session):** valid code + new email → account created → login; Cloud-mode join unchanged.

## Story 4 — Review / test execution
- `/review-story` — **skipped by user request** (`/next skip review`, 2026-07-08).
- `/story-e2e` — ⊘ auto-skipped (Layer: BE).
- `/story-api-tests` / `/story-integration-tests` — ⊘ auto-skipped (Postman never run / no integration test plan).

## Story 5 — Steps (FE: Staff registration UI)
- [x] 1. `join-wizard.tsx`: Local-mode account fields (full name, email, password + confirm) in step 1 + role; validation gates on them; calls `clinicsApi.register` → `/auth/register`. Cloud join path unchanged.
- [x] 2. Registration reachable from the local login screen ("Have a clinic code? Create an account" → `/join`).
- [x] 3. `app/join/page.tsx`: Local mode skips the session gate (self-registration is open; clinic code is the gate) and shows the code form directly. On success → `/login`.
- [x] 4. API errors (invalid code / duplicate email) surface via the wizard's existing inline error banner.

## Story 5 — Verification
- **Typecheck:** `npx tsc --noEmit` clean (caught + fixed a now-dead `mode === "local"` ternary in the join page narrowed to `"cloud"` by the new Local guard).
- **Build:** `npm run build` succeeds (all routes compile).
- **No FE unit-test runner**; E2E deferred.
- **Cloud parity:** Cloud join flow (auth-gated, `clinicsApi.join`) unchanged.
- **Deferred to manual:** Local register with valid code → account → login; invalid code / duplicate email errors.

## Story 5 — Review / test execution
- `/review-story` — **skipped by user request** (`/next skip review`, 2026-07-08).
- `/story-e2e` — ⊘ auto-skipped (Layer: FE, no `test-plan-e2e.md` APPROVED).
- `/story-api-tests` — ⊘ auto-skipped (Postman never run per user preference).
- `/story-integration-tests` — ⊘ auto-skipped (Layer: FE).

## Story 6 — Steps (BE: Admin user-management API)
- [x] 1. `ListUsersQuery` (admin-only) → clinic users with status (name, email, role, `IsActive`, `MustChangePassword`, `LastLoginAt`). **Replaced** the near-identical `GetUsersQuery` (only referenced by `UsersController`) to avoid duplicate list queries.
- [x] 2. `ResetUserPasswordCommand` (admin-only) → `ILocalAuthService.GenerateTemporaryPassword()` (crypto-random, 12 chars, unambiguous alphabet), hashes it, `SetPassword(hash, mustChangePassword: true)`; returns the temp password once (`ResetPasswordResultDto`).
- [x] 3. `SetUserActiveCommand` (admin-only) → `Activate()`/`Deactivate()`; `LoginCommand` already rejects inactive users (Story 1). Records retained (no delete).
- [x] 4. `ChangePasswordCommand` (any authenticated user) → verifies current password, enforces ≥8 (FR-B2), `SetPassword(hash, mustChangePassword: false)` clears the forced-change flag. `RegenerateClinicCodeCommand` (admin-only) added for AC-4.5.
- [x] 5. Admin-only enforced two ways: `[Authorize(Policy = AdminOnly)]` on the endpoints (→ 403 for non-admin, AC-5.4) **and** an inline `currentUser.IsAdmin()` check in each handler (DB-sourced role; testable without HTTP).

## Story 6 — Endpoints
- `GET /api/users` (AdminOnly) — list users + status.
- `POST /api/users/{id}/reset-password` (AdminOnly) — temp password returned once.
- `PUT /api/users/{id}/status` (AdminOnly) — `{ isActive }` deactivate/reactivate.
- `POST /api/auth/change-password` (`[Authorize]`) — current + new password.
- `POST /api/clinics/regenerate-code` (AdminOnly) — new clinic code (AC-4.5).

## Story 6 — Verification
- **Build:** `dotnet build ClinicManagement.sln` → 0 errors, 58 warnings (all pre-existing; 0 in changed files).
- **Unused usings:** IDE0005 analyzer (temp `Directory.Build.props` with `GenerateDocumentationFile`) → clean across the solution.
- **Unit tests:** 60/60 (16 new: `ListUsersQueryHandlerTests` ×2, `ResetUserPasswordCommandHandlerTests` ×4, `SetUserActiveCommandHandlerTests` ×5, `ChangePasswordCommandHandlerTests` ×3, `RegenerateClinicCodeCommandHandlerTests` ×2; 44 pre-existing). Cover: admin happy paths, non-admin rejection (AC-5.4), cross-clinic isolation, non-local-account rejection, self-deactivation guard, wrong/short password.
- **No migration** — reuses Story 1's User credential columns.
- **Deferred to manual (no Docker this session):** admin lists → reset → target forced to change at next login; deactivate → login rejected → reactivate → login OK; non-admin → 403; regenerate code invalidates the old one.

## Story 6 — Review / test execution
- `/review-story` — **skipped by user request** (`/next skip review`, 2026-07-08). Story 6 → **done**.
- `/story-e2e` — ⊘ auto-skipped (Layer: BE).
- `/story-api-tests` / `/story-integration-tests` — ⊘ auto-skipped (Postman never run / no integration test plan APPROVED).

## Story 7 — Steps (FE: Admin user-management UI)
- [x] 1. Admin user-management page (`/users` + `user-management.tsx`): users table (name, email, role, status + "must change password" badge, last login) with **reset-password** and **deactivate/reactivate** actions, each behind a confirm `AlertDialog` (AC-5.1/5.2/5.3).
- [x] 2. Clinic code shown on the page with a **Regenerate** action (confirm dialog) → `clinicsApi.regenerateCode()` → `POST /clinics/regenerate-code` (AC-4.5).
- [x] 3. Reset-password shows the returned temporary password once in a `Dialog` (copy-to-clipboard) for the admin to relay (AC-5.2).
- [x] 4. **Force-password-change screen** (`/change-password` + `change-password-form.tsx`): current/temp + new + confirm; posts to a new `/api/auth/change-password` route that proxies the .NET `/auth/change-password` with the cookie JWT and clears the forced-change flag on success. Middleware forces the user onto this screen while the flag is set (AC-5.2). Also reachable voluntarily from the header menu (Local mode).
- [x] 5. Management page hidden for non-admins: admin-only sidebar entry (`mode==='local' && role==='admin'`) + an in-page "Admins only" gate (AC-5.4); the API is `AdminOnly` (403) as the server-side backstop.

## Story 7 — Endpoints / client
- `web/lib/api/users.ts` (new) — `usersApi.list` / `resetPassword` / `setStatus` over `GET /users`, `POST /users/{id}/reset-password`, `PUT /users/{id}/status` (all return the unwrapped value, matching `UsersController`).
- `web/lib/api/clinics.ts` — added `regenerateCode()` (`POST /clinics/regenerate-code`, `Result<ClinicDto>` unwrapped).
- New routes/pages: `app/users/page.tsx`, `app/change-password/page.tsx`, `components/user-management.tsx`, `components/change-password-form.tsx`, `app/api/auth/change-password/route.ts`.
- Modified: `middleware.ts` (Local force-change gate), `app/api/auth/local-login/route.ts` + `local-logout/route.ts` (set/clear the flag cookie), `lib/auth/local-auth.ts` (`MUST_CHANGE_COOKIE`), `dashboard-sidebar.tsx` (admin nav), `dashboard-header.tsx` (change-password menu item).

## Story 7 — Verification
- **Typecheck:** `npx tsc --noEmit` clean.
- **Build:** `npm run build` succeeds; all 17 routes compile (new `/users`, `/change-password`, `/api/auth/change-password`). No new warnings.
- **No FE unit-test runner** in the repo; E2E deferred (`/story-e2e` auto-skips — no `test-plan-e2e.md`).
- **Cloud parity:** every new behavior is mode-gated on Local (`AUTH_MODE`): the force-change middleware block is inside the existing `resolveAuthMode() === 'local'` branch; the admin sidebar entry and header change-password item require `mode==='local'`; the new `/api/auth/*` routes are Local-only. Cloud path unchanged.
- **Deferred to manual (no dev server/API this session):** admin lists → reset → temp shown → target forced to change at next login; deactivate → login rejected → reactivate → login OK; non-admin cannot see/reach `/users`; regenerate invalidates the old code.

## Story 7 — Review / test execution
- `/review-story` — **done** (2026-07-08). Score **100/100**. Report: [../reviews/story-7.md](../reviews/story-7.md). 1 Minor finding fixed: self-deactivation dead-end (own-row Deactivate now disabled, mirroring the backend guard). Typecheck + build re-verified clean.
- `/story-e2e` — ⊘ auto-skipped (no `test-plan-e2e.md` APPROVED).
- `/story-api-tests` / `/story-integration-tests` — ⊘ auto-skipped (Layer: FE).

## Story 8 — Steps (BE: Admin lockout-recovery utility)
- [x] 1. `reset-admin-password` console command in the API project, intercepted at the top of `Program.cs` **before** the web host starts → runs one-shot and returns an exit code (0 success / 1 failure). Usage: `dotnet run --project ClinicManagement.API -- reset-admin-password [admin-email]`.
- [x] 2. Reuses `ILocalAuthService.GenerateTemporaryPassword()` + `HashPassword()` and `User.SetPassword(hash, mustChangePassword: true)` (which also zeroes failed-attempt count + clears lockout). Target = admin by email, or the **sole** local admin if unambiguous (0 admins → error; >1 → asks for the email).
- [x] 3. Local-mode-only (refuses to run in Cloud mode); runs on the server PC against the local DB (no web endpoint — direct DB access is the "runs locally" gate). Prints clear success (account + one-time temp password) / failure.
- [x] 4. Recovery procedure documented: [../ADMIN_RECOVERY.md](../ADMIN_RECOVERY.md).

## Story 8 — Files
- New: `api/ClinicManagement.Application/Common/Maintenance/AdminPasswordRecoveryService.cs` — testable core (find admin → temp password → `SetPassword` → persist). **Not** registered in DI (can't be injected into an HTTP handler → no unauthenticated reset path).
- New: `api/ClinicManagement.API/Maintenance/AdminPasswordResetCommand.cs` — CLI wrapper (builds config + `AddInfrastructure` DI, Local-mode guard, resolves the core service, prints result).
- Modified: `api/ClinicManagement.API/Program.cs` — early CLI-arg branch + explicit `return 0;` (top-level program now returns `int`).
- New: `api/ClinicManagement.UnitTests/Common/Maintenance/AdminPasswordRecoveryServiceTests.cs` (8 tests).
- New doc: `features/windows-desktop-app/ADMIN_RECOVERY.md`.

## Story 8 — Verification
- **Build:** `dotnet build ClinicManagement.sln` → 0 errors, 0 new warnings (58 pre-existing, none in changed files).
- **Unit tests:** 68/68 (8 new in `AdminPasswordRecoveryServiceTests`: reset-by-email happy path, email trimming, unknown email, non-admin refused, sole-admin (no email), no-admin, multiple-admins ambiguity, lockout cleared; 60 pre-existing).
- **Unused usings:** IDE0005 (temp `Directory.Build.props`) on the two new production files + `Program.cs` → clean; temp props deleted.
- **No migration** — reuses Story 1's `User` credential columns; no schema change.
- **Deferred to manual (no Docker this session):** run `reset-admin-password` on a live Local DB → admin logs in with the temp password → forced-change screen; Cloud mode → utility refuses.

## Story 8 — Review / test execution
- `/review-story` — **skipped by user request** (`/next skip review`, 2026-07-08).
- `/story-e2e` — ⊘ auto-skipped (Layer: BE, no user-facing flow).
- `/story-api-tests` / `/story-integration-tests` — ⊘ auto-skipped (Postman never run / no `test-plan-integration.md` APPROVED).

## Structural notes / decisions
- **JWT package location:** the plan assumed `JwtSecurityTokenHandler` was transitively available in Infrastructure via JwtBearer, but JwtBearer is only referenced by the API project. Since the plan places `LocalAuthService` in Infrastructure, adding `System.IdentityModel.Tokens.Jwt` to the Infrastructure project (alongside the planned `Microsoft.Extensions.Identity.Core`). Consistent with the plan's intent.
- **Per-install signing key:** read from `Auth:Local:SigningKey` (config); if absent, generate a 512-bit key and persist to a gitignored file under the content root, then reuse. Never committed / never in `appsettings.json`.

## Auto-Approved Deviations
| Deviation | Classification | Reason |
|-----------|----------------|--------|
| Added `System.IdentityModel.Tokens.Jwt` 7.1.2 to Infrastructure | Trivial (planned intent) | Plan assumed `JwtSecurityTokenHandler` was transitively available via JwtBearer, but JwtBearer is only in the API project. Adding the explicit package to Infrastructure (where the plan places `LocalAuthService`) realizes the plan's intent. User-approved as a dependency. |
| Login failure → controller returns `401 Unauthorized` (not `400`) | Trivial | Internal to `AuthController`; correct HTTP semantics for auth. No contract elsewhere depends on it (new endpoint). |
| Local emails normalized to lowercase in `User.CreateLocalUser` | Trivial | Makes the filtered unique email index + login lookup case-insensitive per install. Internal; Cloud rows untouched. |
| Included lockout fields + basic lockout in this story | Trivial | Story step 5 says "reject inactive/locked"; plan lists lockout fields as part of this migration. Fields additive/defaulted; login rejects locked accounts (AC-3.4 groundwork). |
| (Story 2) First-run via a dedicated anonymous `POST /api/auth/setup` | Trivial | Plan explicitly allowed "a dedicated setup endpoint". First-run has no authenticated user, so an anonymous, localhost+no-admin-gated endpoint is required (not the `[Authorize]` `POST /api/clinics`). |
| (Story 2) Local first-run discriminated by `Password` present on `CreateClinicCommand` | Trivial | Internal handler branch; the setup endpoint is the only caller that sets `Password`, only in Local mode. Cloud path (Password null) unchanged. |
| (Story 2) `IsLocalRequest` as a private controller helper | Trivial | Plan said "small helper to detect localhost"; loopback check via `IPAddress.IsLoopback` + local==remote. |
| (Story 3) Unified `useSession()` context replacing 5 direct `useUser` consumers | Plan-directed | Plan step 5 mandates "Auth0Provider only in Cloud; local auth context in Local." Consumers can't call Auth0 `useUser` when the provider is absent (Local), so they read the unified context (Cloud bridges `useUser`; Local reads the cookie). Cloud behavior preserved. |
| (Story 3) Auth mode delivered to the browser via SSR (`useSession().mode`) instead of a bootstrap `GET /api/auth/mode` fetch | Trivial | Layout is a server component and reads `AUTH_MODE`; passing the mode down avoids a round-trip and matches the existing `/api/auth/token` same-origin pattern. The .NET `/api/auth/mode` endpoint remains for other consumers. |
| (Story 3) `useSession()` returns a loading default instead of throwing when no provider is in scope | Trivial | Mirrors Auth0 `useUser`'s SSR-tolerant behavior; prevents static-prerender crashes for globally-mounted components. Provider is always mounted at runtime. |
| (Story 3) Logout is a button calling `session.logout()` (not a hardcoded `<a href="/auth/logout">`) | Trivial | Needed for mode-aware logout; Cloud still navigates to `/auth/logout`, Local clears the cookie. |
| (Story 4) Self-registration via a dedicated anonymous `POST /api/auth/register` | Trivial | Plan step: "expose join for Local mode (unauthenticated pre-account)". Self-registration has no session yet, so an anonymous endpoint is required (not the `[Authorize]` `POST /api/clinics/join`). Gated by clinic code, not localhost. |
| (Story 4) Local self-registration discriminated by `Password` present on `JoinClinicCommand` | Trivial | Mirrors Story 2's `CreateClinicCommand` pattern; the register endpoint is the only caller that sets `Password`. Cloud join path (Password null) unchanged. |
| (Story 6) `ListUsersQuery` **replaces** `GetUsersQuery` (deleted the latter) | Trivial (planned intent + Scout rule) | Plan/story explicitly name `ListUsersQuery`. `GetUsersQuery` returned `UserDto` (no status) and was referenced only by `UsersController` (no frontend consumer — `web/lib` has no users API client yet; Story 7 builds it). Replacing it avoids two near-identical list queries. `GET /api/users` now returns `ClinicUserDto` (adds `IsActive`/`MustChangePassword`/`LastLoginAt`); still admin-only (was enforced inline before, now also by policy). |
| (Story 6) `ILocalAuthService.GenerateTemporaryPassword()` new interface method | Trivial | Reset needs a secure temp password; the auth service is its natural home (alongside hashing/JWT). Crypto-random (`RandomNumberGenerator`), 12 chars, unambiguous alphabet. Both impls are ours; additive. |
| (Story 6) `SetUserActiveCommand` rejects an admin deactivating **their own** account | Defensive (uncertain→documented) | Not in spec, but a self-deactivation would be an unrecoverable lockout in Phase 1 (the recovery utility, Story 8, resets a password, not the active flag). A one-line guard prevents a footgun; all other (de)activations behave exactly as AC-5.3 specifies. |
| (Story 6) Admin-only enforced by BOTH `[Authorize(Policy = AdminOnly)]` (endpoints) and inline `IsAdmin()` (handlers) | Trivial (plan-directed) | Plan step 5: "enforce admin-only via role check on these endpoints." Policy gives the correct 403 (AC-5.4); the inline DB-sourced check is defense-in-depth and unit-testable without HTTP. |
| (Story 7) Force-change gating via a `local_must_change_password` cookie + a Local-mode middleware redirect | Trivial (stated requirement, no specified mechanism) | The app JWT carries no `mustChangePassword` claim (Story 1), so the flag can't be read from the session. Login sets an HttpOnly flag cookie when `mustChangePassword`; the middleware forces `/change-password` while set; the change-password proxy route clears it on success. All within the `web` project, mode-gated to Local — no API contract change, Cloud path untouched. Realizes Story 7 step 4 / AC-5.2. |
| (Story 7) User management as a dedicated `/users` page + admin-only sidebar entry (not nested "under settings") | Trivial | Step 1 says "under settings"; the story's own Files list says "dashboard-sidebar.tsx / settings — nav entry (admin-only)". A discoverable top-level page reachable via an admin-only sidebar entry satisfies the intent and AC-5.4. |
| (Story 7) Clinic code + Regenerate placed on the `/users` (admin) page rather than in `ClinicSettings` | Trivial | Regenerate is admin-only (AC-4.5); the read-only code already shows in `ClinicSettings` for everyone. Co-locating the admin action with the admin screen keeps the admin-only mutation in one place. |
| (Story 7) Change-password submits via a new `/api/auth/change-password` Next route (proxy) instead of calling the .NET endpoint directly | Trivial | The route both attaches the cookie JWT as Bearer (same pattern as `local-login`) and clears the forced-change flag cookie server-side on success. Cookie clearing must happen server-side. |
| (Story 7) Removed the dead "Profile" header menu item and wired the "Settings" item to navigate | Trivial (Scout rule) | `Profile` had no handler (dead UI); `Settings` was inert. Now `Settings` routes to `/settings` and (Local) a `Change password` item is added. |
| (Story 7) `/users` non-admin gate renders an inline "Admins only" card instead of reusing `unauthorized-page` | Trivial | `unauthorized-page` is clinic-membership specific (Create/Join clinic CTAs), wrong copy for a role gate. A focused card with a back-to-dashboard action fits AC-5.4. |
| (Story 8) Recovery logic split into an Application-layer `AdminPasswordRecoveryService` (testable core) + a thin API `AdminPasswordResetCommand` CLI wrapper | Trivial (planned intent + testability constraint) | Plan says "CLI/console entry in `ClinicManagement.API`". The `UnitTests` project references **only `Application`** (not API/Infrastructure), so the orchestration logic must live in Application to satisfy the plan's own "add unit tests" requirement. Keeps Clean Architecture (use-case orchestration in Application, host wiring in API). No new package, no API contract, no behavior change. The service is deliberately **not** DI-registered so it can't be injected into a controller (no unauthenticated reset path). |
| (Story 8) Local-mode-only guard + no web endpoint as the "runs locally on the server PC" mechanism | Trivial (stated requirement, no specified mechanism) | Spec FR-B6 says "runnable on the server PC by someone with Windows access". A console command with direct DB access inherently runs on the server; adding a Cloud-mode refusal keeps it offline-only. No HTTP surface = nothing to reach over the LAN. |
| (Story 8) `Program.cs` top-level program now returns `int` (early CLI branch + trailing `return 0;`) | Trivial | Required so the one-shot console command can set a process exit code without booting the web host. Web-server path unchanged (falls through to `return 0;` after `app.Run()`). |

## Significant Deviations
(none)

## Learnings
- **.NET 8 default token handler:** ASP.NET Core 8 `JwtBearer` validates with `JsonWebTokenHandler` by default (`UseSecurityTokenValidators = false`). The legacy `JwtSecurityTokenHandler` in Microsoft.IdentityModel 7.1.2 fails to read its *own* `iss` claim on re-parse (returns empty), which surfaced during offline verification. Production is unaffected because it uses the modern handler. **Constraint for Phase 4 security hardening:** do NOT set `JwtBearerOptions.UseSecurityTokenValidators = true`, or local-token issuer validation would break. Verified the issue→validate contract works with `JsonWebTokenHandler`.
- **Per-install signing key** resolves via `LocalAuthConfig` (shared by issuer + validator so they can never drift): explicit `Auth:Local:SigningKey`, else a generated key file at `.local/signing-key` (gitignored). Never committed / never in appsettings.

## Memory Updated

**Status:** done
**Date:** 2026-07-08
**Files:** root `CLAUDE.md` + `api/{Domain,Application,Infrastructure,API}/CLAUDE.md` + `web/{,lib,components}/CLAUDE.md` (8 files) updated with the pluggable-auth / local-accounts architecture. Learnings captured in `features/LEARNINGS.md`; retrospective in `../retrospective.md`.

## Pull Request

**Status:** ready — push done; PR creation blocked on `gh` account (logged in as `o-benkhalifa`, cannot access `oumayma-404/clinic-management`)
**Date:** 2026-07-08
**Branch:** `feature/windows-desktop-app` -> `main` (repo has no `develop` branch; `main` is the PR base) — **pushed to origin**
**Pre-PR checks:** frontend `tsc --noEmit` clean; `dotnet build ClinicManagement.sln` 0 errors (58 pre-existing warnings); unit tests 69/69 pass.
**Open PR:** https://github.com/oumayma-404/clinic-management/compare/main...feature/windows-desktop-app
**Created by:** Claude
