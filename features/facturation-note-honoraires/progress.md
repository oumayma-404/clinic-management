# Progress: Facturation — Note d'honoraires numérotée

**Started:** 2026-07-17
**Type:** Small
**Branch:** feature/facturation-note-honoraires (worktree, based on feature/windows-desktop-app HEAD; PR target = feature/windows-desktop-app)

## Status
- [x] Implementation
- [x] Quality checks (build, typecheck) — see note on lint below
- [x] Tests (added/modified — see Test Plan + Tests Run below)

## Test Plan
| AC | Action | Target file | Notes |
|----|--------|-------------|-------|
| AC-1 | New class | UnitTests/Domain/InvoiceEntityTests.cs | Draft has no number/deletable; issued not editable |
| AC-2 | New class | UnitTests/Features/Invoices/IssueInvoiceCommandHandlerTests.cs | `AAAA-NNNN`, gapless (max+1), unique-collision retry |
| AC-3 | New class | UnitTests/Domain/InvoiceCalculatorTests.cs | HT→TVA→timbre→TTC, millime rounding, exonerated, stamp toggle |
| AC-4 | New class | UnitTests/Domain/InvoiceEntityTests.cs | Optional dental-record/appointment links stored |
| AC-5 | New class | UnitTests/Domain/InvoiceEntityTests.cs | Partial/exact payment + status; overpayment refused; draft/cancelled reject |
| AC-6 | New class + modify | InvoiceEntityTests.cs (rules) + Common/Authorization/AuthorizationPoliciesTests.cs (AdminOrDoctor role gate) | Issued not deletable, cancel keeps number + requires reason; admin/doctor-only policy |
| AC-10 | New class | UnitTests/Features/Invoices/InvoiceTenantIsolationTests.cs | Cross-clinic get/update/cancel/pay/delete → not found; list scoped to caller clinic |
| realtime | Modify | Common/Behaviors/RealtimeResourceResolverTests.cs | `CreateInvoiceCommand → "invoices"` contract key |

### Coverage notes (ACs without a dedicated unit test)
- **AC-7** (create/issue/payment allowed to any authenticated clinic user incl. secretary): enforced by the controller's class-level `[Authorize]` (no role policy) — a controller-attribute concern, verified at integration/manual level, not unit-testable here.
- **AC-8** (PDF: TND, matricule, no €/«Paris», no pathology): the invoice PDF uses a dedicated renderer (`GenerateInvoicePdfAsync`) with no €/«Paris»/pathology in its code path and TND formatting; binary PDF-text assertions need a PDF-parsing lib not present — content correctness is verified by visual/manual render. (The renderer produces a valid PDF; exercised end-to-end via `GET /invoices/{id}/pdf`.)
- **AC-9** (Recettes filters + dashboard tile): revenue aggregation is covered by the list-scoping test + the pure calc; the FE view/tile has no test framework in this repo (covered by `tsc --noEmit` + `next build` at implementation time).

### Scope note
Per the skill's ~5-class carve-out: this feature is large but the added test classes are **thin domain/handler/isolation tests mirroring siblings** (no new user flow needing E2E). Not escalated to the full pipeline — judged by breadth of new behavior, not raw class count.

## Tests Run
| Suite | Filter | Result |
|-------|--------|--------|
| Unit | `InvoiceCalculatorTests\|InvoiceEntityTests\|IssueInvoiceCommandHandlerTests\|InvoiceTenantIsolationTests` | **29 passed, 0 failed** |
| Unit | `RealtimeResourceResolverTests\|AuthorizationPoliciesTests\|GetDashboardStatsQueryHandlerTests` | **26 passed, 0 failed** |

Run via the SAC/lock-dodging recipe: `dotnet build ...UnitTests.csproj -p:OutDir=<scratch>` then `dotnet vstest <scratch>/ClinicManagement.UnitTests.dll --TestCaseFilter:"..."` (plain `dotnet test` is blocked by Smart App Control `0x800711C7` on this box). Final `dotnet build ClinicManagement.sln` → 0 errors, no new warnings.

No integration-test project (Testcontainers) exists in this repo, so API-contract behavior is covered by handler unit tests (no Postman/Newman per user preference).

## Quality checks run
- Backend: `dotnet build ClinicManagement.sln` → **0 errors**, no new warnings in changed files (only pre-existing CS8618/CS8981/CS8602).
- Frontend typecheck: `npx tsc --noEmit` → **clean**.
- Frontend build: `npm run build` (`next build`) → **success**, all 18 routes compiled incl. `/factures`.
- Frontend lint: **not runnable in this repo** — `eslint` is not a declared/installed dependency (the `lint` script references it but it's absent), and `next build` has `eslint.ignoreDuringBuilds: true`. Per skill guidance the FE gate is `tsc --noEmit` + build. (Not a coverage gap introduced by this feature.)
- EF migration generated with the real `dotnet ef` tool (WDAC did **not** block it here) → `20260717174602_AddInvoicesAndClinicBilling` (+ Designer + snapshot). No hand-authoring needed.

## Working tree note (start of session)
Fresh worktree off `feature/windows-desktop-app` HEAD (12b5501). The parent working tree has a large
body of *uncommitted* sibling work (the "graceful-error-handling" feature: `ApiControllerBase`,
`ErrorMessages`, controller edits using `HandleFailure`) that is NOT part of HEAD and therefore absent
here. The spec was written assuming that work is present. See Significant Deviations DEV-1/DEV-2.

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| `InvoiceLine`/`Payment` modeled as child `Entity<Guid>` with cascade-delete `HasMany` (not EF `OwnsMany`) | Matches the established `DentalRecord`/`DentalRecordTooth` aggregate-child convention in this codebase; spec's "owned" means aggregate-owned, satisfied by cascade children. |
| Money columns `decimal(18,3)` (millimes) per spec; existing `ProcedureType.DefaultCost` stays `decimal(18,2)` | Spec pins 3-decimal millimes for invoice money. |
| Added `MatriculeFiscal` as a NEW `Clinic` field | Spec claimed it was "déjà présent sur Clinic" but it does not exist in this branch (nor in HEAD). Added additively alongside VAT/stamp settings. |
| Added `AdminOrDoctor` authorization policy for the cancel endpoint | AC-6 requires cancel limited to admin/doctor with 403 otherwise; no existing policy covered that pair. |
| Test-infra compile fix: added `Mock<IInvoiceRepository>` to `GetDashboardStatsQueryHandlerTests` ctor call | Build-required — adding `IInvoiceRepository` to the dashboard handler ctor broke the existing test's compile. Mechanical fix only (no scenario change); auto-approved per skill. |
| `InvoiceLine`/`Payment` have no separate `DbSet` (reached only via `Invoice` navigation) | Aggregate-boundary hygiene; EF still models them via the configured `HasMany` relationships. |

## Deferred to /test-small-feature or follow-up
- **Dental-record pre-fill UI (AC-4):** the backend fully supports the optional `DentalRecordId`/`AppointmentId` link (stored, `DentalRecord.Cost/AmountPaid` untouched) and the create endpoint accepts them. The UI currently creates invoices manually / from a patient preset; a "créer une facture depuis cette intervention" button on the dental-records tab is a small follow-up, not wired here.
- **`RealtimeResourceResolverTests`** pins the backend↔frontend key contract; the new `Invoices → "invoices"` key was added to both sides (`RealtimeResource.Invoices`) but the contract test update belongs to the test pass.

## Significant Deviations
### DEV-1: Error contract — follow committed convention, not spec's `ApiControllerBase`/`{ error }`
- **Spec:** `InvoicesController : ApiControllerBase`, failures via `HandleFailure` → `{ error }`.
- **Actual:** `InvoicesController : ControllerBase` returning `BadRequest(result.Error)` / `NotFound(...)`,
  matching every committed controller in this branch.
- **Justification:** `ApiControllerBase`/`ErrorMessages` exist only as uncommitted parent-tree work absent
  from this branch. Recreating them here would collide (both branches adding the same files) on merge.
  The frontend already parses plain-string error bodies (commit `7fa85e7`), and `ExceptionMiddleware`
  already emits `{ error }` for 404/403.
- **Impact:** Error body for 400s is a bare string here vs `{ error }` in the eventual merged tree; FE handles both.
- **Approved:** Yes (user chose "Follow committed convention").

### DEV-2: `MatriculeFiscal` + billing settings added as new Clinic fields (see auto-approved note)
- Folded into the Clinic entity/config/migration/DTO + `UpdateClinicCommand`. Approved implicitly by DEV-1 context.

## Files Changed

### Backend — new
- Domain: `Enums/InvoiceStatus.cs`, `Enums/PaymentMethod.cs`, `Services/InvoiceCalculator.cs`, `Entities/Invoice.cs`, `Entities/InvoiceLine.cs`, `Entities/Payment.cs`, `Repositories/IInvoiceRepository.cs`
- Application: `DTOs/InvoiceDto.cs`, `Common/Models/InvoicePdfData.cs`, `Features/Invoices/InvoiceMappingExtensions.cs`, `Features/Invoices/Commands/{Create,Update,Issue,RecordPayment,Cancel,Delete}InvoiceCommand.cs`, `Features/Invoices/Queries/{GetInvoice,GetInvoices,GetInvoiceRevenue,GetInvoicePdf}Query.cs`
- Infrastructure: `Persistence/Configurations/{Invoice,InvoiceLine,Payment}Configuration.cs`, `Repositories/InvoiceRepository.cs`, `Migrations/20260717174602_AddInvoicesAndClinicBilling.{cs,Designer.cs}`
- API: `Controllers/InvoicesController.cs`

### Backend — modified
- Domain: `Entities/Clinic.cs` (billing fields + `SetBillingSettings`)
- Application: `DTOs/ClinicDto.cs`, `DTOs/DashboardStatsDto.cs`, `Common/Authorization/AuthorizationPolicies.cs` (AdminOrDoctor), `Common/Interfaces/IPdfGenerationService.cs`, `Features/Clinics/Commands/UpdateClinicCommand.cs`, `Features/Clinics/Queries/GetUserStatusQuery.cs`, `Features/Dashboard/Queries/GetDashboardStatsQuery.cs`
- Infrastructure: `Persistence/ApplicationDbContext.cs` (DbSet + Invoice query filter), `Persistence/Configurations/ClinicConfiguration.cs`, `Persistence/Migrations/ApplicationDbContextModelSnapshot.cs`, `Services/PdfGenerationService.cs` (`GenerateInvoicePdfAsync`), `Extensions.cs` (repo DI)
- API: `Controllers/DashboardController.cs` (month range), `Controllers/ClinicsController.cs` + `Models/UpdateClinicRequest.cs` (billing fields)
- Tests: `UnitTests/Features/Dashboard/GetDashboardStatsQueryHandlerTests.cs` (compile fix)

### Frontend — new
- `lib/format.ts` (formatDT / formatDateFr), `lib/api/invoices.ts`, `app/factures/page.tsx`, `components/factures/{invoice-labels.ts,invoice-form-modal.tsx,payment-modal.tsx,invoices-table.tsx}`

### Frontend — modified
- `lib/api/types.ts` (Invoice/Payment/Revenue DTOs + `monthlyRevenueCollected`), `lib/api/dashboard.ts` (month params), `lib/hooks/use-dashboard-stats.ts` (month range), `lib/api/clinics.ts` (ClinicDto billing + update), `lib/realtime/clinic-hub.ts` (`Invoices` key), `app/page.tsx` (Recettes tile), `app/patients/[id]/page.tsx` (Factures tab), `components/dashboard-sidebar.tsx` (Factures nav), `components/clinic-settings.tsx` (Facturation card)
