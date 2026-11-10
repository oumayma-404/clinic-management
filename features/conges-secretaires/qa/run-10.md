# QA run 10 — Congés · every gap covered

**Date:** 2026-11-07 · demo stack (`clinic_pointage_demo`, local) · Chrome 9335 / 9336 (the owner's windows untouched)
**Status:** GREEN — 145 ✅ · 0 ❌ · 1 ⏭ (G1, pre-existing: no « Votre secrétaire » step left in this cabinet)

| Rows | Evidence |
|---|---|
| A1–A22, B1–B6, C1–C5, D1–D3, F4 | the whole congés plan, with A18's absence dated « the two days before today and today » |
| A11 | the acceptance e-mail states « Congé payé restant en 2026 : 21,5 jours » — the same fold as the card |
| K1–K7 | rule, starting balance, days ±, a line cancelled, refusals, her read-only page, 320 / 1536×730 |
| K8 | « 2 j par mois » from 1 January: Acquis 22 j (11 credits, one « Acquis — <mois> » line each), 20 left — `shots/K8-monthly-from-january.png` |
| K9 | a rule cancelled with a motif: struck through, the card claims no balance |
| K10 | 31 December « perdus »: 2025 read back acquis 20 · pris 5 · perdus 15 · reste 0, relevé ends −15; 2026 opens at « Droit 2026 » |
| K11 | « reportés »: 2026 opens « Report de 2025 » 15 j, 33 left — `shots/K11-carried.png` |
| K12 | ended 2025 owing: « Dépassement reporté de 2025 » −3 j, nothing « perdu » |
| K13 | her « jours restants » 8 → 9 on the same page, no reload, when the admin adds a day |
| K14 | `(pointer: coarse)` confirmed; account buttons, relevé ✕, rule dialog fields/choices/buttons ≥ 44 px — `shots/K14-touch-820.png` |
| M1 | the database update on a throwaway copy with real values (progress.md) |

**Gates after the last edit:** unit suite 6772 ✅ · `check:responsive` 86/86 · `tsc` · `next build` · `verify-schema` clean
for the new table · `has-pending-model-changes`: none.
