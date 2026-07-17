# Feature Review: notification-center

**Status:** COMPLETE
**Challenged:** Yes
**Date:** 2026-07-14
**Challenged Date:** 2026-07-14
**Parent Branch:** feature/windows-desktop-app
**Merge Base:** ca04f66 (scoped to the feature's own commits `ca04f66..HEAD` = `176f394` + `4b96178`)
**Files Reviewed:** 50 changed files (+4277 / -16), reviewing code only — excluded generated `20260714113411_AddStaffNotifications.Designer.cs` (1348 lines) + `ApplicationDbContextModelSnapshot.cs` and the `features/notification-center/*.md` docs.
**Review method:** 5 parallel agents adapted to the clinic stack (MediatR + `Result<T>` + EF Core, no ROP/Marten): Code Quality & Architecture, Error Handling (`Result<T>`/HTTP mapping, repointed from the default ROP agent), Business Logic Correctness (spec US-1..US-6), Breaking Changes & Regression, and a dedicated Frontend (React/Next/TS) agent. The Breaking Changes agent found no issues (migration, DI wiring, realtime `notifications` key lock-step, and best-effort generation-after-commit all verified).

## Challenge Summary

| Metric | Count |
|--------|-------|
| Original findings | 8 |
| Confirmed | 8 |
| Confirmed (adjusted) | 0 |
| Dismissed (false positive) | 0 |
| Dismissed (pre-existing) | 0 |
| **Final findings** | 8 |

Every finding was verified against the full source (not just the diff). All 8 held up:
- **Finding 1** confirmed as a genuine tenant-isolation defect — `CurrentClinicProvider.ClinicId` reads only the JWT `clinic_id` claim, the EF global filter is documented **fail-open** when that claim is null ("Do not lean on this filter for isolation"), and `GetAppointmentQuery` relies solely on it while its sibling `GetAppointmentsQuery` DB-resolves the clinic. The DEV-1 note's "tenant-safe via the global query filter" holds only when the claim is present.
- Findings 2–7 verified in source (line numbers re-anchored where the diff-relative numbers didn't match the file — e.g. Finding 7's scroll effect is at `stock-table.tsx:66`, not 393).
- Finding 8 confirmed; it is self-described hygiene (harmless under React 19), so it stays a Suggestion.

## Findings

### Finding 1
- **Severity:** Major
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/Controllers/AppointmentsController.cs
- **Line:** 51
- **Anchor:** `AppointmentsController.GetAppointment`
- **Comment:** The new deep-link endpoint `GET /api/appointments/{id}` delegates to `GetAppointmentQuery`, whose handler does no explicit clinic check — it calls `_appointmentRepository.GetByIdAsync(id)` and relies solely on EF's global query filter (`!IsClinicScoped || a.ClinicId == ScopedClinicId`). That filter is disabled whenever `IClinicContext.GetClinicId()` returns null (the `clinic_id` JWT claim absent/unparsable) — `CurrentClinicProvider.ClinicId => _clinicContext.GetClinicId()`, and its own doc-comment states the fail-open is deliberate and "not a substitute for the per-handler DB-resolved check … Do not lean on this filter for isolation." Per the project convention (clinic resolved from DB `User.ClinicId`, "not purely from the JWT claim"), a missing/stale claim is an expected state — which is why the other appointment paths (`GetAppointmentsQuery`, `UpdateAppointmentCommand`) resolve the clinic from the DB and scope explicitly. Under a null claim this endpoint could return any clinic's appointment (and its patient name/times — PHI) by id, breaking the spec's "clinic isolation enforced server-side (cross-clinic → not found)" and the controller's own doc-comment. Fix: resolve the caller's clinic from the DB (as `GetAppointmentsQuery`/`UpdateAppointmentCommand` do) and return not-found when `appointment.ClinicId` differs, rather than depending on the claim-based filter alone.

### Finding 2
- **Severity:** Minor
- **Category:** Error Handling
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/Controllers/NotificationsController.cs
- **Line:** 65
- **Anchor:** `NotificationsController.MarkRead`
- **Comment:** (Raised by both the Code Quality and Error Handling agents.) `MarkRead` maps every `Result.IsFailure` to `BadRequest` (400), but this endpoint's only realistic non-auth failure is the tenant-mismatch/missing case — `MarkNotificationReadCommand` returns `Result.Failure("Notification not found")` when `notification == null || notification.ClinicId != user.ClinicId`, and its own doc-comment says it "reads as 'not found' (tenant-isolation convention)". Returning 400 contradicts both that convention and the sibling `AppointmentsController.GetAppointment` (added in this same diff) which correctly returns `NotFound(result.Error)` for the identical tenant-scoped not-found — the deep-link path the frontend relies on. Fix: map this endpoint's failure to `NotFound(result.Error)` (pragmatic, since `Result` carries only an error string with no discriminator and not-found is its sole non-auth failure), so a stale/cross-clinic id yields 404.

### Finding 3
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Appointments/Commands/UpdateAppointmentCommand.cs
- **Line:** 230
- **Anchor:** `UpdateAppointmentCommandHandler.Handle` (notification branch)
- **Comment:** When an appointment is reactivated (status Cancelled → Scheduled with no date change — the branch that calls `Reschedule(sameDateTime)`), `becameCancelled` is false and `dateChanged` is false, so neither notification branch runs and no reminder is re-created. Because the earlier cancel (`AppointmentCancelledAsync`) already deleted the reminder, a reactivated future appointment (>24h out) is left permanently without a ~24h reminder, contradicting the intent of US-4 (an active, far-out appointment should carry a reminder). Consider recreating the reminder on reactivation when the new due time is still in the future.

### Finding 4
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Common/Services/NotificationGenerator.cs
- **Line:** 174
- **Anchor:** `NotificationGenerator.SafelyAsync`
- **Comment:** `SafelyAsync` unconditionally broadcasts the `"notifications"` realtime key after every write, even when the write staged nothing user-visible: (a) `ScheduleAppointmentReminderAsync` early-returns for appointments <24h out yet still triggers a clinic-wide refetch; (b) a successfully-scheduled reminder is future-dated (`EffectiveFeedTime > now`) so it won't appear in any feed until due, but the broadcast makes all clients refetch for no visible change — and appointment creation fires two broadcasts (created + reminder). Fix: have the `write` delegate return a bool "did something become due/visible" and only call `NotifyEntityChangedAsync` when true (or broadcast once from the caller after both writes).

### Finding 5
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Notifications/Commands/MarkAllNotificationsReadCommand.cs
- **Line:** 53
- **Anchor:** `MarkAllNotificationsReadCommandHandler.Handle`
- **Comment:** The handler calls `GetUnreadForUserAsync`, which materializes full `StaffNotification` entities, but only `notification.Id` is used (to build `NotificationRead`). For a large unread backlog this loads every column of every row needlessly. Fix: add/use an id-only projection (e.g. `GetUnreadIdsForUserAsync` returning `IReadOnlyCollection<Guid>`) so mark-all fetches just the ids it consumes.

### Finding 6
- **Severity:** Minor
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** web/app/stock/page.tsx
- **Line:** 17
- **Anchor:** `StockPage` / `highlightItemId`
- **Comment:** The deep-link highlight is set once (`setHighlightItemId(itemId)` in `highlightItem`) and never cleared: `highlightItemId` stays set for the lifetime of the mounted page, so the target row keeps its `bg-primary/10 ring-1 ring-primary` treatment permanently (looks like a stuck selection state) until a full reload. Clear it after it has served its purpose, e.g. `useEffect(() => { if (!highlightItemId) return; const t = setTimeout(() => setHighlightItemId(null), 4000); return () => clearTimeout(t) }, [highlightItemId])` — the timeout also needs cleanup on unmount to avoid a stray setState.

### Finding 7
- **Severity:** Minor
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** web/components/stock-table.tsx
- **Line:** 66
- **Anchor:** `StockTable` / scroll-into-view useEffect
- **Comment:** The scroll effect (`useEffect(... scrollIntoView ..., [loading, highlightItemId, items])`) depends on `items`, and `items` gets a fresh reference on every `loadItems()` run (mount + each `refreshKey` bump after add/edit/delete). Combined with the never-cleared `highlightItemId` (Finding 6), editing or deleting any stock item re-runs this effect and yanks the viewport back to the deep-linked row with a smooth scroll — jarring and unexpected. Scroll only once per deep-link: gate on a `hasScrolledRef` that resets when `highlightItemId` changes, or drop `items` from the deps and key the scroll off `highlightItemId`/`loading` only.

### Finding 8
- **Severity:** Suggestion
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** web/lib/hooks/use-notifications.ts
- **Line:** 32
- **Anchor:** `useNotifications` / `refetchList` (and `refetchCount`)
- **Comment:** `refetchList`/`refetchCount` call `setState` after their `await` with no unmount guard. `DashboardHeader` (the sole consumer) is rendered per-page, so navigating between dashboard routes unmounts and remounts it; an in-flight list/count fetch then resolves after unmount and sets state on a dead component. Harmless in React 19 but flagged for hygiene — guard with a mounted flag (or `AbortController`) captured in the panel-open/mount effects and short-circuit the setter if unmounted.

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| Major | 1 |
| Minor | 6 |
| Suggestion | 1 |
| **Total** | 8 |
