# QA run 8 — Congés · « Droit à congé » (rule, starting balance, relevé)

**Date:** 2026-11-06 (15:30 Tunis)
**Environment:** demo stack — API :5096 (worktree, migration `AddStaffLeaveAllowanceEntries` on `clinic_pointage_demo`
only) · web :3096 `next dev` · Chrome 9335 / 9336 (the owner's windows on 9333 / 9334 untouched) · her 2 congé requests
kept aside and restored after.
**Status:** GREEN — 123 ✅ · 0 ❌ · 1 ⏭ (G1, pre-existing: this cabinet has no « Votre secrétaire » step left)

| Rows | Outcome | Evidence |
|---|---|---|
| A1–A22, B1–B6, C1–C5, D1–D3 | ✅ | A4 sets « 24 j par an » from 1 January through « Définir la règle »; A16 « 19,5 jours restants · Prévus 4,5 j »; A19 « 2 jours prévus » for the secretary with no droit. C5's last check re-driven alone after a probe fix (the balance card's two lines were counted as congés: 9 instead of 7) |
| K5 | ✅ | 40 j par mois refused before sending, nothing written; days added with no motif refused |
| K1 | ✅ | monthly rule from « 1er décembre », toast, card « prochain : +2 j le 1er décembre », still 19,5, SQL keeps the yearly rule and appends the monthly one — `shots/K1-rule-dialog.png` |
| K2 | ✅ | starting balance 10 today → « 5,5 jours restants »; relevé « Solde de départ » → « Congé prévu » lines → « solde 5,5 j »; no « Droit 2026 » line |
| K3 | ✅ | « Jours retirés · Rattrapage » −1,5 j → 4 |
| K4 | ✅ | confirm names the line, empty motif refused, back to 5,5, struck through « annulée : Erreur de saisie » in the relevé and the account — `shots/K4-cancelled.png` |
| K6 | ✅ | her `/conges`: 5,5, « Total pris depuis le début », next credit, read-only relevé — `shots/K6-secretary.png` |
| K7 | ✅ | 320×640 and 1536×730: no sideways scroll, « Enregistrer la règle » on screen — `shots/K7-rule-*.png` |

Widths looked at: 320×640 · 560×830 · 986/1000×830 · 1440×900 · 1536×730.
