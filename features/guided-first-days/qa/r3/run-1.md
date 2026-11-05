# QA run 1 — Guided first days, release 3 (« Me guider »)

**Date:** 2026-11-03
**Cycle:** 1 of at most 3
**Plan:** features/guided-first-days/qa/r3/plan.md — driver `walk.mjs`, output `walk-run1.log`
**Environment:** isolated stack — API `:5097` (worktree), web `next dev :3097`, throwaway `clinic_gfd_c`, own Chrome; a
fresh cabinet per drive. Peers live: clinic-management-29 (its own ports, untouched).
**Status:** RED (4)

## Scenarios

28 ✅, 4 ❌ (B2, A3, A4, B7), 1 ⏭ (D4).

## Triage

| Row | Verdict | Why |
|-----|---------|-----|
| B2 | **product** | in the user dialog the bubble sat on « Annuler » (placed under the lit fields) — a click on it timed out |
| A3 | needs a control | the patient was saved (201) but the page stayed on `/patients` |
| A4 | probe | saved (201); a confirmation closed between `waitFor` and `click` |
| B7 | probe | the server returned no pending review: a review only counts visits after the account's creation, and the fixture visit was older than the brand-new admin |

## Fixes applied (cycle 1)

| Finding | Verdict | Root cause | Files | Blast radius |
|---------|---------|------------|-------|--------------|
| F-1 (B2) | confirmed | the bubble avoided only the lit control | `components/guide/guide-overlay.tsx` — `placeBubble`: beside the dialog when there is room, never over the lit control or the dialog's footer; on a phone top/bottom by the same test | the overlay only |
