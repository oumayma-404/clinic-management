# Progress: SMS & WhatsApp Appointment Reminders

**Started:** 2026-07-17
**Type:** Small
**Branch:** feature/sms-whatsapp-reminders (git worktree, based on feature/windows-desktop-app @ 12b5501)

## Status
- [x] Implementation
- [x] Quality checks (build, lint, typecheck)
- [x] Tests (added — see Test Plan + Tests Run below)

## Test Plan
| AC | Action | Target file | Notes |
|----|--------|-------------|-------|
| AC-1 | New | `ReminderSchedulerTests.cs` + `ReminderScheduleTests.cs` | one Pending/channel at computed send time; tiered timing rule |
| AC-2 | New | `ReminderSchedulerTests.cs` | persistence failure swallowed (best-effort) |
| AC-3 | New | `ReminderSchedulerTests.cs` | reschedule voids unsent + re-enqueues (incl. into-soon-window edge) |
| AC-4 | New | `ReminderSchedulerTests.cs` | void removes only Pending, leaves Sent |
| AC-5 | New | `Api/NotificationJobTests.cs` | offline → nothing sent, no retry increment, no commit |
| AC-6 | New | `Api/NotificationJobTests.cs` + `ReminderPhoneTests.cs` | +216 normalization; missing patient/bad phone → Failed; transient below cap → Pending+retry; at cap → Failed; success → Sent |
| AC-7 | New | `ReminderChannelSenderTests.cs` | SMS sends with sender id + bearer key; WhatsApp posts approved template w/ single body param to `{ApiUrl}/{PhoneNumberId}/messages`; non-2xx → transient |
| AC-8 | Coverage note | — | Secrets read via `IConfiguration` (env vars); no unit surface. Verified by build + the appsettings placeholder review (no committed secret values). |
| AC-9 | New | `ReminderSchedulerTests.cs` + `Api/NotificationJobTests.cs` | no channels → nothing enqueued; NotConfigured → left Pending (no Failed spam); per-row commit (one SaveChanges per row) |
| AC-10 | Coverage note | — | Cloud path == the online path in `NotificationJobTests` (probe reachable → sends); no separate Cloud-only unit surface. |

Test-class count note: 5 new classes (over the ~5 "too big" heuristic by design) — this IS genuinely new
feature surface, but each class is targeted and there is no user flow to E2E, so no full-pipeline escalation
(per the skill's breadth-not-count carve-out).

## Tests Run
| Suite | Filter | Result |
|-------|--------|--------|
| Unit (new) | `ReminderScheduleTests\|ReminderPhoneTests\|ReminderSchedulerTests\|ReminderChannelSenderTests\|NotificationJobTests` | **38 passed, 0 failed** |
| Unit (related existing) | `AppointmentSyncMappingTests\|AppointmentTenantIsolationTests\|NotificationGenerationTests` | **32 passed, 0 failed** (no regression from the ctor-arg change) |

Run via the Smart-App-Control workaround (SAC is ON): `dotnet build ...UnitTests.csproj -p:OutDir=<scratch>`
then `dotnet vstest <scratch>/ClinicManagement.UnitTests.dll --TestCaseFilter:"..."`. Build 0 errors, 0
warnings in new/changed test files (fixed 2 xUnit1031 blocking-call warnings by reading the stubbed request
body via `ReadAsStream()` instead of `.Result`). No Postman/Newman (user preference); no E2E (small feature).

## Quality check result
`dotnet build ClinicManagement.sln --no-incremental` → **Build succeeded, 0 errors**. No new warnings in any
changed file (the only warnings on touched files — `Notification.cs(24)` CS8618, `Program.cs(231)` CS0618 —
are pre-existing and sit on unchanged lines, not on the code this feature added). Backend-only feature (Scope:
BE): no frontend typecheck/lint applies. No migration (NotificationType stored as int; no new columns).

Test-infra compile fix (auto-approved, mechanical — NOT a scenario change): the two appointment command
handlers gained an `IReminderScheduler` ctor parameter, which broke 6 handler instantiations in the unit-test
project (`AppointmentSyncMappingTests`, `AppointmentTenantIsolationTests`, `NotificationGenerationTests`). Each
was passed an extra `new Mock<IReminderScheduler>().Object` so the test project compiles at 0 errors. No test
assertions/mocks/payloads were changed — reminder-behavior tests are deferred to /test-small-feature.

## Working tree note (start of session)
Worktree created from HEAD of `feature/windows-desktop-app` (commit 12b5501) — a clean committed base
that carries the full Phase 1–5 Local-mode + notification-center infra this feature builds on. The main
checkout's large uncommitted WIP (other features: graceful-error-handling, cnam-bulletin, facturation,
etc.) is intentionally NOT present here. Only the untracked `spec.md` was copied in.

## Files Changed
Backend (.NET) only — no frontend, no migration (NotificationType stored as int; no new columns).

- `api/ClinicManagement.Domain/Enums/NotificationType.cs` — add `WhatsApp = 4`.
- `api/ClinicManagement.Domain/Entities/Notification.cs` — add `RecordFailedAttempt(error, maxRetries)`.
- `api/ClinicManagement.Domain/Repositories/INotificationRepository.cs` — add `RemoveAsync`.
- `api/ClinicManagement.Infrastructure/Repositories/NotificationRepository.cs` — impl `RemoveAsync`.
- `api/ClinicManagement.Application/Common/Interfaces/IReminderScheduler.cs` — NEW seam (enqueue/void).
- `api/ClinicManagement.Infrastructure/Services/RemindersConfig.cs` — NEW static config accessor.
- `api/ClinicManagement.Infrastructure/Services/ReminderSchedule.cs` — NEW tiered send-time calc (pure).
- `api/ClinicManagement.Infrastructure/Services/ReminderPhone.cs` — NEW +216 E.164 normalizer (pure).
- `api/ClinicManagement.Infrastructure/Services/IReminderChannelSender.cs` — NEW sender seam + result types.
- `api/ClinicManagement.Infrastructure/Services/HttpReminderChannelSender.cs` — NEW shared HTTP base.
- `api/ClinicManagement.Infrastructure/Services/HttpSmsSender.cs` — NEW SMS gateway sender.
- `api/ClinicManagement.Infrastructure/Services/WhatsAppSender.cs` — NEW WhatsApp Business template sender.
- `api/ClinicManagement.Infrastructure/Services/ReminderScheduler.cs` — NEW enqueuer impl (best-effort).
- `api/ClinicManagement.Infrastructure/Extensions.cs` — register scheduler + both senders.
- `api/ClinicManagement.Application/Features/Appointments/Commands/CreateAppointmentCommand.cs` — enqueue post-commit.
- `api/ClinicManagement.Application/Features/Appointments/Commands/UpdateAppointmentCommand.cs` — void/reschedule/re-enqueue.
- `api/ClinicManagement.API/BackgroundJobs/NotificationJob.cs` — rewrite as connectivity-gated dispatcher.
- `api/ClinicManagement.API/Program.cs` — re-enable NotificationJob minutely; keep AISummaryJob disabled.
- `api/ClinicManagement.API/appsettings.json` — add `Reminders` section (placeholders only; no secrets).

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| `IReminderScheduler` impl placed in Infrastructure (not Application, unlike sibling `NotificationGenerator`) | The enqueuer needs `IConfiguration` (channels/lead-times); the Application csproj has no config abstractions and must not depend on Infrastructure. Interface stays in Application (called by handlers); impl is config-aware in Infrastructure. Internal scope, no public-contract change to callers. |
| French date formatting (Tunisia tz + fr-FR) duplicated privately in `ReminderScheduler` | Sharing with `NotificationGenerator` would require editing that other-feature file; keeping a small self-contained copy avoids scope creep. |
| Secrets (`Reminders:Sms:ApiKey`, `Reminders:WhatsApp:AccessToken`) read via `IConfiguration` (environment variables) rather than a bespoke `.local` file store | `IConfiguration` already merges env vars; committed appsettings holds only non-secret placeholders (AC-8). `.local` file-based secret loading for reminders is not wired (would add a new file store for no functional gain over env vars). |

## Significant Deviations
(none)
