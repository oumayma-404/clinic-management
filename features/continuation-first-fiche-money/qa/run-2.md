# QA run 2 — Continuation's first séance: price kept, « le patient n'a pas payé » correctable

**Date:** 2026-11-07
**Cycle:** 2 of at most 3 (full re-run after the cycle-1 fixes)
**Plan:** features/continuation-first-fiche-money/qa/plan.md (20 rows: A6 B3 C3 D5 + fix rows F1 F2 F3)
**Driver:** features/continuation-first-fiche-money/qa/walk.mjs — same file as run 1, three rows added, none removed
(log: scratchpad `qa/walk-run2.log`)
**Environment:** ISOLATED — api :5093 rebuilt from the fixed tree and restarted 18:0x (mine) · web :3093 `next dev`
(mine, hot-reloaded) · database `clinic_qa_cff` · fresh fixtures (stamp 82650: P1 note 2026-0794, devis 2026-0958).
Prod and the shared `clinic_management` untouched.
**Peer sessions live during the run:** clinic-management-4d (busy), clinic-management-77 / -be (shell) — none on these
ports or this database.
**Widths looked at:** 320×700 · 390×844 · 1440×900 · 1536×730 (screenshots opened and read)
**Status:** GREEN (59/59 checks, 0 ⏭)

## Scenarios

| ID | Outcome | Evidence |
|----|---------|----------|
| A1 | ✅ | « Enregistrer la séance — 300,000 DT », Payé « 100,000 », no « Sur la note » |
| A2 | ✅ | plain save: one note 300/100, payment live, fiche 300/100 |
| A3 | ✅ | « Corriger la note — 300,000 DT »; dialog figures exact; « Corriger » on screen at 1536×730; shots/A3-1536x730.png |
| F1 | ✅ | line under « Payé »: « 100,000 DT déjà payés sur la note · « Corriger la note » la remplace. » (amber, no « ne se diminue pas ») |
| C1 | ✅ | 320×700: no overflow, both buttons inside |
| A4 | ✅ | toast « Note n° 2026-0799 émise — 0,000 DT payés »; 0794 Cancelled + voided; 0799 Issued 300/0 supersedes it; fiche 300/0; devis unchanged |
| A5 | ✅ | row « Note n° 2026-0799 … payé 0,000 DT · reste à payer 300,000 DT »; « Corriger » opens the fiche |
| F2 | ✅ | devis text « La 1re séance du 02/11/2026 est facturée sur sa propre note d'honoraires. … » — 0794 named nowhere; shots/A5-1440.png |
| F3 | ✅ | no « avec d'autres actes »; headline « devis + note »; shots/C3-390.png |
| A6 | ✅ | « Reste à payer 500,000 DT » |
| B1 | ✅ | 100 → 50: new note 300/50, payment dated 02/11 |
| B2 | ✅ | « Annuler »: notes and fiche byte-identical |
| B3 | ✅ | séance 2: « Inclus dans le traitement », no price |
| C2 | ✅ | reopen + save: still 2 notes, live one 300/0 |
| C3 | ✅ | 390: no sideways scroll, « Corriger » inside |
| D1 | ✅ | ordinary 90 → 0: new unpaid note 90/0 |
| D2 | ✅ | ordinary 90 → 60: new note 90/60 |
| D3 | ✅ | attached continuation: plain save at 300, note still attached 300/100 |
| D4 | ✅ | /caisse: draft sentence, no « Nouvelle note » |
| D5 | ✅ | /factures « Corriger cette note » → the fiche |

## Findings

None.

## Observations (off-plan)

- Same 3 × 401 before sign-in as run 1 (the `/login` session probe). Not chased.
- `e2e/scripts/check-coverage.mjs` exits 1 on 64 tier-0 scenarios with no test — identical on `main` (this branch
  touches nothing under `e2e/`). Not this pass's to fix.
