# QA run 6 — free times in « Reporter », the press's answer pinned above the buttons

**Date:** 2026-10-30 (11:50–12:05 Tunis)
**Cycle:** 2 of the stale-list follow-up — re-run of the **whole plan** after fixing run 5's finding
**Driver:** walk.mjs, no `ONLY`; the « seen » checks now require the message to be inside the viewport **and** the
topmost element at its own centre (run 5's `waitFor` passed for a message scrolled out of the body)
**Environment:** isolated — API :5098 · web :3098 standalone rebuilt · postgres `clinic_pvl_qa` (owner's window open)
**Status:** GREEN

| ID | Outcome | Evidence |
|----|---------|----------|
| F5 | ✅ | listed 11:42 went past while open → pressed: no PUT, list re-read, « Cette heure est déjà passée. » **on screen** above the buttons, Reporter disabled · shots/run6-F5-time-passed.png |
| F6 | ✅ | taken since the read → the clash **on screen** above the buttons + « Reporter quand même »; list re-read, 09:00 gone; not moved · shots/run6-F6-taken-since-read.png |
| B1-seen · F2-seen · B7-seen | ✅ | the clash, the congé refusal and the 409 banner with « Recharger » all on screen · shots/run6-B7-conflict.png |
| A2-footer · F2-footer | ✅ | one row normally; two rows on a refusal; nothing clipped |
| F7 | ✅ | no hours saved → typed time + « Aucun horaire n'est enregistré pour ce cabinet. » |
| every other row | ✅ | A1–A11, B1–B7, C1–C4, D1–D7, F2–F4 |

61 checks · 61 pass · 0 fail · 0 not exercised. Screenshots read (eye pass): F5, F6, B7, C1 at 320×640.

Gates after the last edit: `tsc` 0 · `check:responsive` 81/81 · `npm run build` ✓ · unit suite unchanged since its last
green run (no backend edit since): 6051 passed / 0 failed.

## Findings

None.
