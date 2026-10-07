# QA run 3 — the cabinet's own letterhead (+ date of birth off the documents)

**Date:** 2026-10-06 · **Plan:** `plan.md` (A9 row corrected after run 2) · **Driver:** `walk.mjs`, the same file, whole plan
**Environment:** API :5000 (PID 10836) and `next dev` :3000 (PID 47636), both owned by this session (`clinic-management-cd`),
unchanged since run 2; the cycle-2 edit is web-only and hot-reloaded. Own Chrome (playwright-core), not the MCP profile.
**Widths looked at:** 1440 × 900 (dialog + card), 320 × 640 (C1), 1536 × 730 (C2). PDFs rasterised at 60 dpi; `A2.png` and
`paper-ordonnance-p1.png` read.
**Artefacts reverted:** the letterhead (B6 withdrew it — keys null) · the C3 test ordonnances (deleted).

## Scenarios

| ID | Result | Evidence |
|----|--------|----------|
| A1 | ✅ | badge « En-tête texte » + « Importer mon papier à en-tête » |
| A2 | ✅ | « En-tête 49 mm · Pied de page 26 mm », switch on |
| A2b | ✅ | the header line (49 mm) falls below the rule at 44.5 mm — `shots/A2.png`: « Fin de l'en-tête » sits under the teal rule |
| A2c | ✅ | 1440 × 900: page ends 758, buttons at 772 |
| A3 | ✅ | aperçu 200 `application/pdf`, frame shown; preview PDF carries both bands |
| A4 | ✅ | saved; badge « Papier du cabinet »; both bands shown at 2480 px |
| A5 | ✅ | `clinics/f64a8a75…/letterhead/262072bc…/header` + `…/footer` |
| A6 | ✅ | ordonnance, note, devis, reçu (note), reçu (devis), avoir — band at the top and bottom of page 1; the printed ordonnance now carries the entête's rule (`shots/paper-ordonnance-p1.png`) |
| A7 | ✅ | long ordonnance: 4 pages, both bands on every page |
| A8 | ✅ | the editor's preview starts with the band, no text header |
| A9 | ⏭ | not exercised — « Télécharger Word » is withdrawn app-wide by the owner (`WORD_EXPORT_OFFERED = false`, 85dfd995); the letterhead docx code compiles and is unreachable |
| B1 | ✅ | phone photo refused « Image trop petite (900 px de large)… » |
| B2 | ✅ | header at 170 mm refused « L'en-tête occupe plus d'un tiers de la page… » |
| B3 | ✅ | `notes.txt` refused « Ce fichier n'est ni un PDF ni une image PNG ou JPEG… » |
| B4 | ✅ | keyboard: 49 → 52 mm |
| B5 | ✅ | footer off: saved without footer; PDF has the header band only |
| B6 | ✅ | « Retirer le papier à en-tête du cabinet ? » → badge « En-tête texte », keys null |
| B7 | ✅ | doctor: no controls, « Modifiable par un administrateur. » |
| B8 | ✅ | forged letterhead key in the body ignored |
| C1 / C1b / C1c | ✅ | 320 px: no horizontal scroll, steps 20 px each, « en-tête » whole, page fits above the buttons |
| C2 / C2b | ✅ | 1536 × 730: buttons inside (639 / 650), page ends 587 above buttons at 603 |
| C3 | ✅ | a document keeps the paper it was issued on and still renders it |
| C4 | ✅ | 409 offers « Recharger », then the save goes through |
| D1 | ✅ | 7 PDFs without a letterhead: no band anywhere |
| D2 | ✅ | saved ordonnance re-renders; one issued before the letterhead keeps its text header |
| D3 | ⏭ | not exercised — the QA clinic's name is blank, so « Informations du cabinet » refuses to save for an unrelated reason |
| D4 | ✅ | no PDF contains « naissance »; the editor's preview has no « Date de naissance » |

## Findings

None.

## Observations (off-plan)
- The QA clinic's name is blank in the database, so text-header documents print « [Nom du cabinet] ». Data, not this change.

**Status: GREEN (33 ✅, 2 ⏭ with reasons, 0 ❌)**
