# Implementation Progress — Adoption Gaps Remediation

**Feature:** `features/adoption-gaps-remediation/`
**Story:** Story 1 — Close the adoption gaps (`Layer: Full`, four ordered parts)
**Branch:** `feature/windows-desktop-app` (owner's decision, per the story's entry criteria)

## Part tracker

| Part | Group | Name | Migration | Status |
|------|-------|------|-----------|--------|
| 1 | C | Remove El Fatoora / TTN | `RemoveEInvoicing` | **implemented** |
| 2 | A | Money integrity | `AddDentalRecordPaymentMethod` | **implemented** |
| 3 | B | Cheque life-cycle | `AddChequeBankedStamp` | **implemented** |
| 4 | D | Remaining defects | `NullablePatientDateOfBirth` (renamed, DEV-9) | **partial, uncommitted** — AC-18/20(be)/21/22/25(part)/26 done; AC-19/23/24 not started |

## Working tree note (start of session, 2026-08-08)

The branch carried the **~25-file security-review batch** the story's entry criteria told me to exclude from
every commit (R-13). **That turned out to be impossible for five of them**: `Program.cs`, `Extensions.cs`,
`ClinicsController.cs`, `api/ClinicManagement.API/CLAUDE.md` and `api/ClinicManagement.Infrastructure/CLAUDE.md`
are files Part 1 itself must rewrite (drop the `dispatch-einvoices` registration, drop 7 DI registrations, drop
the TTN settings surface, drop the TTN doc sections). Leaving their pre-existing hunks out of Part 1's commit
would have meant committing a non-building tree.

Surfaced to the owner, who chose **commit the security batch first**. Landed as `cf903f1`
*chore(security-review): land the pre-existing hardening batch* — verified green before committing
(0 errors, 57 pre-existing warnings; 2230/2230 tests). Part 1 therefore starts from a **clean tree** and R-13's
intent (no unrelated work swept into the feature's commits) is satisfied by a different route than the one the
story wrote down.

## Environment note

The API (`:5000`, PID 28500) and the frontend (`:3000`, PID 35744) were both running at session start, which
the entry criteria forbid — they lock `api/**/bin` and `web/.next`. Both stopped. **Restart with
`/start-clinic` when the session ends.**

MinIO reports `unhealthy` in `docker ps`. Not blocking for Part 1 (no blob path is touched); Part 1 leaves
e-invoice blobs in object storage **by decision**.

## Baselines captured before any change

| Baseline | Value |
|----------|-------|
| `dotnet build --no-incremental` | 0 errors, **57 warnings** — all pre-existing (`CS8618` EF private ctors, `CS8602`/`CS8600` nullable derefs, `CS8981` lowercase migration names, `CS0618` Hangfire `UsePostgreSqlStorage`) |
| Unit suite | **2230 passed**, 0 failed |
| `verify-schema` | *schema matches the model*; `ttn-identity-is-complete` present and clean. Saved to scratchpad as `verify-schema-BEFORE.txt` (258 lines) for the post-migration diff |

## Deviations

### DEV-1: AC-13's validation grep cannot pass as written
**Date:** 2026-08-08
**Story:** 1, Part 1
**Category:** Technical (verification command, not implementation)
**Original Plan:** Validate AC-13 with
`grep -riE "ttn|fatoora|teif|einvoice" api/ web/ --exclude-dir=Migrations`, expecting no output.
**Actual Implementation:** The pattern is corrected before use (see below).
**Justification:** As written the command can **never** return nothing, for three independent reasons found by
running it:
1. **`einvoice` matches `IssueInvoice`.** Case-insensitively, `iss-u-**e-invoice**-command` contains the literal
   `einvoice`. So `IssueInvoiceCommand.cs`, `IssueInvoiceCommandHandlerTests.cs` and every doc comment naming
   them match — and all of those **must survive** Part 1. This is the load-bearing one: it would have read as
   "AC-13 still failing" against files there is nothing wrong with.
2. **Build artifacts are in scope.** `bin/`, `obj/`, `logs/`, `web/.next/` and `node_modules/` carry thousands
   of hits (one webpack cache pack alone has 1138) and three backup files under `bin/` are permission-denied.
3. **Two unfixable source-tree files.** `web/package-lock.json:765` contains `TtN` inside a base64 integrity
   hash, and `api/ClinicManagement.Infrastructure/Assets/P61.pdf` is a binary CNAM form that matches.
The corrected command keeps AC-13's *intent* exactly — no route, screen, badge, setting or job mentions TTN,
El Fatoora, TEIF or e-facturation — while being able to reach zero:
```bash
grep -rinE '\bttn|fatoora|\bteif|\beinvoice|e-invoice|e-facturation' api/ web/ \
  --exclude-dir=Migrations --exclude-dir=bin --exclude-dir=obj --exclude-dir=logs \
  --exclude-dir=.next --exclude-dir=node_modules --exclude=package-lock.json --exclude=*.pdf
```
`\beinvoice` is what excludes `IssueInvoice` (the `e` there is preceded by `u`, a word character) while still
matching `EInvoiceStatus` at a word start. **Re-proved after tightening**, per the too-loose-check trap: the
corrected pattern still matches a deliberate `EInvoiceStatus` / `TtnIdentifier` probe.
**Impact:** Verification only — no implementation change. The second grep (the model snapshot, R-6) is
unaffected and still run separately.
**Approved:** Reported to owner, not blocking — the stated command is simply unrunnable and the intent is
unambiguous.

### DEV-2: AC-13 reaches files the plan's table does not list
**Date:** 2026-08-08
**Story:** 1, Part 1
**Category:** Scope
**Original Plan:** The plan's "Files to Modify (Part 1)" table enumerates the files to change.
**Actual Implementation:** Several files outside that table carry TTN references **in doc comments** and must
also be edited for AC-13 to reach zero — among them `Infrastructure/Services/PushConfig.cs`,
`Infrastructure/Services/RemindersConfig.cs` (both `cref` `TtnConfig` as a precedent),
`Domain/Entities/TreatmentPlan.cs`, `Application/Common/Models/DevisPdfData.cs` (both say "no VAT/timbre/TTN"),
`Infrastructure/Security/LocalDataProtection.cs`, `Infrastructure/Persistence/Configurations/NotificationConfiguration.cs`.
**Justification:** AC-13 is worded as an absolute ("No route, screen, badge, setting or background job
mentions…") and is verified by grep, so a surviving doc comment fails it. These are comment-only edits with no
behavioural effect.
**Impact:** Widens Part 1's file count modestly; no behaviour change. A `cref` to the deleted `TtnConfig` would
additionally break the 0-warning gate, same class as AC-16c's four background-job `cref`s.
**Approved:** Trivial-adjacent, logged for the record.

### DEV-3: AC-13 and AC-16b conflict, and AC-16b wins
**Date:** 2026-08-08
**Story:** 1, Part 1
**Category:** Technical
**Original Plan:** AC-13 requires the source tree to carry no `einvoice` reference; AC-16b requires
`RecurringJob.RemoveIfExists("dispatch-einvoices")` in `Program.cs`.
**Actual Implementation:** The `RemoveIfExists` call stays; AC-13's grep is documented as having exactly **one**
permitted hit (`Program.cs`, two lines: the call and its comment).
**Justification:** `"dispatch-einvoices"` **is** the id of the recurring job sitting in an upgrading install's
Hangfire storage. It is not a name we choose — it is the key that row is stored under, so the removal cannot be
expressed without it, and C-2 ("an install upgrading with a `dispatch-einvoices` row leaves no job") is
unachievable otherwise. AC-13's stated intent is that no *route, screen, badge, setting or background job*
mentions the subsystem, and a `RemoveIfExists` is the opposite of a background job: it is the code that deletes
one. The comment beside it was reworded to « electronic-invoicing » so the literal is the only hit.
**Impact:** AC-13's verification is "one known hit, in `Program.cs`, required by AC-16b" rather than zero.
**Approved:** Reported.

### DEV-4: Part 1 touched more files than the plan's table lists
**Date:** 2026-08-08
**Story:** 1, Part 1
**Category:** Scope
**Original Plan:** ~30 modified files; "6 `CLAUDE.md` files"; the migration "drops 8 `EInvoice*`/`Ttn*` columns
from `Invoices`, 6 from `Clinics`" (14).
**Actual Implementation:** 84 modified + 28 deleted + 3 added. **8** `CLAUDE.md` files (the plan omitted the
**root** `CLAUDE.md` and `UnitTests/CLAUDE.md`). The migration drops **16** columns — **10** from `Invoices`
(`EInvoiceStatus`, `EInvoiceAttemptCount`, `EInvoiceLastError`, `EInvoiceNextAttemptAt`, `EInvoiceSubmittedAt`,
`EInvoiceValidatedAt`, `QrPayload`, `SignedXmlStorageKey`, `TtnIdentifier`, `TtnReceiptStorageKey`) and 6 from
`Clinics` — plus the index.
**Justification:** The extra files are almost all **doc comments** naming `TtnConfig`/`EInvoiceService`/
« El Fatoora » as precedents (see DEV-2), which AC-13 is verified by grep. One was a genuine miss in the plan's
table: `UpdateClinicCommand` still **declared** `TtnEInvoicingEnabled`/`TtnEnvironment` after its usages were
removed — dead request properties the compiler cannot flag, found only by the grep.
**Impact:** None on behaviour; the count is larger than planned, not the scope.
**Approved:** Reported.

## Part 1 — gate results (2026-08-08)

| Gate | Result |
|------|--------|
| AC-13 grep 1 (source tree) | **1 permitted hit** — `Program.cs`'s `RemoveIfExists("dispatch-einvoices")` + its comment, required by AC-16b (DEV-3). Everything else zero |
| AC-13 grep 2 (model snapshot, R-6) | **0 hits** — the regenerated `ApplicationDbContextModelSnapshot.cs` is clean |
| `dotnet build --no-incremental` | **0 errors, 57 warnings** = the baseline exactly, **0 new**. The two warnings landing in files this part changed are both pre-existing (`Clinic.cs` `CS8618` on the EF private ctor; `Program.cs:331` `CS0618` Hangfire `UsePostgreSqlStorage`) |
| Unit suite | **2162 passed, 0 failed** (baseline 2230; the 68 difference is the 7 deleted TTN classes plus the e-invoice cases removed from 3 surviving ones). `DeploymentProfileTests`' four tests pass with the reflected matrix at 14 capabilities |
| `verify-schema` (after) | **schema matches the model** |
| `verify-schema` before/after diff (AC-17) | **exactly two lines removed and nothing else**: `Invoices(EInvoiceStatus, EInvoiceNextAttemptAt): present` and `ttn-identity-is-complete`. This is the AC-17 evidence |
| `npx tsc --noEmit` | clean |
| `npm run check:responsive` | **15/15 checks passed** |
| `npm run build` | clean |
| Device eye pass | **not applicable to Part 1** — every `web/` change is a *removal* (the El Fatoora settings block, a table column + its card badge, three menu items, two banners). No new screen, no new control, no layout added. Recorded rather than skipped silently; Parts 2–4 all add UI and owe the five-width pass |

⚠️ **`GET /api/outbox` returning two queues (AC-15) and the `Valid`/`Submitted`-invoice cancellation (AC-14, C-1)
are covered by unit tests and the compiler (`OutboxDepthDto.EInvoices` was a `required` init prop, so its removal
is compile-checked), but were NOT exercised against a running server.** Same for C-2's « restart leaves no
recurring job ». Those three are owed a manual pass on a live install.

## Environment note — Smart App Control

SAC (documented in `MEMORY.md`) blocked `ClinicManagement.API.dll` for roughly 40 minutes mid-session with
`0x800711C7`, which manifested as **303 test failures and an unrunnable `verify-schema`** — every one of them a
`FileLoadException`, none a real defect. Building to a path outside the repo (the usual workaround) did **not**
help; it cleared on its own once SAC's cloud reputation check completed. Both gates were then re-run and pass.
⚠️ Worth knowing for Parts 2–4: **`dotnet ef` can be driven without the API assembly at all** —
`ClinicManagement.Infrastructure` has an `ApplicationDbContextFactory`, so
`cd api/ClinicManagement.Infrastructure && dotnet ef migrations add <Name>` (no `--startup-project`) scaffolds
while SAC is blocking. That is how `RemoveEInvoicing` was created.

## Session log

- **2026-08-08** — Session 1. Confirmed Part 1 as the target (owner's choice; it is the only hard dependency,
  since Part 2 rewrites files carrying 102 TTN references). Confirmed the dev database is disposable, so
  `RemoveEInvoicing`'s throwing `Down()` (R-2) is acceptable without a fresh backup. Resolved the staging
  conflict above. Captured all three baselines. Started Part 1.

---

## Part 2 — Group A, money integrity (2026-08-08, session 2)

Owner's instruction: **implement the whole of phase A in one pass, no test authorship, report only when every
step is done.** Tests were therefore not *written*, but the three existing classes this part's own changes break
were repaired — leaving them asserting the replaced behaviour would have been a false green, not a deferral.

### What landed, step by step

| Plan step | Landed as |
|-----------|-----------|
| 1. Fiche payment fields | `DentalRecord.PaymentMethod` + the three cheque columns, written only through `SetPayment`, which runs them through the **existing** `ChequeDetails.For` (no new guard). Mapped in `DentalRecordConfiguration` with `Payment`'s own lengths (50 / 200) |
| 2. Rename → `BillDentalRecordCommand` | Same namespace (so the realtime `invoices` key is untouched), returns `Result<DentalRecordBillingResult>`; `InvoicesController` unwraps `.Invoice`, so the route and body are unchanged |
| 3. The pre-commit refusal | `UpdateDentalRecordCommand` loads the fiche's note **before** `SaveChangesAsync` and returns `Result.Failure` + `Code`. `CreateDentalRecordCommand` takes the four fields and no guard |
| 4. The already-billed branch | `TopUpAsync`: higher → an additional `Payment` on the same note (`ToppedUp`), equal/absent → `AlreadyBilled`, lower / acts changed / spent note → refusals with codes |
| 5. `DentalRecordAutoBilling` | The `Contains("déjà facturée")` match **deleted**; switches on the typed outcome; carries the fiche's method + cheque instead of the hard-coded `Cash` |
| 6. Every outcome surfaced | `patient-record-modal` gained `ToppedUp` (success, naming the *increment*), `AlreadyBilled` (**`toast.info`**, no longer plain green) and `Refused` (warning, 10 s) |
| 7. Fiche payment fields, client | « Mode » select beside « Payé » in the footer + `ChequeFields` above it, reusing `cheque-fields.tsx` and `chequePaymentFields()` |
| 8. `CaissePeriod` | New; both caisse handlers delegate to it, `FromDay`/`ToDay` added; `BillingController` binds them on summary, ledger **and** export |
| 9. `web/app/caisse/page.tsx` | Sends day keys; `rangeBounds` deleted, replaced by a comment saying why it must not come back |
| 10. `ExpenseRepository` | `< to` → `<= to`, both sites |
| 11. Plan-workspace void | `void-installment-payment.tsx` (in-place panel, required motif) + the affordance in the cards' menu and in the table, and voided rows are now **rendered struck through with motif and actor** |
| 12. The bridge comment | Corrected in all three places, and `Invoice.Cancel`'s refusal now says the avoir is the only route and that a bridge's carried receipts do not travel back |

### Gate results

| Gate | Result |
|------|--------|
| `dotnet build --no-incremental` | **0 errors, 57 warnings** — the recorded baseline exactly, **0 new** |
| Unit suite | **2168 passed, 0 failed** (Part 1 left 2162; +6 from the repaired classes' new cases) |
| `verify-schema` before/after | **identical apart from the timestamp** — the four columns are diffed against the catalog for free, so no check changed |
| `reconcile-money` before/after | **identical apart from the timestamp** — no closed month moved, no duplicate document |
| `npx tsc --noEmit` | clean |
| `npm run check:responsive` | **15/15 passed** |
| `npm run build` | clean |
| Device eye pass | ⚠️ **OWED** — see below |

### Deviations

#### DEV-5: `GetExpensesQuery` gained the day keys too, which the plan's file table does not list
**Date:** 2026-08-08 · **Story:** 1, Part 2 · **Category:** Scope
**Original Plan:** step 8 names the summary, the ledger and the export.
**Actual Implementation:** `GetExpensesQuery` + `ExpensesController` take `FromDay`/`ToDay` as well.
**Justification:** la caisse renders the dépenses table **inside the same window** as the totals and the extrait.
Leaving that one call on client-composed instants would have meant either keeping `rangeBounds` (which step 9
deletes) or composing the period twice in two conventions — with the money-out list free to answer for a
different day from the money-out figure above it. ⚠️ It resolves a period **only when one was asked for**:
« no dates » on `/expenses` means « toutes les dépenses », not « aujourd'hui », so applying `CaissePeriod`'s
default unconditionally would silently turn the full list into one day's.
**Impact:** one more endpoint; no behaviour change for any caller that sends nothing.

#### DEV-6: `BillDentalRecordCommand.IsAutomatic` — A-1 needed a flag the plan did not name
**Date:** 2026-08-08 · **Story:** 1, Part 2 · **Category:** Technical
**Original Plan:** step 4 — « cancelled or fully credited → refuse and name the invoice, never a second document ».
**Actual Implementation:** *fully credited* refuses on both doors. *Cancelled* refuses on the **automatic** door
(saving the fiche) and raises a fresh note on the **manual** one (« Facturer cette intervention »).
**Justification:** taken literally the rule makes a séance whose note was annulée permanently unbillable — the
manual action is the only way back and it would refuse itself, with the refusal telling the user to press it.
AC-A-1's own wording is « never **silently** create a second document », and a re-save is precisely the silent
path while pressing the button is the deliberate one. The flag changes exactly that one decision and defaults to
`false`, so a caller that forgets it gets the door that asks nothing of it.
**Impact:** one boolean on the command, set in one place (`DentalRecordAutoBilling`).

#### DEV-7: three existing test classes were repaired (no new suites written)
**Date:** 2026-08-08 · **Story:** 1, Part 2 · **Category:** Scope
**Original Plan:** the owner excluded test authorship from this pass; the plan's own file table lists two new
test classes (`BillDentalRecordOutcomeTests`, `CaissePeriodTests`) as Part 2 deliverables.
**Actual Implementation:** the two new classes are **not written**. `InvoiceFromDentalRecordTests`,
`DentalRecordAutoBillingTests` and `DentalRecordActHandlerTests` were updated, because the rename, the typed
result and the two new constructor dependencies break their compilation — and because two of their cases
asserted the exact behaviour this part deliberately replaces (« an already-billed fiche is refused »).
**Justification:** a suite that does not compile is not a deferral, it is a red build; and a case still asserting
the replaced behaviour would be a false green rather than an absent test.
**Impact:** `BillDentalRecordOutcomeTests` and `CaissePeriodTests` are **owed**. The behaviour they were to cover
is partly covered by the repaired classes (the top-up, both refusal codes, the A-1 automatic/manual split), but
`CaissePeriod` itself has **no test at all** — which matters more than it looks, since it is now the single
authority on every caisse bound.

### Owed, and deliberately not claimed

- **The five-width device eye pass** (320 / 390 / 820 / 1180 / 1440 px + a landscape phone) on the fiche's
  payment fields and on `/caisse`. `check:responsive` is a mechanical gate and passed 15/15; it does not look at
  anything. Both surfaces this part touched **add controls**, so the pass is genuinely owed — the « Mode » select
  beside « Payé » and the cheque panel in the dialog footer are exactly the kind of addition that reads fine in
  source and crowds at 320 px.
- **The two test classes above** (DEV-7).
- **The manual passes the plan names**: la caisse with the workstation clock set to UTC−5 and to UTC+8 (AC-6),
  and the fiche re-save flow end to end watching « Encaissé », the patient's solde and the dashboard (AC-1).
  Nothing was exercised against a running server this session.

---

## Part 3 — Group B, cheque life-cycle (2026-08-10, session 4)

Ran **concurrently with the Part 4 session below**, on the same branch and the same working tree. That is the
context for two of this part's decisions and for the DEV-10 migration split; the two sessions did not conflict on
any source file, only on the model snapshot, which is shared by construction.

### What landed, step by step

| Plan step | Landed as |
|-----------|-----------|
| 1. Banked stamp | `ChequeBankedOn` / `ChequeBankedByUserId` / **`ChequeBankedByName`** (DEV-8) on `Payment` and `InstallmentPayment`, written only through `internal SetBanked`, which refuses a non-cheque method. Aggregate entry points `Invoice.SetPaymentBanked` and `TreatmentPlan.SetInstallmentPaymentBanked` on `VoidPayment`'s pattern — including its `Touch()`, which is load-bearing (see below). New VO `Domain/ValueObjects/ChequeBankedStamp.cs`, `ChequeDetails`' sibling and the single guard |
| 2. Two routes | `POST /api/invoices/{id}/payments/{paymentId}/banked` and `POST /api/treatment-plans/{id}/installments/{installmentId}/payments/{paymentId}/banked`, both `AdminOrDoctor`, body `{ banked: bool }`, mirroring the void routes one for one |
| 3. Ids the client needs | `ChequeDto` gains **`InstallmentId`** (+ `Banked`/`BankedOn`/`BankedByName`); `CaisseInstallmentPaymentRow` gains a **required** `InstallmentId`, updated at both projection sites. ⚠️ The plan also asked for « the owning aggregate id »; the DTO already carries it as **`TargetId`**, which the spec's own sentence names in the same breath — so no second field was added |
| 4. The read | `GetChequesDueQuery.Banked` (`null` ≡ `false` ≡ outstanding). Both repositories now return banked rows and the **handler** filters, because the buckets must be computed over the outstanding set whichever tab is open (AC-11) — a SQL-side exclusion could not tell the two apart |
| 5. B-1 | Held by the bridged-plan de-dup, which keys on the **plan**: once a devis is bridged only the invoice-side `Payment` is reachable. Pinned by test |
| 6. B-2 | **Confirmed, not assumed**: `A_Bridge_Invoice_Holding_A_Live_Payment_Cannot_Be_Cancelled` |
| 7. `verify-schema` | All four parts of **`cheque-banked-only-on-cheques`** — the `DataMigrationCounts` field, the `ScalarOrNullAsync` block guarded on **`ChequeBankedOn`** (not L8's `ChequeNumber` — see the test), the `Add(...)` line, and the clean **and** not-applicable cases |
| 8. `/cheques` | « À encaisser » / « Encaissés » segmented track (`period-selector`'s idiom), the mark/un-mark action as `primaryAction` on the card list and an action cell in the table, an `AlertDialog` confirmation (already a `dvh` bottom sheet below `md:` from the primitive), a « Porté en banque » column and CSV column, and the tab carried into the export |

### Two things worth knowing

⚠️ **`Touch()` on the aggregate root is not tidiness — it is AC-10.** `AuditSaveChangesInterceptor` records
**aggregate roots**, so marking a *child* `Payment` without touching the `Invoice` would leave **no audit row at
all**, and « qui a démarqué ce chèque ? » would have no answer anywhere in the product. Both entry points call it.

⚠️ **`Installment.SetPaymentBanked` deliberately does NOT call `RecomputeFromLedger()`**, and the invoice side
does not call `RecomputeCollected()`. That absence *is* AC-9: banking is a tracking state, la caisse counts a
cheque on the day it was received, and re-deriving totals here would move every historical figure a practice has
already read and reconciled. Two tests assert the figures are byte-identical across a mark.

### Gate results

| Gate | Result |
|------|--------|
| `dotnet build --no-incremental` | **0 errors, 55 warnings**, and **none in any file this part changed** (checked by listing all 25 unique warnings and grepping for the changed files — zero hits). The count moved 57 → 59 → 55 during the session as the concurrent Part 4 work added and then cleared its own `CS8601`s |
| Unit suite | **2190 passed, 0 failed** (Part 2 left 2168; +22 = 18 new `ChequeBankedStampTests` + 2 new schema cases + Part 4's own) |
| `verify-schema` | **schema matches the model**, exit 0; `cheque-banked-only-on-cheques` reports **clean** |
| `reconcile-money` | **no drift detected**, exit 0 — the monthly « encaissé » baseline is unchanged, which is the machine-checked form of AC-9 |
| `npx tsc --noEmit` | clean |
| `npm run check:responsive` | **15/15 passed** |
| `npm run build` | clean (this also clears Part 4's « unrunnable until Part 3 compiles » note below) |
| Device eye pass | ⚠️ **OWED** — see below |

### Deviations

#### DEV-8: a third column per ledger — `ChequeBankedByName`
**Date:** 2026-08-10 · **Story:** 1, Part 3 · **Category:** Scope (schema)
**Original Plan:** two columns, `ChequeBankedOn` + `ChequeBankedByUserId`.
**Actual Implementation:** a third, `ChequeBankedByName`, mirroring the void trail's `VoidedByName`.
**Justification:** AC-8 requires the banked row to show « when **and by whom** », and `ChequeBankedByUserId` is the
raw `User.Id` (`local|{guid}` or an Auth0 `sub`) — not a name. `IUserRepository` has no batched read, so resolving
at read time would have meant a new repository method **and** a per-page round trip, and the trail would still go
blank the day that account is deleted. The neighbouring void trail solved the identical problem with a snapshot;
copying it keeps one shape rather than inventing a second.
**Impact:** one nullable column per ledger. **Approved:** owner, before any schema was written.

#### DEV-9: the devis→facture bridge carries the banked stamp
**Date:** 2026-08-10 · **Story:** 1, Part 3 · **Category:** Technical
**Original Plan:** step 5 says only that a stamp « cannot travel back » across the one-way carry, i.e. it addresses
double-marking and not the forward direction.
**Actual Implementation:** `InstallmentPayment.ToBankedStamp()` beside the existing `ToChequeDetails()`, and
`IssueInvoiceCommand` passes both. `Payment`'s ctor and `Invoice.RecordPayment` take an optional
`ChequeBankedStamp`.
**Justification:** the plan side stops being counted the moment the bridge invoice is issued, so a stamp left
behind does not merely go missing — the cheque **reappears** under « À encaisser » although it is physically at
the bank, and re-marking it would record *today* rather than the day it was deposited. That is the same loss
`ToChequeDetails()` exists to prevent one field over (« a cheque left behind vanishes from the list »). Bank in
September, bill the devis in October is an ordinary sequence, not an edge case.
**Impact:** two optional parameters and one new method; pinned by two tests (carried when banked, **not** invented
when still held). **Approved:** owner.

#### DEV-10: the scaffold swept up Part 4's unmigrated model change, and was split by hand
**Date:** 2026-08-10 · **Story:** 1, Part 3 · **Category:** Technical (migration hygiene)
**Original Plan:** `AddChequeBankedStamp` adds six columns.
**Actual Implementation:** as planned — after removing an `AlterColumn Patients.DateOfBirth → nullable` that EF's
differ had scaffolded into it, and hand-reverting `DateOfBirth` to non-nullable in the paired `.Designer.cs` **and**
the model snapshot.
**Justification:** the working tree was clean at session start, but the **concurrent Part 4 session** landed its
nullable-DOB model change (12 files) with no migration while this part was being written, so the differ correctly
reported it as pending and attached it to the next migration generated — mine. Leaving it in would have put a
patient-schema change inside a migration named for cheques and skipped the drop-ordering review Part 4's plan
requires. **Leaving the snapshot nullable would have been worse than either**: the differ would then believe the
change had already shipped and emit **nothing** for it, leaving `DateOfBirth` `NOT NULL` in every database while
the model said otherwise — silent, and invisible to every test.
**Impact:** none on either part's intended schema, and the split is **verified rather than assumed**: Part 4's own
`20260810103310_NullablePatientDateOfBirth` was generated *after* this migration, correctly emitted its
`AlterColumn`, and the live database now reports `Patients.DateOfBirth is_nullable = YES` with all six
`ChequeBanked*` columns present. `AddChequeBankedStamp.Designer.cs` keeps `DateTime` (the model **as of** this
migration) while the current snapshot has `DateTime?` (Part 4's ran later) — which is exactly the correct chain.
**Approved:** owner, with the alternatives spelled out.

### Owed, and deliberately not claimed

- **The five-width device eye pass** (320 / 390 / 820 / 1180 / 1440 px + a landscape phone) on `/cheques`.
  **No browser was available this session** — `agent-browser` is not installed and both the API and the frontend
  were down — so the fallback was taken: `check:responsive` 15/15 plus a re-read of the diff against
  `DEVICE-CONTRACT.md` § 1. That read confirms the segmented track is `w-full` with `flex-1` halves below `sm:`
  and carries `coarse:min-h-11`; both mark controls carry `coarse:h-11`; the confirmation inherits the primitive's
  `dvh` bottom sheet with no `max-w` override; the table stays on the `_LG` hinge (now 10 columns); and the new
  fields are omitted rather than rendered « — ». **None of that is a substitute for looking at it**, and the two
  additions most likely to crowd at 320 px are exactly the ones a grep cannot see: the track sitting above the
  search input, and the card's full-width action row.
- **The « not applicable » half of the `verify-schema` proof on the live database.** The migration had already
  been applied by the time the session's first `verify-schema` ran, so the real database reported a genuine `0`
  rather than « not applicable ». The branch itself is covered by the new
  `The_Banked_Stamp_Invariant_Is_Not_Applicable_Before_Its_Own_Migration`, which pins the part that actually
  matters: the guard keys on **`ChequeBankedOn`**, so a database carrying L8 and not Group B reads « not
  applicable » instead of a reassuring `0`.
- **A manual pass on a running server**: marking and un-marking from `/cheques` and watching « Encaissé », the
  patient's solde and the dashboard **not** move, and the un-mark appearing in `GET /api/audit`. Nothing was
  exercised against a running server this session.

---

## Part 4 — Group D, remaining defects (2026-08-10, session 3) — **PARTIAL, UNCOMMITTED**

Owner's instruction: implement Part 4 out of order (Parts 2/3/4 are independent, per the story's own table).
Part 4 is the largest part — 10 steps, ~28 files — and **did not fit one session**. What landed is listed below;
what did not is listed under « Not started », not implied.

### ⚠️ Part 3 arrived in the working tree mid-session, from another author

`git status` was **clean** at session start. Roughly 18 files of Group B (cheque banked stamp) then appeared and
kept changing under me — `ChequeBankedStamp.cs`, `SetPaymentBankedCommand`, `SetInstallmentPaymentBankedCommand`,
banked members on `Payment`/`InstallmentPayment`/`Invoice`/`TreatmentPlan`/`Installment`, both controllers,
`GetChequesDueQuery`, `ChequesDueDto`, the two EF configurations, `cheques-table.tsx`, and their own migration
`20260810091618_AddChequeBankedStamp`.

Consequences, all real:

- **Their work broke the build twice and I unblocked it once.** `CaisseInstallmentPaymentRow` gained a required
  `InstallmentId` while `CaisseLedgerTests.cs:145` still passed the old 10 arguments — I added the missing
  argument and a fixed `InstallmentId` GUID **so my own gates could run at all**. That edit is theirs, not
  Part 4's. The second break (`SchemaVerificationServiceTests`' `CleanCounts`) they fixed themselves mid-session.
- **They correctly caught and hand-stripped my DOB change out of their migration.** Their file's own XML doc
  records it, and their `.Designer.cs` + the snapshot were reverted to `DateTime` — which is why my scaffold
  afterwards emitted a clean single `AlterColumn` instead of nothing.
- **`npx tsc --noEmit` is currently RED, entirely in `components/caisse/cheques-table.tsx`** (six errors:
  `BANKED_TABS`, `Landmark`, `bankedSummary` undefined) — their file, mid-edit. **No error is in a file this part
  touched.** `npm run build` therefore could not be run green, and was not.

### 🔴 The blocker: this part cannot be committed independently of Part 3

`ApplicationDbContextModelSnapshot.cs` is **one shared file** now holding *both* their banked columns and my DOB
nullability, and their migration sits **earlier in the chain** than mine. So:

- committing my migration + the snapshot **without** their migration ships a snapshot claiming columns no
  committed migration creates — the next `migrations add` would re-emit them (R-3 exactly);
- committing my source **without** the migration ships a model whose `Patient.DateOfBirth` is nullable against a
  database where the column is still `NOT NULL`;
- committing both together violates « each part commits on its own » and sweeps another author's in-flight,
  type-error-carrying work into this feature's history (R-13).

**Nothing was committed.** Recommended order: land Part 3 (with its migration and a green `tsc`), then commit
Part 4 on top. Both migrations are already **applied to the dev database**, so no re-run is needed.

### What landed

| Plan step | AC | Landed as |
|-----------|----|-----------|
| 1. Nullable DOB | AC-18, D-1, D-2 | `Patient.DateOfBirth` → `DateTime?` end to end: entity + ctor + `UpdatePersonalInfo`, `PatientConfiguration`, `PatientDto`, `CreatePatientCommand`, `UpdatePatientCommand` (kind-normalisation moved inside an `is { } born` guard), `PatientIdentity`, `PatientRepository`, `PatientDuplicateIndex`, the import reader, `CreateMedicalDocumentCommand` (`!= default` → `.HasValue`), `AIActionService` (new `BirthDateOrDash`). **`ExportTables:42` needed no change** — `CsvCell.CalendarDay(DateTime?)` already existed and yields a blank cell. Client: `types.ts`, `patients.ts`, three `calculateAge` signatures, « âge inconnu » in the list, the fiche and the summary, and `edit-patient-dialog`'s `birthdate || new Date().toISOString()` — **the client half of the same fabrication** — replaced with `null` |
| 3. Stock lead days | AC-20 | **backend only.** `Clinic.SetStockExpiryLeadDays` guard `1–365` → **`0–365`** with a French message naming « 0 = alerte désactivée »; its first caller ever is the new `SetStockExpirySettingsCommand` + `GetStockExpirySettingsQuery` + `StockExpirySettingsDto`, on `SetRecallSettingsCommand`'s shape, behind `GET`/`PUT /api/stock/expiry-settings` (read `AnyClinicRole`, write `AdminOnly`) |
| 4. One-sided insurance | AC-21 | All **three** guards, in the plan's stated order: `InsuranceInfo` accepts either side (both blank still refused, French message); `PatientFromRequest:73-75`'s create-path **silent drop** → build if *either* side is present; `UpdatePatientCommand` turns a both-blank block into a *clear* rather than a 500; `PatientImportRowReader:193` carries a one-sided row through instead of warning and dropping it. Client: both `"Unknown"` padding blocks deleted; `InsuranceInfoDto` and both client types widened to nullable |
| 5. Pager reset | AC-22 | `usePagedList` takes `filters?: readonly unknown[]`, keyed on **`JSON.stringify`** with a first-run skip; wired to `patients-table` (flag + created-date bounds), `invoices-table` (patient/from/to/status/doctor) and `procedure-types-table` (category) |
| 8. AC-25, part | AC-25 | `Appointment.BookedOutsideWorkingHours` + `MarkBookedOutsideWorkingHours()` **deleted** with all four write sites (`CreateAppointmentCommand`, `CreateRecurringSeriesCommand`, `UpdateAppointmentCommand`, `GoogleCalendarSyncService`) — the `AllowOutsideWorkingHours` **permission** is untouched. New `CancelWaitingListEntryCommand` + `POST /api/waiting-list/{id}/cancel` gives `WaitingListEntry.Cancel()` its first caller |
| 9. AC-26 | AC-26 | `ClinicContext.EnsureClinicAccess` + `BelongsToClinic` and their interface declarations deleted (zero callers, confirmed by grep); `ForbiddenAccessException` kept — `ExceptionMiddleware`'s 403 mapping still uses it — and the now-unused `using` removed |
| 10. Migration | — | **`20260810103310_NullablePatientDateOfBirth`** — scaffolded, not hand-written; one `AlterColumn` widening, **no drops**, so R-12's reorder-below-the-backfill does not arise. Applied. Renamed from the plan's `NullableDobLabOrderAppointment` — see DEV-9 |

### Not started

**AC-19** (`/journal` page + `web/lib/api/audit.ts` + nav entry) · **AC-23 / D-3** (nullable
`LabWorkOrder.AppointmentId` + FK + validation + both-way links) · **AC-24** (agenda inline quick status) ·
**AC-20's client control** · **AC-25's two remaining surfaces** (`MedicalDocument.AppointmentId` in the documents
tab, `WaitingListEntry.ResultingAppointmentId` as a link, the « Retirer » affordance) · the plan's
`NullableDateOfBirthTests` class.

### Gate results

| Gate | Result |
|------|--------|
| `dotnet build --no-incremental` | **0 errors, 55 warnings** vs. the recorded **57** baseline — **0 new**, and **2 fewer**: making `InsuranceInfo`'s two halves nullable cleared two `CS8618`s on its own EF private ctor. Every warning in a touched file is pre-existing (`Patient.cs`/`Clinic.cs` `CS8618`, the controller `CS8602`s); two shifted line number only because a doc comment was added above them |
| Unit suite | **2190 passed, 0 failed** (baseline 2168) |
| `verify-schema` before/after | **both « schema matches the model »**; saved to the scratchpad as `verify-schema-P4-BEFORE.txt` / `-AFTER.txt` |
| `npx tsc --noEmit` | 🔴 **6 errors, all in `components/caisse/cheques-table.tsx`** — Part 3's file, mid-edit. **None in a Part 4 file** |
| `npm run check:responsive` | **15/15 passed** |
| `npm run build` | **not run** — blocked by the `tsc` failure above, which is not this part's |
| Device eye pass | ⚠️ **OWED** — see below |
| `reconcile-money` | not applicable — Part 4 touches no money path |

### Deviations

#### DEV-8: implemented out of order, and Part 3 landed underneath it
**Date:** 2026-08-10 · **Story:** 1, Part 4 · **Category:** Scope
**Original Plan:** parts land 1 → 2 → 3 → 4, one per session.
**Actual Implementation:** Part 4 implemented third, while Part 3 was written in parallel in the same tree.
**Justification:** the owner asked for Part 4, and the story states « Parts 2, 3 and 4 are independent of each
other » — only Part 1 → Part 2 is a hard dependency. The parallelism was not foreseen by the plan.
**Impact:** the commit blocker above. No behavioural impact; the two parts touch disjoint code apart from the
shared model snapshot.

#### DEV-9: the migration carries one of its three planned schema changes
**Date:** 2026-08-10 · **Story:** 1, Part 4 · **Category:** Scope
**Original Plan:** one migration `NullableDobLabOrderAppointment` covering the nullable DOB,
`LabWorkOrders.AppointmentId` + FK, and the `Appointments.BookedOutsideWorkingHours` drop.
**Actual Implementation:** `NullablePatientDateOfBirth`, carrying the DOB widening alone.
**Justification:** step 6 (lab orders) was not reached, and although the `BookedOutsideWorkingHours` **property**
is deleted, dropping its **column** in the same migration as an unrelated widening — while a second author's
migration is uncommitted immediately behind it — is how a migration chain gets tangled.
⚠️ **Consequence to carry forward:** the next part to touch this must drop
`Appointments.BookedOutsideWorkingHours`, and `verify-schema` will **not** complain in the meantime — it diffs the
model against the catalog, and an *extra* column in the database is not model drift.
**Impact:** one more migration owed.

#### DEV-10: AC-18's « the odontogram asks » solved without a nullable `Dentition` column
**Date:** 2026-08-10 · **Story:** 1, Part 4 · **Category:** Technical
**Original Plan:** « `DentitionRules.FromDateOfBirth` — return "ask which dentition" for null ».
**Actual Implementation:** `FromDateOfBirth` returns `DentitionType?`; `PatientFromRequest` **skips**
`SetDentition` when it has no answer; and `Odontogram` gained a `dateOfBirth` prop and renders a « Quelle
dentition charter ? » prompt when there is no date of birth, nothing charted, and no view chosen this session.
**Justification:** `Patient.Dentition` is a `NOT NULL` enum whose entity default is `Adult`, so the server-side
half alone would have left « ask » indistinguishable from « adulte » — the AC would read as satisfied while the
chart still opened on permanent teeth. Making the column nullable is a **fourth** schema change the plan's
migration does not list. The client already had the right shape (`dentitionFromBirthdate` returns null, its own
comment saying « the form must not guess, it must keep asking »), so the honest signal was already on the wire:
**no date of birth ⇒ nothing to infer from**.
**Impact:** one new optional prop; no schema change. A stored « je ne sais pas » would need the nullable column.

#### DEV-11: a Part 3 test file was edited to unblock this part's gates
**Date:** 2026-08-10 · **Story:** 1, Part 4 · **Category:** Scope
**Original Plan:** stage by explicit path; never sweep unrelated work into this feature's commits (R-13).
**Actual Implementation:** `CaisseLedgerTests.cs` gained an `InstallmentId` constant and one ctor argument.
**Justification:** their change made the **test project fail to compile**, so the unit suite — the backend's only
automated check — could not run at all, and neither could `verify-schema`. Two lines, mechanical.
**Impact:** that file belongs to Part 3's commit, not this one.

#### DEV-12: `PatientDuplicateGuardTests` updated, and one new case reversed after it failed
**Date:** 2026-08-10 · **Story:** 1, Part 4 · **Category:** Technical
**Original Plan:** D-2 — a null DOB neither widens nor narrows duplicate matching.
**Actual Implementation:** `Same_Name_With_No_Birth_Date_Supplied_Is_Refused` now expresses « not supplied » as
**`null`** rather than `default(DateTime)` (the sentinel AC-18 retires), and a new
`An_Undated_Patient_On_File_Does_Not_Match_A_Dated_Candidate` pins the other direction.
**Justification worth reading:** that new test was first written asserting the pair **is** flagged, and it failed.
The failure was correct and the test was wrong — before AC-18 an undated record carried the fabricated « thirty
years ago », so the date comparison ran and did not match; now the stored value is null and the comparison cannot
match. Same outcome, more honest route. Asserting a refusal would have been a **widening**, which D-2 forbids.
The test was reversed to pin the preserved behaviour, with a note that whether this *should* warn is a real
product question deliberately left unanswered here.
**Impact:** none. It is the D-2 regression test the plan asks for.

### Owed, and deliberately not claimed

- **The five-width device eye pass** (320 / 390 / 820 / 1180 / 1440 px + a landscape phone). `check:responsive`
  passed 15/15 but it is a grep, not an eye. Two surfaces genuinely need it: the odontogram's new « Quelle
  dentition charter ? » prompt (three buttons that must stay reachable at 320 px; `coarse:h-11` applied but
  unverified) and the « âge inconnu » lines in the patients table's card list.
- **`NullableDateOfBirthTests`**, the class the plan names — DOB behaviour is currently covered only by the two
  `PatientDuplicateGuardTests` cases above.
- **`npm run build`**, unrunnable until Part 3's `cheques-table.tsx` compiles.
- **The manual passes**: a walk-in created from the appointment dialog's inline form storing no DOB, and `0`
  disabling the expiry alert in the job, the dashboard and the list. Nothing was exercised against a running
  server this session.
- **`api/.testrun/`** is untracked build output that should be git-ignored rather than committed.

### Environment note

The API (`:5000`) and frontend (`:3000`) were running at session start and were stopped — they lock
`api/**/bin` and `web/.next`. **Restart with `/start-clinic`.** Smart App Control did not interfere this session;
`dotnet test -p:BaseOutputPath=$TEMP/…` ran clean throughout.
