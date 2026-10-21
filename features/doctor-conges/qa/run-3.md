# QA run 3 — Congés du praticien (re-run after cycle 2)

**Date:** 2026-10-20 · same plan, same `walk.mjs` (C3 now measures the word's clip at 390 **and** 320) · same isolated
stack (API :5099, web :3098, DB `clinic_conges_qa`). API rows: unchanged since run 2 (no `api/` edit), 13/13 ✅.

## Scenarios
Every row ✅ except one: A1–A14, B1, B7, C1, C2, C3 @390, C4, C5, D1–D3, D6, F1-bar (10 widths × filtered/unfiltered).

| ID | Result | Evidence |
|----|--------|----------|
| C3 @390 | ✅ | « congé » pills, word not clipped (F4 fixed) |
| C3 @320 | ❌ | F5 |

## Findings

### F5 — minor · at 320 px the phone-week pill still reads « con… »
- **Repro:** 320×640, `/appointments?date=2026-11-02&view=week` (`shots/C3-320-week.png`).
- **Actual:** the span clips by 2 px in a ~37 px column (`scrollWidth − clientWidth = 2`).
- **Suspected fix (not applied — cycle cap):** compact chip `px-0` instead of `px-0.5` in `renderLeaveChip`
  (`appointment-calendar.tsx`); gains 4 px.

**Status: RED (1 minor finding) — cycle 3 reached, loop stopped.**

Gates after the last edit: check:responsive 81/81 ✅ · tsc ✅ · build ✅.

Re-run: `node walk.mjs <dir holding playwright-core + totp.mjs>` and `node api-rows.mjs` (both from `qa/`, stack on :5099/:3098); delete the 4 probe visits `api-rows.mjs` prints afterwards.
