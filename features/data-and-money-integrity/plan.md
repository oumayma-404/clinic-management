# Implementation Plan: Data & Money Integrity

**Status:** APPROVED
**Created:** 2026-07-27
**Spec:** [spec.md](./spec.md) (APPROVED)
**Exploration:** [exploration.md](./exploration.md)
**Branch:** feature/windows-desktop-app (existing)

---

## Overview

One story, **US-1**, covering all eight audit findings plus the adjacent defects. The user explicitly chose a single
story over the recommended 11-story split; that decision is honored and recorded as **R-1** rather than re-litigated.

To keep it implementable, US-1 is structured as **eleven ordered parts (A–K)**. Each part is a *vertical* increment —
domain → persistence → API → UI → tests — never a technical-layer grouping. Each ends at a clean build gate and is a
natural commit boundary, so `/implement-story` can land the story incrementally and resume at a part boundary.

**Part order is the spec's implementation order** (`H → A → B → C → D → F → E → G`), with the invoice detail modal
folded into the payment-void part so it is never a foundation-only step.

Two orderings are load-bearing and must not be reordered:
- **Part A ships first** — the reconciliation report must be runnable *before* any data migration, on a stopped app.
- **Part H's null-safe code deploys before Part H's blanking migration** — in Local mode migrations run *after*
  Kestrel is serving, so blanking first takes patient search down for the clinic.

### Approach decisions (settled, not open)

| Decision | Choice | Why |
|---|---|---|
| Conflict plumbing | Translate `DbUpdateConcurrencyException` → `ConflictException` **inside `UnitOfWork.SaveChangesAsync`**, then add `when (ex is not ConflictException)` to each affected handler's existing catch-all | One line per file instead of a 4-line block; the four broken numbering-retry filters fix themselves because a `ConflictException` is no longer a `DbUpdateException` |
| Version access | **Mapped `Version` property on `Entity<TId>`**, configured to the `xmin` system column | Reading is `entity.Version` everywhere, so the ~18 hand-written DTO mapping sites become one-line additions and no handler needs a new ctor dependency. The alternative (shadow property + accessor interface) costs ~20 ctor injections |
| Modal conflict UX | Extract `<FormErrorBanner>` + `useConflict` on the first modal, apply to **all seven** | Only `payment-modal.tsx` has an inline banner today; the rest are toast-only and a toast fires behind an open dialog |
| Report host | **Console verb**, logic in an Application-layer service | Mirrors `AdminPasswordRecoveryService`: the verb must run on a stopped app, and `UnitTests` references only Application |
| Report exit codes | `0` clean · `1` couldn't run · `2` ran, found drift | Both existing verbs only use `1` = couldn't run. A config typo and a broken ledger must not look identical |

### Two free wins found during exploration

- **Cross-clinic reads cost nothing.** `ICurrentClinicProvider` is registered in `AddApplication()`, not
  `AddInfrastructure()`. A console verb that calls only `AddInfrastructure(configuration)` gets a DbContext with
  **every clinic query filter inactive** — no `IgnoreQueryFilters()` needed anywhere in the report.
- **No `DbSet<InstallmentPayment>` is needed, and that is the existing convention, not a new constraint.** There is
  no `DbSet<Payment>`, `DbSet<Installment>`, `DbSet<InvoiceLine>` or `DbSet<TreatmentPlanItem>` either. EF discovers
  the type from the parent's `HasMany`. So spec AC-22's "no DbSet is exposed" is satisfied by doing nothing.

---

## Files to Modify / Create

Roughly **210 files**. Grouped by part; `+` = new.

### Part A — Reconciliation report
```
+ api/ClinicManagement.Application/Common/Maintenance/MoneyReconciliationService.cs
+ api/ClinicManagement.Application/Common/Maintenance/MoneyReconciliationReport.cs   (result model)
+ api/ClinicManagement.API/Maintenance/ReconcileMoneyCommand.cs
  api/ClinicManagement.API/Program.cs                        (third verb interception, ~line 35)
+ api/ClinicManagement.UnitTests/Common/Maintenance/MoneyReconciliationServiceTests.cs
+ api/ClinicManagement.UnitTests/Api/Maintenance/ReconcileMoneyCommandTests.cs
  packaging/README.md
```

### Part B — Patient delete blocks + archive
```
  api/ClinicManagement.Domain/Entities/Patient.cs                          (IsArchived, ArchivedAt, Archive, Unarchive)
  api/ClinicManagement.Domain/Repositories/IPatientRepository.cs           (+ includeArchived, + counts)
  api/ClinicManagement.Infrastructure/Persistence/Configurations/PatientConfiguration.cs
        └─ DELETE the duplicate HasMany(p => p.Appointments) block (:122-125)  ← the A1 fix
        └─ + IsArchived/ArchivedAt props, + HasIndex(ClinicId, IsArchived)
  api/ClinicManagement.Infrastructure/Repositories/PatientRepository.cs
+ api/ClinicManagement.Application/Features/Patients/Queries/GetPatientDeletionCheckQuery.cs
+ api/ClinicManagement.Application/Features/Patients/Commands/ArchivePatientCommand.cs
+ api/ClinicManagement.Application/Features/Patients/Commands/UnarchivePatientCommand.cs
  api/ClinicManagement.Application/Features/Patients/Commands/DeletePatientCommand.cs   (pre-check + real message)
  api/ClinicManagement.Application/Features/Patients/Queries/GetPatientsQuery.cs        (exclude archived)
  api/ClinicManagement.Application/Features/Recall/Queries/GetPatientsToRecallQuery.cs  (exclude archived)
+ api/ClinicManagement.Application/DTOs/PatientDeletionCheckDto.cs
  api/ClinicManagement.Application/DTOs/PatientDto.cs                      (+ isArchived)
  api/ClinicManagement.API/Controllers/PatientsController.cs               (3 routes)
+ api/ClinicManagement.Infrastructure/Migrations/*_FixPatientAppointmentDeleteBehavior.cs   (M1)
+ api/ClinicManagement.Infrastructure/Migrations/*_AddPatientArchive.cs                     (M2)
  web/lib/api/patients.ts · web/lib/api/types.ts
  web/components/patients-table.tsx        (pre-check on dialog open, two dialog states, archive action)
  web/app/patients/[id]/page.tsx           (archived banner + unarchive)
  + 3 create/booking patient pickers       (exclude archived + refuse an archived patientId)
+ api/ClinicManagement.UnitTests/Features/Patients/PatientArchiveTests.cs
+ api/ClinicManagement.UnitTests/Features/Patients/PatientDeletionGuardTests.cs
```

### Part C — Appointment update tri-state
```
  api/ClinicManagement.Application/Features/Appointments/Commands/UpdateAppointmentCommand.cs
        └─ ProcedureTypeId / DoctorId / Notes / DoctorName → backing field + [JsonIgnore] *Specified
        └─ status parse failure and duration<=0 now return Result.Failure
  web/components/edit-appointment-dialog.tsx    (send explicit null, « Aucun praticien » sentinel option)
  web/lib/api/appointments.ts
+ api/ClinicManagement.UnitTests/Features/Appointments/AppointmentPartialUpdateTests.cs
  api/ClinicManagement.UnitTests/Features/Appointments/AppointmentSyncMappingTests.cs  (fixture gains a procedure type)
```

### Part D — Invoice payment void + detail modal
```
  api/ClinicManagement.Domain/Entities/Payment.cs        (IsVoided, VoidedAt, VoidReason, VoidedByUserId,
                                                          VoidedByName, SourceInstallmentPaymentId, Void())
                                                          + docstring "Immutable once created" is now false
  api/ClinicManagement.Domain/Entities/Invoice.cs        (VoidPayment, recompute AmountCollected,
                                                          Cancel guard → non-voided, + EInvoiceStatus guard,
                                                          RecordPayment rounds + rejects sub-millime)
+ api/ClinicManagement.Application/Features/Invoices/Commands/VoidPaymentCommand.cs
  api/ClinicManagement.Application/Features/Invoices/Commands/RecordPaymentCommand.cs   (PaidOn validation)
  api/ClinicManagement.Application/Features/Invoices/InvoiceMappingExtensions.cs
  api/ClinicManagement.Application/DTOs/InvoiceDto.cs    (PaymentDto void fields, canCancel, canCreateAvoir)
  api/ClinicManagement.Application/Features/Invoices/Queries/GetPaymentReceiptPdfQuery.cs  (as-of balance, ANNULÉ)
  api/ClinicManagement.Infrastructure/Repositories/InvoiceRepository.cs  (GetCollectedBetweenAsync: !IsVoided)
  api/ClinicManagement.Infrastructure/Services/PdfGenerationService.cs   (receipt ANNULÉ stamp)
  api/ClinicManagement.Application/Common/Models/ReceiptPdfData.cs
  api/ClinicManagement.Infrastructure/Persistence/Configurations/PaymentConfiguration.cs
  api/ClinicManagement.API/Controllers/InvoicesController.cs             (void route, AdminOrDoctor)
+ web/components/factures/invoice-detail-modal.tsx        ← the new surface
  web/components/factures/invoices-table.tsx              (number cell → trigger, mount modal, canCancel)
  web/lib/api/invoices.ts · web/lib/api/types.ts
+ web/lib/auth/can.ts                                     (canReverseFinancials)
+ api/ClinicManagement.UnitTests/Domain/InvoicePaymentVoidTests.cs
  api/ClinicManagement.UnitTests/Domain/InvoiceEntityTests.cs             (Cancel guard change)
  api/ClinicManagement.UnitTests/Features/Invoices/InvoiceTenantIsolationTests.cs  (+ void case)
```
*(M3 is authored in Part E, which is where the table is created — Part D's columns ride along in the same migration;
see "Migrations" below for why they are one file.)*

### Part E — Installment ledger + plan void + receipts
```
+ api/ClinicManagement.Domain/Entities/InstallmentPayment.cs
+ api/ClinicManagement.Infrastructure/Persistence/Configurations/InstallmentPaymentConfiguration.cs
  api/ClinicManagement.Domain/Entities/Installment.cs         (ledger list, RecordPayment appends,
                                                               VoidPayment, denormals recomputed)
                                                               + docstring "v1 keeps only the latest…" is now false
  api/ClinicManagement.Domain/Entities/TreatmentPlan.cs       (VoidInstallmentPayment delegate)
  api/ClinicManagement.Infrastructure/Persistence/Configurations/InstallmentConfiguration.cs
                                                               (HasMany + Navigation field access + DueDate index)
  api/ClinicManagement.Infrastructure/Repositories/TreatmentPlanRepository.cs
        └─ 4 × .ThenInclude(i => i.Payments)   ← the repo's first ThenInclude
        └─ GetInstallmentCollectedBetweenAsync rewritten over ledger rows
        └─ the :88-91 comment inverted (1 of 3)
+ api/ClinicManagement.Application/Features/TreatmentPlans/Commands/VoidInstallmentPaymentCommand.cs
  api/ClinicManagement.Application/Features/TreatmentPlans/Commands/RecordInstallmentPaymentCommand.cs
  api/ClinicManagement.Application/Features/TreatmentPlans/Queries/GetInstallmentReceiptPdfQuery.cs  (+ paymentId)
  api/ClinicManagement.Application/DTOs/TreatmentPlanDto.cs   (InstallmentPaymentDto[])
  api/ClinicManagement.API/Controllers/TreatmentPlansController.cs        (void route, AdminOrDoctor)
  api/ClinicManagement.UnitTests/Api/TreatmentPlansControllerAuthorizationTests.cs  ← BUILD GATE: classify
+ api/ClinicManagement.Infrastructure/Migrations/*_AddPaymentLedgerAndVoids.cs        (M3 + backfill SQL)
  web/components/treatment-plans/plan-workspace.tsx · plan-timeline.tsx
  web/lib/api/treatment-plans.ts · web/lib/api/types.ts
+ api/ClinicManagement.UnitTests/Domain/InstallmentLedgerTests.cs
+ api/ClinicManagement.UnitTests/Features/TreatmentPlans/InstallmentPaymentVoidTests.cs
```

### Part F — Devis→facture carry-over
```
  api/ClinicManagement.Application/Features/Invoices/Commands/IssueInvoiceCommand.cs   (carry-over + transaction)
  api/ClinicManagement.Domain/Services/PlanBillingRules.cs         (comment :19-22 inverted — 2 of 3)
  api/ClinicManagement.Application/Features/Billing/Queries/GetCaisseSummaryQuery.cs   (comment :69-72 — 3 of 3,
                                                                                        + excludedPlanIds)
  api/ClinicManagement.Application/Features/Dashboard/Queries/GetDashboardStatsQuery.cs (+ excludedPlanIds)
  api/ClinicManagement.Domain/Repositories/ITreatmentPlanRepository.cs
  api/ClinicManagement.Infrastructure/Repositories/TreatmentPlanRepository.cs
  api/ClinicManagement.Application/Common/Maintenance/MoneyReconciliationService.cs    (existing-bridge report row)
  web/components/treatment-plans/plan-workspace.tsx · web/components/factures/invoices-table.tsx (disclosure)
+ api/ClinicManagement.UnitTests/Features/Invoices/BridgeCarryOverTests.cs
```

### Part G — Avoirs readable + PDF + netting
```
  api/ClinicManagement.Domain/Repositories/ICreditNoteRepository.cs   (GetByIdAsync, GetByInvoiceIdAsync, list)
  api/ClinicManagement.Infrastructure/Repositories/CreditNoteRepository.cs
+ api/ClinicManagement.Application/Features/Invoices/Queries/GetInvoiceCreditNotesQuery.cs
+ api/ClinicManagement.Application/Features/Invoices/Queries/GetCreditNotePdfQuery.cs
+ api/ClinicManagement.Application/Common/Models/AvoirPdfData.cs
  api/ClinicManagement.Application/Common/Interfaces/IPdfGenerationService.cs   (5th method)
  api/ClinicManagement.Infrastructure/Services/PdfGenerationService.cs          (GenerateAvoirPdfAsync)
  api/ClinicManagement.Application/Features/Invoices/Commands/CreateCreditNoteCommand.cs
        └─ status gate relaxed · method rejected not dropped · RefundedOn validated
  api/ClinicManagement.Application/Features/Invoices/Queries/GetInvoiceRevenueQuery.cs  (net in BOTH branches)
  api/ClinicManagement.Application/Features/Dashboard/Queries/GetDashboardStatsQuery.cs (net avoirs)
  api/ClinicManagement.Application/Features/Billing/Queries/GetPatientBillingSummaryQuery.cs (credited total)
  api/ClinicManagement.Application/DTOs/{InvoiceDto,PatientBillingSummaryDto}.cs
  api/ClinicManagement.API/Controllers/InvoicesController.cs      (2 routes)
  web/components/factures/invoice-detail-modal.tsx · invoices-table.tsx · invoice-labels.ts
  web/app/patients/[id]/page.tsx (solde card)
+ api/ClinicManagement.UnitTests/Features/Invoices/CreditNoteReadTests.cs
```

### Part H — Patient contact optional  *(blanking migration lands LAST)*
```
  api/ClinicManagement.Domain/Entities/Patient.cs        (Email? / PhoneNumber?, ctor guards removed,
                                                          + UpdateContact(Email?, PhoneNumber?))
  api/ClinicManagement.Infrastructure/Persistence/Configurations/PatientConfiguration.cs  (drop 2 × .IsRequired())
  api/ClinicManagement.Application/Features/Patients/Queries/GetPatientsQuery.cs      (:68 :87 :88)
  api/ClinicManagement.Application/Features/Patients/Queries/GetPatientQuery.cs       (:71 :72)
  api/ClinicManagement.Application/Features/Patients/Commands/CreatePatientCommand.cs (sentinel gone, :242 :243)
  api/ClinicManagement.Application/Features/Patients/Commands/UpdatePatientCommand.cs (tri-state, :211 :212)
  api/ClinicManagement.Infrastructure/Services/AIActionService.cs                     (:659-660, :739-740)
  api/ClinicManagement.Infrastructure/Services/GoogleCalendarSyncService.cs           (:669-670 → null)
  api/ClinicManagement.Application/DTOs/PatientDto.cs                                 (string?)
  api/ClinicManagement.Application/Features/Recall/Commands/SendRecallCommand.cs      (refuse, no snooze)
  api/ClinicManagement.Application/Common/Services/ReminderScheduler.cs               (gate at enqueue)
+ api/ClinicManagement.Infrastructure/Migrations/*_MakePatientContactOptional.cs      (M4 — LAST)
  web/components/edit-patient-dialog.tsx      (asterisk, blank check, send null)
  web/components/create-appointment-dialog.tsx (inline create path reconciled)
  web/app/patients/[id]/page.tsx · web/app/recalls/page.tsx · web/components/patients-table.tsx
  ~16 test fixtures constructing new Patient(...)
+ api/ClinicManagement.UnitTests/Features/Patients/PatientContactOptionalTests.cs
```

### Part I — Concurrency, backend
```
  api/ClinicManagement.Domain/Common/Entity.cs                     (+ public uint Version { get; private set; })
  api/ClinicManagement.Infrastructure/Persistence/ApplicationDbContext.cs   (xmin loop, materialized, skips owned)
+ api/ClinicManagement.Application/Common/Exceptions/ConflictException.cs
  api/ClinicManagement.Application/Common/Exceptions/ExceptionMiddleware.cs (409 case)
  api/ClinicManagement.Application/Common/ErrorMessages.cs                  (+ Conflict)
  api/ClinicManagement.Infrastructure/Persistence/UnitOfWork.cs             (translate → ConflictException)
  ~40 handlers                                    catch (Exception ex) → catch (Exception ex) when (ex is not ConflictException)
  4 numbering/delete catches                      DeletePatientCommand:53, IssueInvoiceCommand:98,
                                                  AcceptTreatmentPlanCommand:86, CreateCreditNoteCommand:126
  api/ClinicManagement.Infrastructure/Services/EInvoiceService.cs:117-125   (reload + reapply)
  17 repositories                                 detached-Update guard (AppointmentRepository:170 and
                                                  ClinicRepository:53 are the load-bearing two)
  6 commands + 6 DTOs + ~18 mapping sites         Version round-trip
+ api/ClinicManagement.API/Models/UpdateClinicRequest.cs                    (version as a form field)
+ api/ClinicManagement.Infrastructure/Migrations/*_AddConcurrencyToken.cs   (snapshot-only, empty Up())
  api/ClinicManagement.UnitTests/Common/Exceptions/ExceptionMiddlewareTests.cs  (409 fact)
  api/ClinicManagement.UnitTests/Api/ApiControllerBaseTests.cs                  (+ InlineData 409)
+ api/ClinicManagement.UnitTests/Features/Common/ConcurrencyConflictTests.cs
```

### Part J — Concurrency, frontend
```
+ web/components/ui/form-error-banner.tsx
+ web/lib/hooks/use-conflict.ts
  web/lib/api/client.ts                       (403 empty-body fallback → French)
  web/lib/errors.ts                           (403 + 409 messages)
  7 modals: payment-modal, invoice-form-modal, installment-payment-modal, edit-patient-dialog,
            edit-appointment-dialog, treatment-plan-form-modal, patient-record-modal
            └─ hydration effects re-keyed to [open, x?.id] so a refetch does not clobber typed input
  web/components/clinic-settings.tsx          (record suppressed peer change + « Recharger »)
  web/app/caisse/page.tsx · web/app/creances/receivables-table.tsx · web/app/page.tsx (dashboard)
            └─ useClinicRealtime subscriptions (currently none)
  web/app/patients/[id]/page.tsx              (<InvoicesTable onChanged={...}> — currently missing)
  web/components/treatment-plans/plan-workspace.tsx · patient-plan-card.tsx  (badge → link)
```

### Part K — Documentation
```
  CLAUDE.md · api/ClinicManagement.Domain/CLAUDE.md · api/ClinicManagement.Application/CLAUDE.md
  api/ClinicManagement.UnitTests/CLAUDE.md · packaging/README.md · CODEBASE_AUDIT_2026-07.md
```

---

## Implementation Stories

### US-1: Correct the eight data-loss and money defects, end to end

**As** a clinic using this app daily,
**I want** records that cannot be silently destroyed, money that can be corrected, and edits that cannot overwrite
each other,
**so that** what the app tells me about a patient's balance is true and a mistake is recoverable.

**Delivers:** all 77 acceptance criteria in the spec.

> **Sizing.** This story is deliberately oversized at the user's explicit request (see **R-1**). It is structured into
> eleven ordered parts; each is a vertical increment ending at a clean build gate and is a valid commit and resume
> point. Do not reorder A, and do not move H's blanking migration earlier.

---

#### Part A — Reconciliation report *(must be first)*

1. Create `MoneyReconciliationService` in **Application** (not DI-registered, mirroring `AdminPasswordRecoveryService`
   so `UnitTests` — which references only Application — can test it).
2. Implement each check from spec § slice H: the two ledger comparisons, per-plan `Σ Amount` vs `TotalPlanned`,
   24 months of « encaissé » computed **both ways**, orphan counts, sentinel counts + near-miss placeholders,
   over-credited invoices, duplicate non-cancelled bridge invoices. The bridged-invoice check is added in Part F.
3. Create `ReconcileMoneyCommand` in `API/Maintenance/`, copying `AdminPasswordResetCommand`'s shape:
   `ConfigurationBuilder` on `AppContext.BaseDirectory`, a bare `ServiceCollection` + `AddLogging()` +
   `AddInfrastructure(configuration)` — **deliberately not `AddApplication()`**, so clinic query filters stay inactive
   and the report sees every clinic.
4. Register the verb as a third `if` in `Program.cs` immediately after `provision-cert` (~line 35).
5. Exit codes `0/1/2`; write the same table to stdout and to a timestamped file beside `Backup:DefaultDestination`.
6. Tests: service logic against mocks; command test pins the verb string and the mode guard.

**Gate:** `dotnet build` 0/0. Run the verb against the dev DB and keep the output — it is the "before" baseline.

---

#### Part B — Patient deletion blocks, and archive is the escape

1. **Delete** the duplicate `HasMany(p => p.Appointments)` block at `PatientConfiguration.cs:122-125`. Author **M1**
   (`DropForeignKey` + `AddForeignKey … ReferentialAction.SetNull`).
2. Add `IsArchived` / `ArchivedAt` to `Patient` with `Archive(reason)` / `Unarchive()`, following the
   `Deactivate()`/`Activate()` idiom and `MarkRecallContacted`'s flag+timestamp+reason shape. Config uses
   `.IsRequired().HasDefaultValue(false)` so M2 emits `NOT NULL DEFAULT false`.
   **No global query filter** — this codebase has none on any status flag, and AC-7 requires an archived patient to
   stay reachable by direct URL.
3. Add `includeArchived = false` to the repository read path, following `RecurringAppointmentRepository`'s
   `activeOnly` shape. Audit every call site: the list, header search, recall and the pickers exclude; the deletion
   pre-check and « Solde patient » include.
4. Add the batched deletion pre-check counting all 14 relations — **invoices and treatment plans explicitly**, since
   they have no FK and no database constraint will ever raise for them. Expose it as
   `GET /api/patients/{id}/deletion-check` and use it in `DeletePatientCommand` for the real refusal message.
5. `ArchivePatientCommand` refuses on an outstanding balance or a future appointment — that guard lives in the
   **handler**, not the aggregate (`Patient` holds no invoices), matching the billed-plan-block precedent.
6. Frontend: `patients-table.tsx` calls the pre-check when the dialog opens and renders two states
   (« Supprimer ce patient ? » vs « Suppression impossible » with a Fermer-only footer and each blocker linking to
   `?tab=…`). Archive action + archived banner + unarchive on the detail page.

**Gate:** build + `tsc`. M1 and M2 authored with the API **stopped**.

---

#### Part C — Appointment update stops wiping the act

1. Promote `ProcedureTypeId`, `DoctorId`, `Notes`, `DoctorName` to the backing-field + `[JsonIgnore] …Specified`
   shape already used by `TreatmentPlanItemId` at `UpdateAppointmentCommand.cs:28,40-53`. Change each guard to
   `request.XSpecified && request.X != appointment.X`.
2. Make an unparseable `Status` and a `DurationMinutes <= 0` return `Result.Failure` instead of being ignored.
3. **The fix stays in the command** — `AIActionService.cs:982-988` constructs it directly and bypasses the
   controller, so a DTO-level fix would miss the AI-chat cancel path.
4. Frontend: send explicit `null` rather than `|| undefined` for notes and practitioner; add an
   « Aucun praticien » sentinel option (Radix `Select` cannot hold `value=""` — use the `"all"` idiom from the
   appointments page).
5. Give `AppointmentSyncMappingTests`' fixture a procedure type. **It must fail before the fix and pass after** — it
   currently pins the defect.

**Gate:** build + `tsc`.

---

#### Part D — Void a payment, and the invoice detail modal

1. `Payment` gains the five void columns + `SourceInstallmentPaymentId`; correct its "Immutable once created"
   docstring.
2. `Invoice.VoidPayment(paymentId, reason, actor, creditedTotal)` — **recompute** `AmountCollected` from non-voided
   payments rather than decrement; derive the walked-back status the same way `RecordPayment` derives it forward;
   refuse when the result would fall below `creditedTotal`; refuse an already-voided row.
3. Change `Cancel`'s guard from `_payments.Count > 0` to any **non-voided** payment, and add the
   `EInvoiceStatus is Valid or Submitted or Validating` refusal.
4. `RecordPayment` rounds through `InvoiceCalculator` and rejects sub-millime; `RecordPaymentCommand` validates
   `PaidOn`.
5. `VoidPaymentCommand` (`AdminOrDoctor`) loads the credited total via `ICreditNoteRepository.GetTotalForInvoiceAsync`
   and passes it in — Domain has no repository access.
6. `GetCollectedBetweenAsync` gains `&& !p.IsVoided`. Receipt PDF: as-of balance + « ANNULÉ » stamp.
7. `InvoiceDto` gains `canCancel` / `canCreateAvoir` computed **server-side** — the frontend's re-derivation from
   `status` + `amountCollected` is exactly what produces an enabled button the API refuses.
8. Build `invoice-detail-modal.tsx`: lines, payments (persistent « Reçu » + « Annuler »), avoirs section (populated
   in Part G). Fetched via `invoicesApi.get(id)` — which exists and has zero callers today. Real loading and error
   states with a retry. The void confirm is an **in-place panel**, not a nested dialog.
9. The invoice **number cell** becomes the trigger. Add `canReverseFinancials`; render the action **disabled with a
   `title`**, never hidden.

**Gate:** build + `tsc`.

---

#### Part E — Installment ledger, plan void, honest receipts

1. New `InstallmentPayment` entity + configuration, modelled on `Payment`/`PaymentConfiguration`.
   **Relationship declared only in `InstallmentConfiguration`** (`HasMany` + `Navigation(...).UsePropertyAccessMode`)
   — configuring from both sides is the exact bug Part B fixes. **No `DbSet`.**
   `Installment` becomes the first entity in this codebase that is both a child and a parent.
2. `Installment.RecordPayment` appends a ledger row and recomputes the denormals; `VoidPayment` likewise. Keep
   `AmountPaid`/`LastMethod`/`LastPaidOn` stored — 13 read sites depend on them — and note `AmountPaid` **stops being
   monotonic**, which `Revise` and `ReviseInstallments` key off.
3. Rewrite `GetInstallmentCollectedBetweenAsync` over ledger rows by their own `PaidOn`, excluding voided, still
   rooted at the clinic-filtered `_context.TreatmentPlans` (this *is* the tenant scoping AC-22 asks for). Invert the
   `:88-91` comment.
4. Add the four `.ThenInclude(i => i.Payments)` — the repository's first.
5. `VoidInstallmentPaymentCommand` (`AdminOrDoctor`). **Classify it in
   `TreatmentPlansControllerAuthorizationTests` — the build fails otherwise.** The plan's status is *not* walked back.
6. Receipt query takes a `paymentId` and prints that payment; route, API module and both callers updated.
7. Author **M3**: the six `Payments` columns, the `InstallmentPayments` table, four indexes, and the backfill SQL
   guarded by `WHERE NOT EXISTS`. Handle `AmountPaid > 0 && LastPaidOn IS NULL` with a documented fallback **and a
   count in the report** — `PaidOn` is `NOT NULL`, so a bare insert aborts the whole migration.

**Gate:** build + `tsc`. Re-run Part A's report — **every monthly figure must be unchanged** (AC-24).

---

#### Part F — The devis→facture carry-over

1. In `IssueInvoiceCommand`, after `Issue()` freezes the totals, carry the plan's collected installment money onto
   the invoice as payments with their **original** `PaidOn`, each stamped `SourceInstallmentPaymentId`.
2. Cap and refuse: if `Σ carried > TotalTtc`, fail with a message naming the un-carried amount. Never let
   `RecordPayment`'s over-payment guard throw from inside `Issue()` — that would strand a numbered draft.
3. Wrap the two-aggregate write in `IUnitOfWork.BeginTransactionAsync`/`CommitTransactionAsync` — **their first use
   in this codebase**.
4. Add `excludedPlanIds` to `GetInstallmentCollectedBetweenAsync` (mirroring the outstanding query's **required**
   parameter) and pass `PlanBillingRules.BilledPlanIds(...)` from both cash callers.
5. **Invert all three load-bearing comments together** — `PlanBillingRules.cs:19-22`, `GetCaisseSummaryQuery.cs:69-72`,
   `TreatmentPlanRepository.cs:88-91`. Their shared premise is what this part changes.
6. Add the existing-bridge report row to Part A's service. **Repair nothing.**
7. Frontend disclosure on the draft and both toasts; `handleIssueAndPay` must prefill from the **post-carry-over**
   response or the dentist double-collects.

**Gate:** build + `tsc`.

---

#### Part G — Avoirs become readable

1. Add the three read methods to `ICreditNoteRepository`; add the list and PDF queries and routes.
2. `AvoirPdfData` + `GenerateAvoirPdfAsync` reusing the invoice renderer's header/footer/`FormatDt` helpers. It must
   carry data the entity lacks — the corrected invoice's **number and date**, the **patient**, and the **VAT split**
   (`Amount` is a single scalar today) — all resolved in the handler via the soft `InvoiceId`.
3. Relax `CreateCreditNoteCommand`'s status gate to `Status != Draft && != Cancelled && AmountCollected > 0`;
   reject an unparseable method; validate `RefundedOn`.
4. Net avoirs in **both** branches of `GetInvoiceRevenueQuery` — the no-period branch is what `/factures` loads by
   default — and in the dashboard. Surface the credited total on the invoice row and « Solde patient ».
5. The El Fatoora warning on the avoir screen when the invoice is TTN-registered.

**Gate:** build + `tsc`.

---

#### Part H — Patient contact becomes optional *(blanking last)*

1. **Order within the part is not negotiable.** Make the columns nullable and fix all ten `.Value` dereferences
   **first**; the blanking `UPDATE` is the last statement of the last migration.
2. `Email?` / `PhoneNumber?` on the entity (one character each), drop the two ctor guards, drop the two
   `.IsRequired()` lines in the config — `EmergencyContactPhone` is the working precedent in the same file.
3. Because `UpdatePersonalInfo` takes both positionally among six params, add a dedicated
   `UpdateContact(Email?, PhoneNumber?)` in the `UpdateEmergencyContact` shape so tri-state can be expressed.
4. Fix the ten NRE sites — `GetPatientsQuery.cs:68` first, since one phone-less patient 500s the whole list and the
   header search.
5. Remove both sentinel sources; `UpdatePatientCommand` gets the tri-state so a field can actually be cleared.
6. `SendRecallCommand` refuses and does **not** snooze; gate reminders at enqueue.
7. Frontend: asterisk and blank check off, send `null`, reconcile the inline create path, add the three
   consequence messages, disable « Envoyer » on phone-less recall rows.
8. Author **M4** last: `DROP NOT NULL` ×2, then blank the four literals. Its generated `Down()` cannot run —
   replace the body with a comment saying rollback is restore-from-backup, rather than shipping a `Down()` that
   throws at runtime.
9. Update the ~16 test fixtures constructing `new Patient(...)`.

**Gate:** build + `tsc`. Re-run the report — sentinel counts must be zero, near-miss counts reported.

---

#### Part I — Conflict detection, backend

1. **De-risk first (2 minutes):** run `dotnet ef migrations add ProbeXmin`, confirm the generated `Up()` is empty,
   delete it. The "no migration" claim could not be proven statically. **The API must be stopped** — a running
   process makes `migrations add` emit an empty migration, and here an empty migration is exactly what you expect,
   so the failure mode is invisible.
2. Add `public uint Version { get; private set; }` to `Entity<TId>`; map it in the `OnModelCreating` loop to the
   `xmin` system column (`.HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate()
   .IsConcurrencyToken()`).
3. The loop **must** `.ToList()` before iterating (`modelBuilder.Entity(clrType)` adds entity types to the collection
   being enumerated), skip `entityType.IsOwned()` and `HasSharedClrType` (`PhoneNumber` is owned twice by `Patient`),
   and skip anything not deriving from `Entity<>` (`NotificationRead` is the only one).
4. `ConflictException` + the 409 `case` in `ExceptionMiddleware` + the `ErrorMessages.Conflict` constant.
   `ApiControllerBase` needs no change.
5. Translate in `UnitOfWork.SaveChangesAsync`; add `when (ex is not ConflictException)` to the ~40 handlers that can
   actually produce a conflict. The four `catch (DbUpdateException)` blocks then stop being special.
6. `EInvoiceService.cs:117-125` reloads and reapplies — otherwise a conflict there loses a TTN validation and the
   outbox submits the same invoice twice.
7. Guard the 17 unguarded detached `Update()` calls. **`ClinicRepository.cs:53` matters as much as
   `AppointmentRepository.cs:170`** — the spec named only the latter, but Clinic is one of the six round-tripped
   entities, so an unguarded detached update is a guaranteed 409.
8. Round-trip `Version` on the six commands and DTOs plus the ~18 mapping sites. **Clinic is the outlier**:
   `[FromForm]`, no route id, and it returns the raw `Result` envelope — the version travels as a form field.
9. Commit the snapshot-only concurrency migration so the next unrelated `migrations add` does not silently absorb
   ~20 `AddColumn<uint>("xmin")` operations.

**Gate:** build 0/0. Add the 409 fact to `ExceptionMiddlewareTests` and the `[InlineData]` to `ApiControllerBaseTests`.

---

#### Part J — Conflict detection, frontend

1. Extract `<FormErrorBanner>` (copying `payment-modal.tsx`'s markup) and `useConflict`.
2. Apply to **all seven** modals. The real work is re-keying each hydration effect from the object prop to
   `[open, x?.id]` — today a refetch underneath resets every field. `edit-patient-dialog.tsx` is the expensive one:
   ~25 `useState` calls, a `[patient, open]` effect, and no form-level banner at all.
3. Branch on `err.status === 409` at the call sites, following the existing `status === 0` precedent. Delete on
   conflict closes the dialog and reloads the list. Second consecutive conflict escalates the wording.
4. Fix `client.ts`'s empty-body 403 → French, so a role denial stops rendering « HTTP 403: Forbidden ».
5. `clinic-settings.tsx` records the suppressed peer change and offers « Recharger ».
6. Add the missing realtime subscriptions (caisse, créances, dashboard) and the missing
   `<InvoicesTable onChanged>` on the patient page. Make the two plan badges link to the invoice.

**Gate:** `tsc` 0 + `npm run build` clean.

---

#### Part K — Documentation

Update the six documents listed in the spec, and tick off § 1 of the audit noting the adjacent items closed with it.

---

## Testing Strategy

Conventions: xUnit + Moq, no database, no FluentAssertions, `Pascal_Snake_Case` sentence names, class-level
`<summary>` + per-test `// [AC-n]`, deterministic GUIDs and fixed UTC dates.

**Per part**, alongside the code — not at the end.

**Two existing tests currently pin defects and must be made to fail first:**
- `AppointmentSyncMappingTests.cs:158` passes only because its fixture has no procedure type (Part C).
- `MoneyReadConsistencyTests` mocks the entire collected-cash side to `0m` and asserts `TotalOutstanding` only, so
  it would pass green through every defect in Parts D, E, F and G. Extend it with non-zero payments, an avoir, a
  voided payment and a bridged plan, and add `GetCaisseSummaryQuery` + `GetInvoiceRevenueQuery` as the fourth and
  fifth reads. **The caisse has no test anywhere today.** Its `Wire()` helper hand-reimplements repository SQL in
  LINQ — every new filter must be mirrored there in lock-step or the suite passes while production is wrong.

**Guard tests that will trip:** `TreatmentPlansControllerAuthorizationTests` (Part E — build fails until the void
action is classified), `PlanBillingRulesTests`, the seven `*TenantIsolationTests` (a case per new clinic-scoped verb,
asserting `IsFailure` **and** `SaveChangesAsync … Times.Never`), `ExceptionMiddlewareTests`, `ApiControllerBaseTests`.
Also fix `IssueInvoiceCommandHandlerTests`, which asserts against `DateTime.UtcNow.Year` — it recomputes the same
expression the handler uses, so it can never detect a wrong-year defect and flakes across New Year.

**Execution:** `dotnet test` fails at assembly load with `0x800711C7` (Smart App Control, environmental). Use
`dotnet build -p:OutDir=<scratch>/utbuild/` then `dotnet vstest`. There is no CI, so nothing else will ever run these.

**Frontend:** no test runner exists. Gate is `npx tsc --noEmit` + `npm run build`, both clean.

**Manual verification** at the end of Parts E, F and H: run `reconcile-money` and diff against Part A's baseline.

---

## Risk Register

| ID | Risk | L | I | Part | Mitigation |
|----|------|---|---|------|------------|
| **R-1** | **Single story is ~210 files across 11 parts — will not fit one session, and a mid-part stop leaves the money model half-migrated** | **H** | **H** | all | User's explicit choice, honored. Parts are ordered vertical increments with a build gate each; **commit at every part boundary** and resume there. Never stop mid-part in D, E, F or H. |
| R-2 | The "xmin needs no migration" claim is not statically provable; if wrong, Part I needs a real 20-table migration | M | H | I | Probe migration as the literal first step of Part I; confirm empty `Up()`, delete. 2 minutes to de-risk the slice's headline claim. |
| R-3 | `dotnet ef migrations add` silently emits an **empty** migration when the API is running — and in Part I an empty migration is the expected output, so the failure is invisible | M | H | B,E,H,I | Stop the API before every `migrations add`. Read each generated file before committing. Never pass `--no-build`. |
| R-4 | The M3 backfill runs against a live database in Local mode (migrations dispatch *after* Kestrel binds) and a throw calls `StopApplication()` | M | H | E | `WHERE NOT EXISTS` guard makes it idempotent and retry-safe. Documented backup pre-step. Blanking is last. |
| R-5 | `MoneyReadConsistencyTests` cannot see any Part D/E/F/G defect — it mocks collected cash to zero | **H** | **H** | D–G | Extend the fixture to the collected side **before** the money changes land, and mirror every new repository filter in `Wire()`. |
| R-6 | Money backfill silently moves a closed month's takings | M | H | E | AC-24 requires 24 months identical before/after; Part A's report is the instrument and runs at Parts A, E, F and H. |
| R-7 | Void × avoir cap implemented wrong → the same dinar leaves the caisse twice | M | H | D | The cap is a domain guard with the credited total passed in by the handler; a dedicated test per direction (void-then-avoir, avoir-then-void). |
| R-8 | Carry-over at `Issue()` is the first two-aggregate write and the first transaction use in the codebase; a partial commit loses money | M | H | F | Explicit `BeginTransactionAsync`/`Commit`; a test asserting neither side persists on a mid-operation failure. |
| R-9 | Making `Email`/`PhoneNumber` nullable changes the `Patient` ctor signature and fans out to ~16 test fixtures plus `GoogleCalendarSyncService` | M | M | H | Mechanical; compiler-driven. Do the entity + config + all `.Value` sites in one pass so the build tells you when you're done. |
| R-10 | Blanking deploys before the null-safe code → one blanked row 500s the whole patient list and header search | L | H | H | Ordering is stated in the part and in the spec; blanking is the last statement of the last migration. |
| R-11 | `ClinicRepository.cs:53`'s unguarded detached `Update()` + Clinic being a round-tripped entity = a guaranteed 409 on every clinic-settings save | M | H | I | Found during exploration (the spec named only `AppointmentRepository`). Audit all 17 unguarded repositories, not just the two named. |
| R-12 | Adding `Version` to `Entity<TId>` touches every entity and every hand-written DTO mapping site; Patient and Appointment have no shared mapper | M | M | I | Chosen precisely because it makes each site a one-line addition. Compiler will not catch a *missed* mapping — grep every `new XDto {` for the six types. |
| R-13 | Re-keying seven modals' hydration effects breaks form prefill in a way `tsc` cannot catch | M | M | J | Manual click-through of each modal after the change; the effect split is small and identical in shape across all seven. |
| R-14 | `dotnet test` is blocked on this machine, so regressions surface only at `dotnet build` | H | M | all | Documented `vstest` workaround; run it at each part gate rather than only at the end. |
| R-15 | Scope creep — "do not defer anything" meeting a defect discovered mid-implementation | M | M | all | The spec's **Out of Scope** section is the boundary. Anything new goes to `follow-up/`, not into this story. |
| R-16 | El Fatoora avoir transmission stays unbuilt while avoirs become printable | L | M | G | Deliberate (spec Out of Scope) and disclosed in-UI per AC-45, so a clinic knows it has a manual step. |

---

## Breaking Changes

- **`PUT /api/appointments/{id}`** — omitting `procedureTypeId`, `doctorId`, `notes` or `doctorName` stops meaning
  "clear". Wire-compatible for callers that send every field; the two in-app callers are updated in the same part.
- **`GET /api/treatment-plans/{id}/installments/{installmentId}/receipt-pdf`** → moves under `payments/{paymentId}`.
  Two frontend callers updated.
- **`PatientDto.email` / `phoneNumber` become nullable.** The frontend is already tolerant (`|| "Non renseigné"`
  everywhere), but the TS types change.
- **The six mutating commands require `version`.** A caller that omits it is rejected — all callers are in-repo.
- **`Invoice.Cancel` refuses a TTN-registered invoice** that it would previously have cancelled.
- **`SendRecallCommand` now fails** for a phone-less patient instead of reporting success.
- Archived patients disappear from lists, search, recall and pickers.
- `Payment` and `Installment` docstrings that describe them as immutable / history-free become false and are corrected.

## Migrations

| # | Name | Part | Contents |
|---|---|---|---|
| M1 | `FixPatientAppointmentDeleteBehavior` | B | Drop + re-add `FK_Appointments_Patients_PatientId` with `SetNull` |
| M2 | `AddPatientArchive` | B | `IsArchived` (NOT NULL DEFAULT false), `ArchivedAt`, index `(ClinicId, IsArchived)` |
| M3 | `AddPaymentLedgerAndVoids` | E | 6 `Payments` columns; `InstallmentPayments` table; 4 indexes; backfill SQL |
| M4 | `MakePatientContactOptional` | H | `DROP NOT NULL` ×2, **then** blank the four sentinel literals |
| M5 | `AddConcurrencyToken` | I | Snapshot-only, empty `Up()` — commit it so the next migration doesn't absorb the diff |

Part D's `Payments` columns ride in **M3** rather than their own migration so the table-creating migration lands
before the concurrency one — this sidesteps whether the provider filters `xmin` out of a `CreateTable` column list.

**Rollback is restore-from-backup, not `migrations remove`.** M4's generated `Down()` (`SET NOT NULL`) cannot run
because the blanking left NULLs, and dropping the void columns would leave voided payments live again while
`AmountCollected` stays decremented. M4's `Down()` body is replaced with a comment stating this. A backup is a
documented pre-step for Local installs, where `PgDumpBackupService` already exists.
