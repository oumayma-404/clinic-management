# Feature Specification: Appointment Scheduling UX Enhancements

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-16
**Scope:** FE
**Feature:** Make the appointment calendar and create/edit modals behave more like Google Calendar — center on "now", warn on past-time and overlapping bookings, render odd-length appointments cleanly, and make the patient picker searchable.

## Overview
Five focused frontend improvements to appointment scheduling, all in `web/`. No backend or API changes. Guards are advisory: the user is warned but never blocked — if they confirm/ignore, the booking proceeds exactly as today.

## What Changes
- **Center on now:** On initial load, when today is in the visible range, the calendar scrolls so the current-time line is vertically centered (instead of always snapping to 8 AM). When today is not visible (a past/future day or week), keep the existing 8 AM scroll.
- **Past-time confirmation:** On submitting the create **or** edit dialog, if the resulting start date/time is in the past, show a blocking confirmation popup ("this time is in the past, are you sure?"). Confirm → proceed with create/update; Cancel → return to the form unchanged.
- **Overlap warning:** In the create **and** edit dialog, when the selected date + start time + duration overlaps an existing appointment, show an always-visible inline **text** warning (not a popup) naming the conflicting appointment. Non-blocking — the user can still submit.
- **Clean short/long rendering:** Each appointment renders exactly once as a single continuous block, proportional to its true duration and positioned by its start minute — fixing today's duplicated boxes (a >60-min appt currently draws once per overlapping hour slot) and clipped/awkward short appts.
- **Searchable patient picker:** Replace the plain patient `Select` in the create dialog with a searchable combobox (type to filter patients by name).

## Acceptance Criteria
- **AC-1:** On first render of `/appointments`, if today falls in the visible day/week, the scroll container is positioned so the current-time indicator line is centered in the viewport; if today is not visible, it scrolls to 8 AM as before.
- **AC-2:** Submitting the create or edit dialog with a start date/time earlier than the current moment opens a confirmation dialog; confirming completes the original create/update, cancelling aborts it and leaves the form intact. (Edit only warns when the start is moved to a past time — editing an already-past appointment without changing its time does not nag.)
- **AC-3:** In both dialogs, when the chosen date + start + duration overlaps a non-cancelled appointment, a visible amber text warning is shown identifying the conflict (e.g. patient name + start time); it is not a popup and does not prevent submission. It clears when the overlap is resolved. In edit, the appointment being edited is excluded from the check.
- **AC-4:** An appointment of any duration (e.g. 15 min, 45 min, 90 min) renders as a single block whose height is proportional to its duration and whose top offset matches its start minute; no appointment is drawn more than once, and a very short appointment still shows the patient name legibly (enforced minimum height).
- **AC-5:** The create dialog's patient picker lets the user type a query that filters the patient list by first/last name; selecting a result sets the patient exactly as the current `Select` does.

## Out of Scope
- Any backend / API / validation changes (all client-side; server behavior unchanged).
- Hard-blocking past-dated or overlapping appointments (both remain allowed after the warning).
- Server-side overlap detection, recurring appointments, drag-to-move/resize on the calendar.
- Patient search in the edit dialog (its patient field is read-only and unchanged).

## Edge Cases (Critical only)
- **Busy slots ("Occupé") count as overlaps** — they occupy time, so they trigger the overlap warning like any appointment.
- **Overlap data source:** the dialog fetches appointments for the selected day (via the existing `appointmentsApi`/`useAppointments`); a fetch failure silently disables the warning (never blocks booking).
- **Timezone:** past-time comparison uses the browser's local `new Date()`, consistent with how the calendar already computes the "now" line.
- **Cancelled appointments** are ignored for overlap detection (mirrors the calendar's default filtering).
