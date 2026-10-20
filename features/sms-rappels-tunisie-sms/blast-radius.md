# Blast radius — Rappels SMS via Tunisie SMS

Written before the first edit (`.claude/rules/regression-safety.md` § 1).

| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `PhoneNumber.IsTunisianMobile` | new VO member | none | unaffected — additive |
| 2 | `Patient.SmsNumberE164()` | new domain method over owned `AdditionalPhoneNumbers` | none | unaffected — additive; owned collection loads with the patient |
| 3 | `Appointment.ActsLabel()` | new domain method over `Procedures` | `GetByIdAsync` includes `Procedures` (`AppointmentRepository.cs:23`) | unaffected — additive |
| 4 | `Notification` ctor + `AppointmentMomentUtc` + `RecordRenderedMessage` | outbox entity + column | `ReminderScheduler` (2 sites), 4 test files, `AuditInterceptorTests` | must change — new optional ctor arg (defaulted), migration, no backfill |
| 5 | `ReminderMessage.FormatAppointmentMoment` | date formatter | `ReminderScheduler`, `NotificationJob`, `NotificationJobTests` | must change — delegates to `ReminderText.FormatMoment` (one owner) |
| 6 | `ReminderMessage.AnnouncesStaleMoment` | body-parsing staleness check | `NotificationJob`, `WhatsAppReminderTemplate` doc | must change — legacy rows only (null moment) |
| 7 | `ReminderScheduler.BuildMessage` / `ReachabilityOfAsync` | enqueue | create / recurring / update appointment handlers, `ScheduleRecallAsync` | must change — per-channel reachability, moment stored, preview rendered by `ReminderText`; `ReminderConsentCoverageTests` must stay green |
| 8 | `NotificationJob.DispatchAsync` | dispatcher | `NotificationJobTests`, `NotificationJobMessagingTests`, `OutboxParkingTests` | must change — consent re-check, SMS number, moved (column), render at send |
| 9 | `OutboxMessagingGate.ReviewAsync` | forfait gate | `NotificationJob` (dispatch + review), `OutboxMessagingGateTests`, `WhatsAppTemplateLifecycleTests` | must change — SMS metered, template term WhatsApp-only, cache per (clinic, channel); `:101` assertion flips on purpose |
| 10 | `NotificationJob.EnsureCountingRowAsync` | per-send counter | `NotificationJobMessagingTests:277` | must change — SMS counts 1; assertion flips on purpose |
| 11 | `ReminderScheduler` recall exhaustion check | recall enqueue | `RecallMessagingRefusalTests` | must change — « every sendable channel metered + exhausted » |
| 12 | `ReminderSettingsProvider.ClaimsItsOwnSms` | credential-ownership boundary | `ReminderSettingsChannelIsolationTests` | must change — false when vendor sells messaging; LAN unchanged |
| 13 | `UpdateClinicReminderSettingsCommand` | settings write | `UpdateClinicReminderSettings…Tests`, web `reminder-settings.tsx` | must change — `ManualSms` refusal on hosted, template + lead-time rules |
| 14 | `ReminderSettingsDto` | wire DTO | web `types.ts`, `reminder-settings.tsx`, `rappels-screen.tsx` | must change — additive fields (`SmsVendorManaged`, `SmsMaxLength`, `AllowedLeadTimeHours`) |
| 15 | `HttpReminderChannelSender.PostJsonAsync` | shared HTTP plumbing | `WhatsAppSender`, `HttpSmsSender` (push senders have their own `PostJsonAsync`) | must change — add `ClassifySuccess` hook (default `Sent`); WhatsApp unaffected by default |
| 16 | `HttpSmsSender` → `TunisieSmsSender` | SMS sender (one per channel, `ToDictionary`) | DI `Extensions.cs:418`, `ReminderChannelSenderTests` | must change — replace, never add |
| 17 | `NotificationRepository` reminder-log counts | log counters | `/rappels` | must re-test — channel-agnostic, SMS held rows now counted |
| 18 | `GetOutboxDepthQuery` / `GET /api/outbox` | mirrors the DUE scan | — | unaffected — the due-scan predicate is not changed |
| 19 | `web/app/rappels/page.tsx` | parked page | nav, `/rappels` | must change — un-park, WhatsApp blocks hidden |
| 20 | `web/components/clinic-settings.tsx` | `/settings` | admins | must re-test — new card mounted |
| 21 | `web/components/reminder-settings.tsx` | channels sheet | `/rappels` | must change — SMS gateway fields hidden when vendor-managed |
| 22 | `ReminderLogDto.SendingChannels` (QA F1) | additive wire field | built in `GetClinicReminderLogQuery` only; read by `rappels-screen.tsx` | must change — both in this pass |
| 23 | `ResolvedReminderSettings.CanSend` (QA F1) | shared predicate | `ReminderScheduler.IsSendable`, `NotificationJob.IsSendable`, the log query | must change — the two private copies now delegate |
