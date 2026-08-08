# Story 1: [Full] Close the adoption gaps — the till tells the truth, cheques have a life-cycle, El Fatoora is gone

**Status:** APPROVED
**Story Status:** in-progress (Parts 1 and 2 of 4 implemented)
**Layer:** Full — *deliberate departure from the BE/FE separation rule; see "Structure" below*
**Depends On:** None
**Blocks:** None

## Objective

A clinic can trust the money it sees. A re-saved fiche de soins never silently drops a payment; a cheque has
a banked state distinct from a voided one; la caisse's day is the **Tunisian** day rather than the browser's;
the TTN « El Fatoora » e-invoicing subsystem no longer blocks a cancellation or haunts the UI; and the
remaining small defects — a fabricated date of birth, an unreachable audit journal, an uneditable stock lead
time, an `"Unknown"` insurance row — are closed.

## Structure — four ordered parts, one per session

> This story is **`Layer: Full`** and spans DB, Domain, Application, API, `web/`, docs and tests. That is a
> deliberate owner decision recorded as **R-1** in the plan, not an oversight: *"The owner chose one story
> explicitly against the sizing recommendation. Recorded, not re-litigated."*

Steps are grouped into **four ordered parts**, each a vertical increment that builds, passes its own
validation and **commits on its own**. Land them in order. **Implement one part per session** — the part
boundary is the split point, and each part carries its own entry criteria, steps, validation and commit.

| Part | Group | Name | Migration | Status |
|------|-------|------|-----------|--------|
| 1 | C | Remove El Fatoora / TTN | `RemoveEInvoicing` (**irreversible**) | **implemented** |
| 2 | A | Money integrity | `AddDentalRecordPaymentMethod` | **implemented** |
| 3 | B | Cheque life-cycle | `AddChequeBankedStamp` | not-started |
| 4 | D | Remaining defects | `NullableDobLabOrderAppointment` | not-started |

**Only Part 1 must precede Part 2** (`Invoice.cs` carries 102 TTN references in the exact files Group A
rewrites). Parts 2, 3 and 4 are independent of each other.

## Acceptance Criteria

_From spec:_

**Part 1 — Group C (remove El Fatoora / TTN)**
- [ ] **AC-13:** No route, screen, badge, setting or background job mentions TTN, El Fatoora, TEIF or
      e-facturation. Two greps (see Verification) — the broad one excludes `Migrations/` wholesale, so the
      snapshot needs its own.
- [ ] **AC-14:** An issued invoice can be cancelled purely on its fiscal rules; no e-invoice state can block a
      cancellation. `CanCancel` loses three of its five terms; `Cancel()` loses the « déclarée à El Fatoora »
      throw plus the dequeue block. Deletion is already unaffected — no change owed there.
- [ ] **AC-15:** `GET /api/outbox` reports **two** queues (rappels, e-mails de documents); `OutboxDepthDto`
      loses `EInvoices` (a `required` init prop ⇒ compile-checked), along with `EInvoiceOutboxDepthDto`, the
      `EInvoiceOutboxDepth` domain record and both `IInvoiceRepository` outbox methods.
- [ ] **AC-16:** The unit suite builds and passes with the **7** TTN/TEIF-dedicated test classes deleted and
      the **26** further referencing test files updated. `QrCodeGeneratorTests` **survives** — only its `ttn=`
      payload literal changes.
- [ ] **AC-16b:** The Hangfire recurring job is both unregistered **and** actively removed via
      `RecurringJob.RemoveIfExists("dispatch-einvoices")`.
- [ ] **AC-16c:** The four background jobs whose XML docs `cref` the deleted `EInvoiceOutboxJob` are updated —
      a `cref` to a removed type breaks the 0-warning gate.
- [ ] **AC-17:** `dotnet run -- verify-schema` passes against a database migrated forward, and its
      `ttn-identity-is-complete` check no longer exists.
- [ ] **C-1:** A database holding invoices in `Valid`/`Submitted` — those become cancellable (intended).
- [ ] **C-2:** An install upgrading with a `dispatch-einvoices` row already in Hangfire storage leaves no job.

**Part 2 — Group A (money integrity)**
- [ ] **AC-1:** Fiche 400,000 DT with 200,000 payé, billed and collected at 200,000. Editing « Montant payé »
      to 400,000 leaves **one** document with 400,000 collected; « Encaissé » of the day, the patient's solde
      and the dashboard all move by 200,000.
- [ ] **AC-2:** Lowering « Montant payé » on a billed fiche is refused in French naming the avoir; no payment
      row is written or altered.
- [ ] **AC-3:** Every outcome of the auto-billing attempt is surfaced. `AlreadyBilled` with nothing to add is
      an informational toast, not a plain green success. The outcome is carried by a **typed result**; the
      French substring match at `DentalRecordAutoBilling.cs:86` is **deleted**.
- [ ] **AC-3b:** Changing the acts of an already-billed fiche — and so its `Cost` — is refused in French,
      naming the note d'honoraires and the avoir. No invoice line is added, removed or repriced after issue.
- [ ] **AC-4:** A fiche settled by chèque produces a payment with `Method = Cheque` and its
      number/banque/échéance; it appears in « Chèques à encaisser » and under « dont chèques », **not** under
      « dont espèces ».
- [ ] **AC-5:** An échéancier payment can be voided from the plan workspace with a required motif — identical
      behaviour to voiding an invoice payment, including « REÇU ANNULÉ » on reprint.
- [ ] **AC-6:** With the workstation clock in any timezone, opening la caisse on a given day returns exactly
      the movements of that **Tunisian** day. A 00:00 payment appears in exactly one day's caisse.
- [ ] **AC-7:** `Σ(extrait de caisse) == cashIn − refunds − cashOut == net` holds at a period boundary that has
      an expense dated on the **last day** of the window.
- [ ] **A-1:** A fiche re-saved higher whose invoice has since been cancelled or fully credited — refuse and
      name the invoice, never silently create a second document.
- [ ] **A-2:** Two users saving the same fiche at once — the `xmin` 409 still surfaces as a French conflict.

**Part 3 — Group B (cheque life-cycle)**
- [ ] **AC-8:** Marking a cheque encaissé stamps a banked date, an actor and a moment; the row leaves the
      default « à encaisser » view and is reachable under an « Encaissés » filter showing when and by whom.
- [ ] **AC-9:** Marking a cheque encaissé changes **no** figure in la caisse, the dashboard, the patient's
      solde or the invoice — a tracking state, not a money movement. **Pinned by a test.**
- [ ] **AC-10:** Un-marking is possible and audited (a cheque returned unpaid by the bank is the real case).
- [ ] **AC-11:** The four bucket counts (en retard / bientôt / plus tard / sans date) count **outstanding**
      cheques only.
- [ ] **AC-12:** Voiding the underlying payment removes the cheque from every view regardless of banked state.
- [ ] **B-1:** A cheque carried across the devis→facture bridge must not be markable twice.
- [ ] **B-2:** The bridge exclusion's reversibility is **confirmed by test, not assumed**.

**Part 4 — Group D (remaining defects)**
- [ ] **AC-18:** A walk-in created from the appointment dialog's inline « Nouveau patient » form stores **no**
      DOB rather than a fabricated one; « âge inconnu » in the list, on the fiche and in the summary, and the
      odontogram **asks** which dentition rather than assuming adult.
- [ ] **AC-19:** The audit journal is reachable: an `AdminOnly` `/journal` page over the **unchanged**
      `GET /api/audit`, newest-first, with `entityType`/`entityId`/`from`/`to`/`action` and the standard pager.
- [ ] **AC-20:** `Clinic.StockExpiryLeadDays` is editable from the stock settings, `0` disables the alert, and
      the value drives `StockExpiryJob`, the dashboard alert and the stock list.
- [ ] **AC-21:** Entering only an insurance number (or only a provider) stores exactly what was typed; the
      literal `"Unknown"` never reaches storage or the screen.
- [ ] **AC-22:** Changing **any** filter on a paged list — not only the search box — returns to page 1.
- [ ] **AC-23:** A bon de prothèse can be attached to the séance it belongs to and appears on the patient's
      file; from a lab order the user can reach the patient **and** the appointment.
- [ ] **AC-24:** The agenda offers the legal next statuses inline (« Arrivé », « En cours », « Terminé »,
      « Absent ») without opening the edit dialog, driven by `AppointmentDto.AllowedNextStatuses`.
- [ ] **AC-25:** `Appointment.BookedOutsideWorkingHours` is **removed** (property, mutator, four write sites,
      column). `MedicalDocument.AppointmentId` and `WaitingListEntry.ResultingAppointmentId` are **surfaced**;
      `WaitingListEntry.Cancel()` is made **reachable**.
- [ ] **AC-26:** `ClinicContext.EnsureClinicAccess` + `BelongsToClinic` are deleted (keeping
      `ForbiddenAccessException`). English strings made French **only on the paths this feature touches**.
- [ ] **D-1 / D-2 / D-3:** No DOB backfill; a null DOB neither widens nor narrows duplicate matching; a lab
      order's appointment link is cleared rather than left dangling.

_Story-specific:_

- [ ] Each part builds clean with **0 errors and 0 warnings**, passes the unit suite, and commits on its own.
- [ ] Each of the four migrations is **scaffolded, never hand-written**, inspected, and committed **with its
      regenerated model snapshot** before the next part starts (R-3).
- [ ] `dotnet run -- verify-schema` is run **before and after** every migration and the output diffed (R-3).
- [ ] `dotnet run -- reconcile-money` is run before and after **Part 2** and diffed — it is the only thing that
      can prove the top-up moved no closed month and created no duplicate document.
- [ ] Every new returning catch carries `when (ex is not ConflictException)` (R-5).
- [ ] The five-width eye pass runs at the end of **each** part that touched `web/`, not once at the end (R-8).

## Entry Criteria

Before starting **any** part:

- [ ] `plan.md` and `spec.md` are both **APPROVED** (they are) and have been read — the plan's per-part step
      lists and file tables are the authoritative detail; this story file is the index over them.
- [ ] On branch `feature/windows-desktop-app` (owner's decision — this feature lands there, not on a new
      branch).
- [ ] The branch's **~25 pre-existing dirty files** (the security-review batch: `SECURITY_REVIEW_2026-08.md`,
      `AccountStateMiddleware.cs`, `OutboundEndpointPolicy.cs`, `EffectiveRole.cs`, the reminder-settings
      changes) are recorded in `progress.md` and **excluded from every commit**. Stage by explicit path;
      never `git add -A` (R-13).
- [ ] `docker compose up -d` — postgres reachable; the database is migrated to `HEAD`.
- [ ] No `dotnet run` API and no `next dev` holding `api/**/bin` or `web/.next` (both produce build failures
      that read as code errors).
- [ ] A database **backup** exists before Part 1's migration — its `Down()` throws by design (R-2).

Per-part entry criteria are listed with each part below.

## Steps

> Each part's steps are the plan's own, condensed. **Read the matching section of
> [`../plan.md`](../plan.md) before starting a part** — it carries the line numbers, the file tables and the
> traps, and this story does not restate them.

---

### Part 1 — Remove El Fatoora / TTN (AC-13…AC-17, C-1, C-2)

**Entry:** database backed up; `verify-schema` run and output captured.

1. **Delete the subsystem.** The 8 Infrastructure services, the 6 Application seams, `EInvoiceModels`,
   `TtnIdentityUnavailableException`, `SubmitInvoiceToElFatooraCommand`, `GetEInvoiceArtifactQuery`,
   `EInvoiceOutboxJob`, `Domain/Enums/EInvoiceStatus.cs`.
   ⚠️ **Keep `IQrCodeGenerator`/`QrCodeGenerator`** — `TrustController.cs:131-138` renders the LAN trust
   page's QR from it. Only `QrCodeGeneratorTests`' `ttn=` payload literal changes.
2. **Strip `Invoice.cs`** — 8 e-invoice properties, the 8 `MarkEInvoice*`/`CopyEInvoiceStateFrom` members,
   `CanCancel`'s three e-invoice terms, `Cancel()`'s « déclarée à El Fatoora » throw and the dequeue block
   (AC-14). Leave `CanBeDeleted` and `DeleteInvoiceCommand` alone — neither carries an e-invoice term.
3. **Strip `Clinic.cs`** — `TtnEInvoicingEnabled`, `TtnEnvironment`, the four `Ttn*` identity columns,
   `SetTtnIdentity` and the e-invoicing setter.
4. **Outbox down to two queues (AC-15).** Drop both `IInvoiceRepository` outbox methods, the
   `EInvoiceOutboxDepth` record and their implementations; `GetOutboxDepthQuery` reports two;
   `OutboxDepthDto` loses `EInvoices` (`required` ⇒ the compiler finds every site).
5. **DI + capability.** Drop the 7 registrations at `Extensions.cs:347-354` and
   `DeploymentProfile.SharesInstallWideTtnIdentity` **together with** its `DeploymentProfileTests.ExpectedMatrix:59`
   row — the matrix is reflected over, so there is **no hard-coded 15** to update.
6. **Hangfire (AC-16b, C-2).** Delete the `dispatch-einvoices` `AddOrUpdate` (~`Program.cs:688-694`) **and add**
   `RecurringJob.RemoveIfExists("dispatch-einvoices")` beside the `sync-google-calendar` precedent (~`:752`).
7. **Repair the four `cref`s** (`StockExpiryJob:22`, `BackupJob:17`, `DocumentEmailJob:17`,
   `NotificationJob:84`) — a `cref` to a removed type breaks the 0-warning gate (AC-16c).
8. **PDF surfaces.** Remove the invoice cachet block and the avoir's TTN warning
   (`PdfGenerationService.cs:712-718`) with its `AvoirPdfData.CorrectedInvoiceIsTtnRegistered` ← `CreditNoteDto`
   feed and the banner at `invoice-detail-modal.tsx:453-458`. **Nothing renders an empty placeholder** where
   the QR was.
9. **Audit + schema verification.** Drop `"EInvoiceStatus"` from `AuditSaveChangesInterceptor.cs:78`; drop
   `ttn-identity-is-complete`, its `SchemaVerificationReader` block and the `ClinicsWithPartialTtnIdentity`
   field (AC-17).
10. **`web/`** — remove the El Fatoora settings section, the submit action, the status badges, the artifact
    downloads and the types.
11. **Scaffold `RemoveEInvoicing`** — do **not** hand-write it; the regenerated
    `ApplicationDbContextModelSnapshot.cs` is what makes AC-13's second grep pass. `Down()` throws
    `NotSupportedException` with a French sentence naming the export-before-migrating requirement. Blobs are
    left in object storage by decision. ⚠️ **No migration in this repo has a throwing `Down()`** — this is a
    new pattern, not one to copy from.
12. **Tests (AC-16).** Delete the 7 TTN-dedicated classes; update the 26 referencing ones.
13. **Docs.** The **6** `CLAUDE.md` files (the spec says five — `web/lib/CLAUDE.md` is the omitted one),
    `packaging/README.md`, `packaging/server/clinic-server.iss`, `deploy/README.md`. `features/**` untouched.
14. **Retire `follow-up/ttn-per-clinic-identity-write-path.md`** and its index entry — it asks for a write path
    onto columns this part deletes.

**Part 1 validation:**
- [ ] `grep -riE "ttn|fatoora|teif|einvoice" api/ web/ --exclude-dir=Migrations` returns nothing
- [ ] `grep -riE "ttn|fatoora|teif|einvoice" api/ClinicManagement.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
      returns nothing — **two greps, not one** (R-6: the snapshot is inside `Migrations/`, which the first
      command excludes wholesale, so a snapshot still carrying all 15 hits passes it silently)
- [ ] `dotnet build --no-incremental` clean, **0 warnings** (proves AC-16c)
- [ ] Unit suite green with 7 classes deleted; `DeploymentProfileTests`' four tests still pass
- [ ] An invoice in the old `Valid`/`Submitted` state is cancellable after migrating (AC-14, C-1)
- [ ] `GET /api/outbox` returns two queues (AC-15)
- [ ] `dotnet run -- verify-schema` passes with no `ttn-identity-is-complete` (AC-17)
- [ ] Restarting an install with a `dispatch-einvoices` row leaves no recurring job (AC-16b, C-2)

---

### Part 2 — Money integrity (AC-1…AC-7, A-1, A-2)

**Entry:** Part 1 committed with its snapshot; `reconcile-money` run and output captured.

1. **Fiche payment fields.** Add `PaymentMethod` + `ChequeNumber`/`ChequeBankName`/`ChequeDueDate` to
   `DentalRecord`, guarded by the **existing** `ChequeDetails.For` (**no new guard**); map them; scaffold
   `AddDentalRecordPaymentMethod`. Null method = Cash for historical rows **by read-side convention, not by
   backfill**.
2. **Rename → `BillDentalRecordCommand`** (same namespace ⇒ the realtime `invoices` key is unaffected) and
   change its return to `Result<DentalRecordBillingResult>`. `InvoicesController` unwraps `.Invoice`, so
   `POST /api/invoices/from-dental-record/{id}` keeps its body and the manual dialog needs no contract change.
3. **⚠️ The AC-2 / AC-3b refusal lives in `UpdateDentalRecordCommand`, PRE-commit — not in the billing
   command.** `DentalRecordAutoBilling` runs **post-commit** by design (`:19-21`, `:106`), so a refusal raised
   there arrives *after* the lowered amount or changed `Cost` has been saved — the user sees a French message
   and the edit sticks anyway, leaving the fiche permanently disagreeing with its own note d'honoraires.
   « Refusé » has to mean the save did not happen. So `UpdateDentalRecordCommand` loads the fiche's existing
   invoice link **before** `SaveChangesAsync` (one extra read) and returns `Result.Failure` + `Code` when, on a
   fiche whose invoice is issued and not cancelled, « Montant payé » is **lower** (name the avoir) or the acts
   and therefore `Cost` **changed** (name the note d'honoraires and the avoir). `CreateDentalRecordCommand`
   needs no such guard. `BillDentalRecordCommand` implements the same two refusals as typed outcomes — it is
   also the **manual** path — and **the wording is written once and shared**, so the two cannot drift.
4. **Teach the already-billed branch**, replacing the hard refusal: **higher** `AmountPaid` → additional
   `Payment` on the existing invoice, re-running the over-payment check against the **frozen** TTC →
   `ToppedUp` (AC-1); **lower** → refuse (AC-2); **changed `Cost`** → refuse (AC-3b); **cancelled or fully
   credited** → refuse and name the invoice, never a second document (A-1); nothing to add → `AlreadyBilled`.
   Every refusal sets `Result.Code`. **Every new catch carries `when (ex is not ConflictException)`** (A-2, R-5).
5. **`DentalRecordAutoBilling`.** Delete the `Contains("déjà facturée")` match at `:86`, switch on the typed
   outcome, and pass the fiche's `PaymentMethod` + cheque details instead of the hard-coded `Cash` at `:64`
   (AC-4).
6. **Surface every outcome** in `patient-record-modal.tsx:505-527` — `AlreadyBilled` is an **informational**
   toast, not plain green (AC-3).
7. **Fiche form payment fields**, reusing `factures/cheque-fields.tsx` + `chequePaymentFields()` —
   **phone-leading**, since this is chairside.
8. **`CaissePeriod`.** Create it; move both caisse handlers' byte-identical bound logic into it and add
   `FromDay`/`ToDay` (bare `YYYY-MM-DD`, resolved through `ClinicClock.LocalDayRangeUtc` /
   `LastTickOfLocalDayUtc`). Keep `From`/`To` instants for the other callers. `BillingController` binds day
   keys on summary, ledger **and** export.
9. **`web/app/caisse/page.tsx`** sends day keys; delete `rangeBounds` (`:119-124`); move
   `dashboard-links.ts`'s `/caisse` range links with it (AC-6).
10. **`ExpenseRepository.cs:41,64`:** `< to` → `<= to`, matching the three sibling ledgers (AC-7).
11. **Plan-workspace void affordance** for an installment payment against the **existing**
    `POST /api/treatment-plans/{id}/installments/{installmentId}/payments/{paymentId}/void` — required motif,
    struck-through row with motif and actor, « REÇU ANNULÉ » on reprint. Follow `invoice-detail-modal.tsx`'s
    **in-place panel**, not a nested dialog (AC-5).
12. **Correct the bridge comment in all three places** (`IssueInvoiceCommand.cs:181-187`,
    `TreatmentPlanRepository.cs:217-218`, `PlanBillingRules.cs:31-33`) and make the refusal name the avoir as
    the only route.

**Part 2 validation:**
- [ ] Fiche 400,000 / 200,000 payé → edit to 400,000 → **one** invoice, 400,000 collected; « Encaissé », the
      patient's solde and the dashboard each move by 200,000 (AC-1)
- [ ] Lowering the amount refuses in French **and the fiche is unchanged after the refusal** (re-open it: the
      old amount is still there); no payment row written or altered (AC-2)
- [ ] Adding an act to a billed fiche refuses in French **and the act is not persisted** (AC-3b)
- [ ] `grep "déjà facturée" DentalRecordAutoBilling.cs` returns nothing (AC-3)
- [ ] A fiche settled by chèque produces `Method = Cheque` with number/banque/échéance, appears in « Chèques à
      encaisser » and under « dont chèques », **not** « dont espèces » (AC-4)
- [ ] Voiding an échéancier payment behaves identically to voiding an invoice payment (AC-5)
- [ ] With the workstation clock in another timezone, la caisse returns exactly that Tunisian day; a 00:00
      payment appears in exactly one day (AC-6)
- [ ] `MoneyReadConsistencyTests` holds `Σ(extrait) == cashIn − refunds − cashOut == net` with an expense on
      the window's **last day** (AC-7)
- [ ] Two concurrent fiche saves still yield a French 409 (A-2)
- [ ] `reconcile-money` diffed before/after: no closed month moved, no duplicate document

---

### Part 3 — Cheque life-cycle (AC-8…AC-12, B-1, B-2)

**Entry:** `verify-schema` output captured (the new check must report **« not applicable »** *before* the
migration — never a reassuring `0`).

1. **Banked stamp.** Add `ChequeBankedOn` + `ChequeBankedByUserId` to `Payment` and `InstallmentPayment` with
   an `internal SetBanked(...)` **refusing a non-cheque method**; add the aggregate-root entry points on
   `VoidPayment`'s pattern. Scaffold `AddChequeBankedStamp`. Both null for every existing row.
   ⚠️ The neighbours are named `VoidedAt`, **not** `VoidedOn` — follow the real neighbours.
2. **Two routes, `AdminOrDoctor`, body `{ banked: bool }`**, mirroring the void routes one for one:
   `POST /api/invoices/{id}/payments/{paymentId}/banked` and
   `POST /api/treatment-plans/{id}/installments/{installmentId}/payments/{paymentId}/banked`.
   ⚠️ **Not** a single payment-id route — an `InstallmentPayment` is only addressable as
   `{plan, installment, payment}`, which is exactly the shape `VoidInstallmentPaymentCommand` already takes.
3. **`ChequeDto` gains `InstallmentId` and the owning aggregate id** (it carries **neither** today — `Id` is
   the payment row, `TargetId` is the aggregate root), and `CaisseInstallmentPaymentRow` gains `InstallmentId`.
4. **`GetChequesDueQuery`** defaults to **outstanding**, accepts a `banked` filter, and computes the four
   bucket counts over **outstanding only** (AC-11). Un-marking is supported and lands in the audit ledger
   (AC-10).
5. **The write is keyed by the row the query returned (B-1).** The bridged-plan de-dup keys on the *plan*,
   whole plan (`PlanBillingRules.BilledPlanIds`) — not on the cheque and not on
   `Payment.SourceInstallmentPaymentId` — so once a plan is bridged only the invoice-side `Payment` is
   reachable. The carry is one-way and one-time (`InstallmentPayment.ToChequeDetails()` at issue), so a stamp
   cannot travel back and a cheque cannot be marked twice.
6. **Confirm B-2 rather than assume it (R-9).** A bridge invoice carrying a non-voided payment cannot be
   cancelled (Part 2 step 12 makes the refusal say so), and if its payments were voided first, AC-12 has
   already removed the cheque from every view. **Pin it with a test.**
7. **`cheque-banked-only-on-cheques` — all four parts**, on `cheque-details-only-on-cheques`'s pattern
   (`SchemaVerificationService.cs:362-374`): the `DataMigrationCounts` field, the `ScalarOrNullAsync` block
   **guarded on the new column** (so a pre-migration DB reports « not applicable »), the `Add(...)` line, and
   the clean **and** not-applicable cases in `SchemaVerificationServiceTests`.
8. **`cheques-table.tsx` / `/cheques`:** « Encaissés » filter showing when and by whom, the mark/un-mark
   action, **cards below 640 px**, the confirmation as a **sheet in `dvh`**.

**Part 3 validation:**
- [ ] Marking encaissé stamps date + actor + moment; the row leaves the default view and is found under
      « Encaissés » (AC-8)
- [ ] A test pins that marking changes **no** figure in la caisse, the dashboard, the patient's solde or the
      invoice (AC-9)
- [ ] Un-marking works and appears in `GET /api/audit` (AC-10)
- [ ] The four buckets count outstanding cheques only (AC-11)
- [ ] Voiding the underlying payment removes the cheque from every view, banked or not (AC-12)
- [ ] A bridged cheque cannot be marked twice (B-1); B-2's reversibility is covered **by test, not assumption**
- [ ] `verify-schema` reports the new check **clean** after and **« not applicable »** before

---

### Part 4 — Remaining defects (AC-18…AC-26, D-1, D-2, D-3)

**Entry:** none beyond the global list; independent of Parts 2 and 3.

1. **Nullable DOB (AC-18).** Delete `PatientFromRequest.cs:85-87`'s `UtcNow.AddYears(-30)` substitution, then
   **follow the compiler**: `Patient`, `PatientDto`, `CreatePatientCommand`, `PatientMappingExtensions`,
   `PatientIdentity`, `PatientRepository`, the import reader/planner's `?? default`, `ExportTables:42`, and
   `CreateMedicalDocumentCommand.cs:159` (`!= default` → `.HasValue`).
   ⚠️ **The two real breaks:** `DentitionRules.FromDateOfBirth` (return « ask which dentition » for null — the
   odontogram decision the substitution existed for) and `PatientDuplicateIndex.Entry`, a `record struct` whose
   `DateTime` becomes nullable **without widening or narrowing which rows match** — the name-alone rule already
   written for « no DOB supplied » is the one that fires (D-2, R-4).
   `CnamReimbursementCalculator.RateForPatient` already takes `DateTime?` and needs **no** change. No backfill
   (D-1). Client: widen `types.ts:515` to `dateOfBirth?: string | null`; all four age helpers already guard on
   a falsy value; list, fiche and summary show « âge inconnu ».
2. **Journal (AC-19).** `/journal` page + `web/lib/api/audit.ts` over the **unchanged** `GET /api/audit`, with
   `entityType`/`entityId`/`from`/`to`/`action` and the standard pager, newest-first; nav entry inside
   `buildConfigItems`'s admin branch **plus** the page's own `role === "admin"` gate, copying
   `/cnam-nomenclature`. **Cards below 640 px**, one entry per card.
3. **Stock lead days (AC-20).** Widen `Clinic.cs:341-349` from `1–365` to **`0–365`** with `0` = alerte
   désactivée and a **French** range message — both readers (`StockExpiryJob.cs:82`,
   `DashboardAlertsReader.cs:65`) already implement 0-means-off, so the guard is the only thing out of step.
   Add `SetStockExpirySettingsCommand` + an `AdminOnly` `StockController` route on `RecallController.cs:61`'s
   shape, and the stock-settings control. This gives the existing domain setter its **first caller**.
4. **Insurance (AC-21) — in this exact order.** First make `InsuranceInfo` (`:14-25`) accept a **one-sided**
   value with **French** messages; *then* relax the **create** path's own guard; *then* delete
   `edit-patient-dialog.tsx:655-657,770-772`'s `"Unknown"` padding. `UpdatePatientCommand.cs:237-241`
   constructs the VO **unguarded**, so the reverse order turns a one-sided entry into a **500**.
   ⚠️ **There are three guards, not two, and the third is on the create path.** `PatientFromRequest.cs:73-75`
   builds the VO only when Provider **and** PolicyNumber are both non-blank, otherwise leaving it `null` — a
   **silent drop**, not a refusal. All three creation doors route through it, so without this the AC passes on
   « Modifier » and fails on « Nouveau patient » with no message at all. It becomes « build it if **either**
   side is present ». Verify `PatientImportRowReader.cs:193` likewise carries a one-sided row through — a
   silent drop on a 3 000-row import is unrecoverable without re-importing. **No backfill.**
5. **Pager reset (AC-22).** `usePagedList` takes `filters?: readonly unknown[]` and resets page on any change,
   still debouncing only `search`; wire the three consumers (`invoices-table`, `patients-table`,
   `procedure-types-table`).
   ⚠️ **The reset keys on `JSON.stringify(filters ?? [])`, never on the array's identity.** Callers pass an
   inline literal, which is a **new reference every render** (a `useCallback` dep list is spread by React; an
   array *prop* is not), so an identity-keyed effect fires every render and `setPage(1)` would **undo the
   user's own page click** — breaking paging on all three consumers rather than fixing their filters. Document
   in the hook's doc comment that filter values must be **JSON-serialisable primitives**.
   Then **verify each of the eight hand-rolled lists** resets on every filter it exposes and fix any that does
   not — exploration found all eight currently correct, so this is **confirmation, but no list is assumed**.
6. **Lab orders (AC-23, D-3).** Nullable `LabWorkOrder.AppointmentId` with a real FK, validated clinic- **and**
   patient-side on `CreateInvoiceCommand.cs:87-100`'s pattern; French messages.
   `lab-orders/page.tsx:706,744,799` render `patientName` as a **link**; the order links to its appointment;
   the bon appears on the patient's file.
7. **Agenda quick status (AC-24).** An inline control driven by `AppointmentDto.AllowedNextStatuses` (already
   served at all four read/write sites), copying `lab-orders/page.tsx:716-731`. `Appointment.cs:144-170` is the
   **only** authority — notably `Completed → { Cancelled }` alone. **44 px on a coarse pointer, never
   hover-revealed.**
8. **AC-25.** Remove `Appointment.BookedOutsideWorkingHours`, `MarkBookedOutsideWorkingHours()`, its four write
   sites (`CreateAppointmentCommand.cs:240`, `CreateRecurringSeriesCommand.cs:294`,
   `UpdateAppointmentCommand.cs:526`, `GoogleCalendarSyncService.cs:783`) and its column — **zero readers,
   confirmed**. Surface `MedicalDocument.AppointmentId` in the patient's documents tab (it **is** read, at
   `CreateMedicalDocumentCommand.cs:290-292`; it only lacked a UI consumer). Surface
   `WaitingListEntry.ResultingAppointmentId` as a link to the RDV it became, and make `Cancel()` /
   `WaitingListStatus.Cancelled` reachable with a « Retirer » action, default view hiding cancelled entries.
9. **AC-26.** Delete `ClinicContext.EnsureClinicAccess` + `BelongsToClinic` and their interface declarations.
   ⚠️ **Keep `ForbiddenAccessException`** — `ExceptionMiddleware`'s 403 mapping uses it. French strings **only**
   on paths this part already touches; the ~90-site sweep stays out of scope as a follow-up.
10. **Scaffold ONE migration** for the three schema changes. ⚠️ **It both adds and drops** — the drops must sit
    **below** any backfill in the generated `Up()`; EF's differ orders by **schema dependency, not data
    safety**. Read the generated `Up()` line by line and reorder (R-12).

**Part 4 validation:**
- [ ] A walk-in from the appointment dialog's inline form stores **no** DOB; list, fiche and summary show
      « âge inconnu »; the odontogram **asks** which dentition (AC-18)
- [ ] `/journal` is `AdminOnly`; deleting a patient then opening it shows **who** and **when** (AC-19)
- [ ] Stock lead days editable; `0` disables the alert in the job, the dashboard **and** the list (AC-20)
- [ ] Entering only an insurance number stores exactly that on **« Nouveau patient » as well as « Modifier »**
      (and survives a CSV import row with only one side); `"Unknown"` never reaches storage or screen (AC-21)
- [ ] Changing any filter on all **eleven** paged lists returns to page 1 (AC-22) — **and** paging to page 2
      with the filters untouched **stays** on page 2 (the identity-vs-signature trap)
- [ ] A bon de prothèse attaches to a séance, shows on the patient's file, and links both ways (AC-23)
- [ ] The agenda offers only the legal next statuses inline, 44 px on touch (AC-24)
- [ ] `grep -r "BookedOutsideWorkingHours" api/ --exclude-dir=Migrations` returns nothing (AC-25)
- [ ] `grep -r "EnsureClinicAccess\|BelongsToClinic" api/` returns nothing (AC-26)

## Files to Create/Modify

~100 files. The **authoritative, per-part tables** live in
[`../plan.md` § "Files to Modify/Create"](../plan.md) — they carry the line numbers and the per-file notes and
are not restated here. Summary of scale:

| Part | Create | Delete | Modify |
|------|--------|--------|--------|
| 1 | 1 (the migration) | ~24 (8 services · 6 seams · 4 misc · 7 test classes + 1 follow-up doc) | ~30 (incl. 26 test files, 6 `CLAUDE.md`, `packaging/`, `deploy/`) |
| 2 | 7 (`BillDentalRecordCommand`, `DentalRecordBillingResult`, `CaissePeriod`, migration, 2 test classes, `void-installment-payment.tsx`) | 0 | ~14 |
| 3 | 4 (2 commands, migration, `ChequeBankedStampTests`) | 0 | ~10 |
| 4 | 6 (2 commands, migration, `/journal` page, `audit.ts`, `NullableDateOfBirthTests`) | 0 | ~22 |

## Verification Steps

Run at the **end of each part** (R-8 — not once at the end).

**Verification commands:**
```bash
# Backend — full compile; an incremental build reports "0 warnings" by recompiling nothing
cd api && dotnet build --no-incremental

# Backend unit suite — build OUTSIDE the repo; Smart App Control blocks freshly-built in-repo assemblies (R-7)
cd api && dotnet test -p:BaseOutputPath="$TEMP/cm-tests/"

# Schema — before AND after every migration, then diff the two outputs (R-3)
cd api/ClinicManagement.API && dotnet run -- verify-schema

# Money — before and after Part 2 only, then diff (proves no closed month moved, no duplicate document)
cd api/ClinicManagement.API && dotnet run -- reconcile-money

# Frontend gate — this project has no test runner and no working ESLint, so THIS is the whole gate
cd web && npx tsc --noEmit
cd web && npm run check:responsive
cd web && npm run build

# AC-13's two greps (Part 1) — the second is NOT redundant (R-6)
grep -riE "ttn|fatoora|teif|einvoice" api/ web/ --exclude-dir=Migrations
grep -riE "ttn|fatoora|teif|einvoice" api/ClinicManagement.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs
```

**Device eye pass** (no runner exists for this) — at **320 / 390 / 820 / 1180 / 1440 px** plus a landscape
phone (~380 px of height), on every screen the part touched. Name the widths checked in `progress.md`.
Part 3: `/cheques`. Part 4: `/journal`, the agenda quick-status control, the eleven paged lists. Part 2: the
fiche payment fields and `/caisse`. **The fiche fields and the agenda control on a coarse pointer
specifically** (AC-24's 44 px).

**Manual verification** (no runner):
- La caisse with the workstation clock set to **UTC−5 and to UTC+8** (AC-6).
- The fiche re-save flow end to end, watching « Encaissé », the patient's solde and the dashboard (AC-1).

## Exit Criteria

This story is complete when:

- [ ] All four parts are implemented, validated and **committed separately**
- [ ] All 30 spec ACs (AC-1…AC-26 incl. AC-3b/16b/16c, and edge cases A-1/A-2, B-1/B-2, C-1/C-2, D-1/D-2/D-3)
      are satisfied
- [ ] `dotnet build --no-incremental` is clean with **0 errors and 0 warnings** in changed files
- [ ] The unit suite is green, including the new test classes named in the plan's Testing Strategy
- [ ] `verify-schema` passes: `cheque-banked-only-on-cheques` present and clean, `ttn-identity-is-complete`
      gone, and `cheque-details-only-on-cheques` / `appointment-act-rows` /
      `clinical-child-clinic-matches-patient` still clean
- [ ] `reconcile-money` diffs clean across Part 2
- [ ] `npx tsc --noEmit` + `npm run check:responsive` + `npm run build` all clean
- [ ] The five-width eye pass is done and its widths are **named** in `progress.md`
- [ ] The branch's pre-existing dirty files are **still uncommitted and untouched**
- [ ] All verification steps pass

## Notes

**Traps that have already cost time in this repo — read before starting:**

- **R-3 / migrations.** Four migrations in one story. Scaffold, inspect and commit each **with its snapshot**
  before starting the next part. Use `-p:BaseOutputPath=<temp>`; **never** `--no-build`. An uncommitted model
  snapshot makes the next `migrations add` re-emit the previous migration's changes.
- **R-13 / staging.** The branch carries ~25 dirty files that are **not** this feature. Run
  `git diff HEAD --numstat` before every `git add` and stage by explicit path.
- **Never batch source edits through a broad find/replace.** A replacement shorter than the construct it
  belongs to hits unrelated code; anchor on the whole statement, assert one match, prefer `Edit`.
- **A JSX comment cannot lead an expression** — put it *above* the `{cond && (`, never inside. The parser
  reports the error several lines below the comment that caused it.
- **Constructor changes cascade to unit tests.** Grep the test project for every `new <Type>(` before building.
- **A build failing only with MSB3021/MSB3027 is a file lock, not a compile error** — a running API or
  `next dev` is holding the DLLs. Stop it and rebuild; do not diagnose the code.
- **The 0-warning gate means no NEW warnings.** The repo carries a documented pre-existing baseline
  (`CS8618` on EF private ctors, etc.). New files must contribute none — initialize non-nullable strings with
  `= string.Empty;` matching the sibling entities.
- **Frontend rule sources are mandatory before touching `web/`:** `.claude/rules/frontend-web.md`, the nearest
  `web/**/CLAUDE.md`, and `~/.claude/skills/DEVICE-CONTRACT.md`. Reuse the existing primitive; never re-solve
  what it already solves.
