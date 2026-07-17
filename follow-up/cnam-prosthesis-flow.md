# Prosthesis (Prothèses dentaires) flow on the bulletin

> **Type:** enhancement
> **Priority:** medium
> **Created:** 2026-07-17
> **Feature:** cnam-bulletin-soins

## Summary
The official BS1 has a dedicated "PROTHESES DENTAIRES" table separate from "CONSULTATIONS ET ACTES DE SOINS DENTAIRES". Today the editor builds a single acts list and the PDF renders the prothèses table **empty**. Prostheses have been CNAM-reimbursed since 2019 (outside the care ceiling, no prior approval), so they deserve first-class handling.

## Current State
- `document-editor-content.tsx`: one `bulletinFields.acts` list; no category/table distinction.
- `PdfGenerationService.GenerateBulletinCnamPdf`: renders the soins table from acts, and a hard-coded empty Prothèses table (`BulletinActsTable(c, new List<ActData>(), doctorCode)`).

## Expected State
- Each bulletin act carries a category (or an explicit "table" flag: soins vs prothèse).
- The editor lets the user place an act in either table; the PDF routes acts to the correct table.
- Pairs naturally with the nomenclature "Prothèse" category from `cnam-nomenclature-lookup`.

## Key Files
| File | Purpose |
|------|---------|
| `web/components/document-editor-content.tsx` | `bulletinFields.acts`, `buildBulletinContent`, editor UI |
| `api/.../Services/PdfGenerationService.cs` | `GenerateBulletinCnamPdf` (soins vs prothèses tables) |

## Why Deferred
The first slice proved the end-to-end bulletin flow with a single acts table; splitting tables + carrying a category is a clean follow-on.

## Suggested Approach
1. Add `category` (or `table`) to the bulletin act row; keep ContentJson backward-compatible (default = soins).
2. Editor: a per-act toggle or auto-route by the nomenclature category (Prothèse → prothèses table).
3. PDF: partition acts by table before rendering.

## Acceptance Criteria
- [ ] Acts can be assigned to the soins or prothèses table; the PDF renders each in the right table.
- [ ] Old saved bulletins (no category) still render (all acts → soins table).
- [ ] Nomenclature "Prothèse" acts default into the prothèses table.
