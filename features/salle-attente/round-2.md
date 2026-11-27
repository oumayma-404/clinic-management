# Salle d'attente — round 2 (real clinic day)

Source: the cases review https://claude.ai/artifact/LtCbGxRafwfaHdZxmQuSPq (2026-11-26) and the owner's answers.

## Decisions (owner, 2026-11-26)

| Question | Answer |
|---|---|
| RDV after « Terminé » | **Real time**: called → left. Planned time kept (« prévu 10:00 »), « Annuler » / reopening puts it back |
| « Attente moyenne » | **Arrival → called in**, today's patients already called in, follows the dentist filter |
| Two chairs | **Yes, sometimes** → per-dentist « 2 fauteuils »: « Patient suivant » ends nobody |
| Extras | **All**: « Pas là », « Passer en premier », agenda shows who is here + live overrun + dashboard real call time, early patient grey |

## What changes

| # | Case | Fix |
|---|---|---|
| C1 | Fiche saved late ends a seated visit | While « Au fauteuil », only an explicit end ends it — never the clock (fiche rule + the job's elapse pass) |
| C2 | RDV moved to another day keeps its presence | A move to another clinic day clears arrival / chair / urgent / « pas là » / planned time |
| C3 | Agenda cancel → « Parti sans être vu » | `LeftUnseenAtUtc` says it; an agenda cancel clears it (card leaves the board); « Absent » on a waiting patient stamps it |
| C4 | Same patient arrived twice | Arrival / call refused when another visit of the patient is waiting or seated |
| C5 | Phone search misses today's RDV | Browser matches phone digits; a server hit with a RDV today is listed as that RDV |
| C6 | Comes today, RDV Thursday | Search shows « RDV jeu. 10:00 » + « Le recevoir aujourd'hui » (moves that RDV to now, arrived) |
| C7 | « Faire entrer » with no dentist (group) | Asks « Quel praticien ? » first (`waiting-room-needs-dentist`) |
| C8 | Drag to « Terminé » invents 0 min | Unknown times stay empty; out of averages and of the real-time rule |
| C9 | « Annuler » overwrites a later move | The change carries `After`; restore refuses when the card moved since (`waiting-room-moved-since`, 409) |
| C10 | Dentist on congé / removed | Card says « Dr X absent aujourd'hui » + « Changer de praticien » |
| Q1 | RDV not shortened | Completed with known call + leave → RDV = real time, `BookedWithOverlap`; planned in `PlannedStartUtc`/`PlannedDuration` |
| Q2 | Average jumps | Server `AverageWaitMinutes`: arrival → called, today's called-in |
| Q3 | Two chairs | `Doctor.WorksTwoChairs`; « Patient suivant » ends nobody, max 2 seated |
| E1 | Called but stepped out | « Pas là »: back to waiting, keeps his place, tagged; the next one is called |
| E2 | Toothache walk-in | « Passer en premier » → `UrgentAtUtc`, first in `WaitingRoomOrder` |
| E3 | Agenda / dashboard blind | Agenda block chip « en salle » / « au fauteuil »; dashboard « Au fauteuil » from the board lane |
| E4 | Overrun invisible | Agenda block of a seated visit grows to now |
| E5 | Early patient turns red | Wait colour counts from the RDV time for a booked patient |

## Blast radius

| # | Touching | Other consumers | Verdict |
|---|---|---|---|
| 1 | `Appointment.WaitingRoomLane` (Cancelled/NoShow arms) | board read, `WaitingRoomDay`, payment interceptor, `AppointmentDto.WaitingRoom` | must re-test — agenda cancel, « Absent », board « Parti » |
| 2 | `Appointment.Cancel` / `MarkAsNoShow` | update command, recurring cancel, disregard | must re-test — additive stamp only |
| 3 | `Appointment.Reschedule` (day move clears presence + planned) | update command, « Reporter », recurring edit | must re-test |
| 4 | `Complete` / `MarkVisitCompleted` / board status write (real time) | update command, fiche completion, documents, payment interceptor | must change — settle in one private method |
| 5 | `AppointmentRepository` elapse pass predicate | `AppointmentProgressJob` | must change — called-in counts as on the board |
| 6 | `FicheVisitCompletion.ShouldComplete` | both fiche commands | must change + test |
| 7 | `WaitingRoomOrder.Order` | board read, call-next, header pill | must change (urgent first) |
| 8 | `WaitingRoomChangeDto` / snapshot | `restore`, web `runBoardAction` | must change both sides |
| 9 | `Doctor.WorksTwoChairs` + 5 appointment columns | archive / relay scope (derived from the model) | unaffected — model-derived; verify-schema |
| 10 | migration `AddWaitingRoomRound2` | verify-schema | must re-test |
| 11 | `AppointmentWaitingRoomDto` (+ planned, urgent) | agenda, RDV window | must change — agenda block |
| 12 | `appointment-calendar.tsx` block | agenda day/week/phone | must re-test |
| 13 | `day-summary.ts` « Au fauteuil » | dashboard day board | must change |
| 14 | `staff-chat-notices.tsx` call-in notice (« Pas là », QA F1) | every other notice kind | unaffected — gated on `isCallIn` |
| 15 | `call-in-banner.tsx` button placement (QA F1) | the board only | must re-test — A6 |
