# Feature Specification: Adoption Gaps Remediation

**Status:** APPROVED
**Challenged:** Yes
**Type:** Full — *requested as one spec; too large for `/implement-small-feature` in a single pass.
Implement group by group, A → D. Groups A, B and D are independent of each other; C is independent of all.*
**Created:** 2026-08-08
**Scope:** Full (Domain · Application · Infrastructure · API · web)
**Feature:** Make the till tell the truth, give cheques a life-cycle, remove El Fatoora, and close the
remaining small defects found in `ADOPTION-AUDIT.md`.

## Overview

`ADOPTION-AUDIT.md` found that the clinical spine is sound but money can silently leave the system, and
that several fully-built features are unreachable. This closes the confirmed money leaks, gives a cheque a
banked state, removes the TTN « El Fatoora » e-invoicing subsystem entirely, and fixes the remaining small
defects. Deliberately excluded by the owner and **not** addressed anywhere below: relances/recall, phone
normalisation, every CNAM item, Arabic/RTL, and the split working day.

---

## Group A — Money integrity

### What changes
- Re-saving a fiche de soins with a **higher** « Montant payé » records the difference as an additional
  payment on the fiche's existing invoice, instead of being silently dropped.
- Re-saving with a **lower** amount is refused in French, naming the avoir as the correction path.
- Re-saving with a **changed Cost** (an act added or removed after billing) is likewise refused.
- The auto-billing attempt returns a **typed outcome** instead of being classified by matching French
  prose, so the new outcomes above are expressible at all.
- The fiche captures **how** it was paid; auto-billing carries that method (and any cheque identity) onto
  the payment instead of hard-coding espèces.
- An échéancier payment can be voided from the plan workspace, as an invoice payment already can.
- La caisse's period bounds are computed from the clinic's calendar, not the browser's.
- All four caisse ledgers agree on an inclusive upper bound.

### Acceptance criteria
- **AC-1:** Fiche 400,000 DT with 200,000 payé is billed and collected at 200,000. Editing « Montant payé »
  to 400,000 leaves the invoice at one document with 400,000 collected; « Encaissé » of the day, the
  patient's solde and the dashboard all move by 200,000.
- **AC-2:** Lowering « Montant payé » on a billed fiche is refused with a French message naming the avoir;
  no payment row is written or altered.
- **AC-3:** Every outcome of the auto-billing attempt is surfaced to the user. `AlreadyBilled` with nothing
  to add is an informational toast, not a plain green success.
  The outcome is carried by a **typed result** from `CreateInvoiceFromDentalRecordCommand`, mapped 1:1 onto
  `DentalRecordBillingOutcome`. The French substring match at `DentalRecordAutoBilling.cs:86`
  (`result.Error?.Contains("déjà facturée")`) is **deleted** — with two new outcomes on that path, classifying
  by prose means an edit to a message silently re-breaks the classification.
- **AC-3b:** Changing the acts of a fiche that is already billed — and so its `Cost` — is refused in French,
  naming the note d'honoraires and the avoir as the correction path. No invoice line is added, removed or
  repriced after issue; the document's totals are frozen at issue and its number is legally gapless.
- **AC-4:** A fiche settled by chèque produces a payment with `Method = Cheque` and its number/banque/
  échéance; it appears in « Chèques à encaisser » and under « dont chèques » in la caisse, and **not** under
  « dont espèces ».
- **AC-5:** An échéancier payment can be voided from the plan workspace with a required motif; the voided
  row is struck through with its motif and actor, leaves the period balance, leaves « Chèques à encaisser »,
  and a reprinted receipt is stamped « REÇU ANNULÉ » — identical behaviour to voiding an invoice payment.
- **AC-6:** With the workstation clock set to any timezone, opening la caisse on a given day returns exactly
  the movements of that Tunisian day. A payment recorded at 00:00 appears in exactly one day's caisse.
- **AC-7:** `Σ(extrait de caisse) == cashIn − refunds − cashOut == net` holds at a period boundary that has
  an expense dated on the last day of the window.

### Data / schema changes
- `DentalRecord` — `PaymentMethod` (nullable string, existing `PaymentMethod` vocabulary; null = unspecified,
  treated as Cash for historical rows), plus `ChequeNumber` / `ChequeBankName` / `ChequeDueDate`, all nullable
  and guarded by the existing `ChequeDetails.For` invariant. No new guard is written.

### API contract
- `POST /api/patients/{patientId}/dental-records` and its `PUT` sibling accept the four new fields.
- `POST /api/treatment-plans/{id}/installments/{installmentId}/payments/{paymentId}/void` already exists
  (`TreatmentPlansController.cs:288`) — **no backend change**; this is a client-side gap only.

### Explicit decisions
- **La caisse's day keys move to bare `YYYY-MM-DD`.** The client sends day keys; the server resolves them
  through `ClinicClock.LocalDayRangeUtc` / `LastTickOfLocalDayUtc`. No instant is composed in the browser.
  ⚠️ The precise defect is a **semantic mismatch, not a `toISOString` bug**: `caisse/page.tsx:119-124`
  correctly builds local midnight → UTC and sends the **next** midnight as `to` (exclusive-shaped), while
  `GetCaisseSummaryQuery` / `GetCaisseLedgerQuery` bind `DateTime?` and treat `to` as **inclusive**. A payment
  stamped 00:00:00.000 therefore counts in two days, and on a workstation off UTC+1 the whole window shifts.
  Both queries keep accepting an explicit instant for their other callers; the day-key form is what `/caisse`
  sends. The CSV export and the dashboard's date-range drill-down links must move with it.
- **`ExpenseRepository` moves to `ExpenseDate <= to`**, matching the other three ledgers
  (`ExpenseRepository.cs:41,64` are the two sites; the payment, installment and credit-note repositories are
  already `<= to`).
- **A bridge invoice that carried a payment stays uncancellable.** Building an "unbridge" that returns money
  to the devis is its own feature. What changes here is honesty: the refusal names the avoir as the only
  route, and the contradicting comment is corrected in **all three** places that repeat it —
  `IssueInvoiceCommand.cs:181-187`, `TreatmentPlanRepository.cs:217-218` and `PlanBillingRules.cs:31-33`.

---

## Group B — Cheque life-cycle

### What changes
- A cheque can be marked **encaissé** (banked) without voiding the payment that received it.
- « Chèques à encaisser » lists outstanding cheques by default and can reveal banked ones.

### Acceptance criteria
- **AC-8:** Marking a cheque encaissé stamps a banked date, an actor and a moment; the row leaves the default
  « à encaisser » view and is reachable under a « Encaissés » filter showing when and by whom.
- **AC-9:** Marking a cheque encaissé changes **no** figure in la caisse, the dashboard, the patient's solde
  or the invoice — it is a tracking state, not a money movement. Pinned by a test.
- **AC-10:** Un-marking is possible and audited (a cheque returned unpaid by the bank is the real case).
- **AC-11:** The four bucket counts (en retard / bientôt / plus tard / sans date) count outstanding cheques
  only.
- **AC-12:** Voiding the underlying payment removes the cheque from every view regardless of banked state.

### Data / schema changes
- `Payment` and `InstallmentPayment` — `ChequeBankedOn` (nullable date) + `ChequeBankedByUserId` (nullable).
  Both null for every existing row. `verify-schema` gains a check that a banked stamp only exists where
  `Method = Cheque`, on the pattern of `cheque-details-only-on-cheques`
  (`SchemaVerificationService.cs:362-374`). That check has **four** parts, all of which the sibling needs: a
  field on `DataMigrationCounts`, a `ScalarOrNullAsync` block in `SchemaVerificationReader` guarded on the new
  column (so a pre-migration DB reports « not applicable » rather than a reassuring `0`), the `Add(...)` line,
  and a case in `SchemaVerificationServiceTests` covering both the clean and the not-applicable sets.

### API contract
- `POST /api/invoices/{invoiceId}/payments/{paymentId}/banked` and
  `POST /api/treatment-plans/{planId}/installments/{installmentId}/payments/{paymentId}/banked` —
  body `{ banked: bool }`, both `AdminOrDoctor`.
  ⚠️ **Not** a single `/billing/cheques/{ledger}/{paymentId}` route. An `InstallmentPayment` sits two levels
  inside the `TreatmentPlan` aggregate and is only reachable as `{plan, installment, payment}` — which is
  exactly the shape the existing `VoidInstallmentPaymentCommand` already takes. A payment-id-only route would
  need two repository lookups that exist for no other reason. These mirror the void routes one for one.
- **`ChequeDto` gains `InstallmentId` and the owning aggregate id**, so the client can address either route.
  It carries neither today: `Id` is the payment row and `TargetId` is the aggregate root (invoice *or* devis),
  and `CaisseInstallmentPaymentRow` projects `TreatmentPlanId` but not `InstallmentId`.

### Explicit decision
- **La caisse keeps counting a cheque on `PaidOn` (receipt), not on banking.** Changing that is a change to
  what « Encaissé » means and would move every historical figure. The gap it leaves — Net asserting cash not
  yet in the bank — is instead made visible by the outstanding-cheque total already on the page.

---

## Group C — Remove El Fatoora / TTN e-invoicing

### What changes
The entire TTN subsystem is deleted, not disabled. An invoice is a paper/PDF note d'honoraires with no
electronic-invoicing state, no QR cachet and no submission queue.

### Acceptance criteria
- **AC-13:** No route, screen, badge, setting or background job mentions TTN, El Fatoora, TEIF or
  e-facturation. `grep -ri "ttn\|fatoora\|teif\|einvoice"` over `api/` and `web/`, **excluding
  `api/**/Migrations/**` entirely**, returns nothing.
  ⚠️ The exclusion is the whole directory, not just `.Designer.cs`: the two migrations that *added* the
  columns (`20260717180000_AddEInvoicing.cs`, `20260806075521_AddPerClinicTtnIdentity.cs`) name them in their
  `Up`/`Down` bodies and cannot be edited without corrupting migration history. Only
  `ApplicationDbContextModelSnapshot.cs` — the live snapshot — must lose its hits.
  Separately and not covered by that grep: the five `CLAUDE.md` files, `packaging/README.md`,
  `packaging/server/clinic-server.iss` and `deploy/README.md` lose their TTN sections. `features/**` history
  is left untouched.
- **AC-14:** An issued invoice can be cancelled purely on its fiscal rules; no e-invoice state can block a
  cancellation. `Invoice.CanCancel` loses three of its five terms and the `Cancel()` guard loses its « déclarée
  à El Fatoora » throw plus the dequeue block.
  ⚠️ Deletion is already unaffected — `CanBeDeleted` carries no e-invoice term and `DeleteInvoiceCommand` has
  zero e-invoice references — so no change is owed there.
- **AC-15:** `GET /api/outbox` reports two queues (rappels, e-mails de documents); the e-invoice queue is
  gone and `OutboxDepthDto` no longer carries it (`EInvoices` is a `required` init prop, so removing it is a
  compile-checked change), along with `EInvoiceOutboxDepthDto`, the `EInvoiceOutboxDepth` domain record and
  both `IInvoiceRepository` outbox methods.
- **AC-16:** The unit suite builds and passes with the **7** TTN/TEIF-dedicated test classes deleted —
  `SandboxTtnClientTests`, `TtnIdentityProviderTests`, `XadesEInvoiceSignerTests`, `TeifXmlGeneratorTests`,
  `CancelledInvoiceIsNotDispatchedTests`, `InvoiceEInvoiceTests`, `ClinicEInvoiceSettingsTests` — and the
  **26** further test files that merely reference it updated.
  ⚠️ `QrCodeGeneratorTests` looks like an eighth but **survives**: `IQrCodeGenerator` is also used by
  `TrustController` for the LAN trust page. Only its `ttn=` payload literal changes.
- **AC-16b:** The Hangfire recurring job is both unregistered **and** actively removed —
  `RecurringJob.RemoveIfExists("dispatch-einvoices")`, on the precedent already set for
  `sync-google-calendar` (`Program.cs:752`) and `dispatch-os-push` (`:745`). Without it an existing install
  keeps a recurring-job row pointing at a deleted type and errors every minute.
- **AC-16c:** The four background jobs whose XML docs `cref` the deleted `EInvoiceOutboxJob`
  (`StockExpiryJob:22`, `BackupJob:17`, `DocumentEmailJob:17`, `NotificationJob:84`) are updated — a `cref` to
  a removed type breaks the repo's 0-warning gate.
- **AC-17:** `dotnet run -- verify-schema` passes against a database migrated forward, and its
  `ttn-identity-is-complete` check no longer exists.

### Data / schema changes
- **Drop** from `Invoice`: `EInvoiceStatus`, `EInvoiceSubmittedAt`, `EInvoiceValidatedAt`,
  `EInvoiceLastError`, `EInvoiceAttemptCount`, `EInvoiceNextAttemptAt`, and the TTN identifier / QR payload /
  storage-key columns, plus the eight `MarkEInvoice*` / `CopyEInvoiceStateFrom` members.
- **Drop** from `Clinic`: `TtnEInvoicingEnabled`, `TtnEnvironment`, `TtnUsername`, `TtnApiSecretEncrypted`,
  `TtnCertificateKey`, `TtnCertificatePasswordEncrypted`, and `SetTtnIdentity` / the e-invoicing setter.
- **Delete** `EInvoiceStatus` enum.
- **One forward migration** drops the columns. Deliberately **irreversible** — see below.
- **Delete** the services and seams: `EInvoiceService`, `TeifXmlGenerator`, `XadesEInvoiceSigner`,
  `HttpTtnClient`, `SandboxTtnClient`, `TtnIdentityProvider`, `TtnSecretProtector`, `TtnConfig`,
  `TtnIdentityUnavailableException`, their five interfaces, `EInvoiceModels`, `EInvoiceOutboxJob`,
  `SubmitInvoiceToElFatooraCommand`, `GetEInvoiceArtifactQuery`, and the DI + Hangfire registrations.

### Explicit decisions
- **The migration is one-way and discards the columns' contents.** The audit found the only reachable
  configuration was the sandbox, whose `Validated` responses are fabricated
  (`SandboxTtnClient.cs:51`) — so no row holds a legally meaningful declaration. Any production-signed
  invoice would predate this decision and must be exported before migrating; the migration's `Down()` is
  left throwing rather than pretending to restore data it cannot.
- **The e-invoice blobs are left in object storage.** The migration drops `SignedXmlStorageKey` and
  `TtnReceiptStorageKey`, not the objects they point at. Removing those would need a pre-migration storage
  sweep with no rollback, for artifacts only the sandbox client ever produced.
- **`DeploymentProfile.SharesInstallWideTtnIdentity` is removed**, taking the capability count from 15 to 14.
  ⚠️ There is **no hard-coded 15 to update**: `DeploymentProfileTests` reflects over the `bool` properties
  (`Capabilities()`) and drift-guards them against `ExpectedMatrix`. Deleting the property *and* its matrix
  row keeps all four tests green. Its only production reader is `TtnIdentityProvider.cs:102`, which is itself
  deleted.
- **No French wording is left behind.** An invoice's PDF loses its cachet block entirely; nothing renders an
  empty placeholder where the QR used to be. Two further TTN surfaces go with it, both easy to miss because
  neither is on the invoice: the **avoir** PDF's TTN warning (`PdfGenerationService.cs:710-718`, driven by
  `AvoirPdfData.CorrectedInvoiceIsTtnRegistered` ← `CreditNoteDto`) and the banner it feeds in
  `invoice-detail-modal.tsx:453-456`.
- **`AuditSaveChangesInterceptor.cs:78`** drops `"EInvoiceStatus"` from its significant-changed-fields list.

---

## Group D — Remaining defects

### Acceptance criteria
- **AC-18:** A walk-in created from the « Nouveau patient » inline form of the appointment dialog stores
  **no** date of birth rather than a fabricated one. `PatientFromRequest` stops substituting
  `UtcNow.AddYears(-30)` (`PatientFromRequest.cs:85-87`); a patient with no DOB shows « âge inconnu » in the
  list, on the fiche and in the patient summary, and the odontogram asks which dentition rather than
  assuming adult.
  ⚠️ **The browser is already ready; the server is not.** All four client age helpers already guard on a
  falsy DOB — only the `dateOfBirth: string` type needs widening. Two server sites are hard **compile
  breaks** and are the real work: `DentitionRules.FromDateOfBirth(DateTime, …)` takes a non-nullable value
  and does date arithmetic on it (this is the odontogram's dentition decision, i.e. the reason the
  substitution existed), and `PatientDuplicateIndex.Entry` is a `record struct` holding a non-nullable
  `DateTime` compared with `.Date`. `CreateMedicalDocumentCommand.cs:159`'s `!= default` sentinel becomes
  meaningless and must become `.HasValue`.
  ⚠️ **This does not change the CNAM estimate.** `CnamReimbursementCalculator.RateForPatient` already takes
  `DateTime?` and returns the adult rate for null, so an unknown DOB still estimates at 60 %. What changes is
  that the app no longer *asserts* an age it invented. Moving the estimate itself is a CNAM item and stays
  out of scope.
- **AC-19:** The audit journal is reachable: a `/journal` page, `AdminOnly` in `nav.ts`, listing the ledger
  newest-first with the filters `GET /api/audit` already accepts — `entityType`, `entityId`, `from`, `to`,
  `action` (`Insert` | `Update` | `Delete`) — and the standard pager. Deleting a patient and then opening the
  journal shows who did it and when. The endpoint needs **no** backend change; this is a client-side gap.
- **AC-20:** `Clinic.StockExpiryLeadDays` is editable from the stock settings, `0` disables the alert, and
  the value drives `StockExpiryJob`, the dashboard alert and the stock list. A command and endpoint are added
  — the existing domain setter has no caller. `StockController` has no settings route today; the shape to
  follow is `SetRecallSettingsCommand` / `SetBackupScheduleCommand`.
  ⚠️ **`SetStockExpiryLeadDays` must widen to accept `0`.** It currently throws on `days < 1`
  (`Clinic.cs:341-349`), so the documented « mettre à 0 pour désactiver » is unreachable — the existing test
  has to set the property by reflection to test it. Both readers already implement 0-means-off
  (`StockExpiryJob.cs:82`, `DashboardAlertsReader.cs:65`), so the guard is the only thing out of step: it
  becomes `0–365` with `0` = alerte désactivée, and the French range message follows.
- **AC-21:** Entering only an insurance number (or only a provider) stores exactly what was typed; the
  literal `"Unknown"` never reaches storage or the screen. `InsuranceInfo` accepts a one-sided value.
  ⚠️ **Order matters, and the padding is client-side.** `"Unknown"` is written by
  `edit-patient-dialog.tsx:655-657,770-772`, which pads the missing half precisely to get past the value
  object's two `ArgumentException` guards (`InsuranceInfo.cs:14-25`, both messages English). The VO must
  accept a one-sided value **first**: `UpdatePatientCommand.cs:237-241` constructs it unguarded, so removing
  the padding on its own turns a one-sided entry into an unhandled 500. The dialog already carries a note
  (`:949`) that existing `"Unknown"` rows hydrate a Select with no matching option — those rows stay as they
  are; no backfill.
- **AC-22:** Changing **any** filter on a paged list — not only the search box — returns to page 1.
  `usePagedList` takes the caller's filter values and resets on any change; that fixes the three lists on the
  hook (`invoices-table`, `patients-table`, `procedure-types-table`), whose non-search filters currently
  refetch the same page number.
  ⚠️ The other seven lists **hand-rolled their own paging to get this** (`stock-table`, `lab-orders`,
  `cheques-table`, `receivables-table`, `user-management`, `rappels`, `waiting-list`, `recurring-series`) —
  `receivables-table.tsx:49` says so out loud. Each is **verified** to reset on every filter it exposes, and
  any that does not is fixed in place. They are not migrated onto the hook; the point is that no list is
  assumed correct.
- **AC-23:** A bon de prothèse can be attached to the séance it belongs to and appears on the patient's file;
  from a lab order the user can reach the patient and the appointment. The patient half is a link that does
  not exist today — `lab-orders/page.tsx` renders `patientName` as plain text at `:706`, `:744` and `:799`
  while the DTO already carries `patientId`.
- **AC-24:** The agenda offers the legal next statuses directly on an appointment (« Arrivé », « En cours »,
  « Terminé », « Absent ») without opening the edit dialog. `AppointmentDto.AllowedNextStatuses` is already
  served on every read, and `lab-orders/page.tsx:697-740` is the in-repo precedent for an inline
  status dropdown driven by exactly that field. The transition table (`Appointment.cs:144-170`) is the only
  authority — notably `Completed → { Cancelled }` alone.
- **AC-25:** `Appointment.BookedOutsideWorkingHours` is **removed** — the property, its
  `MarkBookedOutsideWorkingHours()` mutator, its four write sites (`CreateAppointmentCommand.cs:240`,
  `CreateRecurringSeriesCommand.cs:294`, `UpdateAppointmentCommand.cs:526`,
  `GoogleCalendarSyncService.cs:783`) and its column. Nothing reads it, and the booking-time refusal already
  tells the user at the moment it matters; surfacing it would mean inventing a badge nobody asked for.
  ⚠️ **`MedicalDocument.AppointmentId` is a different case and the audit's framing of it is wrong** — it *is*
  read, at `CreateMedicalDocumentCommand.cs:290-292` where it drives post-visit-review completion, and it is
  projected onto the DTO at four sites and typed in `web/`. What it lacks is a **UI consumer**, so it is
  surfaced: the patient's documents tab shows which visit produced an ordonnance.
  `WaitingListEntry.ResultingAppointmentId` is genuinely written-and-never-read and is surfaced (a promoted
  entry links to the RDV it became); `WaitingListEntry.Cancel()` / `WaitingListStatus.Cancelled` have zero
  writers and zero readers — the only exit is physical deletion — and are either made reachable or deleted.
- **AC-26:** `ClinicContext.EnsureClinicAccess` is deleted — it and its `ForbiddenAccessException("Access
  denied…")` are the only occurrences in the solution, and `BelongsToClinic` is reachable only through it.
  English user-facing strings **on the paths this feature already touches** are made French: `InsuranceInfo`'s
  two guards (AC-21), the new stock-settings command (AC-20) and the lab-order changes (AC-23).
  ⚠️ **A full English sweep of `api/` is explicitly OUT OF SCOPE.** It is ~90 Application-layer sites (35 ×
  « Unable to resolve current clinic », 48 × `Failure($"Error …ing: {ex.Message}")`) plus 100+ `ArgumentException`
  messages across ~30 domain entities — and the two clusters compound, since a handler splicing `{ex.Message}`
  is how « Invalid tooth number: 99 » reaches a French UI. That is its own feature, captured as a follow-up.

### Data / schema changes
- `LabWorkOrder` — nullable `AppointmentId` with a real FK, clinic-checked on write like every other
  cross-aggregate id.
- `Patient.DateOfBirth` — made nullable end to end (entity, `PatientDto`, `web/lib/api/types.ts`,
  `CreatePatientCommand`, list, fiche, odontogram, any age-derived read). It is non-nullable on all of those
  today; `UpdatePatientCommand` is the only one already `DateTime?`.
- **Drop** `Appointment.BookedOutsideWorkingHours` and its column (AC-25).
- One migration carries all three. ⚠️ It both **adds** a column and **drops** two: the drops must sit *below*
  any backfill in the generated `Up()`, and EF's differ orders by schema dependency, not data safety.

---

## Device Behaviour

- **Leading device:** desk for the journal and la caisse; **phone** for the agenda status actions and the
  fiche's payment fields — those are used chairside.
- **Narrow width (< 640 px):** the journal and the cheques list render as cards, one movement per card, with
  the row action in a single menu; the cheque « marquer comme encaissé » confirmation and the void-motif
  dialog become sheets in `dvh`.
- **Touch:** the agenda's quick-status control is a 44 px tap target on a coarse pointer, not a hover-revealed
  affordance; nothing in Group A or D is reachable only by hover.
- Inherits `~/.claude/skills/DEVICE-CONTRACT.md` in full; `npm run check:responsive` + `npx tsc --noEmit` +
  `npm run build` is the gate, then an eye pass at 320/390/820/1180/1440 px.

---

## Out of Scope

- Relances / recall, phone normalisation, every CNAM item (plafond, dépôt/remboursement d'un bulletin),
  Arabic/RTL, the split working day — owner's explicit exclusions.
- Un-bridging a devis→facture passerelle (Group A decision).
- Changing when la caisse recognises a cheque (Group B decision).
- A patient-merge or soft-delete.
- Any change to the invoice/devis/avoir numbering series.
- **The full English-string sweep of `api/`** (~90 Application sites + 100+ Domain `ArgumentException`
  messages) — AC-26 covers only the dead code and the paths this feature touches. Follow-up feature.
- **Deleting the orphaned e-invoice blobs** from object storage (Group C decision).
- **Repricing a billed fiche.** A changed `Cost` is refused, not carried onto the invoice (AC-3b).
- **Backfilling existing `"Unknown"` insurance rows** or DOBs that happen to be 30 years before creation.

## Edge Cases

- **A-1:** A fiche re-saved with a higher amount whose invoice has since been cancelled or fully credited —
  refuse and name the invoice, never silently create a second document.
- **A-2:** Two users saving the same fiche at once — the existing `xmin` optimistic-concurrency 409 must
  still surface as a French conflict, not be flattened by a new catch.
- **B-1:** A cheque carried across the devis→facture bridge must not be markable twice; the de-dup that
  already governs the cheques list governs the banked stamp.
  ⚠️ That de-dup keys on the **plan id, whole-plan** (`PlanBillingRules.BilledPlanIds`), not on the cheque and
  not on `Payment.SourceInstallmentPaymentId`. So once a plan is bridged its installment rows never reach the
  list at all, and the only stamp the screen can write is on the invoice-side `Payment` — which is why the
  write must be keyed by the row the query actually returned. The carry is **one-way and one-time**
  (`InstallmentPayment.ToChequeDetails()` at issue), so a stamp does not travel back.
- **B-2:** The bridge exclusion is *reversible* — cancel the bridge invoice and the plan-side rows reappear,
  without the stamp written on the invoice side. This is closed by Group A rather than by new code: a bridge
  invoice that carried a non-voided payment **cannot be cancelled**, and if its payments were voided first
  then AC-12 has already removed the cheque from every view. The plan must confirm this holds rather than
  assume it.
- **C-1:** A database holding invoices in `Valid` or `Submitted` — the migration drops the state, and
  `CanCancel` widens accordingly. Those invoices become cancellable, which is the intended outcome.
- **D-1:** An existing patient whose DOB is exactly 30 years before their creation date cannot be told apart
  from a fabricated one; the backfill is deliberately **not** attempted, and only new records are honest.
- **D-2:** A patient with no DOB reaching `PatientDuplicateIndex` — the name+DOB rule cannot apply, so the
  name-alone rule already written for « no DOB supplied » is the one that fires. Making the entry's `DateTime`
  nullable must not silently widen or narrow which rows match.
- **D-3:** A lab order whose linked appointment is later deleted or moved to another patient — the FK is
  nullable and clinic-checked on write, so the link is cleared rather than left dangling.
- **C-2:** An install upgrading with a `dispatch-einvoices` row already in Hangfire storage — removed by
  `RemoveIfExists`, not merely unregistered (AC-16b).
