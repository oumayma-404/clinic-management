# Progress: Single-Dentist Practitioner Identity

**Started:** 2026-07-21
**Type:** Small
**Branch:** feature/windows-desktop-app (user declined a dedicated branch)

## Status
- [x] Implementation
- [x] Quality checks — backend `dotnet build ClinicManagement.Application.csproj` → 0 errors, 0 new warnings (44 pre-existing CS8618 in Domain). Frontend `npx tsc --noEmit` → 0 errors. No unit test news-up the changed handlers/hook.
- [x] Tests — `CreateClinicLocalSetupTests.cs` +2 (#15: practitioner setup creates a linked Doctor; a setup missing first/last name is rejected with nothing persisted). #2 (`useDoctors`) is FE → covered by the `tsc`/build gate (no FE test runner in this repo). Green.

## Working tree note (start of session)
Unrelated in-flight work present in the tree — EXCLUDED from any staging for this feature:
- `medication-catalog-picker` feature (untracked `Medication*` files + migration `20260721155459_AddMedicationCatalog*`, `Infrastructure/Extensions.cs`, `Persistence/ApplicationDbContext.cs`, `ApplicationDbContextModelSnapshot.cs`, `appsettings.Development.json`).
- The prior fix `fix-patient-file-tenant-isolation` (5 Files handlers + its progress.md).
- Other approved `features/fix-*` spec folders in this batch.

## Files Changed
- `api/ClinicManagement.Application/DTOs/CreateClinicRequest.cs` — DoctorDto gains optional `UserId`.
- `api/ClinicManagement.Application/Features/Clinics/Queries/GetUserStatusQuery.cs` — projects `UserId = d.UserId`.
- `api/ClinicManagement.Application/Features/Clinics/Commands/CreateClinicCommand.cs` — Local first-run practitioner name validation (#15).
- `web/lib/api/clinics.ts` — DoctorDto gains optional `userId`.
- `web/lib/hooks/use-doctors.ts` — resolve current user's doctor by linked user id (email/name fallback), any role (#2).

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Added optional `UserId` to `DoctorDto` (BE DTO + FE type) + projection in `GetUserStatusQuery` | The spec pins "match by linked user id first"; the DTO didn't expose the link. Additive, response-only field — not consumed by `UpdateDoctorsCommand`, so no input/behavior change. |
| `#15` guard validates FirstName+LastName only when `DoctorInfo` has a non-empty `Specialty` (practitioner intent) | Preserves the existing "no DoctorInfo → admin-only account" behavior; mirrors the Cloud path's required-fields check. Internal, same error semantics as the sibling path. |

## Significant Deviations
(none)
