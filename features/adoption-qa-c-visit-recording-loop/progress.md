# Progress: Adoption QA — Batch C (visit-recording loop)

**Started:** 2026-07-24
**Type:** Small
**Branch:** feature/windows-desktop-app

## Status
- [x] Implementation
- [x] Quality checks (dotnet build 0/0; web tsc 0; next build ok)
- [ ] Tests (handled by /test-small-feature)

## Files Changed
- `api/.../Features/Patients/Commands/CreateDentalRecordCommand.cs` — C2 optional `AppointmentId`; post-commit `CompleteReviewedAppointmentAsync` (MarkVisitCompleted + CancelPostVisitReview + broadcast "appointments"); injected IAppointmentRepository/INotificationGenerator/IRealtimeNotifier/ILogger. Mirrors CreateMedicalDocumentCommand.
- `web/lib/api/dental-records.ts` — `appointmentId` on the create request.
- `web/components/patient-record-modal.tsx` — `appointmentId` prop threaded into the create payload (create only).
- `web/app/patients/[id]/page.tsx` — C1 reads `?addRecord=1&appointmentId=` (window.location + replaceState, no useSearchParams), opens the modal, threads the id, clears on close.

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Used `window.location.search` + `history.replaceState` (not `useSearchParams`) | Matches the appointments/stock deep-link pattern; avoids the Next 15 Suspense-boundary build error. |
