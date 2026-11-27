# QA run 2 — salle d'attente round 2

Plan: `qa/r2-plan.md` (+ A6b) · driver: `qa/r2-walk.mjs` (run `7x1t`), same file as run 1 plus row A6b and the
A3/A4 probe fixes. Environment: API `:5098` unchanged since run 1 (no backend fix); web `:3098` rebuilt after the
cycle-1 fix (new chunks verified to carry « n'est pas là »). No peer on these ports.
Widths looked at: 320, 390, 820, 1440, 1536 × 730; dentist panel 390.

## Scenarios

All 48 scripted rows ✅ — the run-1 table holds row for row, plus:

| ID | Result | Evidence |
|---|---|---|
| A3 | ✅ | « Attente moyenne 0 min » = API, unchanged after 35 s |
| A6 | ✅ | banner « Pas là » (now right after the sentence): p4 back « Pas là · 16:48 », p2 seated, toast « … n'est pas là · … est appelé·e » |
| A4 | ✅ | p2 and p3 both seated, two « Terminer » (`shots/r2-A4-two-chairs-1440.png`) |
| B8 | ✅ | third seat → 409 `waiting-room-chairs-full` |
| A6b | ✅ | call-in notice « Pas là »: p22 back waiting, p23 seated, notice answered (`shots/r2-A6b-notice-pas-la-1440.png`) |

## Findings (eye pass on the screenshots)

### F2 — minor — an overrunning séance is drawn UNDER the visits it now overlaps

- **Where:** agenda day view, `shots/r2-A13-agenda-day-1440.png` — a patient left « au fauteuil » since 09:45 grows
  to now (E4) across the afternoon, and the RDVs it now runs into are painted over it in the same lane.
- **Expected:** the overrun shares the column with the visits it collides with, side by side, as any overlap does.
- **Cause:** `computeOverlapLanes` measures each block with the BOOKED duration while `renderAppointmentBlock`
  draws the live one, so the packer never sees the collision.
- **Owner:** `web/components/appointment-calendar.tsx` (`computeOverlapLanes`).

### F3 — major — « Le recevoir aujourd'hui » hides the date it is about on a phone

- **Where:** `shots/r2-C3-other-day-row-320.png` — the row reads « Soni… / RDV s… »: the other day's date and hour,
  the one fact the secretary needs before moving the RDV, is truncated at 320 (and, by measure, at 390).
- **Expected:** the RDV's day and hour readable at every width.
- **Cause:** the row keeps `truncate` on the name and the detail while two controls (« Recevoir », « Sans RDV »)
  take ~150 px of the line. C3's probe checked the controls, not that the detail fits.
- **Owner:** `web/components/waiting-room/arrival-search.tsx` (`ResultRow`).

## Observations (off-plan)

- O1 (run 1) unchanged — the dashboard « Au fauteuil » is cabinet-wide.
- The staff-chat call-in notices of earlier calls stack top-right until answered (the walk never answers them) —
  round-1 design (urgent notice stays until answered).

## Status

RED (2 findings — eye pass; all scripted rows green)
