# QA run 2 — Pointage sur la borne du cabinet

**Date:** 2026-11-05
**Cycle:** 2 of at most 3 (re-run after the cycle-1 fixes)
**Plan:** features/pointage-borne/qa/plan.md (34 scenarios: A18 B13 C6 D5) — the whole plan, same `walk.mjs`
plus three checks for the fixed visuals (A16 ×2, C2 ×1); no row removed
**Driver:** features/pointage-borne/qa/walk.mjs (full drive, reset first) + a same-session re-drive of A10/A11 after a probe fix
**Environment:** isolated — api :5096 (unchanged since run 1: no backend edit) · web :3096 `next dev`, restarted
18:00 after the fixes and the build (my orphaned dev child PID 20992 stopped first) · DB `clinic_staffleave_qa`
**Peer sessions live during the run:** none on these ports/DB (isolated stack)
**Widths looked at:** 320×640 · 390×844 · 547 · 820×1180 · 1000 · 1180×820 · 1440×900 · 1536×730
**Status:** GREEN

## Scenarios

Every row of run 1 passed again (102 ✅ · 2 ⏭ on the full drive), plus:

| ID | Outcome | Evidence |
|----|---------|----------|
| C5 (F-1) | ✅ | 409 + « Recharger »; after « Recharger » the departure reads **17:30** (server value) |
| A16 (F-2) | ✅ | the three figures share one row (label tops equal); shots/run2-A16-mon-profil-547.png |
| A16 (F-3) | ✅ | schéma card is one row: title left, « Changer de schéma » right, same line; same shot |
| C2 (F-4) | ✅ | card titles « Jeudi 5 novembre » / « Mardi 3 novembre »; shots/run2-C2-presences-390.png |
| A11 | ✅ | full drive ❌ « button n=0 » was the probe: Radix keeps the page `aria-hidden` while the closing « Ajouter » dialog unmounts (B10 clicked that same button seconds later). Probe now waits for the dialog to detach and polls; re-driven A10+A11 → « 08:30 – Départ non pointé » + button ✅ |
| B9 | ⏭ | curl: 401 `terminal_unknown` (no cookie / bogus cookie) |
| D5 | ⏭ | unit suite 6450 / 0 |

⚠️ The re-drive of A10 shows « today row … présente » ❌ — expected: B13's setup closed today's open line by SQL
earlier in the full drive, and the row passed in that drive. The re-drive's extra 3 nov line was deleted
afterwards (own artefact, `AttendanceEntries` id e9164732…).

⚠️ The full drive's screenshots of the secretary window timed out silently (window behind the admin's);
`A16-mes-presences.png` is therefore still run 1's. The fresh shots were taken with the window brought to front
and are the ones cited above.

## Findings

None.

## Observations (off-plan)

- Carried from run 1, unchanged: an admin-added line shows « Corrigé » with its motif; `useFreshVersion`'s
  `mounted` ref under StrictMode (pre-existing hook).
