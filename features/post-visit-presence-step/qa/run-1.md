# QA run 1 — Post-visit popup asks « venu ? » first

**Run:** 2026-10-03 ~18:05 · `walk.mjs` (one Chrome launch, headless, `channel: chrome`)
**Environment:** API :5000 (`bin/Debug`, started 17:47 outside a session) · web :3000 = a peer-started
`next dev --turbopack` (compiles the working tree, so the change is live) · peers live: `clinic-management-1b`,
`-2c`, `-a5` (all busy; none touched these records). Nothing was restarted.
**Deviation from plan:** tested on `next dev`, not the standalone build — the stack was a peer's. The one
CSS-order risk (`h-11!` vs sonner's 24 px) is `!important`, so source order cannot flip it; measured 44 px.
**Widths looked at (screenshots read):** 1440×900 · 1536×730 · 820×1180 coarse · 390×844 coarse.

## Scenarios

| ID | Outcome | Evidence |
|----|---------|----------|
| A1 | ✅ | « Séance de QA Présence … terminée. Le patient est-il venu ? » · Plus tard · Absent · Venu — `shots/A1-step1-1440.png` |
| A2 | ✅ | step 2 = Plus tard · Remplir la fiche de soins; 0 writes on « Venu » — `shots/A2-step2-1440.png` |
| A3 | ✅ | `/patients/<id>?addRecord=1&appointmentId=<ap1>` |
| A4 | ✅ | toast « Patient marqué comme absent. », dialog closed — `shots/A4-absent-toast-1440.png` |
| A5 | ✅ | SQL: ap2 `Status` = 6 (NoShow) |
| B1 | ✅ | refusal toast « Transition impossible : un rendez-vous « Terminé » ne peut pas passer à « Absent ». »; dialog stays on step 1; ap3 still 4 — `shots/B1-refusal-1440.png` |
| B2 | ✅ | snooze written for the review; no prompt after reload |
| B3 | ✅ | Escape on step 2 closes + snoozes |
| B4 | ✅ | a `Completed` visit opens directly on step 2 |
| B5 | ✅ | double-click « Absent » → exactly 1 PUT |
| C1 | ✅ | 1536×730: buttons at y 416–452, all inside — `shots/C1-1536x730.png` |
| C2 | ✅ | coarse at 820 px; no dialog; toast « Absent » / « Venu », both 44 px; « Venu » → same toast shows « Remplir » only — `shots/C2-tablet-step1.png`, `C2-tablet-step2.png` |
| C3 | ✅ | tablet « Absent » → success toast; ap5 = 6 — `shots/C3-tablet-absent.png` |
| C4 | ✅ | coarse at 390 px; no dialog, no toast — `shots/C4-phone-390.png` |
| D1 | ✅ | `getByRole('button', { name: /^Plus tard$/ })` (e2e's locator) resolves on step 1 and closes it |
| D2 | ✅ | ap2 gone from the real `pending-reviews` and from `/appointments/to-close` |
| D3 | ✅ | the success toast carries no buttons |

## Findings

None.

## Probe fixes (same run, not source changes)

- First drive: phone and tablet pages laid out at 1440 px — a raw CDP `setDeviceMetricsOverride` is reset by
  Playwright's own viewport. Switched to `setViewportSize` + CDP touch only, and asserted `innerWidth`. Whole
  walk re-driven; all rows green.

## Observations (off-plan)

- At 820 px the toast (bottom-center of the viewport) overlaps the rail's last items — pre-existing toaster
  placement, unchanged here.
- C4 counted 2 `pending-reviews` calls while the phone page loaded; the counter is context-wide and the desk
  tab was still polling, so it is not attributable to the phone page.

## Test artefacts left in the dev database

Two patients « QA Présence musmpg17 » / « QA Présence musmshka », each with 5 appointments today 09:00–11:00 —
now `NoShow` or `Cancelled`, so none sits in anybody's popup queue or « À clôturer ».

**Status: GREEN**
