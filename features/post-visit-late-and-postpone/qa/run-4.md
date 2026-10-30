# QA run 4 — free times in « Reporter », after the footer fix

**Date:** 2026-10-30 (11:00–11:10 Tunis)
**Cycle:** 2 of the free-slot change — re-run of the **whole plan** after fixing run 3's finding
**Driver:** walk.mjs, no `ONLY` (same script as run 3, plus `A2-footer` / `F2-footer`; A2 now waits out the pressed
cell's colour transition before its shot)
**Environment:** isolated — API :5098 · web :3098 standalone, **rebuilt with the fix** · postgres `clinic_pvl_qa`,
shared with the owner's open test window (the walk cancels its own test visits at the end)
**Status:** GREEN

| ID | Outcome | Evidence |
|----|---------|----------|
| A2 | ✅ | « Demain 24 libres » → 12:00 (checked) → « Reporter » → moved to 31/10 12:00, no refusal · shots/run4-A2-free-slots.png |
| A2-footer | ✅ | one row, every button inside the 569–967 box: Retour 569–645 · Mettre en liste d'attente 680–869 · Reporter 877–967 |
| F2-footer | ✅ | refusal state: two rows, nothing clipped — Retour + « Mettre en liste d'attente » on the first, « Reporter quand même » on the second · shots/run4-F2-conge-typed.png |
| every other row of runs 1–3 | ✅ | A1, B3, C4, B1, B2, F2, F2-typed, F3, F3-retry, F4, B6, A3, A4–A11, C1–C3, D1–D7, A5 |

55 checks · 55 pass · 0 fail · 0 not exercised. Widths looked at: 1536×730, 1440×900, 820×1024, 320×640, 1180×820 coarse,
390×844.

Gates after the last edit: `tsc` 0 · `check:responsive` 81/81 · `npm run build` ✓ · unit suite (unfiltered, Release)
6051 passed / 0 failed / 6 skipped.

## Findings

None.
