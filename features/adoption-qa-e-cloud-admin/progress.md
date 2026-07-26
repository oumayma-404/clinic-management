# Progress: Adoption QA — Batch E (Cloud admin gap)

**Started:** 2026-07-24
**Type:** Small
**Branch:** feature/windows-desktop-app

## Status
- [x] Implementation
- [x] Quality checks (dotnet build 0/0)
- [ ] Tests (handled by /test-small-feature)

## Files Changed
- `api/.../Features/Clinics/Commands/CreateClinicCommand.cs` — Cloud branch: the clinic creator's `userRole` is now `"admin"` (was their selected doctor/secretary role). The DB user role **and** the Auth0 `app_metadata` push (`UpdateUserMetadataAsync(userId, clinicId, userRole)`) both receive "admin". The selected clinical role still drives whether a `Doctor` record is created + linked.

## Notes
- Local first-run (`CreateLocalFirstRunAsync`) already minted an "admin" — unchanged (AC-4).
- `JoinClinicCommand` (joining an existing clinic) is untouched, so subsequent members keep their selected non-admin role (AC-3).
- Out of scope: retrofitting existing Cloud clinics created before this fix (needs a promotion tool/migration).
