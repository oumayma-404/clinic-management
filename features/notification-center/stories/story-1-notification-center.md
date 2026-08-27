# Story 1: [Full] In-App Staff Notification Center

**Status:** APPROVED
**Story Status:** implemented
**Layer:** Full
**Depends On:** None
**Blocks:** None

## Objective

Deliver the complete clinic-scoped, per-user in-app notification feed described in the spec as **one vertical slice** (Domain → Persistence → Application → API → Frontend). A bell + unread badge in the dashboard header opens a panel of recent notifications; notifications are generated inline (best-effort) by four triggers — appointment created / cancelled / rescheduled and low-stock — plus a read-time 24h reminder. Read/unread state is per-user, rows deep-link to the related record, and the bell/panel update live over the existing SignalR pipeline. This story is the whole feature; the plan (`../plan.md`) is the authoritative source for every design decision, file list, and edge case.

> **Single-story by planning decision.** `plan.md` (Type: single implementation story) intentionally keeps this as one full-stack story implemented as ordered step-groups A–G. `/break-plan` was deliberately skipped (per user directive); this file wraps the plan's US-1 so it can be tracked and checkpointed per group in `progress.md`.

## Acceptance Criteria

_From spec:_

- [ ] US-1 — Notification bell & panel (unread badge, 99+ cap, newest-50 list, per-row content, empty/loading/error states)
- [ ] US-2 — Appointment created (patient-only, actor excluded, deep-link)
- [ ] US-3 — Appointment cancelled / rescheduled (actor excluded, reminder lifecycle)
- [ ] US-4 — Upcoming ~24h reminder (read-time due-ness, no background job; suppressed on cancel; moved on reschedule)
- [ ] US-5 — Low stock (edge-triggered not-low→low crossing; create-already-low fires nothing; re-arm)
- [ ] US-6 — Per-user read/unread, mark-all, late-joiner baseline (`User.CreatedAt`), deep-link navigation

_Story-specific:_

- [ ] Existing outbound `Notification` / `NotificationService` / `NotificationJob` / `AppointmentCreatedEventHandler` remain untouched and dormant (no global domain-event dispatch).
- [ ] `"notifications"` realtime key added to backend resolver output AND frontend `RealtimeResource` in lock-step, pinned by the resolver contract test.

## Entry Criteria

- [ ] `plan.md` is APPROVED (it is).
- [ ] `dotnet build` and `npx tsc --noEmit` / `npm run build` start clean.

## Steps

Implement the plan's step-groups **in order**, each leaving the build green (`dotnet build` 0/0; `tsc --noEmit` + `npm run build` clean). Full detail per group is in `../plan.md` → "Implementation Story".

1. **Group A — Domain + persistence** (`../plan.md` steps 1–6): enums, `StaffNotification` aggregate, `NotificationRead` join, EF configs, DbSets + global query filters, `IStaffNotificationRepository` + impl, migration.
2. **Group B — Read side** (steps 7–13): `NotificationDto` + `ToDto`, `GetNotificationsQuery`, `GetUnreadCountQuery`, `MarkNotificationReadCommand`, `MarkAllNotificationsReadCommand`, `NotificationsController`, update `RealtimeResourceResolverTests`.
3. **Group C — Generation seam + appointment-created trigger** (steps 14–15): `INotificationGenerator` + `NotificationGenerator` (best-effort, post-commit persist + `"notifications"` broadcast); wire into `CreateAppointmentCommandHandler`.
4. **Group D — Reminder** (steps 16–17): schedule ~24h reminder at booking (`EffectiveFeedTime = start - 24h`), due-ness enforced by the Group-B `EffectiveFeedTime <= now` filter.
5. **Group E — Cancel + reschedule triggers + reminder lifecycle** (step 18): capture old status/date before mutation; generate cancelled/rescheduled (actor-excluded); suppress/move reminder; guard the cancelled→scheduled reactivation edge.
6. **Group F — Low-stock trigger** (step 19): capture `IsLowStock()` before mutators; on `!wasLow && isLowNow` generate `LowStock` (visible to all).
7. **Group G — Frontend** (steps 20–25): `NotificationDto` type + `notifications.ts` API, `Notifications` realtime key, `use-notifications` hook, `notification-panel.tsx`, `dashboard-header.tsx` bell/badge/popover, deep-link handling on `/appointments` and `/stock`.

## Files to Create/Modify

See `../plan.md` → "Files to Create / Modify" (authoritative). ~25+ files across Domain, Infrastructure, Application, API, UnitTests, and `web/`.

## Verification Steps

- [ ] `dotnet build` — 0 errors / 0 warnings (after each group).
- [ ] `dotnet test` — new test classes pass (or authored + build-verified with execution deferred if Smart App Control blocks local execution — see `plan.md` Testing Strategy).
- [ ] `npx tsc --noEmit` — clean.
- [ ] `npm run build` — clean.
- [ ] Manual: bell shows unread badge; panel lists newest-first; triggers generate notifications; deep-links navigate; mark-all drops badge to 0.

## Exit Criteria

- [ ] All seven step-groups implemented, each leaving the build green.
- [ ] All spec acceptance criteria (US-1…US-6) satisfied.
- [ ] Backend quality gate: `dotnet build` 0/0; tests authored (execution per plan note).
- [ ] Frontend quality gate: `tsc --noEmit` + `npm run build` clean.
- [ ] No changes to the dormant outbound notification pipeline.

## Notes

- Best-effort generation must never break the core action (appointment/stock command) — swallow+log narrowly, never blanket-swallow (per the InternetProbe learning).
- All times computed in UTC; mirror `UpdateAppointmentCommand`'s `DateTimeKind` handling (R-9).
- Consult `../plan.md` Risk Register (R-1…R-9) before each group.
