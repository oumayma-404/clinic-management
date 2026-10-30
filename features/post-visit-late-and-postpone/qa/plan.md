# QA plan — Retard et report

**Written:** 2026-10-29
**Change under test:** `feature/post-visit-late-and-postpone` (worktree, uncommitted, base `main` 23ec0cfe)
**Rendering files in the diff:** 8 — post-visit-review-popup, appointment-postpone-dialog (new), appointment-late-arrival-chips (new), appointment-quick-actions, visits/visit-closure-list, edit-appointment-dialog, appointment-calendar, appointment-merge
**Blast-radius table:** progress.md (19 rows; `must re-test`: #7, #9, #18 + #4 job residuals)
**Budget:** small feature, 11 ACs → tiers A, B, C (reachable), D · 26 scenarios
**Stack:** isolated — API :5098 + `next dev` :3098 on `clinic_pvl_qa` (a copy of `clinic_conges_qa`, main's schema). QA admin, clinic Ibn Khaldoun. Own test patient (no phone → no reminder can leave). Google token nulled and SMS/WhatsApp URLs emptied **in the copy only**.

Test records (created over the API, all on today's date unless said): **R1** popup layout + Reporter · **R2** Venu + retard · **R3** liste d'attente · **R4** « À clôturer » ⋯ · **R5** live visit (start −3 min, 5 min) · **R6/R7** Absent → Terminé free / taken · **R8** blocker in tomorrow 11:00 · **R9** stale-version target · **R10/R11** reactivation into a taken slot.
The pending-review queue is served filtered to the one test visit a scenario needs (route on `pending-reviews`), so the dev data's other reviews never interleave.

## Tier A — happy path (one per AC)

| ID | AC | Surface | Steps | Expected (observable) | Layer | Widths |
|----|----|---------|-------|-----------------------|-------|--------|
| A1 | 1 | popup | R1 pending, `/appointments` | 4 buttons Plus tard · Reporter · Absent · Venu on ONE row (same `top`), all inside the dialog box | browser | 1536×730, 820×1024 (fine pointer) |
| A2 | 2 | popup → Reporter | Reporter → « Demain » → 10:00 → Reporter | toast « Séance reportée »; R1 = tomorrow 10:00 local, `Scheduled`, retard null; R1 not in pending-reviews; dialog closed | browser + api | 1536×730 |
| A3 | 3 | popup → Reporter → liste | R3: Reporter → « Mettre en liste d'attente » | toast « Ajouté à la liste d'attente »; R3 `Cancelled`, motif « Reporté par le patient »; 1 active waiting-list entry for the patient | browser + api | 1536×730 |
| A4 | 4 | popup fiche step | R2: Venu → « Noter un retard » → 15 | toast « Retard de 15 min noté. »; R2 retard 15, statut unchanged (`AwaitingClosure`); then « Aucun » → null; then 15 again | browser + api | 1536×730 |
| A5 | 5 | agenda ⋯ live | R5 `InProgress` → ⋯ → Retard 5 | R5 retard 5; at booked end +1 min still `InProgress`, not in `to-close`, no pending review; at booked end +5 +1 min `AwaitingClosure` + review pending; popup opens on the fiche step (no presence buttons) | api + browser | 1440 |
| A6 | 6 | agenda block | R2 (retard 15) in Jour view | block shows « +15 »; its accessible name contains « arrivé avec 15 min de retard »; same at phone width | browser | 1440, 390 |
| A7 | 7 | agenda ⋯ | open ⋯ on R5 (En cours), R1 (Planifié), R2-like Terminé | En cours: « Reporter… » + « Retard »; Planifié: « Reporter… », no « Retard »; Terminé: « Retard », no « Reporter… » | browser | 1440 |
| A8 | 8 | « À clôturer » | R4 row → ⋯ → « Venu en retard » 10 | R4 `Completed` + retard 10 (one PUT); the row's step moves off « Venue » | browser + api | 1440 |
| A9 | 9 | edit dialog | R2: retard 15 → 20, save; reopen → « Aucun », save; reopen → statut Absent | API 20, then null; with Absent chosen the chips disappear; after save R2 `NoShow`, retard null | browser + api | 1440 |
| A10 | 10 | edit dialog | R6 Absent → Terminé (free slot); R7 Absent with R7b booked in its slot → Terminé | R6 `Completed`; R7: the named clash sentence + « quand même » → `Completed` | browser + api | 1440 |
| A11 | 11 | tablet toast | coarse pointer 1180×820, R-tab pending | toast with Venu · Absent · Reporter, each ≥ 44 px tall; Reporter → dialog « Reporter la séance »; phone 390 coarse: no toast, no dialog | browser | 1180×820, 390×844 |

## Tier B — alternate paths

| ID | Surface | Steps | Expected (observable) | Layer | Widths |
|----|---------|-------|-----------------------|-------|--------|
| B1 | Reporter | move into R8's slot (tomorrow 11:00, same practitioner) | the server's clash sentence in the form + button « Reporter quand même »; press → moved | browser + api | 1440 |
| B2 | Reporter | keep the booked hour (late evening = closed) on « Demain » | hours refusal + « Reporter quand même » → moved | browser + api | 1440 |
| B3 | popup Reporter | « Retour » | back to step 1, the four buttons again | browser | 1536×730 |
| B4 | API | `{status: NoShow, lateArrivalMinutes: 15}` on an AwaitingClosure visit | 400, French, retard unchanged | api | — |
| B5 | API | retard on a `Scheduled` visit; retard 241 | both 400 | api | — |
| B6 | liste d'attente retry | cancel PUT forced to fail once → press again | error toast, dialog stays; second press succeeds; exactly ONE waiting-list entry | browser + api | 1440 |
| B7 | Reporter 409 | open Reporter (agenda ⋯) on R9, move R9 +30 min over the API, then Reporter | banner with the conflict sentence + « Recharger »; Recharger → subject line shows the new time; Reporter → moved | browser + api | 1440 |

## Tier C — edge cases

| ID | Case | Steps | Expected (observable) | Layer | Widths |
|----|------|-------|-----------------------|-------|--------|
| C1 | 320 px | Reporter dialog (agenda ⋯) and « À clôturer » ⋯ | `scrollWidth ≤ 320`; every button inside the viewport | browser | 320 |
| C2 | tri-state absent | edit dialog on a visit with retard: change notes only, save | PUT body has no `lateArrivalMinutes`; retard kept | browser + api | 1440 |
| C3 | no 2nd popup | after A4 | still exactly one review row for R2 | api | — |
| C4 | 730 px tall | Reporter step inside the popup | « Reporter » button visible without scrolling | browser | 1536×730 |

## Tier D — regression sweep

| ID | Blast row | Area | Expected (observable) | Layer | Widths |
|----|-----------|------|-----------------------|-------|--------|
| D1 | #7 | reactivate R10 (cancelled) into R11's slot | 400 `slot_taken`, French; with `allowOverlap` → `Scheduled` | api | — |
| D2 | #9 | « Plus tard » | popup closes; after reload it stays closed (snooze) | browser | 1536×730 |
| D3 | #12 | edit dialog moves a visit into a taken slot | the existing overlap confirmation still appears and the save lands | browser + api | 1440 |
| D4 | #16 | CSV export of today | header has « Retard (min) »; R2's row carries its retard | api | — |

Added 2026-10-30 after the owner asked whether the blast radius was closed — consumers the first table missed:

| ID | Blast row | Area | Expected (observable) | Layer | Widths |
|----|-----------|------|-----------------------|-------|--------|
| D5 | #11 | « À clôturer » table at tablet widths, new ⋯ beside Venu · Absent | Venu, Absent and ⋯ fully inside the table box and the window; the table needs no sideways scroll to reach them | browser | 1024×768, 1180×820 coarse |
| D6 | #6 | agenda ⋯ on an « Absent » block | « Terminé » offered; free slot → `Completed`; slot given away → error toast naming the clash, statut stays `NoShow` | browser + api | 1440 |
| D7 | #13 | edit dialog « Recharger » with a retard | user picks 20, colleague saves 30 → 409 → Recharger → 30 shown, « Statut » named as taken over; save lands without a 409 | browser + api | 1440 |
| D8 | #9, #14 | e2e `booking-browser`, `carried-draft-browser`, `appointment`, `concurrency` specs on this stack | all pass | e2e | — |

Added 2026-10-30 with the free-slot list (`blueprint-free-slots.md`, blast rows F1–F9). A2, B1, B2, B7 now reach a typed
time through « Autre heure… »; A2 picks a **listed** time instead of typing one.

| ID | Blast row | Area | Expected (observable) | Layer | Widths |
|----|-----------|------|-----------------------|-------|--------|
| A2 | F6 | popup → Reporter → « Demain » chip (« N libres ») → listed « 12:00 » | « 12:00 » checked; save lands with no refusal; R1 = tomorrow 12:00; every footer button inside the dialog | browser + api | 1536×730 |
| F2-footer | F6 | the popup's refusal state (« Retour » · « Mettre en liste d'attente » · « Reporter quand même ») | every footer button inside the dialog box — none clipped (run 3 found « Retour » cut to « tour ») | browser | 1536×730 |
| B1-list | F3, F6 | Demain's grid | R8's 09:00 is **not** listed | browser | 1440 |
| F2 | F3, F6 | a congé day among the chips | chip reads « Congé »; picking it says « Le praticien est en congé ce jour-là. », lists no time, « Reporter » disabled; a typed 14:00 is refused by name with « Reporter quand même »; nothing moves | browser + api | 1536×730 |
| F3 | F6 | free-slot read forced to 500 | « Les heures libres n'ont pas pu être chargées. » + Réessayer; the time field is shown directly; Réessayer → chips + grid | browser | 1536×730 |
| A11-slots | F6 | the agenda dialog on a coarse tablet | every day chip and time button ≥ 44 px | browser | 1180×820 coarse |
| F4 | F6 | calendar → a day ~3 weeks out (AC-14) | a read `from=` that day; its chip is checked; « Heures libres · {day} » lists times | browser | 1536×730 |

Added 2026-10-30 after the owner asked again for the blast radius — the list is read once, so it can go stale:

| ID | Blast row | Area | Expected (observable) | Layer | Widths |
|----|-----------|------|-----------------------|-------|--------|
| F5 | F6 | a listed time goes past while the form stays open (start injected 1–2 min ahead, pressed once past) | **no PUT** (the server accepts a past time); « Cette heure est déjà passée. »; the list re-read; « Reporter » disabled | browser | 1536×730 |
| F6 | F6 | a listed time taken after the read (R8's 09:00 injected into the first read) | the named refusal + « Reporter quand même »; the list re-read and 09:00 no longer listed; nothing moves | browser | 1536×730 |
| B1-seen · F2-seen · B7-seen | F6 | the refusal / the 409 banner after a press | **on screen** — inside the viewport and topmost at its centre (run 5's screenshots showed it scrolled out of the body, below the grid, while a DOM « visible » passed) | browser | 1536×730, 1440×900 |
| F7 | F3, F6 | a cabinet with no hours saved (every day `Unrestricted`, edited read) | « Aucun horaire n'est enregistré pour ce cabinet. »; calendar + typed time; no chips, no grid, no « Voir les heures libres »; « Reporter » usable | browser | 1536×730 |

## Not covered, and why

| Area | Why not |
|------|---------|
| #18 PC de secours copy | needs a paired PC; the row feed is metadata-driven and the column is a plain nullable `int` |
| `e2e/specs/booking-browser.spec.ts` | needs CI's bootstrapped database; « Plus tard » keeps its exact name (`e2e/lib/goto.ts`) — D2 covers it by name |
| agenda drag-move | the gesture code is untouched; the block only gained a pill outside the drag's `dataset` contract |
| « Remplir la fiche » / « Facturer » | clinical + money writes outside this change (verification.md § 7) |
