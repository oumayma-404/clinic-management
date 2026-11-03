# QA run 2 — Guided first days, release 2 (parts D + E)

**Date:** 2026-11-03
**Cycle:** 2 of at most 3 (after `/fix-qa-findings` cycle 1: F-1, F-2)
**Plan:** features/guided-first-days/qa/r2/plan.md (34 scenarios) — the whole plan, same `walk.mjs`
**Driver output:** `walk-run2.log` · results `walk-results.json`
**Environment:** same isolated stack as run 1 (api :5097 + console :5094, web :3097 `next dev` hot-reloaded the two edited
files, console :3094, throwaway `clinic_gfd_r2`). A new cabinet, admin, doctor and secretary per drive.
**Peer sessions live:** as in run 1 (clinic-management-29 busy on :5099/:5098/:3098 — untouched).
**Widths looked at:** 320 · 390 · 1440 · 1536×730 — `Read`: C3-patients-after-screen-change, C4-card-390, C5-form-390,
A9-retours-1440, A10-devis-new-patient, A1-aide-1440
**Status:** GREEN

## Scenarios

Every row of run 1 re-driven: **73 checks ✅, 0 ❌, 1 ⏭**.

| Changed since run 1 | Outcome | Evidence |
|---|---|---|
| C3 (F-1) | ✅ | card still up on `/patients` after « Patients » in the rail, `TimesShown` 2; shots/C3-patients-after-screen-change.png |
| D6 (F-1) | ✅ | no « Séance terminée » while the card is up; it opens once the card is answered; shots/D6-visit-after-card.png |
| A3 (F-2) | ✅ | « Retour à l'agenda » is a link to `/appointments` |
| C5 (probe) | ✅ | 390 sheet measured after it landed; « Envoyer » on screen; shots/C5-form-390.png |
| A8 | ✅ | reached with the card up (no snooze needed): 4th star kept, row `semaine-1|4|3|agenda|devis|…` |
| B11 | ⏭ | LAN profile — a second API in `SelfHostedLan` |

All other rows: ✅ as in run 1 (see `walk-run2.log`).

## Findings

None.

## Observations (off-plan)

- The console page (`/retours`, `next dev`) shows Next's « 1 Issue » dev badge; nothing in the console server log. A
  client-side dev-only warning, not chased (off-plan).
