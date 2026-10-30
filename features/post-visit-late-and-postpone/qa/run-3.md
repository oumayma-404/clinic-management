# QA run 3 — free times in « Reporter » (option A of `blueprint-free-slots.md`)

**Date:** 2026-10-30 (10:40–10:55 Tunis)
**Cycle:** 1 of the free-slot change
**Driver:** walk.mjs, the **whole plan** (no `ONLY`), then `ONLY=F4` for the AC-14 row written after the launch
**Environment:** isolated — API :5098 (rebuilt with `FreeSlotFinder`) · web :3098 standalone (rebuilt) · postgres
`clinic_pvl_qa`, shared with the owner's open test window. The copy holds a congé for the QA doctor on 2–4 Nov
(« Vacances »), which F2 used rather than writing one. The walk now cancels its own « Test R…-NNNN » visits at the
end, so the owner's prompt queue and « À clôturer » are left as found.
**Status:** RED (1 finding, from the eye pass — every scripted check passed)

| ID | Outcome | Evidence |
|----|---------|----------|
| A1 · A1@820 · B3 · C4 | ✅ | four answers on one line (448 px); « Retour » back; « Reporter » at y=506 in 730 px |
| A2 | ✅ | chip « Demain 24 libres » → listed 12:00 → moved to 31/10 12:00, **no refusal** · shots/run3-A2-free-slots.png |
| B1-list · B1 | ✅ | R8's 09:00 not listed; typed 09:00 → « Ce créneau est déjà réservé… » → « Reporter quand même » → moved |
| B2 | ✅ | typed 18:00 → closed hour named → moved anyway |
| F2 · F2-typed | ✅ | « Lun. 2 Congé » → « Le praticien est en congé ce jour-là. », no time, Reporter disabled; typed 14:00 → « Salma Ben Youssef est en congé du 02/11 au 04/11. » + « Reporter quand même »; not moved |
| F3 · F3-retry | ✅ | forced 500 → « Les heures libres n'ont pas pu être chargées. » + Réessayer, time field shown, day 31/10; Réessayer → 5 chips + grid |
| F4 | ✅ | calendar 20/11 → read `from=2026-11-20`, its chip checked, « Heures libres · vendredi 20 novembre » |
| B6 · B6-dup · A3 | ✅ | liste d'attente path unchanged |
| B7 | ✅ | colleague's move → « Recharger » → subject 15:30, typed 14:00 kept → moved |
| C1-popover · C1-button · C1-dialog | ✅ | 320×640: sheet 320 px, every button inside, scrollWidth 320 |
| A11 · A11-slots · A11-phone | ✅ | coarse 1180: 5 day chips and 19 times all ≥ 44 px; phone silent |
| A4–A10, C2, C3, D1–D7, A5 | ✅ | retard rows, unchanged by this change — all green |

52 + 1 checks · 53 pass · 0 fail · 0 not exercised. Widths looked at: 1536×730, 1440×900, 820×1024, 320×640, 1180×820 coarse.

## Findings

### 1 · major — « Retour » clipped in the popup's refusal state
- **Where:** popup → Reporter → a typed time refused (taken slot, closed hour, congé).
- **Expected:** the three footer buttons inside the dialog.
- **Observed:** « Retour » reads « tour » — cut by the dialog edge (shots/run3-F2-conge-typed.png, run3-B1-slot-taken.png).
- **Measured (probe):** footer box 398 px; « Retour » 76 + « Mettre en liste d'attente » 189 + « Reporter quand même » 178
  + gaps = **459 px**. The ordinary state (« Reporter à 09:00 », 145) needed 426 px — already 28 px over, hidden
  inside « Retour »'s own padding by luck. The footer is `justify-end` and does not wrap from `md:`, so the overflow
  spills off the left edge.
- **Owner:** `web/components/appointment-postpone-dialog.tsx` footer (`DialogFooter`).
- **Fix (applied after this run):** the button reads « Reporter » (the picked time stays highlighted in the grid) and
  the footer wraps from `md:` instead of clipping. Held by the new rows `A2-footer` and `F2-footer`.

## Observations (off-plan)
- run3-A2's selected « 12:00 » looks faded: the shot caught its colour transition. A probe 800 ms later reads the same
  background as the primary button. Probe, not product — A2 now waits before its shot.
- The five day chips plus the calendar button take two lines at 448 px (the calendar alone on the second). Kept: with
  four chips, the copy's 3-day congé would have left one bookable chip (Aujourd'hui, Demain, then congés only).
