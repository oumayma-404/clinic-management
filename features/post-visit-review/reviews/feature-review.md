# Feature Review: post-visit-review

**Status:** COMPLETE
**Challenged:** Yes
**Date:** 2026-07-17
**Challenged Date:** 2026-07-17
**Parent Branch:** feature/windows-desktop-app (working tree — feature is uncommitted)
**Merge Base:** n/a — reviewed the uncommitted working tree (whole feature is unstaged/untracked, absent from any commit)
**Files Reviewed:** 27 tracked-modified + 4 new untracked feature files (+348 / −13 on tracked; new files read in full). Excluded per progress.md "Working tree note": `.gitignore`, `CLAUDE.md`, `*.csproj`, `appsettings.json`, `define-small-feature-prompt.md`, `features/LEARNINGS.md`, `features/notification-center/*`, `packaging/server/clinic-server.iss`, `web/Dockerfile`, `CLINIC-FEATURES-OVERVIEW.md`. EF migration (`20260717081751_AddPostVisitReview.*`) checked for correctness but not line-reviewed (generated).
**Review method:** 4 parallel agents adapted to the stack (Backend Quality+CQRS/`Result<T>` — ROP agent dropped, this repo has no Extensions.ROP; Business-Logic correctness vs. AC-1…AC-8; Breaking-Changes with mandatory cross-boundary field tracing; Frontend React/Next/TS) + orchestrator cross-boundary tracing of both threaded fields.

## Challenge Summary

| Metric | Count |
|--------|-------|
| Original findings | 12 |
| Confirmed | 11 |
| Confirmed (adjusted) | 1 |
| Dismissed (false positive) | 0 |
| Dismissed (pre-existing) | 0 |
| **Final findings** | 12 |

All findings were verified against the full source files. Every finding held up; one had its severity lowered (Finding 8). No false positives.

## Findings

### Finding 1
- **Severity:** Major
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Appointments/Commands/UpdateAppointmentCommand.cs
- **Line:** 257-269
- **Anchor:** UpdateAppointmentCommandHandler.Handle (post-visit review block)
- **Comment:** A transition to **NoShow** leaves the pending post-visit review active. The block only cancels on `becameCancelled` and only re-ensures for Scheduled/Confirmed/InProgress; NoShow falls through untouched. Consequence: an appointment marked NoShow still surfaces its review at the end time and prompts the doctor with "La visite de {patient} est terminée. Ajoutez son dossier médical." for a visit that never happened. A no-show has no documentable visit — add `AppointmentStatus.NoShow` to the cancel path (`CancelPostVisitReviewAsync`), alongside `becameCancelled`. (Same class of side-effect the cancel path already handles; NoShow was simply omitted.)
- **Verification:** Confirmed against source. `becameCancelled` (line 224-225) is the only trigger for `CancelPostVisitReviewAsync`; the `else if` (line 262-264) covers only Scheduled/Confirmed/InProgress. A NoShow transition matches neither branch, so an existing (possibly future-dated) review is left in place and surfaces at the end time. `Appointment.MarkVisitCompleted()` correctly no-ops on NoShow (AC-7), but the *notification* is never cleared.

### Finding 2
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Common/Services/NotificationGenerator.cs
- **Line:** 238-247
- **Anchor:** NotificationGenerator.ResolveTargetUserIdAsync
- **Comment:** The doctor lookup (`_doctors.GetByIdAsync(doctorGuid)`) is not scoped to the appointment's clinic. AC-2's fallback is "DoctorId null / doctor has no linked user → TargetUserId null → all staff see it." If `Appointment.DoctorId` (a free-text, unvalidated `string?`) parses to a Guid that resolves to a doctor in a *different* clinic with a linked user, `TargetUserId` is set to that foreign user; the feed/pending queries filter on `ClinicId == clinicId` **and** `TargetUserId == userId`, so no one in the appointment's clinic matches and the review becomes invisible to everyone — silently violating the all-staff fallback. Cross-clinic Guid collisions are practically impossible via normal flows (so low likelihood), but the correct degrade is to null out the target when the resolved doctor's clinic differs, so an unresolvable/foreign doctor falls back to all-staff rather than a dead notification.
- **Verification:** Confirmed against source. `ResolveTargetUserIdAsync` returns `doctor?.UserId` with no clinic check. `Doctor` carries a `ClinicId` (Domain/Entities/Doctor.cs:7), so the suggested degrade (compare the resolved doctor's `ClinicId` to the appointment's clinic, null the target on mismatch) is feasible. Note the method's signature doesn't currently receive the clinic id — the fix needs to thread it in from `EnsurePostVisitReviewAsync`.

### Finding 3
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Appointments/Commands/UpdateAppointmentCommand.cs
- **Line:** 266-268
- **Anchor:** UpdateAppointmentCommandHandler.Handle (EnsurePostVisitReviewAsync call)
- **Comment:** AC-4's "doctor change recomputes TargetUserId" is not reachable through this command. `UpdateAppointmentCommand` exposes no `DoctorId` (only `DoctorName`) and the handler never mutates `appointment.DoctorId`, so `EnsurePostVisitReviewAsync` is always re-invoked with the unchanged `DoctorId` — the target user can never actually change on update. The recompute machinery (`MovePostVisitReview` refreshing `TargetUserId`) is correct but effectively dead for the doctor-change scenario. Either the update surface needs to accept a doctor change, or the AC clause can't be satisfied by the current command.
- **Verification:** Confirmed against source. `UpdateAppointmentCommand` (lines 13-22) has only `DoctorName`, no `DoctorId`; the handler calls `UpdateDoctorName` (line 115) which changes the display string, not the `DoctorId` used for target resolution. No other doctor-reassignment path exists in this command. This is a spec/AC coverage gap, not a code defect in isolation — the recompute machinery is correct but unreachable. Resolve by either extending the update surface or amending AC-4's edge case.

### Finding 4
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Notifications/Queries/GetPendingReviewsQuery.cs
- **Line:** 56-59
- **Anchor:** GetPendingReviewsQueryHandler.Handle (catch block)
- **Comment:** The catch swallows the exception into `Result.Failure($"Error retrieving pending reviews: {ex.Message}")` with no logging, and the raw `ex.Message` is then surfaced to the client via the controller's `BadRequest(result.Error)` (minor info leak). This **mirrors the sibling handlers** `GetNotificationsQuery` and `GetUnreadCountQuery` verbatim — it is consistent with the existing (imperfect) area convention, not a new violation. Best practice is to inject `ILogger<GetPendingReviewsQueryHandler>`, `_logger.LogError(ex, "Error retrieving pending reviews for user {UserId}", userId)`, and return a generic operator message; if pursued, fix the two siblings too for consistency.
- **Verification:** Confirmed against source. The precedent claim was checked: `GetNotificationsQuery.cs:70` does exactly the same (`Result.Failure($"Error retrieving notifications: {ex.Message}")`, no logging). So this conforms to the area convention — a real but consistent Minor. Since the new handler follows the established (imperfect) pattern, treat the fix as an area-wide cleanup, not a feature defect: fix all three together or none.

### Finding 5
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/DTOs/PendingReviewDto.cs
- **Line:** 28
- **Anchor:** PendingReviewMappingExtensions.ToPendingReviewDto
- **Comment:** `PatientName` is declared on the DTO but the mapper hard-codes it to `null` and nothing else populates it — a dead/misleading field (the popup already renders `message`, which embeds the patient name). Either remove `PatientName` from the DTO and the API contract, or populate it from a real source. An always-null property invites frontend consumers to bind to data that never arrives.
- **Verification:** Confirmed against source. Line 28 is `PatientName = null` and no other code path sets it; the popup (`post-visit-review-popup.tsx`) renders `active.message`, never `patientName`. The API contract in spec.md marks `patientName?` optional, so removing it is contract-compatible.

### Finding 6
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/DTOs/PendingReviewDto.cs
- **Line:** 29
- **Anchor:** PendingReviewMappingExtensions.ToPendingReviewDto
- **Comment:** `CreatedAt` is mapped from `notification.EffectiveFeedTime` (the deferred visible-at time = appointment end), not the notification's real `CreatedAt`. The name misleads any consumer reading it as creation time. Rename the DTO property to its meaning (e.g. `VisibleAt`) or map the true `CreatedAt`, whichever the popup needs (the popup currently doesn't display it).
- **Verification:** Confirmed against source. Line 29 is `CreatedAt = notification.EffectiveFeedTime`. The popup does not read this field today, so a rename is low-risk; the mismatch is purely a naming/clarity defect.

### Finding 7
- **Severity:** Minor
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** web/app/documents/page.tsx
- **Line:** 67-71
- **Anchor:** DocumentsPage / linkSuffix effect
- **Comment:** `linkSuffix` is seeded to `""` and only populated in a post-mount `useEffect(..., [])`. A template card clicked between first paint and effect commit navigates to `/documents/<type>` with no `appointmentId`, silently dropping the deep-link (record won't be tied to the visit, appointment won't be marked Completed). Human reaction time makes this near-impossible to hit, but it's a genuine ordering race. Safer: derive the suffix synchronously during render from `window.location.search` (SSR-guarded), or read it in the click handler.
- **Verification:** Confirmed against source. `linkSuffix` initial state `""` (line 67), populated only in `useEffect` (lines 68-71), consumed in the card `onClick` (line 104). The race is real though practically unreachable. Note the effect already `encodeURIComponent`s the id (line 70) — the fix here is about *timing*, not encoding (see Finding 11 for the popup-side encoding).

### Finding 8
- **Severity:** Suggestion
- **Category:** Frontend
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** web/components/post-visit-review-popup.tsx
- **Line:** 115-124
- **Anchor:** PostVisitReviewPopup / handleAddRecord (missing-appointmentId path)
- **Comment:** When `active.appointmentId` is null/undefined (`PendingReviewDto.appointmentId` is typed optional), the handler snoozes then pushes to `/documents` with no id; the created document carries no `appointmentId`, so the backend never marks the visit Completed and the review stays pending server-side — after the 1h snooze the same popup returns indefinitely with no way to clear it. If absent is truly possible, don't snooze-and-navigate a review that can't be fulfilled.
- **Challenge note:** Severity lowered Minor → Suggestion. Verified this is defensive/near-dead code: a `PostVisitReview` is *always* constructed with a non-null `appointmentId` (`NotificationGenerator.EnsurePostVisitReviewAsync` always passes `appointmentId: appointmentId`, lines 199-207), and the DTO maps straight from `notification.AppointmentId`. The optional type is the only reason the branch exists; the described "popup returns forever" state is not reachable through any real generation path. Still worth the cheap guard, but not a delivered-behavior defect.

### Finding 9
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Documents/Commands/CreateMedicalDocumentCommand.cs
- **Line:** 245-250
- **Anchor:** CreateMedicalDocumentCommandHandler.CompleteReviewedAppointmentAsync
- **Comment:** This side-effect flips the appointment to `Completed`, but `RealtimeBroadcastBehavior` derives its broadcast from the command's area ("documents"), so appointment/calendar views aren't told to refetch and keep showing the stale (non-Completed) status until another trigger. Consider broadcasting the "appointments" realtime key here (the notification-cancel already broadcasts "notifications") so calendar views reflect the completion.
- **Verification:** Confirmed against source. `CompleteReviewedAppointmentAsync` (lines 245-250) mutates + saves the appointment and cancels the review (which broadcasts "notifications" via `NotificationGenerator.SafelyAsync`), but nothing broadcasts the "appointments" key. `RealtimeBroadcastBehavior` will broadcast "documents" for this command (per the Application CLAUDE.md structural rule), not "appointments". Calendar/appointment views stay stale until the next unrelated refetch.

### Finding 10
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Documents/Commands/CreateMedicalDocumentCommand.cs
- **Line:** 246
- **Anchor:** CreateMedicalDocumentCommandHandler.CompleteReviewedAppointmentAsync
- **Comment:** The explicit `_appointmentRepository.UpdateAsync(appointment, ...)` is redundant — `appointment` is already change-tracked from `GetByIdAsync`, so the subsequent `SaveChangesAsync` persists `MarkVisitCompleted()` on its own (per the repo "rely on EF change tracking" convention). It mirrors existing `UpdateAsync` usage elsewhere, so low-priority cleanup, not a defect.
- **Verification:** Confirmed against source. Line 245-247 loads via `GetByIdAsync`, calls `MarkVisitCompleted()`, then `UpdateAsync` + `SaveChangesAsync`. The `UpdateAsync` is a no-op given EF tracking, but the pattern is used across the codebase (e.g. `UpdateAppointmentCommand.cs:212`), so this is consistent-with-convention cleanup, not a bug.

### Finding 11
- **Severity:** Suggestion
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** web/components/post-visit-review-popup.tsx
- **Line:** 120
- **Anchor:** PostVisitReviewPopup / handleAddRecord
- **Comment:** `router.push(\`/documents?appointmentId=${active.appointmentId}\`)` doesn't `encodeURIComponent` the id, whereas the receiving page re-encodes it. The value is a server GUID so it's safe today; encode here too for symmetry to avoid a latent footgun if the id shape ever changes.
- **Verification:** Confirmed against source. Line 120 interpolates the raw id; the receiving `documents/page.tsx:70` does `encodeURIComponent`. GUIDs are URL-safe so no live bug, purely defensive symmetry.

### Finding 12
- **Severity:** Suggestion
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** web/components/post-visit-review-popup.tsx
- **Line:** 107-113 / 39-46
- **Anchor:** PostVisitReviewPopup / snooze + saveSnooze
- **Comment:** The persisted `SnoozeMap` in localStorage (`clinic:pvr-snooze`) is never pruned — expired entries and entries for long-fulfilled reviews accumulate forever. Not a correctness bug (expired entries compare `<= now` and are ignored), but on a shared/long-lived browser the blob grows unbounded. Drop entries whose timestamp `<= Date.now()` when writing in `snooze`, or filter against the current `reviews` set.
- **Verification:** Confirmed against source. `snooze` (lines 107-113) merges a new entry and `saveSnooze` (lines 39-46) writes the whole map with no pruning. Unbounded growth is real but harmless to correctness — a housekeeping improvement.

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| Major | 1 |
| Minor | 6 |
| Suggestion | 5 |
| **Total** | 12 |

**Cross-boundary tracing (threaded fields) — verified clean:** `MedicalDocument.AppointmentId` is carried at every hop (frontend JSON + multipart send → `CreateMedicalDocumentRequest` → controller both bind paths → command → entity ctor → EF config + index → migration → all read DTOs: Create/Update/Get/GetList + frontend `MedicalDocumentDto`). `StaffNotification.TargetUserId` is a trailing optional ctor param (no caller breakage); repo predicates add `(TargetUserId == null || == userId)` so existing null-target rows stay clinic-wide (AC-8). `NotificationCategory.PostVisitReview = 6` is appended (no renumbering). Migration additive/nullable, matches snapshot, no phantom FK. New endpoint inherits `[Authorize]` (no allow-list drift). Target-user identity is consistent — `Doctor.UserId` and `IClinicContext.GetUserId()` are both the `User.Id` string (Auth0 sub / `local|{guid}`).
