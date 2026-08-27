# Exploration — data-and-money-integrity

**Date:** 2026-07-27
**Source:** 8 parallel Explore agents + hand-verification of the 5 highest-impact claims.
**Target:** the 8 findings in §1 of `CODEBASE_AUDIT_2026-07.md` ("Data loss & money correctness").

> Everything below was read in source. Where a doc, comment or prior spec disagrees with the code, the code is
> reported and the disagreement is called out.

---

## 0. The 8 targets and their prior-art status

| # | Target | Prior-art verdict | Anchor |
|---|---|---|---|
| 1 | Patient delete cascades away appointments | **Deferred twice, then "settled" as block-with-clear-message — but the formalization was skipped** | `features/adoption-qa-d-data-hygiene/spec.md:49,53`; `features/adoption-qa-h-residual-hygiene/spec.md:18` + `progress.md:22` |
| 2 | Partial `PUT /appointments/{id}` wipes procedure type | Deliberately omitted once, then closed **only** for the plan link (tri-state, AC-17) | `features/clinical-loop-integration/progress.md:54`; `features/treatment-plan-workspace/spec.md:273-277` |
| 3 | devis→facture re-bills collected money | **Explicitly deferred with a full diagnosis; "needs a payment-carry-over design"** | `features/treatment-plan-workspace/spec.md:131-134`, `:432-433`; `progress.md:486-492` |
| 4 | No payment correction | **Deferred as "a separate, explicit feature"** | `features/unified-billing-ledger/spec.md:72` |
| 5 | Avoirs write-only | **MISSED** — never declared out of scope; created by two auto-approved deviations | `features/adoption-qa-f-avoir-credit-note/progress.md:27-28` |
| 6 | No optimistic concurrency | Deferred as a **repo-wide v1 posture**, never as a feature | `features/windows-desktop-app/spec.md:229`; `features/stock-persistence/reviews/feature-review.md:65` |
| 7 | Installment revenue in the wrong month | **Deferred and disclosed as approved deviation DEV-4** | `features/unified-billing-ledger/progress.md:91-95`, `spec.md:71` |
| 8 | Patient contact sentinels | **MISSED** — zero spec coverage; only an in-code comment acknowledges it | `CreatePatientCommand.cs:92-93` |

Consciously deferred: 1 (partly), 2 (partly), 3, 4, 6, 7. Genuinely missed: 5, 8.
For 3, 4 and 7 the prior specs said *"separate design"*, not *"never"* — this spec closes them.

---

## 1. Patient delete — the damage is much larger than appointments

### 1a. The duplicate-config conflict (exhaustive sweep)

`ApplyConfigurationsFromAssembly` (`ApplicationDbContext.cs:90`) applies configs **alphabetically by class name**; both
sides are `ConfigurationSource.Explicit`, so **last one wins**. Six relationships are configured twice; exactly **one**
conflicts:

| Relationship | Site A | Site B | Winner | Conflict |
|---|---|---|---|---|
| **Patient ↔ Appointment** | `AppointmentConfiguration.cs:68-72` **SetNull** | `PatientConfiguration.cs:122-125` **Cascade** | `PatientConfiguration` (P > A) → **Cascade** | 🔴 **yes** |
| Patient ↔ PatientFlag | `PatientConfiguration.cs:112` Cascade | `PatientFlagConfiguration.cs:40` Cascade | PatientFlag | no |
| Patient ↔ PatientFile | `PatientConfiguration.cs:117` Cascade | `PatientFileConfiguration.cs:52` Cascade | PatientFile | no |
| ProcedureType ↔ Appointment | `AppointmentConfiguration.cs:74` SetNull | `ProcedureTypeConfiguration.cs:64` SetNull | ProcedureType | no |
| DentalRecord ↔ DentalRecordTooth | `DentalRecordConfiguration.cs:77` Cascade | `DentalRecordToothConfiguration.cs:28` Cascade | DentalRecordTooth | no |
| Medication ↔ ActiveIngredient | `MedicationConfiguration.cs:53` Cascade | `MedicationActiveIngredientConfiguration.cs:28` Cascade | Medication | no |

Ground truth (not inference): `ApplicationDbContextModelSnapshot.cs:1974-1977` shows `OnDelete(DeleteBehavior.Cascade)`,
and the physical constraint is `InitialCreate.cs:91-95` `onDelete: ReferentialAction.Cascade`, never altered since.
Only the `SetNull` was lost — `IsRequired(false)` survived (the FK is optional because `Appointment.PatientId` is `Guid?`).

### 1b. What a patient delete destroys today

| Table | OnDelete | Effect |
|---|---|---|
| `Appointments` | **Cascade** | 🔴 entire appointment history hard-deleted |
| `ToothStates` | Cascade | 🔴 entire persistent odontogram destroyed |
| `DentalRecords` (→ teeth, acts) | Cascade | 🔴 all fiches de soins destroyed |
| `PatientFiles` | Cascade | rows deleted, **MinIO/disk blobs orphaned** — nothing deletes storage |
| `PatientFolders` | Cascade | deleted |
| `PatientFlags`, `RecurringAppointments`, `PatientMedicalHistories`, `PatientFamilyHistories`, `LabWorkOrders`, `WaitingListEntries` | Cascade | deleted |
| `Notifications` | SetNull | orphaned rows survive |
| `MedicalDocuments` | **Restrict** | 🔴 **the only guard** — blocks the whole delete |
| `Invoices` | **no FK at all** | 🔴 orphaned silently (`InvoiceConfiguration.cs:21` is a bare `Property` + index) |
| `TreatmentPlans` | **no FK at all** | 🔴 orphaned silently (`TreatmentPlanConfiguration.cs:21`) |

`DeletePatientCommand.cs` (64 lines) checks **only** clinic resolution + tenant. No pre-check, no `ILogger`.
Its `catch (DbUpdateException)` message — *"Impossible de supprimer ce patient : des données liées (factures,
rendez-vous, dossiers) existent."* (`:53-58`) — **is currently a lie**: rendez-vous cascade away rather than blocking,
and factures have no FK. Only `MedicalDocuments` can produce it.

⚠️ **Cross-item hazard:** `DbUpdateConcurrencyException : DbUpdateException`, so once a concurrency token exists,
`DeletePatientCommand.cs:53` swallows conflicts into that same misleading message. Same shape at the three
numbering-retry filters (`AcceptTreatmentPlanCommand.cs:86`, `IssueInvoiceCommand.cs:98`,
`CreateCreditNoteCommand.cs:126`) — a conflict would be retried as if it were a numbering collision.

---

## 2. Appointment partial update

### 2a. Provided-vs-omitted, per field (`UpdateAppointmentCommand.cs`)

| Field | Line | Guard | Defect |
|---|---|---|---|
| `AppointmentDateTime` | 127 | `.HasValue` | none |
| `DurationMinutes` | 164 | `.HasValue && > 0` | silently ignores `0`/negative instead of 400 |
| `DoctorName` | 174 | `!= null` | **unclearable** via null |
| `DoctorId` | 180 | `.HasValue && != current` | **unclearable** — `SetDoctorId(null)` unreachable; null branch is dead code |
| `Notes` | 191 | `!= null` | **unclearable** — FE sends `notes \|\| undefined`, so clearing the box is a silent no-op |
| **`ProcedureTypeId`** | **198** | **`!= appointment.ProcedureTypeId`** | 🔴 **the wipe — no provided/omitted concept at all** |
| `Status` | 226 | non-blank + `TryParse` | unparseable status **silently ignored** |
| `TreatmentPlanItemId` | 288 | `…Specified && != current` | ✅ already tri-state |

`ProcedureTypeId` is the **only** field with the omitted→wipe defect. Line 221 `SetProcedureType(null, null, null)`
nulls all three columns (`Appointment.cs:199-205`).

### 2b. The tri-state pattern to generalize (`UpdateAppointmentCommand.cs:28,40-53`)

Backing field + setter that flips a `[JsonIgnore] public bool XSpecified { get; private set; }`. System.Text.Json
invokes a setter **only** for keys physically present in the payload, so the setter doubles as a "was assigned" probe.

### 2c. Who triggers the wipe today

| Caller | Payload | Result |
|---|---|---|
| `edit-appointment-dialog.tsx:270-277` "Enregistrer" | 6 keys incl. explicit `procedureTypeId` | usually safe; unsafe if the type was deactivated (`procedureTypesApi.list(false)` fetches active-only) |
| **`edit-appointment-dialog.tsx:326-328` "Annuler le rendez-vous"** | **`{ status: "cancelled" }` alone** | 🔴 **wipe** |
| **`AIActionService.cs:982-988` AI-chat cancel** | `{ Id, Status = "Cancelled" }` | 🔴 **wipe — and bypasses the controller entirely** |

Verified negatives: the calendar has **no** drag/resize (no `onDrop`/`draggable` in `appointment-calendar.tsx`);
waiting-list promote creates a new appointment; post-visit review calls `MarkVisitCompleted()` on the entity directly.

**⇒ The fix must live in the command, not a controller DTO** — `AIActionService` constructs the command directly.

`web/lib/api/client.ts:135-145` uses plain `JSON.stringify`, which **drops `undefined` keys** — so the raw JSON
already carries the omitted-vs-explicit-null distinction. The `…Specified` sentinel is the correct fix vector.

Test gap: **zero** tests touch `ProcedureTypeId` on the update path. `AppointmentSyncMappingTests.cs:158` sends a bare
`new UpdateAppointmentCommand { Id = … }` and passes only because its fixture has no procedure type.

---

## 3. Invoice / Payment / CreditNote model

### 3a. `Invoice` (`Domain/Entities/Invoice.cs`)

- `TotalHt/TotalVat/TotalTtc` and **`AmountCollected` are stored columns**, not derived. `RecomputeTotals()` is
  private and called from only 3 places (ctor 108, `SetLines` 134, `Issue` 171).
- `Outstanding => Math.Max(0m, TotalTtc - AmountCollected)` (`:69`) — **floored at 0, and credit notes do not
  reduce it.**
- VAT/timbre are **frozen at `Issue()`**; while Draft they are all zero, so a draft's `TotalTtc == TotalHt`.
- `RecordPayment` (`:195-210`): requires `Issued|PartiallyPaid`; `amount > 0`; refuses
  `AmountCollected + amount > TotalTtc`; appends a `Payment`; `AmountCollected += amount`; status ⇒
  `Paid` if `>= TotalTtc` else `PartiallyPaid`. `paidOn` is **caller-supplied**.
- 🔴 **No public method removes/voids a payment, decrements `AmountCollected`, or moves status backwards.**
  Grep-verified: no `Reverse`/`VoidPayment`/`RemovePayment`/`DeletePayment` anywhere in `api/`.

**The single most important design fork** — `Cancel` (`:216-235`) guards on **`_payments.Count > 0`**, not
`AmountCollected > 0`:
> *"Une facture avec des paiements enregistrés ne peut pas être annulée. Établissez un avoir."*
If reversal **deletes** `Payment` rows, cancelling a once-paid invoice silently becomes possible.
If reversal **appends** a marker row, cancellation stays blocked forever.

Status machine: `Draft=0, Issued=1, PartiallyPaid=2, Paid=3, Cancelled=4`. No path exists from
Paid → PartiallyPaid → Issued.

### 3b. `Payment` (31 lines) — `Domain/Entities/Payment.cs`

`{ InvoiceId, Amount, Method, PaidOn, CreatedAt }`. Docstring `:7`: *"Immutable once created."*
No `IsVoided`, no `VoidedAt`, no `ReversedByPaymentId`, no `Note`/`Reference`. Ctor guard `amount <= 0` makes
negative amounts impossible. `PaymentMethod` = `Cash|Cheque|Card|Transfer` only.

### 3c. `CreditNote` (avoir)

Aggregate root, clinic-scoped, **soft-linked** to the invoice (`InvoiceId`, no FK, no navigation).
`{ ClinicId, InvoiceId, Number, IssueDate, Amount, Reason, Method?, RefundedOn, CreatedAt }`.
Own per-clinic-per-year `AAAA-NNNN` sequence, **unfiltered** unique index (contrast the invoice's filtered one).
`IssueDate` is **always `UtcNow`**, not caller-settable, while `RefundedOn` is — backdating splits across two fields.
The invoice is **never mutated** by an avoir: no `AmountCollected` decrement, no status change, no `UpdatedAt` touch.

**Write-only, precisely:**
- `ICreditNoteRepository` has only `GetMaxSequenceForYearAsync`, `GetTotalForInvoiceAsync`,
  `GetRefundedBetweenAsync`, `AddAsync` — **no `GetByIdAsync`, no `GetByInvoiceIdAsync`, no list.**
- `CreditNoteDto` exists (8 fields) but is produced **only** by `CreateCreditNoteCommand`.
- `InvoiceDto` has **no credit-note field of any kind**.
- No PDF, no `GET` endpoint, no frontend read. `invoicesApi.createAvoir` discards the returned object
  (`invoices-table.tsx:136-146`) — no number shown, no PDF offered.
- `CreateCreditNoteCommand.cs:93-98` **silently drops an unparseable `Method`**, unlike `RecordPaymentCommand.cs:46-49`
  which rejects it.

### 3d. Where avoirs net (and don't)

| Read | Nets avoirs? | Anchor |
|---|---|---|
| `GetCaisseSummaryQuery` | ✅ yes | `:77-78` |
| `GetInvoiceRevenueQuery` | ⚠️ **only when both `From` and `To` are supplied**; the no-period branch ignores refunds | `:64-75` |
| `GetDashboardStatsQuery` (`MonthlyRevenueCollected`) | ❌ no | `:94-98` |
| `GetPatientBillingSummaryQuery` | ❌ no (reports outstanding only) | — |
| `Invoice.Outstanding` / `GetOutstandingByPatientAsync` | ❌ no — a fully-refunded invoice still reads `Paid`, `Outstanding = 0` | `Invoice.cs:69` |

`features/treatment-plan-workspace/spec.md:432-433` re-declares the dashboard netting as knowingly open.

---

## 4. TreatmentPlan / Installment

### 4a. `Installment` — no payment history exists

```csharp
public decimal AmountPaid;        // cumulative running total ONLY
public PaymentMethod? LastMethod; // latest payment's method
public DateTime? LastPaidOn;      // latest payment's date
```
Docstring `Installment.cs:9-10` says so explicitly: *"v1 keeps only the latest method/date, not a full payment
history."* `RecordPayment` (`:59`) is additive-only; `AmountPaid` is monotonically non-decreasing.
`Revise` (`:43`) refuses `amount < AmountPaid`. `CreditNote` is hard-bound to `InvoiceId` — **avoirs cannot
correct a plan payment.** There is no route, command, API method or UI control for reversal.

Downstream consequences of the missing history:
- `GetInstallmentReceiptPdfQuery.cs:80` prints the **cumulative** `AmountPaid` as "the" receipt amount, dated
  `LastPaidOn` — a second partial payment silently reissues a receipt for the *sum*.
- `plan-timeline.tsx:105-107` documents that a twice-topped-up échéance collapses into one feed entry.

### 4b. The wrong-month bug (`TreatmentPlanRepository.cs:87-100`)

```csharp
.SelectMany(p => p.Installments)
.Where(i => i.LastPaidOn != null && i.LastPaidOn >= from && i.LastPaidOn <= to)
.SumAsync(i => (decimal?)i.AmountPaid, ct) ?? 0m;
```
All-or-nothing bucket keyed on the **single** `LastPaidOn`: if it falls in the window, the **entire cumulative
`AmountPaid`** is counted; otherwise nothing.

1. Pay 300 on 20 Jan + 200 on 5 Feb → January reports 0 (**retroactively** — it reported 300 before 5 Feb),
   February reports 500.
2. **Historical reports are not stable.** Re-running "encaissé janvier" after a February top-up returns a different
   number for a closed month.
3. A payment on a plan later **Cancelled** disappears from every historical figure (`DebtBearingPlanStatuses` filter).

Callers: `GetCaisseSummaryQuery.cs:74`, `GetDashboardStatsQuery.cs:96`.
**The invoice side is already event-sourced and correct** (`InvoiceRepository.cs:89-96` sums `Payment` rows by
`PaidOn`) — that is the model to mirror.

`Installments` has **only** the FK index — no index on `LastPaidOn` or `DueDate`.

### 4c. The bridge (`CreateInvoiceFromTreatmentPlanCommand.cs`)

Seeds lines at full `PlannedCost`, quantity 1 (`:102-103`). `plan.AmountPaid` is **never read in this file**.
Invoice is created with `AmountCollected = 0`. The moment it leaves Draft,
`PlanBillingRules.RepresentsItsPlan` suppresses the plan's outstanding in all three outstanding reads → the collected
money **vanishes from the balance and reappears as invoice debt**.

Symmetrically on the **cash** side there is **no** de-dup: the original installment collection stays in
`GetInstallmentCollectedBetweenAsync`, and paying the invoice adds a *second* receipt to `GetCollectedBetweenAsync`.

⚠️ **DEV-5** (`features/treatment-plan-workspace/progress.md:486-489`) anticipates exactly this:
> *"If the deferred 'collected installment money surviving the bridge' fix ever carries payments onto the invoice,
> a de-dup on the collected side becomes necessary **at that moment**."*

Constraint: `Invoice.RecordPayment` requires `Issued|PartiallyPaid`, so a carry-over payment cannot be seeded on a
**Draft** invoice without either relaxing that guard or deferring the carry-over to `Issue()`.

### 4d. `PlanBillingRules` — the single de-dup authority

`DebtBearingPlanStatuses = { Accepted, InProgress, Completed }`; `CarriesDebt`; `RepresentsItsPlan(status) =>
status != Draft && status != Cancelled`; two `BilledPlanIds` overloads.
Class doc `:18-22` is load-bearing:
> *"Both rules apply to **outstanding** balances … They deliberately do **not** apply to collected cash … suppressing
> a bridged plan's collections would erase real money from the caisse rather than de-duplicate it."*

Call sites: `GetPatientBillingSummaryQuery.cs:73,79`, `GetReceivablesQuery.cs:56`, `GetDashboardStatsQuery.cs:105`,
`AmendTreatmentPlanCommand.cs:168`, `ReviseTreatmentPlanInstallmentsCommand.cs:70`,
`TreatmentPlanRepository.cs:93,108`.

---

## 5. Patient contact — sentinels and the null-safety sweep

### 5a. Current model

`Email.Value` and `PhoneNumber.Value` are **non-nullable `string`**; both ctors throw on blank. There is no
`TryCreate`. `Patient.Email`/`Patient.PhoneNumber` are **non-nullable navs**, required positional ctor params
(`Patient.cs:52-75`), and `.IsRequired()` in `PatientConfiguration.cs:35-49` → both DB columns are **NOT NULL**.

**The precedent to copy already exists in the same entity:** `Patient.EmergencyContactPhone` is a nullable owned VO
(`Patient.cs:21`, `UpdateEmergencyContact(string?, PhoneNumber?)` `:116-121`, config `:91-96` with no `IsRequired`).
⚠️ EF still emits `.IsRequired()` on the inner `Value` of an *optional* owned nav in the snapshot — that is normal and
does **not** mean NOT NULL; DB nullability comes from the nav being optional.

### 5b. Two independent sentinel pairs

| Site | Values |
|---|---|
| `CreatePatientCommand.cs:101-106` | `"noemail@example.com"` / `"0000000000"` |
| `GoogleCalendarSyncService.cs:669-670` (Google→App placeholder patient) | `"unknown@example.com"` / `"000-000-0000"` |

**Comparison/read sites: ZERO.** Nothing compares against them, no migration writes them, no test asserts them.
Removing the sentinel breaks no equality check — but **existing DB rows contain them**, so a data backfill is needed
(all four literals) *after* the columns are made nullable.

### 5c. Every `.Value` dereference that must be made null-safe

🔴 **Unguarded — will NRE (10 sites, 6 files):**

| Anchor | Consequence |
|---|---|
| `GetPatientsQuery.cs:68` | 🔴 **worst** — `NormalizeForSearch(p.PhoneNumber.Value)` inside an in-memory `Where` over the whole clinic; one phone-less patient NREs the entire patient search |
| `GetPatientsQuery.cs:87,88` | NRE on the patient list |
| `GetPatientQuery.cs:71,72` | NRE on patient detail |
| `CreatePatientCommand.cs:242,243` | NRE on the create response |
| `UpdatePatientCommand.cs:211,212` | NRE on the update response |
| `AIActionService.cs:659-660, 739-740` | NRE in AI chat (`view_patient`, `search_patient`) |

🟢 **Already null-safe (3 sites):** `NotificationJob.cs:118` (`ToE164(patient.PhoneNumber?.Value)` → French failure),
`GetPatientsToRecallQuery.cs:91`, `GetClinicReminderStatusQuery.cs:79` (`MaskRecipient`).

**Verified NOT affected:** `TeifXmlGenerator.cs` has zero `Email|Phone|Tel` matches (patient contact is not in the TEIF
payload); `CnamBs1BulletinRenderer.cs:399` reads `patientPhone` from the frontend-supplied content map, not the entity;
no reminder sender reads `Patient.PhoneNumber` except `NotificationJob`.

**DTO/write sites to change:** `PatientDto.cs:11-12` (`string` → `string?`);
`UpdatePatientCommand.cs:105-110` — blank currently means *"keep existing"*, so **there is no way to clear either
field today**; `GoogleCalendarSyncService.cs:669-670` must pass `null`.

**Frontend is already tolerant:** `patients-table.tsx:226,229`, `patient-summary-modal.tsx:123,131`,
`patients/[id]/page.tsx:1443,1448` all use `|| "Non renseigné"`. `edit-patient-dialog.tsx` already labels
**Email `(optionnel)`** (`:655`) and **phone required with `*`** (`:638`) — so the FE and backend already disagree.

---

## 6. Optimistic concurrency — precise absence, and the cheapest seam

Grep-verified **zero hits** repo-wide for: `IsRowVersion`, `IsConcurrencyToken`, `RowVersion`, `xmin`,
`UseXminAsConcurrencyToken`, `ConcurrencyCheck`, `DbUpdateConcurrencyException`, `ValueGeneratedOnAddOrUpdate`.
Not one entity, owned type or shadow property carries a token. All 40+ entities are last-write-wins.

Base classes are bare: `Entity<TId>` (identity equality only), `AggregateRoot<TId>` (8 lines, ctors only — the
domain-events list was removed). `CreatedAt`/`UpdatedAt` are duplicated per entity, **not inherited**, so there is no
existing central hook. `NotificationRead` is a plain class, not an `Entity<>`.

**Three seams, least disruptive first:**
1. **Npgsql `UseXminAsConcurrencyToken()` in a loop over `modelBuilder.Model.GetEntityTypes()`** — `xmin` is a Postgres
   system column, so **no migration at all**. There is already an idiomatic per-entity-type loop to extend at
   `ApplicationDbContext.cs:135-156` (the UTC value-converter loop). Must skip `entityType.IsOwned()`.
2. Shadow `byte[]`/`uint` per root — one migration adding a column per table.
3. A property on `Entity<TId>` — cleanest conceptually but Domain has **zero package references**.

`ApplicationDbContext.SaveChangesAsync` (`:239-249`) only calls `ConvertDateTimesToUtc()` then base — no audit
stamping, no transaction, no concurrency handling. `UnitOfWork.SaveChangesAsync` is a bare delegate with no try/catch.
Nothing wraps writes in a transaction; the dominant shape is *mutate → one `SaveChangesAsync` → post-commit
best-effort side effects*. `CreatePatientCommand` calls `SaveChangesAsync` **twice** (`:188`, `:232`) with no
enclosing transaction.

---

## 7. Error contract — what a 409 must respect

- `Result` carries **a single `string? Error`**. No code, no kind, no payload. A handler **cannot** signal
  "conflict" to the controller today. `features/graceful-error-handling/spec.md:55` froze this.
- `ApiControllerBase.HandleFailure(result, statusCode = 400)` renders **exactly** `{ "error": "<message>" }` and does
  **not** inspect the message — the *action* picks the status. Usage: 139 × 400, 19 × 404. **Only 400 and 404 are ever
  produced.** Pinned by `ApiControllerBaseTests.cs:31-33` (key is exactly `error`).
- `ExceptionMiddleware` maps `ForbiddenAccessException`→403, `NotFoundException`→404,
  `FluentValidation.ValidationException`→400, everything else→500 with the shared generic message. Only **two**
  custom exceptions exist. A third (`ConflictException`) + a third `case` is the cheapest path that leaves `Result`
  untouched.
- 🔴 **`features/graceful-error-handling/spec.md:19,58` explicitly fenced off 409**: *"No new error-code system, no
  409/422 redesign"*. Introducing one is not a body-contract violation but **is** the exact thing that spec deferred —
  the new spec must say it is lifting that deferral.
- **Nothing in the repo returns or handles 409 today** (grep-verified in both halves). The only near-precedent for a
  lost race is the invoice-numbering retry → `Result.Failure("Impossible d'attribuer un numéro … Veuillez réessayer.")`
  surfacing as a plain 400.
- `ApiError` (`client.ts:3`) carries `status`, but **every call site is status-blind** except three `=== 0` offline
  checks (`ai-chat.tsx:308`, `appointment-calendar.tsx:73`, `appointments/page.tsx:163,179`). A 409 with a proper
  `{ error }` body renders correctly today with **zero** client changes — it just looks like a 400.
- ⚠️ `features/graceful-error-handling/progress.md:26` claims `AbortController`/`ensureSuccess`/`toApiError` landed;
  grep returns **zero matches**. Treat that progress.md as aspirational.

**No "your data is stale" UX exists anywhere.** The realtime layer refetches silently
(`use-clinic-realtime.ts:15-17`), reloading pages underneath open modals (`patients/[id]/page.tsx:193-202`,
`treatment-plans/[id]/page.tsx:52-55`). The **only** don't-clobber guard is `clinic-settings.tsx:272-278`, which
suppresses the refresh while editing and then silently drops the peer's change.

French message conventions: sentence case, always a trailing period, vouvoiement, never names an id.
Not-found idiom `"<Entité> introuvable."`; catch-all `"Erreur lors de <l'action>."`; recoverable failures append
`"Veuillez réessayer."`.

---

## 8. Frontend surfaces

### 8a. There is **no invoice detail view**

No `factures/[id]/`, no drawer, no expandable row, **no `sheet.tsx` primitive** in `components/ui/`.
`InvoiceDto.payments` is referenced in exactly **one** place in the whole frontend (`payment-modal.tsx:57,64,65`,
to diff out the new payment id). `invoicesApi.get(id)` exists and **is never called**.
`PaymentDto` = `{ id, amount, method, paidOn }` — no `createdAt`, no void marker.

**⇒ There is nowhere today for a per-payment row, and therefore nowhere for a per-payment "Corriger" action.**
The spec must either create the first invoice detail surface or hang the action off the row action cluster like
"Établir un avoir" does.

### 8b. `invoices-table.tsx` — the surface to extend

Row actions are icon-only ghost buttons with `title=` tooltips, gated by status. Avoir button appears when
`(Paid || PartiallyPaid) && amountCollected > 0` (`:406`). No optimistic updates anywhere — every mutation calls
`afterMutation()` = `load() + onChanged?.()`. Per-row busy via a single `busyId`.
Load-bearing comment `:336-338`: *"A note with recorded payments can't be voided (would erase collected cash) — only
an issued, not-yet-paid note is cancellable; corrections go through an avoir (finding #8)."*

**Three modal patterns coexist:** `AlertDialog` for destructive-without-reason; plain `Dialog` + `Textarea` when a
typed **motif** is required (Cancel becomes **`Retour`**, confirm is imperative-explicit); plain `Dialog` for forms.
No dialog anywhere requires typing a confirmation word — a required free-text `Motif` is the strongest gate.

⚠️ `patients/[id]/page.tsx:1380-1394` mounts `<InvoicesTable>` **without `onChanged`**, so the « Solde patient » card
above it does **not** refresh after an invoice mutation.

### 8c. Formatting

`formatDT` (`web/lib/format.ts:8-15`) — `Intl.NumberFormat("fr-TN", { min/maxFractionDigits: 3 })` + `" DT"`.
`1234.5` → `"1 234,500 DT"`. Null → `"0,000 DT"`. `roundMillimes` mirrors `InvoiceCalculator.RoundMoney`.
`formatDateFr` → `"17 juil. 2026"`, fallback `"—"`. Money inputs are always
`<Input type="number" min="0" step="0.001">` labelled `Montant (DT)`.

### 8d. Payment recording UX (the pattern a correction must match)

`payment-modal.tsx` — 3 fields (Montant/Mode/Date), prefilled to `invoice.outstanding`, client-side cap against
`outstanding`, inline red banner for errors (**not** a toast), and on success the canonical
`toast.success("Paiement enregistré", { action: { label: "Télécharger le reçu", … } })` (`:67-77`).
`installment-payment-modal.tsx` is its identical twin for plans.

---

## 9. Test conventions and the guards this feature will trip

- 106 files / ~655 facts. **Pure unit + Moq — no DB, no in-memory provider, no Testcontainers, no
  `WebApplicationFactory`.** Repository SQL is **never executed by any test**.
- Naming: `<Subject>Tests` (subject may be a *behaviour*, e.g. `MoneyReadConsistencyTests`); methods are
  `Pascal_Snake_Case` full sentences (`A_Bridged_Plan_Is_Counted_Once_On_All_Three_Reads`).
- **Spec-ID traceability is mandatory** (`UnitTests/CLAUDE.md:26`) — class-level XML `<summary>` plus a per-test
  `// [AC-n]` comment. Sub-lettered ids (`AC-12a`) are normal.
- Harness: readonly `Mock<T>` fields → real-domain-ctor fixtures → expression-bodied `CreateHandler()` →
  `Arrange()`/`Authenticated()` → assert `result.IsSuccess/IsFailure` + `_uow.Verify(SaveChanges, Times.Never)` on
  every rejection. Never `Assert.Throws` on a handler. `NullLogger<T>.Instance` for loggers. No FluentAssertions.
- Domain tests: bare `Assert.Throws<InvalidOperationException>(...)` without asserting the message; when the message
  matters, `Assert.Contains("<fragment>", ex.Message)` — never the whole French string.

**Guards that will trip:**

| Guard | Trips when |
|---|---|
| `Domain/PlanBillingRulesTests.cs:50` | any new `TreatmentPlanStatus` **or** `InvoiceStatus` member — must be classified in both `CarriesDebt` and `DebtBearingPlanStatuses` |
| `Api/TreatmentPlansControllerAuthorizationTests.cs:90` | **any** new action on `TreatmentPlansController` — must be added to `AdminOrDoctorActions` or `AnyAuthenticatedActions`; financial-reversal actions need `[Authorize(Policy = AdminOrDoctor)]` |
| `Features/Billing/MoneyReadConsistencyTests.cs` | all six assertions; a new money read joins as a fourth accessor. ⚠️ Its `Wire()` **hand-reimplements the repository SQL in LINQ** (`:132-150`) — a divergence is invisible to the suite |
| `Api/ControllerAuthorizationCoverageTests.cs:56,67` | only on a new `[AllowAnonymous]` or a controller/action rename |
| `Common/Behaviors/RealtimeResourceResolverTests.cs:53` | on a `Features/<Area>` folder rename; convention says add an `[InlineData]` per new mutating command |
| `*TenantIsolationTests` | mandatory per `UnitTests/CLAUDE.md:27` — a case per new clinic-scoped verb |

> **Correction to a common belief:** `MoneyReadConsistencyTests` pins **three** reads (« Solde patient », « Créances »,
> dashboard), not four. `GetCaisseSummaryQuery` has **no test anywhere** — grepping `Caisse` across the test project
> returns zero hits. Adding it to the harness is a genuine, cheap scope item for this feature.

**Environment:** no CI of any kind (no `.github/`, no pipeline file), no test script. `dotnet test` fails at
assembly-load with `0x800711C7` (Windows Smart App Control) — environmental. Workaround:
`dotnet build -p:OutDir=<scratch>/utbuild/` then `dotnet vstest`. The de-facto gate is `dotnet build` 0/0 +
`npx tsc --noEmit` 0. **Stop the API before `dotnet ef`** (a running process makes `migrations add` emit an *empty*
migration) and **never pass `--no-build`**.

---

## 10. Spec conventions to follow

Section order (dominant for money specs):
**Overview → What Changes → API Contract → Data / Schema Changes → Acceptance Criteria → Out of Scope →
Edge Cases (Critical only) → Tests → Documentation to update on completion**

- **There are no `As a … I want …` user stories in any spec in this repo.** `## What Changes` bullets are the unit of
  work, sliced as `### Slice A — <name> (no migration)` with the migration count in the heading.
- ACs are flat `- **AC-1:** <one testable sentence>.` bullets with **bold group headers** between blocks.
  Insertions use letter suffixes (`AC-12a`) rather than renumbering, so existing `[AC-N]` test tags stay valid.
- `## Out of Scope` items are `- **<bold noun phrase>.** <why, plus what makes the later change additive>` — always
  justified, never a bare list.
- Edge-case heading is literally `## Edge Cases (Critical only)`.
- API contract idiom: `### New — \`POST /api/…\` · **\`[Authorize(Policy = AdminOrDoctor)]\`**` then
  `Request:` / `Response 2XX:` / `Errors:` lines.
- The last 20+ features skip `plan.md`/`stories/` and go spec → `progress.md` directly.

**Hard rules the spec must respect:**
- Handler shape: *resolve clinic → validate/load with per-aggregate `ClinicId` re-verification → mutate via aggregate
  methods → repository `AddAsync/UpdateAsync` → **one** `SaveChangesAsync` → map → `Result<TDto>.Success`.*
- Return `Result<T>`, **never throw for business failures**. **Do not add FluentValidation validators.**
- Domain has **zero package references**; all setters `private set`; invariants throw `ArgumentException`/
  `InvalidOperationException` with **French** messages; child collections are `private readonly List<>` exposed as
  `IReadOnlyCollection<>`.
- Money is TND millimes, `decimal(18,3)`, rounded **only** through `InvoiceCalculator`.
- Soft links (`Invoice.TreatmentPlanId`, `Appointment.TreatmentPlanItemId`, `InvoiceLine.DentalRecordId`) are
  deliberately FK-less — keep them that way.
- `PlanBillingRules` is the single de-dup authority, and its rules apply to **outstanding only, never collected cash**.
- The billed-plan block lives in the **handlers**, not the aggregate.
- Only two authorization policies matter here: `AdminOnly` (reference data/settings) and `AdminOrDoctor`
  (reversing/altering an issued financial document).
- **No entity in this codebase records *who* mutated a financial document** — there is no audit table
  (`features/treatment-plan-workspace/spec.md:316-321`).
