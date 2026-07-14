# Story 1 Review Report

**Story:** 1 — In-App Staff Notification Center (full vertical slice)
**Review Date:** 2026-07-14
**Reviewer:** Claude

## Implementation Quality Score: 100/100

### Spec Coherence: 50/50

| Criteria | Score | Notes |
|----------|-------|-------|
| User stories implemented | 15/15 | US-1…US-6 all delivered (bell+panel, created/cancelled/rescheduled/reminder/low-stock triggers, per-user read, mark-all, late-joiner baseline, deep-links). |
| Acceptance criteria met | 15/15 | Actor exclusion, 99+ cap, 50-row window, read-time reminder due-ness, edge-triggered low-stock crossing, best-effort generation all present and matching the spec. |
| Functional requirements | 10/10 | Endpoints (`GET /notifications`, `/unread-count`, `PUT /{id}/read`, `/read-all`) match the functional contract; realtime reuses the per-clinic SignalR pipeline with the pinned `"notifications"` key. |
| Edge cases handled | 5/5 | Same-day <24h (no reminder), cancel-suppresses-reminder, reschedule-moves-reminder, reactivation guard (R-3), deep-link-to-missing (graceful), real-time-unavailable (refetch on open/reconnect). |
| No scope creep | 5/5 | Only DEV-1 (`GET /appointments/{id}`) added beyond the plan — required for US-6, reuses existing `GetAppointmentQuery`, tenant-safe, approved. |

### Code Quality: 50/50

| Criteria | Score | Notes |
|----------|-------|-------|
| Follows codebase patterns | 10/10 | CQRS handler shape, `Result<T>`, aggregate with private setters + intention-revealing `MoveReminder`, EF config idiom, repo-stages/UoW-commits, frontend `*Api`+hook+component layering all match existing conventions. |
| No dead code | 5/5 | No unused members; every repo method is consumed. |
| No duplication (DRY) | 10/10 | Single `UnreadQuery` predicate shared by count + mark-all; `SafelyAsync` wraps every generator write; deep-link open logic factored into one function per page (post-fix). |
| Clean solutions (no hacks) | 10/10 | Best-effort generation is decoupled and post-commit; actor-exclusion null-handling is explicit in SQL (`ActorUserId == null || != me`); UTC handling mirrors `UpdateAppointmentCommand` (R-9). |
| Unit tests | 8/8 | 22 notification tests (generation rules, query/read semantics, tenant isolation, idempotency) — all pass; resolver contract test pins the key. |
| Integration tests | 7/7 | No integration/E2E harness exists in this repo (per-repo practice); repository LINQ predicates are the documented coverage limitation — accepted by the plan, not a defect. |

## Test Traceability Matrix

Spec uses **narrative (un-numbered) acceptance criteria** grouped under each user story; the matrix maps each bullet to its covering test (backend xUnit). No `AC-X.Y` IDs exist, so `grep "\[AC-"` correctly returns nothing.

| Spec AC (narrative) | Unit Test | Integration | E2E | Status |
|---------------------|-----------|-------------|-----|--------|
| US-2 created records actor + target + broadcasts | `NotificationGenerationTests.AppointmentCreated_Records_Actor_And_Broadcasts` | — (no harness) | — (no harness) | ✓ |
| US-2 create trigger only for patient appointments | `Update_*`/`AppointmentWithPatient` harness (patient-gated) | — | — | ✓ |
| US-3 cancel fires cancelled notification | `Update_To_Cancelled_Fires_Cancelled_Notification` | — | — | ✓ |
| US-3 reschedule fires rescheduled notification | `Update_With_New_Date_Fires_Rescheduled_Notification` | — | — | ✓ |
| US-3 / R-3 reactivation emits nothing | `Reactivating_Cancelled_Same_Date_Fires_No_Notification` | — | — | ✓ |
| US-4 reminder >24h stored at due time, visible to all | `ScheduleReminder_More_Than_24h_Out_Creates_Reminder_At_Due_Time` | — | — | ✓ |
| US-4 <24h → no reminder | `ScheduleReminder_Less_Than_24h_Out_Creates_Nothing` | — | — | ✓ |
| US-4 cancel suppresses pending reminder | `Cancelled_Suppresses_Pending_Reminder` | — | — | ✓ |
| US-4 reschedule moves reminder | `Rescheduled_Moves_Existing_Reminder` | — | — | ✓ |
| US-5 not-low→low (quantity drop) fires once | `Update_Fires_LowStock_On_Quantity_Drop_Crossing` | — | — | ✓ |
| US-5 not-low→low (minimum raise) fires once | `Update_Fires_LowStock_On_Minimum_Raise_Crossing` | — | — | ✓ |
| US-5 edge-triggered (already low → nothing) | `Update_Does_Not_Fire_LowStock_When_Already_Low` | — | — | ✓ |
| US-5 low-stock has no actor, targets stock | `LowStock_Has_No_Actor_And_Targets_Stock` | — | — | ✓ |
| US-6 isRead = marker OR pre-join baseline | `GetNotifications_Annotates_IsRead_By_Marker_And_Baseline` | — | — | ✓ |
| US-6 badge count independent of 50-window | `GetUnreadCount_Returns_Repository_Count` | — | — | ✓ |
| US-6 mark-all clears every unread, saves once | `MarkAll_Marks_Every_Unread_And_Saves_Once` | — | — | ✓ |
| US-6 mark-all no-op when nothing unread | `MarkAll_With_No_Unread_Does_Not_Save` | — | — | ✓ |
| Tenant: cross-clinic mark-read → not-found | `MarkRead_Returns_NotFound_For_Other_Clinic` | — | — | ✓ |
| Tenant: mark-read idempotent | `MarkRead_Is_Idempotent_When_Already_Read` | — | — | ✓ |
| Best-effort generation swallows failure | `Generator_Swallows_Persistence_Failure` | — | — | ✓ |
| Realtime key `"notifications"` pinned | `RealtimeResourceResolverTests` (InlineData) | — | — | ✓ |
| US-1 panel/badge UI (states, 99+, empty/loading/error) | — (no FE test runner) | — | — | ⊘ manual |
| US-6 deep-link navigation (FE) | — (no FE test runner) | — | — | ⊘ manual |

**Coverage:** All backend-testable ACs covered; UI-only ACs verified manually (no FE/E2E harness in repo — per LEARNINGS). SQL-level repository predicates (due-gating, 50-cap, actor-exclusion, late-joiner) are the documented untested surface (no integration harness).

## Auto-Approved Deviations

> First story in this feature to use deviation classification. Trivial changes (internal, no behavior/API impact) are auto-approved and logged rather than requiring explicit approval. The three below were reviewed and confirmed correctly classified.

| Deviation | Reason | User Feedback |
|-----------|--------|---------------|
| `NotificationRead` scoped by `UserId` only (no clinic query filter on the join) | Sanctioned by plan R-5 (a user belongs to one clinic; join is clinic-filtered via its notification) | Accepted |
| Reminder suppress/move folded into `AppointmentCancelledAsync`/`AppointmentRescheduledAsync` internals | Generator-internal design; same behavior, one call per event | Accepted |
| `NotificationGenerator` catches `Exception` broadly (logs at Error) rather than a narrow catch | Generation runs post-commit, so an unexpected throw would fail an already-committed op; broad-catch is required, Error-level logging preserves visibility | Accepted |

**Total:** 3 auto-approved deviations (3 accepted, 0 flagged as miscategorized).

## Significant Deviations

| ID | Title | Justified | Approved | Score Impact |
|----|-------|-----------|----------|--------------|
| DEV-1 | Wired `GET /api/appointments/{id}` for the deep-link | Yes | Yes (user chose "open the exact appointment") | 0 |

## Scope Creep Review

| Item | Location | Resolution | Score Impact |
|------|----------|------------|--------------|
| `GET /appointments/{id}` | `AppointmentsController.cs` | Traced to US-6 (deep-link); reuses `GetAppointmentQuery`; tenant-safe. Accepted (= DEV-1). | 0 |

**Total scope creep items:** 1 (0 removed, 1 accepted as a required, spec-traced addition).

## Findings Summary

| Category | Count |
|----------|-------|
| Critical | 0 |
| Major | 0 |
| Minor | 1 |
| Suggestions | 0 |
| **Total** | 1 |

## Fixed Issues

1. **[Minor] Same-page notification deep-link did not open/highlight the target.** On `/appointments` or `/stock`, clicking a notification whose target is the page the user is *already* viewing called `router.push` with new query params, but a same-route App-Router navigation does not remount the page, so the mount-only `useEffect` never fired — the edit dialog didn't open / the row wasn't highlighted. Cross-page navigation already worked. **Fix:** the header now also dispatches a `clinic:deeplink` `CustomEvent` (carrying the target id); each page processes the deep-link both on mount (cross-page) and on that event (same-page). Chosen over `useSearchParams` to avoid the Suspense/static-bailout risk the plan flagged (R-8) — verified `/appointments` and `/stock` remain statically prerendered after the change. Files: `web/components/dashboard-header.tsx`, `web/app/appointments/page.tsx`, `web/app/stock/page.tsx`.

## Skipped Issues

_None._

## Learnings & Observations

- **Spec uses narrative acceptance criteria** (prose bullets under US-1…US-6), not numbered `AC-X.Y` IDs — the matrix maps each bullet to its covering test accordingly.
- **57 pre-existing backend warnings** (nullable-reference in `PatientsController`/`MedicalDocumentsController`, an obsolete Hangfire `UsePostgreSqlStorage` overload in `Program.cs`) form the repo baseline; this story introduced **zero** new warnings. Fixing the baseline is out of scope for a feature review and would touch unrelated features — the repo's established gate is "no new warnings."
- **Repository LINQ predicates are the untested surface** (due-gating, 50-row cap, actor-exclusion, late-joiner). Unit tests mock the repo, and the repo has no integration harness in this repo. Accepted per the plan's Testing Strategy; if an integration harness is ever added, `IStaffNotificationRepository` query semantics are the first thing to cover.
- **Domain-event dispatch stays dormant** as required — generation is inline/best-effort in the command handlers, so the outbound `AppointmentCreatedEventHandler` never fires. The single hard constraint (no reactivation of the email/SMS pipeline) is honored.
- Deep-link navigation now relies on a lightweight `window` `CustomEvent` seam; if more deep-linkable screens are added, reuse the `clinic:deeplink` convention rather than reaching for `useSearchParams`.

## Quality Check Results

| Check | Result |
|-------|--------|
| Backend build (0 errors) | Pass (0 errors; 57 pre-existing warnings, 0 in this feature) |
| Backend unit tests | Pass (notification suite 22/22; full suite 229/229 at commit — unchanged, review touched no backend files) |
| Frontend type checking | Pass (`tsc --noEmit` clean) |
| Frontend build | Pass (`npm run build` clean; target pages still static) |
| Integration tests | N/A (no harness in repo) |
| E2E tests | N/A (no harness in repo; skipped during review by design) |
