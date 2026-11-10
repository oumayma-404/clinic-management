# QA run 6 — Pointage obligatoire (part 3) · after the cycle-1 fix

**Date:** 2026-11-06 (12:07 Tunis)
**Why:** re-run of the whole plan after run 5's F1 fix (`noValidate` on the declaration form).
**Plan:** `qa/plan.md` (40 scenarios), same `walk.mjs`.
**Environment:** same isolated stack as run 5 — DB `clinic_pointage_qa`, API :5096 (unchanged code, no restart
needed: the fix is web-only), web :3096 `next dev` (hot-reloaded the edit), Chrome 9333 / 9334.
**Status:** GREEN — 128 ✅ · 0 ❌ · 0 ⏭

## Probe fix during this run (no source change)

The first drive of run 6 failed 15 checks from B9 to D4, all one chain. Cause: **the probe**. Chrome 9333 was still
open from run 5, so the walk reused the borne tab as a **background** tab; Chrome draws no frame for a hidden tab, so
the A12 screenshot hung its 30 s, the borne's 30 s idle timer returned it to the tiles, and every row needing that
declaration (B9, B10, C4, A13 → A14, A15, C9 → A17, B11, D4) failed with it. Evidence: the A12 / C4 / A1 screenshots
were not refreshed by that drive. `walk.mjs` now brings the page to the front before every screenshot (with a 10 s
cap) and before every borne punch; the whole plan was then re-driven in the same launch window. Not a cycle.

## Scenarios

Every row ✅. The ones run 5 had red or that the fixes touched:

| ID | Evidence |
|---|---|
| A18 | 3 Nov asked first, then 4 Nov; « Valider » enabled on « 930 »; **« 930 » + Enter → « Arrivée enregistrée »**; SQL `2026-11-03|17:00|1 ; 2026-11-04|09:30|1`; today's arrival open — `shots/A18-oldest-first.png` |
| A5 / C7 / B13 | one button « Se déconnecter » + link « Demander un congé »; focus on the title, scroll locked, Enter keeps her in; `/conges` open — `shots/B13-conges-open.png` |
| B14 | no active borne → not locked, write not 423; restored → locked |
| B3 | switch off → open screen unlocked in 3 s (`clinics` broadcast) |
| C8 | punch held 2.5 s: footer disabled, Escape ignored, then « Départ enregistré » |
| A14 / C9 | « 08:00 – 17:30 » amber · « Corrigé » « Déclaré » side by side before « Borne en panne hier » — `shots/A14-sheet-declared.png` |
| A17 / B11 / D4 | one reminder, xmin unchanged, still one on a second pass, bell → `/mon-profil` |

Widths looked at: 320×568 · 560×760 · 812×380 · 820×1000/1180 · 820×600 · 1000×830 · 1440×900 · 1536×730.

## Observations (off-plan)

- Same as run 5: the dialog title's focus ring (`ui/dialog.tsx`, pre-existing).

**Gates after the last edit (web only):** check:responsive 86/86 · tsc clean · `next build` green (run once the QA dev server was stopped).
