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
| 5 | FE | Staff registration UI | implemented |
| 6 | BE | Admin user-management API | not-started |
| 7 | FE | Admin user-management UI | not-started |
| 8 | BE | Admin lockout-recovery utility | not-started |

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

## Significant Deviations
(none)

## Learnings
- **.NET 8 default token handler:** ASP.NET Core 8 `JwtBearer` validates with `JsonWebTokenHandler` by default (`UseSecurityTokenValidators = false`). The legacy `JwtSecurityTokenHandler` in Microsoft.IdentityModel 7.1.2 fails to read its *own* `iss` claim on re-parse (returns empty), which surfaced during offline verification. Production is unaffected because it uses the modern handler. **Constraint for Phase 4 security hardening:** do NOT set `JwtBearerOptions.UseSecurityTokenValidators = true`, or local-token issuer validation would break. Verified the issue→validate contract works with `JsonWebTokenHandler`.
- **Per-install signing key** resolves via `LocalAuthConfig` (shared by issuer + validator so they can never drift): explicit `Auth:Local:SigningKey`, else a generated key file at `.local/signing-key` (gitignored). Never committed / never in appsettings.
