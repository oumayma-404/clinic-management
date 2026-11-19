# QA plan — Séance au fauteuil

**Written:** 2026-11-18
**Change under test:** worktree `.claude/worktrees/seance-au-fauteuil`, branch `feature/seance-au-fauteuil` (uncommitted) on `origin/main` a3249a87
**Rendering files in the diff:** 22 — fiche modal, prescription section, preview + invoice PDF dialogs, agenda page + calendar, RDV window, quick actions, « Au fauteuil », patient page, post-visit popup, plan rows ×3, « À clôturer », « À encaisser » (3 new), « Facturer cette intervention », installment payment modal
**Blast-radius table:** `features/seance-au-fauteuil/blast-radius.md` (12 rows `must re-test` / `must change`)
**Budget:** large (money + clinical) → tiers A, B, C, D · 24 scenarios
**Stack:** isolated — API :5098 / web :3098 on the DB copy `clinic_seance_qa` (the shared DB is behind this branch's migrations)
**Roles:** the dentist = `qa.doctor@ibnkhaldoun.test`, the desk = `qa.secretary@ibnkhaldoun.test`, Mode discret = the admin (API, TOTP). Every fixture is a fresh patient « QA Fauteuil … ».

## Tier A — happy path (the dentist at the chair, then the desk)

| ID | Route / surface | Steps | Expected (observable) | Layer | Widths |
|----|-----------------|-------|-----------------------|-------|--------|
| A1 | `/appointments` (Jour) | « ⋯ » on today's running visit V1 | a « Fiche de soins » link whose href is `/patients/<id>?addRecord=1&appointmentId=<V1>` | browser | 1440 |
| A2 | that link | open it | the fiche opens on V1's act; footer has « Enregistrer » **and** « Terminer la séance — X »; « Payé » is empty | browser | 1440 |
| A3 | fiche | type a note, « Enregistrer » | the modal stays open, « Enregistré à HH:mm » shows; API: one fiche on V1, V1 not « Terminé » | browser + api | 1440 |
| A4 | fiche « Prescription » | add a médicament, « Imprimer l'ordonnance » | a fiche PUT then `GET /medical-documents/<id>/pdf`; modal still open; API: the fiche has an ordonnance, still one fiche | browser + api | 1440 |
| A5 | fiche | « Terminer la séance » | the modal closes; API: V1 « Terminé », no note raised (« Payé » empty) | browser + api | 1440 |
| A6 | `/a-cloturer` → « À encaisser » as the desk | — | the tab carries N; its list has V1's patient, « à facturer », amount = the fiche's total | browser | 1440 |
| A7 | the list → « Encaisser » | Chèque, n° 7781234, BIAT, confirm | API: a note issued and paid for the fiche, its payment a cheque n° 7781234 BIAT; the row leaves the list | browser + api | 1440 |
| A8 | patient page → reopen V1's fiche | read « Payé », then « Enregistrer la séance » unchanged | « Payé » shows the desk's amount; API: still exactly one live payment on the note | browser + api | 1440 |

## Tier B — alternate paths

| ID | Route / surface | Steps | Expected (observable) | Layer | Widths |
|----|-----------------|-------|-----------------------|-------|--------|
| B1 | new fiche on running visit V2 | « Tout payé », « Terminer la séance » | « Payé » = the total; API: a note raised and paid (the chair can still take the money) | browser + api | 1440 |
| B2 | API | draft note on visit V3 (RDV window shape: `appointmentId`, a line with no fiche), fiche saved on V3, then bill the fiche | `400` with code `seance_has_note`; no second note | api | — |
| B3 | « À encaisser » as the desk | V3's row (a draft typed 5 DT under the fiche) → « Encaisser » → « Facturer et encaisser » | the row reads « à facturer » at the FICHE's amount; the dialog names the draft it replaces; one note, the fiche's total, paid; the draft is gone; the row leaves | browser + sql | 1440 |
| B4 | API as the desk | change V1's act price with a `correctionReason` | refused, code `correction_requires_doctor`; the note untouched | api | — |
| B5 | V1's fiche as the dentist | raise the act's price, save → « Corriger la note » → « Corriger » | API: the old note cancelled, a new note holds the **same cheque** (n° 7781234, BIAT, same day, same amount) and owes the difference | browser + api | 1440 |
| B6 | Mode discret | the admin hides money (API) | `GET /appointments/to-collect` → 403 for the desk; no « À encaisser » tab on `/a-cloturer` | api + browser | 1440 |
| B7 | devis séance V4 (accepted devis, a late agreed échéance, séance today, fiche) | « À encaisser » → « Encaisser » → confirm | the row reads « échéance du devis n° … » at the late amount; API: the installment payment carries `dentalRecordId` = V4's fiche | browser + api | 1440 |

## Tier C — edge cases

| ID | Case | Steps | Expected (observable) | Layer | Widths |
|----|------|-------|-----------------------|-------|--------|
| C1 | 320 px floor | open V2-like fiche at 320 / 390 | no horizontal overflow (`main` and the dialog); both footer buttons fully inside the viewport; « Tout payé » visible | browser | 320, 390 |
| C2 | owner's laptop | fiche at 1536 × 730 | the footer's save buttons are inside the viewport without scrolling the page | browser | 1536×730 |
| C3 | « À encaisser » on a phone | open the tab at 390 | no sideways scroll; the row's « Encaisser » inside the viewport | browser | 390 |
| C4 | save, reopen, save again | A3 → A5 → A8 | the act and its price unchanged after the third save | api | — |

## Tier D — regression sweep (from the blast-radius table)

| ID | Blast-radius row | Area | Expected (observable) | Layer | Widths |
|----|------------------|------|-----------------------|-------|--------|
| D1 | #6 | « À clôturer », past visit V5 with a fiche and only a draft note → « Encaisser » → confirm | the dialog names the draft; one note from the fiche, the draft gone, the row answered | browser + sql | 1440 |
| D2 | #1, #14 | « À clôturer », past visit V6 without a fiche | « Remplir la fiche de soins » links to `addRecord=1&appointmentId=<V6>` | browser | 1440 |
| D3 | #13 | RDV window of V1 (after B5's correction) | the header has « Fiche de soins »; the money line reads « À encaisser 10,000 DT · note n° … » (the corrected price's difference) | browser | 1440 |
| D4 | #12 | patient page → V1's ordonnance preview → « Imprimer » | no error toast; the PDF was fetched | browser | 1440 |
| D5 | #12 | `/factures` → V1's note PDF → « Imprimer » | no error toast | browser | 1440 |
| D6 | #1, #14 | dashboard « Au fauteuil » (doctor) | a running visit's card opens `addRecord=1&appointmentId=` | browser | 1440 |
| D7 | #2 | the minute job | visit V7 ending in ~2 min gets a fiche mid-séance (API): it stays « En cours », then turns « Terminé » within 2 min of its end | api | — |
| D9 | owner, 2026-11-19 | the RDV window of a séance with no fiche (V6) | « Non facturé », and no « Facturer cette consultation » | browser | 1440 |
| D8 | owner, 2026-11-19 | `/appointments` as the desk and the dentist | the agenda is as it was: no « À encaisser » pill, no « à encaisser » in any block's name | browser | 1440 |

## Not covered, and why

| Area | Why not |
|------|---------|
| A real printer | headless Chrome; `window.print` is stubbed and its call counted |
| Tablet print hand-off (`coarse:` → system viewer) | needs a touch context's PDF viewer; `printPdfBlob`'s branch is unchanged code |
| 409 « Recharger » on the in-place save | covered by `fiche-merge` unit logic and N49; needs two writers on one fiche |
