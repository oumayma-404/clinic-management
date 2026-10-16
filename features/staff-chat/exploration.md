# Staff chat — exploration findings

> 2026-10-15 · `/think-solution` (3 agents) + `/define-feature` (4 agents). Facts measured in source; file:line refs
> are as of this date.

## Realtime
- `ClinicHub` joins only `clinic-{id}` (`api/ClinicManagement.API/Hubs/ClinicGroups.cs:10`, `ClinicHub.cs:39-48`).
  No per-user group; nothing calls `Clients.User`.
- One event, `entityChanged`, payload = one lowercase key, never data (`IRealtimeNotifier.cs`, `SignalRRealtimeNotifier.cs:22-28`).
- Hub methods run with **no tenant scope** (`ClinicHub.cs` docstring; `ClinicHubTenantScopeTests`).
- A `Features/<Area>/Commands` namespace emits key `<area>` via `RealtimeBroadcastBehavior`; `clinic-hub.ts:15-48` must
  declare it; `RealtimeResourceResolverTests` checks both directions, allow-lists pinned empty. `IDoesNotBroadcast`
  opts a command out.
- Web: one connection per tab (`clinic-realtime-provider.tsx`, N45). On reconnect `emit(undefined)` invalidates every
  query (`:55`) — no server backlog, so a chat must re-read « since last seen » itself.
- `/hub` is exempt from the rate limiter (`Startup/RateLimiting.cs:144-154`) — sending through a hub method would be
  unlimited **and** unscoped. Send via REST (global 600/min/user).

## Notification center (closest precedent)
- `StaffNotification` one row per event; `ActorUserId` excludes the actor; `TargetUserId` null = whole clinic.
- Per-user read/dismiss markers (`NotificationRead`, `NotificationDismissal`), composite key, no clinic filter.
- No purge. Excluded from audit? No — excluded from clinic archive (`ClinicArchiveScope.cs:67-80`).
- Spec rules to match: clinic-scoped (cross-clinic = not found) · actor never notified · read state per user ·
  account-age cutoff (older items shown as read) · `99+` badge · missing deep-link target lands somewhere sensible ·
  works offline on the LAN · « Aucune notification » empty state.
- Lock-screen push rule: fixed label, never a patient name (`StaffNotificationRules.cs:81-90`, AC-47).
- Bell UI: `notification-panel.tsx` is `w-80` — exactly 320 px at 320 px; do not copy.

## Users, roles, group practice
- Roles `admin` · `doctor` · `secretary` (`User.cs:23-36`); `User.Id` is a string; one clinic per user.
- `AnyClinicRole` policy = the bell's policy (`NotificationsController.cs:13`); `ControllerAuthorizationCoverageTests`
  requires a class-level named policy.
- Several dentists per clinic; `Doctor.UserId` nullable; the owner-dentist is usually **admin** with a linked Doctor.
  No « my agenda »; no per-doctor privacy (any doctor sees every patient).
- `ListUsersQuery` admin-only — a colleague list for all roles is new.
- No user delete; `Deactivate()` kills sessions; rows elsewhere keep string ids, no FK. Audit shows the email of a
  departed actor.
- No seat limit (`SubscriptionPlan` gates nothing).
- Journal d'activité: admin-only; every saved `AggregateRoot` is journaled unless excluded
  (`AuditSaveChangesInterceptor.cs:62`).

## Appointments / arrival
- No « arrivé » status; no chair/room entity; no today's-queue view. `AppointmentProgressJob` sets « En cours » at slot
  start.
- Secretary surfaces on a today's RDV: agenda block « ⋯ » (`appointment-quick-actions.tsx:58`, desktop/tablet only,
  blocks ≥ 36 px) and the edit dialog (`edit-appointment-dialog.tsx:1067-1097`, the only path on a phone; its comment
  says opening it « usually means le patient est arrivé »). Secretaries are redirected away from the dashboard.
- `Appointment.DoctorId` nullable (imported RDVs carry none).
- Deep links: `/appointments?appointmentId=` + `clinic:deeplink`; `/patients/{id}`. Chips should be real `<a>` so
  `plan-workspace` unsaved-edit guard intercepts them.

## Interruptions, toasts, focus
- Sonner (`app-toaster.tsx:40-57`): mouse top-right ×5; touch bottom-center ×3 above `--bottom-inset`; 4 s.
- ⚠️ Toaster `z-index: 999999999` draws **above the session lock screen** (`session-lock-gate.tsx` is `z-50`) — a chat
  toast with a patient name would be readable on a locked screen.
- Post-visit prompt: stays quiet while `body[data-sheet-open]` / `[data-scroll-locked]`; frontend-web § 5: an
  unrequested interruption is never a sheet, stays quiet while a dialog is open.
- No non-modal side panel exists anywhere; `Sheet` is modal and hides `BottomNav`.
- Ctrl/⌘-K is taken (patient search). Focus rules: Escape + visible 44 px ✕, focus to the title, return to trigger.

## Errors, offline, subscription
- Network failure → `ApiError(0, network)` → 8 s toast « Impossible de joindre le serveur… », optional « Réessayer »;
  dialog keeps its input; no offline queue.
- 402 on writes when expired; reads always work; banner, never a sign-out. Mark-read writes of the bell are exempt
  (« writes experienced as reading ») — precedent for chat read markers.
- Session expiry → toast + logout with return URL; idle 30 min / 8 h trusted.
- Text limits: trimmed, constant = EF max length, French refusal « … ne peut pas dépasser 1000 caractères. »
- Empty kinds: `EmptyState` (nothing yet / no match) vs `LoadFailureNotice` (failed) — never conflated.

## Native alerts
- Desktop shell bridge: `version`, `platform`, `confirmIdentity` only; no toast, tray, flash, sound; closing the window
  quits. New method ⇒ `mobile/shared/bridge.md` row + version bump (1.3.0 → 1.4.0).
- Web: no `Notification` API, no service worker, no sound anywhere.
- Phone push: not wired on any device; impossible on `SelfHostedLan`; held 21:00–08:00.

## Persistence conventions
- `Entity<TId>` / `AggregateRoot<TId>`, `Version` → `xmin`; delete scaffolded `xmin` lines in migrations.
- Clinic query filter `IsSystemWide || ClinicId == ScopedClinicId`; `TenantScopeFilterTests` checks every entity.
- Per-user marker rows: `SkipsConcurrencyToken`, `NotificationRead` shape.
- Template: `PatientFileAnnotation` (entity · configuration · repository · migration `20260903160412`).
