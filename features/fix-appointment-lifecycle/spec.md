# Feature Specification: Appointment Lifecycle Correctness

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-21
**Scope:** Full
**Feature:** Allow editing/reactivating cancelled appointments, show appointments on the correct local day, and don't remind for cancelled visits.

## Overview
Three lifecycle defects: (1) editing a cancelled appointment always fails because the update handler calls `Reschedule()` (which throws on a cancelled appointment) before the status block can un-cancel it; (2) the day/week fetch window is sent as local wall-clock but interpreted as UTC, so appointments near local midnight show on the wrong day; (3) the reminder dispatcher never re-checks appointment status at send time, so a cancel-vs-send race can text a patient about a cancelled visit.

## What Changes
- `UpdateAppointmentCommand` reactivates a cancelled appointment before applying a date change, so "un-cancel and move to a new time" (and editing any field of a cancelled appointment) succeeds instead of returning 400.
- The day/week appointment query window is computed so appointments display on their correct local calendar day (the local-wall-clock-treated-as-UTC offset is removed).
- `NotificationJob` re-checks the appointment is still active (not Cancelled/NoShow) at send time and skips the reminder otherwise.

## Acceptance Criteria
- **AC-1:** Editing a cancelled appointment — changing its date and/or setting status back to Scheduled — succeeds and yields a Scheduled appointment at the new time (no "Cannot reschedule a cancelled appointment" error).
- **AC-2:** Editing a field of a cancelled appointment without changing its status no longer 400s due to the reschedule guard.
- **AC-3:** An appointment at 00:30 local time appears on its own local day in day/week views (not the previous day); the last hour before local midnight stays on the correct day.
- **AC-4:** The SMS/WhatsApp dispatcher does not send a reminder for an appointment that is Cancelled or NoShow at send time.

## Out of Scope
- Google Calendar sync on create/offline gating (covered by `fix-appointment-google-sync`).
- Reworking the domain state machine beyond enabling the reactivate-then-reschedule path.

## Edge Cases (Critical only)
- Reactivating a Completed appointment stays disallowed (only Cancelled → Scheduled is enabled).
