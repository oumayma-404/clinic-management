# Feature Specification: Retard et report

**Status:** APPROVED
**Type:** Small
**Created:** 2026-10-29
**Scope:** FE + BE
**Feature:** The « Séance terminée » popup, the agenda and « À clôturer » can record a late arrival and postpone a visit.

Approved by the owner on 2026-10-29 (« go with A », « implement now »). What changes, the decisions and the
blast radius are in [`blueprint.md`](blueprint.md); this file only records the approval and the acceptance criteria.

## Acceptance Criteria

- **AC-1:** The desktop popup's first step shows **Plus tard · Reporter · Absent · Venu** on one line at 448 px.
- **AC-2:** « Reporter » opens a date + heure form (next open days as chips, a calendar, a 24 h time). Saving moves the
  RDV (statut « Planifié », the popup waits for the new slot, the rappel follows). Never counted as « Absent ».
- **AC-3:** « Mettre en liste d'attente » adds the patient to the Liste d'attente, then cancels the RDV with the motif
  « Reporté par le patient ». A retry after a failure never adds a second entry.
- **AC-4:** After « Venu », « Noter un retard » offers 5 · 10 · 15 · 20 · 30 · Autre minutes; a choice is saved at
  once, writes no statut, and can be changed or removed (« Aucun »).
- **AC-5:** A retard never moves the RDV. Recorded during the séance (agenda ⋯), it pushes « Séance passée », « À
  clôturer » and the popup to booked end + retard; the popup then opens on the fiche step. No second popup.
- **AC-6:** The agenda block shows « +15 »; its accessible name says « arrivé avec 15 min de retard ».
- **AC-7:** The agenda ⋯ menu offers « Reporter… » (Planifié / En cours / Séance passée) and a « Retard » row
  (En cours / Séance passée / Terminé).
- **AC-8:** « À clôturer » presence rows read Venu · Absent · Reporter · ⋯; the ⋯ holds « Venu en retard » (statut
  Terminé + retard, one write). *(Amended 2026-10-30 by the owner: « Reporter » is a visible button, not in the ⋯.)*
- **AC-9:** The edit dialog shows the retard chips under the statut while the chosen statut can carry one, sends the
  retard only when it changed, and « Recharger » reconciles it with the statut.
- **AC-10:** « Absent » can become « Terminé »; if the slot was given to someone else, the clash is named (`slot_taken`)
  with the « quand même » override. Reactivating a cancelled RDV into a taken slot is named the same way.
- **AC-11:** On a tablet the toast carries Venu · Absent · Reporter at 44 px; « Reporter » opens the form as a dialog.
  On a phone nothing changes (no prompt).

Added 2026-10-30 (the owner asked to see the free slots when choosing; option A of
[`blueprint-free-slots.md`](blueprint-free-slots.md), « implement »). AC-2's « next open days as chips » is replaced by:

- **AC-12:** « Reporter » lists the practitioner's **free times**: day chips over the next 14 days with « N libres »,
  « Congé » or « Complet » (closed days left out), and the chosen day's starts grouped « Matin » / « Après-midi ».
  The list follows the save's own rules (the doctor's hours or the clinic's, the break, the congés, every visit
  holding a slot, the visit's own length, the visit itself excluded), so a listed time saves with no refusal.
  The picked time stays highlighted; the button reads « Reporter » (a time in its label did not fit the popup).
- **AC-13:** « Autre heure… » keeps the typed 24 h time and its named refusals (« Reporter quand même »). A day with no
  free time says why (« Le praticien est en congé ce jour-là. », « Aucune heure libre ce jour-là. », « Le cabinet est
  fermé ce jour-là. »). With no hours configured, or a failed read, the typed time is shown directly — a failed read
  says so with « Réessayer », never « aucune heure ».
- **AC-14:** A date past the 14 days, picked in the calendar, loads the 14 days from it.
