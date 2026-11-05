# QA run 5 — Congés des secrétaires (rebased on main 7000b0e0)

**Date:** 2026-11-05
**Why a fifth run:** the branch was rebased onto origin/main (26 commits, 4 migrations — review Finding 1); same plan, re-run whole, plus G1.
**Plan:** features/conges-secretaires/qa/plan.md (42 rows) · **Driver:** walk.mjs (92 checks)
**Environment:** isolated — api :5096 rebuilt on the rebased tree, main's 4 migrations applied to `clinic_staffleave_qa` at startup (`verify-schema`: the 8 « MISSING » lines they caused are gone; the 5 left are the copied database's own, identical before and after) · web :3096 `next dev` · two visible Chrome windows
**Gates after the last edit:** unit suite 6450/0 (6 skipped) ✅ · tsc ✅ · check:responsive 86/86 ✅
**Status:** GREEN (91/92, 1 not exercised)

## What moved between the drives

| Drive | Result | Cause |
|-------|--------|-------|
| 1 | 8 ❌ | **probe**: main's « Une semaine avec APEXA » corner card (fixed bottom-right, after a minute, for an account used 3+ days) sat over a decision card's « Refuser »; A12's click timed out and A13/A20/F4/D3 inherited it. The walk now answers « Plus tard » through `addLocatorHandler` |
| 2 | 1 ❌ | **probe**: F4 still expected a day count on a half-day refusal, which the server deliberately no longer repeats; F4 now asserts the days once |
| F4 alone | ✅ | — |

## Not exercised

| Row | Why |
|-----|-----|
| G1 | this cabinet's « Premiers pas » has no « Votre secrétaire » step left, so the tour cannot be started here. The tour is launched from the dashboard, which mounts `/users` on « Comptes » by default; `app/users/page.tsx` also switches to « Comptes » whenever that tour runs |

## Findings

None in the product.
