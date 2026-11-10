# QA run 2 — Congés des secrétaires

**Date:** 2026-11-05
**Cycle:** 2 of at most 3 (re-run after the cycle-1 fixes, same plan, same `walk.mjs`)
**Plan:** features/conges-secretaires/qa/plan.md (33 rows)
**Driver:** features/conges-secretaires/qa/walk.mjs (78 checks)
**Environment:** isolated — api :5096 **restarted with the fixes** (worktree `bin/Debug`, rebuilt after the last edit) · web :3096 `next dev` (hot-reloaded) · DB `clinic_staffleave_qa` · mailpit :8025. Two visible Chrome windows over CDP (ports 9333 admin / 9334 secretary).
**Peer sessions live during the run:** clinic-management-c2 (busy), clinic-management-29 (idle), clinic-management-77 — none on :5096/:3096 or this DB.
**Widths looked at:** 320×640 coarse · 390×844 coarse · 820×1024 · 1440×900 · 1536×730 · admin window 986×738 · secretary window 546×738
**Status:** GREEN (78/78 after one probe re-drive; 1 ⏭ by design: B7)

## Answered before the browser

| ID | Outcome | Evidence |
|----|---------|----------|
| P1 / D4 | ✅ | unchanged since run 1 — the fixes add no migration |
| P2 / D5 | ✅ | unfiltered suite after the last edit: `Passed 6220, Failed 0, Skipped 6` (one earlier run had `RelayFollowerTests.An_Unanswered_Heartbeat_Starts_The_Two_Minutes_Again` red while the API was cold-starting beside it; green on the full re-run — timing, not this change) |
| F-1 wire | ✅ | `GET /api/team/leave-preview?…2026-12-30…2027-01-02` → `{"days":4,"balanceAfter":17.500}` with 4,5 already taken (= 24 − 4,5 − 2; the old code gave 15,5) |

## Scenarios

| ID | Outcome | Evidence |
|----|---------|----------|
| A1–A14 | ✅ | same evidence as run 1, every check green |
| A15 | ✅ | preview « 4 jours · il restera **19,5** jours »; admin card « Solde après : **19,5** jours » + « Déjà en congé sur ces dates : QA Secrétaire 991861 »; shots/A15-others-off.png |
| A16 | ✅ | after acceptance « 19,5 jours restants · 4,5 jours pris sur 24 jours » — the same figure the form announced |
| A17, A18, setup-R6 | ✅ | as run 1 |
| B1–B5 | ✅ | as run 1 |
| B6 | ✅ (re-driven) | banner + « Recharger »; dialog closed after 235 ms; R4 « Annulé », status 3 |
| B7 | ⏭ | not exercised — no doctor session; API refusal belongs to `/test-small-feature` |
| C1 | ✅ | overflow 0 at 320; « Annuler » ≥ 44 px |
| C2 | ✅ | sheet: fields scroll inside, « Envoyer la demande » **and** « Annuler » pinned in view; shots/C2-320-sheet.png |
| C3, C4, C5 | ✅ | as run 1; C4 « Refuser la demande » in view at 1536×730 with the new `DialogBody` |
| D1–D3 | ✅ | as run 1 |

## Findings

None.

## Observations (off-plan)

- At 320×640 the day count (« 1 jour · il restera … ») sits under « Motif », inside the scrolling part of the sheet, so on a small phone it is read after a short scroll. It was below the fold before the fix too. Could move beside the footer if wanted; not chased.
- Run-1 observations still stand (reads allowed while the module is off · empty « Cabinet : » line on this dev clinic · short dates in server sentences, as specified).

## Probe bugs fixed during the run (not product defects)

| Row | Symptom | Real cause |
|-----|---------|------------|
| B6 | « Recharger » dialog « still open » | fixed 800 ms wait; the close was measured at 235 ms on the re-drive — now polled up to 5 s |
| B6 (`ONLY=B6`) | first click timed out | standalone, the windows start on /settings — the row now navigates itself |
