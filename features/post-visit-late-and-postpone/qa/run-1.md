# QA run 1 — Retard et report

**Date:** 2026-10-29 (23:16–23:30 Tunis)
**Cycle:** 1 of at most 3
**Plan:** features/post-visit-late-and-postpone/qa/plan.md (26 scenarios: A11 B7 C4 D4 → 41 checks)
**Driver:** features/post-visit-late-and-postpone/qa/walk.mjs (+ `probe-c1.mjs` re-measure of C1, scratchpad)
**Environment:** isolated — API :5098 (worktree `bin/Debug`, started by me 22:56, migration applied at boot) · web :3098 `next dev` (started by me) · postgres `clinic_pvl_qa` (copy of `clinic_conges_qa`; Google token nulled and SMS/WhatsApp URLs emptied in the copy only) · own headless Chrome. Shared :5000/:3000, `clinic_management` and the MCP profile untouched.
**Peer sessions live during the run:** clinic-management-95 (idle), clinic-management-3d (shell); tuto-videos' stack on :5099/:3099 (not touched)
**Widths looked at:** 320×640 · 390×844 · 667×375 (landscape) · 820×1024 · 1180×820 coarse · 1440×900 · 1536×730
**Status:** GREEN

## Scenarios

| ID | Outcome | Evidence |
|----|---------|----------|
| A1 | ✅ | 4 answers at the same top (416) inside the 448 px dialog, 1536×730 and 820×1024 · shots/run1-A1-1536x730.png |
| A2 | ✅ | R1 → 30/10 12:00, `Scheduled`, retard null, review gone; toast « Séance reportée · vendredi 30 octobre à 12:00 » |
| A3 | ✅ | `Cancelled`; motif « Reporté par le patient » (sql — not on the DTO); toast « Ajouté à la liste d'attente » |
| A4 | ✅ | 15 saved at once, statut stays `AwaitingClosure`, pill « Retard de 15 min »; « Aucun » → null · shots/run1-A4-retard-noted.png |
| A5 | ✅ | booked end +70 s: `InProgress`, not on « À clôturer », no prompt · booked end + 5 +75 s: `AwaitingClosure`, prompt due · prompt opens on the fiche step with « Retard de 5 min » |
| A6 | ✅ | « +15 » on the block at 1440 and 390; name « … arrivé avec 15 min de retard … » · shots/run1-A6-block-390.png |
| A7 | ✅ | En cours: Reporter + Retard · Terminé: Retard only · Planifié: Reporter only · « 20 min » → PUT `{ lateArrivalMinutes: 20 }` alone |
| A8 | ✅ | one PUT `{ status: Completed, lateArrivalMinutes: 10 }`; row now asks « Fiche » |
| A9 | ✅ | 15 shown → 20 saved · « Aucun » sends null · Absent hides the chips, save → `NoShow`, retard erased |
| A10 | ✅ | Absent → Terminé on a free slot; on a re-booked one « Enregistrer sur un créneau déjà pris ? » → Continuer → Terminé |
| A11 | ✅ | coarse 1180: toast Venu 44 · Absent 44 · Reporter 44 px; Reporter → dialog · phone 390 coarse: nothing |
| B1 | ✅ | « Ce créneau est déjà réservé pour ce praticien (30/10 09:00–09:30). » → « Reporter quand même » → moved |
| B2 | ✅ | « … Le vendredi, le cabinet est ouvert de 09:00 à 17:00 … » → moved anyway |
| B3 | ✅ | « Retour » → the four answers |
| B4 | ✅ | `{ NoShow, 15 }` → 400 « Un retard ne se note que pour un patient venu … » |
| B5 | ✅ | Planifié + retard → 400; 241 → 400 « Le retard doit être compris entre 1 et 240 minutes. » |
| B6 | ✅ | forced 500 on the cancel → error toast, form open; retry → one waiting-list entry, not two |
| B7 | ✅ | colleague's move → « Cet enregistrement a été modifié par quelqu'un d'autre … » + Recharger → subject 15:30, typed 14:00 kept → moved · shots/run1-B7-conflict.png |
| C1 | ✅ | 320×640: popover 248 px, scrollWidth 320; sheet fits, last button 580–616 · 667×375: sheet scrolls 13 px, last button reachable |
| C2 | ✅ | duration-only save sends no `lateArrivalMinutes` key; retard 10 kept |
| C3 | ✅ | one review row for R2 after the retard |
| C4 | ✅ | « Reporter » at y=486 in a 730 px window |
| D1 | ✅ | reactivation into a taken slot → 400 `slot_taken`, French; with override → 200 |
| D2 | ✅ | « Plus tard » closes; prompt stays away after a reload |
| D3 | ✅ | edit dialog's existing taken-slot confirmation → saved |
| D4 | ✅ | CSV column « Retard (min) »; R14 → 10 |

`verify-schema` on the copy before and after the migration: identical apart from the clock-drift sign.

## Findings

None.

## Observations (off-plan)

- The prompt's sentence wraps « venu » and « ? » onto separate lines at 448 px (« … Le patient est-il venu ⏎ ? »): `PRESENCE_QUESTION` uses a plain space before « ? ». Pre-existing (in `main`), not chased.

## Probe bugs fixed during the run (not product defects)

| Row | Symptom | Real cause |
|-----|---------|------------|
| all browser rows | every page on `/login` after a successful sign-in | the script navigated while the page's session refresh was in flight; the dropped rotated cookie made the next load present a spent one and the server revoked the session. Every navigation now waits for `networkidle` |
| setup | `slot_taken` on the first booking | the void attempt's visits held the slots; setup now cancels earlier runs' « Test R…-NNNN » visits |
| A3 | « no motif » | `AppointmentDto` carries no `cancellationReason`; read from the row instead |
| C1 | last button looked clipped at 320 | screenshot taken during the sheet's slide-in; re-measured settled at 320×640, 390×844 and 667×375 |
