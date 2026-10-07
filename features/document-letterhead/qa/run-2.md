# QA run 2 — the cabinet's own letterhead (+ date of birth off the documents)

**Date:** 2026-10-06 · **Plan:** `plan.md` · **Driver:** `walk.mjs` (same file as run 1, plus the rows the cycle-1 fixes
made reachable: A2b, A2c, C1b, C1c, C2b; A8 re-measured; A9 re-driven; C4 driven by a concurrent PUT)
**Environment:** API :5000 (PID 10836) and `next dev` :3000 (PID 47636, restarted after the cycle-1 edits), both owned by
this session (`clinic-management-cd`). No API change in cycle 1, so the API was not rebuilt. Own Chrome (playwright-core).
**Widths looked at:** 1440 × 900, 320 × 640, 1536 × 730. PDFs rasterised at 60 dpi.
**Artefacts reverted:** the letterhead (B6) · the C3 test ordonnances (deleted).

## Scenarios

| ID | Result | Evidence |
|----|--------|----------|
| A1 | ✅ | badge « En-tête texte » |
| A2 | ✅ | « En-tête 40 mm · Pied de page 23 mm », switch on |
| A2b | ❌ | header line suggested at **40 mm**, above the letterhead's rule at 44.5 mm — **F3 not fixed** |
| A2c | ✅ | 1440 × 900: the whole page fits above the buttons |
| A3 | ✅ | aperçu 200 `application/pdf`; preview PDF carries both bands |
| A4 | ✅ | saved; the card stays open and shows both bands (F2 fixed) |
| A5 | ✅ | `clinics/{id}/letterhead/{guid}/header` + `…/footer` |
| A6 | ✅ | 6 money/medical PDFs carry both bands |
| A7 | ✅ | long ordonnance: both bands on every page |
| A8 | ✅ | the editor's preview starts with the band, no text header |
| A9 | ❌ | `locator.click: Timeout 30000ms` — see the verdict in « Fixes applied (cycle 2) » |
| B1–B8 | ✅ | refusals, keyboard move, footer off, withdraw, doctor read-only, forged key ignored |
| C1 / C1b / C1c | ✅ | 320 px: no horizontal scroll, steps one line each, « en-tête » whole, page fits (F5 fixed) |
| C2 / C2b | ✅ | 1536 × 730: buttons inside, page fits (F4 fixed) |
| C3 | ✅ | a document keeps the paper it was issued on |
| C4 | ✅ | 409 offers « Recharger », then the save goes through (F1 fixed) |
| D1 / D2 / D4 | ✅ | no band without a letterhead; pre-letterhead document keeps its text; no « naissance » anywhere |
| D3 | ⏭ | not exercised — the QA clinic's name is blank, so « Informations du cabinet » refuses to save for an unrelated reason |

## Findings

### F3 (carried) — major — the suggested « Fin de l'en-tête » still cuts above the letterhead's rule
- **Observed:** 40 mm on `entete-imprimeur.pdf` (rule at 44.5 mm). The cycle-1 change (longest blank run) was right but
  never got the chance: the rule was not seen as ink at all.
- **Owner:** `web/lib/letterhead/bands.ts` `inkRows`.

### A9 — probe — the editor has no Word button to press
- The « Plus » the walk clicked is the hidden phone nav button, and no Word control exists on the page.

**Status: RED (1 finding — F3, major; A9 to triage)**

## Fixes applied (cycle 2)

| Finding | Verdict | Root cause | Files | Blast radius |
|---------|---------|------------|-------|--------------|
| F3 | confirmed | the page was shrunk to a 240 px probe with the canvas's default low-quality sampling, which skipped the 0.56 mm rule entirely (the same algorithm with area averaging finds it at 48 mm); ink threshold also fixed at 200, too dark for a coloured rule | `web/lib/letterhead/bands.ts` — 800 px probe, `imageSmoothingQuality = "high"`, threshold relative to the paper's own shade (90th percentile − 22, capped at 232), ≥ 0.4 % of the row inked | `suggestCuts` has one consumer (the import dialog); only the suggestion moves, the user still drags the lines |
| A9 | deliberate | « Télécharger Word » was withdrawn app-wide by the owner (`WORD_EXPORT_OFFERED = false`, commit 85dfd995 « « Télécharger Word » est retiré de toute l'application ») | — (plan row + walk row corrected) | the letterhead docx code stays compiled and unreachable, like the rest of `generateWord` |

**Guards added:** none — the cut suggestion is a heuristic with no second owner to drift from.
**Gates after the last edit:** check:responsive ✅ 76/76 · tsc ✅ · build ✅ · no API change (dotnet suite last run: 4 940 ✅)
**Re-run:** run-3.md — GREEN (33 ✅, 2 ⏭ with reasons)
