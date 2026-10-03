# QA run 2 — Reste à payer worklist

**Date:** 2026-10-03
**Cycle:** 2 of at most 3
**Plan:** features/reste-a-payer-worklist/qa/plan.md (20 scenarios + 5 layout probes added from run 1)
**Driver:** features/reste-a-payer-worklist/qa/walk.mjs
**Environment:** api :5000 (mine, PID 40936) · web :3000 `next dev` (owner clinic-management-2c, hot reload) · postgres clinic-postgres
**Peer sessions live during the run:** clinic-management-1c, clinic-management-2c (committed this work as 2e796cb6 during the run), clinic-management-a5
**Widths looked at:** 320×730 · 390×844 · 820×1024 · 1440×900 · 1536×730
**Status:** RED (1 finding: 1 major — found by the eye pass, not by the script)

## Scenarios

All 25 scripted rows ✅ (B5 ⏭ — no secretary credentials). F-1 and F-2 from run 1 are fixed:
`C1-cards` labels on one line at 320 · `C2-cards` no overlap at 820 · `A2-sort` / `C2-sort` « Plus ancien » on one line.

## Findings

### F-3 · major · at 820 px the 4th tab is off-screen

- **Repro:** `/a-cloturer` at 820×1024 (tablet portrait, rail open).
- **Expected:** all four tabs visible (the strip wraps).
- **Actual:** the strip scrolls sideways; « Suites à planifier » is cut at the edge and « Reste à payer » is out of view.
- **Evidence:** shots/C2-820.png.
- **Suspected owner:** `web/app/a-cloturer/page.tsx:260` — `sm:flex-nowrap` turns the wrap off from 640 px, which three tabs survived and four do not.
- **Regression?** Yes — blast-radius row #8 (« 4th tab wraps »); the probe checked each tab's own label, not the strip.

## Probe bugs fixed during the run (not product defects)

| Row | Symptom | Real cause |
|-----|---------|------------|
| login | 2FA refused once | shared QA account, code reused in the same window; the helper now retries on the next window |
| — | mixed output | a stopped run's browser kept writing to the same file; killed it and re-ran into a fresh file |
| B1 | 0 rows after « Effacer la recherche » | counted before the debounced reload; now waits for rows |

## Fixes applied (cycle 1)

| Finding | Verdict | Root cause | Files | Blast radius |
|---------|---------|------------|-------|--------------|
| F-1 | confirmed | three grid columns at every width; the middle `minmax(0,1fr)` collapsed beside the amount | `reste-a-payer-list.tsx` (`ListCard` → container query `@md`) | 1 component, no consumers |
| F-2 | confirmed | `ModeSegmented` at `sm:w-auto` let its `flex-1` options shrink, then the search box squeezed it | `reste-a-payer-list.tsx` (`sm:w-64 sm:shrink-0`, search `min-w-0`) | 1 call site; the primitive untouched |

**Gates after the last edit:** check:responsive 72/72 ✅ · tsc 0 ✅
