# QA run 4 — Congés des secrétaires (after the review fixes)

**Date:** 2026-11-05
**Why a fourth run:** `/apply-review-fixes` changed 18 files after the challenged review; this is the same plan, re-run whole, plus F1–F4.
**Plan:** features/conges-secretaires/qa/plan.md (41 rows) · **Driver:** walk.mjs (91 checks)
**Environment:** isolated — api :5096 rebuilt (Debug) and restarted after the last server edit · web :3096 `next dev` · DB `clinic_staffleave_qa` · two visible Chrome windows (9333 admin / 9334 secretary)
**Widths looked at:** 320×640 coarse · 390×844 coarse · 820×1024 coarse · 1440×900 · 1536×730
**Gates after the last edit:** tsc ✅ · check:responsive 85/85 ✅ · unit suite 6342/0 (6 skipped) ✅
**Status:** GREEN (91/91, one drive)

## Probe fix carried in

`hScroll` measured `document.documentElement` only; `<main>` is the page scroller, so a sideways scroll inside it was invisible. It now takes the larger of the two. C3 (320, admin) passes under the new probe because the filter is a 2×2 grid now — the run-3 screenshot showed the scrollbar the old probe missed.

## Rows added (F1–F4)

| ID | Result | Evidence |
|----|--------|----------|
| F1 | ✅ | `shots/F1-820-cards.png` — one card per row at 820, the footer on one line inside the card |
| F2 | ✅ | the discard confirm appears; the motif survives « Continuer la saisie » |
| F3 | ✅ | `shots/F3-320-bottom.png` — last block ends above the fixed button |
| F4 | ✅ | stored row: « Votre demande de congé le 25/11 (demi-journée) (0,5 jour) est refusée : « Période chargée ». » — the doubled bracket was then dropped for a half day (the period already says it); multi-day keeps « (3 jours) », held by `A_Refusal_Quotes_The_Motif_Word_For_Word` |

## Findings

None.
