# Progress: Adoption QA — G (Cloud admin retrofit)

**Started:** 2026-07-24
**Type:** Small (forced follow-up)
**Branch:** feature/windows-desktop-app

## Status
- [x] Implementation
- [x] Quality checks (dotnet build 0/0)
- [ ] Tests (handled by /test-small-feature)

## Files Changed
- `api/.../Domain/Entities/User.cs` — new `PromoteToAdmin()` (role-only mutator, preserves email/full name).
- `api/.../Application/Common/Interfaces/IClinicAdminBackfill.cs` (new).
- `api/.../Infrastructure/Services/ClinicAdminBackfill.cs` (new) — idempotent: for each clinic with no active admin, promote the earliest active user (creator) + push role to Auth0 (best-effort).
- `api/.../Infrastructure/Extensions.cs` — register `IClinicAdminBackfill`.
- `api/.../API/Program.cs` — invoke `BackfillAsync()` in the Cloud startup path, right after `SeedAllClinicsAsync()`.

## Notes
- Cloud-only (runs in the `!isLocalAuthMode` startup block). Reads across clinics with no clinic in scope (User/Clinic are unfiltered; the query filter is inactive at startup).
- Idempotent: a clinic that already has an active admin is skipped; orphan clinics (no users) are skipped.

## Deferred to /test-small-feature
Backfill scenarios: promotes only when no active admin; idempotent on second run; orphan-clinic skip; Auth0 failure swallowed.
