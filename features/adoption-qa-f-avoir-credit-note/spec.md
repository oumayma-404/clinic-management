# Spec: Adoption QA — F (Avoir / credit-note)

**Status:** APPROVED
**Type:** Small (forced follow-up — deferred from Batch B / finding #8)
**Created:** 2026-07-24
**Scope:** Full
**Branch:** feature/windows-desktop-app (existing)
**Feature:** Correct a *paid* note d'honoraires with a proper **avoir** (credit note). Batch B only *blocked* voiding a paid invoice; this gives the lawful correction path.

## Overview
Today a note with recorded payments cannot be cancelled (`Invoice.Cancel()` throws) and there is no way to reverse collected cash. This adds an **avoir**: a dated, numbered credit document tied to an invoice that offsets some or all of its collected amount, so the caisse/recettes reflect the net and the correction leaves an audit trail instead of a silent deletion.

## Design decision (recommended)
**Separate `CreditNote` (avoir) aggregate** with its **own** `AAAA-NNNN` sequence (like `TreatmentPlan` has one separate from `Invoice`), linked to the invoice by `InvoiceId`. Rejected alternative: a negative `Payment`/line on the invoice (breaks `Payment` immutability + the "no overpayment/positive-only" invariants and muddies the fiscal document). Confirm at approval.

## What Changes
- New `CreditNote` aggregate (clinic-scoped): `InvoiceId`, `Number` (own gapless per-clinic-per-year sequence), `IssueDate`, `Amount` (TND millimes, > 0, ≤ the invoice's collected amount), `Reason`, optional `Method`/`RefundedOn`. Numbering mirrors `IssueInvoiceCommand` (unique index + recompute-and-retry).
- `CreateCreditNoteCommand` + `POST /api/invoices/{id}/avoir`. Guard: invoice must be `PartiallyPaid`/`Paid`; sum of existing avoirs + new amount ≤ collected.
- Caisse/recettes net out avoirs: `GetCaisseSummaryQuery` and `GetInvoiceRevenueQuery` subtract credit-note amounts in the period (by `RefundedOn`/`IssueDate`), a new `ICreditNoteRepository.GetRefundedBetweenAsync` mirroring `InvoiceRepository.GetCollectedBetweenAsync`.
- FE: an "Établir un avoir" action on a paid row in `invoices-table.tsx` (admin/doctor) → amount+reason modal; the row shows an avoir badge/linked amount.

## API Contract
### POST /api/invoices/{id}/avoir
Request: `{ amount: decimal, reason: string, method?: string, refundedOn?: date }`
Response 2XX: `InvoiceDto` (or `AvoirDto`) reflecting the net
Errors: `400 invoice not paid / amount exceeds collected / reason missing` · `404 not found / other clinic`

## Data / Schema Changes
- New table `CreditNotes` (`Id`, `ClinicId` [query-filtered], `InvoiceId`, `Number`, `IssueDate`, `Amount decimal(18,3)`, `Reason`, `Method?`, `RefundedOn?`, `CreatedAt`). EF migration + unique index `(ClinicId, Number)` filtered non-null.

## Out of Scope
- Avoir PDF export (follow-up; the invoice/receipt renderers exist to extend later).
- El Fatoora e-invoice for credit notes (TTN avoir flow).
- Editing/deleting an issued avoir (append-only, like invoices).

## Edge Cases (Critical only)
- Multiple partial avoirs on one invoice must not cumulatively exceed collected.
- An avoir for the full collected amount = effective reversal, but the invoice keeps its number/status (no silent delete).
- Tenant isolation: cross-clinic invoice id → 404; the new command carries the `*TenantIsolation` assertion.
