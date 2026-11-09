# QA run 4 — Pointage obligatoire (part 3) · after the owner's polish

**Date:** 2026-11-06 (10:14 Tunis)
**Why:** not a fix cycle — the owner asked for the four off-plan observations of runs 1–3 to be fixed (« fix them »),
and those changes touch what the lock and the sign-out window draw, so the whole plan was re-run once.
**Plan:** `qa/plan.md`, all 34 scenarios, same `walk.mjs` (A10 gained two checks: one headline, « Annuler » in the
pinned footer; no row removed)
**Environment:** isolated, rebuilt for this run — DB `clinic_pointage_qa` (fresh copy of `clinic_management`, every
migration applied) · API :5096 · web :3096 `next dev` · Chrome 9333 / 9334 (fresh profiles). One launch aborted at the
very first sign-in (the cold dev server was still compiling `/login`; the page was fine once warm) — re-launched, no
source change.
**Status:** GREEN — 105 ✅ · 0 ❌ · 0 ⏭

## What changed, and the rows that see it

| Polish | Rows | Evidence |
|---|---|---|
| Sign-out window with the pad: one headline, « Annuler » in the footer | A10, C3 | `shots/A10-C3-signout-pad-730.png` (1536×730) |
| Lock on a borne: one headline | A9 | `shots/A9-lock-with-pad.png` (820) |
| Lock on an ordinary device: no decorative icon | A5, C1, C2 | `shots/C1-lock-320.png`, `shots/C2-lock-380.png` |
| CSV « Corrigé · Déclaré · … » | unit test `The_Export_State_Keeps_Every_Fact_Of_A_Closed_Line` (export is a download) | — |

Every other row as in run 3.
