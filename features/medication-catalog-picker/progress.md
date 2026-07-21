# Progress: Medication Catalog Picker (Ordonnance)

**Started:** 2026-07-21
**Type:** Small
**Branch:** feature/windows-desktop-app (user chose to reuse current branch — matches recent official-documents commits)

## Status
- [x] Implementation
- [x] Quality checks (dotnet build 0/0, tsc --noEmit clean, next build clean)
- [ ] Tests (handled by /test-small-feature)

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
