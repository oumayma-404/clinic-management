# Progress: Adoption QA — F (Avoir / credit-note)

**Started:** 2026-07-24
**Type:** Small (forced follow-up)
**Branch:** feature/windows-desktop-app

## Status
- [x] Implementation
- [x] Quality checks (dotnet build 0/0; web tsc 0; next build ok)
- [ ] Tests (handled by /test-small-feature)

## Files Changed
- `api/.../Domain/Entities/CreditNote.cs` (new aggregate) + `Domain/Repositories/ICreditNoteRepository.cs` (new).
- `api/.../Infrastructure/Persistence/Configurations/CreditNoteConfiguration.cs` (new); `Repositories/CreditNoteRepository.cs` (new).
- `api/.../Infrastructure/Persistence/ApplicationDbContext.cs` — `CreditNotes` DbSet + clinic query filter.
- `api/.../Infrastructure/Extensions.cs` — register `ICreditNoteRepository`.
- `api/.../Infrastructure/Migrations/20260724144012_AddCreditNotes.*` — new `CreditNotes` table (generated via `dotnet ef`).
- `api/.../Application/DTOs/CreditNoteDto.cs` (new); `Features/Invoices/Commands/CreateCreditNoteCommand.cs` (new, numbering retry + ≤collected guard + tenant check).
- `api/.../Application/Features/Billing/Queries/GetCaisseSummaryQuery.cs` — net avoir refunds out of CashIn.
- `api/.../Application/Features/Invoices/Queries/GetInvoiceRevenueQuery.cs` — net refunds out of collected (windowed branch).
- `api/.../API/Controllers/InvoicesController.cs` — `POST {id}/avoir` (AdminOrDoctor).
- `web/lib/api/invoices.ts` — `createAvoir`; `web/components/factures/invoices-table.tsx` — "Établir un avoir" action + modal on paid/partially-paid rows.

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Caisse/recettes net avoirs into CashIn / TotalCollected instead of adding a `Refunds` DTO field | Keeps the caisse reconcilable (CashIn − CashOut = Net) with no FE change; a dedicated "Avoirs" line is a follow-up. |
| No per-invoice avoir badge/total on `InvoiceDto` | No AC requires it; avoids widening the invoice query/DTO. The avoir action + caisse netting are the ACs. |
| Endpoint returns `CreditNoteDto` (spec allowed "InvoiceDto or AvoirDto") | Returns the created resource; cleaner. |

## Deferred to /test-small-feature
Avoir scenarios: ≤-collected guard, cumulative-avoir cap, caisse/revenue netting by RefundedOn, tenant isolation, numbering-collision retry.
