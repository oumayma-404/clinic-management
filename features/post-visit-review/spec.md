# Feature Specification: Post-Visit Review & Medical-Record Prompt

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-17
**Scope:** Full
**Feature:** When an appointment's time has passed, prompt the doctor (or all staff) to add the patient's medical record; filling the record marks the appointment as done.

## Overview
Once an appointment's end time passes, the system surfaces a "post-visit review" notification asking staff to record what happened by adding the patient's medical record. If the appointment names a doctor who is linked to a staff user, only that doctor is prompted; otherwise everyone in the clinic is. On the doctor's open app the prompt also appears as a dismissible popup ("remind me later" leaves it in the notification feed). When a medical record is created for that appointment, we treat the visit as having occurred and move the appointment to `Completed`.

> ⚠️ Forced Type:Small at user request. This is genuinely 4 areas + 2 data-model changes + new UI; scope is at/over the small-feature limit. Popup layout is intentionally minimal (no mockup step).

## What Changes
- New notification category `PostVisitReview`, scheduled (deferred) at appointment create/update to become visible at the appointment's **end** time (`AppointmentDateTime + Duration`), reusing the existing reminder/deferred-visibility pattern — no background job.
- `StaffNotification` gains an optional `TargetUserId`: when set, only that user sees the row; when null, the row stays clinic-wide (all existing behavior unchanged).
- Post-visit target resolution: `Appointment.DoctorId` → `Doctor` → `Doctor.UserId`. If a linked user exists, `TargetUserId` = that user; otherwise `TargetUserId` = null (all staff).
- On reschedule the post-visit notification moves to the new end time; on cancel it is removed (mirrors reminder handling).
- New popup on the frontend: when a due, unread `PostVisitReview` notification is visible to the current user, a modal asks "how was the visit" with **Add medical record** and **Remind me later**. "Remind me later" closes the popup (client-side snooze) but leaves the notification in the bell/panel.
- `MedicalDocument` gains an optional `AppointmentId`; creating a medical record with an `AppointmentId` marks that appointment `Completed` (best-effort, post-commit — never fails the record creation).
- New domain transition allowing `Scheduled`/`Confirmed`/`InProgress` → `Completed` (record-fill implies the patient came), since today `Complete()` requires `InProgress`.

## Acceptance Criteria
- **AC-1:** Creating an appointment with a patient schedules a `PostVisitReview` notification whose visible time equals the appointment end (`AppointmentDateTime + Duration`); patient-less "busy slots" schedule nothing.
- **AC-2:** If the appointment's `DoctorId` resolves to a `Doctor` with a linked `UserId`, the notification's `TargetUserId` is that user (only they see it); if `DoctorId` is null or the doctor has no linked user, `TargetUserId` is null and all clinic staff see it.
- **AC-3:** The notification is **not** visible before the appointment end time and becomes visible in the bell/panel once the end time has passed (deferred visibility via `EffectiveFeedTime`).
- **AC-4:** Rescheduling moves the post-visit notification to the new end time; cancelling removes it.
- **AC-5:** For a user with a due, unread `PostVisitReview` notification, a popup appears in the app; **Remind me later** closes it and it does not reappear for a snooze window, but the notification remains unread in the panel.
- **AC-6:** The popup's **Add medical record** action opens medical-record creation for that appointment's patient, associated with the appointment.
- **AC-7:** Successfully creating a `MedicalDocument` that carries an `AppointmentId` transitions that appointment to `Completed` (from `Scheduled`, `Confirmed`, or `InProgress`); a `Cancelled`/`Completed`/`NoShow` appointment is left unchanged. Record creation always succeeds even if the status update is skipped.
- **AC-8:** Existing notifications (all with null `TargetUserId`) and all Cloud-mode behavior are unchanged.

## API Contract
### GET /api/notifications/pending-reviews
Returns due, unread `PostVisitReview` notifications visible to the current user (drives the popup; frontend polls periodically).
Response 2XX: `[{ id, title, message, appointmentId, patientName?, createdAt }]`
Errors: `401` unauthenticated.

### POST /api/documents (modify existing CreateMedicalDocument)
Request: add optional `appointmentId: Guid?` to the existing body.
Response 2XX: unchanged (created document).
Behavior: when `appointmentId` is present and resolves to an appointment in the caller's clinic, mark it `Completed` post-commit (best-effort).

<!-- GET /api/notifications, /unread-count, read/read-all are unchanged; they simply also honor TargetUserId. -->

## Data / Schema Changes
- `NotificationCategory` — add `PostVisitReview` (next enum value). Reuses `NotificationTargetKind.Appointment` + `AppointmentId` for the deep-link (no new target kind).
- `StaffNotification.TargetUserId` — new `string?` column, nullable, default null. Repository predicates (`GetRecentForUserAsync`, `UnreadQuery`) add `(TargetUserId == null || TargetUserId == userId)`. New migration.
- `INotificationGenerator` — add `SchedulePostVisitReviewAsync(...)` + reschedule/cancel handling; called inline post-commit from Create/Update appointment handlers.
- `MedicalDocument.AppointmentId` — new `Guid?` column, nullable, default null; add to `CreateMedicalDocumentCommand`. New migration. (No FK cascade needed; deleting an appointment leaves the record.)
- `Appointment` — add a guarded domain method (e.g. `MarkVisitCompleted()`) allowing `Scheduled`/`Confirmed`/`InProgress` → `Completed`; blocked from `Cancelled`/`Completed`/`NoShow`.
- Frontend: `NotificationDto`/types extend with `TargetUserId`? — not required (server filters); add `patientName` only if returned by the new endpoint. New popup component + periodic poll; add `PostVisitReview` → icon in `notification-panel.tsx`'s `CATEGORY_ICON`.

## Out of Scope
- `DentalRecord` and `PatientFile` uploads as completion triggers — only `MedicalDocument` completes the appointment.
- Server-side snooze persistence (remind-me-later is client-side, per-browser).
- A recurring background job — deferred visibility + frontend polling replaces it.
- Email/SMS; the dormant outbound `Notification`/`NotificationService` stays untouched.
- Editing/deleting a medical record reverting the appointment status.

## Edge Cases (Critical only)
- Appointment created with an end time already in the past → notification is immediately visible (not an error).
- Doctor changed on reschedule/update → post-visit notification's `TargetUserId` is recomputed to match the current doctor.
- Two staff (all-target case) — first to add the record completes the appointment; a second attempt is a no-op because the appointment is already `Completed`.
- Notification generation or the post-commit completion failing must log at Error and never roll back the appointment or the medical record.
