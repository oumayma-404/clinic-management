# Progress: Medication Catalog Picker (Ordonnance)

**Started:** 2026-07-21
**Type:** Small
**Branch:** feature/windows-desktop-app (user chose to reuse current branch — matches recent official-documents commits)

## Status
- [x] Implementation
- [x] Quality checks (dotnet build 0/0, tsc --noEmit clean, next build clean)
- [x] Tests (added — see Test Plan + Tests Run below; 39 passed)

## Test Plan
| AC | Action | Target file | Notes |
|----|--------|-------------|-------|
| AC-1 | New test class | UnitTests/Features/Medications/MedicationCrudTests.cs | provisional+active default, ≥1 DCI required, global (no ClinicId), combination DCIs captured |
| AC-1 | New test class | UnitTests/Infrastructure/Persistence/MedicationCatalogSeedTests.cs | starter seed integrity: every med has brand + ≥1 DCI, ≥1 combination product, deterministic ids, counts match |
| AC-2 | New test class | UnitTests/Features/Medications/GetMedicationsQueryHandlerTests.cs | filter by brand/DCI/form/strength (case-insensitive), blank=no filter, trims, dcis mapped, IncludeInactive forwarded |
| AC-3 | New test class | UnitTests/Api/MedicationsControllerAuthorizationTests.cs | mutations require AdminOnly; reads no admin policy; class-level [Authorize]; nothing anonymous |
| AC-8 | (in CRUD) | MedicationCrudTests.cs | duplicate brand+form+strength rejected with French message |

Coverage notes (no unit-test surface):
- **AC-4 / AC-5 / AC-6 / AC-7** are frontend-only (admin page + lock card, editor combobox fill, free-text
  fallback, legacy round-trip). `web/` has no test runner (per LEARNINGS the FE gate is `tsc --noEmit` +
  `next build`), so these are covered by the green typecheck + production build run at implementation time,
  not by unit tests.
- **"Seed rows are provisional" (AC-1)** at the DB level was verified against the live database
  (`provisional=25`) after the migration applied; the unit tests assert the entity's provisional-by-default
  invariant + seed integrity.

## Tests Run
| Suite | Filter | Result |
|-------|--------|--------|
| Unit (xUnit) | `FullyQualifiedName~Medication` (4 medication classes) | **39 passed, 0 failed** |

## Test-run environment note
Two Windows blockers required the documented workaround (per MEMORY `smart-app-control-blocks-tests`):
Smart App Control blocks `dotnet test` on freshly-built DLLs, and the running API locks the shared `bin`.
Both were dodged by building the test project to an isolated scratch `OutDir` and running `dotnet vstest`
on the built DLL.

**Shared UnitTests project is currently RED from concurrent unrelated WIP** — `NotificationJob.cs` and
`CreateAppointmentCommand.cs` were modified in the working tree *during this session* (parallel
agents/worktrees; not committed at HEAD, not part of this feature) adding ctor params (`ILogger`,
`IServiceScopeFactory`) without updating `NotificationJobTests`/`AppointmentTenantIsolationTests`/
`AppointmentSyncMappingTests`/`NotificationGenerationTests`. To run the medication tests in isolation, those
4 sibling files were temporarily excluded via a throwaway `Directory.Build.targets` (since deleted — no
existing file was edited). Those 4 failing tests are NOT this feature's responsibility; whoever owns that
WIP must update them before the full unit suite compiles.

## Working tree note (start of session)
Unrelated changes present at start — EXCLUDE from this feature's commits:
- `api/ClinicManagement.API/appsettings.Development.json` (pre-existing modification)
- `.claude/worktrees/` (untracked tooling dir)
Include: `features/medication-catalog-picker/` (this feature).

## Files Changed
Backend (created):
- Domain/Entities/Medication.cs, MedicationActiveIngredient.cs
- Domain/Repositories/IMedicationCatalogRepository.cs
- Infrastructure/Repositories/MedicationCatalogRepository.cs
- Infrastructure/Persistence/Configurations/MedicationConfiguration.cs, MedicationActiveIngredientConfiguration.cs
- Infrastructure/Persistence/MedicationCatalogSeed.cs
- Infrastructure/Migrations/20260721155459_AddMedicationCatalog.cs (+ .Designer.cs + snapshot) — EF-tool generated; seed inserts hand-added to Up()
- Application/DTOs/MedicationDto.cs
- Application/Features/Medications/Commands/{Create,Update,Deactivate,ConfirmMedicationData}Command.cs, MedicationMapper.cs
- Application/Features/Medications/Queries/GetMedicationsQuery.cs
- API/Controllers/MedicationsController.cs, API/Models/MedicationRequests.cs

Backend (modified):
- Infrastructure/Persistence/ApplicationDbContext.cs (DbSets Medications + MedicationActiveIngredients)
- Infrastructure/Extensions.cs (register IMedicationCatalogRepository)

Frontend (created):
- web/lib/api/medications.ts (medicationsApi)
- web/components/medication-catalog-table.tsx, medication-form-modal.tsx
- web/app/medications/page.tsx (admin-gated)

Frontend (modified):
- web/lib/api/types.ts (MedicationDto)
- web/lib/realtime/clinic-hub.ts (RealtimeResource.Medications — backend already broadcasts it structurally)
- web/components/dashboard-sidebar.tsx (admin-only "Médicaments" nav item)
- web/components/document-editor-content.tsx (MedicationLine type; medication catalog load for prescriptions;
  MedicationItem free-text Input + catalog combobox lookup → stores medicationId + dci snapshot + label
  "Brand Strength Form"; free-text edit clears the catalog link; catalog passed through)

## Quality checks
- Backend: `dotnet build ClinicManagement.sln` → 0 errors; no warnings in any Medication* file (13 pre-existing
  solution warnings unrelated: CS8618/CS8981/CS8602/CS0618).
- Frontend: `npx tsc --noEmit` clean; `npm run build` clean (new `/medications` route built, editor route OK).

## Scope notes
- Realtime live-refresh on the admin page IS wired (matches CNAM) — one FE key + subscription; the backend
  RealtimeBroadcastBehavior already broadcasts "medications" structurally (namespace-derived, not excluded).
- Printed medication label = "Brand Strength Form" (user decision at spec time; DCI captured on the line but
  not printed).
- DCI normalization is Trim-only + case-insensitive dedupe (case preserved for display); slice 2's interaction
  check can match case-insensitively without a data migration.

## Migration note
Migration generated with the EF tool AFTER stopping the running API (PID 34088, user-approved) to
release the bin lock. Schema is tool-scaffolded; seed InsertData loops hand-added to Up() (EF does not
scaffold seed data — same approach as AddCnamCatalog). API left stopped for the user to restart.

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|

## Significant Deviations
(none yet)
