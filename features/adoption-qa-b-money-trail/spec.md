# Spec: Adoption QA — Batch B (the money trail)

**Status:** APPROVED
**Type:** Small (forced multi-item pass — user fed the full adoption-QA blueprint)
**Created:** 2026-07-24
**Scope:** Full
**Branch:** feature/windows-desktop-app (existing)
**Feature:** Make the devis → facture → CNAM money trail coherent — the focused pass deferred as `P0-3` in `derive-and-confirm-batch-2`. Both design forks pre-decided by the user: **linked bridge** (B4) and **block-void-when-paid** (B5).

## Overview
Today a draft quote shows as patient debt, a fully-treated plan can never reach "Terminé", a plan with no échéancier can never take a payment, there is no devis→facture bridge (acts re-typed and double-counted), voiding a paid invoice silently erases its cash, and the two "encaissé" figures disagree. This batch fixes all six as one consistent model: a plan counts as debt only once accepted, becomes payable, can be completed, can be billed into a linked invoice (counted once), and paid cash can never be silently voided.

## What Changes
- **B1 — Draft ≠ debt.** `GetPatientBillingSummaryQuery.cs:73` counts only plans `Accepted | InProgress | Completed` (exclude **Draft** + Cancelled). One filter fixes both the `Outstanding` sum (`:77`) and the CNAM estimate loop (`:97-104`). Mirrors the invoice filter at `:68`.
- **B2 — Reach "Terminé".** New `CompleteTreatmentPlanCommand` (+ handler calling existing `TreatmentPlan.Complete()`), `POST /treatment-plans/{id}/complete`, and a "Terminer" button in `treatment-plans-table.tsx` (activates the dead `Completed` branch `:276`). Also auto-`Complete()` in `MarkTreatmentPlanItemDoneCommandHandler` when the last item flips to Done. Copy `AcceptTreatmentPlanCommand` + its endpoint + FE `handleAccept`.
- **B3 — Payable plan without échéancier.** On `Accept()`, if `SetInstallments` was empty, auto-generate one lump-sum installment = `TotalPlanned`, so every accepted plan is payable through the existing per-installment "Encaisser" path.
- **B4 — Linked devis→facture bridge.** New nullable `Invoice.TreatmentPlanId` (migration). New `CreateInvoiceFromTreatmentPlanCommand` (inject `ITreatmentPlanRepository`, tenant-check, map `plan.Items` → invoice lines: `DesignationFr→Designation`, `PlannedCost→UnitPriceHt` qty 1, carry `DentalActCodeId`/`CodeActe`), `POST /invoices/from-plan/{planId}`, FE "Facturer le devis" button. `GetPatientBillingSummaryQuery` counts the **linked invoice when one exists, else the plan's `Outstanding`** — never both.
- **B5 — Block void of a paid note.** `Invoice.Cancel()` (`:211`) throws if any `Payment` exists; `invoices-table.tsx:299` hides "Annuler" for `Paid`/`PartiallyPaid`. (Full Avoir credit-note is a documented follow-up.)
- **B6 — "Total encaissé" attribution.** `GetInvoiceRevenueQuery.cs:50-55` derives `TotalCollected` from payment date, reusing `InvoiceRepository.GetCollectedBetweenAsync` (PaidOn); `TotalInvoiced` stays on IssueDate.

## API Contract
### POST /api/treatment-plans/{id}/complete
Response 2XX: `TreatmentPlanDto` (Status = Completed)
Errors: `400 plan not Accepted/InProgress OR items not all Done` · `404 not found / other clinic`

### POST /api/invoices/from-plan/{planId}
Response 2XX: `InvoiceDto` (Draft, lines seeded from plan, `treatmentPlanId` set)
Errors: `400 plan not Accepted/empty` · `404 not found / other clinic`

## Data / Schema Changes
- `Invoice.TreatmentPlanId` — `Guid?`, nullable, FK → `TreatmentPlan`. EF migration; no backfill (existing invoices stay null).

## Acceptance Criteria
- **AC-1:** A Draft plan contributes 0 to "Solde dû total" and to the CNAM estimate; an Accepted/InProgress plan contributes its `Outstanding`.
- **AC-2:** A plan with all items Done can be set to Completed via the endpoint/button; marking the last item Done auto-completes it. The FE "Terminé" badge is reachable.
- **AC-3:** Accepting a plan that had no installments creates one lump-sum installment equal to `TotalPlanned`; a payment can then be recorded and `Outstanding` decreases.
- **AC-4:** "Facturer le devis" creates a Draft invoice whose lines match the plan items and whose `treatmentPlanId` links back; the plan is no longer double-counted in "Solde patient" once linked.
- **AC-5:** Attempting to cancel an invoice that has any recorded payment is rejected (domain + hidden UI action); a Draft/Issued unpaid invoice still cancels.
- **AC-6:** A payment collected in a different period from the invoice's issue date is attributed to the collection (PaidOn) period in "Total encaissé", matching the caisse figure.
- **AC-7:** New commands (`Complete`, `CreateInvoiceFromTreatmentPlan`) carry a tenant-isolation assertion (reject cross-clinic ids).

## Out of Scope
- Full **Avoir** / credit-note entity + numbering sequence (follow-up; B5 only blocks the unsafe void).
- Editing seeded invoice lines beyond the normal invoice editor.
- Migrating/reconciling invoices that were created before the link existed.

## Edge Cases (Critical only)
- B3: an empty `SetInstallments` after B3 must not produce a second lump-sum on re-accept; guard for an existing schedule.
- B4: a plan already fully billed (linked invoice exists) should refuse a second bridge invoice or warn.
- B4 dedup: a Completed plan with residual `Outstanding` and no linked invoice still counts as debt.
