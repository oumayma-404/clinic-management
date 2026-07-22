# Feature Specification: Unified Billing Ledger & Receivables

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-22
**Scope:** Full
**Feature:** Give the clinic a single, trustworthy view of money: one per-patient balance and a clinic-wide "who owes me" list across all payment tracks, installment revenue that actually reaches the dashboard, overdue detection, printable receipts, invoice PDFs that show what's paid vs owed, and the CNAM-reimbursable vs. out-of-pocket split on quotes and invoices.

## Overview
The app has three money systems that never reconcile: **devis → échéancier → installment payments** (`TreatmentPlan`/`Installment`), **note d'honoraires → payment** (`Invoice`/`Payment`), and a standalone **`DentalRecord.AmountPaid`**. Only invoice payments reach the dashboard, so a dentist running the natural Tunisian devis+installments flow sees **0 DT** revenue. There is no unified per-patient balance, no accounts-receivable list, no overdue tracking, no patient receipt, and the invoice PDF hides paid/outstanding. This feature makes **invoices + treatment-plan installments** the two authoritative money sources, aggregates them into one balance per patient and one clinic-wide receivables view, feeds installment collections into the dashboard, adds overdue detection and receipts, and surfaces the CNAM split (using the existing `CnamReimbursementCalculator`). Builds on `live-dashboard`, `facturation-note-honoraires`, `patient-record-payments-summary`, and `dental-core`.

## What Changes

### One per-patient balance + clinic-wide receivables
- A new **patient billing summary** aggregates, for a patient: total invoiced-outstanding (`Invoice.Outstanding` over non-cancelled invoices) + total plan-installment-outstanding (`TreatmentPlan.Outstanding`), yielding a single **"Solde patient"** shown on the patient page.
- A new clinic-wide **"Créances" (accounts-receivable) list**: every patient with a positive balance, their outstanding total, and the oldest overdue date — sortable, so the dentist can see who owes and chase them.

### Installment revenue reaches the dashboard
- `MonthlyRevenueCollected` on the dashboard includes **installment payments** collected in-month (from `RecordInstallmentPayment`), not just invoice `Payment`s. The dashboard gains an **"En attente de recouvrement" (total outstanding)** figure alongside "Encaissé ce mois-ci".

### Overdue / aging
- Installments past `DueDate` and not fully paid are flagged **"En retard"** in the plan UI, and the receivables list surfaces the aging (e.g. oldest overdue date / days overdue). No new scheduling job — computed on read against the current date.

### Patient receipts (reçu)
- Recording an invoice `Payment` **or** an installment payment produces a **printable receipt PDF** (clinic header, patient, date, amount, method, what it was for, running balance) via the existing `IPdfGenerationService`.

### Invoice PDF shows paid vs. owed
- The note-d'honoraires PDF shows **amount paid** and **outstanding** (currently totals HT/TVA/timbre/TTC only) by carrying payment figures into `InvoicePdfData`.

### Stop double-counting `DentalRecord.AmountPaid`
- Once a dental record is invoiced, its standalone `AmountPaid`/"Reste" becomes **read-only and clearly marked "facturé"** on the patient page, and the invoice is the source of truth for that record's money. The unified balance and all revenue/receivables figures derive **only** from invoices + installments — never from `DentalRecord.AmountPaid` — so the same acte is never counted twice.

### CNAM reimbursable vs. out-of-pocket
- The devis and the invoice show, per document, the **CNAM-reimbursable portion** and the **patient out-of-pocket (reste à charge)**, computed with the existing `CnamReimbursementCalculator` / `GetReimbursementEstimateQuery` over the CNAM-coded lines. Non-CNAM lines count fully as out-of-pocket.

## Acceptance Criteria
- **AC-1:** The patient page shows a single **Solde patient** = sum of non-cancelled invoice outstanding + plan installment outstanding for that patient; it matches the sum of the per-invoice and per-plan figures already shown.
- **AC-2:** A clinic-wide receivables view lists every patient with balance > 0, their outstanding total and oldest overdue date, sorted by amount (default) — clinic-scoped.
- **AC-3:** Dashboard "Encaissé ce mois-ci" includes installment payments collected in the current month; a devis-only clinic that collects installments no longer shows 0 DT.
- **AC-4:** The dashboard shows a clinic-wide total-outstanding figure.
- **AC-5:** An installment past its `DueDate` and not fully paid is labelled "En retard" in the plan UI and appears in the receivables aging.
- **AC-6:** Recording an invoice payment or an installment payment yields a downloadable receipt PDF containing clinic header, patient, date, amount, method, and remaining balance.
- **AC-7:** The invoice PDF shows amount paid and outstanding, consistent with the invoice's recorded payments.
- **AC-8:** After a dental record is invoiced, its record-level "Amount Paid"/"Reste" is read-only and marked facturé; no revenue/receivables/balance figure anywhere is derived from `DentalRecord.AmountPaid`.
- **AC-9:** The devis and invoice each display a CNAM-reimbursable total and a patient out-of-pocket total; the two sum to the document total, and non-CNAM lines are fully out-of-pocket.
- **AC-10:** All new figures (balance, receivables, revenue, receipts) are clinic-scoped and use the app's TND millime handling (`decimal(18,3)`, `formatDT`).

## API Contract
### GET /api/patients/{patientId}/billing-summary
Response 2XX: `{ invoiceOutstanding: decimal, installmentOutstanding: decimal, totalOutstanding: decimal, oldestOverdueDate: date|null, cnamReimbursable: decimal, patientOutOfPocket: decimal }`
Errors: `404` (patient/other clinic).

### GET /api/billing/receivables
Response 2XX: `[{ patientId, patientName, totalOutstanding: decimal, oldestOverdueDate: date|null, daysOverdue: int|null }]` — clinic-scoped, patients with balance > 0.
Errors: `401`.

### GET /api/payments/{paymentId}/receipt-pdf  and  GET /api/treatment-plans/{planId}/installments/{installmentId}/receipt-pdf
Response 2XX: `application/pdf` (receipt). Errors: `404` (not found / other clinic).

### Changed
- `GET /api/dashboard/stats` response gains `monthlyRevenueCollected` (now inclusive of installments) and `totalOutstanding`.
- Devis PDF (`GetDevisPdfQuery`) and invoice PDF (`GetInvoicePdfQuery` / `InvoicePdfData`) gain reimbursable + out-of-pocket totals; invoice PDF also gains amount-paid + outstanding.

## Data / Schema Changes
- **No new persisted money entity.** Balance, receivables, aging, revenue, and the CNAM split are **computed** from existing `Invoice`/`Payment`, `TreatmentPlan`/`Installment`, and the CNAM calculator.
- **`InvoicePdfData`** gains `AmountCollected`, `Outstanding`, `CnamReimbursable`, `PatientOutOfPocket` (all `decimal`). Devis PDF model gains the two CNAM fields.
- Repository additions: aggregate installment-collected-in-range (mirroring `IInvoiceRepository.GetCollectedBetweenAsync`) and a clinic receivables query.
- `DentalRecord.AmountPaid` keeps its column (informational); the change is that it is no longer read into any aggregate and is UI-locked once invoiced.

## Out of Scope
- A discrete payment-ledger entity per installment (installments keep `LastMethod`/`LastPaidOn`; no per-installment transaction history).
- Refund/void/correct-a-payment flows (`Payment` stays immutable) — a separate, explicit feature.
- Revenue-over-time charts, per-procedure, or per-dentist reporting (no practitioner attribution exists on money entities yet).
- Converting an accepted devis into an invoice automatically (linking `Invoice`↔`TreatmentPlan`) — noted as a candidate but not required here; reconciliation works via the computed aggregate.
- Changing Tunisian VAT/timbre defaults (configurable already in clinic settings).

## Edge Cases (Critical only)
- **Same acte in a devis and an invoice:** the aggregate counts invoice outstanding and installment outstanding separately by their own entities; it does not attempt to detect that a devis line and an invoice line are the "same" acte — the dentist bills through one track. Document this so the balance is understood as invoice-track + plan-track, not de-duplicated across a manual re-entry.
- **Cancelled invoices / cancelled plans:** excluded from balance, receivables, revenue, and aging.
- **Overpayment on an installment or invoice:** outstanding floors at 0 (never negative) in every aggregate, mirroring existing `RecordPayment` guards.
- **CNAM estimate unavailable** (line has no CNAM code / calculator returns nothing): that line is treated as 100% out-of-pocket; the split still sums to the total.
- **Local mode offline:** receipts/PDFs generate locally (no internet needed); dashboard/receivables are pure DB reads and work offline.
