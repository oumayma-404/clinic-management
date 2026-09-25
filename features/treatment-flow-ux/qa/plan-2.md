# QA plan 2 — the gaps left by plan 1

**Written:** 2026-09-24 · **Fixtures:** `arrange-2.mjs` → `fixtures-2.json` (+ plan-1 `fixtures.json`)
**Driver:** `walk-2.mjs` · every row screenshotted and READ; nothing money/clinical is confirmed except where
noted (throwaway fixtures only).

| Tier | ID | Surface | Expected (observable) | Widths |
|---|---|---|---|---|
| Tablet | T1–T7 | plan (several acts), plan (one act), patient band, lists, booking, fiche (mixed), edit RDV | no horizontal scroll; nothing clipped; primary button visible | 820×1024, 1180×820 |
| Touch | C1–C4 | plan strip + popover + ⋯, fiche controls, booking strip + buttons, patient card button | `(pointer: coarse)` matches; every tapped control ≥ 44 px tall | 390, 820 coarse |
| Confirm | D1–D14 | Annuler le devis · Supprimer · Dupliquer · Changer de patient · Changer le praticien · Ne plus réclamer · Facturer · Remettre à faire · Détacher la note · Reprendre (arrêté) · Reprendre (non réclamé) · Rétablir · Arrêter (rendu d'abord) · Créer le devis | title = question, body = short bullets, the right destructive button; « Retour » leaves nothing written | 1440 (+390 for 3) |
| Windows | W1–W8 | séances editor (edit / +) · Tout modifier · Encaisser · échéancier fold · Modifier l'échéancier · échéance « Encaisser » · Déplacer (RDV in place) · catalogue séances dialog | opens, readable, footer reachable at 730 px; closes clean | 1440, 1536×730, 390 |
| States | S1–S6 | billed · stopped · not claimed · cancelled · several acts · six séances | badge + one main action + money fact right for the state; six séances scroll in their own box | 1440, 390 |
| Fiche | F1–F4 | mixed séance (own fee + treatment) · cheque fields · prescription médicament + examen · « À continuer » | two « Payé » rows told apart; cheque fields appear; prescription lines readable; no helper captions | 1440, 390 |
| Conflict | X1 | in-row price save after a peer edit | refusal banner offers « Recharger » | 1440 |
| Lists | L1–L4 | Traitements en cours (cards) · devis table (cards) · patient band + Reste à payer · À clôturer 3 tabs | words, no bare fractions, no h-scroll | 390, 1440 |
| Popup | P1 | post-visit prompt (real queue) | « Remplir la fiche de soins » wording | 1440 |
| Reception | R1–R3 | secretary: patient band, treatment page menu, booking a multi-séance act and SAVING it | no 403; a draft treatment is created (API) | 1440 |
| Dark | K1–K3 | plan, fiche, booking in dark theme | legible, no light-only colours | 1440 |

Not covered: a real tablet (emulated only); VoiceOver/TalkBack.
