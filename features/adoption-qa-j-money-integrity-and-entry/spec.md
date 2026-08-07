# Spec: Adoption QA — J (money integrity and entry)

**Status:** APPROVED
**Type:** Small (single-theme multi-item pass — one clock, one guard, one helper, propagated)
**Created:** 2026-08-03
**Scope:** Full
**Branch:** new, off `main`
**Feature:** Every dinar can be **entered** on the device it is collected on, is **counted once** by every read, and can be **corrected** — closing the two money Blockers (cash that vanishes; amounts that cannot be typed on a tablet) and the six consistency defects behind them.

> **Why this is one feature, not several.** All of it is the same three root causes: a guard that was documented and not called, a clock that was fixed everywhere except the one validator every money date flows through, and a helper that was written with a docstring naming its own bug and then applied to one field out of ten. Fixing them separately would mean touching the same files three times.

## Context — what the review confirmed

- `RecordInstallmentPaymentCommand` contains **zero** references to invoices or billing — no already-billed guard — while both installment money reads carry `&& !excluded.Contains(p.Id)` unconditionally and `CarryOverPlanPaymentsAsync` runs **once, at issue**. So an échéance collected after the devis is bridged to a note reduces the patient's balance and reaches **no** money read.
- `PaymentDateRules`' own docstring names "an installment payment's" as a caller. Grep finds three call sites; the installment path is not one.
- `PaymentDateRules` uses `DateTime.UtcNow.Date`, not `ClinicClock.ClinicToday()` — so between 00:00 and 01:00 Tunis, the date the client itself pre-filled with `todayLocalIso()` is refused as "in the future". The P6 sweep fixed the numbering and the caisse default and left the one validator every money date flows through.
- `Invoice.IssueDate = DateTime.UtcNow` while the sequence year comes from `ClinicClock.ClinicYear()` — a note issued at 00h30 on 1 January is numbered `2027-0001` and prints « Le 31/12/2026 ». Same split in `CreditNote` and `TreatmentPlan.Accept`.
- `Invoice.Cancel()` refuses only `Valid | Submitted | Validating`; `Queued` and `Signed` pass, `CancelInvoiceCommand` never dequeues, and `EInvoiceService.ProcessAsync` never consults `Invoice.Status` — so a cancelled note is declared to TTN and comes back « validée ».
- `GetInvoiceRevenueQuery` counts **invoice payments only** while caisse and dashboard add installments. `MoneyReadConsistencyTests` pins caisse↔dashboard and never touches this third read.
- `parseAmountInput` exists with a docstring stating the exact defect — "*a `type="number"` input refuses a comma, and `e.target.value` comes back **empty**… so the dentist typed an amount, saw a filled field, and the submit sent nothing*" — and is used on **1 of 10** money fields.
- `useDirtyGuard` + `DiscardChangesDialog` exist and are wired into five forms; not the booking, payment, échéance, fiche-billing or expense dialogs.

## What Changes

### J1 — An échéance on a billed plan is refused (Blocker)

- `RecordInstallmentPaymentCommand`: refuse with a French `Result.Failure` when the plan is represented by a non-cancelled invoice. Reuse `PlanBillingRules.RepresentsItsPlan` — the same authority the reads already use, so the guard and the exclusion cannot disagree.
- The message must name the action: « Ce devis est facturé (note n° X). Enregistrez le paiement sur la note d'honoraires. »
- `plan-workspace.tsx`: `canCollect` gains the billed condition, so « Encaisser » is disabled behind the « Facturé — n° » badge that already renders beside it. Include the reason as visible text, not a `title` (a tooltip is unreachable on touch).
- **Recommended over** teaching the reads to include billed plans: the plan's money is deliberately excluded because the invoice now represents it; including it would double-count. Refusing at the write is the only correct side.

### J2 — `PaymentDateRules` is called where its docstring says it is (Major)

- Call it from `RecordInstallmentPaymentCommand`. Today an échéance can be dated next month (drops the balance now, appears in no caisse until then) or `01/01/0001` (drops the balance forever, appears in no caisse ever).
- Audit the other two ledgers for the same omission while the file is open.

### J3 — One clock for every money date (Major)

- `PaymentDateRules`: `ClinicClock.ClinicToday()` instead of `DateTime.UtcNow.Date`.
- `Invoice.IssueDate`, `CreditNote`'s date and `TreatmentPlan.AcceptedDate`: set from the clinic-local day so the **printed date and the sequence year agree**. A 2027 sequence number on a 2026-dated document is what an accountant rejects.
- ⚠️ Keep the stored value an **explicit UTC instant** — `ApplicationDbContext` treats `Unspecified` as UTC on write, so handing a bare local value shifts every boundary by an hour.
- ⚠️ Bucketing reads (`GetInvoicedBetweenAsync`) key on `IssueDate`; changing it moves which month a note books into. Verify against `reconcile-money` before and after and diff — that is what the verb is for.

### J4 — A cancelled invoice is never declared (Blocker)

- `Invoice.Cancel()`: refuse or dequeue `Queued` and `Signed`. **Recommended: dequeue** (clear `EInvoiceStatus`/`EInvoiceNextAttemptAt`) rather than refuse — a dentist must be able to cancel a mis-keyed note that happens to be queued, and refusing would make it uncancellable.
- Belt and braces: `EInvoiceService.ProcessAsync` also checks `Invoice.Status != Cancelled` before submitting, and `GetDueForElFatooraDispatchAsync` excludes cancelled rows. Three guards because a note validated at TTN can never be cancelled there.

### J5 — The third money read joins the other two (Major)

- `GetInvoiceRevenueQuery.TotalCollected` adds `GetInstallmentCollectedBetweenAsync`, using the **same** `PlanBillingRules.BilledPlanIds` de-dup the other two use.
- Route it through `InvoiceCalculator.RoundMoney` — it is currently the only money read that does not.
- Extend `MoneyReadConsistencyTests` to pin **all three** reads over one window, not two. The test that existed is precisely why this defect survived.

### J6 — The avoir reverses the TVA that was charged (Major)

- `GetCreditNotePdfQuery`: the timbre is **outside** the VAT base (`ttc = ht + vat + stamp`), so de-VATing the whole credited TTC over-reports TVA. Subtract the stamp before de-VATing, or better, derive the split from the invoice's frozen `TotalHt`/`TotalVat` proportionally.
- A full-value avoir on a 100 DT HT / 7 % / 1 DT stamp note must report HT 100,000 + TVA 7,000 + timbre 1,000 — not HT 100,935 + TVA 7,065.

### J7 — Invoice debt is aged (Major)

- `IInvoiceRepository.GetOutstandingByPatientAsync` returns `(PatientId, Outstanding)` with **no date**, so `oldestOverdue` is populated only in the plan loop and the « Retard » column is blank for pure invoice debt — which is where most of the debt is.
- Return the oldest unpaid issue date alongside the total; populate `ReceivableDto.DaysOverdue` from it in both branches.

### J8 — Money can be typed on the device it is collected on (Blocker)

- Convert every money field to `type="text" inputMode="decimal"` + `parseAmountInput`: `payment-modal.tsx:108`, `installment-payment-modal.tsx:138`, `bill-dental-record-dialog.tsx:190`, `patient-record-modal.tsx:1029` (« Payé »), `record/act-detail-fields.tsx:65` (« Tarif »), `caisse/page.tsx:733` (dépense), `invoice-form-modal.tsx`, `revise-installments-modal.tsx`, `treatment-plan-form-modal.tsx`.
- `procedure-type-form-modal.tsx:318`: `step="0.01"` makes **millimes unreachable on the field that seeds every invoice line** — and its `placeholder="Ex. 70,000"` shows a comma the input rejects. Same conversion.
- Stop prefilling with `String(invoice.outstanding)` — it renders `45.5` in a product that prints `45,500`. Use `lib/format.ts`; never hand-format a dinar.
- **Move `parseAmountInput` into `lib/format.ts`** and export it. It is currently module-private in `invoices-table.tsx`, which is exactly why it was applied once — a helper nine files cannot import is a helper that gets retyped or skipped.

### J9 — A typed payment is not discarded by a stray tap (Blocker)

- `useDirtyGuard(open, onOpenChange)` + `<DiscardChangesDialog guard={guard}/>` on `create-appointment-dialog.tsx:545`, `edit-appointment-dialog.tsx:465`, `payment-modal.tsx:89`, `installment-payment-modal.tsx:121`, `bill-dental-record-dialog.tsx`, and the caisse expense dialog (`caisse/page.tsx:682`).
- The last four are `mobile="bottom"` sheets, so on a phone the strip above the sheet is a live dismiss target over money being entered — per `.claude/rules/frontend-web.md` § 5, entered data confirms before discarding on **every** channel: swipe, back gesture, outside tap, close control.

### J10 — The printed note carries its mandatory mentions (Major)

- `InvoicePdfData` gains the **patient address** (and CIN if the patient carries one — see the deferred note below). Today the render model has no patient address at all.
- `PdfGenerationService:265` prints « Note d'honoraires soumise au timbre fiscal » **unconditionally**, while the timbre line itself is conditional — so a note with the timbre switched off asserts a timbre. Gate the footer on the same condition.
### J11 — The default tax position is wrong, and it is the wrong way round (Major → treat as Blocker for any clinic invoicing for real)

**The regulatory research completed after this spec was first drafted and overturned its premise. Dental acts are NOT TVA-exempt in Tunisia — they are taxable at the reduced rate.**

- **Code de la TVA, Tableau « B » nouveau, § II « Les activités et les services », n° 1** lists services performed by *« les médecins, les médecins spécialistes, **les dentistes**, les sages-femmes et les vétérinaires »* among services **subject to VAT at the reduced rate**. **Confirmed** — official Ministry of Finance consolidated code.
- Cross-checked the other way: **Tableau « A » nouveau (exonérations) contains no hit for médecin / dentiste / soins / santé / clinique.** There is no exemption to invoke. **Confirmed.**
- The code text says 6 %; LF 2018 re-based the rates to 19 / 13 / 7, so dental acts are **7 %**. The rate structure is Confirmed; the 6→7 mapping onto the dentist line is **Likely** (no post-2018 text expressly restates it).
- **Code TVA art. 18 § II** requires the invoice to carry *« les taux et les montants de la taxe sur la valeur ajoutée »*. The client's **address and fiscal-ID number are required only for clients subject to the déclaration d'existence** — i.e. businesses, **not a private patient**. The Code also expressly recognises **« notes d'honoraires »** as the equivalent fiscal document. So the app's instrument is right and must carry the rate and amount.

**What changes:**
- `Clinic.SetBillingSettings` defaults become **`VatApplicable = true`, `VatRate = 7m`** for a newly created clinic. Keep both editable — a non-assujetti under the forfait régime is a real case, and the rate can move by finance law.
- **Do not migrate existing clinics silently.** Changing `VatApplicable` retroactively would alter what already-issued notes assert. New clinics get the corrected default; existing ones get a **one-time admin prompt** in clinic settings stating the legal position with the Tableau B reference, and the admin decides.
- **The « exoneration mention » item is withdrawn** — it was premised on dental acts being exempt. A general obligation to state a legal ground when omitting TVA is **Unverified — no source found**; the only mentions the Code prescribes are *« vente à l'exportation »* and the suspension formula, neither of which applies. Do **not** add a mention field.
- The invoice PDF must print the TVA **rate and amount** whenever VAT applies (it already does when `VatApplicable`) — the fix is the default, not the renderer.
- ⚠️ **`StampDutyAmount = 1.000` is correct and stays.** Code des droits d'enregistrement et de timbre **art. 117 § I n° 6°**: *« Les factures … 1,000 par facture »*. **Confirmed.** LF 2026's 1,5 / 2 DT tiers apply to **grandes surfaces only** (built area > 3 000 m²) and never to a cabinet. Whether the 1 DT strictly attaches to a *note d'honoraires* is **Likely (inference)** — the tariff says *« factures »* unqualified and no text names notes d'honoraires; it is 1 DT per document either way, so the default is safe.
- Still keep `PdfGenerationService:265`'s unconditional « soumise au timbre fiscal » footer gated on the timbre actually applying (that half of the original item stands).
- `InvoicePdfData` still gains the **patient address** — harmless and useful — but note it is **not** legally required for a private patient, so it must not become a validation blocker.

## Data / Schema Changes

- **None.** The one column this spec originally called for — a TVA-exoneration mention on `Clinic` — is **withdrawn** with J11, because dental acts are not exempt and no such mention is legally prescribed. Every item is behaviour, defaults, validation or UI.
- J11 changes a **default value** in `Clinic.SetBillingSettings`, not a column. Existing rows are untouched by design.
- Run **`dotnet run -- reconcile-money`** before and after the whole batch and diff — J3 moves date boundaries and J5 changes a total, which is exactly the class of change that verb exists to prove safe. No migration ⇒ `verify-schema` is not required, though running it costs nothing.

## API Contract

### POST /api/treatment-plans/{id}/installments/{installmentId}/payments  (J1) — new refusal
Errors: `400` with `{ error }` naming the invoice number and directing the user to the note d'honoraires.

### GET /api/billing/receivables  (J7) — response widened
`ReceivableDto.DaysOverdue` is now populated for invoice-only debt (was always `null`).

### GET /api/invoices/revenue  (J5) — value change, same shape
`totalCollected` now includes devis instalments. **This is a breaking change to a displayed figure**: `/factures` will show a larger number that finally agrees with la caisse and the dashboard. Note it in `progress.md`.

## Out of Scope

- **Cheque tracking** (number, bank, échéance date for post-dated cheques) and **per-payment-method totals** (a cash-only figure for the drawer). Both are real deal-breakers from the review and both need new columns plus a ledger filter plus UI — a feature of their own, not an item here.
- **CNAM tiers-payant as a receivable — this is NOT a gap, and must not be built.** The 2026-08-03 review listed it as a deal-breaker; the regulatory research then **disproved that**. Convention sectorielle des médecins dentistes de libre pratique (Dec 2020, approved by arrêté du 3 février 2021, JORT 2021-014), **art. 56**: *« Le médecin dentiste consulté perçoit **l'intégralité** de ses honoraires du bénéficiaire qui se fait rembourser ultérieurement par la caisse. »* **Confirmed.** There is **no tiers payant for ambulatory dental care** — the dentist collects the full fee from the patient, who is reimbursed afterwards. So CNAM is never a debtor of the cabinet, `Invoice` correctly has no CNAM-part column, and the existing "indicative split" labelling is exactly right. Building a CNAM receivable would model a cash flow that does not exist.
- **A new opportunity this research surfaced, deliberately not in this spec:** convention **art. 57** forbids a conventioned dentist from charging a beneficiary **more than the honoraires conventionnels**, and any overrun is sanctionable. A warning when a line price exceeds the tarif conventionnel for a CNAM patient would be a genuine differentiator (no competitor knows this rule exists) — but it needs the corrected tariff data first (see spec K) and is a feature, not an item.
- **Revenue by act or by practitioner.** Needs `DoctorId` on the money entities, which do not have it.
- **CSV/Excel export.** Deferred with the import work.
- Any change to how the four-ledger caisse read is composed — `CaisseLedgerTests` pins Σ movements to the totals and that design is sound.
- The invoice draft form showing TVA/timbre before an irreversible issue (Major, deferred — J10 fixes the *printed* document, not the pre-issue preview).

## Edge Cases (Critical only)

- **J1 must not strand a legitimately unbilled plan.** A `Draft` bridge invoice does *not* represent the plan (`PlanBillingRules` already encodes this) — only a non-cancelled issued one does. Collecting on a plan whose bridge invoice was later cancelled must still work.
- **J3 is the highest-risk item in the spec.** Changing `IssueDate` moves which month a note books into. A note issued before the change and read after must not move. Diff `reconcile-money` output; if any closed month shifts, stop.
- J3 must not re-arm finding #20: `EndOfLocalDayUtc` is the *next* midnight (exclusive) while money reads are inclusive both ends — use `LastTickOfLocalDayUtc`.
- **J5 changes a number a dentist has been reading.** If they reconciled against the old `/factures` figure, the new one will look like a jump. Say so in the release note.
- J8: `parseAmountInput` must reject a bare `,` and a double separator without throwing — `Number.parseFloat` returns `NaN`, so the caller needs the existing `> 0` validation to catch it. Also handle a pasted `1 200,500` (non-breaking space).
- J8: converting to `type="text"` removes the browser's own min/step validation — the server-side amount validation must already be complete (the review confirms over-payment, negative and sub-millime are refused on both ledgers; assert it stays true).
- J9: the guard must not fire on a dialog the user never typed into (a pristine open→close must not prompt).
- J10: a clinic with no address or no matricule fiscal must still render a document, not throw.

## Testing

- New: `InstallmentOnBilledPlanIsRefusedTests` (J1) — including the cancelled-bridge-invoice case.
- New: `PaymentDateRulesUsesClinicDayTests` (J3) — assert a payment dated « today » at 00:30 Tunis is accepted.
- New: `CancelledInvoiceIsNotDispatchedTests` (J4) — `Queued` and `Signed` both.
- Extend `MoneyReadConsistencyTests` to three reads (J5) — the load-bearing test of this spec.
- New: credit-note VAT/stamp split test (J6) with the 100/7 %/1 DT case from the review.
- ⚠️ Per `smart-app-control-blocks-tests`: `dotnet test` fails at load with `0x800711C7` here (SAC ON, environmental). Write them; verify elsewhere.
- Frontend (J8, J9): no test runner exists in `web/`. The gate is `npm run check:responsive` + `npx tsc --noEmit` + `npm run build`, then a **hand pass entering `45,500` on a French-locale phone/emulator** — that is the only thing that proves J8, and `tsc` cannot see it.
