# Progress: Adoption QA — Batch B (the money trail)

**Started:** 2026-07-24
**Type:** Small
**Branch:** feature/windows-desktop-app

## Status
- [x] Implementation
- [x] Quality checks (dotnet build 0/0; web tsc 0; next build ok)
- [ ] Tests (handled by /test-small-feature)

## Files Changed
- `api/.../Domain/Entities/TreatmentPlan.cs` — B3 auto lump-sum installment on `Accept()` when no échéancier.
- `api/.../Domain/Entities/Invoice.cs` — B4 `TreatmentPlanId` property + ctor param; B5 `Cancel()` throws if payments exist.
- `api/.../Infrastructure/Persistence/Configurations/InvoiceConfiguration.cs` — map `TreatmentPlanId`.
- `api/.../Infrastructure/Migrations/20260724125528_AddInvoiceTreatmentPlanLink.*` — additive nullable `Invoices.TreatmentPlanId` (generated via `dotnet ef`).
- `api/.../Features/Billing/Queries/GetPatientBillingSummaryQuery.cs` — B1 count only Accepted/InProgress/Completed; B4 skip plans billed to an issued invoice (dedup).
- `api/.../Features/TreatmentPlans/Commands/CompleteTreatmentPlanCommand.cs` (new) — B2 manual "Terminer".
- `api/.../Features/TreatmentPlans/Commands/MarkTreatmentPlanItemDoneCommand.cs` — B2 auto-complete when last item done.
- `api/.../Features/Invoices/Commands/CreateInvoiceFromTreatmentPlanCommand.cs` (new) — B4 linked bridge.
- `api/.../Features/Invoices/Queries/GetInvoiceRevenueQuery.cs` — B6 collected by PaidOn; outstanding per-invoice.
- `api/.../Controllers/TreatmentPlansController.cs` — `POST {id}/complete`.
- `api/.../Controllers/InvoicesController.cs` — `POST from-plan/{planId}`.
- `web/lib/api/treatment-plans.ts` (`complete`), `web/lib/api/invoices.ts` (`createFromPlan`).
- `web/components/treatment-plans/treatment-plans-table.tsx` — "Facturer le devis" + "Terminer" actions.
- `web/components/factures/invoices-table.tsx` — B5 cancel only when issued + zero collected.

## Design forks (pre-decided by user)
- B4 = **linked bridge** (Invoice.TreatmentPlanId; billing counts invoice when linked, else plan).
- B5 = **block cancel when paid** (avoir credit-note is a documented follow-up, out of scope here).

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| `Invoice.TreatmentPlanId` not surfaced on FE `InvoiceDto`/`ToDto` | Dedup is backend-only; no AC requires it in the DTO — avoids widening the pinned contract. |
| Migration generated with `dotnet ef` (not hand-authored) | `dotnet ef` works in this env (only `dotnet test` is WDAC-blocked); single additive nullable column. |
