# QA run 3 — Pointage obligatoire (part 3)

**Date:** 2026-11-06 (09:38 Tunis)
**Cycle:** 3 — re-run after `/fix-qa-findings` cycle 2 (F2)
**Plan:** `features/pointage-obligatoire/qa/plan.md` — all 34 scenarios, same `walk.mjs` (A10 gained the « no longer
asks / no longer says présente » check; no row removed)
**Environment:** isolated — API :5096 (backend unchanged since run 1, mine) · web :3096 `next dev` (hot-reloaded, mine)
· DB `clinic_pointage_qa` · Chrome 9333 / 9334 · peers as in run 1, none on these ports or this DB
**Status:** GREEN

## Scenario results

103 ✅ · 0 ❌ · 0 ⏭ over the 34 scenarios (A1–A17, B1–B12, C1–C6, D1–D5). Rows the fixes reach:

| ID | Outcome | Evidence |
|----|---------|----------|
| A9 | ✅ | the lock's pad punch toasts « Arrivée enregistrée à HH:mm. », the lock closes |
| A10 | ✅ | dialog reads « Départ enregistré · À 09:42. » with nothing still asking « Pointer votre départ ? » or saying « présente »; then signed out, line closed — `shots/A10-departure-confirmed.png` (1536×730) |
| C3 | ✅ | « Se déconnecter sans pointer » on screen at 1536×730 before the punch |

**Widths looked at (runs 1–3):** 320×568 · 560×760 · 812×380 · 820×600 · 820×1000 · 820×1180 · 1000×830 · 1440×900
· 1536×730.

## Fixes applied (cycle 2)

| Finding | Verdict | Root cause | Files | Blast radius |
|---------|---------|------------|-------|--------------|
| F2 | confirmed (introduced by F1's fix) | the dialog now stays open 2 s after the punch, and its header ignored `departedAt` while the `team` broadcast refreshed her status underneath — so « Vous êtes présente. » (a fallback only reachable once she has left) rendered beside « Départ enregistré » | `sign-out-prompt.tsx` — header becomes « Départ enregistré · À HH:mm. » once recorded; the false fallback removed | 1 consumer (`dashboard-header`), re-run |

**Gates after the last edit:** check:responsive 86/86 (one red on the way — `arch-clipping` on a `flex justify-center`
inside the dialog's scroller, fixed with `mx-auto`) · tsc (source) clean · build green.

## Carried to the user (off-plan, not fixed here)

From run 1 § Observations, unchanged:
- CSV « État » drops « Déclaré » on a line whose arrival an admin added (`AttendanceRules.StateLabel` puts `Corrigé` first).
- Decorative second icon under « Borne : Accueil » on the non-borne lock.
- Two stacked headlines on the borne lock (« Pointez votre arrivée » + « Ines Gharbi / Arrivée »).
- At 1536×730 the sign-out prompt's own « Annuler » scrolls below the fold (the ✕ still closes it).
