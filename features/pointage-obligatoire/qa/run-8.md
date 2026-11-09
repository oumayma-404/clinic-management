# QA run 8 — Pointage obligatoire (part 3) · after the run-7 fix

**Date:** 2026-11-06 (13:30 Tunis)
**Why:** run 7 found F1 (« Ajouter une borne » scrolled sideways at 320 px) and two probe gaps in C10; whole plan re-run.
**Plan:** `qa/plan.md` unchanged since run 7. Same `walk.mjs` (C10 measures every scroller in the dialog, after
`bringToFront`; red-proved in run 7).
**Environment:** as run 7 — demo stack API :5096 / web :3096 on `clinic_pointage_demo`, Chrome 9335 / 9336, the
owner's windows on 9333 / 9334 untouched, her congés kept aside in `qa_keep_leaves`.
**Status:** GREEN — 160 ✅ · 0 ❌ · 0 ⏭

## Scenarios

| ID | Outcome | Evidence |
|---|---|---|
| A1–A20 | ✅ | A2 through « Ajouter une borne » → « Cet appareil »; A19/A20 her device chosen, claimed on her open screen, cookie HttpOnly on hers |
| B1–B17 | ✅ | B15 waiting / signed-out device; B16 a waiting borne alone never locks; B17 no device picked |
| C1–C10 | ✅ | C10: 320×568 and 1536×730 — no scroller overflows, « Enregistrer la borne » on screen, rows ≥ 40 px — `shots/C10-*.png` |
| D1–D5 | ✅ | unchanged |

Widths looked at: 320×568 · 560×760 · 812×380 · 820×600/1180 · 1000×830 · 1440×900 · 1536×730.

**Gates after the last edit:** unit suite 6743 ✅ · check:responsive 86/86 ✅ · tsc ✅ · `next build` — see progress.md.
