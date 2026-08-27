# Progress: Post-Visit Review & Medical-Record Prompt

**Started:** 2026-07-17
**Type:** Small
**Branch:** feature/windows-desktop-app (user-confirmed; ongoing working branch where notification-center — which this builds on — also lives)

## Status
- [x] Implementation
- [x] Quality checks (build, lint, typecheck)
- [x] Tests (added/modified — see Test Plan below)

## Working tree note (start of session)
Pre-existing unrelated uncommitted/untracked files (NOT part of this feature — excluded from any commit):
`.gitignore`, `CLAUDE.md`, `api/ClinicManagement.API/ClinicManagement.API.csproj`, `api/ClinicManagement.API/appsettings.json`,
`define-small-feature-prompt.md`, `features/LEARNINGS.md`, `features/notification-center/stories/progress.md`,
`packaging/server/clinic-server.iss`, `web/Dockerfile`, `CLINIC-FEATURES-OVERVIEW.md`,
`features/notification-center/retrospective.md`, `features/notification-center/reviews/feature-review.md`.

## Design decisions
- **Target resolution** (`Appointment.DoctorId` → `Doctor` → `UserId`) lives in `NotificationGenerator` (injects `IDoctorRepository`), so handlers stay thin and both create/update reuse it. `DoctorId` is `string?`; parsed via `Guid.TryParse` → null target (all staff) on any miss.
- **One idempotent generator method** `EnsurePostVisitReviewAsync` (create-or-move) covers create, reschedule, duration change, doctor change, reactivation — called whenever the appointment is in an active state post-commit. `CancelPostVisitReviewAsync` removes it on cancel and after record-fill completion.
- **Popup navigation**: popup → `/documents?appointmentId=Y` → gallery forwards param to `/documents/{type}?appointmentId=Y` → editor fetches the appointment to preselect the patient and passes `appointmentId` to create. Avoids adding patient resolution to the pending-reviews query.
- **Snooze** is client-side (localStorage, per notification id, 1h window) — never marks the notification read (AC-5).
- **Completion side-effect** (record-fill → appointment Completed + review removed) is best-effort/post-commit in the documents handler; logs at Error, never fails the record creation (AC-7 + notification-center learning).

## Files Changed
**Backend — Domain**
- `Enums/NotificationCategory.cs` — `PostVisitReview = 6`.
- `Entities/StaffNotification.cs` — `TargetUserId` (string?, ctor param) + `MovePostVisitReview(...)`.
- `Entities/Appointment.cs` — `MarkVisitCompleted()` (Scheduled/Confirmed/InProgress → Completed; idempotent no-op otherwise).
- `Entities/MedicalDocument.cs` — `AppointmentId` (Guid?, ctor param).
- `Repositories/IStaffNotificationRepository.cs` — `GetPostVisitReviewByAppointmentAsync` + `GetPendingReviewsForUserAsync`.

**Backend — Infrastructure**
- `Repositories/StaffNotificationRepository.cs` — `TargetUserId` visibility in `GetRecentForUserAsync`/`UnreadQuery`; two new methods.
- `Persistence/Configurations/StaffNotificationConfiguration.cs` — `TargetUserId` (maxLength 255).
- `Persistence/Configurations/MedicalDocumentConfiguration.cs` — `AppointmentId` + index.
- `Migrations/20260717081751_AddPostVisitReview.*` + snapshot — additive `StaffNotifications.TargetUserId`, `MedicalDocuments.AppointmentId` (+ index). Generated with `dotnet ef` (WDAC did not block it here).

**Backend — Application**
- `Common/Interfaces/INotificationGenerator.cs` + `Common/Services/NotificationGenerator.cs` — `EnsurePostVisitReviewAsync`/`CancelPostVisitReviewAsync` + `IDoctorRepository`-backed target resolution + FR strings.
- `Features/Appointments/Commands/CreateAppointmentCommand.cs` + `UpdateAppointmentCommand.cs` — schedule/ensure/cancel post-visit review post-commit.
- `Features/Documents/Commands/CreateMedicalDocumentCommand.cs` — `AppointmentId` + best-effort completion side-effect.
- `Features/Documents/{Queries/GetMedicalDocumentQuery,Queries/GetMedicalDocumentsQuery,Commands/UpdateMedicalDocumentCommand}.cs` + `DTOs/MedicalDocumentDto.cs` — `AppointmentId` mapping.
- `DTOs/PendingReviewDto.cs` (new), `Features/Notifications/Queries/GetPendingReviewsQuery.cs` (new).

**Backend — API**
- `Controllers/NotificationsController.cs` — `GET /notifications/pending-reviews` ([Authorize]; no allow-list change).
- `Controllers/MedicalDocumentsController.cs` + `Models/CreateMedicalDocumentRequest.cs` — `appointmentId` (JSON + multipart).

**Frontend**
- `lib/api/types.ts` — `PendingReviewDto`; `appointmentId?` on `MedicalDocumentDto`.
- `lib/api/notifications.ts` — `pendingReviews()`.
- `lib/api/medical-documents.ts` — `appointmentId` on create request (JSON + FormData).
- `components/notification-panel.tsx` — `PostVisitReview` icon (`ClipboardPlus`).
- `components/post-visit-review-popup.tsx` (new) — polls pending reviews, snooze (localStorage, 1h), deep-link.
- `components/dashboard-header.tsx` — mounts the popup.
- `app/documents/page.tsx` — forwards `appointmentId` to the template editor link (via `window.location.search`).
- `components/document-editor-content.tsx` — reads `appointmentId`, preselects the appointment's patient, sends it on create.

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Added a mocked `IDoctorRepository` to `NotificationGenerationTests.GeneratorHarness` so the project compiles against the new `NotificationGenerator` ctor. | Build-required compile fix in test infrastructure (skill exception) — NOT a test-scenario change. New/changed test scenarios (post-visit generation rules, target resolution, completion side-effect) are deferred to `/test-small-feature`. |

## Significant Deviations
(none)

## Quality checks
- `dotnet build ClinicManagement.sln` → 0 errors; all warnings pre-existing (CS8618/CS8602/CS8981), none in changed files.
- `npx tsc --noEmit` (web) → clean.
- `npm run build` (web) → success; `/documents` still statically prerendered.

## Test Plan (/test-small-feature)
Repo has a single test project (`ClinicManagement.UnitTests`, xUnit + Moq) and **no** integration/Testcontainers or Postman/Newman harness — so this is a unit-level pass. Each AC is a test row or an explicit coverage note.

| AC | Action | Target file | Notes |
|----|--------|-------------|-------|
| AC-1 | Add scenarios | `Features/Notifications/NotificationGenerationTests.cs` | Generator creates a `PostVisitReview` due at the appointment end; `CreateAppointmentCommandHandler` schedules a review at start+duration with a patient, and **nothing** for a patient-less busy slot. |
| AC-2 | Add scenarios | `Features/Notifications/NotificationGenerationTests.cs` | Target resolution matrix: linked doctor → that user; doctor w/o user, null id, unparsable id → all staff (no doctor lookup for unparsable). |
| AC-3 | Add scenario (+note) | `Features/Notifications/NotificationGenerationTests.cs` | `EffectiveFeedTime == appointmentEnd` asserted + future-dated → no realtime broadcast. **Coverage note:** the due-ness filter (`EffectiveFeedTime <= now`) is the pre-existing EF `UnreadQuery`/`GetPendingReviewsForUserAsync` predicate — no repo-level unit harness exists; unchanged deferred-visibility mechanism. |
| AC-4 | Add scenarios + modify existing | `Features/Notifications/NotificationGenerationTests.cs` | Generator: `EnsurePostVisitReviewAsync` moves an existing review (recomputes target) instead of adding; `CancelPostVisitReviewAsync` removes / no-ops. Update-handler tests now also assert cancel → `CancelPostVisitReviewAsync`, reschedule → `EnsurePostVisitReviewAsync` at new end. |
| AC-5 | Coverage note | — | FE-only client-side snooze (localStorage, per-browser). No FE test framework wired in `web/` → covered by the `tsc --noEmit` + `npm run build` gate run at implementation time. |
| AC-6 | Coverage note | — | FE popup navigation / patient preselect. Its backend surface (record carries `AppointmentId`) is covered by AC-7; FE nav has no unit surface here. |
| AC-7 | New test class | `Features/Documents/PostVisitReviewCompletionTests.cs` | `Appointment.MarkVisitCompleted` state matrix (Scheduled/Confirmed/InProgress → Completed; Cancelled/Completed/NoShow no-op) + `CreateMedicalDocumentCommandHandler` completes the appointment & cancels its review, is a silent no-op cross-clinic / when no `AppointmentId`, and **succeeds even if the side-effect throws** (best-effort). |
| AC-8 | Modify existing (+note) | `Features/Notifications/NotificationGenerationTests.cs` | `AppointmentCreated` asserts `TargetUserId == null` (existing categories stay clinic-wide). **Coverage note:** the additive `(TargetUserId == null || == user)` visibility predicate is EF-level in `StaffNotificationRepository` — no repo unit harness. |

## Tests Run
| Suite | Filter | Result |
|-------|--------|--------|
| Unit | `FullyQualifiedName~NotificationGenerationTests\|FullyQualifiedName~PostVisitReviewCompletionTests` | 33 passed, 0 failed, 0 skipped |

- `dotnet build ClinicManagement.UnitTests` → 0 errors, 0 warnings.
- Smart App Control did **not** block this run — the DLL loaded and executed (33/33 green).
- No Postman/Newman (user preference); no E2E/integration harness exists in this repo.

## Review Fixes Applied (/apply-review-fixes, 2026-07-17)
Challenged all 12 confirmed findings against live source; applied 11 (1 Major + 5 Minor + 5 Suggestion), deferred 1. Left **uncommitted** (Type: Small, manual-commit → `/finish-feature` picks up). Re-ran gate: build 0/0, `tsc` clean, 33/33 unit tests green.

| # | Sev | Fix |
|---|-----|-----|
| 1 | Major | `UpdateAppointmentCommand` — NoShow transition now also cancels the pending post-visit review (was left surfacing for a visit that never happened). |
| 2 | Minor | `NotificationGenerator.ResolveTargetUserIdAsync` — threads clinicId; a doctor resolved from another clinic degrades to all-staff (null) instead of a dead, invisible review. |
| 4 | Minor | `GetPendingReviewsQuery` — catch now logs at Error + returns a generic message (was leaking `ex.Message`, no log). Aligns with the repo-wide logging convention rather than the imperfect notification-query siblings. |
| 5 | Minor | `PendingReviewDto` — removed always-null `PatientName` (BE + FE type). |
| 6 | Minor | `PendingReviewDto` — renamed misleading `CreatedAt` → `VisibleAt` (it maps `EffectiveFeedTime`; BE + FE type). |
| 7 | Minor | `documents/page.tsx` — deep-link suffix computed at click time (removed the `linkSuffix` mount-effect race that could drop `appointmentId`). |
| 8 | Suggestion | `post-visit-review-popup` — `handleAddRecord` guards a missing `appointmentId` (defensive; PVR is always generated with one). |
| 9 | Suggestion | `CreateMedicalDocumentCommand.CompleteReviewedAppointmentAsync` — broadcasts the `"appointments"` realtime key so calendar views refetch the now-Completed status. |
| 10 | Suggestion | Same method — dropped the redundant `UpdateAsync` (relies on EF change tracking). |
| 11 | Suggestion | `post-visit-review-popup` — `encodeURIComponent` on the deep-link id (defensive symmetry). |
| 12 | Suggestion | `post-visit-review-popup` — prunes expired snooze entries on write (was unbounded localStorage growth). |

**Deferred — #3 (Minor, spec-coverage gap):** AC-4's "doctor change recomputes `TargetUserId`" is unreachable — `UpdateAppointmentCommand` exposes only `DoctorName`, never `DoctorId`. No in-scope code defect (the `MovePostVisitReview` recompute machinery is correct, just never triggered on a doctor change). **Chosen resolution: amend AC-4 to drop the doctor-change edge**, not expand the update surface — adding doctor reassignment is a separate feature (new command field + controller binding + FE) out of this Type:Small's scope. To revisit if/when appointment doctor-reassignment is specced.

Test edits required by the fixes: `PostVisitReviewCompletionTests` — added `IRealtimeNotifier` mock to the harness (ctor change, #9); happy-path now asserts the `"appointments"` broadcast instead of the removed `UpdateAsync` (#9/#10); cross-clinic no-op asserts no broadcast.
