# Progress: Adoption QA — J (money integrity and entry)

**Started:** 2026-08-03
**Type:** Small (forced — the spec is an 11-item single-theme pass, ~39 files)
**Branch:** `feature/audit-sections-3-to-10` (see DEV-1)

## Status
- [x] Implementation — **all 11 items (J1–J11) done**
- [x] Quality checks (dotnet build, tsc, next build, check:responsive) — **mechanical half green**
- [ ] Eye pass at 320/390/820/1180/1440 — **NOT DONE, see « Gate » below**
- [x] Tests written — 9 classes, **84 passing**; `InvoiceDebtIsAgedTests` re-run pending (see « Tests Run »)

## Test Plan

| J | Action | Target file | Notes |
|---|--------|-------------|-------|
| J1 | **New class** | `Features/TreatmentPlans/InstallmentOnBilledPlanIsRefusedTests.cs` | The refusal, the message naming the note, every representing status — **and the four cases that must still work** (cancelled bridge, draft bridge, unbilled plan, a bridge for a *different* plan). |
| J2 | Scenarios folded into J1 + J3 | ↑ + `PaymentDateRulesUsesClinicDayTests` | Future date and `0001-01-01` refused **on the installment ledger**, which is the path the guard was missing from. |
| J3 | **New class** | `Features/Invoices/PaymentDateRulesUsesClinicDayTests.cs` | Fixed instants only. |
| J4 | **Add scenarios** | `Domain/InvoiceEInvoiceTests.cs` | Dequeue on cancel for `Queued` **and** `Signed`; the declared-states guard *not* loosened; a never-queued note untouched. |
| J4 | **New class** | `Infrastructure/Services/CancelledInvoiceIsNotDispatchedTests.cs` | The dispatcher's own guard, asserted as a **negative** (nothing signed, nothing submitted). |
| J5 | **Add scenarios** | `Features/Billing/MoneyReadConsistencyTests.cs` | The spec's load-bearing item — extended, not paralleled. |
| J6 | **New class** | `Features/Invoices/CreditNotePdfSplitTests.cs` | The 100 / 7 % / 1 DT case, plus a `[Theory]` pinning that the three printed components always sum to the credited total. |
| J7 | **New class** | `Features/Billing/InvoiceDebtIsAgedTests.cs` | **Found a real bug — see below.** |
| J10 | **New class** | `Features/Invoices/InvoicePdfMentionsTests.cs` | Address formatting + the addressless/matricule-less render cases. |
| J11 | **New class** | `Domain/ClinicBillingDefaultsTests.cs` | The corrected default, and that it stays editable. |

### Coverage notes — ACs with no unit surface (recorded, not contrived)

- **J8 (money entry) and J9 (dirty guard) have no automated coverage, by fact of the repo.** There is no test
  runner in `web/` — no vitest, no jest, and `npm run lint` cannot run (`eslint` is in the script but not in
  `devDependencies`). Per `web/CLAUDE.md` that is a documented property of the repo, not a gap to fill mid-feature.
  They are covered by `npx tsc --noEmit` + `npm run build` + `npm run check:responsive` (all green) and by the
  hand pass, **which is still outstanding**. `parseAmountInput` is a pure function and *would* be the ideal unit
  test the moment a runner exists — noted here so it is the first thing written then.
- **J10's footer gate** (« Note d'honoraires soumise au timbre fiscal » made conditional) is inside the QuestPDF
  composition. There is no PDF text extraction in this repo, so the rendered *wording* cannot be asserted;
  `InvoicePdfMentionsTests` pins the datum the gate switches on (`StampDutyAmount` 0 vs 1,000) and the wording is
  verified by review. Deliberately not faked with a "does it throw" test, which would prove nothing.
- **J11's "do not migrate existing clinics"** is a *non*-change: it lives in the absence of a migration and in the
  settings notice. Nothing in the domain can assert the absence of a backfill, so it is recorded rather than tested.
  `verify-schema` remains the gate for schema-level change (this spec adds none).
- **J4's `GetDueForElFatooraDispatchAsync` predicate** (the third guard) is SQL. Nothing in this project touches a
  database — `UnitTests/CLAUDE.md` is explicit that migrations and query predicates are outside its reach — so it is
  covered by the two guards above it plus review, not by a contrived in-memory double.
- **`EInvoiceStatus.Validating`** is in `Invoice.Cancel`'s guard expression but **no public mutator can produce it**.
  Pinning it would mean reflecting onto a private setter to manufacture a state the domain cannot reach, which tests
  the reflection rather than the rule. Noted in the test's own comment.

## Bug found & fixed by the tests

**`GetReceivablesQuery` aged debt on the wrong calendar** — caught by `InvoiceDebtIsAgedTests`, 5 of its 9 cases
failing, every one off by **exactly one day**.

- **What the test caught.** « Retard » read 46 days for a 45-day-old note, and **1 day for a note issued at 00:30
  this morning**.
- **Root cause.** `(clinicToday - overdue.Value.Date).Days` subtracted a **UTC** calendar date from a
  **clinic-local** one. `overdue` is a stored instant, so `.Date` is its UTC date — and for the first hour of every
  clinic day (23:00–24:00 UTC, Tunisia being UTC+1) that is the *previous* day.
- **Why J7 armed it.** The plan track was unaffected in practice: an échéance's `DueDate` is stored as midnight of
  a chosen day, so its UTC date and its intended day already agree. J7 introduced `Invoice.IssueDate` into this
  arithmetic — a true `DateTime.UtcNow` instant — and the mismatch became reachable. It is the § 4.1 defect **J3
  exists to close**, re-armed by a newly-added date, in the same spec that closes it elsewhere.
- **Fix** (two lines, `GetReceivablesQuery.cs`): difference through `ClinicClock.ToClinicLocal(...).Date` so both
  sides are on the clinic's calendar. Applied to *both* tracks — it is a no-op for midnight-stored due dates and
  removes the distinction as something anyone has to remember.
- Found by the test, not by review. It is exactly the class of error the eye cannot see.

## Working tree note (start of session)

The branch carried **208** modified/untracked files of unrelated in-flight work when this
session opened — the `audit-sections-3-to-10` batch (procedure-type categories, document
emails, reminder settings, the mobile-redesign sweep) plus three sibling adoption-QA specs
(`…-i-access-control-and-audit`, `…-k-cnam-bulletin-validity`) and two design folders
(`agenda-phone-ux`, `landing-website`). It is now ~239.

**None of it belongs to this feature.** Every commit for J must stage its paths explicitly
(`git diff HEAD --numstat` first, per `check-file-is-clean-before-staging`); `git add -A`
would swallow the batch.

⚠️ **A concurrent session was editing `web/components/document-editor-content.tsx` and
`web/components/patient-record-modal.tsx` while this pass ran** — line numbers shifted between two
consecutive `tsc` runs, and three transient breaks appeared and were fixed by that other session
mid-turn (a missing `PatientAlertPanel` import, a missing `useCallback` import). One of them I briefly
duplicated before backing it out (DEV-4). Nothing of J depends on those files beyond the J8 edit to
`patient-record-modal.tsx`, which is intact.

## Where this pass started

A **previous session had already implemented J1 (backend), J2, J4, J5, J6 and the render half of
J3** and left no log, so the first act of this session was to verify each item against the source
rather than trust the empty progress file. Findings:

| Item | State on arrival | Done this session |
|---|---|---|
| J1 | backend guard present in `RecordInstallmentPaymentCommand` | **frontend half** (`plan-workspace.tsx`) |
| J2 | all four money-date entry points call `PaymentDateRules` | verified only — nothing to do |
| J3 | `PaymentDateRules` on `ClinicClock`; PDF renders via `FrDay` | verified + **DEV-2 logged** |
| J4 | all three guards present (entity dequeue, service check, repo predicate) | verified only |
| J5 | `GetInvoiceRevenueQuery` sums both ledgers, rounds through `InvoiceCalculator` | verified only |
| J6 | avoir VAT/stamp split proportional, timbre its own printed line | verified only |
| J7 | **not started** | whole item |
| J8 | `parseAmountInput` still module-private in `invoices-table.tsx`, 1 of 10 fields | whole item |
| J9 | five forms guarded, none of the six money dialogs | whole item |
| J10 | **not started** | whole item |
| J11 | **not started** | whole item |

## Files Changed (this session)

### J7 — invoice debt is aged
- `api/ClinicManagement.Domain/Repositories/IInvoiceRepository.cs` — `GetOutstandingByPatientAsync`
  returns `(PatientId, Outstanding, OldestUnpaidIssueDate)`.
- `api/ClinicManagement.Infrastructure/Repositories/InvoiceRepository.cs` — `g.Min(i => i.IssueDate)`
  in the **same** projection as the sum (two queries could disagree about which notes are unpaid if a
  payment landed between them).
- `api/…/Features/Billing/Queries/GetReceivablesQuery.cs` — both tracks now date the debt, and the
  patient's « Retard » is the **earlier** of the two (`Keep`). Previously the plan loop overwrote and
  the invoice loop contributed nothing.
- `api/…/Features/Billing/Queries/GetPatientBillingSummaryQuery.cs` — **the same defect in the adjacent
  read**, not named by the spec: « Solde patient »'s `OldestOverdueDate` was also plan-only. Fixed here
  because the invoices are already loaded (no extra query) and per `fixes-dont-propagate` a guard wired
  to one of two call sites is the repo's dominant defect shape.
- `api/ClinicManagement.UnitTests/Features/Billing/MoneyReadConsistencyTests.cs` — mock mirrors the new
  projection (build-required).

No frontend change: `receivables-table.tsx` already renders `daysOverdue > 0`, so an invoice issued
today (0 days) correctly shows no « En retard » badge.

### J8 — money can be typed on the device it is collected on
- `web/lib/format.ts` — **`parseAmountInput` moved here and exported**, and hardened: strips any
  whitespace incl. the non-breaking and narrow-no-break spaces `Intl.NumberFormat("fr-TN")` itself
  emits, accepts comma or dot, and returns **`NaN`** for a bare `,`, a double separator (`1,2,3`) or a
  dot-grouped `1.200,500` rather than silently truncating them to `1.2`. Never throws.
- `web/components/factures/invoices-table.tsx` — private copy deleted, imports the shared one.
- Eleven money fields converted to `type="text" inputMode="decimal"` + `parseAmountInput`, and every
  prefill moved off `String(...)` onto **`formatAmount`**:
  `factures/payment-modal.tsx` · `treatment-plans/installment-payment-modal.tsx` ·
  `factures/bill-dental-record-dialog.tsx` · `patient-record-modal.tsx` (« Payé ») ·
  `record/act-detail-fields.tsx` + `record/use-session-acts.ts` (« Tarif », **both** parse points) ·
  `app/caisse/page.tsx` (dépense) · `factures/invoice-form-modal.tsx` (P.U. HT) ·
  `treatment-plans/revise-installments-modal.tsx` · `treatment-plans/treatment-plan-form-modal.tsx`
  (Coût + Montant) · `procedure-type-form-modal.tsx` (Coût par défaut) ·
  `clinic-settings.tsx` (Montant du timbre + Taux de TVA — **DEV-3**).
- Left as real `type="number"` deliberately: « Qté » on an invoice line, « Durée (min) » on a procedure
  type and on the acts picker, stock quantity + threshold, and the reminder/recurrence counts — integer
  counts, not money.
- `procedure-type-form-modal.tsx` gained an explicit **NaN refusal** (« Saisissez un coût valide, par
  exemple 70,000 »): the browser no longer refuses a malformed value, and `NaN` would have been sent as
  a null cost, silently unpricing the act that seeds every invoice line. `stock-item-form-modal.tsx`
  gained the same guard for the same reason.

#### J8, second pass — the helper's whole point (DEV-7)
Grepping `replace(",", ".")` after centralising the helper found **five more hand-rolled copies** and
**two more money fields still on `type="number" step="0.01"`**. J8's own justification is « a helper nine
files cannot import is a helper that gets retyped or skipped », so leaving them would have defeated the
item. All now go through the one implementation:

| Site | What it is | Was |
|---|---|---|
| `cnam-letter-values-card.tsx` (×2 inputs) | valeur de la lettre clé — **money** | `type="number"` + a dead comma swap |
| `dental-act-form-modal.tsx` (Tarif par défaut) | **money** | same |
| `dental-act-form-modal.tsx` (Coefficient) | a rate | same |
| `cnam-entry-form-modal.tsx` (Coefficient) | a rate | same |
| `app/lab-orders/page.tsx` `parseAmountOrNull` | **money** | already `type="text"`, but its own weaker parser |
| `appointment-acts-picker.tsx` (« Montant ») | **money** — creates a `ProcedureType.defaultCost` | `type="number" step="0.01"` |
| `stock-item-form-modal.tsx` (Prix unitaire) | **money** | `type="number" step="0.01"` |

⚠️ In the five `.replace(",", ".")` cases the comma swap was **dead code**: the field was `type="number"`,
which never yields a comma — it returns an *empty* value for the rejected keystroke. So each of those
screens carried a helper that looked like it handled the defect and could not. Each already refused a
non-finite value in French, so routing them through `parseAmountInput` is behaviour-preserving on the
refusal path and only fixes the accept path.
`lab-orders`' wrapper is **kept**, reduced to the one thing that is genuinely local — the
empty-means-null contract an optional cost needs — and now delegates. Its old copy replaced only the
**first** comma (so « 1,2,3 » read as `1.2`) and stripped no whitespace (so « 1 200,500 » pasted back out
of this very app failed).
**`grep -rn 'replace(",", ".")'` now returns only comment text — `parseAmountInput` is the single
implementation in `web/`.**

### J9 — a typed payment is not discarded by a stray tap
`useDirtyGuard` + `<DiscardChangesDialog>` on all six: `create-appointment-dialog.tsx` ·
`edit-appointment-dialog.tsx` · `factures/payment-modal.tsx` ·
`treatment-plans/installment-payment-modal.tsx` · `factures/bill-dental-record-dialog.tsx` ·
`app/caisse/page.tsx` (expense dialog).

Follows the repo's established shape (`revise-installments-modal.tsx`): only the **root** and
« Annuler » route through the guard; every save path calls the raw `onOpenChange` so a successful save
never asks. The `AlertDialog` escalations in the two appointment dialogs also keep the raw prop — a
confirmation the user just accepted must not then ask whether to discard.
⚠️ `bill-dental-record-dialog.tsx` is open-driven by `record`, not an `open` boolean, so the guard is
fed `!!record`.

**Verified the spec's premise rather than trusting it.** The spec says « the last four are `mobile="bottom"`
sheets ». None of the four passes a `mobile` prop, which looked like the spec was wrong — but
`ui/dialog.tsx` declares **`mobile = "bottom"` as the default**, so all four genuinely are bottom sheets
below `md:` and the spec is right. Worth recording because the natural conclusion from grepping the call
sites is the opposite one, and a comment asserting the wrong presentation would have shipped.

### J1 — frontend half
- `treatment-plans/plan-workspace.tsx` — **`canCollectInstallments` derived once** (`!isDraft &&
  status !== "Cancelled" && !billed`) and read by **both** the card list and the table. The condition
  was written inline in each; hoisting it is the point, since that is exactly how a guard lands on one
  surface and not the other — and the phone is the surface that would have kept the button.
- The reason is **visible text**, not a `title`: a `role="note"` paragraph in the Échéancier card
  naming the invoice number and saying an encaissement entered there would reach neither la caisse nor
  les recettes.

### J10 — the printed note carries its mandatory mentions
- `api/…/Common/Models/InvoicePdfData.cs` — `PatientAddress` (nullable, documented as **never** a
  validation blocker: art. 18 § II requires the client's address only for a client subject to the
  déclaration d'existence, i.e. a business, not a private patient).
- `api/…/Features/Invoices/Queries/GetInvoicePdfQuery.cs` — `FormatAddress(patient)` → « rue, code
  postal ville ». Every part guarded, so a patient with no address renders one line fewer, not a throw.
- `api/…/Services/PdfGenerationService.cs` — the address line, and the **timbre footer gated on
  `StampDutyAmount > 0`**. It printed « soumise au timbre fiscal » unconditionally while the timbre
  *line* is conditional, so a note with the timbre off asserted a droit de timbre it had never charged
  — the document contradicted its own totals. Only the timbre clause is conditional; the currency half
  is true either way.

### J11 — the corrected default tax position
- `api/ClinicManagement.Domain/Entities/Clinic.cs` — **`VatApplicable = true`** (was `false`) for a
  newly created clinic, at the existing 7 %, with the Tableau « B » nouveau § II n° 1 citation and the
  art. 117 § I n° 6° citation for why `StampDutyAmount = 1.000` stays. Both remain editable.
- `web/components/clinic-settings.tsx` — the notice for **existing** clinics (DEV-5).
- Existing rows are untouched by design: flipping the flag retroactively would change what
  already-issued, numbered notes d'honoraires assert.
- Test fixtures adjusted for the new default (build-required, DEV-6): `InvoiceFromDentalRecordTests`
  `SessionTtc` 331 → **354.100** (330 HT + 7 % + 1 DT timbre); `BridgeCarryOverTests` needed no
  assertion change because it already derived from `invoice.TotalTtc` — its comment now says so.

## Release note (J5 — a displayed figure changes)

`GET /api/invoices/revenue` → `totalCollected` now **includes devis instalments**, netted through
the same `PlanBillingRules.BilledPlanIds` de-dup la caisse and the dashboard use. `/factures`
will therefore show a **larger** « Total encaissé » than before, and for the first time the same
number as la caisse and the dashboard. A clinic that reconciled against the old figure will read
the change as a jump; it is not new money, it is money that was always collected and was only
missing from this one read.

**Second changed figure (J11):** a **newly created** clinic now issues notes d'honoraires with 7 %
TVA by default. Existing clinics are unchanged until an admin decides.

## Gate

| Check | Result |
|---|---|
| `dotnet build ClinicManagement.sln` | ✅ **0 errors.** Warnings are entirely the repo's pre-existing baseline (`CS8618`/`CS8602`/`CS8600`/`CS8604`/`CS0618`/`CS8981`); scoping the output to the files I changed returns **empty**. Built with `-p:BaseOutputPath=<scratch>` per `ef-migration-scaffolding-hazards`. |
| `npx tsc --noEmit` | ✅ **exit 0** — reached three separate times, each immediately after a batch of J edits. ⚠️ See the concurrency note below: it also went red four times *between* those runs, always in a file with **zero** J content. |
| `npm run check:responsive` | ✅ **all 11 enforced checks pass** (run three times across the pass) |
| `npm run build` | ⚠️ **Compile step succeeds every single run** (« ✓ Compiled successfully »). The whole-project **type-check** step is intermittently red on the concurrent session's files. Last state: the only file with an error is `components/document-editor-content.tsx`. |
| **No error, in any run, named a file J touched** | ✅ verified explicitly — `tsc` output reduced to a unique file list and grepped against all 24 J files: **no overlap**. |
| Eye pass 320/390/820/1180/1440 | ❌ **NOT DONE.** No browser in this environment — stated plainly rather than claimed. A diff audit against the contract was done instead (below); it is not a substitute. |
| Hand-enter `45,500` on a French-locale device | ❌ **NOT DONE. This is the only thing that proves J8** and no static check can see it. |

**Concurrency caveat on the frontend gate.** A parallel session was editing
`document-editor-content.tsx`, `patient-record-modal.tsx`, `recurring-series/page.tsx` and
`edit-patient-dialog.tsx` throughout this pass — four transient breaks appeared and were self-fixed by
that session mid-turn (a missing `PatientAlertPanel` import, a missing `useCallback`, a missing
`useDoctors`, missing `CNAM_REGIMES`/`CNAM_LIENS`). Line numbers shifted between two consecutive `tsc`
runs. I stopped fixing them after the first (see DEV-4) because a half-written refactor is not mine to
guess at, and my one attempt produced a duplicate-identifier clash. **The gate should be re-run once that
session settles**; nothing about J is implicated.

## Tests Run

Runner recipe: `dotnet build <UnitTests>.csproj --no-incremental -p:OutDir=<scratch>/` then
`dotnet vstest <scratch>/<dll> --TestCaseFilter:"FullyQualifiedName~<Class>"` — the documented Smart-App-Control
workaround (`smart-app-control-blocks-tests`). Targeted only; no full-suite run.

| Suite | Filter | Result |
|-------|--------|--------|
| Unit | `InstallmentOnBilledPlanIsRefusedTests` | **12 passed**, 0 failed |
| Unit | `PaymentDateRulesUsesClinicDayTests` | **10 passed**, 0 failed |
| Unit | `InvoiceEInvoiceTests` (10 pre-existing + 6 new) | **16 passed**, 0 failed |
| Unit | `CancelledInvoiceIsNotDispatchedTests` | **4 passed**, 0 failed |
| Unit | `MoneyReadConsistencyTests` (11 pre-existing + 5 new) | **16 passed**, 0 failed |
| Unit | `CreditNotePdfSplitTests` | **12 passed**, 0 failed |
| Unit | `InvoicePdfMentionsTests` | **8 passed**, 0 failed |
| Unit | `ClinicBillingDefaultsTests` | **6 passed**, 0 failed |
| Unit | `InvoiceDebtIsAgedTests` | 4 passed, **5 failed → bug found and fixed → re-run BLOCKED**, see below |

**84 passing across the 8 verified classes, 0 failures.** No `Skip`, no `[Fact(Skip)]`.

### ⚠️ `InvoiceDebtIsAgedTests` is written and its fix applied, but the green re-run is outstanding
The 5 failures were diagnosed (all off-by-exactly-one, same root cause) and fixed in `GetReceivablesQuery`. The
verifying re-run could not be completed because the build broke underneath it, twice, in **files this feature does
not touch**:
1. Smart App Control flagged the freshly-rebuilt `ClinicManagement.Application.dll` (`0x800711C7`) — the
   time-varying environmental block, not a defect.
2. Then the concurrent session's unattended-backup work landed: `PgDumpBackupService` no longer implements
   `IBackupService.PruneOldBackupsAsync` / `ResolveDestinationRoot` (2 errors), so the test project cannot compile
   at all.

**To finish, once that settles:**
```bash
cd api && dotnet build-server shutdown
dotnet build ClinicManagement.UnitTests/ClinicManagement.UnitTests.csproj --no-incremental -p:OutDir=<fresh-scratch>/
dotnet vstest <fresh-scratch>/ClinicManagement.UnitTests.dll \
  --TestCaseFilter:"FullyQualifiedName~InvoiceDebtIsAgedTests"
```
Expected: 9 passed. The fix is two lines and each failing case was off by exactly one day in the direction it
corrects — but that is reasoning, not a green bar, and it is recorded as outstanding rather than claimed.

### ⚠️ Two lessons from this run, worth keeping
- **`--no-incremental` is mandatory for the verification build here.** Three consecutive builds reported
  **`0 Error(s)` while silently producing a stale assembly**: `MoneyReadConsistencyTests` had a genuine missing
  `using` (`Application.Features.Invoices.Queries`) and two whole classes were absent from the DLL, yet the build
  looked green and 68 tests "passed" against the old binary. The tell was `--ListTests` finding 0 occurrences of a
  class that was demonstrably on disk and demonstrably passed to `csc`.
- **A `dotnet vstest` filter that matches nothing prints `No test matches`, which reads like a pass** when grepped
  for `Failed:`. Always confirm a per-class count rather than trusting a combined `|` filter's total.

### Escalation call (recorded per the skill)
Nine test classes exceeds the skill's ~5-class "too big" signal. **Not escalated**, under the documented carve-out:
this is a hardening pass fanning *thin* classes across *existing* handlers (11 defect fixes, no new user flow), each
mirroring a sibling. Judged by breadth of new behaviour, not class count.

### Diff audit against the device contract (done in place of the eye pass)
Not a substitute for looking at it — recorded so the manual pass knows what to focus on.

- **§ 3 (16 px fields).** Every converted `Input` either passes no `className` or an already-`md:`-prefixed
  one (`clinic-settings`: `h-8 md:text-sm`). No unprefixed `text-sm` was introduced, so no primitive's
  `text-base` is stripped by tailwind-merge. The conversion **removes** `min`/`step` attributes only —
  it touches no class.
- **§ 2 (44 px on coarse).** `globals.css` already floors `input` at `min-height: 44px` under
  `(pointer: coarse)`, and `type="number"` → `type="text"` does not leave that selector. No
  `.touch-target` added, so no overlay-steals-neighbour risk.
- **§ 8 (11 px floor, no pixel sizes).** Both new notices use `text-xs` (12 px). No `text-[Npx]`.
- **§ 10 (ungated grids / popovers).** Neither notice introduces a grid; the J11 one is `flex gap-2`,
  the J1 one a single paragraph.
- **Token discipline.** J11's notice uses the `bg-warning-wash` + `text-warning-ink` pair (not
  `amber-*` with a hand-maintained `dark:` twin); J1's uses `bg-muted/40` + `text-muted-foreground`.
  No double-opacity `/30/10` class.
- **§ 13 (UX floor).** Both notices carry `role="note"`; the J11 icon is `aria-hidden`. Neither is
  interactive, so neither needs a focus path. The `DiscardChangesDialog` is the repo's existing shared
  component — one wording, `AlertDialogCancel` (« Continuer la saisie ») as the default, destructive
  action on the right.
- **§ 5 (dirty guard channels).** `useDirtyGuard` covers ✕ / `Escape` / outside tap through Radix's
  single `onOpenChange`, **and** the Android back gesture via its own history entry. That is the whole
  « entered data confirms before discarding on every channel » requirement, satisfied by reusing the
  hook rather than re-deriving it.

**What the eye pass still has to catch, and this audit cannot:** whether the two new notice blocks push
the sticky footer of their card/sheet off a 380 px-tall landscape phone, and whether the J11 notice's
three paragraphs make the billing card overlong at 320 px.

### Still to do when we resume
1. **Re-run the frontend gate** once the parallel session's files settle (`tsc` + `build`), to get a
   whole-project green rather than a J-files-clean green.
2. The **eye pass** at 320/390/820/1180/1440 + landscape + keyboard, focused on: the six J9 dialogs
   (all bottom sheets or `mobile="sheet"` below `md:` — check the discard prompt is reachable, the sheet
   still sizes in `dvh`, and the footer is not pushed under the keyboard), the Échéancier card's new
   billed-plan notice, and the new TVA notice in clinic settings.
3. The **hand entry of `45,500`** into each converted field on a French-locale phone/emulator, plus a
   paste of `1 200,500` (non-breaking space) and a check that a bare `,` is refused rather than sent.
4. `/test-small-feature`.
5. Decide whether the sibling-session compile fix (DEV-4a, `AuthController.cs`) stays in J's commit or
   moves to spec I's.

## Auto-Approved Deviations

| Deviation | Reason |
|-----------|--------|
| `MoneyReadConsistencyTests` mock returns the new 3-tuple, mirroring the repository's new projection | Build-required: the solution does not compile otherwise. Assertion intent preserved. |
| `TreatmentPlanTenantIsolationTests` — `RecordInstallmentPaymentCommandHandler` gained `IInvoiceRepository` (J1, prior session), so the 5-arg call no longer compiled; the class already had an `_invoices` mock | Build-required. **Also moved its `PaidOn` from 2026-08-05 to 2026-01-05**: the handler now validates the date *before* resolving the clinic, so a future date made the test pass on « date dans le futur » and stop exercising the tenant guard — green for the wrong reason, the exact trap `UnitTests/CLAUDE.md` documents. |
| `CreditNoteReadTests` — `GetInvoiceRevenueQueryHandler` gained `ITreatmentPlanRepository` (J5, prior session); added an unstubbed `Mock<ITreatmentPlanRepository>` | Build-required. Left unstubbed on purpose: it returns 0, so both cases keep asserting exactly what they were written for (the avoir netted in **both** branches) with the expected 350 unchanged. |
| `InvoicePdfData.PatientAddress` is a single formatted string, not the `Address` value object | The renderer needs one line; shipping the VO would put French address composition in the PDF service. `FormatAddress` has one call site. |

## Significant Deviations

### DEV-1 — implemented on `feature/audit-sections-3-to-10`, not a new branch off `main`
The spec says « Branch: new, off `main` ». The working tree already held 208 files of the
`audit-sections-3-to-10` batch plus a prior session's J1/J2/J4/J5/J6 work **uncommitted on this
branch**. Branching off `main` would have stranded that work and made J's own backend half
unbuildable. Continuing here is the only option that keeps the pass coherent.
**Impact:** J's commits must stage paths explicitly. **Approved:** implicit (continuation).

### DEV-2 — J3 fixed on the **render** side, not by changing what is stored
Spec: « `Invoice.IssueDate`, `CreditNote`'s date and `TreatmentPlan.AcceptedDate`: set from the
clinic-local day ». The prior session instead introduced `PdfGenerationService.FrDay` =
`ClinicClock.ToClinicLocal(instant)` and routed **every** money-document date through it, leaving
`DateTime.UtcNow` in the three entities. Verified this session and kept, because it is the better
call and the spec itself flags J3 as « the highest-risk item »:

- The stored instant is already correct — every money read buckets it through `ClinicClock`'s
  local-day bounds, so a note issued 00h30 Tunis on 1 January already books into January.
- Storing a clinic-local wall-clock value instead would shift the instant by an hour, and
  `ApplicationDbContext` (which writes `Unspecified` as UTC) would **bake that in**, moving which
  month every *past* note books into — precisely the « if any closed month shifts, stop » risk.
- The frontend was never wrong: `formatDateFr` → date-fns formats in the browser's own zone.

So « the printed date and the sequence year agree » is satisfied, and no month attribution moves.
Verified no raw `:dd/MM/yyyy` remains on an instant in the PDF service (the two survivors format
user-typed date *strings*, which are correctly zone-free).
⚠️ `reconcile-money` before/after was **not** run — with no stored value changed there is no
boundary to diff, which is the whole point of this deviation. Say so rather than implying it ran.
**Approved:** Y (verified against the risk the spec itself names).

### DEV-3 — `clinic-settings.tsx` added to J8's list; the TVA **rate** field converted too
The spec enumerates ten files and does not name `clinic-settings.tsx`, whose « Montant du timbre (DT) »
is a millime-precision money field with the identical defect. Asked; user chose **include it**, and
also the « Taux de TVA (%) » field beside it — not money, but « 7,5 » is what a French keyboard types
and a `type="number"` input refuses the comma and returns an empty value.
**Impact:** one more file in the diff; no contract change. **Approved:** Y (user, this session).

### DEV-4 — two compile fixes in **another session's** in-flight files
Neither belongs to J; both blocked J's own gate.
- **4a** `api/…/Controllers/AuthController.cs` — added the missing
  `using ClinicManagement.Application.Common.Authorization;` for the `AuthorizationPolicies.Authenticated`
  class policy that sibling spec **I** (access-control) introduced. Every other controller already has
  this using; without it the solution had 1 error and J's backend could not be verified.
- **4b** `web/components/patient-record-modal.tsx` — briefly added a `PatientAlertPanel` import for a
  usage that spec I's session had added without one, then **backed it out** when that session added the
  same import itself (duplicate identifier). Net effect on this file from 4b: **none**.
**Impact:** 4a is a one-line additive using in a file J otherwise does not touch — it should probably
be committed with spec I, not with J. **Approved:** judged (build-required, zero behaviour change).

### DEV-5 — J11's « one-time admin prompt » is a **persistent notice with no stored state**
The spec asks for a one-time prompt *and* pins « Data / Schema Changes: **None** ». A durable
per-clinic « already decided » flag needs storage, so the two cannot both hold. Asked; user chose the
**persistent notice**: shown in clinic settings whenever `vatApplicable` is false, citing Tableau « B »
nouveau § II n° 1, and explicitly saying that a cabinet under the forfait régime is correct as-is.
Rejected alternatives: a `localStorage` dismissal (reappears on the reception PC after being dismissed
on the tablet — worse than an accurate banner) and a `VatPositionAcknowledgedAt` column + hand-authored
migration (contradicts the spec's « None », and `dotnet ef` is WDAC-blocked here).
**Impact:** a non-assujetti clinic sees an informational banner permanently. It is accurate, it is not
an error state, and it names their case. **Approved:** Y (user, this session).

### DEV-6 — a test fixture constant changed because J11 changes a default
`InvoiceFromDentalRecordTests.SessionTtc` 331 → 354.100. Not a test-scenario change but the direct
arithmetic consequence of `VatApplicable = true`: the same session now carries 7 % TVA. Flagged as
significant rather than auto-approved because it is a **failing**, not merely non-compiling, test —
the skill forbids leaving one in that state. `TotalHt` is still asserted separately at 330, which is
the figure the *acts* determine and must not move when the clinic's tax posture does.
**Approved:** judged (build-and-green required).

### DEV-7 — J8 extended to **seven** sites the spec did not enumerate
The spec lists ten files; the user approved an eleventh (DEV-3). A grep for `replace(",", ".")` after
centralising the helper then found five more hand-rolled copies and two more money fields on
`type="number" step="0.01"` (table above). Converted them **without asking**, on this reasoning:

- The item's own stated root cause is that the helper was unimportable and therefore *retyped or
  skipped*. These five are the retyped copies. Centralising the helper and leaving five stale duplicates
  behind would make the diff look like the fix while preserving the defect — the exact `fixes-dont-propagate`
  shape (12 confirmed prior instances in this repo).
- Each change is the same two lines already applied ten times, in files that already refuse a non-finite
  value in French, so the refusal path is behaviour-identical and only the accept path is fixed.
- `appointment-acts-picker.tsx`'s « Montant » writes a `ProcedureType.defaultCost` — literally the same
  field the spec calls « the field that seeds every invoice line », reached from the booking dialog
  instead of the catalogue. Fixing one and not the other is not a defensible boundary.

**Impact:** +7 files (24 total in the frontend half), no contract change, no behaviour change on any
refusal path. **Not** asked because the classification is unambiguous (internal, same behaviour on valid
input, no new dependency, and the spec pins the *approach* — « convert every money field » — rather than
the file list). Flagged as significant anyway because it widens the diff past the spec's enumeration.
**Approved:** judged.

## Deferred to `/test-small-feature`

The spec's own list, all still outstanding: `InstallmentOnBilledPlanIsRefusedTests` (J1, incl. the
cancelled-bridge case) · `PaymentDateRulesUsesClinicDayTests` (J3, « today » at 00:30 Tunis accepted) ·
`CancelledInvoiceIsNotDispatchedTests` (J4, `Queued` **and** `Signed`) · `MoneyReadConsistencyTests`
extended to **three** reads (J5 — the load-bearing one) · the credit-note VAT/stamp split (J6, the
100 DT / 7 % / 1 DT case).

Plus what this session's work newly enables:
- J7: the oldest-unpaid-issue-date aggregation, and that « Retard » takes the **earlier** of the two
  tracks (a six-month-old note beside a week-late échéance reads six months).
- J8: `parseAmountInput` as a pure unit — `45,500` · `1 200,500` with a non-breaking space · a bare
  `,` → NaN · `1,2,3` → NaN · `1.200,500` → NaN · a negative round-tripping.
- J10: the timbre footer gated (a clinic with the timbre off must not assert one), and a patient with
  no address rendering rather than throwing.
- J11: a newly created `Clinic` defaults to `VatApplicable = true` at 7 % while an existing row is
  untouched.

⚠️ Per `smart-app-control-blocks-tests`: `dotnet test` fails at load with `0x800711C7` here (SAC ON,
environmental). Write them; verify via the `-p:OutDir=<scratch>` + `dotnet vstest` workaround or
elsewhere.
