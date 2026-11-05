# QA run 2 — lost second factor: a visible way back

**Date:** 2026-11-03
**Cycle:** 2 of at most 3 (after F-1)
**Plan:** `qa/plan.md` (rows A4–A6 updated to the corrected wording) — same `walk.mjs`, output `walk-run2.log`
**Environment:** as run 1 (isolated API `:5097` + web `:3097` from this worktree, throwaway `clinic_sfr_qa`, own headless
Chrome, fresh cabinet). Peers: demo-cabinet session on its own ports, untouched.
**Widths looked at:** 320×640 · 390×844 · 1024 · 1279 · 1280 · 1440×900 · 1536×730 — `Read`: A4-stepup-1440 (settled, no
promise about the next sign-in)
**Status:** GREEN

## Scenarios

**59 ✅, 0 ❌, 0 ⏭** — every row of the plan, A1–A8, B1–B6, C1–C5, D1–D5.

| Row | Outcome |
|-----|---------|
| A4 | ✅ step-up names his e-mail, the codes, the sessions — and says nothing about his next sign-in |
| A5 | ✅ toast « Second facteur réinitialisé. Ses sessions ont été fermées. »; DB cleared; item gone |
| A6 | ✅ doctor signs in with his password alone; nothing on screen had said otherwise |
| A8 | ✅ second admin: reset, then her next sign-in asks for a new factor |
| all others | ✅ as in run 1 |

## Gates after the last source edit

check:responsive 81/81 ✅ · tsc ✅ · `npm run build` ✅ (dev server stopped first, `.next` removed after) · API unchanged
since f341e051 (its suite: 6055 ✅ + the pre-existing `FreeSlotFinderTests` date failure, fixed separately on
`fix/freeslot-test-date`).

## Still open (not this branch)

- `follow-up/second-factor-reset-promises-re-enrolment.md` — the e-mail, in-app notice, vendor console and LAN verb
  still promise a re-enrolment that only happens for an admin.
- The native « Please fill in this field » bubble on arriving at the recovery-code step (run 1 observation, older than
  this change).
