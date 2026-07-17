# Complete bilingual FR/AR labels (CNAM UI + PDF)

> **Type:** enhancement
> **Priority:** low
> **Created:** 2026-07-17
> **Feature:** cnam-bulletin-soins

## Summary
The official BS1 form is fully bilingual (French + Arabic). Our BS1 PDF has Arabic only on the header and a few section titles, and the CNAM editor UI is French-only. Complete the FR/AR labelling to match the official form.

## Current State
- PDF (`GenerateBulletinCnamPdf`): bilingual header + some section titles; most field labels FR-only.
- Editor / patient dialog / clinic settings CNAM fields: FR-only labels.

## Expected State
- BS1 PDF field labels bilingual where the official form is (Assuré, Malade, Adresse, Code postal, lien options, care types, table headers, footnotes).
- Consider whether the app UI labels need Arabic too (likely PDF-only is enough — confirm with the user).

## Key Files
| File | Purpose |
|------|---------|
| `api/.../Services/PdfGenerationService.cs` | `GenerateBulletinCnamPdf` labels |
| `C:\Users\Oumayma Benkhalifa\Downloads\BS1.pdf` | source of the exact AR wording |

## Why Deferred
Cosmetic/compliance polish; the FR labels are sufficient to produce a usable bulletin. Best done together with the PDF-fidelity pass (`cnam-bs1-pdf-fidelity.md`).

## Suggested Approach
1. Extract the exact AR strings from the official form.
2. Add AR alongside FR on each PDF label (RTL rendering — verify QuestPDF handles the Arabic glyphs/shaping).

## Acceptance Criteria
- [ ] BS1 PDF labels are bilingual matching the official form.
- [ ] Arabic renders correctly (shaping/RTL) in the generated PDF.
