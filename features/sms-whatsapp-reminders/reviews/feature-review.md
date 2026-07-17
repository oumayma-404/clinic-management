# Feature Review: sms-whatsapp-reminders

**Status:** INCOMPLETE
**Challenged:** No
**Date:** 2026-07-17
**Parent Branch:** feature/windows-desktop-app
**Merge Base:** 12b55012efd4dbbd80dc91b8e67aaeacc0de928f
**Files Reviewed:** 27 changed code/test files (+~1621, -45); `features/**` docs excluded from the agent diff
**Review method:** 5 parallel agents adapted to the clinic-management stack (MediatR + `Result<T>`, no ROP): Code Quality, Error-Handling/CQRS (repointed from ROP), Business Logic, Breaking Changes, Security.

## Findings

### Finding 1
- **Severity:** Minor
- **Category:** Error Handling
- **File:** api/ClinicManagement.API/BackgroundJobs/NotificationJob.cs
- **Line:** ~112
- **Anchor:** `NotificationJob.DispatchAsync` — `case ReminderSendOutcome.TransientFailure`
- **Comment:** Transient send failures (and the eventual cap-crossing to `Failed` inside `RecordFailedAttempt`) are recorded only onto `notification.ErrorMessage` and persisted — **nothing is logged**. The HTTP exception is also swallowed unlogged inside `HttpReminderChannelSender.PostJsonAsync`. This is inconsistent with the permanent-failure path (`FailAsync` logs a Warning). A gateway that keeps 5xx-ing, and a reminder that permanently fails after exhausting its retry budget, produce zero operator-visible signal. Add a `LogWarning` in the `TransientFailure` branch (at minimum when the row crosses to `Failed`) mirroring `FailAsync`, including `NotificationId`, `RetryCount`/`MaxRetries`, and the error.

### Finding 2
- **Severity:** Minor
- **Category:** Business Logic / Correctness
- **File:** api/ClinicManagement.API/BackgroundJobs/NotificationJob.cs
- **Line:** ~127
- **Anchor:** `NotificationJob.SaveAsync` (the "a later tick can never double-send it (AC-9)" comment) / `ProcessPendingNotifications`
- **Comment:** The per-row commit prevents *batch-rollback* double-sends, but the comment's absolute "can never double-send" overstates the guarantee — the dispatcher is really **at-least-once**, via two windows: (a) `Cron.Minutely` with no `[DisableConcurrentExecution]` — Hangfire does not serialize recurring runs, so a batch that exceeds ~1 min (each send has a 15s timeout) can overlap the next tick and both runs read the same row as `Pending` before either commits; (b) send succeeds but the subsequent `SaveChangesAsync` throws → row stays `Pending` → next tick re-sends. Mitigation: add `[DisableConcurrentExecution(timeoutSeconds)]` to the job to close window (a), and soften the comment to reflect at-least-once semantics (true dedup needs an idempotency key, likely out of scope).

### Finding 3
- **Severity:** Minor
- **Category:** Breaking Change / Regression
- **File:** api/ClinicManagement.API/BackgroundJobs/NotificationJob.cs
- **Line:** ~82
- **Anchor:** `NotificationJob.DispatchAsync` — `if (!_senders.TryGetValue(notification.Type, out var sender))`
- **Comment:** A row whose `Type` has no matching sender (legacy `Email`/`Both` rows) is left `Pending` and `LogDebug`-skipped **forever** — `GetPendingNotificationsAsync` returns every due `Pending` row, so such rows are never terminal and the minutely job re-scans an ever-growing backlog. Agent 4 tied this to `AppointmentCreatedEventHandler`, which still constructs a `NotificationType.Both` reminder on appointment-create; **however** Agent 3 + repo docs (spec Out-of-Scope; root CLAUDE.md) state that domain-event dispatch is **not wired**, so that handler is inert and produces no rows today. **To verify in challenge:** (a) confirm no `IDomainEvent`/MediatR dispatch fires `AppointmentCreatedEventHandler` (if it does fire, this rises to Major — a live duplicate-reminder path alongside `ReminderScheduler` + an unbounded `Both` backlog); (b) regardless, consider having the dispatcher terminally resolve rows whose channel has no sender (→ `Failed`) instead of leaving them `Pending` forever, especially for existing Cloud DBs that may already hold a `Both` backlog accumulated while the job was disabled.

### Finding 4
- **Severity:** Suggestion
- **Category:** Code Quality
- **File:** api/ClinicManagement.API/BackgroundJobs/NotificationJob.cs
- **Line:** ~48
- **Anchor:** `NotificationJob.ProcessPendingNotifications`
- **Comment:** The job method takes no `CancellationToken`, so `GetPendingNotificationsAsync()`, `SendAsync(...)`, and `SaveChangesAsync()` run with `default` — a shutdown/redeploy can't cooperatively cancel an in-flight batch of outbound HTTP sends. Hangfire can inject a `CancellationToken` (job-cancellation) argument; accept it and thread it through the repo/sender/UoW calls.

### Finding 5
- **Severity:** Suggestion
- **Category:** Code Quality
- **File:** api/ClinicManagement.Infrastructure/Services/ReminderScheduler.cs
- **Line:** ~130
- **Anchor:** `ReminderScheduler.SafelyAsync`
- **Comment:** All three public entry points funnel failures through one `catch` that logs `"Failed to schedule/void appointment reminders."` with no identifying context, so a logged error can't be tied to an appointment. Pass the appointment id (and an operation label) into `SafelyAsync` and include them in the structured log.

### Finding 6
- **Severity:** Suggestion
- **Category:** Security / Privacy
- **File:** api/ClinicManagement.Infrastructure/Services/HttpSmsSender.cs
- **Line:** ~40
- **Anchor:** `HttpSmsSender.SendAsync` (not-configured branch); same in `WhatsAppSender.SendAsync`
- **Comment:** The not-configured branch logs the full patient E.164 phone (PII): `"...skipping SMS reminder to {Phone}."`. It's at `Debug` (below Info+, acceptable per spec), but this branch fires every minute for every pending row while a channel is enabled-but-unconfigured, so a Debug-enabled install accumulates patient phone numbers in logs. Drop the phone or mask it (last 3 digits); `NotificationId` already identifies the row.

### Finding 7
- **Severity:** Suggestion
- **Category:** Dead Code
- **File:** api/ClinicManagement.Infrastructure/Extensions.cs
- **Line:** ~120
- **Anchor:** `AddInfrastructure` — `services.AddScoped<INotificationService>(...)`
- **Comment:** `NotificationJob` no longer depends on `INotificationService` (replaced by `IReminderChannelSender`). After this change `INotificationService`/`NotificationService` (the log-only email/SMS stub) has no remaining live consumer, yet is still registered. Not a break — but remove the registration + stub, or add a comment that it's intentionally retained, to avoid confusion over which outbound path is live.

### Finding 8
- **Severity:** Suggestion
- **Category:** Documentation
- **File:** api/ClinicManagement.Domain/CLAUDE.md (and api/ClinicManagement.API/CLAUDE.md, root CLAUDE.md)
- **Line:** n/a
- **Anchor:** CLAUDE.md maps
- **Comment:** Per the repo convention ("update the nearest CLAUDE.md when code changes"), several maps are now stale: the Domain enum table lists `NotificationType` as `Email, SMS, Both` (no `WhatsApp`); the API BackgroundJobs table still says `NotificationJob` sends via `INotificationService` with "registration commented out"; and the root/Infra notes still call the `Notification` outbox "dormant"/"stub that logs". (Note: the small-feature pipeline has no `/update-memory` step, so this is a deliberate follow-up rather than a pipeline miss.)

### Finding 9
- **Severity:** Suggestion
- **Category:** Code Quality
- **File:** api/ClinicManagement.API/appsettings.json
- **Line:** ~61
- **Anchor:** `"Reminders"` section
- **Comment:** Agent 1 flagged the `"// Channels"`/`"// ApiUrl"` pseudo-comment keys as a config-polluting hack. **Likely a non-issue:** this pattern is the repo's **existing convention** — the committed `Backup`, `Cors`, `Https`, and `Hosting` sections all use the same `"// key"` documentation style, and `RemindersConfig` reads each concrete key by name (never `.Get<T>()` on the whole section), so the comment keys are inert. Included only so `/challenge-review` can confirm consistency with precedent; if confirmed, reject. (If the section were ever bound via `.Get<RemindersOptions>()`, the comment keys would need to move out.)

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| Major | 0 |
| Minor | 3 |
| Suggestion | 6 |
| **Total** | 9 |

Business-logic agent verified all 10 acceptance criteria against the code with no findings. The Minors are observability/idempotency hardening; the strongest item to resolve in `/challenge-review` is Finding 3 (is `AppointmentCreatedEventHandler` truly inert?).
