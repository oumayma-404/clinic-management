/define-small-feature Harden and fix bugs in existing features before adding new ones — a single cleanup/hardening pass across the current clinic-management app. No new user-facing features; only make what exists work correctly and securely. Group all of the following into one small feature:

## 1. Cross-clinic data isolation (highest priority — systemic security bug)
Multi-tenancy is enforced inconsistently. Several command/query handlers fetch an entity by raw Id without verifying it belongs to the caller's clinic (no IClinicContext injected), letting an authenticated user in clinic A read/modify clinic B's data by passing B's GUID. There is no EF global query filter as a backstop.
DECISION (pre-answered): implement BOTH layers — (a) an EF Core global query filter (HasQueryFilter by ClinicId) as a defense-in-depth backstop so this class of bug cannot recur, AND (b) explicit per-handler clinic checks. Copy the correct pattern already used in GetPatientQuery / GetPatientsQuery / the Stock commands.
Handlers confirmed vulnerable:
- api/ClinicManagement.Application/Features/Patients/Commands/UpdatePatientCommand.cs (no IClinicContext at all)
- api/ClinicManagement.Application/Features/Appointments/Commands/UpdateAppointmentCommand.cs:50
- api/ClinicManagement.Application/Features/Appointments/Commands/CreateAppointmentCommand.cs:70 (verify referenced patient.ClinicId == caller clinic)
- api/ClinicManagement.Application/Features/Documents/Queries/GetMedicalDocumentQuery.cs:26
- api/ClinicManagement.Application/Features/Documents/Queries/GetMedicalDocumentsQuery.cs:31 (filters only by PatientId)
- api/ClinicManagement.Application/Features/Documents/Commands/UpdateMedicalDocumentCommand.cs:53
- api/ClinicManagement.Application/Features/Documents/Commands/DeleteMedicalDocumentCommand.cs:30
- api/ClinicManagement.Application/Features/Patients/Commands/CreatePatientMedicalHistoryCommand.cs:38 and the sibling family-history / dental-record create/update/delete commands
- api/ClinicManagement.Application/Features/ProcedureTypes/Commands/UpdateProcedureTypeCommand.cs:44 and DeleteProcedureTypeCommand.cs:37
Note for the global query filter: it must resolve the current clinic per-request (same IClinicContext → User.ClinicId lookup handlers use). Ensure it does not break legitimate cross-cutting paths (Local-mode fallback policy, background jobs / PdfGenerationJob / Google sync that run outside a request scope, and the reset-admin-password CLI). Where a handler must bypass the filter (e.g. background jobs with no clinic context), use IgnoreQueryFilters explicitly and document why.

## 2. Auth gap in Cloud mode
api/ClinicManagement.API/Controllers/GoogleCalendarController.cs has no class-level [Authorize]; only authorize/callback are [AllowAnonymous]. In Cloud mode the fallback policy is null, so sync-from-google / status / sync-appointment/{id} are reachable unauthenticated. Add [Authorize] at the class level, keeping [AllowAnonymous] only on authorize/callback.

## 3. OAuth state validation (pre-answered: INCLUDE in this pass)
The Google OAuth `state` parameter is generated but never validated on callback (GoogleCalendarController.cs:196,228) — a CSRF gap. Fix it in this pass: persist the generated state (per-user/session or the existing token store) and reject the callback if the returned state is missing or does not match.

## 4. Data-integrity bugs
- Notes corruption on Google Calendar round-trip: App→Google writes a multi-line "Doctor/Notes/Status/Patient ID" block as the event description (GoogleCalendarSyncService.cs:318), but Google→App does appointment.Notes = googleEvent.Description (GoogleCalendarSyncService.cs:449), overwriting the clean Notes field with the whole blob on every sync-from-google. Fix so the user's Notes are preserved (parse back the Notes line, or store/compare only the Notes portion).
- Orphaned file blobs: DeleteMedicalDocumentCommand.cs:30 deletes the DB row but never deletes document.FileId from IFileStorage, leaking the PDF blob. Delete the blob too (mirror the orphan-cleanup pattern used on upload failure).
- Insurance can't be cleared: UpdatePatientCommand.cs:100-105 has an empty "clear insurance" branch, so sending null silently keeps stale insurance. Implement clearing.

## 5. Frontend / smaller bugs
- web/lib/hooks/use-clinic-access.ts:71-84 — the catch treats any fetch failure (500 / transient network blip) as "no clinic" and redirects an authenticated member to /setup. Only redirect on an actual "no clinic / not a member" response, not on transient errors.
- Remove dead/404 client methods appointmentsApi.get and appointmentsApi.delete (web/lib/api/appointments.ts:15,51) — they call routes that don't exist on the controller and are unused.
- Remove leftover debug console.log calls: use-clinic-access.ts:54,58; setup-wizard.tsx:225; join-wizard.tsx:106; medical-documents.ts:43,57,59; clinic-settings.tsx:397. Keep legitimate console.error handlers.

## 6. Stale docs to correct in the same pass
- CLAUDE.md, web/CLAUDE.md, web/components/CLAUDE.md claim dashboard stats, appointment-list, and the whole stock feature use hardcoded sample data — this is false; all three are API-wired. Only notifications-list is still static. Correct these claims.
- web/CLAUDE.md:46 and web/components/CLAUDE.md:35 reference old /api/auth/* paths; code uses /bff/auth/*. Correct.

## Explicit scope boundaries (call these out as decisions in the spec)
- Notifications (fake hardcoded UI, empty backend feature folders, disabled NotificationJob), AI patient-summary (dead), the stubbed working-hours save (clinic-settings.tsx:392-408), and adding patient delete are NOT part of this pass — they are new/unfinished features, not bugs. For the fake notifications UI, the only in-scope action is to hide/disable it (or clearly mark it non-functional) so it doesn't mislead users; do not build the feature.

Goal: after this pass, everything that currently exists works correctly and securely, with no cross-tenant leaks, no data corruption, no CSRF gap on OAuth, and no misleading stubbed UI.
