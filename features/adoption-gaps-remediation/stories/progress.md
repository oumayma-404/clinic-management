# Implementation Progress — Adoption Gaps Remediation

**Feature:** `features/adoption-gaps-remediation/`
**Story:** Story 1 — Close the adoption gaps (`Layer: Full`, four ordered parts)
**Branch:** `feature/windows-desktop-app` (owner's decision, per the story's entry criteria)

## Part tracker

| Part | Group | Name | Migration | Status |
|------|-------|------|-----------|--------|
| 1 | C | Remove El Fatoora / TTN | `RemoveEInvoicing` | **implemented** |
| 2 | A | Money integrity | `AddDentalRecordPaymentMethod` | **implemented** |
| 3 | B | Cheque life-cycle | `AddChequeBankedStamp` | not-started |
| 4 | D | Remaining defects | `NullableDobLabOrderAppointment` | not-started |

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
