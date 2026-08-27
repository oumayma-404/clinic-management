# Progress: Unified Billing Ledger & Receivables

**Started:** 2026-07-22
**Type:** Small (forced — see Significant Deviations DEV-1)
**Branch:** feature/unified-billing-ledger (worktree off feature/windows-desktop-app)

## Status
- [x] Implementation
- [x] Quality checks (backend `dotnet build` = 0 errors / 0 new warnings; frontend `npx tsc --noEmit` = 0 errors; `next build` = success)
- [ ] Tests (handled by /test-small-feature)

## Quality-check notes
- Backend: `dotnet build api/ClinicManagement.sln` → 0 errors; 10 warnings, all pre-existing baseline (`CS8618`/`CS8981`/`CS8602` in AppointmentsController, MedicalDocumentsController, PatientsController, ProcedureTypesController, Program.cs, Swagger filter) — none in changed/new files.
- EF migration `20260722184000_AddInvoiceLineCnamActCode` was **tool-generated** (`dotnet ef` 10.0.3 ran; WDAC did not block it) — snapshot + Designer updated by the tool.
- Frontend gate = `npx tsc --noEmit` (0 errors) + `npm run build` (success; ESLint not installed / `ignoreDuringBuilds`). New `/creances` route + updated `/`, `/patients/[id]` all compiled.
- Tests NOT run (this skill excludes tests; `dotnet test` is also WDAC-blocked locally per [[smart-app-control-blocks-tests]]).

## AC coverage
AC-1 Solde patient (billing-summary + patient card) · AC-2 Créances (`/billing/receivables` + `/creances`) · AC-3 dashboard installment revenue · AC-4 dashboard total outstanding · AC-5 overdue "En retard" + aging · AC-6 receipt PDFs (renderer + 2 endpoints + downloads) · AC-7 invoice PDF paid/outstanding · AC-8 DentalRecord.AmountPaid locked + never in aggregates · AC-9 CNAM split on invoice + devis · AC-10 clinic-scoped, decimal(18,3)/formatDT.

## Files Changed
### Backend
- `Domain/Entities/InvoiceLine.cs` — + `DentalActCodeId`/`CodeActe`
- `Domain/Entities/Invoice.cs` — `SetLines` overload carries the act code
- `Domain/Repositories/IInvoiceRepository.cs` + `Infrastructure/Repositories/InvoiceRepository.cs` — `GetOutstandingByPatientAsync`, `GetByPaymentIdAsync`
- `Domain/Repositories/ITreatmentPlanRepository.cs` + `Infrastructure/Repositories/TreatmentPlanRepository.cs` — `GetInstallmentCollectedBetweenAsync`, `GetInstallmentOutstandingByPatientAsync`
- `Infrastructure/Persistence/Configurations/InvoiceLineConfiguration.cs` — configure new columns
- `Infrastructure/Migrations/20260722184000_AddInvoiceLineCnamActCode.*` + snapshot (tool-generated)
- `Application/Common/Interfaces/ICnamBillingCalculator.cs` + `Common/Services/CnamBillingCalculator.cs` (new) + `Extensions.cs` registration
- `Application/Common/PaymentMethodLabels.cs` (new)
- `Application/Common/Models/{InvoicePdfData,DevisPdfData}.cs` — + paid/outstanding + CNAM fields; `ReceiptPdfData.cs` (new); `IPdfGenerationService.cs` — + `GenerateReceiptPdfAsync`
- `Infrastructure/Services/PdfGenerationService.cs` — invoice + devis totals rows; new receipt renderer
- `Application/Features/Invoices/Queries/GetInvoicePdfQuery.cs` + `TreatmentPlans/Queries/GetDevisPdfQuery.cs` — map paid/outstanding + CNAM split
- `Application/Features/Invoices/{InvoiceMappingExtensions,Commands/CreateInvoiceCommand,Commands/UpdateInvoiceCommand}.cs` + `DTOs/InvoiceDto.cs` — line act-code plumbing
- `Application/DTOs/{PatientBillingSummaryDto,ReceivableDto,DashboardStatsDto}.cs`
- `Application/Features/Billing/Queries/{GetPatientBillingSummaryQuery,GetReceivablesQuery}.cs` (new)
- `Application/Features/Invoices/Queries/GetPaymentReceiptPdfQuery.cs` + `TreatmentPlans/Queries/GetInstallmentReceiptPdfQuery.cs` (new)
- `Application/Features/Dashboard/Queries/GetDashboardStatsQuery.cs` — installment revenue + total outstanding
- `API/Controllers/BillingController.cs` (new) + `TreatmentPlansController.cs` (installment receipt endpoint)
- `UnitTests/Features/Dashboard/GetDashboardStatsQueryHandlerTests.cs` — build-required ctor fix (see Auto-Approved)

### Frontend
- `web/lib/api/types.ts` — DashboardStats.totalOutstanding, InvoiceLineDto CNAM fields, PatientBillingSummaryDto, ReceivableDto
- `web/lib/api/billing.ts` (new); `treatment-plans.ts` (installment receipt); `invoices.ts` (line CNAM fields)
- `web/lib/download.ts` (new) — shared blob download helper
- `web/app/page.tsx` — "Créances" (total outstanding) card
- `web/app/creances/page.tsx` + `web/components/creances/receivables-table.tsx` (new); `dashboard-sidebar.tsx` — Créances nav link
- `web/app/patients/[id]/page.tsx` — Solde patient card + invoiced-record marking + modal `isInvoiced`
- `web/components/patient-record-modal.tsx` — `isInvoiced` locks the amount-paid input
- `web/components/factures/{payment-modal,invoice-form-modal}.tsx` — receipt download + per-line CNAM act picker
- `web/components/treatment-plans/{installment-payment-modal,treatment-plans-table}.tsx` — receipt download + "En retard" badge

## Deferred to /test-small-feature
New scenarios the change enables (not written here): `CnamBillingCalculator` capping/estimate-unavailable/free-text edge cases; billing-summary aggregation + oldest-overdue; receivables merge/sort/aging + `daysOverdue`; dashboard now includes installment revenue + total outstanding (the two new assertions on the adapted dashboard test); receipt endpoints' 404 (`NotFoundException`) vs 400 mapping and unpaid-installment guard; invoice/devis PDF CNAM + paid/outstanding fields; `InvoiceLine` act-code persistence + DTO mapping round-trip.

## Working tree note (start of session)
- Worktree created off HEAD `a512b80` of `feature/windows-desktop-app`. Tracked tree was clean at creation.
- Untracked feature-doc folders in the main worktree (`features/clinical-loop-integration/`, `features/reliability-and-polish/`) are NOT part of this feature and are excluded. Only `features/unified-billing-ledger/` was copied into this worktree.

## Files Changed
(tracked during implementation)

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Added `ITreatmentPlanRepository` mock to `GetDashboardStatsQueryHandlerTests` (ctor gained the dep) | Build-required compile fix; permissive mock returns empty/0 so the test's original appointment-count assertions still hold. New revenue/outstanding assertions deferred to /test-small-feature. |
| New `PaymentMethodLabels.ToFrench` helper (French labels for `PaymentMethod`) | Internal display constants for receipts/PDFs; no contract change. |
| Shared `web/lib/download.ts` blob helper | Internal FE refactor (extract the repeated anchor-click download). |
| CNAM split block on invoice/devis PDFs rendered only when `CnamReimbursable > 0` | Display decision internal to PDF rendering; when nothing is reimbursable the out-of-pocket trivially equals the total, so omitting the block loses no info and keeps private-clinic documents clean. The computation still always sums to the document total. |
| Per-document CNAM split placed on the PDFs + patient billing-summary, NOT added to the list DTOs (`InvoiceDto`/`TreatmentPlanDto`) | The pinned API contract puts the CNAM fields on the PDFs + billing-summary only; adding them to list DTOs would widen the contract and force a full-catalog load per listed document. AC-9 ("the devis and invoice each display") is satisfied on the documents themselves + the patient summary. |
| Receipt endpoints throw `NotFoundException` (→404) for not-found while keeping `Result.Failure` (→400) for render errors | The spec pins 404 for the receipt endpoints; the middleware already maps `NotFoundException`→404. Sibling invoice/devis PDF endpoints use plain 400 for not-found, but the receipt contract explicitly wants 404. |

## Significant Deviations
### DEV-1: Forced small pipeline on an oversized spec
- **Original:** Spec is `Type: Small` but declares `Scope: Full`, 10 ACs, 3+ new endpoints, dashboard + 2 PDF changes, receivables view, CNAM split — beyond the ~10-file small-feature envelope.
- **Decision:** User explicitly chose "Force small pipeline anyway" (2026-07-22). Spec flipped DRAFT→APPROVED with user confirmation. Per the forced-small exception, after exploration the real file count + a scope boundary was surfaced to the user before coding.
- **Approved:** Y

### DEV-2: Scope boundary = "Everything incl. invoice CNAM" (~40+ files)
- **Decision (2026-07-22):** After exploration surfaced the real surface (~30–40 files) and two spec gaps, user chose the maximal boundary: all ACs including the invoice CNAM split.
- **Impact:** All of AC-1..AC-10 implemented in one pass, including a schema change to `InvoiceLine` (see DEV-3).
- **Approved:** Y

### DEV-3: InvoiceLine gains a CNAM act-code link + EF migration (contradicts spec "no migration")
- **Original spec:** "No new persisted money entity … no columnar migration." But `InvoiceLine` carries NO CNAM code (only `Designation/Quantity/UnitPriceHt/LineTotalHt`), so AC-9's invoice CNAM split is not computable without one.
- **Actual:** Add `DentalActCodeId` (Guid?) + `CodeActe` (string?) to `InvoiceLine`, mirroring `TreatmentPlanItem`, so the reimbursable split can be computed uniformly for invoices and devis. Adds an EF migration (additive columns).
- **`dotnet ef` was NOT blocked here** (WDAC did not fire for `dotnet ef` 10.0.3) → migration `20260722184000_AddInvoiceLineCnamActCode` was **tool-generated** (not hand-authored) and the model snapshot updated by the tool. No regen-before-merge flag needed. (Note: `dotnet test` remains WDAC-blocked per [[smart-app-control-blocks-tests]] — that only affects running tests, not building or EF.)
- **Justification:** Explicitly chosen by the user (DEV-2); the only way to satisfy AC-9 for invoices.
- **Approved:** Y

### DEV-4: "Installment payments collected this month" is a LastPaidOn approximation
- **Original spec (AC-3):** dashboard "Encaissé ce mois-ci" includes installment payments collected in the current month.
- **Constraint:** Installments store only `LastMethod`/`LastPaidOn` + cumulative `AmountPaid` (no per-payment history — explicitly out of scope in the spec). So "collected in-month" is computed as: installments whose `LastPaidOn` falls in [monthStart, monthEnd], counting their full `AmountPaid`. An installment paid partially across two months is attributed only to its last-payment month.
- **Same caveat:** receipt "running balance" and installment receipts reflect the installment's latest state (no per-transaction id).
- **Impact:** Behavior differs subtly from a true per-payment ledger; acceptable given the out-of-scope note. Disclosed here.
- **Approved:** Y (implied by DEV-2 scope choice; the only option without a per-installment payment-history entity, which the spec puts out of scope)
