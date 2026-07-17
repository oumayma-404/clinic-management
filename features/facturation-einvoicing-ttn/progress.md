# Progress: Facturation électronique TTN / TEIF (« El Fatoora »)

**Started:** 2026-07-17
**Type:** Small (forced via /next — spec is authored full-pipeline; user chose small pipeline + full vertical slice)
**Branch:** feature/facturation-einvoicing-ttn (off feature/windows-desktop-app)

## Status
- [x] Implementation
- [x] Quality checks (build, lint, typecheck)
- [ ] Tests (handled by /test-small-feature)

## Quality check results
- **Backend `dotnet build ClinicManagement.sln`:** 0 compile errors; 0 new warnings in changed files
  (only pre-existing solution-wide CS8632/CS8618/CS8981 + a copy-lock MSB3026 because the API is running —
  not a compile failure). Infrastructure (incl. the hand-authored migration) also built clean to a scratch dir.
- **Frontend `npx tsc --noEmit`:** exit 0 (clean) — the definitive type gate.
- **Frontend ESLint:** not runnable in this repo (`eslint` package not installed — config+script exist but
  `next build` runs with lint disabled; documented in web CLAUDE.md). `next build` NOT run — a `next dev`
  server is live on :3000 and owns `.next`; building would clobber the running app.
- **Migration:** hand-authored (`20260717180000_AddEInvoicing`) because `dotnet ef` is blocked here
  (WDAC + running-app DLL lock). Must be verified/regenerated with the EF tool in an unrestricted env before merge.

## Working tree note (start of session)
Unrelated uncommitted changes present at start (excluded from this feature's staging):
- api/ClinicManagement.API/Controllers/PatientsController.cs
- api/ClinicManagement.Application/DTOs/DentalRecordDto.cs
- api/ClinicManagement.Application/Features/Patients/Commands/CreateDentalRecordCommand.cs
- api/ClinicManagement.Application/Features/Patients/Commands/UpdateDentalRecordCommand.cs
- api/ClinicManagement.Application/Features/Patients/Queries/GetDentalRecordsQuery.cs

## Design decisions (scope of this pass)
- **Full vertical slice** chosen by the user (backend + QR-on-PDF + frontend).
- **Outbox folded onto the Invoice aggregate** (EInvoiceStatus + AttemptCount + NextAttemptAt + LastError)
  rather than a separate outbox table — FR-5 already puts this state "on the invoice"; a Hangfire job
  auto-dispatches `Queued` invoices when the server has internet. See DEV-2.
- **TTN client abstracted** behind `ITtnClient`; a fully-working `SandboxTtnClient` (default) + a best-effort
  `HttpTtnClient` (REST+OAuth2, contract UNVERIFIED against live TTN — OQ #1) selected by config.
- Legal artifacts (signed XML, TTN receipt) stored via the existing `IFileStorage` seam (blob), keys on the
  invoice — mirrors how logos/files are stored (OQ #6 resolved to file storage).
- Signing certificate + TTN secrets read from the per-install `.local/` store (cert/token precedent).

## Known external gaps (spec Open Questions — NOT closeable in-repo)
- OQ #2: exact TEIF XSD not available → generated TEIF follows the documented TEIF structure best-effort;
  XSD validation cannot be run here.
- OQ #1/#3: real TTN transport/endpoints + exact XAdES profile unverified → sandbox is the exercisable path.

## Files Changed
**Domain:** `Enums/EInvoiceStatus.cs` (new), `Entities/Invoice.cs` (e-invoice state + lifecycle methods),
`Entities/Clinic.cs` (TTN settings + `SetElFatooraSettings`), `Repositories/IInvoiceRepository.cs` (outbox query).
**Application:** `Common/Models/EInvoiceModels.cs` (new), `Common/Interfaces/{ITeifXmlGenerator,IEInvoiceSigner,IQrCodeGenerator,ITtnClient,IEInvoiceService}.cs` (new),
`Features/Invoices/Commands/SubmitInvoiceToElFatooraCommand.cs` (new), `Features/Invoices/Queries/GetEInvoiceArtifactQuery.cs` (new),
`DTOs/InvoiceDto.cs` + `DTOs/ClinicDto.cs` + `Features/Invoices/InvoiceMappingExtensions.cs` (e-invoice fields),
`Features/Invoices/Queries/GetInvoicePdfQuery.cs` (QR), `Common/Models/InvoicePdfData.cs` (QR fields),
`Features/Clinics/Commands/UpdateClinicCommand.cs` + `Features/Clinics/Queries/GetUserStatusQuery.cs` (TTN settings).
**Infrastructure:** `Services/{TtnConfig,TeifXmlGenerator,XadesEInvoiceSigner,QrCodeGenerator,SandboxTtnClient,HttpTtnClient,EInvoiceService}.cs` (new),
`Services/PdfGenerationService.cs` (QR render), `Persistence/Configurations/{InvoiceConfiguration,ClinicConfiguration}.cs`,
`Repositories/InvoiceRepository.cs` (outbox query), `Extensions.cs` (DI), `ClinicManagement.Infrastructure.csproj` (QRCoder + System.Security.Cryptography.Xml),
`Migrations/20260717180000_AddEInvoicing.cs` + `.Designer.cs` + `ApplicationDbContextModelSnapshot.cs` (hand-authored).
**API:** `Controllers/InvoicesController.cs` (submit + download endpoints), `Controllers/ClinicsController.cs` + `Models/UpdateClinicRequest.cs` (TTN settings),
`BackgroundJobs/EInvoiceOutboxJob.cs` (new), `Program.cs` (recurring job).
**Frontend:** `lib/api/types.ts` + `lib/api/invoices.ts` + `lib/api/clinics.ts`, `components/factures/invoice-labels.ts`,
`components/factures/invoices-table.tsx` (status column + actions + connectivity gating), `components/clinic-settings.tsx` (TTN settings).

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|

## Significant Deviations
- **DEV-1 (new dependencies):** added NuGet `QRCoder` (QR cachet PNG) + `System.Security.Cryptography.Xml`
  (XAdES/XMLDSig signing) to Infrastructure. Canonical minimal libraries required to deliver the
  QR + electronic-signature the spec mandates and the user approved (full slice). Approved: implicit via
  full-slice choice.
- **DEV-2 (outbox on aggregate):** offline outbox modeled as invoice state + a Hangfire dispatch job rather
  than a dedicated outbox entity/table. Justified by FR-5 (state lives "on the invoice") and small-pass
  scope. Approved: consistent with spec Data section.
