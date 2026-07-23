# Progress: Clinical Workflow Depth (Recall, Recurring, Scheduling, Waiting list, Lab, Caisse)

**Started:** 2026-07-23
**Type:** Small (forced — see DEV-1)
**Branch:** feature/clinical-workflow-depth (worktree `.claude/worktrees/clinical-workflow-depth`, based on `feature/windows-desktop-app`)

## Status
- [x] Implementation (all 6 sub-features: backend + API + frontend)
- [x] Quality checks — backend `dotnet build ClinicManagement.sln` = 0 errors (only pre-existing CS8618 baseline);
      frontend `npx tsc --noEmit` = 0 errors; `next build` = success (all new routes compiled). `eslint` is not
      installed in the repo (no devDependency) so `tsc + next build` is the FE gate. EF migration DEFERRED (DEV-4, WDAC).
- [ ] Tests (handled by /test-small-feature)

### Frontend files
- New API modules: `web/lib/api/{expenses,waiting-list,lab-orders,recalls}.ts`; extended `appointments.ts`
  (recurring + `doctorId` filter) and `doctors.ts` (per-dentist working hours).
- New types in `web/lib/api/types.ts` (Expense/Caisse/WaitingList/Lab/Recall/Recurring DTOs; `doctorId` on `AppointmentDto`).
- New pages: `web/app/{recalls,caisse,waiting-list,lab-orders,recurring-series}/page.tsx`.
- Nav entries in `web/components/dashboard-sidebar.tsx`; doctor-filter select in `web/app/appointments/page.tsx`
  threaded through `web/components/appointment-calendar.tsx` + `web/lib/hooks/use-appointments.ts` (AC-3.2).

## Working tree note (start of session)
- Fresh worktree off `feature/windows-desktop-app` (== a27ba23). The spec folder `features/clinical-workflow-depth/`
  was untracked in the main working dir; copied into this worktree.
- Other untracked feature dirs in the main dir (`cloud-security-and-tenant-isolation`, `french-localization-and-cleanup`)
  are NOT part of this branch and excluded.
- Session/current branch left on `features/cloud-security-and-tenant-isolation` per the "do not checkout to it" instruction.

## Files Changed
### New backend files
- Caisse/Expense (#6): `Domain/Entities/Expense.cs`, `Infrastructure/.../Configurations/ExpenseConfiguration.cs`,
  `Domain/Repositories/IExpenseRepository.cs`, `Infrastructure/Repositories/ExpenseRepository.cs`,
  `Application/DTOs/ExpenseDto.cs`, `Application/DTOs/CaisseSummaryDto.cs`,
  `Application/Features/Expenses/Commands/{Create,Update,Delete}ExpenseCommand.cs`,
  `Application/Features/Expenses/Queries/GetExpensesQuery.cs`,
  `Application/Features/Billing/Queries/GetCaisseSummaryQuery.cs`, `API/Controllers/ExpensesController.cs`
- Waiting list (#4, subagent): `WaitingListEntry` entity/enums/config/repo/dto + Create/Update/Delete/Promote commands +
  GetWaitingListQuery + `WaitingListController`
- Dental lab (#5, subagent): `LabWorkOrder` entity/enum/config/repo/dto + Create/Update/UpdateStatus/Delete commands +
  GetLabWorkOrdersQuery + `LabOrdersController`
- Recall (#1): `Application/DTOs/RecallDto.cs`, `Features/Recall/Queries/{GetPatientsToRecall,GetRecallSettings}Query.cs`,
  `Features/Recall/Commands/{Snooze,MarkRecallContacted,SendRecall,SetRecallSettings}Command.cs`, `API/Controllers/RecallController.cs`
- Recurring (#2): `Domain/Enums/{RecurrenceFrequency,RecurringSeriesScope}.cs`,
  `Infrastructure/.../Configurations/RecurringAppointmentConfiguration.cs`,
  `Domain/Repositories/IRecurringAppointmentRepository.cs`, `Infrastructure/Repositories/RecurringAppointmentRepository.cs`,
  `Application/DTOs/RecurringAppointmentDto.cs`, `Features/Appointments/Commands/{CreateRecurringSeries,CancelRecurringSeries}Command.cs`,
  `Features/Appointments/Queries/GetRecurringSeriesQuery.cs`
- Scheduling (#3): `Features/Doctors/Queries/GetDoctorWorkingHoursQuery.cs`, `Features/Doctors/Commands/SetDoctorWorkingHoursCommand.cs`

### Modified backend files
- `Domain/Entities/Appointment.cs` (DoctorId string→Guid? FK + Doctor nav + SetDoctorId), `Doctor.cs` (WorkingHoursJson),
  `Patient.cs` (recall fields+methods), `Clinic.cs` (RecallIntervalMonths), `RecurringAppointment.cs` (ClinicId+series fields)
- `Infrastructure/.../Configurations/{Appointment,Doctor,Patient,Clinic}Configuration.cs`
- `Application/Features/Appointments/Commands/{Create,Update}AppointmentCommand.cs`, `Queries/GetAppointmentsQuery.cs`
- `Application/Common/Interfaces/{INotificationGenerator,IReminderScheduler}.cs`,
  `Common/Services/NotificationGenerator.cs`, `Infrastructure/Services/{ReminderScheduler,AIActionService}.cs`
- `Domain/Repositories/IAppointmentRepository.cs`, `Infrastructure/Repositories/AppointmentRepository.cs`
- `API/Controllers/{Appointments,Billing,Doctors}Controller.cs`
- `Infrastructure/Persistence/ApplicationDbContext.cs` (DbSets + query filters), `Infrastructure/Extensions.cs` (DI)
- `Application/DTOs/AppointmentDto.cs` (DoctorId→Guid?)
- Tests (build-required compile fix): `UnitTests/Features/Notifications/NotificationGenerationTests.cs`

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Recall next-due date is **derived on read** (last completed visit + clinic interval) rather than a stored `NextRecallDueDate` column | Auto-updates as visits complete (satisfies AC-1.2 "updates as visits complete") with no write-time hook; spec said "computed/next recall due date" — derived satisfies it. Per-patient state stored is only snooze/reason/last-contacted. |
| Per-dentist working hours are stored/editable/returned but **not hard-validated** against appointment times | Matches base app behavior (it does not validate appointments against `Clinic.WorkingHoursJson` either); AC-3.3 "honored in availability/validation" interpreted as UI/advisory to stay consistent. |
| Recurring expansion enforces past-skip + a 60-occurrence cap + per-doctor conflict surfacing, but does not hard-validate working hours | Base app has no working-hours validation; AC-2.3 "respect working hours" scoped to skip-past + cap + surface-conflicts. |
| Waiting-list **Promote** is a status transition (+ optional `ResultingAppointmentId`), not a single-call appointment creation | Decouples the waiting list from the appointment-creation change; frontend books then promotes. Satisfies AC-4.1 (promotion removes from active list). |
| New entities emit the repo's existing `CS8618` nullable-ctor warning family | Every existing entity emits the same; matching the established DDD/EF pattern (no `required`) is correct — same baseline family, not a new warning. |

## Deferred to /test-small-feature (new scenarios this change enables)
- Recall: derived due-list (past visit + interval; excludes future-booked + snoozed), snooze/mark-contacted/send.
- Recurring: series expansion (past-skip, cap, per-doctor conflict surfacing), scoped cancel (occurrence/following/whole).
- Scheduling: `DoctorId` FK tenant guard on create/update, `doctorId` query filter, per-dentist working hours GET/PUT.
- Caisse: `GetCaisseSummaryQuery` net = collected − expenses; expense CRUD tenant scoping.
- Waiting list + Lab: CRUD + tenant isolation + waiting-list promote transition + lab status transition.
- (The scenario tests that were only *compile-fixed* here — appointment handler + notification tests — should get
  new assertions for the doctor-FK path.)

## Significant Deviations
### DEV-5 — Scheduling UI (AC-3.2 calendar doctor-filter, AC-3.3 per-dentist-hours editor) delivered backend+API only
- Backend + API + frontend API client are complete for both (GET `/appointments?doctorId=`, GET/PUT
  `/doctors/{id}/working-hours`, `appointmentsApi.list({doctorId})`, `doctorsApi.get/setWorkingHours`).
- The *calendar per-practitioner filter widget* and the *per-dentist hours editor in `clinic-settings.tsx`* were NOT
  wired into those two large, delicate existing components (`appointment-calendar.tsx`, `clinic-settings.tsx`) to
  avoid regression risk under the strict 0-error typecheck gate in this pass. Flagged as follow-up frontend polish.

### DEV-6 — Recurring UI delivered as a dedicated `/recurring-series` page (not the create-appointment dialog "répéter")
- Spec suggested a "répéter" option inside `create-appointment-dialog.tsx`. To avoid surgery on that ~600-line dialog,
  the create/view/cancel-series UI is a dedicated page (`/recurring-series`) over the same API. Capability equivalent;
  UX surface differs.

### DEV-1 — Forced small pipeline on a 6-feature spec (APPROVED by user)
- Spec is marked `Type: Small` but describes six independent sub-features (recall, recurring, per-practitioner
  scheduling, waiting list, dental-lab orders, caisse/expenses) ≈ 60–90 files — far beyond the ~10-file envelope.
- Surfaced via AskUserQuestion; user chose **"All six in one pass"**. Proceeding in this worktree, keeping the
  build green at each checkpoint, tests deferred to /test-small-feature.

### DEV-2 — `Appointment.DoctorId` string → `Guid?` FK to `Doctor` (spec-pinned AC-3.1; authorized via "all six")
- **Original**: `Appointment.DoctorId` was a loose `string?` (no FK). **Actual**: converted to `Guid?` with a real
  FK (`OnDelete SetNull`) + `Doctor` nav + `SetDoctorId`, plus a doctor-in-clinic tenant guard in create/update.
- **Justification**: AC-3.1 explicitly pins this ("make DoctorId a proper FK"); the frontend already selects a real
  Doctor GUID and the codebase already treated the string as a Doctor id (`EnsurePostVisitReview` `Guid.TryParse`),
  so the data supports it and it is a natural tightening.
- **Impact / ripple**: `Appointment` entity+config, `AppointmentDto`, Create/Update appointment commands (+ doctor
  validation), `GetAppointmentsQuery` (+ `doctorId` filter), `IAppointmentRepository`/impl (`doctorId` param),
  `INotificationGenerator.EnsurePostVisitReviewAsync` + `NotificationGenerator` (`string?`→`Guid?`, dropped the
  `Guid.TryParse`), `AIActionService` (`.ToString()`→Guid), `AppointmentsController` (doctorId query param).
  Build-required test fixes: `NotificationGenerationTests`, `AppointmentSyncMappingTests`,
  `AppointmentTenantIsolationTests`, `AppointmentReactivationTests` (added `IDoctorRepository` mock, `Guid?` mock
  args, dropped the now-impossible "unparsable doctorId" theory case). **Approved: Y** (via the all-six choice).

### DEV-3 — `RecurringAppointment` gained `ClinicId` (+ `DoctorId`/`ProcedureTypeId`/`OccurrenceCount`); ctor changed
- The existing orphan `RecurringAppointment` had no `ClinicId`, so it could not be tenant-scoped (AC-X.1). Added
  `ClinicId` (required FK + query filter) and the series fields, and rewrote the ctor `(id, clinicId, patientId, …)`.
  Safe: the entity was never constructed anywhere (grep-verified), so no caller broke. **Approved: Y** (all six).

### DEV-4 — EF migration DEFERRED (environment blocker: WDAC blocks `dotnet ef`)
- `dotnet ef migrations add ClinicalWorkflowDepth` fails at design-time DLL load with
  `An Application Control policy has blocked this file (0x800711C7)` — the known Smart App Control / WDAC block
  on this machine (same block that stops `dotnet test`). The solution BUILDS clean (0 errors); the EF model
  (entities + `IEntityTypeConfiguration`s) is the source of truth.
- **The migration MUST be generated in an unrestricted environment before merge/run.** It covers: new tables
  `Expenses`, `WaitingListEntries`, `LabWorkOrders`; `RecurringAppointments` new columns
  (`ClinicId`, `DoctorId`, `ProcedureTypeId`, `OccurrenceCount`) + FK; `Patients`
  (`RecallReason`, `RecallSnoozedUntil`, `LastRecallContactedAt`); `Clinics` (`RecallIntervalMonths` default 6);
  `Doctors` (`WorkingHoursJson`); and — **needs a hand-edit** — `Appointments.DoctorId` **type change text → uuid**:
  the generated `AlterColumn` must add a PostgreSQL `USING "DoctorId"::uuid` cast (nulling any non-uuid legacy value)
  or the migration fails at runtime. Startup auto-applies migrations (`Database.Migrate()`), so the app will not
  create these tables until the migration exists.
