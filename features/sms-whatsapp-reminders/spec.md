# Feature Specification: SMS & WhatsApp Appointment Reminders

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-17
**Scope:** BE
**Feature:** Deliver real SMS and WhatsApp appointment reminders to patients ahead of their appointment, reliably in offline-LAN installs, by reviving the dormant `Notification` outbox + a connectivity-gated Hangfire dispatcher.

## Overview
Today appointment reminders are in-app only; the email/SMS `Notification` pipeline is fully dormant (stubbed to log). This feature wires it up for real, over **SMS** and **WhatsApp** — the #1 baseline-parity gap for Tunisian clinics. Because the server frequently has no internet at reminder time (Local mode), reminders are **persisted to an outbox** and **dispatched when connectivity returns**, mirroring the existing Google "non synchronisé" pattern. No new outbox entity, no domain-event pipeline — we reuse the existing `Notification` aggregate, `INotificationRepository`, and `NotificationJob`, and enqueue inline (post-commit, best-effort) from the appointment command handlers.

> Provider onboarding (WhatsApp Business API verification + utility-template approval, SMS sender-ID registration) is an operator/ops task, not code. The feature ships the senders; a channel only sends once its credentials + (for WhatsApp) an approved template are configured.

## What Changes
- Add a **`WhatsApp`** value to `NotificationType` (keep `Email`/`SMS`/`Both`; stored as `int` → no migration).
- New channel-generic sender seam **`IReminderChannelSender`** with two implementations: **`HttpSmsSender`** (config-driven generic HTTP gateway, alphanumeric sender ID) and **`WhatsAppSender`** (WhatsApp Business API, pre-approved utility template).
- New static config accessor **`RemindersConfig`** (mirrors `ConnectivityConfig`) reading a new `Reminders` section (channels, lead-time tiers, retry cap, per-channel endpoints).
- `CreateAppointmentCommand` / `UpdateAppointmentCommand` **enqueue / void** reminder rows inline post-commit (best-effort), alongside the existing `NotificationGenerator` calls.
- `NotificationJob.ProcessPendingNotifications` becomes a real dispatcher: connectivity-gated, routes each due row to the sender matching its channel, +216 phone normalization, bounded retry.
- Re-enable the `NotificationJob` recurring registration in `Program.cs` (`Cron.Minutely`) — carefully fixing the unclosed `/* */` block so the AI/calendar jobs stay disabled.

## Reminder scheduling (tiered lead times)
At create/reschedule, compute one send time from configurable tiers (default `[24h, 6h]`, min-lead `1h`), against `now` (UTC):
1. Largest tier `T` where `appointmentTime − T > now` → schedule at `appointmentTime − T` (prefer 24h, else 6h).
2. Else if `appointmentTime − 1h > now` (closer than 6h but still >1h out) → schedule **promptly** (next tick).
3. Else (`appointmentTime ≤ now + 1h`, or in the past) → **no reminder**.

## Acceptance Criteria
- **AC-1:** Creating an appointment with a patient, when ≥1 channel is configured, enqueues one `Pending` `Notification` **per configured channel** at the computed send time (per the tiered rule); each carries the rendered French reminder text (patient name, appointment date/time in clinic local time, clinic name) and links `AppointmentId`/`PatientId`.
- **AC-2:** Enqueuing is best-effort post-commit — any failure to enqueue is logged at Error and **never** rolls back or fails the appointment create/update (same contract as `NotificationGenerator`).
- **AC-3:** Rescheduling voids all **unsent** (`Pending`) reminder rows for the appointment and re-enqueues fresh reminders for the new time (same rule); reminders already `Sent` are not re-sent.
- **AC-4:** Cancelling or marking no-show voids all **unsent** reminder rows for the appointment so they never send.
- **AC-5:** The dispatcher runs as a minutely Hangfire recurring job; before any send it checks `IInternetProbe.IsInternetReachableAsync` — if the server has no internet it sends nothing, leaves rows `Pending`, and does **not** increment any retry count.
- **AC-6:** When online, each due `Pending` row is sent via the sender matching its `Type`, with the patient phone normalized to `+216` E.164: success → `MarkAsSent`; a missing patient, or empty/unparseable phone → `Failed` immediately (no retry, no crash); a transient send/gateway failure → left `Pending` and retried on later ticks up to `MaxRetries` (default 3), then `Failed` with the error message.
- **AC-7:** SMS sends via the configured HTTP gateway using the configured alphanumeric sender ID; WhatsApp sends via the configured Business API using the pre-approved **utility template** (a single body parameter carrying the reminder text) — WhatsApp free-text is never sent.
- **AC-8:** Secret credentials (SMS API key, WhatsApp access token) are read from environment / `.local`, never committed appsettings; committed `appsettings.json` holds only non-secret placeholders (channel toggles, sender ID, endpoints, template name).
- **AC-9:** A minutely tick never double-sends a row (a row leaves `Pending` on the attempt's commit); if no channels are configured, nothing is enqueued (no failure noise).
- **AC-10:** Cloud mode is functionally unchanged — `IInternetProbe` returns the static online default, so reminders send whenever channels are configured; no Cloud-only regression.

## Data / Schema Changes
- **`NotificationType`** — add `WhatsApp = 4`. Stored via existing `HasConversion<int>()` → **no migration**.
- **`Notification`** — no new columns. Voiding an unsent reminder = removing its `Pending` row (reuse `GetByAppointmentIdAsync`; add a repository removal, or use a status the `Pending` query already excludes). A transient failure must keep the row `Pending` and increment `RetryCount` without setting `Failed` until the cap — may require a small domain method (e.g. `RecordFailedAttempt()`) distinct from the terminal `MarkAsFailed`.
- **Message payload for both channels:** store the pre-rendered French text in `Notification.Message`; SMS sends it directly, WhatsApp passes it as the template's single `{{1}}` body parameter (avoids per-field template mapping and any schema change).
- **Config (`Reminders` section):** `Channels` (e.g. `["Sms","WhatsApp"]`), `LeadTimesHours` (`[24,6]`), `MinLeadHours` (`1`), `MaxRetries` (`3`), `Sms:{ApiUrl,SenderId}`, `WhatsApp:{ApiUrl,PhoneNumberId,TemplateName,TemplateLanguage}`. Secrets `Sms:ApiKey` + `WhatsApp:AccessToken` sourced from env/`.local` only.

## Out of Scope
- Email reminders (SMTP stub stays dormant); the dead domain-event → `AppointmentCreatedEventHandler` path stays inert.
- Recurring "recall" cadence or more reminders than the single tiered one per appointment.
- Two-way messaging / inbound replies / WhatsApp delivery-status webhooks (no new `[AllowAnonymous]` endpoint; if one is ever added it must be pinned in `ControllerAuthorizationCoverageTests`).
- Per-clinic (DB) channel selection — channels/credentials are **per-install config**; multi-tenant per-clinic config is not covered.
- Patient opt-in/opt-out UI, delivery dashboard, provider onboarding/Meta verification/template approval, and any frontend change.
- Re-sending a reminder that was already `Sent` before a reschedule.

## Edge Cases (Critical only)
- Appointment ≤1h out at create, in the past, or patient-less "busy slot" → no reminder enqueued.
- Empty/unparseable phone or patient not found at send time → row `Failed` immediately (never retried, never throws).
- Server offline across many ticks → rows stay `Pending` and send when internet returns; offline skips do **not** consume the retry budget.
- Reschedule into the ≤1h window → existing unsent rows voided, nothing re-enqueued.
- No channels configured (or a channel's credentials/template missing) → that channel enqueues/sends nothing rather than generating `Failed` spam.
