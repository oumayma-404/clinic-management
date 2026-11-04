# Story 5: BE - Documents open, print, and say « SPÉCIMEN »

**Status:** APPROVED
**Story Status:** reviewed (2026-11-03, 100/100)
**Layer:** BE
**Depends On:** 4
**Blocks:** 7

## Objective

Every PDF the demo produces — ordonnance, certificat, note d'honoraires, devis, reçu, avoir, aperçu — opens,
downloads and prints on the cabinet's letterhead with a light diagonal « SPÉCIMEN — DÉMONSTRATION » and a footer
line « Document de démonstration, sans valeur »; PDF generation is limited per session. Production PDFs unchanged.

## Acceptance Criteria

_From spec:_

- [x] AC-5.1 preview + Télécharger + Imprimer produce the real PDF with letterhead and cachet
- [x] AC-5.2 ordonnance aperçu in a fiche works; AC-5.3 files open full size; AC-5.4 « Modifier » opens, save refused
- [x] AC-5.5 mark + footer on every demo PDF; AC-5.6 per-session limit

_Story-specific:_

- [~] One choke point — `IPdfGenerationService`, wrapped on the demo; `DocumentPage` misses the P 061 / BS1 overlays (DEV-5)
- [x] No mark on any other deployment kind (test)

## Entry Criteria

- [ ] Story 4 implemented (documents exist to look at)

## Steps

1. **Watermark seam** — `IDocumentWatermark` (on when `RefusesWrites`), optional parameter on
   `PdfGenerationService` (keeps the 3 `new PdfGenerationService(...)` tests compiling); `DocumentPage.Compose`
   draws `page.Foreground()` + footer line.
2. **Allow the two POST reads** — `[AllowedInDemo]` on `MedicalDocuments.GeneratePdfForDownload` and
   `PreviewFicheOrdonnance`; update `DemoExemptionCoverageTests`.
3. **Rate limit** — `RateLimiting.DemoPdfPolicy` keyed on `family_id`, no limiter outside the demo; on the 9 PDF
   actions (`Billing` receipt, `Clinics` letterhead preview, `Invoices` pdf + avoir pdf, `MedicalDocuments` pdf +
   preview + download, `TreatmentPlans` devis + receipt).
4. **Tests** — render one document with the seam on/off and assert the text; existing render tests green.

## Files to Create/Modify

### Files to Create

| File | Purpose |
|------|---------|
| `api/ClinicManagement.Infrastructure/Services/DocumentWatermark.cs` | seam |

### Files to Modify

| File | Changes |
|------|---------|
| `DocumentPage.cs`, `PdfGenerationService.cs`, `Infrastructure/Extensions.cs` | mark |
| `MedicalDocumentsController.cs`, `InvoicesController.cs`, `BillingController.cs`, `TreatmentPlansController.cs`, `ClinicsController.cs` | attributes, policy |
| `RateLimiting.cs` | policy |
| render + coverage tests | assertions |

## Verification Steps

- [ ] API suite green, unfiltered
- [ ] Browser: each document type preview + download + print shows the mark; aperçu works; `HostedMultiTenant` boot → no mark
- [ ] 40 rapid PDF requests in one session → 429 after the limit; another session unaffected

## Exit Criteria

- [ ] All ACs checked; gates green; committed; README status → `implemented`
