# QA run 1 — Séance au fauteuil

**Date:** 2026-11-18
**Cycle:** 1 of at most 3
**Plan:** features/seance-au-fauteuil/qa/plan.md (24 scenarios: A8 B7 C4 D7, B6 driven last)
**Driver:** features/seance-au-fauteuil/qa/walk.mjs (`RUN=1`; A1 re-driven alone with `ONLY=A1` after its probe fix)
**Environment:** isolated — api :5098 (this worktree's Debug build, started by me) · web :3098 `next dev` (mine) ·
postgres `clinic-postgres`, database **`clinic_seance_qa`** (a copy of `clinic_management` taken at the start; the
shared database is behind this branch's migrations and was not touched) · MinIO bucket `clinic-seance-qa`
**Roles:** the dentist `qa.doctor@ibnkhaldoun.test`, the desk `qa.secretary@ibnkhaldoun.test`, Mode discret by the admin
**Peer sessions live during the run:** clinic-management-f7 (busy — owns :5099/:3099, `seance-dictation`), anakin-9c
(idle), anakin-89 (shell). None shares this database or these ports.
**Widths looked at:** 320 · 390 · 1440 · 1536×730 (screenshots read: A1, A2, A6, B5, C1-320, D3, D6)
**Status:** GREEN (24/24, no product finding)

## Scenarios

| ID | Outcome | Evidence |
|----|---------|----------|
| A1 | ✅ | « ⋯ » → « Fiche de soins » = `/patients/<P1>?addRecord=1&appointmentId=<V1>`; shots/A1-quick-actions-1a.png |
| A2 | ✅ | footer « Enregistrer » + « Terminer la séance — 40,000 DT »; `#paid` = "" ; shots/A2-fiche-open-1.png |
| A3 | ✅ | stays open, « Enregistré à 19:47 »; 1 fiche; visit `InProgress` |
| A4 | ✅ | a save, then `GET /medical-documents/<id>/pdf`, `print()` called once; ordonnance on the fiche; still 1 fiche |
| A5 | ✅ | modal closed; visit `Completed`; no live note |
| A6 | ✅ | « Fauteuil Un1846 19:36 · à facturer 40,000 DT · Encaisser »; shots/A6-to-collect-1.png |
| A7 | ✅ | note 2026-0789 paid by cheque 7781234 BIAT; the pill went 9 → 8 with no reload |
| A8 | ✅ | reopened « Payé » = 40,000 (the desk's money); re-save: still 1 live payment |
| B1 | ✅ | « Tout payé » = 40,000; « Terminer » raised and paid the note at the chair |
| B2 | ✅ | `400 seance_has_note` « Cette séance a déjà un brouillon de note d'honoraires, créé depuis son rendez-vous … »; 1 note |
| B3 | ✅ | « note en brouillon, à émettre » → `/patients/<P3>?tab=invoices` |
| B4 | ✅ | the desk's correction refused (`correction_requires_doctor`); note untouched |
| B5 | ✅ | note 2026-0789 cancelled; 2026-0791 holds cheque 7781234, 40,000, same day; 10,000 owed; shots/B5-correction-1.png |
| B6 | ✅ | admin hid money: `GET /appointments/to-collect` → 403 for the desk, no pill. Reverted on the copy by SQL (`IsMoneyHidden=false`) |
| B7 | ✅ | « échéance du devis n° 2026-0957 · 100,000 DT » → installment payment 100,000 carries the fiche's id |
| C1 | ✅ | 320 and 390: no overflow (document, `main`, dialog); save + « Tout payé » in view; shots/C1-fiche-320-1.png |
| C2 | ✅ | 1536×730: the save button on screen without scrolling |
| C3 | ✅ | 390: the list is a sheet, « Encaisser » in view |
| C4 | ✅ | after four saves (A3, A4, A5, A8) and the correction: one act, the corrected price |
| D1 | ✅ | draft-only past séance asks « Émettre la note » → `/patients/<P5>?tab=invoices` |
| D2 | ✅ | « Remplir la fiche de soins » → `/patients/<P6>?addRecord=1&appointmentId=<V6>` |
| D3 | ✅ | RDV window: « Fiche de soins » in the header; « À encaisser 10,000 DT · note n° … »; shots/D3-rdv-window-1.png |
| D4 | ✅ | séance row « Prescrit : … » → preview → « Imprimer »: PDF fetched, `print()` once |
| D5 | ✅ | `/factures` note 2026-0791 → « Voir le PDF » → « Imprimer »: `print()` once, no error |
| D6 | ✅ | « Au fauteuil » card → `/patients/<id>?addRecord=1&appointmentId=<id>`; shots/D6-dashboard-1.png |
| D7 | ✅ | fiche saved mid-séance left V7 `Scheduled`; the job made it `Completed` after its 19:48 end. Run 1's first drive is confirmed by the audit: fiche at 18:36:56 (no status write), job `InProgress → Completed` at 18:38:11 |

## Findings

None.

## Observations (off-plan)

- An empty « Payé » shows its placeholder « 0,000 » in grey. It reads as « nothing taken », which is the meaning — not
  chased.
- « Corriger la note » from the fiche titles itself « Corriger la note de cette séance »: the fiche passes no note
  number to the preview. Pre-existing; not chased.
- The RDV window's récapitulatif warns of overlaps with this walk's own fixtures (« Chevauchement à 19:08 … ») —
  test data, not the product.

## Probe bugs fixed during the run (not product defects)

| Row | Symptom | Real cause |
|-----|---------|------------|
| A4 → 9 rows | « Page crashed » on every later doctor row | a real `print()` on the PDF frame crashed the headless renderer; `printPdfBlob`'s frame now gets a stand-in window whose `print()` is counted, and a crashed tab is replaced |
| D7 | « before=Completed » | the status was read at the end of the walk, long after the job ran; now read right after the fiche |
| A1 | href of another patient's visit | the first « Fiche de soins » link on the page, not the open menu's; then a positional click opened a neighbour's menu on overlapping blocks — scoped to the open menu and opened by focus + Enter |
| D2 | `href=null` | « Remplir la fiche de soins » is a button that navigates; pressed, then the URL read |
| D4 | ⏭ « no Ouvrir l'ordonnance » | the desktop row's control is « Prescrit : … » with the accessible name « Ouvrir l'ordonnance — … » |
| D6 | ⏭ « no chair card » | read after a fixed 3 s, before the day board loaded; now waits for the card |
