# QA run 1 — salle d'attente round 2

Plan: `qa/r2-plan.md` · driver: `qa/r2-walk.mjs` (run `z1ta`) · log: 45 rows, one launch.
Environment: API `:5098` (Debug, started 16:31 after the migration `20261126151948_AddWaitingRoomRound2`, DB
`clinic_sa_qa`), web `:3098` production standalone built 16:2x from the round-2 tree. Headless Chrome, `fr-FR`,
`Africa/Tunis`. No peer session on these ports (isolated stack; shared Docker started by this session).
Widths looked at: 320, 390, 820, 1440, 1536 × 730 (picker), 390 (dentist panel).

## Scenarios

| ID | Result | Evidence |
|---|---|---|
| B1 | ✅ | second RDV « Arrivé » → 409 `waiting-room-already-here` |
| B10 | ✅ | « Pas là » on a waiting patient → 409 `waiting-room-not-in-chair` |
| B4 | ✅ | moved to tomorrow → lane `Upcoming`, arrival null |
| D2 | ✅ | same-day move keeps `Waiting` |
| B5 | ✅ | agenda cancel of an arrived patient → off the board |
| B6 | ✅ | « Absent » on a waiting patient → `Left`, `leftAtUtc` set |
| B7 | ✅ | « À venir » → « Terminé »: Completed, no times, RDV unchanged |
| B9 | ✅ | secretary sets chairs → 403 `waiting-room-not-your-chairs` |
| D1 | ✅ | ordinary agenda cancel 200 |
| D8 | ✅ | desk banner « faites entrer » names p1 (`shots/r2-D8-desk-banner-1440.png`) |
| A1 | ✅ | « 16:41 – 16:41 · 0 min »; SQL RDV 15:41Z, `PlannedStartUtc` 15:30Z |
| A3 | ⏭ | probe read the label's own span, not the figure — fixed in the driver, re-driven in run 2 |
| A6 | ❌ | F1 — the banner's « Pas là » cannot be pressed (`shots/r2-A6-banner-pas-la-1440.png`) |
| A4 | ⏭ | cascade of A6 (p4 still seated) + probe counted « Terminer » before the refresh — fixed in the driver |
| A5 | ✅ | « Les deux fauteuils sont occupés » disabled |
| B8 | ⏭ | cascade of A6: p4 was already seated, so the move answered `changed: false` |
| C5 | ✅ | dentist panel at 390: no sideways scroll (`shots/r2-C5-two-chairs-390.png`) |
| A11 | ✅ | « Quel praticien ? » → Dr Nadia; SQL `DoctorId` set |
| A12 | ✅ | « Dr Salma Ben Youssef absent aujourd'hui » + « Changer de praticien »; Salma not offered; changed to Nadia |
| C4 | ✅ | picker « Annuler » on screen at 390 × 844 and 1536 × 730 |
| A10 | ✅ | p6 found by 5 phone digits under « Rendez-vous d'aujourd'hui » |
| A7 | ✅ | « Passer en premier » → first with « Urgent »; « Retirer « Urgent » » removes it |
| A16 | ✅ | early p10 « 24 min » neutral (`bg-muted`); p11 amber |
| C3 | ✅ | 320: « RDV sam. 28 nov. 10:00 · … Recevoir », « Sans RDV » beside it |
| A8 | ✅ | p5 received today (28/11 09:00Z → today) |
| A9 | ✅ | « Annuler » → RDV back on 28/11, off today's board |
| B2 | ✅ | undo after a later move refused: « … a déjà été déplacé·e depuis … »; card stays « Parti » |
| B3 | ✅ | plain undo → back in « À venir » |
| D6 | ✅ | agenda day view renders |
| A13 | ✅ | « … · En cours · au fauteuil » · « … · Planifié · en salle » |
| A2 | ✅ | « Amira … · 16:41 (prévu 16:30) · 1 min · Terminé » |
| A14 | ✅ | overrun block 56 px vs 27 px for a 30-min visit |
| A15 | ✅ | « Au fauteuil » names a seated patient (see O1) |
| D4 | ✅ | header pill « Salle d'attente : 8 patients pour vous » |
| C1 | ✅ | board at 320: no sideways scroll |
| C2 | ✅ | board at 390 / 820 / 1440: no sideways scroll |
| B11 | ✅ | seated overrun still `InProgress` after a job minute |
| D3 | ✅ | past RDV nobody marked → `AwaitingClosure` (7) |
| PAGE | ✅ | no page errors |

## Findings

### F1 — major — « Pas là » on the desk banner is unreachable while the call rings

- **Repro:** dentist presses « Patient suivant » → desk board.
- **Expected:** the desk presses « Pas là » on the call.
- **Actual:** the banner's « Pas là » / « OK » sit at the far right of the banner, exactly under the staff-chat
  call-in notice (« Faites entrer le patient suivant … Voir la salle d'attente · Ouvrir · OK · J'arrive ») and the
  board's « Annuler » toasts, both pinned top-right. The click times out (intercepted). The notice — which is where
  the desk actually answers a ring, and which stays until answered — offers no « Pas là » at all.
- **Evidence:** `shots/r2-A6-banner-pas-la-1440.png`.
- **Owner:** `web/components/waiting-room/call-in-banner.tsx` (text `flex-1` pushes the buttons right) ·
  `web/components/staff-chat/staff-chat-notices.tsx` (call-in notice has no « Pas là »).

## Observations (off-plan)

- **O1** — the dentist's dashboard « Au fauteuil » names Dr Salma's seated patient (cabinet-wide day board, tie on
  claim broken by earliest start) while Dr Nadia has her own patient seated. The day board has always been
  cabinet-wide; not changed here.

## Status

RED (1 finding)
