# QA run 2 — Retard et report (« Reporter » moved out of the ⋯ in « À clôturer »)

**Date:** 2026-10-30 (09:55 Tunis)
**Cycle:** 2 — the owner asked for « Reporter » as a visible button (`visits/visit-closure-list.tsx` only)
**Driver:** walk.mjs with `ONLY=C1,A8,B7,D5` — the rows that read this component; nothing else imports it
**Environment:** isolated — API :5098 · web :3098 standalone build (rebuilt with the change) · postgres `clinic_pvl_qa`, shared with the owner's open test window (the walk books only yesterday's and tomorrow's « Test R… » visits)
**Status:** GREEN

| ID | Outcome | Evidence |
|----|---------|----------|
| C1 | ✅ | 320×640: ⋯ popover 247 px, « Reporter » row button 42–150 px, sheet fits, scrollWidth 320 · shots/run2-C1-popover-320.png |
| A8 | ✅ | ⋯ « Venu en retard » 10 → one PUT `{ status: Completed, lateArrivalMinutes: 10 }`; row asks « Fiche » |
| B7 | ✅ | the row's « Reporter » → form → colleague's move → « Recharger » → moved |
| D5 | ✅ | coarse 1024 / 1180: Venu · Absent · Reporter · ⋯ end inside the table box (987 ≤ 999, 1143 ≤ 1155); the row wraps « Retirer » under them · shots/run2-D5-a-cloturer-1180.png |

Gate after the edit: `tsc` 0 · `check:responsive` 81/81 · `npm run build` ✓.

## Findings

None.
