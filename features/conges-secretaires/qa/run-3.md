# QA run 3 — Congés des secrétaires (redesign)

**Date:** 2026-11-05
**Why a third run:** the owner asked for the screens to be redesigned (mockup https://claude.ai/artifact/7ysCZtWNAoxzgLivSBniV2); this is the same plan re-run on the new interface, plus four new rows (A19–A22).
**Plan:** features/conges-secretaires/qa/plan.md (37 rows) · **Driver:** walk.mjs (86 checks)
**Environment:** isolated — api :5096 rebuilt after the last server edit · web :3096 `next dev` · DB `clinic_staffleave_qa` · two visible Chrome windows (9333 admin / 9334 secretary)
**Widths looked at:** 320×640 coarse · 390×844 coarse · 820×1024 · 1440×900 · 1536×730 · admin window 986×738 · secretary window 546×738
**Gates after the last edit:** tsc ✅ · check:responsive 85/85 ✅ · dotnet build ✅ · unit suite 6220/0 ✅
**Status:** GREEN (86/86)

## What moved between the three drives of this run

| Drive | Result | Cause |
|-------|--------|-------|
| 1 | 21 ❌ | one probe miss cascading: the walk looked for « 16 novembre » and the new rows read « 16 → 18 novembre », so nothing was accepted and every later row inherited it |
| 2 | 4 ❌ | probes: an A10 pattern not updated, two reads taken before the row moved sections, a 43,9999 px button read as < 44 |
| 3 | 86/86 ✅ | — |

## Product changes made during the run (not findings of the old plan)

| What | Where |
|------|-------|
| At 320 px a decision card's buttons wrapped into three ragged rows | `staff-leave-request-list.tsx` — Refuser / Accepter side by side, « Annuler » under them on a phone |
| The rail badge read « 4 demande de congé en attente » to a screen reader | `dashboard-sidebar.tsx` — plural |

## Observations (off-plan)

- **The « wonky » font the owner saw was the test server, not the redesign.** `next dev` on :3096 could not reach
  Google Fonts when it started, so every page fell back to « Geist Fallback » (Arial stretched to Geist's metrics).
  Restarted with a clean `.next`: Geist loads (`document.fonts` → `Geist: loaded`), shots/FONT-after.png. A production
  build downloads and self-hosts the font at build time, so this cannot reach a clinic.
- `next dev`'s « N » badge still sits over the collapsed rail's bottom-left button (dev only).
