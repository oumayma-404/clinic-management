# Feature Specification: Data & Money Integrity

**Status:** APPROVED
**Challenged:** Yes (5-lens completeness pass — edge cases, data/migration, cross-feature, UX, adjacent defects)
**Type:** Full
**Created:** 2026-07-27
**Scope:** Full-stack
**Branch:** feature/windows-desktop-app (existing)
**Feature:** Close all eight § 1 findings of `CODEBASE_AUDIT_2026-07.md` — the places where the app destroys records,
bills money twice, or cannot correct a mistake — together with the adjacent defects that would otherwise re-break them.

> **Implementation order is part of the spec.** Land **H → A → B → C → D → F → E → G** with a clean build gate
> (`dotnet build` 0 errors / 0 warnings, `npx tsc --noEmit` 0) between slices. Two orderings are load-bearing and
> non-negotiable: the reconciliation report (H) must exist **before** any data migration runs, and every null-safe
> `.Value` fix in slice F must be **deployed before** the sentinel-blanking migration.

---

## Overview

Eight defects in the audit's "Data loss & money correctness" section share three root causes:

1. **Nothing in this app can be corrected.** `Payment` is immutable, `Installment.AmountPaid` only ever increases,
   `AmountCollected` only ever increases, and `CreditNote` is write-once. A typo is permanent.
2. **Two things that mean "absent" are conflated.** An omitted JSON field and an explicit `null` are the same value
   on the wire, so cancelling an appointment erases its act; a blank email and "no email given" are the same string,
   so every contact-less patient shares one fake identity.
3. **Money is stored in two places that can disagree.** `Invoice.AmountCollected` is a column, `Σ Payment.Amount` is a
   query, and different screens read different ones. An installment stores a running total with only its *last*
   payment date, so a two-instalment month reports zero.

None of these are new discoveries by accident. Six of the eight were **consciously deferred** by earlier specs that
said *"separate design"*, not *"never"* (`features/unified-billing-ledger/spec.md:72`,
`features/treatment-plan-workspace/spec.md:131-134`, `features/adoption-qa-h-residual-hygiene/spec.md:18`,
`features/windows-desktop-app/spec.md:229`, `features/unified-billing-ledger/progress.md:91-95`). Two were genuinely
missed (avoir readability, contact sentinels). **This spec closes all eight and defers nothing that would re-break
them.**

It also lifts one explicit prior fence: `features/graceful-error-handling/spec.md:19,58` ruled out *"no new 409/422"*.
Optimistic concurrency needs a 409, so that deferral is being lifted deliberately, within the same `{ "error": … }`
body contract that spec established.

### Scale note

This is large — eight slices, four migrations, two new domain concepts and a new UI surface. It was kept as one
feature deliberately: the eight defects interlock (a payment void is meaningless without the ledger; the ledger is
unsafe without the carry-over de-dup; the carry-over is invisible without the detail modal), and splitting them
would ship intermediate states where the money is *more* wrong than it is today. The slices below are independently
buildable and independently shippable in the stated order.

---

## What Changes

### Slice H — Reconciliation report *(no migration, no schema)*

Built first because it must be runnable **before** the migrations, when the app may be down.

- **A new Local-mode console verb** — `dotnet run -- reconcile-money`, alongside the existing `reset-admin-password`
  and `provision-cert` verbs intercepted before the web host boots (`Program.cs`). Not an endpoint: it has to run on
  a stopped app, and there is no admin report surface in this codebase to hang it on.
- **What it prints, per clinic:**
  - `Σ non-voided Payment.Amount` vs `Σ Invoice.AmountCollected` — the two ledgers of finding 4, which have drifted
    silently for as long as the app has existed.
  - `Σ InstallmentPayment.Amount` vs `Σ Installment.AmountPaid`.
  - Per plan: `Σ Installment.Amount` vs `TotalPlanned` — the invariant that `AddItems`/`RemoveItem` can already break
    (`TreatmentPlan.cs:349,382` recompute the total and leave the échéancier alone).
  - Monthly « encaissé » for the last 24 months, computed **both the old way and the new way**. This is the single
    line that proves the ledger migration moved no closed month.
  - Orphan counts: `Invoices` and `TreatmentPlans` whose `PatientId` matches no patient (neither has an FK), plus
    `ToothStates` and `Notifications`.
  - Sentinel counts for all four literals, plus a separate count of near-miss placeholders (see Edge Cases).
  - **Bridged invoices carrying un-transferred plan money** — the existing-bad-data list from slice C.
  - Over-credited invoices (`Σ CreditNote.Amount > AmountCollected`) and duplicate non-cancelled bridge invoices per
    plan — both possible today because each is a read-then-write with no unique index.
- Output is a plain-text table to stdout **and** a timestamped file next to the backup folder, so the before/after
  pair can be diffed. Exit code is non-zero when any check fails, but the verb never mutates anything.

### Slice A — Stop destroying records *(1 migration)*

**A1. Patient deletion blocks instead of cascading.**

- The duplicate `HasMany(p => p.Appointments)` block in `PatientConfiguration.cs:122-125` is deleted, restoring
  `AppointmentConfiguration.cs:68-72`'s intended `SetNull`. This is a latent trap regardless of A1 — two configs
  declaring the same relationship with opposite behaviour, resolved by alphabetical class-name order.
  *(A sweep confirmed exactly six duplicated relationships in the whole Configurations folder; this is the only one
  that conflicts. The other five are identical on both sides and are left alone.)*
- `DeletePatientCommand` gains a **pre-check** that counts, in one batched pass, everything attached to the patient:
  appointments, invoices, treatment plans, dental records, tooth states, medical documents, files, folders, flags,
  recurring series, medical/family history, lab orders, waiting-list entries, and outbox notifications.
  **Invoices and treatment plans must be counted explicitly** — they have no FK, so nothing at the database level
  will ever raise for them.
- If anything is attached, the delete is refused with a message naming the actual counts — replacing the current
  message, which lists three things of which only one can ever trigger it and which is therefore a lie today.
- Cancelled invoices, cancelled appointments and voided payments **still block**: they are fiscal records.
- The pre-check is also exposed as a read so the confirm dialog can show the block *before* the user clicks.

**A2. Patient archiving — the escape hatch.**

Blocking with no alternative would make the delete button decorative: there is no merge, no soft-delete and no
archive anywhere in this codebase, so a duplicate patient with one booking could never be removed.

- New `Patient.IsArchived` + `ArchivedAt`, with `Archive(reason)` / `Unarchive()` domain methods. Fully reversible;
  nothing is destroyed.
- Archived patients are hidden from the patient list, the header search, the recall list, and every patient picker
  (appointment booking, invoice creation, document creation), and are excluded from « Créances » and the dashboard
  KPIs. They remain reachable by direct URL and keep every record intact.
- A patient with an **unpaid balance or a future appointment** cannot be archived — that is a hide-the-problem move,
  and the refusal says so.
- Deletion remains possible for a patient with genuinely nothing attached (the created-by-mistake case).

**A3. The appointment partial-update wipe, and its siblings.**

- The tri-state pattern already written for `TreatmentPlanItemId` (`UpdateAppointmentCommand.cs:28,40-53` — a backing
  field whose setter flips a `[JsonIgnore] …Specified` flag, because System.Text.Json only invokes setters for keys
  physically present in the payload) is **generalized** to `ProcedureTypeId`, `DoctorId`, `Notes` and `DoctorName`.
- Consequence for `ProcedureTypeId`: omitting it leaves the act untouched; sending explicit `null` clears it.
  Cancelling an appointment stops destroying its procedure type, snapshot duration and colour.
- Consequence for `DoctorId` / `Notes` / `DoctorName`: sending explicit `null` now **clears** them. Today clearing the
  notes box is a silent no-op and a practitioner can never be unassigned — `Appointment.SetDoctorId(null)` exists and
  is unreachable. This resurrects it rather than deleting it.
- Two silent-swallow defects in the same handler are fixed: an unparseable `Status` and a `DurationMinutes <= 0` are
  currently ignored while the endpoint returns 200. Both now fail explicitly.
- **The fix must live in the command, not a controller DTO** — `AIActionService.cs:982-988` constructs
  `UpdateAppointmentCommand` directly for the AI-chat "annuler le rendez-vous" action and bypasses the controller
  entirely. That path triggers the wipe today.
- Frontend: `edit-appointment-dialog.tsx` stops sending `|| undefined` for notes and practitioner (which is what makes
  clearing a no-op) and sends explicit `null`. The practitioner `Select` gains an « Aucun praticien » option — Radix
  `Select` cannot hold `value=""`, so it needs a sentinel value like the `"all"` used on the appointments page.

### Slice B — A correctable payment ledger *(1 migration)*

**B1. Invoice payments become voidable.**

- `Payment` gains `IsVoided`, `VoidedAt`, `VoidReason`, `VoidedByUserId`, `VoidedByName`. The row is **kept and shown**;
  nothing is deleted.
- New `Invoice.VoidPayment(paymentId, reason, actor, creditedTotal)`. It recomputes `AmountCollected` from the sum of
  **non-voided** payments rather than decrementing — the two ledgers have drifted silently for as long as the app has
  existed, and a recompute makes the arithmetic unfalsifiable. `Payments` is already `Include`d on every load
  (`InvoiceRepository.cs:22`), so the sum is free.
- Status walks back: `Paid → PartiallyPaid → Issued`, derived from the recomputed total exactly as `RecordPayment`
  derives it forward.
- **Guard against double-refunding money an avoir already returned:** the void is refused when
  `AmountCollected − amount < Σ non-cancelled CreditNote.Amount`. The credited total is loaded by the *handler* and
  passed in — Domain has no repository access.
- A void is **not reversible**. To correct a correction, record the right payment again.
- Voiding an already-voided payment is a **French failure, not a second decrement** — the double-click case must not
  silently double-apply.
- `AdminOrDoctor` only, matching every other operation that alters an issued financial document.
- **This is the first place in the codebase that records who mutated a financial document.** There is no audit table
  and this spec does not create one; the actor is stored on the void itself, as a name snapshot plus a soft
  (FK-less) user id, because users get deactivated and `User.Id` is a string.

**B2. Installment payments become real rows.**

- New `InstallmentPayment` child of `Installment`, mirroring `Payment` exactly: `Amount`, `Method`, `PaidOn`,
  `CreatedAt`, plus the same five void columns. This is what fixes the wrong-month bug — the invoice side is already
  event-sourced and correct (`InvoiceRepository.cs:89-96`) and is the model being mirrored.
- `Installment.AmountPaid` / `LastMethod` / `LastPaidOn` are **kept as stored denormalizations**, recomputed inside
  record and void. Thirteen read sites depend on them, and `Installment.Revise` guards against lowering `Amount`
  below `AmountPaid`. Note that `AmountPaid` **stops being monotonic** — `Revise` and `ReviseInstallments` key off it.
- `GetInstallmentCollectedBetweenAsync` sums **ledger rows by their own `PaidOn`**, excluding voided rows. A payment
  of 400 DT in January and 600 DT in February now reports 400 and 600.
- Plan payments become voidable on the same terms as invoice payments.
- **Tenant scoping:** the ledger is a grandchild. It carries no query filter and **no `DbSet<>` is exposed on the
  context** — every read enters through the filtered `TreatmentPlans` root and `SelectMany`s down. This keeps the
  children-carry-no-filter convention and prevents an unscoped `_context.InstallmentPayments.Where(…)` from leaking
  money across clinics. A tenant-isolation test case is mandatory.
- **Backfill:** one ledger row per installment with `AmountPaid > 0`, dated `LastPaidOn`, for the cumulative amount.
  This reproduces today's figures **exactly** — no past month moves. **The wrong-month attribution is fixed going
  forward only; historical splits cannot be reconstructed because the data was never stored.** Say this out loud in
  the release note, or someone will expect January to heal.

**B3. Receipts stop lying.**

- `GetInstallmentReceiptPdfQuery` takes a `paymentId` and prints **that payment**, not the cumulative sum. Today a
  second partial payment silently reissues a receipt for the total.
- A voided payment's receipt still downloads — the paper is already in the patient's hands and the clinic needs to
  reproduce what was handed over — but it is over-stamped « ANNULÉ le {date} — motif : {motif} », mirroring the
  existing « FACTURE ANNULÉE » banner on the invoice PDF. A clean reprintable receipt for reversed money is a fraud
  surface; removing it entirely makes reconciliation impossible.
- Both receipt renderers stop reading the **live** `invoice.Outstanding` for « Reste à payer » and use the balance
  **as of that payment**. This is already wrong today when reprinting the first of two receipts; after a void it
  would show a balance that grew.

**B4. Payment validation gaps closed (same class of defect, same pass).**

- `PaidOn` and `RefundedOn` are validated: not in the future, and not `default(DateTime)`. Today `PaidOn` is a
  non-nullable `DateTime` with no validation anywhere, so a client that omits the key posts `0001-01-01` — a payment
  that increments `AmountCollected` but is invisible in every cash window forever.
- Payment amounts round through `InvoiceCalculator.RoundMoney` and reject anything that rounds to zero.
  `Invoice.RecordPayment` currently accepts `0.0004` and stores `0.000` — a zero-amount row that would block
  cancellation forever.
- `CreateCreditNoteCommand` stops **silently dropping** an unparseable payment method and rejects it, matching
  `RecordPaymentCommand`. Now that avoirs are printable, that silent drop becomes a visible blank on a document.

### Slice C — The devis→facture carry-over *(no migration)*

- At **`Issue()`** — not at draft creation, because `RecordPayment` requires an issued invoice — the plan's collected
  installment money is carried onto the invoice as payment rows **keeping their original `PaidOn` dates**, so no
  month's takings shift.
- Each carried payment records `SourceInstallmentPaymentId`. **This is what makes the cash-side de-dup
  implementable** — without a discriminator both `GetCollectedBetweenAsync` and `GetInstallmentCollectedBetweenAsync`
  count the same dinar and the caisse doubles.
- `GetInstallmentCollectedBetweenAsync` gains an `excludedPlanIds` parameter — mirroring
  `GetInstallmentOutstandingByPatientAsync`, which already takes a **required** one so a read cannot silently skip
  the de-dup — and both cash callers pass `PlanBillingRules.BilledPlanIds(...)`.
- **Three load-bearing comments state the opposite rule and must be inverted together**, or the next reader will undo
  this: `PlanBillingRules.cs:19-22`, `GetCaisseSummaryQuery.cs:69-72`, `TreatmentPlanRepository.cs:88-91`. All three
  say the de-dup deliberately does *not* apply to collected cash *because the bridge copies no payment onto the
  invoice*. That premise is exactly what this slice changes — the condition
  `features/treatment-plan-workspace/progress.md:486-489` (DEV-5) anticipated has come due.
- **The carry-over is capped and never silent.** A plan can have collected more than the invoice bills (acts removed
  after payment, VAT turned off, draft lines edited). If `Σ carried > TotalTtc`, the issue is **refused** with a
  French message naming the un-carried amount — never clamped silently, and never allowed to throw
  "Le paiement dépasse le montant restant dû" from inside `Issue()`, which would leave a numbered draft that can
  neither be issued nor rebuilt.
- **Issuing a bridge invoice now writes to two aggregates in one operation** (mark the installment payments carried +
  record the invoice payments). This is the first place in this codebase where a partial commit would lose money, so
  it runs inside `IUnitOfWork.BeginTransactionAsync`/`Commit` — which exists and is currently unused everywhere.
- **Existing bridged invoices are reported, not repaired.** They are listed by slice H with the amount involved so the
  clinic can correct them by hand. Writing synthetic payments onto numbered, possibly TTN-registered documents is
  riskier than the problem.
- Frontend disclosure, because the money moves at issue and not at draft: the draft shows
  « {montant} déjà encaissé sur le devis sera reporté à l'émission », the issue toast names the carried amount, and
  **`handleIssueAndPay` must prefill from the post-carry-over response** — otherwise it offers the full amount and
  the dentist double-collects.

### Slice D — Avoirs become readable *(no migration)*

- `ICreditNoteRepository` gains `GetByIdAsync`, `GetByInvoiceIdAsync` and a clinic-scoped list. It currently has no
  read path of any kind.
- `InvoiceDto` gains `creditNotes` and `creditedTotal`, plus **server-computed `canCancel` / `canCreateAvoir` flags**.
  The frontend currently re-derives both from `status` + `amountCollected`, which is precisely the divergence that
  produces an enabled button the API refuses.
- A **`GET /api/invoices/{id}/avoirs/{avoirId}/pdf`** rendering a proper « AVOIR ». This needs data the entity does
  not hold: the corrected invoice's **number and date** (a legal avoir must cite them, and `CreditNote` holds only a
  soft `InvoiceId` with no navigation), the **patient** (resolved the same way), and the **VAT split** — `Amount` is a
  single scalar today, so a VAT-applicable clinic's avoir cannot currently be rendered correctly. The renderer takes
  a new `AvoirPdfData` and reuses the existing header/footer/`FormatDt` helpers.
- The avoir number and PDF stop being thrown away: the creation toast names the number and offers the download,
  matching the payment-receipt pattern.
- **Netting is made consistent across all five money reads.** Today only the caisse nets avoirs unconditionally;
  `GetInvoiceRevenueQuery` nets them **only when both `from` and `to` are supplied** — and `/factures` loads with both
  filters empty, so *the default « Total encaissé » every user sees is the non-netting branch*. The dashboard does not
  net at all. All of them now do.
- A fully-refunded invoice keeps `Reste 0` — the debt is settled, the patient does not owe again — but « Solde
  patient » and the invoice row now show the credited total, so the zero is explained rather than silent.
- `CreateCreditNoteCommand`'s status gate relaxes from `Paid|PartiallyPaid` to `Status != Draft && != Cancelled &&
  AmountCollected > 0`. Otherwise a void that walks an invoice back to `Issued` **permanently closes the avoir path**.
- **El Fatoora is explicitly out of scope and explicitly disclosed.** A Tunisian avoir against a TTN-registered
  invoice is itself a declarable document (TEIF 381) and this app does not transmit it. The avoir screen warns
  « Cet avoir n'est pas transmis à El Fatoora » when the invoice is TTN-registered, so the clinic knows it has a
  manual step rather than assuming it is handled.

### Slice F — Patient contact becomes genuinely optional *(1 migration, must be last)*

- `Patient.Email` and `Patient.PhoneNumber` become **nullable owned value objects**. The precedent already exists on
  the same entity: `EmergencyContactPhone` is exactly this shape.
- **All ten unguarded `.Value` dereferences are made null-safe before anything is blanked.** The worst is
  `GetPatientsQuery.cs:68` — an unguarded `p.PhoneNumber.Value` inside an in-memory `Where` over the whole clinic, so
  **one phone-less patient 500s the entire patient list and the header search**.
- Both sentinel sources stop producing sentinels: `CreatePatientCommand.cs:101-106` and
  `GoogleCalendarSyncService.cs:669-670` (a second, undocumented pair on Google-created placeholder patients).
- `UpdatePatientCommand` gets the **same tri-state treatment as slice A3**. Today blank means *"keep existing"*, so
  there is no way to clear either field — nullable columns alone would leave clearing a silent no-op.
- The frontend drops the required-asterisk and the blank check on phone (email is already labelled `(optionnel)`),
  keeps `isDeliverablePhone` validation for non-blank values, and sends `null` rather than `""`. The inline
  create-patient path in the appointment dialog is reconciled to match.
- Where a missing phone has a consequence, the UI says so rather than showing a neutral blank: under the field
  (« Sans numéro de téléphone, ce patient ne recevra ni rappel ni relance »), on the patient detail, and on the recall
  list, where the « Envoyer » action is disabled.
- **`SendRecallCommand` stops lying** — today it marks the patient contacted and snoozes them 30 days regardless, so a
  phone-less patient silently disappears from the recall list for a month. It now refuses, and reminders are gated at
  **enqueue** rather than only failing at dispatch, so the outbox stops accumulating explainable-but-useless failures.
  *(Verified: the sentinel never reached a gateway — `PhoneNumber.ToE164` already rejects a 10-digit string — so this
  changes no outbound behaviour, only the noise and the lie.)*
- **Deployment ordering is a hard requirement.** The blanking `UPDATE` is the **last** migration in the batch, after
  the columns are nullable and after the null-safe code is deployed. In Local mode migrations run *after* Kestrel is
  already serving, so there is a guaranteed live window; blanking first would take patient search down for the clinic.

### Slice E — Optimistic concurrency *(no migration)*

- Postgres' `xmin` system column becomes the concurrency token on every aggregate root — no schema change, no new
  column. Applied by a loop in `OnModelCreating`, which **must** materialize with `.ToList()`, skip
  `entityType.IsOwned()`, and skip `NotificationRead` (a plain class, not an `Entity<>`). Calling
  `modelBuilder.Entity(clrType)` on an owned type promotes it to a standalone entity — that would silently give
  `Email`, `PhoneNumber`, `Address`, `InsuranceInfo`, `CnamInfo` and `ColorHex` their own tables.
- **The token round-trips.** It is exposed on the read DTO and accepted back on the mutating command for every entity
  with a real multi-user edit form: Patient, Appointment, Invoice, TreatmentPlan, DentalRecord, Clinic. A
  server-side-only token would only compare against a value loaded microseconds earlier in the same request — it
  catches a double-click, not the five-minute-old form, which is the case a clinic actually hits. Every other
  aggregate keeps the token without the round-trip.
- A conflict returns **HTTP 409** with the established `{ "error": "…" }` body and the message
  « Ce dossier vient d'être modifié par un autre utilisateur. Les données ont été actualisées — vérifiez puis
  enregistrez à nouveau. »
- **The catch-all is the whole problem, and it must be fixed first.** 163 handlers in 152 files end in
  `catch (Exception ex) { return Result.Failure(...) }`, and `HandleFailure` defaults to 400. Without this,
  *every* conflict returns a generic 400 and the entire slice produces zero observable behaviour. A
  `ConflictException` is introduced, mapped to 409 by `ExceptionMiddleware` (a third `case` alongside
  `NotFoundException` and `ForbiddenAccessException`), and `DbUpdateConcurrencyException` is translated to it
  **before** the catch-all in every mutating handler.
- **Four existing `catch (DbUpdateException)` blocks would otherwise swallow conflicts**, because
  `DbUpdateConcurrencyException` derives from it:
  - `DeletePatientCommand.cs:53` reports a concurrent edit as *"des données liées existent"*.
  - `IssueInvoiceCommand.cs:98`, `AcceptTreatmentPlanCommand.cs:86`, `CreateCreditNoteCommand.cs:126` retry a conflict
    **five times as a numbering collision** — and the retry can never succeed, because EF keeps the same stale
    original value. Five wasted round-trips, a wrong message, a wrong status, and in the invoice case a skipped
    El Fatoora enqueue.
  All four narrow their filter to exclude concurrency conflicts.
- **`EInvoiceService.cs:117-125` swallows its persist failure**, so a conflict there would lose a TTN validation and
  cause the outbox to **submit the same invoice to El Fatoora twice**. It reloads and reapplies instead.
- **Detached-`Update()` audit.** A detached entity has an original token of `0`, so `Set.Update(detached)` becomes a
  guaranteed conflict. `AppointmentRepository.cs:170` calls it unconditionally, unlike Patient/Invoice/TreatmentPlan
  which guard. Every repository is audited and given the same guard before the token is enabled.
- Frontend: a `409` is handled distinctly from a `400`. `ApiError` already carries `status`, and the three existing
  `status === 0` offline checks are the precedent. The modal **stays open with the user's input preserved**, the page
  refetches underneath, and the message appears in the modal's inline banner — a toast fired behind an open dialog is
  easy to miss. On a conflicting **delete** the confirm dialog closes (its target may no longer exist) and the list
  reloads. A second consecutive conflict escalates the wording rather than repeating it.
- `clinic-settings.tsx:272-278` — the app's only don't-clobber guard — keeps suppressing the refetch while editing but
  now **records that a peer change arrived** and shows a notice with a « Recharger » option. Silently dropping the
  peer's change now also means a stale token and therefore a baffling 409.
- **A 403 fallback is added at the same time.** A role denial is produced by authorization middleware, not
  `ExceptionMiddleware`, so the body is empty and the frontend currently renders the literal English string
  « HTTP 403: Forbidden ». Every new `AdminOrDoctor` action in this feature would hit it.

### Slice G — The invoice detail modal *(no migration)*

The UI home for slices B and D. There is no invoice detail view anywhere in the app today — no page, no drawer, no
expandable row — so there is nowhere for a per-payment action to live.

- Opened from the **invoice number cell**, which is inert text today. This gives draft rows a target too and costs no
  new icon in a row that already renders up to eight.
- Shows: header totals; the act lines; **every payment** with its method, date, a persistent « Reçu » download, and an
  « Annuler » action; and **the avoirs** with their number, date, motif, amount and PDF.
- It re-fetches via `invoicesApi.get(id)` — which exists and has zero callers today — because the row is a list
  snapshot and per-payment accuracy is the entire point. That means a real loading state, and an error state with a
  retry: this modal is the only place voidable payments exist, so a silent load failure is a dead end.
- Voided rows stay in place, struck through, with an « Annulé » badge and the motif. The modal stays open after a
  successful void.
- The void confirm is an **in-place panel inside the modal, not a nested dialog** — there is no precedent for nested
  Radix dialogs in this repo and the one place that tried something similar documented the focus/Enter conflicts.
  Its required-motif error is an inline banner, not a toast.
- A shared `canReverseFinancials = role === "admin" || role === "doctor"` predicate is added. Every client-side gate
  in the app today compares against `"admin"` only, so **a doctor — the primary user — would be denied by all of
  them**. The four financial-reversal actions (invoice cancel, avoir, plan cancel, payment void) currently disagree
  with each other about whether to hide or to call-and-surface-the-403; they are unified onto **rendered but
  disabled, with a `title`** — hiding it makes a secretary conclude the operation is impossible.
- Reachability gaps closed: the plan workspace's « Facturé » badge and the patient plan card's badge become links (the
  reverse direction already exists), and « Créances » rows deep-link to `?tab=factures`, which the patient page
  already honours.
- **Refresh wiring that is wrong today and would make this feature look broken:** the patient page mounts
  `<InvoicesTable>` without `onChanged`, so « Solde patient » never refreshes after the mutation that moves it most;
  and « Créances », the caisse and the dashboard have **no realtime subscription at all**, so a void leaves them
  showing pre-void figures indefinitely. All four are wired.

---

## API Contract

### New — `POST /api/invoices/{id}/payments/{paymentId}/void` · **`[Authorize(Policy = AdminOrDoctor)]`**
Void a recorded payment. The row is kept and marked, never deleted.
Request: `{ reason: string, version: string }`
Response 2XX: `InvoiceDto` (recomputed totals, walked-back status, `payments[]` including the voided row)
Errors: `400` amount would fall below the credited total / reason missing / already voided ·
`403` not admin-or-doctor · `404` not found or other clinic · `409` version conflict

### New — `POST /api/treatment-plans/{id}/installments/{installmentId}/payments/{paymentId}/void` · **`[Authorize(Policy = AdminOrDoctor)]`**
Same semantics on the plan side.
Request: `{ reason: string, version: string }` · Response 2XX: `TreatmentPlanDto` · Errors: as above.
> Must be classified in `AdminOrDoctorActions` in `TreatmentPlansControllerAuthorizationTests`, whose
> `Every_Action_Is_Classified_By_This_Test` fails the build on any unclassified new action.

### New — `GET /api/invoices/{id}/avoirs`
List the invoice's credit notes.
Response 2XX: `CreditNoteDto[]` · Errors: `404` not found or other clinic

### New — `GET /api/invoices/{id}/avoirs/{avoirId}/pdf`
Response 2XX: `application/pdf`, filename `avoir-{number}.pdf` · Errors: `400` · `404`

### New — `POST /api/patients/{id}/archive` and `POST /api/patients/{id}/unarchive`
Request: `{ reason?: string, version: string }` · Response 2XX: `PatientDto`
Errors: `400` outstanding balance or future appointment · `404` · `409`

### New — `GET /api/patients/{id}/deletion-check`
What blocks this patient's deletion, so the dialog can say it before the user clicks.
Response 2XX: `{ canDelete: bool, blockers: { kind: string, label: string, count: int }[] }`

### Changed — `GET /api/treatment-plans/{id}/installments/{installmentId}/receipt-pdf`
Becomes `…/payments/{paymentId}/receipt-pdf` — prints that payment, not the cumulative total.

### Changed — `PUT /api/appointments/{id}`
`procedureTypeId`, `doctorId`, `notes`, `doctorName` become tri-state: **omitted** leaves the field untouched,
explicit `null` clears it. An unparseable `status` and a `durationMinutes <= 0` now return `400` instead of being
silently ignored. Adds `version`.
> Wire-compatible for callers that send every field; the change is that omission stops meaning "clear".

### Changed — `POST /api/invoices/{id}/payments`, `POST /api/treatment-plans/{id}/installments/{installmentId}/payments`, `POST /api/invoices/{id}/avoir`
`paidOn` / `refundedOn` are validated (not future, not default). An unparseable `method` on the avoir is now rejected
rather than silently dropped. Amounts round to the millime and reject zero.

### Changed — `GET /api/invoices`, `GET /api/invoices/{id}`
`InvoiceDto` gains `creditNotes`, `creditedTotal`, `canCancel`, `canCreateAvoir`, `version`; `PaymentDto` gains
`isVoided`, `voidedAt`, `voidReason`, `voidedByName`, `createdAt`.

### Changed — `GET /api/invoices/revenue`
Nets credit notes in **both** branches. The no-period branch — which is what `/factures` loads by default — currently
ignores them.

### Changed — `GET /api/patients`, `GET /api/patients/{id}`, `POST /api/patients`, `PUT /api/patients/{id}`
`email` and `phoneNumber` become nullable in both directions. On update they are tri-state: omitted keeps, explicit
`null` clears. Adds `version` and `isArchived`. The list excludes archived patients unless `includeArchived=true`.

### Unchanged, deliberately
`Invoice.Outstanding` still ignores credit notes — a refunded debt is settled, not re-owed. Invoice, devis and avoir
numbering sequences stay independent and are not harmonized. No credit note is transmitted to El Fatoora.

---

## Data / Schema Changes

Four migrations, in this order. **The table-creating migration must come before the concurrency one**, so that
`CreateTable` never has to answer whether the provider filters `xmin` out of a column list.

**M1 — `FixPatientAppointmentDeleteBehavior`**
Drop and re-add `FK_Appointments_Patients_PatientId` with `ON DELETE SET NULL`. The physical constraint has been
`CASCADE` since `InitialCreate` and no later migration touched it.

**M2 — `AddPatientArchive`**
`Patients.IsArchived boolean NOT NULL DEFAULT false`, `ArchivedAt timestamptz NULL`. Index `(ClinicId, IsArchived)`.

**M3 — `AddPaymentLedgerAndVoids`**
- `Payments` += `IsVoided boolean NOT NULL DEFAULT false`, `VoidedAt timestamptz NULL`,
  `VoidReason varchar(1000) NULL`, `VoidedByUserId varchar(200) NULL` *(soft link — `User.Id` is a string, and users
  get deactivated)*, `VoidedByName varchar(200) NULL`, `SourceInstallmentPaymentId uuid NULL`.
- New `InstallmentPayments`: `Id uuid PK ValueGeneratedNever`, `InstallmentId uuid NOT NULL` (FK → `Installments`,
  Cascade), `Amount decimal(18,3) NOT NULL`, `Method int NOT NULL`, `PaidOn timestamptz NOT NULL`,
  `CreatedAt timestamptz NOT NULL`, plus the same five void columns.
  **Configured from one side only** — configuring a relationship from both sides is exactly the bug M1 fixes.
- Indexes: `InstallmentPayments(InstallmentId)`, `InstallmentPayments(PaidOn)`, `Payments(InvoiceId, PaidOn)`
  *(partial, `WHERE NOT "IsVoided"`)*, and `Installments(DueDate)` — currently `Installments` has only its FK index,
  so the existing per-month query already scans an unindexed date.
- **Data:** one `InstallmentPayment` per installment with `AmountPaid > 0`, dated `LastPaidOn`, amount = cumulative
  `AmountPaid`, method = `LastMethod`. Guarded with `WHERE NOT EXISTS (…)` so a re-run inserts nothing. Amounts are
  copied verbatim — they were already rounded at write and must not be re-rounded.

**M4 — `MakePatientContactOptional`** *(last)*
`ALTER … DROP NOT NULL` on `Patients.Email` and `Patients.PhoneNumber`, **then** blank the four literals
(`noemail@example.com`, `unknown@example.com`, `0000000000`, `000-000-0000`). Order matters: blanking before the
constraint drop fails outright.

**No migration for concurrency.** `xmin` is a Postgres system column. But it **does** produce a model-snapshot diff,
so a dedicated (SQL-empty) migration is generated and committed for it — otherwise the next unrelated
`migrations add` silently absorbs ~20 `AddColumn<uint>("xmin")` operations into itself.

**Rollback is restore-from-backup, not `migrations remove`.** Two changes are not cleanly reversible: the generated
`Down()` for M4 is `SET NOT NULL`, which fails because the blanking left NULLs; and the blanking itself destroys the
original values. Dropping the void columns would leave voided payments live again while `AmountCollected` stays
decremented — a state the old code cannot self-heal. Taking a backup is a documented pre-step for Local installs,
where `PgDumpBackupService` already exists.

**Environment:** stop the API before `dotnet ef migrations add` — a running process makes it emit an *empty*
migration, and with the xmin migration an empty file is exactly what you expect, so the failure is invisible. Never
pass `--no-build`.

---

## Acceptance Criteria

**Records are no longer destroyed (slice A)**
- **AC-1:** `PatientConfiguration` no longer declares the `Appointments` relationship; the model snapshot and the
  physical FK both read `SetNull`.
- **AC-2:** Deleting a patient with any attached appointment, invoice, plan, record, tooth state, document, file,
  folder, flag, recurring series, history entry, lab order or waiting-list entry is refused, and the message names
  the actual blocking counts.
- **AC-3:** The refusal counts invoices and treatment plans explicitly — verified by a case where they are the *only*
  blockers, which no database constraint would catch.
- **AC-4:** A cancelled invoice, a cancelled appointment and a voided payment still block deletion.
- **AC-5:** A patient with nothing attached can still be deleted.
- **AC-6:** `GET /api/patients/{id}/deletion-check` returns the same verdict the delete would, and the confirm dialog
  shows it on open.
- **AC-7:** An archived patient is absent from the patient list, the header search, the recall list, every patient
  picker, « Créances » and the dashboard KPIs; is reachable by direct URL; and keeps every record.
- **AC-8:** Archiving is refused for a patient with an outstanding balance or a future appointment.
- **AC-9:** Unarchiving restores the patient everywhere.
- **AC-10:** A `PUT /api/appointments/{id}` that omits `procedureTypeId` leaves the procedure type, snapshot duration
  and colour untouched. Cancelling from the edit dialog, and from the AI chat, both preserve them.
- **AC-10a:** An explicit `procedureTypeId: null` still clears them.
- **AC-11:** Explicit `null` on `doctorId`, `notes` and `doctorName` clears each; omitting each leaves it untouched.
- **AC-12:** An unparseable `status` and a `durationMinutes <= 0` return `400`; neither is silently ignored.

**Payments become correctable (slice B)**
- **AC-13:** Voiding a payment keeps the row, marks it with motif, actor and timestamp, and recomputes
  `AmountCollected` as the sum of non-voided payments.
- **AC-14:** Status walks back correctly: voiding on a `Paid` invoice yields `PartiallyPaid` or `Issued` according to
  the recomputed total.
- **AC-15:** A void is refused when it would take `AmountCollected` below the total already credited by avoirs.
- **AC-16:** Voiding an already-voided payment returns a French failure and does not decrement twice.
- **AC-17:** A void is `AdminOrDoctor`; a secretary receives `403` and the UI shows a disabled control with a reason.
- **AC-18:** `GetCollectedBetweenAsync` excludes voided payments, so the caisse, the dashboard and the revenue KPI all
  drop by the voided amount **on the original payment's day**.
- **AC-19:** An installment payment is stored as its own row with its own date; 400 DT in January and 600 DT in
  February report 400 and 600 respectively.
- **AC-20:** `Installment.AmountPaid`, `LastMethod` and `LastPaidOn` stay consistent with the ledger after both a
  record and a void.
- **AC-21:** Installment payments are voidable on the same terms, and the plan's status is **not** walked back
  (a plan's status reflects clinical progress, not payment).
- **AC-22:** Nothing can read `InstallmentPayment` outside a clinic-filtered `TreatmentPlans` root; a cross-clinic
  read returns nothing and no `DbSet` is exposed.
- **AC-23:** The backfill produces exactly one row per already-paid installment and re-running it inserts nothing.
- **AC-24:** Every monthly « encaissé » figure for the last 24 months is **identical before and after** the backfill.
- **AC-25:** An installment receipt prints the single payment requested, not the cumulative total.
- **AC-26:** A voided payment's receipt still downloads and is stamped « ANNULÉ » with its motif.
- **AC-27:** Both receipts print the balance **as of that payment**, not the live balance.
- **AC-28:** A payment or refund dated in the future, or at `default(DateTime)`, is refused.
- **AC-29:** A payment amount that rounds to zero is refused; amounts round to the millime.
- **AC-30:** An unparseable payment method on an avoir is rejected, not silently dropped.

**The devis→facture bridge stops re-billing (slice C)**
- **AC-31:** Issuing a bridge invoice for a 1000 DT plan with 600 DT collected produces an invoice reading 400 DT
  outstanding, with the carried payments keeping their original dates.
- **AC-32:** The same 600 DT appears **once** in the caisse and once in the dashboard — not twice — and in the month
  it was originally collected.
- **AC-33:** Issuing is refused, with a message naming the un-carried amount, when the plan collected more than the
  invoice's TTC. The invoice is left issuable after the discrepancy is resolved.
- **AC-34:** The carry-over and the installment marking commit together or not at all.
- **AC-35:** `PlanBillingRules`, `GetCaisseSummaryQuery` and `TreatmentPlanRepository` no longer document the
  opposite rule.
- **AC-36:** The reconciliation report lists every pre-existing bridge invoice carrying un-transferred plan money,
  with the amount; no existing invoice is modified.
- **AC-37:** The bridge draft and the issue toast both disclose the amount to be carried, and « Émettre et encaisser »
  prefills from the post-carry-over balance.

**Avoirs become readable (slice D)**
- **AC-38:** An invoice's avoirs are listed with number, date, motif and amount, and each has a downloadable PDF.
- **AC-39:** The avoir PDF cites the corrected invoice's number and date, the patient, and the VAT split.
- **AC-40:** The invoice row shows an avoir badge and the credited total; « Solde patient » shows the credited total.
- **AC-41:** `canCancel` and `canCreateAvoir` come from the server; the UI never enables an action the API refuses.
- **AC-42:** `GET /api/invoices/revenue` nets avoirs whether or not a period is supplied, and the dashboard nets them.
- **AC-43:** A fully-refunded invoice reads `Reste 0` and shows its credited total; it does not reappear in
  « Créances ».
- **AC-44:** An avoir can still be created on an invoice that a void walked back to `Issued`.
- **AC-45:** The avoir screen warns that the avoir is not transmitted to El Fatoora when the invoice is
  TTN-registered.

**Contact details become optional (slice F)**
- **AC-46:** A patient can be created and updated with no email and no phone; nothing writes a sentinel.
- **AC-47:** No existing row contains any of the four sentinel literals after the migration.
- **AC-48:** The patient list, header search, patient detail, create and update responses, and both AI actions all
  work when a patient has neither — in particular, one phone-less patient does not break the list or the search.
- **AC-49:** Sending explicit `null` clears an existing email or phone; omitting it keeps the current value.
- **AC-50:** A non-blank phone is still validated as Tunisian; the form no longer marks it required.
- **AC-51:** `SendRecallCommand` refuses for a phone-less patient and does **not** stamp the 30-day snooze; the recall
  list disables « Envoyer » for those rows.
- **AC-52:** No reminder row is enqueued for a patient without a deliverable phone.
- **AC-53:** The UI states the consequence of a missing phone on the form, the patient detail and the recall list.

**Concurrent edits stop overwriting (slice E)**
- **AC-54:** Two staff loading the same patient, invoice, appointment, plan, record or clinic and both saving:
  the second receives `409` and their change is not applied.
- **AC-55:** The 409 body is the canonical `{ "error": … }` shape with the agreed French message — no second key, no
  internal detail, no row version echoed.
- **AC-56:** A conflict is **not** reported as a validation error, a numbering collision, or "des données liées
  existent". Each of the four existing `catch (DbUpdateException)` sites is verified individually.
- **AC-57:** A concurrency conflict inside the invoice-numbering retry is not retried as a collision.
- **AC-58:** A conflict during e-invoice persistence reloads and reapplies; the invoice is never submitted to TTN
  twice.
- **AC-59:** No repository's detached-`Update()` path produces a spurious conflict.
- **AC-60:** The token is present on the read DTO and required on the mutating command for Patient, Appointment,
  Invoice, TreatmentPlan, DentalRecord and Clinic. A stale token is rejected even minutes later.
- **AC-61:** On a 409 the modal stays open, the user's input is preserved, the underlying page refetches, and the
  message appears in the modal's inline banner.
- **AC-62:** On a 409 during a delete, the confirm dialog closes and the list reloads.
- **AC-63:** A second consecutive conflict shows escalated wording.
- **AC-64:** Clinic settings shows a notice when a peer change was suppressed during editing, with a reload option.
- **AC-65:** A `403` renders a French message, never « HTTP 403: Forbidden ».
- **AC-66:** No owned type became a standalone entity — the model snapshot has no table for `Email`, `PhoneNumber`,
  `Address`, `InsuranceInfo`, `CnamInfo` or `ColorHex`.

**The detail surface (slice G)**
- **AC-67:** The invoice number opens a detail modal showing lines, payments and avoirs, fetched fresh.
- **AC-68:** The modal has distinct loading, empty and error states, and the error state offers a retry.
- **AC-69:** Voided payments appear struck through with an « Annulé » badge and their motif; the modal stays open
  after a void.
- **AC-70:** Each payment has a persistent receipt download — not only the one-time toast action.
- **AC-71:** Reversal actions are consistently gated on admin-or-doctor across invoice cancel, avoir, plan cancel and
  both voids.
- **AC-72:** The plan workspace badge, the patient plan card badge and « Créances » rows all reach the invoice.
- **AC-73:** « Solde patient », « Créances », the caisse and the dashboard all refresh after a void or an avoir,
  including when the change came from another user.

**Verification and cross-cutting**
- **AC-74:** `reconcile-money` runs on a stopped app, mutates nothing, and reports every check in the slice-H list.
- **AC-75:** Run before and after the migration, every per-clinic and per-month figure matches except those an AC
  above says must change.
- **AC-76:** Tenant isolation holds for every new command and query: another clinic's row reads as not-found and no
  save occurs.
- **AC-77:** `dotnet build` is 0 errors / 0 warnings and `npx tsc --noEmit` is clean at every slice boundary.

---

## Out of Scope

- **Transmitting credit notes to El Fatoora (TEIF 381).** A real legal gap, but it needs the whole e-invoice state
  machine, signing, outbox and retry replicated onto `CreditNote` — a feature of its own. Disclosed in the UI
  instead (AC-45), so nobody assumes it is handled. Additive later: `CreditNote` gains the same fields `Invoice` has.
- **Correcting or voiding an avoir.** Credit notes stay append-only, matching the decision in
  `features/adoption-qa-f-avoir-credit-note/spec.md:34`. A mistyped avoir remains permanent — stated here so it is a
  known limit rather than a surprise. Fixing it needs the same reversal design as payments, one level up.
- **Repairing existing bridge invoices.** Reported, not rewritten (AC-36). They are numbered, and some are filed with
  TTN; the correction belongs to a human with the clinic's context.
- **Reconstructing historical installment payment splits.** The data was never stored. The ledger fixes attribution
  from its first day forward; the backfill deliberately reproduces today's figures so no closed month moves (AC-24).
- **Patient merge.** Archiving covers the duplicate case without needing to decide which record's history wins.
  Merge is a separate problem with its own conflict rules.
- **A general audit trail.** The actor is recorded on payment voids only, because a reversal without an author is not
  a correction. Extending it to every mutation means an audit table and a decision about retention —
  `features/treatment-plan-workspace/spec.md:316-321` deferred it and this spec does not reverse that.
- **The UTC day-boundary and New-Year numbering defects** (§4 of the audit). Verified as genuinely separate: with
  Tunisia at UTC+1 and payment dates stored as UTC midnight of a picked day, none of the eight fixes produces a
  visibly wrong day. The only reachable errors are the one-hour New-Year numbering window and the `/factures` filter's
  bare wall-clock bounds, neither of which any fix here touches.
- **`DentalRecord.Cost`/`AmountPaid` as a fourth money track.** It feeds no money read and appears in no consistency
  test, so nothing in this feature can make it drift. It needs its own reconciliation question.
- **`UpdateStockItemCommand`'s absent movement row and `StockItem.UnitPrice`'s `decimal(18,2)`.** Real, in the audit,
  not money-read-connected.
- **A merge/compare UI for conflicts.** The chosen 409 experience is tell-and-refresh; a field-by-field merge is a
  substantial feature on top of an already large one, and additive later.
- **Making the sidebar responsive.** The new modal inherits the app's existing mobile limitation; fixing it is §7 of
  the audit.

---

## Edge Cases (Critical only)

- **A void on an invoice that already has an avoir.** Both reduce collected cash, and the caisse subtracts avoirs
  separately — so an unguarded void would take the same dinar out twice, and could drive `creditable` negative,
  permanently breaking future avoirs. AC-15 caps the void at the un-credited portion; AC-44 keeps the avoir path open
  after a walk-back.
- **A bridge invoice whose payments are all voided.** `Invoice.Cancel` guards on *any* payment row existing, so with
  voids kept as rows the invoice could never be cancelled — and a bridged plan whose invoice cannot be cancelled can
  never be amended or re-billed either, a permanent dead end. The guard becomes *any non-voided payment*, and the
  frontend's `isCancellable` is replaced by the server's `canCancel` so the two cannot disagree.
- **Cancelling an invoice that TTN has validated.** `Cancel` has no e-invoice guard today. With voids making
  once-paid invoices cancellable, a locally-cancelled invoice could still be registered nationally. `Cancel` refuses
  when `EInvoiceStatus` is `Valid`, `Submitted` or `Validating`, pointing at the avoir instead.
- **A plan that collected more than its invoice bills.** Possible after an amendment removed acts, or when VAT
  settings changed. The carry-over must not throw from inside `Issue()` — that would leave a numbered draft that can
  neither be issued nor rebuilt. AC-33 refuses the issue with a specific message.
- **The migration running while the app serves traffic.** In Local mode migrations are dispatched *after* Kestrel
  binds, so the backfill runs against a live database. The backfill must be idempotent and retry-safe rather than
  abort-on-conflict, and the sentinel blanking must be last so a phone-less row never reaches un-deployed
  non-null-safe code.
- **An installment with `AmountPaid > 0` but `LastPaidOn` null.** Unreachable through the domain but possible in the
  data; `PaidOn` is `NOT NULL`, so a bare insert would abort the whole migration. It falls back to the plan's
  acceptance date and is **counted in the report**, because that silently assigns money to a month.
- **A near-miss placeholder phone.** A clinic that typed `00000000` (eight zeros) has a *different* string that will
  not be blanked — and `ToE164` accepts it as deliverable, so the gateway gets billed for it. These are counted
  separately in the report; the match is not widened by guessing.
- **The refreshed data invalidates the user's input after a 409.** If a peer collected the balance while the payment
  modal was open, « vérifiez puis enregistrez à nouveau » is wrong advice. That case gets its own message and a
  disabled confirm.
- **A patient whose only blocker is an orphaned invoice.** Invoices have no FK, so orphans already exist from past
  cascading deletes. The deletion pre-check counts them; the report lists them; neither is silently ignored.
- **Two payments on the same invoice on the same day.** Ordering in the modal, and the payment-modal's
  find-the-new-payment id diff, both need a deterministic tiebreaker — `PaidOn` then `CreatedAt`.

---

## Tests

Written to this repo's conventions: xUnit + Moq, no database, no FluentAssertions, `Pascal_Snake_Case` sentence
names, a class-level `<summary>` and a per-test `// [AC-n]` comment, deterministic GUIDs and fixed UTC dates.

**Domain** — `Invoice.VoidPayment` (recompute, walk-back, avoir cap, double-void, `Cancel`'s new non-voided guard and
its e-invoice guard); `Installment` ledger record/void and denormalization consistency; `Patient.Archive`/`Unarchive`;
`Appointment` tri-state clearing; payment date and rounding guards. Thrown French messages asserted with
`Assert.Contains` on a fragment, never the whole string.

**Handlers** — one class per new command, plus regression cases on the changed ones. The critical regression is the
mirror of `AppointmentPlanLinkUpdateTests.An_Unrelated_Edit_Leaves_The_Plan_Act_Link_Untouched`, which has no
procedure-type equivalent today.

**Guard tests that must be updated, not just kept green:**
- `AppointmentSyncMappingTests.cs:158` sends a bare update and **passes only because its fixture has no procedure
  type** — it currently pins the defect. Give the fixture a procedure type; it must then fail before the fix and pass
  after.
- `MoneyReadConsistencyTests` mocks **the entire collected-cash side to zero** and asserts `TotalOutstanding` only, so
  it would pass green through every defect in slices B, C and D. It is extended to the collected side with non-zero
  payments, at least one avoir, at least one voided payment, and a bridged plan — and `GetCaisseSummaryQuery` and
  `GetInvoiceRevenueQuery` are added as fourth and fifth reads. **The caisse has zero tests anywhere today.**
  Its `Wire()` helper hand-reimplements repository SQL in LINQ, so every new filter (`!IsVoided`, `excludedPlanIds`,
  avoir netting) must be mirrored there in lock-step or the suite passes while production is wrong.
- `TreatmentPlansControllerAuthorizationTests` — the new void action goes in `AdminOrDoctorActions`; the build fails
  otherwise.
- `IssueInvoiceCommandHandlerTests` asserts against `DateTime.UtcNow.Year`, recomputing the same expression the
  handler uses — it can never detect a wrong-year defect and flakes across New Year. Pin a fixed year.
- `*TenantIsolationTests` — a case per new clinic-scoped verb, asserting `IsFailure` **and**
  `SaveChangesAsync … Times.Never`.

**Frontend** — no test runner exists; the gate is `npx tsc --noEmit` clean and `npm run build` clean.

**Execution** — `dotnet test` fails at assembly load with `0x800711C7` on this machine (Windows Smart App Control,
environmental). Use the documented workaround: `dotnet build -p:OutDir=<scratch>/utbuild/` then `dotnet vstest`.
The tests in this spec are expected to be **written and executed** via that path, not merely written; there is no CI
in this repo, so nothing else will ever run them.

---

## Documentation to update on completion

- Root `CLAUDE.md` — the billing/CNAM/treatment-plan bullet (payments are now correctable and event-sourced on both
  tracks), the tenant-isolation bullet (concurrency tokens now exist), and the security-posture bullet (409 is now a
  real status).
- `api/ClinicManagement.Domain/CLAUDE.md` — `Payment` is no longer described as immutable; `Installment` no longer
  "keeps only the latest method/date"; `PlanBillingRules`' outstanding-only rule now has a stated exception for
  carried payments.
- `api/ClinicManagement.Application/CLAUDE.md` — the handler shape gains conflict translation; the two-aggregate
  transactional write at `Issue()` is the first use of `IUnitOfWork.BeginTransactionAsync`.
- `api/ClinicManagement.UnitTests/CLAUDE.md` — `MoneyReadConsistencyTests` now covers five reads including the caisse.
- `packaging/README.md` — the `reconcile-money` verb, and backup-before-upgrade as a required step for this release.
- `CODEBASE_AUDIT_2026-07.md` — tick §1's eight items and note the adjacent items closed with them.
