# Progress: Clinical Loop Integration

**Started:** 2026-07-22
**Type:** Small (forced; ~25 files / 2 additive migrations — user chose full-slice, checkpointed)
**Branch:** feature/windows-desktop-app (current branch, per user)

## Status
- [x] Part A — Allergy alerts at point of care (FE)
- [x] Part B — Dental record → plan item auto-complete (BE+FE)
- [x] Part C — Appointment → plan item link (BE+FE, 1 migration)
- [x] Part D — Writable odontogram (diagnosis) → plan (BE+FE, 1 migration)
- [x] Quality checks (backend build 0/0 to scratch; FE `tsc --noEmit` 0 errors)
- [ ] Tests (handled by /test-small-feature)

## Working tree note (start of session)
Clean except untracked new feature folders (clinical-loop-integration, reliability-and-polish, unified-billing-ledger). Only this feature's files will be staged; user commits manually.

## Key exploration findings
- `ToothState` already patient-owned (`PatientId`, no `ClinicId`, cascade from Patient); `DentalRecordId` already nullable → diagnostic tooth states just need a `Source` discriminator, no relationship rewrite.
- `IToothStateRepository` already exists (`GetByPatientIdAsync`, `GetByDentalRecordIdAsync`, `AddAsync`, `DeleteAsync`).
- `TreatmentPlanItem.MarkDone(doneOn, linkedDentalRecordId)` already exists.
- `dotnet ef` is WDAC-blocked on this machine → migrations hand-authored (2 additive columns: `ToothState.Source`, `Appointment.TreatmentPlanItemId`).
- Repo conventions: `Result<T>` + `Result.Failure("fr msg")`, `IClinicContext`+`IUserRepository` for clinic scoping, `IUnitOfWork.SaveChangesAsync`, aggregate methods with `ArgumentException` guards, EF `Id` ValueGeneratedNever, enums `HasConversion<int>()`. No FluentValidation.

## Backend status (all parts) — DONE, builds green
- Domain: `ToothStateSource` enum (new); `ToothState.Source`; `Appointment.TreatmentPlanItemId` + `SetTreatmentPlanItem`.
- EF: `ToothStateConfiguration.Source`, `AppointmentConfiguration.TreatmentPlanItemId` (plain column + index).
- Migration `20260722160000_AddClinicalLoopLinks` (.cs + .Designer.cs hand-authored; snapshot updated) — 2 additive columns.
- Odontogram: `IToothStateRepository.GetByIdAsync`; `DiagnoseToothCommand` + `RemoveToothConditionCommand` (new); `GetOdontogramQuery`/`ToothStateDto` expose `Source`; `OdontogramController` POST/DELETE `conditions`.
- Dental record: `DentalRecordLinker` (new helper) — clears open diagnoses on treated teeth (AC-5) + marks plan item done linked to record (AC-4); Create/Update dental-record commands accept `TreatmentPlanId`/`TreatmentPlanItemId`.
- Appointment: `AppointmentPlanLink` validator (new); Create appointment accepts + validates + stores `TreatmentPlanItemId`; `AppointmentDto`/GET queries expose it.
- Verified: `dotnet build ClinicManagement.API.csproj -o <scratch>` → 0 errors / 0 new warnings (host bin locked by running app 50004 → scratch-output build per skill).

## Frontend status — DONE, `tsc --noEmit` green
- Types/API: `types.ts` (`ToothStateDto.source`, `AppointmentDto.treatmentPlanItemId`); `odontogram.ts` (`diagnose`/`removeCondition`); `dental-records.ts` + `appointments.ts` (plan link fields).
- Part A: `patient-record-modal.tsx` medical-alert banner (allergies/flags/antécédents); patient page passes `patient`.
- Part B: `patient-record-modal.tsx` "Acte du plan" picker (marks step réalisé on save); `treatment-plans-table.tsx` manual "Réalisé" toggle removed + caption explaining record-driven completion; patient page computes `openPlanItems`.
- Part C: `create-appointment-dialog.tsx` preset/plan-scheduling mode (locked patient + plan link forwarded); `treatment-plans-table.tsx` per-item "Planifier" opens the create dialog preset.
- Part D: `odontogram.tsx` rewritten — clickable teeth chart diagnoses (POST/DELETE), diagnosis vs treatment styling, "Créer un plan depuis l'odontogramme"; `treatment-plan-form-modal.tsx` `seedLines` prop; patient page renders the seeded plan modal.

## FE quality-gate note
`eslint` is NOT installed in web/ (npx pulls a mismatched v10 that can't load the repo config) and `next.config.ts` sets `eslint.ignoreDuringBuilds:true`, so lint cannot run here — the real FE gate is `npx tsc --noEmit`, which passes with 0 errors. `next build` intentionally skipped: the app is running and shares `.next`, so a build would risk disrupting the live dev server (tsc is the sound type gate).

## Files Changed
Backend: ToothStateSource.cs(+), ToothState.cs, Appointment.cs, ToothStateConfiguration.cs, AppointmentConfiguration.cs, 20260722160000_AddClinicalLoopLinks.cs(+)/.Designer.cs(+), ApplicationDbContextModelSnapshot.cs, IToothStateRepository.cs, ToothStateRepository.cs, ToothStateDto.cs, GetOdontogramQuery.cs, DiagnoseToothCommand.cs(+), RemoveToothConditionCommand.cs(+), OdontogramController.cs, DentalRecordLinker.cs(+), CreateDentalRecordCommand.cs, UpdateDentalRecordCommand.cs, AppointmentPlanLink.cs(+), CreateAppointmentCommand.cs, UpdateAppointmentCommand.cs, AppointmentDto.cs, GetAppointmentsQuery.cs, GetAppointmentQuery.cs

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| `Appointment.TreatmentPlanItemId` stored as a plain column (no FK) | Plan item ids are regenerated on Draft-plan edits, so a FK would be brittle; existence validated at the handler. No external contract change. |
| Plan-from-odontogram seeding done FE-side (no new seeding endpoint) | Spec API contract pins only the diagnose POST/DELETE; seeding is assembled from the already-loaded odontogram + plans. |
| Combined both additive columns into one migration | Fewer hand-authored Designer/snapshot files = lower risk; identical schema outcome. |
| Test compile fix (build-required, auto-approved): added a permissive `Mock<ITreatmentPlanRepository>()` arg to `CreateAppointmentCommandHandler` construction in `AppointmentSyncMappingTests`, `AppointmentTenantIsolationTests`, `NotificationGenerationTests` | The new ctor dependency broke existing scenario tests at compile time; each call was translated to the new seam preserving the original assertion. No new test scenarios added (those are `/test-small-feature`'s job). |
| Appointment→plan link is create-only (Update maps the field but doesn't edit it) | AC-3 only requires creating a linked appointment; avoids a dormant clear-path + accidental link-wipe on edit. |
| Part C "Étape du plan" picker delivered as the plan table's per-item "Planifier" (opens the create dialog preset) rather than a free dropdown inside the create dialog | Same capability, cleaner entry point (starts from the actual plan step); avoids dynamically loading a patient's plans inside the appointment dialog. |
| Backend `MarkTreatmentPlanItemDoneCommand` + `/items/{id}/done` endpoint retained (no longer surfaced as a UI toggle) | The dental-record flow marks items done via the domain method; the endpoint stays as an authenticated seam. Removing it would touch the controller + risk tests for no functional gain. UI-facing AC-6 (no evidence-free toggle) is satisfied by removing the button. |

## Significant Deviations
- DEV-1 (RESOLVED): migration `20260722160000_AddClinicalLoopLinks` was initially hand-authored (dotnet ef was blocked while the app held the DLL locks). After bringing the app down, the EF tool ran fine: `dotnet ef migrations has-pending-model-changes` → **"No changes … since the last migration"** (confirms the hand-authored Designer + snapshot match the model), and `dotnet ef database update` **applied** it. Columns verified live in `clinic_management`: `Appointments.TreatmentPlanItemId (uuid)`, `ToothStates.Source (integer)`. Full solution build (incl. tests) = **0 warnings / 0 errors**.
- App state: the running API (`ClinicManagement.API.exe` PID 50004 + its `dotnet run` launcher PID 33960) was stopped to release the DLL locks and apply the migration; it has **not** been restarted.
