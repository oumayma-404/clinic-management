# QA run 4 — Congés du praticien (owner-approved fix of run 3's F5)

**Date:** 2026-10-20 · owner asked for the fix past the cycle cap (« fix ») · same plan, same `walk.mjs`, same
isolated stack (API :5099, web :3098, DB `clinic_conges_qa`). No `api/` change since run 2, so API rows unchanged (13/13 ✅).

## Fix applied

| Finding | Verdict | Root cause | Files | Blast radius |
|---------|---------|------------|-------|--------------|
| F5 | confirmed | compact chip's `px-0.5` left the word 2 px short in a ~37 px column | `appointment-calendar.tsx` `renderLeaveChip` → compact `px-0` | compact branch only (phone Semaine ribbon); desktop chip unchanged |

## Result
Whole walk re-run: **ALL CLEAR** — every row of runs 1–3 ✅, C3 ✅ at 390 **and** 320 (`shots/C3-320-week.png`: « congé » in full).

Gates after the last edit: check:responsive 81/81 ✅ · tsc ✅ · build ✅.

**Status: GREEN**
