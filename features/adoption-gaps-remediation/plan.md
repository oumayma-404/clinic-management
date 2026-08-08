# Implementation Plan: Adoption Gaps Remediation

**Status:** APPROVED
**Challenged:** Yes
**Created:** 2026-08-08
**Spec:** [features/adoption-gaps-remediation/spec.md](./spec.md)

## Overview

The spec's four groups are planned as **one user story** at the owner's explicit direction, structured into
**four ordered, dependency-respecting internal parts**. Each part is a vertical increment (Domain → EF →
migration → Application → API → web → docs → tests) that lands and commits on its own, so the part boundary is
the natural split point if a session runs out. The oversize is recorded as **R-1** rather than re-litigated.

**The parts run C → A → B → D, not the spec's A → D.** Exploration found a hard overlap the spec did not
account for: `Invoice.cs` carries 102 TTN references, `CreateInvoiceFromDentalRecordCommand` 10 and
`IssueInvoiceCommand` 16 — the exact files Group A rewrites. Deleting El Fatoora first means Group A's typed
result is written once, into a command with no e-invoice branches, and AC-14's `CanCancel` rewrite lands before
Group A touches cancellation wording. Groups B and D are disjoint from both and keep their spec order.

Five architectural decisions were taken during the interview:

1. **`CreateInvoiceFromDentalRecordCommand` is renamed `BillDentalRecordCommand`** and becomes the single
   authority on « make this fiche's money right » — create, top up, or refuse. The name stops lying once it no
   longer only creates, and the over-payment / payment-date / cheque validation exists in exactly one place.
2. **It returns `Result<DentalRecordBillingResult>`** — a record carrying `Outcome` (enum, 1:1 with
   `DentalRecordBillingOutcome`), `Invoice`, `AmountCollected` and `Message`. A failure `Code` alone cannot
   express AC-1, because a top-up is a *success* that must read differently from a first billing.
   `InvoicesController` unwraps `.Invoice`, so `POST /api/invoices/from-dental-record/{id}` keeps its body and
   the manual `bill-dental-record-dialog` needs no contract change. Refusals additionally set `Result.Code`,
   reusing the existing `patient_duplicate` convention.
3. **La caisse's duplicated period arithmetic is extracted** into one Application-side `CaissePeriod` resolver
   that both `GetCaisseSummaryQuery` and `GetCaisseLedgerQuery` call, at the same time as the day-key form is
   added. The two handlers hold byte-identical bound logic today, duplicated on purpose; adding a second rule to
   both copies is the `fixes-dont-propagate` shape.
4. **`usePagedList` gains `filters?: readonly unknown[]`** — the same values callers already close over in their
   `fetchPage` `useCallback` deps, so there is one list to keep in sync rather than two.
   ⚠️ **The reset effect must key on a serialized signature, never on the array's identity.** Callers pass an
   inline literal (`filters={[status, from, to]}`), which is a **new reference every render** — a `useCallback`
   dep list is spread by React, an array *prop* is not — so an identity-keyed effect fires on every render and
   `setPage(1)` would undo the user's own page click, breaking paging on all three consumers rather than fixing
   their filters. The hook therefore computes `const filterKey = JSON.stringify(filters ?? [])` internally and
   keys the reset on that string, so an unchanged filter set is a no-op whatever the array's identity.
   Consequence to state in the hook's doc comment: **filter values must be JSON-serialisable primitives** —
   every filter here is (string, ISO date string, boolean, number); a `Date` or an object must be passed in its
   primitive form.
5. **`WaitingListEntry.Cancel()` is made reachable rather than deleted.** A patient who left without being seen
   is a fact; physical deletion erases it and the audit ledger can only record that a row vanished.

**Four** migrations, one per part — every part touches schema — so each part stays independently committable.

### What exploration corrected in the spec

- **All seven hand-rolled paged lists already reset to page 1 on every filter they expose** (verified
  individually). AC-22's real gap is the *three* `usePagedList` consumers, whose non-search filters refetch the
  same page number. The verification half of AC-22 is confirmation, not repair — but it is still done, and any
  regression found is fixed in place.
- **`Payment`/`InstallmentPayment` name the field `VoidedAt`, not `VoidedOn`** — the new banked columns follow
  the real neighbours (`ChequeBankedOn` stays a date per spec; the actor field is `ChequeBankedByUserId`).
- **The DTO is `LabWorkOrderDto`, the query is `GetChequesDueQuery`**, and there are **six** `CLAUDE.md` files
  with TTN hits (the spec says five — `web/lib/CLAUDE.md` is the omitted one).
- **No migration in this repo has a throwing `Down()`.** Group C's is a new pattern, not one to copy.
- **`ForbiddenAccessException` has other legitimate users** (`ExceptionMiddleware`'s 403 mapping). AC-26 deletes
  `EnsureClinicAccess`/`BelongsToClinic`, not the exception type.

---

## Files to Modify/Create

### Part 1 — Group C: remove El Fatoora / TTN

#### Files to Delete
| File | Note |
|------|------|
| `api/ClinicManagement.Domain/Enums/EInvoiceStatus.cs` | enum |
| `api/ClinicManagement.Application/Common/Interfaces/{IEInvoiceService,IEInvoiceSigner,ITeifXmlGenerator,ITtnClient,ITtnIdentityProvider,ITtnSecretProtector}.cs` | six seams |
| `api/ClinicManagement.Application/Common/Models/EInvoiceModels.cs` | `TeifInvoiceInput`, `SignedEInvoiceResult`, `TtnSubmissionResult/Outcome`, `EInvoiceArtifactResult` |
| `api/ClinicManagement.Application/Common/Exceptions/TtnIdentityUnavailableException.cs` | |
| `api/ClinicManagement.Application/Features/Invoices/Commands/SubmitInvoiceToElFatooraCommand.cs` | |
| `api/ClinicManagement.Application/Features/Invoices/Queries/GetEInvoiceArtifactQuery.cs` | |
| `api/ClinicManagement.Infrastructure/Services/{EInvoiceService,TeifXmlGenerator,XadesEInvoiceSigner,HttpTtnClient,SandboxTtnClient,TtnIdentityProvider,TtnSecretProtector,TtnConfig}.cs` | eight services |
| `api/ClinicManagement.API/BackgroundJobs/EInvoiceOutboxJob.cs` | |
| `api/ClinicManagement.UnitTests/Infrastructure/Services/{SandboxTtnClientTests,TtnIdentityProviderTests,XadesEInvoiceSignerTests,TeifXmlGeneratorTests,CancelledInvoiceIsNotDispatchedTests}.cs` | 5 of 7 |
| `api/ClinicManagement.UnitTests/Domain/{InvoiceEInvoiceTests,ClinicEInvoiceSettingsTests}.cs` | 2 of 7 |

#### Files to Create
| File | Purpose |
|------|---------|
| `api/ClinicManagement.Infrastructure/Migrations/<ts>_RemoveEInvoicing.cs` | drops 14 columns + the `(EInvoiceStatus, EInvoiceNextAttemptAt)` index; `Down()` throws |

#### Files to Modify (Part 1)
| File | Changes |
|------|---------|
| `api/ClinicManagement.Domain/Entities/Invoice.cs` | drop 8 e-invoice properties + the 8 `MarkEInvoice*`/`CopyEInvoiceStateFrom` members; `CanCancel` loses 3 of 5 terms; `Cancel()` loses the « déclarée à El Fatoora » throw and the dequeue block |
| `api/ClinicManagement.Domain/Entities/Clinic.cs` | drop `TtnEInvoicingEnabled`, `TtnEnvironment`, the four `Ttn*` identity columns, `SetTtnIdentity` and the e-invoicing setter |
| `api/ClinicManagement.Domain/Repositories/IInvoiceRepository.cs` | drop `EInvoiceOutboxDepth` record, `GetDueForElFatooraDispatchAsync`, `GetEInvoiceOutboxDepthAsync` |
| `api/ClinicManagement.Infrastructure/Repositories/InvoiceRepository.cs` | drop both outbox implementations |
| `api/ClinicManagement.Infrastructure/Persistence/Configurations/{Invoice,Clinic}Configuration.cs` | drop the column mappings + the e-invoice index |
| `api/ClinicManagement.Infrastructure/Persistence/AuditSaveChangesInterceptor.cs:78` | drop `"EInvoiceStatus"` from the significant-fields list |
| `api/ClinicManagement.Infrastructure/**Migrations**/ApplicationDbContextModelSnapshot.cs` | regenerated by the migration — must lose all 15 hits. ⚠️ It lives **inside `Migrations/`**, so AC-13's broad grep excludes it: it needs its own grep (see Part 1 validation) |
| `api/ClinicManagement.Infrastructure/Persistence/SchemaVerificationReader.cs` | drop the `partialTtnIdentity` block |
| `api/ClinicManagement.Application/Common/Maintenance/SchemaVerificationService.cs` | drop the `ttn-identity-is-complete` registration |
| `api/ClinicManagement.Application/Common/Interfaces/ISchemaVerificationReader.cs` | drop `ClinicsWithPartialTtnIdentity` from `DataMigrationCounts` |
| `api/ClinicManagement.Application/DTOs/{InvoiceDto,ClinicDto,CreditNoteDto,OutboxDepthDto}.cs` | drop e-invoice/TTN fields; delete `EInvoiceOutboxDepthDto` (`EInvoices` is `required` ⇒ compile-checked) |
| `api/ClinicManagement.Application/Common/Models/{AvoirPdfData,InvoicePdfData}.cs` | drop `CorrectedInvoiceIsTtnRegistered` and the QR/cachet fields |
| `api/ClinicManagement.Application/Features/Invoices/*` | `InvoiceMappingExtensions`, `CreateInvoiceCommand`, `CreateInvoiceFromTreatmentPlanCommand`, `DeleteInvoiceCommand`, `IssueInvoiceCommand`, `UpdateInvoiceCommand`, `GetInvoicePdfQuery`, `GetCreditNotePdfQuery` — drop e-invoice branches |
| `api/ClinicManagement.Application/Features/Outbox/Queries/GetOutboxDepthQuery.cs` | two queues, not three |
| `api/ClinicManagement.Application/Features/Clinics/Commands/UpdateClinicCommand.cs` | drop the 17 TTN writes |
| `api/ClinicManagement.API/Controllers/{InvoicesController,ClinicsController,OutboxController}.cs` | drop the submit + artifact routes and the TTN settings surface |
| `api/ClinicManagement.API/Models/UpdateClinicRequest.cs` | drop 3 TTN fields |
| `api/ClinicManagement.API/Program.cs` | delete the `dispatch-einvoices` `AddOrUpdate` (~L688-694); add `RecurringJob.RemoveIfExists("dispatch-einvoices")` beside the `sync-google-calendar` precedent (~L752) |
| `api/ClinicManagement.API/BackgroundJobs/{StockExpiryJob,BackupJob,DocumentEmailJob,NotificationJob}.cs` | repair the four `cref`s to the deleted `EInvoiceOutboxJob` (0-warning gate) |
| `api/ClinicManagement.Infrastructure/Extensions.cs:347-354` | drop 7 DI registrations; **keep** `IQrCodeGenerator` (`TrustController` uses it) |
| `api/ClinicManagement.Infrastructure/Deployment/DeploymentProfile.cs` | drop `SharesInstallWideTtnIdentity` (15 → 14 capabilities) |
| `api/ClinicManagement.Infrastructure/Services/PdfGenerationService.cs` | drop the invoice cachet block and the avoir TTN warning (L712-718) |
| `web/components/clinic-settings.tsx`, `factures/{invoices-table,invoice-labels.ts,invoice-detail-modal,invoice-form-modal}.tsx`, `document-editor-content.tsx`, `documents/honoraires-launcher.tsx` | drop the El Fatoora settings section, submit action, status badges, artifact downloads and the avoir banner (L453-458) |
| `web/lib/api/{clinics,invoices,types}.ts` | drop `EInvoiceStatus` and the TTN calls/types |
| 26 further unit-test files | update the references listed in AC-16 |
| 6 × `CLAUDE.md`, `packaging/README.md`, `packaging/server/clinic-server.iss`, `deploy/README.md` | drop the TTN sections |
| `follow-up/ttn-per-clinic-identity-write-path.md` (+ its index entry) | **delete** — it asks for a write path onto columns this part drops |

### Part 2 — Group A: money integrity

#### Files to Create
| File | Purpose |
|------|---------|
| `api/ClinicManagement.Application/Features/Invoices/Commands/BillDentalRecordCommand.cs` | renamed from `CreateInvoiceFromDentalRecordCommand`; owns create / top-up / refuse |
| `api/ClinicManagement.Application/Common/Models/DentalRecordBillingResult.cs` | `Outcome` + `Invoice` + `AmountCollected` + `Message` |
| `api/ClinicManagement.Application/Features/Billing/CaissePeriod.cs` | the one authority on caisse period bounds (day keys and instants) |
| `api/ClinicManagement.Infrastructure/Migrations/<ts>_AddDentalRecordPaymentMethod.cs` | 4 nullable columns on `DentalRecords` |
| `api/ClinicManagement.UnitTests/Features/Invoices/BillDentalRecordOutcomeTests.cs` | AC-1, AC-2, AC-3, AC-3b, A-1, A-2 |
| `api/ClinicManagement.UnitTests/Features/Billing/CaissePeriodTests.cs` | AC-6, AC-7 |
| `web/components/treatment-plans/void-installment-payment.tsx` | the plan-workspace void affordance (AC-5) |

#### Files to Modify (Part 2)
| File | Changes |
|------|---------|
| `api/ClinicManagement.Domain/Entities/DentalRecord.cs` | add `PaymentMethod`, `ChequeNumber`, `ChequeBankName`, `ChequeDueDate` through `ChequeDetails.For` (no new guard) |
| `api/ClinicManagement.Infrastructure/Persistence/Configurations/DentalRecordConfiguration.cs` | map the four columns |
| `api/ClinicManagement.Application/Features/Patients/DentalRecordAutoBilling.cs` | delete the `Contains("déjà facturée")` match (L86); switch on the typed outcome; carry the fiche's method + cheque instead of hard-coded `Cash` (L64) |
| `api/ClinicManagement.Application/DTOs/DentalRecordDto.cs` | extend `DentalRecordBillingOutcome` with the new outcomes; add the four payment fields |
| `api/ClinicManagement.Application/Features/Patients/Commands/{Create,Update}DentalRecordCommand.cs` | accept + persist the four fields; **the authoritative AC-2 / AC-3b refusal**, pre-commit — see Part 2 step 3a |
| `api/ClinicManagement.API/Controllers/InvoicesController.cs` | unwrap `.Invoice` so the endpoint body is unchanged |
| `api/ClinicManagement.Application/Features/Billing/Queries/{GetCaisseSummaryQuery,GetCaisseLedgerQuery}.cs` | add `FromDay`/`ToDay`; delegate all bound arithmetic to `CaissePeriod` |
| `api/ClinicManagement.API/Controllers/BillingController.cs` | bind the day-key params on summary, ledger and export |
| `api/ClinicManagement.Infrastructure/Repositories/ExpenseRepository.cs:41,64` | `ExpenseDate < to` → `<= to` |
| `api/ClinicManagement.Application/Features/Invoices/Commands/IssueInvoiceCommand.cs:181-187`, `api/ClinicManagement.Infrastructure/Repositories/TreatmentPlanRepository.cs:217-218`, `api/ClinicManagement.Domain/Services/PlanBillingRules.cs:31-33` | correct the « cancelling the bridge hands the money back » comment in **all three** places; the refusal names the avoir |
| `web/app/caisse/page.tsx` | send bare `YYYY-MM-DD` day keys; delete `rangeBounds` (L119-124) |
| `web/lib/dashboard-links.ts` | move the `/caisse` date-range links to day keys |
| `web/components/patient-record-modal.tsx:505-527` | handle every outcome; `AlreadyBilled` becomes informational, not plain green |
| `web/components/patients/…` fiche form | the four payment fields, reusing `factures/cheque-fields.tsx` + `chequePaymentFields()` |
| `web/lib/api/{invoices,expenses,types}.ts` | day-key params; the new outcome values and fiche fields |
| `api/ClinicManagement.UnitTests/Features/Billing/MoneyReadConsistencyTests.cs` | extend for the inclusive expense bound (AC-7) |

### Part 3 — Group B: cheque life-cycle

#### Files to Create
| File | Purpose |
|------|---------|
| `api/ClinicManagement.Application/Features/Invoices/Commands/SetPaymentBankedCommand.cs` | mirrors `VoidPaymentCommand` |
| `api/ClinicManagement.Application/Features/TreatmentPlans/Commands/SetInstallmentPaymentBankedCommand.cs` | mirrors `VoidInstallmentPaymentCommand` |
| `api/ClinicManagement.Infrastructure/Migrations/<ts>_AddChequeBankedStamp.cs` | 2 columns × 2 tables |
| `api/ClinicManagement.UnitTests/Features/Billing/ChequeBankedStampTests.cs` | AC-8…AC-12, B-1, B-2 |

#### Files to Modify (Part 3)
| File | Changes |
|------|---------|
| `api/ClinicManagement.Domain/Entities/{Payment,InstallmentPayment}.cs` | `ChequeBankedOn` + `ChequeBankedByUserId`; `internal void SetBanked(...)` refusing a non-cheque method |
| `api/ClinicManagement.Domain/Entities/{Invoice,TreatmentPlan}.cs` | aggregate-root entry points, on `VoidPayment`'s pattern |
| `api/ClinicManagement.Infrastructure/Persistence/Configurations/{Payment,InstallmentPayment}Configuration.cs` | map both columns |
| `api/ClinicManagement.Application/DTOs/ChequesDueDto.cs` | `ChequeDto` gains `InstallmentId` and the owning aggregate id; a `Banked` flag + stamp |
| `api/ClinicManagement.Domain/Repositories/ITreatmentPlanRepository.cs` | `CaisseInstallmentPaymentRow` gains `InstallmentId` |
| `api/ClinicManagement.Application/Features/Billing/Queries/GetChequesDueQuery.cs` | default to outstanding; `banked` filter; buckets count outstanding only (AC-11) |
| `api/ClinicManagement.API/Controllers/{Invoices,TreatmentPlans}Controller.cs` | the two `/banked` routes, `AdminOrDoctor` |
| `api/ClinicManagement.Application/Common/Interfaces/ISchemaVerificationReader.cs`, `Common/Maintenance/SchemaVerificationService.cs`, `Infrastructure/Persistence/SchemaVerificationReader.cs`, `UnitTests/Common/Maintenance/SchemaVerificationServiceTests.cs` | all **four** parts of a new `cheque-banked-only-on-cheques` check, on the `cheque-details-only-on-cheques` pattern (field · guarded `ScalarOrNullAsync` · `Add(...)` · clean + not-applicable test cases) |
| `web/components/caisse/cheques-table.tsx`, `web/app/cheques/page.tsx` | « Encaissés » filter, the mark/un-mark action, card form < 640 px, confirmation as a sheet in `dvh` |
| `web/lib/api/{invoices,treatment-plans,types}.ts` | the two calls + the DTO fields |

### Part 4 — Group D: remaining defects

#### Files to Create
| File | Purpose |
|------|---------|
| `api/ClinicManagement.Application/Features/Stock/Commands/SetStockExpirySettingsCommand.cs` | on `SetRecallSettingsCommand`'s shape |
| `api/ClinicManagement.Application/Features/WaitingList/Commands/CancelWaitingListEntryCommand.cs` | makes `Cancel()` reachable |
| `api/ClinicManagement.Infrastructure/Migrations/<ts>_NullableDobLabOrderAppointment.cs` | adds `LabWorkOrders.AppointmentId` + FK, makes `Patients.DateOfBirth` nullable, drops `Appointments.BookedOutsideWorkingHours` — **drops below any backfill** |
| `web/app/journal/page.tsx` | the audit journal (AC-19) |
| `web/lib/api/audit.ts` | client for the existing `GET /api/audit` |
| `api/ClinicManagement.UnitTests/Features/Patients/NullableDateOfBirthTests.cs` | AC-18, D-1, D-2 |

#### Files to Modify (Part 4)
| File | Changes |
|------|---------|
| `api/ClinicManagement.Domain/Entities/Patient.cs` | `DateOfBirth` → `DateTime?` (property, ctor, `UpdatePersonalInfo`) |
| `api/ClinicManagement.Application/Features/Patients/PatientFromRequest.cs:85-87` | delete the `UtcNow.AddYears(-30)` substitution |
| `api/ClinicManagement.Application/Features/Patients/DentitionRules.cs:38` | `FromDateOfBirth(DateTime?, …)` — return "ask which dentition" for null |
| `api/ClinicManagement.Application/Features/Patients/PatientDuplicateIndex.cs:75,100,131` | `Entry.DateOfBirth` → `DateTime?`; the name-alone rule fires for null (D-2), matching neither wider nor narrower |
| `api/ClinicManagement.Domain/Repositories/IPatientRepository.cs:84`, `Infrastructure/Repositories/PatientRepository.cs:63` | `PatientIdentity.DateOfBirth` → `DateTime?` |
| `api/ClinicManagement.Application/DTOs/PatientDto.cs:9`, `Features/Patients/PatientMappingExtensions.cs:28`, `Commands/CreatePatientCommand.cs:17,221` | nullable end to end |
| `api/ClinicManagement.Application/Features/Documents/Commands/CreateMedicalDocumentCommand.cs:159` | `!= default` → `.HasValue` |
| `api/ClinicManagement.Application/Features/Patients/Import/{PatientImportRowReader.cs:245,PatientImportPlanner.cs:87,104}` | drop the `?? default` sentinel |
| `api/ClinicManagement.Application/Common/Csv/ExportTables.cs:42` | blank cell, not a fabricated day |
| `api/ClinicManagement.Domain/Entities/Clinic.cs:341-349` | `SetStockExpiryLeadDays` guard `1–365` → `0–365`, `0` = alerte désactivée, French range message |
| `api/ClinicManagement.API/Controllers/StockController.cs` | the settings route (`AdminOnly`, on `RecallController.cs:61`'s shape) |
| `api/ClinicManagement.Domain/ValueObjects/InsuranceInfo.cs:14-25` | accept a one-sided value; both messages French — **before** the client padding is removed |
| `api/ClinicManagement.Application/Features/Patients/PatientFromRequest.cs:73-75` | the **create**-path guard: build the VO when *either* side is present, instead of silently dropping a one-sided entry (AC-21) |
| `api/ClinicManagement.Application/Features/Patients/Import/PatientImportRowReader.cs:193` | verify a one-sided insurance row is carried through, not dropped |
| `web/components/edit-patient-dialog.tsx:655-657,770-772` | delete the `"Unknown"` padding |
| `api/ClinicManagement.Domain/Entities/LabWorkOrder.cs`, its configuration, `Create/UpdateLabWorkOrderCommand`, `LabWorkOrderDto` | nullable `AppointmentId`, clinic- **and** patient-checked on `CreateInvoiceCommand.cs:87-100`'s pattern |
| `api/ClinicManagement.Domain/Entities/Appointment.cs:78,81-85` + its 4 write sites | remove `BookedOutsideWorkingHours` and `MarkBookedOutsideWorkingHours()` |
| `api/ClinicManagement.Domain/Entities/WaitingListEntry.cs` | reachable `Cancel()`; DTO surfaces `ResultingAppointmentId` as a link |
| `api/ClinicManagement.Application/Common/Services/ClinicContext.cs:73-85`, `Common/Interfaces/IClinicContext.cs:31,36` | delete `EnsureClinicAccess` + `BelongsToClinic` (keep `ForbiddenAccessException`) |
| `web/lib/hooks/use-paged-list.ts` | `filters?: readonly unknown[]`; reset page on any change |
| `web/components/{patients-table,factures/invoices-table,procedure-types-table}.tsx` | pass `filters` |
| `web/app/lab-orders/page.tsx:706,744,799` | `patientName` becomes a link to the patient; appointment link |
| `web/components/appointment-calendar.tsx`, `web/app/appointments/page.tsx` | inline quick-status control from `allowedNextStatuses`, 44 px on a coarse pointer |
| `web/components/documents/…` patient documents tab | show which visit produced the document (`MedicalDocument.AppointmentId`) |
| `web/lib/nav.ts` | `/journal` inside `buildConfigItems`'s admin branch |
| `web/lib/api/types.ts:515` | `dateOfBirth?: string \| null` |
| `web/app/waiting-list/page.tsx`, `web/components/stock-table.tsx` (+ 6 other hand-rolled lists) | the cancel action; verify each list's filter→page-1 reset and fix any that fails |

---

## Implementation Stories

### US-1: Close the adoption gaps — the till tells the truth, cheques have a life-cycle, El Fatoora is gone

**Goal:** A clinic can trust the money it sees: a re-saved fiche never silently drops a payment, a cheque has a
banked state, la caisse's day is the Tunisian day, the e-invoicing subsystem no longer blocks a cancellation or
haunts the UI, and the remaining small defects — a fabricated date of birth, an unreachable audit journal, an
uneditable stock lead time, a `"Unknown"` insurance row — are closed.
**Blocked by:** None
**Layers:** DB, Domain, Application, API, UI, docs, tests

> **Structure.** Four ordered parts, each a vertical increment that builds, passes and commits on its own.
> Land them in order; the part boundary is the split point if the session is cut short (**R-1**).

---

#### Part 1 — Remove El Fatoora / TTN (AC-13 … AC-17, C-1, C-2)

1. Delete the 8 Infrastructure services, the 6 Application seams, `EInvoiceModels`,
   `TtnIdentityUnavailableException`, `SubmitInvoiceToElFatooraCommand`, `GetEInvoiceArtifactQuery`,
   `EInvoiceOutboxJob` and `Domain/Enums/EInvoiceStatus.cs`. **Keep** `IQrCodeGenerator`/`QrCodeGenerator` —
   `TrustController.cs:131-138` renders the LAN trust page's QR from it; only `QrCodeGeneratorTests`' `ttn=`
   payload literal changes.
2. Strip `Invoice.cs`: the 8 e-invoice properties, the 8 `MarkEInvoice*`/`CopyEInvoiceStateFrom` members,
   `CanCancel`'s three e-invoice terms, and `Cancel()`'s « déclarée à El Fatoora » throw plus the dequeue block
   (AC-14). Leave `CanBeDeleted` and `DeleteInvoiceCommand` alone — neither carries an e-invoice term.
3. Strip `Clinic.cs`: `TtnEInvoicingEnabled`, `TtnEnvironment`, the four `Ttn*` identity columns,
   `SetTtnIdentity` and the e-invoicing setter.
4. Drop both `IInvoiceRepository` outbox methods, the `EInvoiceOutboxDepth` record and their implementations;
   `GetOutboxDepthQuery` reports two queues and `OutboxDepthDto` loses `EInvoices` (a `required` init prop, so
   the compiler finds every site) — AC-15.
5. Drop the DI registrations (`Extensions.cs:347-354`), `DeploymentProfile.SharesInstallWideTtnIdentity` **and**
   its `DeploymentProfileTests.ExpectedMatrix:59` row (the matrix is reflected over, so there is no hard-coded
   15 to update).
6. In `Program.cs`: delete the `dispatch-einvoices` `AddOrUpdate` (~L688-694) and add
   `RecurringJob.RemoveIfExists("dispatch-einvoices")` beside the `sync-google-calendar` precedent (~L752) —
   AC-16b, C-2.
7. Repair the four background-job `cref`s (`StockExpiryJob:22`, `BackupJob:17`, `DocumentEmailJob:17`,
   `NotificationJob:84`) — a `cref` to a removed type breaks the 0-warning gate (AC-16c).
8. Remove the PDF surfaces: the invoice cachet block, and the avoir's TTN warning
   (`PdfGenerationService.cs:712-718`) with its `AvoirPdfData.CorrectedInvoiceIsTtnRegistered` ←
   `CreditNoteDto` feed and the banner it drives at `invoice-detail-modal.tsx:453-458`. Nothing renders an empty
   placeholder where the QR was.
9. Drop `"EInvoiceStatus"` from `AuditSaveChangesInterceptor.cs:78`; drop `ttn-identity-is-complete` and its
   reader block and `ClinicsWithPartialTtnIdentity` field (AC-17).
10. `web/`: remove the El Fatoora settings section, the submit action, the status badges, the artifact downloads
    and the types.
11. **Scaffold** `RemoveEInvoicing` (do not hand-write it — the regenerated
    `ApplicationDbContextModelSnapshot.cs` is what makes AC-13's grep pass). `Down()` throws
    `NotSupportedException` with a French sentence naming the export-before-migrating requirement. Blobs are
    left in object storage by decision.
12. Delete the 7 TTN-dedicated test classes and update the 26 referencing ones (AC-16).
13. Update the 6 `CLAUDE.md` files, `packaging/README.md`, `packaging/server/clinic-server.iss`,
    `deploy/README.md`. `features/**` history is left untouched.
14. Retire **`follow-up/ttn-per-clinic-identity-write-path.md`** and its index entry — it asks for the admin
    write path onto the four `Ttn*` columns, which this part deletes, so leaving it open would keep an
    instruction to build a surface for columns that no longer exist.

**Validation:**
- [ ] `grep -riE "ttn|fatoora|teif|einvoice" api/ web/ --exclude-dir=Migrations` returns nothing (AC-13, part 1)
- [ ] `grep -riE "ttn|fatoora|teif|einvoice" api/ClinicManagement.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
      returns nothing (AC-13, part 2). **Two greps, not one:** the snapshot sits inside `Migrations/`, which the
      first command excludes wholesale — so a snapshot still carrying all 15 TTN hits passes it silently
- [ ] `dotnet build` clean, **0 warnings** (proves AC-16c)
- [ ] Unit suite green with 7 classes deleted (AC-16); `DeploymentProfileTests`' four tests still pass
- [ ] An invoice in the old `Valid`/`Submitted` state is cancellable after migrating (AC-14, C-1)
- [ ] `GET /api/outbox` returns two queues (AC-15)
- [ ] `dotnet run -- verify-schema` passes with no `ttn-identity-is-complete` (AC-17)
- [ ] Restarting an install with a `dispatch-einvoices` row leaves no recurring job (AC-16b, C-2)

---

#### Part 2 — Money integrity (AC-1 … AC-7, A-1, A-2)

1. Add `PaymentMethod` + the three cheque fields to `DentalRecord`, guarded by the **existing**
   `ChequeDetails.For` (no new guard); map them; scaffold `AddDentalRecordPaymentMethod`. Null method = Cash for
   historical rows.
2. Rename `CreateInvoiceFromDentalRecordCommand` → **`BillDentalRecordCommand`** (same namespace, so the
   realtime `invoices` key is unaffected) and change its return to `Result<DentalRecordBillingResult>`.
   `InvoicesController` unwraps `.Invoice`, keeping `POST /api/invoices/from-dental-record/{id}`'s body.
3a. **The AC-2 / AC-3b refusal lives in `UpdateDentalRecordCommand`, pre-commit — not in the billing command.**
   `DentalRecordAutoBilling` runs **post-commit** by design (`DentalRecordAutoBilling.cs:19-21`, `:106`: « The
   record is already committed »), so a refusal raised there arrives *after* the lowered « Montant payé » or the
   changed `Cost` has been saved: the user sees a French message and the edit stuck anyway, leaving the fiche
   permanently disagreeing with its own note d'honoraires. « Refusé » has to mean the save did not happen.
   So `UpdateDentalRecordCommand` loads the fiche's existing invoice link **before** `SaveChangesAsync` (one extra
   read on the update path) and returns `Result.Failure` with the French message + `Code` when, on a fiche whose
   invoice is issued and not cancelled:
   - « Montant payé » is **lower** than what has already been collected → name the avoir (AC-2);
   - the acts — and therefore `Cost` — changed → name the note d'honoraires and the avoir (AC-3b).
   Nothing is written; the fiche keeps its previous state. `BillDentalRecordCommand` still implements the same two
   refusals as its own typed outcomes: it is the **manual** « Facturer cette intervention » path as well, and a
   backstop that agrees with the pre-commit guard costs nothing. The refusal wording is written **once** and shared
   by both, so the two cannot drift.
   `CreateDentalRecordCommand` needs no such guard — a new fiche has no invoice.

3. Teach it the already-billed branch, replacing the hard refusal:
   - **higher** `AmountPaid` → record the difference as an additional `Payment` on the existing invoice,
     re-running the over-payment check against the frozen TTC → `Outcome = ToppedUp` (AC-1);
   - **lower** → refuse in French naming the avoir, writing nothing (AC-2);
   - **changed `Cost`** → refuse in French naming the note d'honoraires and the avoir; no line is added, removed
     or repriced after issue (AC-3b);
   - **invoice cancelled or fully credited** → refuse and name the invoice, never a second document (A-1);
   - nothing to add → `Outcome = AlreadyBilled`.
   Every refusal also sets `Result.Code`. **Every new catch carries `when (ex is not ConflictException)`** so the
   `xmin` 409 still surfaces as a French conflict (A-2).
4. `DentalRecordAutoBilling`: delete the `Contains("déjà facturée")` match at L86, switch on the typed outcome,
   and pass the fiche's `PaymentMethod` + cheque details instead of the hard-coded `Cash` at L64 (AC-4).
5. Extend `DentalRecordBillingOutcome` and surface **every** outcome in
   `patient-record-modal.tsx:505-527` — `AlreadyBilled` is an informational toast, not plain green (AC-3).
6. Add the four payment fields to the fiche form, reusing `factures/cheque-fields.tsx` and
   `chequePaymentFields()` — phone-leading, since this is chairside.
7. Create `CaissePeriod`; move both caisse handlers' identical bound logic into it and add `FromDay`/`ToDay`
   (bare `YYYY-MM-DD`, resolved through `ClinicClock.LocalDayRangeUtc` / `LastTickOfLocalDayUtc`). Keep `From`/
   `To` instants for the other callers. `BillingController` binds day keys on summary, ledger **and** export.
8. `web/app/caisse/page.tsx` sends day keys; delete `rangeBounds` (L119-124); move `dashboard-links.ts`'s
   `/caisse` range links with it (AC-6).
9. `ExpenseRepository.cs:41,64`: `< to` → `<= to`, matching the three sibling ledgers (AC-7).
10. Add the plan-workspace void affordance for an installment payment against the **existing**
    `POST /api/treatment-plans/{id}/installments/{installmentId}/payments/{paymentId}/void` — required motif,
    struck-through row with motif and actor, « REÇU ANNULÉ » on reprint. Follow
    `invoice-detail-modal.tsx`'s in-place panel, not a nested dialog (AC-5).
11. Correct the bridge-cancellability comment in **all three** places
    (`IssueInvoiceCommand.cs:181-187`, `TreatmentPlanRepository.cs:217-218`, `PlanBillingRules.cs:31-33`) and
    make the refusal name the avoir as the only route.

**Validation:**
- [ ] Fiche 400,000 / 200,000 payé → edit to 400,000 → one invoice, 400,000 collected; « Encaissé », the
      patient's solde and the dashboard each move by 200,000 (AC-1)
- [ ] Lowering the amount refuses in French **and the fiche is unchanged after the refusal** (re-open it: the old
      amount is still there); no payment row written or altered (AC-2)
- [ ] Adding an act to a billed fiche refuses in French **and the act is not persisted** (AC-3b)
- [ ] `grep "déjà facturée" DentalRecordAutoBilling.cs` returns nothing (AC-3)
- [ ] A fiche settled by chèque produces `Method = Cheque` with number/banque/échéance, appears in « Chèques à
      encaisser » and under « dont chèques », **not** « dont espèces » (AC-4)
- [ ] Voiding an échéancier payment behaves identically to voiding an invoice payment (AC-5)
- [ ] With the workstation clock in another timezone, la caisse returns exactly that Tunisian day; a 00:00
      payment appears in exactly one day (AC-6)
- [ ] `MoneyReadConsistencyTests` holds `Σ(extrait) == cashIn − refunds − cashOut == net` with an expense on the
      window's last day (AC-7)
- [ ] Two concurrent fiche saves still yield a French 409 (A-2)

---

#### Part 3 — Cheque life-cycle (AC-8 … AC-12, B-1, B-2)

1. Add `ChequeBankedOn` + `ChequeBankedByUserId` to `Payment` and `InstallmentPayment` with an `internal
   SetBanked(...)` refusing a non-cheque method; add the aggregate-root entry points on `VoidPayment`'s pattern.
   Scaffold `AddChequeBankedStamp`. Both null for every existing row.
2. Add the two routes, `AdminOrDoctor`, body `{ banked: bool }`, mirroring the void routes one for one:
   `POST /api/invoices/{id}/payments/{paymentId}/banked` and
   `POST /api/treatment-plans/{id}/installments/{installmentId}/payments/{paymentId}/banked`. **Not** a single
   payment-id route — an `InstallmentPayment` is only addressable as `{plan, installment, payment}`.
3. `ChequeDto` gains `InstallmentId` and the owning aggregate id (it carries neither today), and
   `CaisseInstallmentPaymentRow` gains `InstallmentId`, so the client can address either route.
4. `GetChequesDueQuery` defaults to outstanding, accepts a `banked` filter, and computes the four bucket counts
   over **outstanding only** (AC-11). Un-marking is supported and lands in the audit ledger (AC-10).
5. **The write is keyed by the row the query returned** (B-1): the bridged-plan de-dup keys on the *plan*, whole
   plan, so once a plan is bridged only the invoice-side `Payment` is reachable — a stamp cannot travel back
   across the one-way `ToChequeDetails()` carry, and a cheque cannot be marked twice.
6. **Confirm rather than assume B-2**: a bridge invoice carrying a non-voided payment cannot be cancelled
   (Part 2 step 11 makes the refusal say so), and if its payments were voided first, AC-12 has already removed
   the cheque from every view. Pin it with a test.
7. Add the **four** parts of `cheque-banked-only-on-cheques` to `verify-schema`, on
   `cheque-details-only-on-cheques`'s pattern: the `DataMigrationCounts` field, the `ScalarOrNullAsync` block
   guarded on the new column (so a pre-migration DB reports « not applicable », never a reassuring `0`), the
   `Add(...)` line, and the clean **and** not-applicable cases in `SchemaVerificationServiceTests`.
8. `cheques-table.tsx`: « Encaissés » filter showing when and by whom, the mark/un-mark action, cards below
   640 px, the confirmation as a sheet in `dvh`.

**Validation:**
- [ ] Marking encaissé stamps date + actor + moment; the row leaves the default view and is found under
      « Encaissés » (AC-8)
- [ ] A test pins that marking changes **no** figure in la caisse, the dashboard, the patient's solde or the
      invoice (AC-9)
- [ ] Un-marking works and appears in `GET /api/audit` (AC-10)
- [ ] The four buckets count outstanding cheques only (AC-11)
- [ ] Voiding the underlying payment removes the cheque from every view, banked or not (AC-12)
- [ ] A bridged cheque cannot be marked twice (B-1); B-2's reversibility is covered by test, not assumption
- [ ] `dotnet run -- verify-schema` reports the new check clean, and « not applicable » pre-migration

---

#### Part 4 — Remaining defects (AC-18 … AC-26, D-1, D-2, D-3)

1. **Nullable DOB (AC-18).** Delete `PatientFromRequest.cs:85-87`'s substitution, then follow the compiler:
   `Patient`, `PatientDto`, `CreatePatientCommand`, `PatientMappingExtensions`, `PatientIdentity`,
   `PatientRepository`, the import reader/planner's `?? default`, `ExportTables:42`, and
   `CreateMedicalDocumentCommand.cs:159` (`!= default` → `.HasValue`). The two real breaks are
   `DentitionRules.FromDateOfBirth` (return « ask which dentition » for null — the odontogram decision the
   substitution existed for) and `PatientDuplicateIndex.Entry`, whose `record struct` `DateTime` becomes
   nullable **without widening or narrowing which rows match**: the name-alone rule already written for « no DOB
   supplied » is the one that fires (D-2). `CnamReimbursementCalculator.RateForPatient` already takes `DateTime?`
   and needs no change. No backfill (D-1). Client: widen `dateOfBirth` in `types.ts:515`; all four age helpers
   already guard on a falsy value; the list, fiche and summary show « âge inconnu ».
2. **Journal (AC-19).** `/journal` page + `web/lib/api/audit.ts` over the **unchanged** `GET /api/audit`, with
   `entityType`/`entityId`/`from`/`to`/`action` and the standard pager, newest-first; nav entry inside
   `buildConfigItems`'s admin branch plus the page's own `role === "admin"` gate, copying
   `/cnam-nomenclature`. Cards below 640 px, one entry per card.
3. **Stock lead days (AC-20).** Widen `Clinic.cs:341-349` to `0–365` with `0` = alerte désactivée and a French
   range message — both readers already implement 0-means-off. Add
   `SetStockExpirySettingsCommand` + an `AdminOnly` `StockController` route on `RecallController.cs:61`'s shape,
   and the stock-settings control. This gives the existing domain setter its first caller.
4. **Insurance (AC-21), in this order:** first make `InsuranceInfo` accept a one-sided value with **French**
   messages, *then* relax the **create** path's own guard, *then* delete
   `edit-patient-dialog.tsx:655-657,770-772`'s `"Unknown"` padding — `UpdatePatientCommand.cs:237-241` constructs
   the VO unguarded, so the reverse order turns a one-sided entry into a 500. No backfill of existing rows.
   ⚠️ **There are three guards, not two, and the third is on the create path.**
   `PatientFromRequest.cs:73-75` builds the VO only when `Provider` **and** `PolicyNumber` are both non-blank and
   otherwise leaves it `null` — a **silent drop**, not a refusal. All three creation doors route through it (the
   patient form, the appointment dialog's inline « Nouveau patient », and the CSV import), so without this the AC
   passes on « Modifier » and fails on « Nouveau patient » with no message at all. It becomes « build it if
   **either** side is present » — still `null` when both are blank, so an untouched form stores nothing as before.
   Verify `PatientImportRowReader.cs:193` likewise carries a one-sided row through rather than dropping it: a
   silent drop on a 3 000-row import is unrecoverable without re-importing.
5. **Pager reset (AC-22).** `usePagedList` takes `filters?: readonly unknown[]` and resets page on any change,
   still debouncing only `search`; wire the three consumers. ⚠️ The reset keys on
   `JSON.stringify(filters ?? [])`, **not** on the array's identity — an inline literal is a new reference every
   render, and an identity-keyed effect would undo the user's page click on every render (decision 4).
   Verify by hand after wiring: change a filter → page 1; then go to page 2 and **stay** there across a re-render. Then **verify each of the eight hand-rolled lists**
   resets on every filter it exposes and fix any that does not — exploration found all eight currently correct,
   so this is confirmation, but no list is assumed.
6. **Lab orders (AC-23, D-3).** Nullable `LabWorkOrder.AppointmentId` with a real FK, validated clinic- **and**
   patient-side on `CreateInvoiceCommand.cs:87-100`'s pattern; French messages. `lab-orders/page.tsx:706,744,799`
   render `patientName` as a link, and the order links to its appointment; the bon appears on the patient's file.
7. **Agenda quick status (AC-24).** An inline control on the appointment driven by
   `AppointmentDto.AllowedNextStatuses` (already served at all four read/write sites), copying
   `lab-orders/page.tsx:716-731`. `Appointment.cs:144-170` is the only authority — notably
   `Completed → { Cancelled }` alone. 44 px on a coarse pointer, never hover-revealed.
8. **AC-25.** Remove `Appointment.BookedOutsideWorkingHours`, `MarkBookedOutsideWorkingHours()`, its four write
   sites and its column (zero readers, confirmed). Surface `MedicalDocument.AppointmentId` in the patient's
   documents tab — it *is* read, at `CreateMedicalDocumentCommand.cs:290-292`, and only lacked a UI consumer.
   Surface `WaitingListEntry.ResultingAppointmentId` as a link to the RDV it became, and make `Cancel()` /
   `WaitingListStatus.Cancelled` reachable with a « Retirer » action, default view hiding cancelled entries.
9. **AC-26.** Delete `ClinicContext.EnsureClinicAccess` + `BelongsToClinic` and their interface declarations
   (keep `ForbiddenAccessException` — `ExceptionMiddleware` uses it). French strings only on the paths this part
   already touches; the ~90-site Application sweep and the 100+ domain `ArgumentException` messages stay out of
   scope as a follow-up.
10. Scaffold **one** migration for the three schema changes, with the two drops **below** any backfill — EF's
    differ orders by schema dependency, not data safety.

**Validation:**
- [ ] A walk-in created from the appointment dialog's inline form stores **no** DOB; the list, fiche and summary
      show « âge inconnu » and the odontogram asks which dentition (AC-18)
- [ ] `/journal` is `AdminOnly`; deleting a patient then opening it shows who and when (AC-19)
- [ ] Stock lead days editable; `0` disables the alert in the job, the dashboard and the list (AC-20)
- [ ] Entering only an insurance number stores exactly that **on « Nouveau patient » as well as « Modifier »**
      (and survives a CSV import row with only one side); `"Unknown"` never reaches storage or screen (AC-21)
- [ ] Changing any filter on all eleven paged lists returns to page 1 (AC-22) — **and** paging to page 2 with the
      filters untouched stays on page 2 (the identity-vs-signature trap in decision 4)
- [ ] A bon de prothèse attaches to a séance, shows on the patient's file, and links both ways (AC-23)
- [ ] The agenda offers only the legal next statuses inline, 44 px on touch (AC-24)
- [ ] `grep -r "BookedOutsideWorkingHours" api/ --exclude-dir=Migrations` returns nothing (AC-25)
- [ ] `grep -r "EnsureClinicAccess\|BelongsToClinic" api/` returns nothing (AC-26)
- [ ] `npx tsc --noEmit` + `npm run check:responsive` + `npm run build` clean; eye pass at
      320/390/820/1180/1440 px

---

## Testing Strategy

The backend unit suite is the **only** automated check this product has, and nothing in it touches a database.
`web/` has no test runner, no working ESLint and no CI — `tsc --noEmit` + `check:responsive` + `build` + an eye
pass *is* that project's whole gate. Schema-shaped claims are therefore verified by `verify-schema`, not tests.

### Unit tests (xUnit + Moq, `api/ClinicManagement.UnitTests/`)
- **`DentalRecordRefusalTests`** (new): the pre-commit half of AC-2 / AC-3b — a lowered « Montant payé » and a
  changed act list on a billed fiche each return `Result.Failure` and **`SaveChangesAsync` is never called**
  (asserted on the `IUnitOfWork` mock), so the fiche cannot be left disagreeing with its invoice.
- **`BillDentalRecordOutcomeTests`** (new): each outcome — first billing, `ToppedUp` (AC-1), lower-amount refusal
  (AC-2), changed-`Cost` refusal (AC-3b), `AlreadyBilled`, cancelled/fully-credited invoice (A-1) — and that a
  `ConflictException` is not flattened by the new catches (A-2). Asserts on the **enum**, never on prose.
- **`DentalRecordAutoBillingTests`** (extend): the method + cheque details reach the payment (AC-4).
- **`CaissePeriodTests`** (new): a day key resolves to the Tunisian day; a 00:00 payment lands in exactly one
  day (AC-6); the last-tick bound is inclusive on all four ledgers (AC-7).
- **`MoneyReadConsistencyTests`** (extend): `Σ(extrait) == cashIn − refunds − cashOut == net` with an expense on
  the window's last day; caisse-vs-dashboard agreement preserved.
- **`ChequeBankedStampTests`** (new): the stamp changes no figure anywhere (AC-9, the spec's explicit "pinned by
  a test"); buckets count outstanding only (AC-11); a void removes the cheque regardless of banked state
  (AC-12); a bridged cheque is unmarkable twice (B-1); the bridge-cancellation interaction (B-2).
- **`SchemaVerificationServiceTests`** (extend): `cheque-banked-only-on-cheques` clean **and** not-applicable.
- **`NullableDateOfBirthTests`** (new): a null DOB does not widen or narrow duplicate matching (D-2);
  `DentitionRules` asks rather than assumes; the CNAM estimate is unchanged.
- **`DeploymentProfileTests`** (extend): the matrix row is deleted with the property; all four tests stay green.
- **`InvoiceCancellationTests`** (extend): an invoice in the old `Valid`/`Submitted` state is cancellable
  (AC-14, C-1).
- Delete the 7 TTN-dedicated classes; update the 26 referencing ones (AC-16).

### Schema verification (`dotnet run -- verify-schema`)
- Run **before and after** each of the three migrations and diff the output, per the repo's own workflow.
- New: `cheque-banked-only-on-cheques`. Removed: `ttn-identity-is-complete`.
- Existing checks that must stay clean: `cheque-details-only-on-cheques`, `appointment-act-rows`,
  `clinical-child-clinic-matches-patient`.

### Reconciliation (`dotnet run -- reconcile-money`)
- Run before and after **Part 2** and diff — it is the only thing that can prove the top-up moved no closed
  month and created no duplicate document.

### Manual / device verification (no runner exists for these)
- La caisse with the workstation clock set to UTC−5 and to UTC+8 (AC-6).
- The fiche re-save flow end to end, watching « Encaissé », the patient's solde and the dashboard (AC-1).
- Eye pass at 320/390/820/1180/1440 px on `/journal`, `/cheques`, the fiche payment fields and the agenda
  quick-status control; the last two on a **coarse pointer** specifically (AC-24's 44 px).

---

## Risk Register

| ID | Risk | Likelihood | Impact | Part | Mitigation |
|----|------|------------|--------|------|------------|
| R-1 | One story is far past a single implementation session | High | Med | all | Four ordered parts, each independently committable; split at a part boundary |
| R-2 | The TTN migration is irreversible and discards column contents | Med | High | 1 | Throwing `Down()`; export before migrating; audit found only fabricated sandbox states |
| R-3 | **Four** migrations in one story → snapshot drift, duplicate migrations | High | High | 1,2,3,4 | Scaffold + commit the snapshot with each migration, one at a time; `-p:BaseOutputPath` |
| R-4 | Nullable DOB silently changes duplicate matching | Med | High | 4 | `NullableDateOfBirthTests` pins the match set before and after (D-2) |
| R-5 | A new catch in the billing command flattens the `xmin` 409 | Med | High | 2 | Every new catch carries `when (ex is not ConflictException)`; pinned by test (A-2) |
| R-6 | AC-13's grep **cannot see** `ApplicationDbContextModelSnapshot.cs` — it is inside `Migrations/`, which the command excludes, so a snapshot still holding 15 TTN hits passes silently | Med | Med | 1 | Two greps: the broad one plus a second aimed at the snapshot alone; scaffold the migration rather than hand-write it so the snapshot regenerates |
| R-7 | Smart App Control blocks freshly-built test assemblies | High | Low | all | Build to a path outside the repo: `BaseOutputPath=<temp> dotnet test` |
| R-8 | `web/` regressions are invisible — no test runner, no CI, no ESLint | High | Med | 2,3,4 | `tsc --noEmit` + `check:responsive` + `build` + the five-width eye pass, per part |
| R-9 | B-2's bridge reversibility is assumed rather than verified | Med | Med | 3 | The spec requires confirming it; a test, not a reading |
| R-10 | Renaming the billing command breaks realtime or the manual dialog | Low | Med | 2 | Same namespace ⇒ same `invoices` key; controller unwraps `.Invoice` ⇒ unchanged body |
| R-11 | Deleting `using`s during the TTN sweep breaks the build on a co-located type | Med | Low | 1 | Grep every symbol a namespace supplies before dropping it (LEARNINGS) |
| R-12 | Part 4's migration drops a column above its backfill | Low | High | 4 | Inspect the generated `Up()` and reorder; EF orders by schema dependency, not data safety |
| R-13 | The whole 100-file diff lands as one commit and swallows in-flight work | Med | Med | all | `git diff HEAD --numstat` before every `git add`; branch carries 25 dirty files today |

### R-1: Single story exceeds one session
- **Description:** ~100 files across Domain, Application, Infrastructure, API, `web/`, docs and tests, plus
  three migrations and a subsystem deletion. Well past the ~10-12 file session heuristic.
- **Likelihood:** High · **Impact:** Medium · **Part:** all
- **Mitigation:** The four parts are ordered and dependency-respecting, and each is a vertical increment that
  builds, passes its validation and commits on its own. `/implement-story` lands them part by part.
- **Contingency:** Stop at a part boundary. Parts 2, 3 and 4 are independent of each other; only Part 1 must
  precede Part 2.
- **Note:** The owner chose one story explicitly against the sizing recommendation. Recorded, not re-litigated.

### R-2: The TTN migration is irreversible
- **Description:** `RemoveEInvoicing` drops 14 columns including `TtnIdentifier`, `SignedXmlStorageKey` and
  `TtnReceiptStorageKey`. A production-signed invoice's declaration would be unrecoverable.
- **Likelihood:** Medium · **Impact:** High · **Part:** 1
- **Mitigation:** The audit established that the only reachable configuration was the sandbox, whose `Validated`
  responses are fabricated (`SandboxTtnClient.cs:51`) — no row holds a legally meaningful declaration. `Down()`
  throws with a French sentence rather than pretending to restore. Blobs are left in object storage.
- **Contingency:** Restore from the pre-migration backup the startup path already takes (it aborts the migration
  if the backup fails).

### R-3: Snapshot drift across three migrations
- **Description:** An uncommitted model snapshot makes the next `migrations add` re-emit the previous
  migration's changes; a running dev API also locks `api/**/bin`. **Four** migrations, one per part.
- **Likelihood:** High · **Impact:** High · **Parts:** 1, 2, 3, 4
- **Mitigation:** Scaffold, inspect, and commit each migration **with its snapshot** before starting the next
  part; use `-p:BaseOutputPath=<temp>`; never `--no-build`. Run `verify-schema` before and after each and diff.
- **Contingency:** `migrations remove`, reset the snapshot to the last committed state, re-scaffold.

### R-4: Nullable DOB changes duplicate matching
- **Description:** `PatientDuplicateIndex.Entry` is a `record struct` whose non-nullable `DateTime` is compared
  with `.Date` and against `default`. Making it nullable can silently widen (everyone with no DOB matches) or
  narrow (nobody does) which rows match — and this product has no patient merge and no soft delete, so a false
  negative is permanent.
- **Likelihood:** Medium · **Impact:** High · **Part:** 4
- **Mitigation:** `NullableDateOfBirthTests` pins the match set for the three rules (name+DOB, name-alone,
  phone) before and after; the name-alone rule already written for « no DOB supplied » is what fires (D-2).
- **Contingency:** Keep the `Entry` non-nullable and carry an explicit `HasDob` flag if the nullable form cannot
  be made to match exactly.

### R-5: A new catch flattens the concurrency 409
- **Description:** Part 2 adds branches (and their catches) to the billing command. The repo's standing rule is
  that a catch-all returning a `Result` must carry `when (ex is not ConflictException)` or the `xmin` 409
  becomes a generic failure.
- **Likelihood:** Medium · **Impact:** High · **Part:** 2
- **Mitigation:** Every new returning catch carries the filter; a test saves the same fiche twice and asserts
  the French conflict survives (A-2).
- **Contingency:** Add the filter and re-run `ConcurrencyConflictTests`.

### R-8: `web/` regressions are invisible
- **Description:** Parts 2-4 change eleven paged lists, the caisse page, the cheques table, the agenda, the
  fiche form, the patient dialog and add a new page — with no test runner, no CI and no working ESLint.
- **Likelihood:** High · **Impact:** Medium · **Parts:** 2, 3, 4
- **Mitigation:** Run the full gate (`tsc --noEmit`, `check:responsive`, `build`) at the end of **each part**,
  not once at the end; eye pass at the five widths on every screen the part touched.
- **Contingency:** Standing up a runner is out of scope; regressions are caught by the eye pass or in review.

---

## Breaking Changes

### 1. El Fatoora / TTN e-invoicing is removed
- **What breaks:** `POST` submit and the artifact download routes disappear; `InvoiceDto` loses its e-invoice
  fields; `ClinicDto` and `UpdateClinicRequest` lose the TTN settings; `OutboxDepthDto` loses `EInvoices`;
  `DeploymentProfile` loses a capability (15 → 14).
- **Who is affected:** Any client reading those fields — in practice only this repo's `web/`, updated in the
  same part. An issued invoice in `Valid`/`Submitted` becomes cancellable (C-1, intended).
- **Handling:** Removed in one part with its clients; `EInvoices` being a `required` init prop makes the DTO
  change compile-checked.

### 2. `PatientDto.dateOfBirth` becomes nullable
- **What breaks:** Any consumer assuming a date is always present.
- **Who is affected:** `web/` (`types.ts:515`), the CSV export, the odontogram's dentition default.
- **Handling:** All four client age helpers already guard on a falsy value; the type widens and the three
  surfaces render « âge inconnu ». No backfill (D-1).

### 3. La caisse's period parameters
- **What breaks:** `/caisse` now sends bare `YYYY-MM-DD` day keys instead of composed instants.
- **Who is affected:** the caisse page, its CSV export, and the dashboard's date-range drill-down links.
- **Handling:** All three move in Part 2; `From`/`To` instants keep working for every other caller.

### 4. `CreateInvoiceFromDentalRecordCommand` → `BillDentalRecordCommand`
- **What breaks:** the command type name and its `Result<T>`.
- **Who is affected:** internal callers and `InvoiceFromDentalRecordTests`; the HTTP route and response body are
  unchanged because the controller unwraps `.Invoice`.

### 5. `Appointment.BookedOutsideWorkingHours` is removed
- **What breaks:** nothing — zero readers, verified across the whole solution.

---

## Migrations

**Four** — one per part, each scaffolded (never hand-written) so the model snapshot regenerates.

### Migration 1 — `RemoveEInvoicing` (Part 1)
- **What:** drops 8 `EInvoice*`/`Ttn*` columns from `Invoices`, 6 from `Clinics`, and the
  `(EInvoiceStatus, EInvoiceNextAttemptAt)` index.
- **When:** with Part 1, before any Part 2 work.
- **Rollback:** **none** — `Down()` throws `NotSupportedException`. Restore from backup.
- **Steps:** 1) `verify-schema` and capture output · 2) scaffold, inspect the generated `Up()`, replace `Down()`
  with the throw · 3) migrate · 4) `verify-schema` and diff — `ttn-identity-is-complete` must be gone and
  nothing else changed · 5) confirm `ApplicationDbContextModelSnapshot.cs` has zero TTN hits.

### Migration 2 — `AddDentalRecordPaymentMethod` (Part 2)
- **What:** adds `PaymentMethod`, `ChequeNumber`, `ChequeBankName`, `ChequeDueDate` to `DentalRecords`, all
  nullable. Null method = Cash for historical rows, by read-side convention, **not** by backfill.
- **Rollback:** ordinary `Down()` drops the four columns.
- **Steps:** `reconcile-money` before · scaffold · migrate · `verify-schema` + `reconcile-money` after, diff both.

### Migration 3 — `AddChequeBankedStamp` (Part 3)
- **What:** adds `ChequeBankedOn` + `ChequeBankedByUserId` to `Payments` and `InstallmentPayments`. All null.
- **Rollback:** ordinary `Down()`.
- **Steps:** scaffold · migrate · `verify-schema` — the new `cheque-banked-only-on-cheques` must report clean
  after and « not applicable » before.

### Migration 4 — `NullableDobLabOrderAppointment` (Part 4)
- **What:** adds `LabWorkOrders.AppointmentId` + FK; alters `Patients.DateOfBirth` to nullable; drops
  `Appointments.BookedOutsideWorkingHours`.
- **⚠️ Ordering:** it both adds and drops. The **drops must sit below any backfill** in the generated `Up()`;
  EF's differ orders by schema dependency, not data safety. Inspect and reorder before running (R-12).
- **Rollback:** `Down()` re-adds the dropped column (as nullable) and reverts `DateOfBirth` — note that
  reverting to non-nullable will fail if any row has a null DOB by then, which is expected and correct.
- **Steps:** scaffold · **read the generated `Up()` line by line** · reorder if needed · migrate ·
  `verify-schema`.
