# Feature Specification: Pointage obligatoire

**Status:** APPROVED
**Type:** Small
**Created:** 2026-11-05
**Scope:** Full
**Feature:** A secretary cannot work before she clocks in, and a forgotten departure is always caught.
**Part 3 of 3** of [`pointage-conges/blueprint.md`](../pointage-conges/blueprint.md) · after
[`pointage-borne`](../pointage-borne/spec.md) (required).
Visuals: https://claude.ai/artifact/LJQDQvKRaLqCLmXvRJrrsF (section « She can't forget ») · ⚠️ build from `main`.

## Overview
- **Arrival is enforced.** During opening hours a tracked secretary's app is locked until she has clocked in, and
  the server refuses her saves meanwhile.
- **Departure cannot be physically forced, so three nets catch it:**
  - a reminder 15 min after closing time;
  - « Pointer votre départ ? » when she signs out;
  - the next morning, before her arrival, she must give yesterday's departure hour, marked « Déclaré ».

## What Changes
- **Paramètres → « Équipe »:** the sub-switch « Bloquer l'app tant que l'arrivée n'est pas pointée », on by
  default once pointage is on.
- **Her account:** a full-screen lock « Pointez votre arrivée », which lifts by itself once she has clocked in.
- **A reminder notification**, pushed to her phone.
- **Sign-out:** asks to clock out first.
- **The borne:** a « Votre départ d'hier n'a pas été pointé » step.
- **The admin's sheet:** shows « Déclaré ».

## Acceptance Criteria
- **AC-1:** The lock holds **only** when all of these are true. One server rule decides it, and both the screen and
  the save refusal ask that same rule.
  - Pointage and the lock are on.
  - The account is tracked.
  - Today is an open day, and it is inside today's opening hours.
  - There is no line for her today.
  - She has no accepted congé today.
  - The cabinet is not running on the PC de secours.
- **AC-2:** While locked, every screen of her account shows « Pointez votre arrivée » and names the cabinet's
  bornes.
  - On a borne, the pad is inside the lock.
  - The lock closes **by itself** when her punch lands (realtime), with no reload and no button.
- **AC-3:** While locked, the server refuses her saves with `423 clock_in_required`.
  - Reads, sign-out, « Mon profil » (her schéma) and « Congés » stay open.
  - Endpoints that stay open carry an attribute, held by a coverage test in both directions.
- **AC-4:** There is no lock after her departure, after closing time, on a closed day or during a congé, so she can
  still check tomorrow's agenda from home.
- **AC-5:** An admin's « Ajouter un pointage » for her today (borne broken) lifts the lock.
- **AC-6:** **15 min after today's closing time**, while she is still « présente », she gets « Départ non pointé —
  Vous êtes toujours présente depuis 08:02 ».
  - It is sent once per line and pushed to her phone when the app is installed.
  - The reminder job **writes no pointage row**, so it never makes an admin's open correction refuse.
- **AC-7:** « Se déconnecter » while she is present asks « Pointer votre départ ? ».
  - On a borne, it opens the pad.
  - Elsewhere it names the borne.
  - « Se déconnecter sans pointer » is always available.
- **AC-8:** At her next arrival, if a line from an earlier day is still open, the borne first shows « Votre départ
  d'hier n'a pas été pointé ». The screen shows:
  - that day's arrival;
  - **her last save in the app that day** (from the journal; « aucune » when there is none);
  - a required « Heure de départ », between the arrival and 23:59 of that day.

  The hour is saved as « Déclaré », and then the schéma and the arrival proceed.
- **AC-9:** The admin's sheet shows a declared departure in orange as « Déclaré », with that last save time beside
  it. The admin can still correct it, with a motif. The monthly totals count declared lines and list how many there
  are.
- **AC-10 (device):**
  - At 320 px the lock fits with no scroll, and its only control (sign-out) is ≥ 44 px on a coarse pointer.
  - The declaration step is usable on a tablet with the on-screen keyboard open.
  - Times use the 24 h `TimeField`.

## API Contract
### GET /api/team/me/clock-status — own account
Response 200: `{ locked, bornes: string[], presentSince?, canClockOutHere }`

### POST /api/pointage/punch (extended)
Request: `{ userId, pattern, declaredPreviousDeparture?: "HH:mm" }`
Errors added: `409 previous_departure_missing { day, arrival, lastSaveAt? }`

### Any non-GET `/api` request while locked
`423 { error, code: "clock_in_required" }`

### PUT /api/clinics/team-modules
Gains `attendanceLock?`.

## Data / Schema Changes
- **`Clinics.AttendanceLockEnabled`:** bool, default true. It goes through the same 7 `ClinicDto` sites.
- **`NotificationCategory`:** gains `AttendanceDepartureMissing` (appended).
- **Departure origin:** uses `AttendanceEntries.DepartureSource = DeclaredBySelf` (from part 2).

## Device Behaviour
- **Leading device:** whatever she opens first in the morning, phone or reception PC.
- **Narrow width (< 640):** the lock is a full-screen card; the sign-out prompt is a bottom sheet.
- **Touch:** nothing hover-only. The reminder is a push notification, not a banner she has to be looking at.

## Out of Scope
- A borne that works during an internet cut. The lock lifts during the cut; an arrival already pointed has its
  departure declared the next morning, and a day with no arrival pointed stays without a line.
- Locking a shared session: the lock is on **her** account.
- Lateness against planned hours.

## Edge Cases (Critical only)
- **Opening hours not set:** there is no lock, because « inside opening hours » cannot be answered. The reminder is
  skipped for the same reason.
- **She signs in from home before opening:** no lock until opening time.
- **A declared hour after a later correction:** the admin's correction wins and the line reads « Corrigé ».
