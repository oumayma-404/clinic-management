# QA plan — Treatment flow UX (18 items)

**Written:** 2026-09-24
**Change under test:** working tree on `feature/security-remediation` (uncommitted), `web/` + `e2e/` only
**Rendering files in the diff:** ~35 — booking dialogs + picker, fiche modal + record/*, treatment workspace + dialogs, patient band, lists, catalogue, odontogram labels
**Blast-radius tables:** `features/treatment-flow-ux/blast-radius-{booking,fiche,plan,patient}.md`
**Budget:** large / shared primitive / money + clinical → tiers A–D
**Fixtures:** `qa/fixtures.json` from `qa/arrange.mjs` (throwaway patients `QAU* Flow <ts>` only)

## Tier A — happy path

| ID | Surface | Steps | Expected (observable) | Layer | Widths |
|----|---------|-------|-----------------------|-------|--------|
| A1 | `/treatment-plans/{main}` T1 | open | title « Couronne · dent 16 »; badge « En cours »; chip « Devis n° {number} »; strip shows « faite le », « prévue le », « à planifier »; exactly one large primary button; no « Retour aux plans », no « PROCHAINE SÉANCE », no « séance sur 3 faite » | browser | 1536×730, 390 |
| A2 | same, T2 | tap « Empreinte » on the strip; then tap « Préparation » | popover 1: « prévue le » + « Voir le RDV »; popover 2: « faite le » + « Voir la fiche » + « Remettre à faire » | browser | 1440 |
| A3 | same, T4 | read « L'argent » | figures « Prix » 300 · « Payé » 50 · « Reste à payer » 250; one « Encaisser »; échéancier folded (« Échéancier ») | browser | 1440 |
| A4 | same, T5 | open « ⋯ »; open « Arrêter »; press « Retour » | menu group labels Document / Modifier / Fin du traitement; stop dialog body is bullets (≤ 30 words); nothing written | browser | 1440 |
| A5 | same, T3 | type a remise of 20 on the act, see the bar, Enregistrer | bar « 1 modification »; after save: Prix 280 · Reste à payer 230; step 2 still has `minDaysAfterPrevious = 7` | browser + api | 1440 |
| A6 | `/patients/{main}` L1 | read the treatment card, press its primary button | card: « Couronne · dent 16 », dots + « Empreinte prévue le … », « Reste à payer »; button « Planifier : … » opens « Nouveau rendez-vous » with the act row already there (no navigation) | browser | 1440, 390 |
| A7 | booking for main patient, R1+R2 | in the dialog from A6 | « Continuer un traitement » card(s) with « Planifier : » and no devis number / price; the act row shows the strip with « ce RDV »; tag « Payé sur le traitement »; no « Chiffré sur le devis » paragraph | browser | 1440 |
| A8 | booking for plain patient, R2 | add « Couronne / bridge » from the catalogue | strip with « ce RDV » on séance 1; « Prix du traitement » field; « Séances » and « En 1 séance » buttons; no « Traitement en N séances. Ce rendez-vous est la 1re » text | browser | 1440, 320 |
| A9 | booking for cont patient, R3 | open the dialog | card « Soin de carie… » + « Non terminée le » + « Continuer »; after Continuer the row reads « suite du » with « Nom de la séance » and « Prix du reste »; no « C'est la suite d'une séance précédente ? » link, no 2nd dialog | browser | 1440 |
| A10 | edit RDV (main séance 2), R4 | open it, read the footer, open « ⋯ » | footer: « ⋯ », « Fermer », « Enregistrer » only; menu: « Annuler le rendez-vous », « Supprimer » | browser | 1536×730 |
| A11 | fiche for today patient, F1–F4 | open « Enregistrer la fiche » / fiche for séance 2 | title « Fiche de soins · … »; band « Couronne · dent 16 » + strip with Empreinte current; « Changer » menu has « Sans traitement »; act tag « Payé sur le traitement »; footer « Payé aujourd'hui » + Prix / Déjà payé / Reste à payer; button « Enregistrer la séance » | browser | 1536×730, 390 |
| A12 | same, F3 | add « Détartrage » | tag « Ajouté au traitement »; « par dent » / « pour tout »; footer « Prix du traitement » old → new | browser | 1440 |
| A13 | same, save | remove Détartrage, Enregistrer la séance | toast; step 2 of today's plan has a `doneDate`; the plan still lists 3 steps | browser + api | — |
| A14 | `/treatment-plans` lists | open the page | « Traitements en cours » list row for main: dots + words, no « étape N / M »; table: name « Couronne · dent 16 », « Devis n° … » | browser | 1440, 390 |
| A15 | `/a-cloturer` « Suites à planifier » | press « Planifier la suite » on the cont row | ONE dialog: « Nouveau rendez-vous » with the continuation row already present | browser | 1440 |

## Tier B — alternate paths

| ID | Surface | Steps | Expected (observable) | Layer | Widths |
|----|---------|-------|-----------------------|-------|--------|
| B1 | `/treatment-plans/{draft}` | open | badge « À commencer »; chip « Pas de devis »; « Créer le devis » reachable; no « Sans devis » badge; no « Aucun devis édité… » caption | browser | 1440 |
| B2 | `/patients/{draft}` | read the card | no « À accepter » headline; primary « Planifier : Préparation » | browser | 1440 |
| B3 | A8 dialog | press « En 1 séance » | strip disappears; « En 3 séances » offered back | browser | 1440 |
| B4 | A10 menu | « Annuler le rendez-vous » | a confirmation opens (alertdialog); press « Non » / Retour — nothing written | browser | 1440 |
| B5 | `/treatment-plans/{main}` T5 | open « Supprimer » (if offered) or « Annuler le devis » | body in bullets, destructive button; Retour | browser | 1440 |

## Tier C — edge cases

| ID | Case | Steps | Expected (observable) | Layer | Widths |
|----|------|-------|-----------------------|-------|--------|
| C1 | 320 px floor | treatment page, booking (A8), fiche (A11) at 320×720 | `document.documentElement.scrollWidth ≤ 320`; primary buttons fully visible | browser | 320 |
| C2 | owner's laptop | booking (A7) and fiche (A11) at 1536×730 | the footer's primary button is inside the viewport | browser | 1536×730 |
| C3 | save twice | A5 then reopen the page | remise 20 still shown; séance 2 delay still 7 | api | — |

## Tier D — regression sweep (from the blast-radius tables)

| ID | Blast-radius row | Area | Expected (observable) | Layer | Widths |
|----|------------------|------|-----------------------|-------|--------|
| D1 | booking #13 quick actions | agenda « ⋯ » on an RDV | menu opens with statuses | browser | 1440 |
| D2 | patient #8 table host 2 | patient « Traitements » tab | table renders the main plan with « Voir le traitement » in its menu | browser | 1440 |
| D3 | patient #12 catalogue | `/procedure-types` | a protocol cell draws the strip; the edit button opens the séances dialog | browser | 1440 |
| D4 | plan #9 installment modal | patient page « Reste à payer » → « Encaisser » | the payment modal opens (Retour, nothing written) | browser | 1440 |
| D5 | all | console | no `pageerror` on any page walked | browser | — |
| D6 | gate | `check:responsive` + `tsc` + `build` + `check-coverage` | all green (check-coverage: same output as HEAD) | cli | — |

## Not covered, and why

| Area | Why not |
|------|---------|
| « Facturer », « Arrêter » confirm, « Ne plus réclamer », « Supprimer » confirm | irreversible money/clinical writes; dialogs are opened and read, never confirmed |
| Stop « refund first » branch | needs a devis with money and no work; read by source (N40 still green) |
| Physical tablet / coarse pointer | the dev browser cannot emulate `coarse:` without CDP; 44 px rules checked by source |
