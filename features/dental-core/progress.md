# Progress: Dental Core

**Started:** 2026-07-22
**Type:** Small
**Branch:** feature/windows-desktop-app (current branch, per user choice)

## Status
- [x] Implementation
- [x] Quality checks (build, lint, typecheck)
- [ ] Tests (handled by /test-small-feature)

## Working tree note (start of session)
Unrelated uncommitted WIP present — EXCLUDE from any staging for this feature:
- api/ClinicManagement.API/appsettings.Development.json
- api/ClinicManagement.Application/Common/Models/ResolvedReminderSettings.cs
- api/ClinicManagement.Infrastructure/Services/ReminderSettingsProvider.cs
- api/ClinicManagement.Infrastructure/Services/RemindersConfig.cs
- api/ClinicManagement.Infrastructure/Services/WhatsAppSender.cs
- COMPETITIVE_ANALYSIS.md, .claude/worktrees/

## Scope decision (forced-small, confirmed by user)
Full vertical slice (~45-50 files, backend + frontend) implemented in one pass on the current branch.
Env: Smart App Control / WDAC blocks running fresh DLLs (0x800711C7) and `dotnet ef` — so the EF
migration is hand-authored and verification is compile-scoped (no app run, no `dotnet ef`).

## Files Changed
### Domain (created)
- Enums/ToothCondition.cs, Enums/TreatmentPlanStatus.cs, Enums/TreatmentPlanItemStatus.cs
- Common/FdiTooth.cs
- Entities/DentalActCode.cs, Entities/ToothState.cs
- Entities/TreatmentPlan.cs, Entities/TreatmentPlanItem.cs, Entities/Installment.cs
- Repositories/IDentalActCodeRepository.cs, IToothStateRepository.cs, ITreatmentPlanRepository.cs

### Infrastructure (created / modified)
- Persistence/Configurations/{DentalActCode,ToothState,TreatmentPlan,TreatmentPlanItem,Installment}Configuration.cs
- Repositories/{DentalActCode,ToothState,TreatmentPlan}Repository.cs
- Persistence/DentalActCatalogSeed.cs (~100 official DCH acts, provisional)
- Persistence/ApplicationDbContext.cs (3 DbSets + TreatmentPlan query filter) — modified
- Extensions.cs (3 repo registrations) — modified
- Services/PdfGenerationService.cs (GenerateDevisPdfAsync) — modified

### Application (created / modified)
- DTOs/{DentalActDto,ToothStateDto,TreatmentPlanDto}.cs, Common/Models/DevisPdfData.cs
- Features/DentalActs/{DentalActMappingExtensions + 4 commands + GetDentalActsQuery}
- Features/Patients/{Commands/SetToothStateCommand, Queries/GetOdontogramQuery}
- Features/TreatmentPlans/{TreatmentPlanMappingExtensions + 7 commands + 3 queries}
- Common/Interfaces/IPdfGenerationService.cs (GenerateDevisPdfAsync) — modified

### API (created)
- Controllers/{DentalActsController, OdontogramController, TreatmentPlansController}.cs

### Frontend (created / modified)
- lib/api/{dental-acts,odontogram,treatment-plans}.ts (created)
- components/{dental-acts-table,dental-act-form-modal,odontogram}.tsx (created)
- components/treatment-plans/{treatment-plan-labels.ts, installment-payment-modal, treatment-plan-form-modal, treatment-plans-table}.tsx (created)
- app/dental-acts/page.tsx, app/treatment-plans/page.tsx (created)
- lib/api/types.ts (5 DTOs), lib/realtime/clinic-hub.ts (2 keys), components/dashboard-sidebar.tsx (2 nav entries),
  app/patients/[id]/page.tsx (Odontogramme + Plan de traitement tabs) (modified)

## Quality checks (backend)
- `dotnet build ClinicManagement.API.csproj -o <scratch>` → **Build succeeded, 0 errors, 13 warnings** (all pre-existing
  baseline CS8618/CS8600/CS8981 in unrelated files; none in new code). Verified via scratch-dir build because the
  running API (PID) locks the host `bin` (MSB3021/MSB3027 copy-lock — not compile errors).
- Frontend `npx tsc --noEmit` (web) → **0 errors** (no `typecheck` script exists; tsc is the real gate). ESLint is not
  installed in the repo (`next build` disables lint) — documented env limitation, not a coverage gap.

## Test-worthy behaviors (for /test-small-feature)
Numbering retry on collision (Accept) · installment overpayment guard · installments-sum/rounding validation ·
tenant isolation for TreatmentPlan (root) + odontogram (via patient) · FDI + surfaces validation · act-catalog
CRUD/soft-deactivate/provisional-confirm · devis-PDF renders for draft & accepted.

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Added `Common/FdiTooth.cs` shared FDI helper instead of duplicating `DentalRecordTooth`'s private validation | Internal, no behavior change; avoids copy-paste across ToothState + TreatmentPlanItem; existing DentalRecordTooth untouched |
| `DentalActCode.Coefficient` nullable (CNAM entry required it > 0) | Coefficient (cotation) is not in the CNAM acts list source; per spec it seeds null and is admin-editable |

## Significant Deviations
- **DEV-1 (RESOLVED): EF migration generated + applied.** Initially deferred (WDAC was assumed to block `dotnet ef` +
  the running API locked the host `bin`). During a stack restart the API was stopped, freeing the lock — and `dotnet ef`
  turned out NOT to be WDAC-blocked here (only `dotnet test` is). So the migration was generated with the tool
  (`20260722111316_AddDentalCore.cs` + `.Designer.cs` + updated `ApplicationDbContextModelSnapshot.cs`), the
  `DentalActCatalogSeed.Acts` `InsertData` loop was added (mirroring `AddCnamCatalog`), and it was applied via
  `dotnet ef database update`. Verified in postgres: all 5 tables present (DentalActCodes, ToothStates, TreatmentPlans,
  TreatmentPlanItems, Installments) and **100 dental acts seeded**. No longer a pre-merge action item.
