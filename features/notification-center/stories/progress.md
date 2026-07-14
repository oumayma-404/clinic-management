# Implementation Progress: In-App Staff Notification Center

**Feature:** [features/notification-center/](../)
**Started:** 2026-07-14
**Last Updated:** 2026-07-14

## Story Status

| Story | Status | Started | Completed |
|-------|--------|---------|-----------|
| Story 1: In-App Staff Notification Center (full vertical slice) | reviewed | 2026-07-14 | 2026-07-14 |

## Current Story: Story 1

**File:** [story-1-notification-center.md](./story-1-notification-center.md)
**Branch:** `feature/windows-desktop-app` (user chose to stay on the current branch, not create `feature/notification-center`)

### Step Progress (plan step-groups A–G)

| Group | Step | Status | Checks | Notes |
|-------|------|--------|--------|-------|
| A | Domain + persistence (enums, StaffNotification, NotificationRead, EF configs, DbSets + filter, repo, migration) | done | ✓ | migration `20260714113411_AddStaffNotifications` |
| B | Read side (DTO, GetNotifications, GetUnreadCount, MarkRead, MarkAllRead, controller) | done | ✓ | |
| C | Generation seam (`INotificationGenerator`/`NotificationGenerator`) + appointment-created | done | ✓ | |
| D | Reminder (24h, read-time due-ness) | done | ✓ | folded into generator |
| E | Cancel + reschedule triggers + reminder lifecycle | done | ✓ | actor-excluded; R-3 reactivation guard |
| F | Low-stock trigger (not-low→low crossing) | done | ✓ | |
| G | Frontend (types, api module, realtime key, hook, panel, header, deep-links) | done | ✓ | + GET /appointments/{id} (DEV-1) |

### Final Quality Checks

| Check | Status | Last Run |
|-------|--------|----------|
| Backend build (0 err / no new warnings) | passing | 2026-07-14 |
| Backend tests (`dotnet test`) | passing (229/229, incl. new NotificationGeneration/Query/TenantIsolation) | 2026-07-14 |
| Frontend typecheck (`tsc --noEmit`) | passing | 2026-07-14 |
| Frontend build (`npm run build`) | passing | 2026-07-14 |

### Working tree note (start of session)

Pre-existing uncommitted changes unrelated to this story — **excluded from this story's commits**:
- `.gitignore`, `api/ClinicManagement.API/appsettings.json`, `web/Dockerfile` (modified before this session).

### Blockers

- None. (Backend `dotnet test` execution may be blocked locally by Smart App Control — see memory `smart-app-control-blocks-tests`; tests are authored + build-verified, execution deferred per repo practice.)

## Auto-Approved Deviations

| Story | Deviation | Classification | Reason |
|-------|-----------|----------------|--------|
| 1 | `NotificationRead` scoped by `UserId` only (no global clinic query filter on it) rather than "add both to the clinic global-query-filter block" | Trivial (sanctioned by plan R-5) | R-5 explicitly offers "scope by UserId (a user belongs to one clinic)" as an acceptable mitigation; it has no `ClinicId` column, and every query filters `NotificationReads` by the current `UserId`. Avoids a required-navigation-in-query-filter. |
| 1 | Reminder suppress/move folded into `AppointmentCancelledAsync`/`AppointmentRescheduledAsync` (generator internals) instead of separate public `SuppressAppointmentReminderAsync`/`MoveAppointmentReminderAsync` methods | Trivial | Internal design of the generator seam; keeps the handler calling one method per event and co-locates the reminder lifecycle with its trigger. Same behavior; the 24h/create/move/remove logic is private helpers. |
| 1 | `NotificationGenerator` catches `Exception` broadly (logs at **Error** with the exception, never rethrows) rather than a "narrow" catch | Trivial (reconciles two guidances) | The spec's hard constraint is that generation must **never fail/roll back the core operation**; since generation runs after the core commit, letting an unexpected exception propagate would fail an already-committed appointment/stock op. Broad-catch is required; logging at Error (with `ex`) satisfies the InternetProbe learning's real concern (visibility), unlike its Debug-level cached-`false` case. |

## Deviations from Plan

### DEV-1: Wired `GET /api/appointments/{id}` for the notification deep-link
**Date:** 2026-07-14
**Story:** 1 (Group G)
**Category:** Scope
**Original Plan:** Frontend files only; API additions limited to `NotificationsController`. Plan step 75 wanted `/appointments?appointmentId=&date=` but the notification DTO carries no appointment date.
**Actual Implementation:** Added a thin `GET /api/appointments/{id}` route to the existing `AppointmentsController` (`[Authorize]`), wired to the **already-existing but previously-unrouted** `GetAppointmentQuery`; added `appointmentsApi.get(id)`. The appointments page fetches the appointment on deep-link, focuses its day, and opens the edit dialog (graceful when missing).
**Justification:** Needed to satisfy US-6 ("navigate to the appointment, its day, opened/highlighted"). Reuses existing wired Application code; authenticated (no anonymous-allow-list / `ControllerAuthorizationCoverageTests` impact — verified passing). Tenant-safe via the global query filter (cross-clinic id → not found).
**Impact:** Small additive read endpoint. No effect on other features.
**Approved:** Yes (user chose "Open the exact appointment" via AskUserQuestion).

## Learnings

_(captured as discovered)_

## Session Log

### Session 1 - 2026-07-14
**Story:** Story 1
**Progress:**
- Scaffolded single-story wrapper (per `/next` directive: no `/break-plan`).
- Group A done: domain (enums, `StaffNotification`, `NotificationRead`), EF configs, DbSets + global filter, repository, migration. Build 0 errors, no new warnings.
- Starting Group B (read side).

### Session 2 - 2026-07-14 (Story review — `/review-story`)
**Story:** Story 1 → **reviewed**
**Score:** 100/100. Report: [reviews/story-1.md](../reviews/story-1.md)
**Findings:** 1 Minor, fixed.
- **[Minor, fixed]** Same-page notification deep-link didn't open/highlight the target (a same-route `router.push` doesn't remount, so the mount-only effect never fired). Fixed with a `clinic:deeplink` `CustomEvent` the target pages listen for; kept off `useSearchParams` to preserve static prerendering (R-8). Files: `dashboard-header.tsx`, `app/appointments/page.tsx`, `app/stock/page.tsx`.
**Quality gates:** backend build 0 err (57 pre-existing warnings, 0 in this feature); notification unit tests 22/22; `tsc --noEmit` clean; `npm run build` clean (target pages still static).
**Observations:** spec uses narrative ACs; repository LINQ predicates untested (no integration harness — accepted per plan); 57 pre-existing warnings are the repo baseline (out of scope).
