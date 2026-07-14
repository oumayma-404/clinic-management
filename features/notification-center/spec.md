# Feature Specification: In-App Staff Notification Center

**Status:** APPROVED
**Challenged:** Yes
**Created:** 2026-07-14
**Feature:** An in-app notification feed (bell in the header + panel) that keeps clinic staff aware of clinic activity — new/cancelled/rescheduled appointments, upcoming reminders, and low stock — with per-user read/unread state and live updates.

## Overview

Clinic staff currently have no way to see clinic activity at a glance — the dashboard's notifications list was hardcoded sample data and has been removed, and the bell icon in the header is inert. This feature turns notifications into a real, clinic-scoped, in-app feed: a bell with an unread badge in the header opens a panel of recent notifications, newest first. Notifications are generated automatically by clinic events (appointment created/cancelled/rescheduled, an upcoming-appointment reminder coming due, and a stock item hitting its low-stock threshold). Each staff member has their own read/unread state; clicking a notification takes them to the related record and marks it read, and they can mark all as read at once. The panel updates live via the app's existing real-time mechanism.

This is **in-app only** — it does **not** send email or SMS. The existing (disabled) outbound email/SMS reminder pipeline is left untouched and is never triggered by this feature.

## User Stories

### US-1 — Notification bell & panel
As clinic staff, I want a bell in the header showing my unread count, opening a panel that lists my clinic's recent notifications newest-first, so I can stay aware of what's happening without hunting through pages.

**Acceptance Criteria:**
- The header shows a bell icon; when the current user has ≥1 unread notification, an unread-count badge is shown on the bell. When 0, no badge (or an empty/neutral bell).
- Unread counts above 99 display as `99+`.
- Clicking the bell opens a panel listing the most recent notifications for the user's clinic, newest first.
- The panel shows at most the 50 most recent notifications; older notifications remain stored but are not shown.
- Each row shows: an icon/indicator for the notification category, a title, a short message, and a relative timestamp (e.g. "il y a 5 min", French locale).
- Unread rows are visually distinguished from read rows.
- Empty state: when there are no notifications, the panel shows "Aucune notification".
- Loading and error states are handled: a spinner/placeholder while loading; on load failure the panel surfaces a non-blocking error (the bell itself never crashes the header).

### US-2 — Appointment created
As clinic staff, I want to be notified when a new appointment is booked in my clinic (by someone else), so I know the schedule changed.

**Acceptance Criteria:**
- When an appointment **with a patient** is created, a notification of category "appointment created" is generated for the clinic.
- The staff member who created the appointment does **not** receive this notification; all other staff in the clinic do.
- The notification names the patient and the appointment date/time.
- Patient-less "busy slot" appointments do **not** generate a notification.
- Clicking the notification deep-links to that appointment (see US-6).

### US-3 — Appointment cancelled / rescheduled
As clinic staff, I want to be notified when an appointment in my clinic is cancelled or rescheduled (by someone else), so I don't act on stale schedule information.

**Acceptance Criteria:**
- When an appointment **with a patient** is cancelled, a notification of category "appointment cancelled" is generated for the clinic.
- When an appointment **with a patient** is rescheduled, a notification of category "appointment rescheduled" is generated, naming the old and new date/time.
- The actor is excluded; all other clinic staff receive it.
- Clicking the notification deep-links to the appointment (for a cancelled appointment, to its (former) day; see US-6 and Edge Cases).

### US-4 — Upcoming-appointment reminder
As clinic staff, I want upcoming appointments to appear in the feed shortly before they happen, so the clinic is prepared.

**Acceptance Criteria:**
- When an appointment with a patient is created, a reminder is scheduled for ~24 hours before the appointment.
- The reminder appears in the feed only once it comes **due** (its scheduled time is at/before "now") — not at booking time.
- **Due-ness is evaluated at read time, not by a background job.** The reminder is stored at booking with an effective feed time = its due time; the list and unread-count queries surface/count it only when `dueTime ≤ now`. No running scheduler is required (there is none), so this works fully offline on the LAN.
- If the appointment is created **less than 24h** before its start, no separate reminder is created (the "appointment created" notification suffices).
- If the appointment is **cancelled** before the reminder comes due, the pending reminder is suppressed (it never appears).
- If the appointment is **rescheduled**, its reminder reflects the new time (a reminder for the old time never appears once the appointment has moved).
- The reminder is visible to all clinic staff.

### US-5 — Low stock
As clinic staff, I want to be notified when a stock item runs low, so I can reorder before it runs out.

**Acceptance Criteria:**
- The trigger is a **not-low → low crossing**, evaluated by comparing `IsLowStock()` (i.e. `CurrentStock ≤ MinimumStockLevel`) **before vs. after** a stock-mutating Update command. When the item was **not** low before and **is** low after, a "low stock" notification is generated for the clinic, naming the item and its current/minimum quantities. Because both quantity and minimum level are set on the same Update, a crossing caused by **raising `MinimumStockLevel`** (quantity unchanged) counts the same as one caused by a quantity drop — one rule covers both.
- **Creating** a stock item that is already at/below its minimum does **not** generate a notification (no prior not-low state to cross from); it still surfaces as low via the existing computed low-stock badge on the stock screen.
- The notification is **edge-triggered**: while the item stays low, further decreases (or repeated saves) do **not** generate additional notifications (before-state is already low → no crossing).
- If the item is restocked above its minimum and later crosses to at/below minimum again, a **new** notification is generated (one per crossing).
- The notification is visible to all clinic staff.
- Clicking it deep-links to the stock screen with that item highlighted (see US-6).

### US-6 — Read/unread interactions & navigation
As clinic staff, I want to manage which notifications I've seen and jump to what they refer to, so the feed stays useful and actionable.

**Acceptance Criteria:**
- Read/unread state is **per-user**: marking a notification read affects only the current user's view; other staff still see it as unread until they act.
- Clicking a notification (a) marks it read for the current user and (b) navigates (deep-links) to the related record:
  - appointment categories → the appointments screen focused on that appointment (its day, with the appointment opened/highlighted);
  - low stock → the stock screen with that item highlighted.
- A "Tout marquer comme lu" (mark all as read) action marks **all** of the user's unread notifications read (not just the visible 50), so the unread badge always drops to 0 afterward.
- The unread badge count reflects **all** of the user's unread notifications (subject to the 99+ display cap), consistent with what "mark all as read" clears.
- On first login / at feature go-live, only notifications whose **effective feed time is at/after the user's `User.CreatedAt`** (and after go-live) count as unread for that user; earlier notifications appear in the panel as already-read. No historical notifications are back-generated. (`User.CreatedAt` is the join proxy — see Data/Behavior Semantics.)

## User Interface

- **Location:** the existing bell button in the dashboard header (currently inert with a hardcoded dot) becomes the notification center trigger. The hardcoded red dot is replaced by a real unread-count badge, shown only when unread > 0.
- **Panel:** opens from the bell (dropdown/popover style, right-aligned), containing:
  - a header row with a title ("Notifications") and the "Tout marquer comme lu" action (disabled/hidden when there are no unread);
  - a scrollable list (max ~50 rows) of notification rows, newest first;
  - per-row: category icon, title, message, relative time, and an unread indicator;
  - empty state "Aucune notification".
- **Language:** all user-facing strings are French (Tunisia-targeted app), consistent with newer components. Relative timestamps use the French date locale.
- **Behavior:** clicking a row closes the panel, marks the row read, and navigates to the target. The panel/badge update live (see FR below) without a manual refresh.
- Available in both auth modes (Cloud/Local); it is standard authenticated staff UI, not gated to a mode or a single role.

## API Endpoints

Functional contract (paths follow existing conventions, e.g. `api/stock`); all require an authenticated user and are scoped server-side to the caller's clinic and identity.

### GET `/api/notifications`
- **Purpose:** list the current user's clinic notifications for the panel, newest first, capped at the most recent 50, each annotated with the current user's read state.
- **Response 2XX:** a list of notification items, each with: `id`, `category` (e.g. AppointmentCreated / AppointmentCancelled / AppointmentRescheduled / Reminder / LowStock, exposed as a string), `title`, `message`, `createdAt`, `isRead` (for this user), and a navigation target reference (e.g. related appointment id / stock item id and target kind).
- **Errors:** `400` with a message on failure; `401` if unauthenticated.

### GET `/api/notifications/unread-count`
- **Purpose:** the current user's total unread count for the badge (may exceed the 50 shown; display-capped at 99+ client-side).
- **Response 2XX:** `{ unreadCount: number }`.
- (May be folded into the list response if preferred during planning; the functional need is "a badge count independent of the 50-row display window.")

### PUT `/api/notifications/{id}/read`
- **Purpose:** mark a single notification read for the current user.
- **Response 2XX:** success (updated item or no content).
- **Errors:** `400` if the notification does not belong to the user's clinic (reported as not-found, per the app's tenant-isolation convention); `401` if unauthenticated.

### PUT `/api/notifications/read-all`
- **Purpose:** mark all of the current user's unread notifications read.
- **Response 2XX:** success.
- **Errors:** `401` if unauthenticated.

> Exact verb/shape (PUT vs POST, folding unread-count into the list) is a planning detail; the functional contract above is the requirement.

## Data / Behavior Semantics (functional)

- **Clinic scoping:** every notification belongs to exactly one clinic; a user (who belongs to exactly one clinic) only ever sees their clinic's notifications.
- **Per-user visibility & read state:** the panel presents a **clinic-scoped** notification set; read/unread is tracked **per viewer**, not as a single shared flag — one staff member reading a notification does not clear it for others. Two distinct per-viewer rules apply on top of the clinic set, and must not be conflated:
  - **Actor exclusion (absent from the viewer's panel):** a notification generated by an appointment action performed by user *V* is **hidden entirely from V's own panel** — it never appears for V and never counts toward V's badge — while remaining visible/unread to all *other* clinic staff. (Each notification therefore records which user, if any, to exclude.) Low-stock and reminders have no single actor and are visible to all staff.
  - **Unread baseline (shown-as-read for late joiners):** a viewer who joined the clinic **after** a notification's effective feed time still *sees it in the panel, but as already-read* (never unread, never badged). The join moment is anchored to **`User.CreatedAt`** (the app is one-clinic-per-user with no re-join flow). This governs unread counting, not visibility — see US-6.
- **Category:** each notification carries a category identifying its kind (appointment created / cancelled / rescheduled, reminder, low stock), used for the icon and the navigation target.
- **Content:** notifications store a French title + message and the reference(s) needed to deep-link (e.g. related appointment id, stock item id). Patient name and appointment times may appear in appointment notifications — this is an internal, authenticated staff feed and staff are already authorized to see patient data.
- **Timestamps:** notifications are ordered by their effective feed time (creation time for immediate ones; due time for reminders), newest first.
- **Generation is best-effort:** creating a notification must never block or fail the underlying action (appointment create/cancel/reschedule, stock change). A notification-generation failure is logged and swallowed; the core operation still succeeds.
- **Reminder lifecycle:** a reminder is stored at booking (~24h before), surfaces at read time when due (`dueTime ≤ now`, no background job), is suppressed on cancellation, and follows the appointment on reschedule.
- **Low-stock lifecycle:** edge-triggered on the not-low → low crossing, detected by comparing `IsLowStock()` before vs. after each stock-mutating Update (covering both quantity drops and `MinimumStockLevel` raises); creation-already-low does not fire; re-armed after recovery above minimum.

## Scope

### In Scope
- Bell + unread badge + notifications panel in the header (both auth modes).
- Clinic-scoped, per-user read/unread notification feed (list, mark-read, mark-all-read, unread count).
- Four notification triggers: appointment created, appointment cancelled, appointment rescheduled, low stock; plus surfacing the upcoming (~24h) appointment reminder when due.
- Deep-link navigation from a notification to the related appointment/stock record.
- Live update of the bell/panel via the existing real-time (SignalR per-clinic "entity changed") mechanism.
- Whatever backend groundwork is required for notifications to actually be generated (see Dependencies — domain-event dispatch is currently not wired).

### Out of Scope
- Sending email or SMS (the existing outbound `NotificationService`/`NotificationJob` stay disabled and untouched; feed items are never emailed/texted — no double-send).
- Notification preferences / per-user or per-role subscription settings / muting.
- Role-based filtering of notification types (v1: all clinic staff see all types).
- Deleting / clearing / archiving notifications from the feed ("Tout effacer"), and any auto-expiry/purge of old notifications.
- Pagination or "load more" beyond the most-recent 50 shown.
- Additional triggers beyond the four listed (e.g. patient flags, document generation, check-in).
- Back-generating notifications for data that existed before go-live.
- Multi-clinic-per-user handling (each user belongs to exactly one clinic).

## Edge Cases

- **Same-day booking (<24h):** "appointment created" fires; no separate reminder is created.
- **Cancel before reminder due:** the pending reminder is suppressed and never appears.
- **Reschedule:** the reminder reflects the new time; no stale old-time reminder appears. Multiple reschedules do not stack stale reminders.
- **Patient-less "busy slot" appointments:** generate no appointment notifications.
- **Actor exclusion:** the user who performed an appointment action does not get that notification; low-stock/reminders (no single actor) go to all staff.
- **Deep-link to a missing/changed record:** if the target record no longer exists or is not visible (e.g. a cancelled appointment no longer shown in the default view), clicking still marks the notification read and navigates to the relevant screen; the user is not left on a broken/blank state (at minimum, land on the list; a "not found" hint is acceptable).
- **Low-stock flapping:** set-below-threshold repeatedly without recovery → a single notification; recover-above then drop-below again → a new one. A crossing caused by raising the minimum level (unchanged quantity) fires once; creating an item already low fires nothing.
- **Badge vs 50-row window:** the badge counts all unread even if >50; "mark all as read" clears all unread so the badge reaches 0.
- **New staff member joins later:** they see the clinic's recent notifications in the panel, but only those created after they joined count toward their unread badge (no day-one flood).
- **Real-time unavailable (e.g. offline-LAN, dropped socket):** the feed must not silently stay stale — it refetches on panel open and on reconnect, so the badge/list self-correct even without a live push.

## Non-Functional Hints

- **Reliability:** notification generation is decoupled/best-effort so it can never break core clinic workflows (booking, stock changes).
- **Multi-tenant safety:** notifications are strictly clinic-isolated, enforced server-side (consistent with the app's existing tenant-isolation convention — cross-clinic access reported as not-found).
- **Real-time:** reuse the existing per-clinic SignalR "entity changed" pipeline rather than adding a new channel; a new "notifications" resource key is added on both backend and frontend in lock-step (a contract test pins this mapping).
- **Performance:** the panel query is bounded (recent 50); the unread count is a lightweight aggregate.
- **Offline-LAN (Local mode):** the feature needs no internet egress and must work fully offline on the LAN.

## Dependencies

- **Domain-event dispatch is currently not wired (technical prerequisite).** Domain events are raised into aggregates (`AddDomainEvent`) but never published to MediatR — there is no `SaveChanges` override / interceptor / `IPublisher.Publish` call anywhere. As a result the existing `AppointmentCreatedEventHandler` never fires today. For the event-driven triggers to work, notification generation must actually run. The plan must choose an approach (wire domain-event dispatch, or generate notifications inline in the relevant command handlers) — this is a `/plan-feature` decision. **Note the blast radius:** if dispatch is wired globally, currently-dormant handlers (e.g. the existing 24h-reminder creation, and any handler for `AppointmentConfirmedEvent`/`AppointmentRescheduledEvent`/`PatientFlagAddedEvent`) would begin firing; this must be accounted for.
  - **Hard constraint — do not reactivate the outbound reminder handler.** The *only* existing domain-event handler, `AppointmentCreatedEventHandler`, creates an **outbound** email/SMS `Notification` (the 24h reminder). Because the Overview guarantees the outbound pipeline is "never triggered," the chosen generation approach **must not cause that handler to fire** — even to merely create (never-sent) outbound rows. This effectively rules out naively wiring *global* domain-event dispatch unless the plan also neutralizes/converts that handler. Inline or scoped/targeted generation for the in-app feed is the safer path. (`AppointmentConfirmedEvent`/`AppointmentRescheduledEvent`/`PatientFlagAddedEvent` have no handlers today, so they are not a concern.)
- **New domain events required:** there is no `AppointmentCancelledEvent` and no low-stock/stock event today (only Created/Confirmed/Rescheduled/PatientFlagAdded exist). Cancel and low-stock triggers depend on adding these (or generating those notifications inline).
- **Existing `Notification` model mismatch:** the current `Notification` entity models an outbound email/SMS reminder (no `ClinicId`, no read state, has `Type`/`Status`/`SentAt`/`RetryCount`). Supporting an in-app, clinic-scoped, per-user-read feed requires extending it (or introducing a purpose-built record) — an architecture decision for planning. Either way the outbound send lifecycle must stay disabled and separate.
- **Real-time infrastructure (reused):** `ClinicHub` + `IRealtimeNotifier` + `RealtimeBroadcastBehavior` (backend) and `use-clinic-realtime` / `RealtimeResource` (frontend). A mutating command placed under `Features/Notifications/Commands` auto-broadcasts the `notifications` resource key; the frontend must add the matching key.
- **Frontend integration points:** `dashboard-header.tsx` (bell), `lib/api/client.ts` + a new `lib/api/notifications.ts`, `lib/api/types.ts`, a `use-notifications` hook, and the target pages (`/appointments`, `/stock`) needing to accept a deep-link target (appointment id / stock item id) to open/highlight the record.

## Open Questions

- None blocking. Architecture choices (extend vs. new entity; wire event dispatch vs. inline generation; exact endpoint shapes) are deferred to `/plan-feature`.

### Resolved during spec challenge (2026-07-14)

- **Actor exclusion = absent, not shown-as-read.** A notification for user *V*'s own appointment action is hidden entirely from V's panel (never appears, never badged) while unread for other staff; each notification records which user to exclude. Distinct from the late-joiner "shown-as-read" rule.
- **In-app generation must not reactivate the outbound reminder handler.** The existing `AppointmentCreatedEventHandler` (outbound email/SMS 24h reminder) must not fire — this effectively rules out naive global domain-event dispatch unless that handler is neutralized; inline/scoped generation preferred.
- **Unread baseline anchored to `User.CreatedAt`** (join proxy; one-clinic-per-user, no re-join flow). Notifications with effective feed time before a viewer's `CreatedAt` show as already-read.
- **Low-stock trigger = `IsLowStock()` before-vs-after each stock-mutating Update** (covers both quantity drops and `MinimumStockLevel` raises). Create-already-low fires nothing.
- **Reminder due-ness = read-time evaluation** (`dueTime ≤ now` in list/count queries), no background job — works offline on the LAN.
