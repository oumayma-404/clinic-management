# Spec: Adoption QA — Batch C (visit-recording loop)

**Status:** APPROVED
**Type:** Small (forced multi-item pass — user fed the full adoption-QA blueprint)
**Created:** 2026-07-24
**Scope:** Full
**Branch:** feature/windows-desktop-app (existing)
**Feature:** Make the "record the finished visit" bell actually work — the dead deep-link (#4) and the prompt that only closes via a medical *document* not a dental *record* (#10). Ship C1+C2 together.

## Overview
The post-visit bell sends the user to `/patients/{id}?addRecord=1&appointmentId=…`, but the patient page never reads those params, so nothing opens. And even after recording, the prompt only clears when a *medical document* with an appointment link is saved — charting a dental record (the natural clinical action) leaves the bell nagging. This batch closes both halves of that one loop.

## What Changes
- **C1 — Read the deep-link.** `patients/[id]/page.tsx` reads `useSearchParams()`; when `addRecord=1`, it opens the record modal (`setRecordModalOpen(true)`) and threads `appointmentId` into `<PatientRecordModal>`. Copy the `?param` / `clinic:deeplink` handling already on `/appointments` and `/stock`.
- **C2 — Dental record closes the prompt.** `CreateDentalRecordCommand` gains an optional `AppointmentId`; after `SaveChangesAsync` it best-effort marks the appointment visit-completed and dismisses the PostVisitReview notification. `PatientRecordModal` threads the deep-link `appointmentId` through the dental-record save. Copy `CreateMedicalDocumentCommand.CompleteReviewedAppointmentAsync` (`:274-307`): re-resolve clinic, tenant-check, `MarkVisitCompleted()`, `CancelPostVisitReviewAsync`, broadcast `"appointments"`.

## API Contract
### POST /api/patients/{id}/dental-records  (modify existing)
Request: existing body **+** optional `appointmentId: Guid?`
Response 2XX: unchanged (dental record DTO)
Behavior: when `appointmentId` present and valid for the clinic, the linked appointment is completed and its post-visit prompt is dismissed.

## Acceptance Criteria
- **AC-1:** Clicking the "Compte rendu de visite" bell navigates to the patient page **and** the add-record modal opens automatically (from `?addRecord=1`).
- **AC-2:** The `appointmentId` from the deep-link is threaded into the record modal and submitted with the save.
- **AC-3:** Saving a dental record that carries a valid `appointmentId` marks that appointment visit-completed and removes the PostVisitReview notification (the bell stops nagging).
- **AC-4:** Saving a dental record with no `appointmentId` behaves exactly as before (no appointment side-effect).
- **AC-5:** `CreateDentalRecordCommand` rejects an `appointmentId` belonging to another clinic (tenant-isolation assertion).

## Out of Scope
- Persisting `appointmentId` as a stored column on `DentalRecord` (used only to drive the side-effect).
- Changing how the medical-document path closes the prompt (unchanged).

## Edge Cases (Critical only)
- Side-effect is best-effort post-commit — a failure to close the prompt must not fail the dental-record save.
- Re-opening an already-completed appointment's deep-link must no-op gracefully (prompt already gone).
