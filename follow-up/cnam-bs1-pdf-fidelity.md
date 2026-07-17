# Validate & correct the BS1 PDF against the official form

> **Type:** incomplete
> **Priority:** high
> **Created:** 2026-07-17
> **Feature:** cnam-bulletin-soins

## Summary
The generated 2-page BS1 PDF was corrected against the official CNAM form (downloaded to `C:\Users\Oumayma Benkhalifa\Downloads\BS1.pdf`; source `cnam.nat.tn/doc/upload/BS1.pdf`), but it has **not been visually rendered/verified**. The composer uses QuestPDF's `Table` primitive, which is new to this file. Render one sample bulletin and eyeball it against the official form before merge; fix any layout drift.

## Current State
`PdfGenerationService.GenerateBulletinCnamPdf` now renders: official section headings, ☒/☐ checkbox rows (régime / malade lien / care type), 7-column dental tables (Date · Dent · Code acte · Cotation · Honoraires · Code Prof. santé · Cachet et signature), a permanent+temporary FDI tooth chart with D/G markers, official footnotes, and the "déposer sous 60 jours" notice. Compiles 0 errors/0 warnings; **not yet rendered.**

## Key Files
| File | Purpose |
|------|---------|
| `api/ClinicManagement.Infrastructure/Services/PdfGenerationService.cs` | `GenerateBulletinCnamPdf` + `BulletinActsTable`/`BulletinToothDiagram`/helpers |
| `C:\Users\Oumayma Benkhalifa\Downloads\BS1.pdf` | Official reference (move a copy to `features/cnam-bulletin-soins/reference/BS1.pdf`) |

## Why Deferred
PDF rendering in this repo is operator-verified (needs the running app + a real request), and QuestPDF layout exceptions only surface at generation time.

## Suggested Approach
1. Copy the official form into `features/cnam-bulletin-soins/reference/BS1.pdf` for future diffs.
2. Run the app, create a `bulletin-cnam` doc with sample data, download the PDF.
3. Compare side-by-side: box positions, table column widths, tooth-chart geometry, bilingual labels, footnotes.
4. Fix drift; watch for QuestPDF `Table`/layout runtime exceptions.

## Open questions to resolve during this work
- The official BS1 has **no "Matricule Fiscal" slot** (it uses per-row "Code Professionnel de santé"). We currently print MatriculeFiscal as an extra cabinet field — decide whether to keep or drop it.
- Whether ☒/☐ glyphs render correctly in the bundled Helvetica (fallback: `[X]`/`[ ]`).

## Acceptance Criteria
- [ ] A rendered sample BS1 matches the official form's structure/labels closely enough to be CNAM-acceptable.
- [ ] No QuestPDF runtime exception on generation (incl. empty acts + many acts).
- [ ] Matricule-fiscal decision made and reflected.
