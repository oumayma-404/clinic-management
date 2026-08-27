# Feature Specification: Post-Visit Review → Patient Dental-Record Modal

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-17
**Scope:** Full
**Feature:** The post-visit-review popup's "Ajouter le dossier médical" action now opens the patient's page with the "Add Medical Record" (dental-record) modal, and saving that record completes the appointment + clears the review — instead of deep-linking to the documents gallery.

## Overview
Today, clicking "Ajouter le dossier médical" in the post-visit popup navigates to `/documents?appointmentId=…` (the medical-document template gallery), and the appointment is marked Completed only when a `MedicalDocument` is created there. This changes the destination to the patient's own page with the existing **Add Medical Record** modal (`PatientRecordModal`, which creates a `DentalRecord`) auto-opened, and preserves the "filling the record marks the visit done" behavior by moving the completion side-effect onto dental-record creation — mirroring the existing `CreateMedicalDocumentCommand` side-effect (feature `post-visit-review`, AC-7).

## What Changes
- The popup's "Ajouter le dossier médical" button resolves the review's `patientId` (via the appointment) and navigates to `/patients/{patientId}?addRecord=1&appointmentId={appointmentId}` instead of `/documents?appointmentId=…`. It still snoozes client-side on click (unchanged).
- The patient detail page opens its existing "Add Medical Record" modal (`PatientRecordModal`, create mode) automatically when it loads with `?addRecord=1`, and carries the `appointmentId` into the dental-record creation.
- `DentalRecord` gains an optional `AppointmentId`; creating a dental record with an `AppointmentId` marks that appointment `Completed` and clears its pending post-visit review — best-effort, post-commit, never failing the record creation (same contract as `MedicalDocument`).
- Dental-record creation with a completion side-effect broadcasts the `"appointments"` realtime key so calendar/appointment views refresh the now-Completed status.

## Acceptance Criteria
- **AC-1:** Clicking "Ajouter le dossier médical" navigates to `/patients/{patientId}?addRecord=1&appointmentId={id}` (patientId resolved from the appointment) and no longer navigates to `/documents`.
- **AC-2:** Loading the patient page with `?addRecord=1` auto-opens `PatientRecordModal` in create (not edit) mode; loading it without the param does not open the modal.
- **AC-3:** Saving a dental record created on this path (i.e. with an `appointmentId`) marks the appointment `Completed` (from Scheduled/Confirmed/InProgress; idempotent no-op otherwise) and removes its pending `PostVisitReview` notification, so the popup/panel stops prompting.
- **AC-4:** The completion side-effect is best-effort and post-commit: if it throws (e.g. cross-clinic/unknown appointment, or a downstream error) the dental record is still created successfully and no error is surfaced.
- **AC-5:** A cross-clinic or unknown `appointmentId` is a silent no-op (record created, no appointment touched), matching the `MedicalDocument` completion path.
- **AC-6:** Creating a dental record **without** an `appointmentId` (the normal "Add Dental Record" / "Add Medical Record" flows) behaves exactly as before — no completion side-effect, no broadcast beyond the existing behavior.
- **AC-7:** A dental-record completion broadcasts the `"appointments"` realtime key so appointment/calendar views refetch.

## API Contract
### POST /api/patients/{patientId}/dental-records
Request (adds one optional field to the existing `CreateDentalRecordCommand` body):
`{ interventionDate, procedureType, cost, amountPaid, isAdultTeeth, toothNumbers, notes, importantNotes, appointmentId?: guid | null }`
Response 2XX: `DentalRecordDto` (unchanged shape; `appointmentId` echo optional).
Errors: unchanged (`400` on validation / not-found, per the existing not-found convention). The completion side-effect never changes the status code.

## Data / Schema Changes
- `DentalRecord.AppointmentId` — new optional `Guid?` (nullable), set only when a record is created from the post-visit path. Additive EF column + new migration; nullable, no default, no FK enforcement required (mirrors `MedicalDocument.AppointmentId`).

## Out of Scope
- Updating an existing dental record does **not** trigger completion (only creation carries `appointmentId`).
- No pre-filling of the dental-record form from the appointment (procedure type, date, etc.) — the modal opens empty in create mode; `appointmentId` is carried only for the completion side-effect.
- No change to the `/documents` medical-document flow — its own AC-7 completion side-effect stays as-is.
- No change to the popup's snooze / "Plus tard" behavior.

## Edge Cases (Critical only)
- **Review with no resolvable patient:** the post-visit review is only generated for appointments with a patient (feature `post-visit-review`, AC-1), so `patientId` should always resolve; if the appointment fetch fails, the popup keeps the review pending (no navigation) rather than sending the user to a dead page.
- **Modal closed without saving:** the review stays pending and reappears after the 1h client snooze — unchanged, expected behavior.
