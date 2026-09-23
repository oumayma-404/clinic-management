# QA run 3 — wave 3 (money) · 2026-09-23 · GREEN

Arranged by `qa/arrange-3.mjs`, driven by `qa/walk-3.mjs` (headless Chrome, 1440×900, fresh « QAG Flex » patient).
API pid 29736 (restarted by clinic-management-8f after a6's walk, with a6's go), web = bf's `next dev`.
Peers live: clinic-management-a6 (its own walk on the fiche, finished before the final run).

| ID | Result | Evidence |
|----|--------|----------|
| G3a | ✅ | « Rendre au patient ? » · « 100,000 DT sont à rendre » · rendu −100 today in cash · receipt kept on 2026-09-20 · « rendu au patient » on the échéancier |
| G3b | ✅ | « Retour » → 300 / 300 unchanged, remise dialog still open |
| G3c | ✅ | « Rendre 100,000 DT et arrêter ? » → Stopped, 0 collected |
| G3d | ✅ | la caisse extrait lists « Rendu au patient » |
| G2 | ✅ | dates kept, rows 100 · 100 · 50 |
| G8 | ✅ | no « Reste » figure, no « Encaisser » |
| G1 | ✅ | a typed 0 is saved as 0 |
| G6 | ✅ | booking shows 250 (300 − 50 remise) |
| G5 | ⏭ | server rule — `G5_Redating_A_Fiche_Moves_Its_Devis_Payment_And_Its_Seance` |
| G4 | ⏭ | tooth picker not scripted — helper shared with the odontogram seed |
| R1 | ✅ | workspace figures render |

## Found and fixed during the run
1. The rendu sentence printed « 300.000 DT » (invariant culture) — read as three hundred thousand. `PlanRefund` pins fr-FR now.
2. The remise dialog's hint still said a remise below what was collected « est refusée » — removed (it is a rendu now).

## Probe bug (not product)
- The walk's API login reused the arrange script's TOTP code in the same 30 s window → no token, every read null. The walk now logs in after the browser, in a fresh window.

Gate: unit suite 4823 green (unfiltered) · tsc · check:responsive 72/72 · `next build` (scratch copy — a live `next dev` owns `web/.next`) ·
reconcile-money before/after: only the 8 written-off plans join the checks (0 DT on them); a +350 DT September delta was a peer's
payment recorded between the two runs (13:31:15), confirmed by SQL.
