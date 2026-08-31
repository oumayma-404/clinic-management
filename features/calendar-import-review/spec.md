# Feature Specification: Calendar import creates reviewable patients

**Status:** APPROVED
**Type:** Small
**Created:** 2026-08-31
**Scope:** Full
**Feature:** A cabinet declares that its Google calendar holds only appointments; the import then accepts any event titled « Prénom Nom », creates the patient when no existing one matches, and says so until a human has confirmed the record.

## Overview

Today `GoogleCalendarSyncService.IsClinicAppointment` demands a keyword (`appointment|patient|doctor|clinic|
consultation|visit`) in the event title, so a practice must title events « Appointment: Ahmed Ben Ali » for the
import to see them. That convention confuses staff and buys nothing for a cabinet whose calendar contains
nothing but appointments. This opens the gate behind a per-clinic declaration, and makes a patient the import
conjured **visibly provisional** rather than silently indistinguishable from a record somebody filled in.

It also repairs two live defects on the same path: every calendar-created patient is currently stored as **born
today** (`DateTime.UtcNow` passed as `DateOfBirth`, the exact substitution `Patient`'s own docstring records as
removed), and patient matching falls back to `Contains`, so an event titled « Ali » attaches to « Ali Ben Salah ».

## What Changes

- A clinic can declare « ce calendrier ne contient que des rendez-vous ». Default **off**; off is today's behaviour, byte for byte.
- With it on, any event whose title parses as a two-to-four-word person's name is an appointment.
- Patient matching becomes **exact and unambiguous only**: full name, or first + last. The `Contains` fallback is deleted.
- An unmatched name creates a patient with `DateOfBirth` **null** (not today's date) and a review stamp.
- The patient's fiche carries a banner naming Google Calendar as the source, until a human confirms the record.
- One clinic-wide notification per newly created patient, deep-linking to that patient. Never an OS push.
- `/patients` gains an « À compléter » filter for the backlog.
- The stamp clears on any save of the patient's personal info, or on an explicit « C'est correct ».

## Acceptance Criteria

- **AC-1:** `Clinic.GoogleCalendarHoldsOnlyAppointments` defaults to false, and with it false the Google→App import selects exactly the same events it selects today.
- **AC-2:** With it true, an event titled « Ahmed Ben Ali » is imported as an appointment at the event's start and duration.
- **AC-3:** With it true, a title that does not parse as a two-to-four-word name (« Réunion CNAM », a single word, five words or more) is **skipped** — no appointment, no patient — and logged at Warning.
- **AC-4:** An event name matching exactly one existing patient (full name, or first + last, case- and space-insensitive) books against that patient; no patient is created and no notification is written. Archived patients are matched, so the import never duplicates someone the clinic already has.
- **AC-5:** An event name matching **more than one** patient is skipped with a Warning. The import never guesses between two people and never adds a third.
- **AC-6:** An event titled « Ali » does not attach to a patient called « Ali Ben Salah ».
- **AC-7:** An unmatched name creates a patient with the title's first and last name, `DateOfBirth` **null**, `Gender` `"Unknown"`, and `CalendarImportPendingReviewSince` set from `ClinicClock` — never `DateTime.UtcNow`.
- **AC-8:** That patient's fiche shows a banner: the record came from Google Agenda and is to be completed, with an action opening the existing edit dialog, and a « C'est correct » that clears the stamp without editing.
- **AC-9:** One `StaffNotification` per newly created patient — category `PatientImportedNeedsReview`, clinic-wide, no actor, `TargetKind.Patient` — and clicking it opens `/patients/<id>`.
- **AC-10:** `StaffNotificationRules.ReachesALockedPhone` classifies the new category as **false**, so it never reaches a lock screen.
- **AC-11:** The notification is best-effort: a generator that throws leaves the imported appointment and patient committed.
- **AC-12:** Saving the patient's personal info clears the stamp, the banner and the filter membership. Clearing happens inside `Patient.UpdatePersonalInfo`, so no call site can forget it.
- **AC-13:** `/patients?pendingCalendarReviewOnly=true` returns only stamped patients, filtered **in SQL**, and the screen shows it as a removable chip that seeds the same URL key it reads.
- **AC-14:** Disconnecting Google resets `GoogleCalendarHoldsOnlyAppointments` to false — a promise about one calendar must not survive onto a different account.
- **AC-15:** At 320 px the patient-page banner wraps without horizontal page scroll and its two actions stay ≥ 44 px on a coarse pointer; the opt-in control and the « À compléter » chip are both reachable and operable at 320 px.

## API Contract

### GET /api/googlecalendar/status
Response 200: adds `holdsOnlyAppointments: boolean`

### PUT /api/googlecalendar/import-settings
Request: `{ holdsOnlyAppointments: boolean }`
Response 200: `{ holdsOnlyAppointments: boolean }`
Errors: `403 — admin only` · `409 — clinic has no Google connection`

### GET /api/patients
Adds query param `pendingCalendarReviewOnly: boolean` (default false). `PatientDto` gains `calendarImportPendingReviewSince: string | null`.

### POST /api/patients/{id}/confirm-calendar-import
Response 200: `{ confirmed: true }`
Errors: `404 — patient not found in this clinic`

### GET /api/notifications
`NotificationDto` gains `patientId: string | null`.

## Data / Schema Changes

One migration, three columns:

- `Patients.CalendarImportPendingReviewSince` — `timestamptz`, **nullable**. Non-null = conjured from a calendar title, unconfirmed. Filtered index for AC-13.
- `StaffNotifications.PatientId` — `uuid`, **nullable**. Written only by the new category.
- `Clinics.GoogleCalendarHoldsOnlyAppointments` — `boolean NOT NULL DEFAULT false`.

New enum members: `NotificationCategory.PatientImportedNeedsReview = 17`, `NotificationTargetKind.Patient = 8`.

⚠️ Check the scaffold for `AddColumn<uint>("xmin")` across all 38 entities and delete those lines. Run
`dotnet run -- verify-schema` before and after and diff.

## Device Behaviour

- **Leading device:** desk and tablet — the correction is typing a birth date and a phone number into a fiche.
- **Narrow width (< 640):** the patient-page banner follows the existing `isArchived` amber banner on that page — text block above, actions below, full width. The « À compléter » chip sits in the toolbar's existing chip row. The opt-in lives in the agenda bar's Google popover, whose width is already clamped against the viewport.
- **Touch:** nothing is hover-revealed. Both banner actions and the chip's dismiss are real controls at the 44 px floor. Floor inherited from `.claude/rules/frontend-web.md`.

## Out of Scope

- Importing an unparseable event as a patient-less slot — considered and rejected; it leaves the per-appointment data entry this removes.
- Changing the **write** format. Pushed events stay « Appointment: {name} »; the reader strips that prefix first, so existing events keep round-tripping.
- Recording provenance for the CSV import or self-signup. This column is about the calendar path only.
- Targeting the notification at one doctor. `doctorId` is null on this import path, so there is no doctor to resolve.
- Aggregating the notifications into one counted row.

## Edge Cases (Critical only)

- **First sync writes many rows.** The window is −7/+90 days, so a first connect can create dozens of patients and dozens of notifications. Accepted: each is a distinct record needing attention with its own deep link, and one aggregate row would hide all but the count.
- **Our own format still works with the gate on** — « Appointment: Ahmed Ben Ali » has its prefix stripped before the name test.
- **A one-word title is skipped**, which also retires the branch that stored « Karim Karim » as first and last name.
- **A matched patient is never stamped**, so an established practice connecting its calendar gets no banners and no bell rows for people it already knows.
