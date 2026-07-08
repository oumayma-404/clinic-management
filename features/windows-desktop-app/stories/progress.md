# Progress — Windows Desktop / Offline-LAN, Phase 1

**Feature:** windows-desktop-app (Phase 1 — Pluggable Auth + Local Accounts)
**Branch:** `feature/windows-desktop-app`
**Plan:** [../plan.md](../plan.md) (APPROVED · Challenged)

## Story Status

| Story | Layer | Name | Status |
|-------|-------|------|--------|
| 1 | BE | Local auth mode + login API | reviewed |
| 2 | BE | First-run clinic + admin creation | not-started |
| 3 | FE | Local login + first-run setup UI | not-started |
| 4 | BE | Staff self-registration API | not-started |
| 5 | FE | Staff registration UI | not-started |
| 6 | BE | Admin user-management API | not-started |
| 7 | FE | Admin user-management UI | not-started |
| 8 | BE | Admin lockout-recovery utility | not-started |

## Working tree note (start of session)
- `web/components/document-editor-content.tsx` — pre-existing modified file, **unrelated** to this backend story. Excluded from this story's commits (staged by explicit path only).

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

## Significant Deviations
(none)

## Learnings
- **.NET 8 default token handler:** ASP.NET Core 8 `JwtBearer` validates with `JsonWebTokenHandler` by default (`UseSecurityTokenValidators = false`). The legacy `JwtSecurityTokenHandler` in Microsoft.IdentityModel 7.1.2 fails to read its *own* `iss` claim on re-parse (returns empty), which surfaced during offline verification. Production is unaffected because it uses the modern handler. **Constraint for Phase 4 security hardening:** do NOT set `JwtBearerOptions.UseSecurityTokenValidators = true`, or local-token issuer validation would break. Verified the issue→validate contract works with `JsonWebTokenHandler`.
- **Per-install signing key** resolves via `LocalAuthConfig` (shared by issuer + validator so they can never drift): explicit `Auth:Local:SigningKey`, else a generated key file at `.local/signing-key` (gitignored). Never committed / never in appsettings.
