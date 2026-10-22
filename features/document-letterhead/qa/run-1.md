# QA run 1 — the cabinet's own letterhead (+ date of birth off the documents)

**Date:** 2026-10-06 · **Plan:** `plan.md` (24 scenarios) · **Driver:** `walk.mjs` (2 drives: the second after probe fixes only)
**Environment:** API :5000 (Debug, new code) and `next dev` :3000, both started and recorded by this session
(`clinic-management-cd`, lease generations 53/54) minutes before the walk; migration `AddClinicLetterhead` applied.
Peers listed by `ListAgents`: clinic-management-e0, clinic-management-33 (idle/shell), anakin-e1, anakin-27 (other repo) —
none touched the QA clinic. Own Chrome (playwright-core), not the MCP profile.
**Widths looked at:** 1440 × 900 (all dialog + card shots), 320 × 640 (C1), 1536 × 730 (C2). PDFs rasterised at 60 dpi
(all), header zoomed at 300 dpi (sharpness).
**Artefacts reverted:** the letterhead (B6 withdrew it — clinic back to text header) · two test ordonnances created for C3
and deleted (204).

## Scenarios

| ID | Result | Evidence |
|----|--------|----------|
| A1 | ✅ | `shots/A1.png` — badge « En-tête texte », « Importer mon papier à en-tête » |
| A2 | ✅ | « En-tête 41 mm · Pied de page 24 mm », switch on — `shots/A2.png` (but see F3, F4) |
| A3 | ✅ | preview POST 200 `application/pdf`, frame shown — `shots/A3.png`; preview PDF carries both bands |
| A4 | ❌ | saved + toast + badge « Papier du cabinet », but the card collapsed — **F2**; images load once reopened (2 × 2480 px) |
| A5 | ✅ | `clinics/f64a8a75…/letterhead/{guid}/header` + `…/footer`, same guid |
| A6 | ✅ | ordonnance, note, devis, reçu (note), reçu (devis), avoir — band edge to edge at the top and the bottom of page 1 — `shots/paper-*-p1.png` read |
| A7 | ✅ | 26-line ordonnance → 3 pages, both bands on every page — `shots/A7-long-p2.png` read |
| A8 | ✅ | `shots/A8.png`: the A4 preview opens on the header band, no text header (the walk's own check measured the wrong `.light` box — probe) |
| A9 | ⏭ | not exercised — the Word export is inside the « Plus » menu and the walk's selector did not open it (probe); re-driven in run 2 |
| B1 | ✅ | « Image trop petite (900 px de large) : elle serait floue à l'impression… » — `shots/B1.png` |
| B2 | ✅ | header at 163 mm refused « L'en-tête occupe plus d'un tiers de la page… » |
| B3 | ✅ | `notes.txt` → « Ce fichier n'est ni un PDF ni une image PNG ou JPEG… » |
| B4 | ✅ | ↓ ×4 on « Fin de l'en-tête » → 41 → 44 mm |
| B5 | ✅ | scan, footer off → saved, footer key null, PDF header band only — `shots/B5-nofooter-p1.png` |
| B6 | ✅ | « Retirer le papier à en-tête du cabinet ? » → « Retirer l'en-tête » → badge « En-tête texte », keys null |
| B7 | ✅ | doctor: no controls, « Modifiable par un administrateur. » — `shots/B7.png` |
| B8 | ✅ | forged `letterheadHeaderKey` in the body ignored — no band |
| C1 | ✅ | 320 px: no horizontal scroll, both buttons inside — `shots/C1-cut.png` (but see F5) |
| C2 | ✅ | 1536 × 730: footer buttons end at 650 px on both steps |
| C3 | ✅ | the document kept paper A's key after paper B was saved, and still renders a band |
| C4 | ❌ | the open dialog was reset to « Fichier » by the concurrent save — **F1**; the « Recharger » path was unreachable |
| D1 | ✅ | 7 PDFs without a letterhead: no band anywhere — `shots/no-lh-*-p1.png` read |
| D2 | ✅ | saved ordonnance re-renders; one issued before the letterhead keeps its text header |
| D3 | ⏭ | not exercised — the QA clinic's name is blank, so « Informations du cabinet » refuses to save for an unrelated reason |
| D4 | ✅ | no PDF contains « naissance »; the editor's preview has no « Date de naissance » |

## Findings

### F1 — major — The import dialog resets to « Fichier » whenever the clinic changes while it is open
- **Repro:** open « Importer », pick a file, reach « Aperçu »; meanwhile any write to the clinic row (a colleague's
  letterhead save, or « Informations du cabinet » saved in another tab).
- **Expected:** the dialog keeps the page and the lines; « Enregistrer » meets a 409 and offers « Recharger ».
- **Observed:** the dialog jumps back to step 1 with nothing loaded; the work is lost and the 409 recovery cannot be reached.
- **Owner:** `web/components/letterhead/letterhead-import-dialog.tsx` — the reset effect depends on `version`, and
  `useClinicLetterhead` updates that prop on every `clinics` broadcast.

### F2 — minor — After saving or removing, the card collapses and the result is not shown
- **Repro:** save a letterhead (or « Retirer »).
- **Expected:** the card stays open and shows the new paper (or the text header).
- **Observed:** the whole settings page swaps to its loader on the `clinics` broadcast and every card remounts closed
  (`shots/A4.png`).
- **Owner:** `web/components/clinic-settings.tsx:220` — `loadClinicData` sets `isLoading` on every reload, not only the first.

### F3 — major — The suggested « Fin de l'en-tête » cuts above the letterhead's own underline
- **Repro:** import `entete-imprimeur.pdf` (text block, then a thin rule at 44.5 mm).
- **Expected:** the suggested line falls below the rule, so the saved band carries the whole entête.
- **Observed:** suggested at 41 mm — the rule is left out of the band, on the ordonnance and every document (`shots/A2.png`,
  `shots/paper-ordonnance-p1.png`). A separator under the text is the ordinary letterhead layout.
- **Owner:** `web/lib/letterhead/bands.ts` `suggestCuts` — takes the FIRST blank run after the ink.

### F4 — minor — In « Découpe » the page is taller than the dialog, so both lines are never on screen together
- **Repro:** « Découpe » at 1440 × 900 or 1536 × 730.
- **Expected:** the whole page visible, both lines adjustable without scrolling.
- **Observed:** the page is sized by width (`max-w-md`) and runs below the dialog's fold; the footer line needs a scroll
  inside the dialog (`shots/A2.png`).
- **Owner:** `letterhead-import-dialog.tsx` `CutCanvas`.

### F5 — minor — At 320 px the title breaks at « en- / tête » and the step pills wrap
- **Observed:** « Importer mon papier à en- » / « tête »; « 1. » / « Fichier » on two lines (`shots/C1-cut.png`).
- **Owner:** `letterhead-import-dialog.tsx` — title and `StepTrail`.

## Eye pass — will it look great?
- The bands print at true scale, edge to edge, on every page; content keeps its 2 cm margins (`shots/paper-*`).
- 300 dpi zoom of the printed header: crisp text, lossless PNG (2480 px wide), ordonnance PDF ≈ 150 KB.
- Without a letterhead, every document is unchanged (`shots/no-lh-*`).

## Observations (off-plan)
- The QA clinic's name is blank in the database, so text-header documents print an empty name line (and the editor shows
  « [Nom du cabinet] »). Data, not this change.
- The aperçu's PDF toolbar names the file with a blob GUID — the documented limit of a `blob:` aperçu.

**Status: RED (5 findings — 2 major, 3 minor)**

## Fixes applied (cycle 1)

| Finding | Verdict | Root cause | Files | Blast radius |
|---------|---------|------------|-------|--------------|
| F1 | confirmed | the reset effect re-ran whenever `version` changed, and every `clinics` broadcast changes it | `letterhead-import-dialog.tsx` — reset only on the closed→open edge (`wasOpen` ref) | one dialog, no other consumer |
| F2 | confirmed | `loadClinicData` set `isLoading` on every reload, so a broadcast swapped the page to its loader and remounted every card closed | `clinic-settings.tsx` — loader on the first load only (`loadedOnce` ref) | all `/settings` cards now keep their state across a broadcast — re-tested by A4/B6 |
| F3 | confirmed (partly fixed) | `suggestCuts` took the first blank run after the ink | `web/lib/letterhead/bands.ts` — longest blank run | one consumer; the real cause was found in cycle 2 |
| F4 | confirmed | the cut canvas was sized by width only | `letterhead-import-dialog.tsx` `CutCanvas` — width capped by the dialog's height × the page's aspect | one dialog |
| F5 | confirmed | `en-tête` breaks at its hyphen; the step pills had no `nowrap` | `letterhead-import-dialog.tsx` — title span + `StepTrail` wrap | one dialog |
| A3/A6/A8/A9 (run 1) | probe | empty body read, a correct pre-letterhead document counted, wrong `.light` box, Word inside a menu | `walk.mjs` | — |

**Gates after the last edit:** check:responsive ✅ 76/76 · tsc ✅ · build ✅ · no API change
**Re-run:** run-2.md — RED (F3 carried; A9 triaged as deliberate)
