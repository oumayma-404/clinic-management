# Progress: Patient-File Tenant Isolation & Delete Integrity

**Started:** 2026-07-21
**Type:** Small
**Branch:** feature/windows-desktop-app (user declined a dedicated branch)

## Status
- [x] Implementation
- [x] Quality checks (build) — `dotnet build ClinicManagement.Application.csproj` → 0 errors, 0 new warnings (44 pre-existing CS8618 in Domain, none in changed files). Backend-only spec; no FE checks needed. No unit test news-up the 5 changed handlers, so the solution still compiles.
- [x] Tests — new `FilesTenantIsolationTests.cs` (8): cross-clinic get/list/download/delete → not-found (AC-1/AC-2); folder-delete commits DB before blobs + survives a blob failure without orphaning (AC-3). Ran via the `vstest` SAC workaround — all green.

## Working tree note (start of session)
Unrelated in-flight work present in the tree — EXCLUDED from any staging for this feature:
- `medication-catalog-picker` feature: untracked `api/.../Entities/Medication*.cs`, `Features/Medications/`, `Controllers/MedicationsController.cs`, `Models/MedicationRequests.cs`, `DTOs/MedicationDto.cs`, `Repositories/MedicationCatalogRepository.cs`, `Persistence/Configurations/Medication*Configuration.cs`, `Persistence/MedicationCatalogSeed.cs`, `IMedicationCatalogRepository.cs`, migration `20260721155459_AddMedicationCatalog*`, `features/medication-catalog-picker/`.
- Modified (medication-related): `Infrastructure/Extensions.cs`, `Persistence/ApplicationDbContext.cs`, `Migrations/ApplicationDbContextModelSnapshot.cs`, `appsettings.Development.json`.
- Other approved bug-fix spec folders under `features/fix-*` (this batch).

## Files Changed
- `api/ClinicManagement.Application/Features/Files/Queries/GetPatientFilesQuery.cs`
- `api/ClinicManagement.Application/Features/Files/Queries/GetPatientFoldersQuery.cs`
- `api/ClinicManagement.Application/Features/Files/Queries/DownloadPatientFileQuery.cs`
- `api/ClinicManagement.Application/Features/Files/Commands/DeletePatientFileCommand.cs`
- `api/ClinicManagement.Application/Features/Files/Commands/DeletePatientFolderCommand.cs`

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| `GetPatientFilesQuery`/`GetPatientFoldersQuery` also verify the scoped folder/parent-folder belongs to the resolved patient (not just the patient's clinic) | Closes the "own patientId + foreign folderId" variant of the same hole; satisfies AC-1 for folder-scoped reads. Internal, no contract change. |

## Significant Deviations
(none)
