# QA plan — salle d'attente, round 2

Scope: `features/salle-attente/round-2.md` (C1–C10, Q1–Q3, E1–E5) and its blast-radius table.
Stack: isolated — API `:5098` on the DB copy `clinic_sa_qa`, web `:3098` (production standalone build).
Accounts: `qa.secretary@…` (desk), `qa.doctor@…` (Dr Nadia Trabelsi). Cabinet de groupe (Nadia + Salma).
Driver: `qa/r2-walk.mjs` (one launch). Test patients are created per run (`Qa<run><x>`); this morning's demo
cards are never moved — a test patient that must be called first is put first with « Passer en premier ».

Money and clinical records: no fiche, no note, no payment is written. Board moves change the visit's status
only — that is the feature under test.

## Tier A — happy path

| ID | Case | Steps | Expected (observable) | Layer |
|---|---|---|---|---|
| A1 | Q1 real time | desk « Arrivé » p1 → dentist « Passer en premier » p1, « Patient suivant » → desk « Terminer » | Terminé card « HH:MM – HH:MM »; SQL `AppointmentDateTime` = call minute, `PlannedStartUtc` = booked slot | browser + sql |
| A2 | Q1 agenda « prévu » | desk agenda, day view | p1's block reads « prévu HH:MM » (booked time) | browser |
| A3 | Q2 average | board stat after A1 | « Attente moyenne » equals the API's `averageWaitMinutes`, not « — »; unchanged 35 s later | browser + api |
| A4 | Q3 two chairs | dentist turns « 2 fauteuils » on; p2 seated (Appeler), p3 then called | both p2 and p3 listed in « Mes 2 fauteuils », each with « Terminer »; p2 still `InChair` | browser |
| A4b | Q3 switch off (run 4) | chairs emptied → the switch clicked again | « Mes 2 fauteuils » gone; on a failed click the row names what covers the switch | browser |
| A5 | Q3 full | a third patient waits while two are seated | « Les deux fauteuils sont occupés » shown, disabled | browser |
| A6 | E1 « Pas là » | dentist calls p4 (urgent) → desk banner « Pas là » | p4 back in « En salle d'attente » tagged « Pas là · HH:MM »; next patient of that chair seated; toast « … n'est pas là … » | browser |
| A6b | E1 on the ring (run-1 F1) | dentist calls p22 (urgent) → desk staff-chat call-in notice « Pas là » | p22 back waiting, p23 seated, notice answered, toast « … n'est pas là · … est appelé·e » | browser |
| A7 | E2 urgent | desk menu « Passer en premier » on a waiting test patient | card first in its lane with « Urgent »; « Retirer « Urgent » » puts it back | browser |
| A8 | C6 other day | p5 has a RDV in 2 days; desk types his name | row « RDV <jour> » + « Le recevoir aujourd'hui »; press → p5 in « En salle d'attente »; SQL date = today | browser + sql |
| A9 | C6 undo | « Annuler » on A8's toast | SQL date back to the other day; card gone from today | browser + sql |
| A10 | C5 phone | desk types p6's phone digits (p6 has a RDV today) | p6 listed under « Rendez-vous d'aujourd'hui », not « Sans RDV » | browser |
| A11 | C7 seat unassigned | p7 (RDV with no dentist) waiting → desk « Faire entrer » | dialog « Quel praticien ? »; pick Dr Nadia → p7 au fauteuil; SQL `DoctorId` = Nadia | browser + sql |
| A12 | C10 congé | congé for Dr Salma today (API); p8 (Salma) waiting | card « Dr Salma Ben Youssef absent aujourd'hui » + « Changer de praticien » → Nadia → warning gone | browser + sql |
| A13 | E3 agenda | desk agenda day view | a waiting test patient's block says « en salle », a seated one « au fauteuil » | browser |
| A14 | E4 overrun | p9 booked −60 min (30 min), seated | its agenda block taller than a 30-min block | browser |
| A15 | E3 dashboard | dentist `/` | « Au fauteuil » card names a seated patient of Nadia | browser |
| A16 | E5 early grey | p10 RDV +40 min, arrival backdated 25 min (SQL on own test row) | wait pill neutral (`bg-muted`); p11 RDV −30, arrival −25 → amber | browser |
| G3a | Q1 undo (run 4) | p24 seated → desk « Terminer » → toast « Annuler » | after Terminer: RDV = call minute, `PlannedStartUtc` = booked; after Annuler: booked start + 30 min back, planned null, « Au fauteuil » | browser + sql |
| G3b | Q1 reopen (run 4) | p25 finished → « ⋯ » « Remettre au fauteuil » | booked start + 30 min back, planned null, status InProgress | browser + sql |

## Tier B — alternate paths

| ID | Case | Expected | Layer |
|---|---|---|---|
| B1 | C4 second RDV of a waiting patient « Arrivé » | 409 `waiting-room-already-here` | api |
| B2 | C9 « Annuler » after a later move | desk « Arrivé » → API moves the card → « Annuler » → toast « … déjà été déplacé… », card stays moved | browser |
| B3 | C9 « Annuler » with nothing moved | card back in « À venir » | browser |
| B4 | C2 arrived RDV moved to tomorrow | GET → lane `Upcoming`, presence times null | api + sql |
| B5 | C3 agenda cancel of an arrived patient | not in the board's Done lane | api |
| B6 | C3 « Absent » on a waiting patient | Done lane, lane `Left`, `leftAtUtc` set | api |
| B7 | C8 « À venir » → « Terminé » | arrival/call/leave null; RDV time unchanged | api + sql |
| B8 | Q3 third seat | 409 `waiting-room-chairs-full` | api |
| B9 | Q3 secretary sets a dentist's chairs | 403 `waiting-room-not-your-chairs` | api |
| B10 | E1 « Pas là » on a waiting patient | 409 `waiting-room-not-in-chair` | api |
| B11 | C1 job leaves a seated overrun alone | p9 still `InProgress` (3) after a job minute | sql |

## Tier C — edge cases / widths

| ID | Case | Expected | Layer |
|---|---|---|---|
| C1 | board 320 px | no sideways scroll of `main`; tags wrap; shot | browser |
| C2 | board 390 / 820 / 1440 | shots read | browser |
| C3 | « Qui arrive ? » other-day row at 320 | pill reads « Recevoir », « Sans RDV » reachable, nothing clipped; at 1440 the row stays on one line | browser |
| C4 | dentist picker at 390 and 1536×730 | dialog fully on screen, « Annuler » visible | browser |
| C5 | dentist panel two chairs at 390 | both rows + « Terminer » on screen | browser |

## Tier D — regression sweep (blast radius)

| ID | Row | Expected | Layer |
|---|---|---|---|
| D1 | #1/#2 agenda cancel of a non-board RDV | 200, status Cancelled | api |
| D2 | #3 same-day move keeps presence | lane still `Waiting` | api |
| D3 | #5 job still closes a non-board RDV | a past RDV nobody marked goes `AwaitingClosure` (7) | sql |
| D4 | #7 header pill | dentist on `/appointments` sees the pill count | browser |
| D5 | #8 plain undo | covered by B3 | browser |
| D6 | #12 agenda loads | day view renders blocks, no page error | browser |
| D7 | #13 dashboard | renders (A15) | browser |
| D8 | round 1 core | Arrivé → Patient suivant → desk banner « faites entrer » | browser |
