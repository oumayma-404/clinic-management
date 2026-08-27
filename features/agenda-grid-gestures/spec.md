# Feature Specification: Agenda Grid Gestures

**Status:** APPROVED
**Type:** Small
**Created:** 2026-08-12
**Scope:** FE
**Feature:** The time grid stops covering itself with an empty-state card, and gains the two pointer gestures a calendar is expected to have — drag across hours to book a span, drag a block to move it.

## Overview
Three changes to `web/components/appointment-calendar.tsx`, all on the time grid (Jour and Semaine at `md:`+).
The « Aucun rendez-vous cette semaine » card is an `inset-0` overlay: it reads as a modal, and it sits over the
hours the user is trying to click. Its sentence moves into the grid's existing footer strip instead. On top of
that, dragging across empty hours opens « Nouveau rendez-vous » with that **span** already set, and dragging an
existing block to another slot **moves** the appointment — the two gestures a dentist arriving from Google
Calendar reaches for first. No backend change: `PUT /api/appointments/{id}` already accepts a new
`appointmentDateTime` with `version` and the three advisory overrides.

## What Changes
- The empty-state overlay card is removed from the time grid. Its sentence becomes a quiet line in the footer
  strip below the grid, which now renders whenever the range is empty (today it renders only when the hour
  window is trimmed).
- « Nouveau rendez-vous » stays reachable while the range is empty — from the toolbar button and the phone's
  floating action, which already exist. The overlay's own duplicate button goes with the overlay.
- Dragging vertically across empty hour cells paints a provisional selection and, on release, opens the create
  dialog with that day, its start time, and its **duration** pre-filled.
- `CreateAppointmentDialog` gains a `defaultDurationMinutes` prop. When supplied, choosing acts no longer
  overwrites the duration — a dragged span is an explicit statement about how long the visit is.
- Dragging an existing appointment block onto another slot moves it: the block follows the pointer, and on
  release the new start is saved through `appointmentsApi.update` carrying the appointment's `version`.
- A move refused as `slot_taken`, out-of-hours or past-time raises a compact confirmation naming the server's
  own reason; confirming re-sends with the matching override, cancelling snaps the block back to where it was.
- Both gestures snap to 15 minutes (the grid's existing sub-hour unit) and are scoped to **Jour and Semaine**.
  Semaine is where the request came from; Jour renders the same grid from the same code, so excluding it would
  be arbitrary.

## Acceptance Criteria
- **AC-1:** With no appointments in range, no card, sheet or blur overlays the time grid, and every hour cell in
  the range is clickable at the position it occupies.
- **AC-2:** With no appointments in range, the footer strip states « Aucun rendez-vous cette semaine » (Semaine)
  or « … ce jour-là » (Jour) plus the click/touch hint, and the strip renders even when the hour window is not
  trimmed. While `loading` or `error` is set the sentence is absent, so a failed fetch is never reported as an
  empty day.
- **AC-3:** Dragging from 09:00 to 11:00 in a day column opens the create dialog with that date, `09:00`, and a
  120-minute duration; a drag ending inside the same 15-minute unit as it started behaves as today's plain
  click (start time only, no duration override).
- **AC-4:** With a dragged duration in play, selecting or changing acts in the create dialog leaves the duration
  field at the dragged value; the user can still edit it by hand.
- **AC-5:** Dragging an appointment block to a free slot in the same or another day column persists the new
  start; the grid reflects it after the refetch, and the appointment's own duration is unchanged.
- **AC-6:** A drop the server refuses with `slot_taken`, out-of-hours or past-time shows a confirmation quoting
  the server's French reason. Confirming re-sends with `allowOverlap` / `allowOutsideWorkingHours` / the
  past-time acknowledgement and the move persists. Cancelling leaves the appointment at its original time and
  the block returns there.
- **AC-7:** A drop that lands on the slot the appointment already occupies is a no-op — no request is sent.
- **AC-8:** A 409 (a peer moved the same appointment) surfaces the server's French sentence and the grid
  refetches; the block is never left showing a time that was not saved.
- **AC-9:** A cancelled or completed appointment is not draggable; clicking it still opens its dialog.
- **AC-10:** At 320 px both gestures leave the grid usable: vertical scrolling through 24 hours and the Jour
  horizontal day-swipe still work, and the drag gestures only begin after a long press (see Device Behaviour).
- **AC-11:** The full non-gesture path is untouched — clicking an hour opens the create dialog, clicking a block
  opens its edit dialog, and the edit dialog remains the way to move an appointment without dragging.

## Device Behaviour
- **Leading device:** desk (a coarse-pointer drag is the secondary path, per `~/.claude/skills/DEVICE-CONTRACT.md`).
- **Narrow width (< 768):** Semaine renders `renderWeekStrip()`, not the time grid, so **drag-to-move does not
  exist at phone width** — the strip's rows keep opening the edit dialog, which is where a phone user moves an
  appointment. Jour does render the grid on a phone and gets both gestures. This is a property of the existing
  layout, not a capability removed here.
- **Touch:** both gestures start on a ~350 ms long press with a light haptic-free visual cue (the pressed block
  or the first cell highlights). Before that threshold the touch belongs to the scroll container, so a thumb
  drag scrolls the day and the Jour horizontal swipe still changes day. Every drag target is a full hour cell
  or an appointment block — no new sub-44 px control is introduced.

## Out of Scope
- Resizing an appointment by dragging its edge (changing duration in place).
- Dragging in Mois view, or between Mois cells.
- Dragging in `renderWeekStrip()` (phone Semaine) or in the phone Mois view.
- Multi-select of several appointments, and dragging more than one at a time.
- Any backend, DTO or schema change; any change to the three refusal codes or their wording.
- The per-day « Aucun rendez-vous » rows inside `renderWeekStrip()` — those are that view's own empty state and
  stay as they are.

## Edge Cases (Critical only)
- A drag that starts on an empty cell and crosses over an existing block: the selection is still a span of
  hours, and the create dialog's own double-booking confirmation handles the collision on submit.
- A drag released outside the grid (over the toolbar, or off the window) cancels — nothing is created, nothing
  is moved.
- A drag upward (release above the start cell) yields the same span as the equivalent downward drag.
- A move whose new start falls outside the currently displayed hour window (the grid is trimmed to opening
  hours) cannot be expressed by the gesture and needs no special handling — the drop target does not exist.
- `showFullDay` toggling mid-drag: the drag is cancelled rather than remapped, since every cell's position
  changes underneath it.
