# QA run 5 — after /apply-review-fixes

**Date:** 2026-10-20 · same plan + `walk.mjs` (all earlier rows kept) · isolated stack rebuilt with the review fixes
(API :5099, web :3098, DB `clinic_conges_qa`).

New rows, one per review fix:

| Row | Proves | Result |
|-----|--------|--------|
| RV-blocks | paging ‹ › onto a congé week keeps the 10:00 block on its hour line (Critical) | ✅ 0 px off |
| RV-leaves-failed | a failed congé read shows « Les congés des praticiens n'ont pas pu être chargés » + Réessayer | ✅ |
| RV-strip | phone Jour (390): Mon/Tue of the week read « praticien en congé », Wed open | ✅ |
| RV-mois | desktop Mois at 1536×730: congé days labelled and named, no visit chip half-drawn | ✅ |
| RV-profil | a congé saved in another tab leaves « Mon profil » and its typed ordre untouched | ✅ |
| F1-bar | now also 768 / 820 / 1024, filtered and not: no overflow anywhere; under a 40rem bar the title has its own line | ✅ |

A1's 820 expectation updated: the bar is two lines there by design now (title line + controls), height ≤ 100 px.

**Result: 52 ✅, 0 ❌, 0 ⏭ — GREEN.** Gates after the last edit: check:responsive 81/81 · tsc · build · unit suite 5 985 passed / 0 failed.
