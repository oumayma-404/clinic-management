# Story 1 (BE): Local auth mode + login API

**Status:** implemented
**Layer:** BE
**Depends On:** —

## Objective
Introduce the `Auth:Mode = Cloud | Local` switch and make **local email+password login** work at the API level: a local user can authenticate and receive a signed JWT that authorizes existing clinic-scoped endpoints — with Cloud/Auth0 behavior unchanged.

_From spec:_ AC-3.1, AC-3.2, AC-3.3, AC-7.1, AC-7.2, AC-7.3; FR-A1–A5, FR-B1, FR-B2.

## Entry criteria
- Plan APPROVED (Phase 1).
- Local database reachable (fresh or existing).

## Steps
1. Add `Auth:Mode` config (default `Cloud`). In `Program.cs` (~71–103), branch JWT setup: Cloud = Auth0 authority (unchanged); Local = validate the app-issued JWT with the per-install signing key.
2. Extend `User` (Domain) with `PasswordHash`, `IsActive` (default true), `MustChangePassword` (default false), optional `LastLoginAt` + lockout fields; add a factory for local users with a stable `local|{guid}` id.
3. EF config + migration: map new columns; partial unique index on `Email` `HasFilter("\"PasswordHash\" IS NOT NULL")`.
4. Create `ILocalAuthService`/`LocalAuthService`: password hashing via `PasswordHasher<T>` (add `Microsoft.Extensions.Identity.Core`) + JWT issuance (`JwtSecurityTokenHandler`) emitting `sub`, `email`, `role`, `clinic_id` claims. Generate/read the per-install signing key (never committed).
5. Add `LoginCommand` + `AuthController`: `POST /api/auth/login` (email+password → JWT; reject inactive/locked), `GET /api/auth/mode` (public).
6. Register a **no-op `IAuth0ManagementService`** when `Auth:Mode = Local`; add `UserRepository.GetByEmailAsync`.
7. Verify `ClinicContext` resolves the local user from the JWT `sub`/claims unchanged.

## Files to create/modify
- `api/.../API/Program.cs` — mode-branched JWT setup.
- `api/.../Domain/Entities/User.cs` — credential fields + local factory.
- `api/.../Infrastructure/Persistence/Configurations/UserConfiguration.cs` + new migration.
- `api/.../Infrastructure/Repositories/UserRepository.cs` — `GetByEmailAsync`.
- `api/.../Infrastructure/Extensions.cs` — register `LocalAuthService`, no-op Auth0 mgmt (Local).
- New: `ILocalAuthService` (Application), `LocalAuthService` (Infrastructure), `LoginCommand`(+handler), `AuthController`.
- `ClinicManagement.Infrastructure.csproj` — add `Microsoft.Extensions.Identity.Core`.

## Verification steps
- Seed one local user; `POST /api/auth/login` with correct creds → 200 + JWT; wrong creds → 401; inactive user → rejected.
- Call a clinic-scoped endpoint with the JWT → authorized and correctly clinic-scoped.
- Set `Auth:Mode=Cloud` → Auth0 JWT-bearer config path unchanged (regression test).
- Migration applies cleanly to a fresh DB and to an existing (Cloud) DB.

## Exit criteria
- With `Auth:Mode=Local`, a seeded local user logs in and reaches clinic-scoped data via the issued JWT.
- Cloud mode auth is provably unchanged.
