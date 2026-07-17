# Feature Specification: Per-Clinic Reminder Channels & Credentials

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-17
**Scope:** Full
**Feature:** Let each clinic (admin) configure its own SMS/WhatsApp reminder channels and sender credentials, stored encrypted per-clinic and overriding the per-install `Reminders` config, so a multi-clinic Cloud deployment sends under each clinic's own sender identity.

> **Scope note:** this is at/beyond the small-feature envelope (schema + encryption + resolver + endpoints + FE) and is marked `Type: Small` by explicit user direction. `/implement-small-feature` may re-flag the size; if the encryption or FE surface balloons, revisit `/define-feature`.

## Overview
Today reminder channels + credentials come from a single per-install `Reminders` config section (`RemindersConfig` over `IConfiguration`, secrets from env), and the dispatcher processes all `Notification` rows with those install-wide settings. This feature adds an optional per-clinic override: a clinic's admin sets which channels are enabled and that clinic's own sender identity + credentials, persisted (secrets encrypted at rest). At enqueue and at send time the effective settings are resolved per clinic, falling back to the per-install config when a clinic hasn't configured its own. Local/single-clinic installs are unaffected (they use the fallback).

## What Changes
- New per-clinic reminder settings (channels enabled + sender identity + secret credentials), persisted, admin-managed.
- Secret credentials (SMS API key, WhatsApp access token) are **encrypted at rest** via ASP.NET Core Data Protection; never returned to the client and never logged.
- The `Notification` outbox row records its owning `ClinicId` (populated at enqueue) so the dispatcher can resolve that clinic's credentials at send time.
- A new resolver returns **effective settings = per-clinic (if set) ?? per-install `RemindersConfig`**, consumed by the enqueuer (channel selection), the dispatcher, and the channel senders (endpoint + sender identity + secret + template).
- Admin-only endpoints to read (secret-masked) and update a clinic's reminder settings; a section in the existing clinic settings UI.

## Acceptance Criteria
- **AC-1:** An admin can GET their clinic's reminder settings; the response includes channel toggles + non-secret fields (sender id, phone-number id, template name/language) and a per-secret **configured/not-configured flag** — never the secret values.
- **AC-2:** An admin can PUT their clinic's reminder settings (channels enabled, sender identity, and secrets). Secrets are write-only: an omitted/blank secret field leaves the stored secret unchanged; a provided value replaces it. Non-admins are rejected (403/route auth), and a clinic can only read/write its own settings (tenant-scoped).
- **AC-3:** Secret credentials are stored encrypted at rest (ciphertext in the DB, not plaintext) and are decrypted only in-process at send time. Secrets never appear in any GET response, log line, or error message.
- **AC-4:** At enqueue, `ReminderScheduler` uses the appointment's clinic's **enabled channels** when the clinic has settings, else the per-install `Reminders:Channels`. Each enqueued `Notification` records its `ClinicId`.
- **AC-5:** At send time, the dispatcher resolves each row's clinic settings and sends via that clinic's endpoint/sender identity/secret/template when configured, else the per-install values. A row with no clinic (`ClinicId` null — legacy/global) uses the per-install config, preserving today's behavior.
- **AC-6:** A clinic with **no** reminder settings behaves exactly as today (per-install config) — Local/single-clinic installs and existing Cloud clinics are unchanged (fallback).
- **AC-7:** If a clinic enables a channel but its (or the install's) credentials for that channel are absent, the sender reports `NotConfigured` and the row is left `Pending` with no `Failed` spam — same contract as the parent feature.
- **AC-8:** The committed `appsettings.json` gains no secret values; the per-install path still sources secrets from env only. The per-clinic secrets live only in the (encrypted) DB.

## API Contract
### GET /api/clinics/reminder-settings
Response 2XX: `{ smsEnabled: bool|null, whatsAppEnabled: bool|null, smsSenderId: string|null, whatsAppPhoneNumberId: string|null, whatsAppTemplateName: string|null, whatsAppTemplateLanguage: string|null, smsApiKeyConfigured: bool, whatsAppAccessTokenConfigured: bool }`
Errors: `401/403 — not authenticated / not admin`

### PUT /api/clinics/reminder-settings
Request: `{ smsEnabled?: bool|null, whatsAppEnabled?: bool|null, smsSenderId?: string, whatsAppPhoneNumberId?: string, whatsAppTemplateName?: string, whatsAppTemplateLanguage?: string, smsApiKey?: string, whatsAppAccessToken?: string }`
Response 2XX: same shape as GET (secret-masked)
Errors: `400 — validation`, `401/403 — not authenticated / not admin`
Notes: `smsApiKey`/`whatsAppAccessToken` are write-only — omitted/empty ⇒ unchanged; provided ⇒ re-encrypted & replaced. (Provider endpoint URLs stay per-install — not per-clinic.)

## Data / Schema Changes
- **New entity `ClinicReminderSettings`** (1:1 with `Clinic`, own table): `ClinicId` (PK/FK), nullable `SmsEnabled`/`WhatsAppEnabled` (bool? — null = inherit install default), `SmsSenderId`, `WhatsAppPhoneNumberId`, `WhatsAppTemplateName`, `WhatsAppTemplateLanguage` (nullable strings), `SmsApiKeyEncrypted`, `WhatsAppAccessTokenEncrypted` (nullable strings — Data-Protection ciphertext), timestamps. EF config + repository + migration.
- **`Notification.ClinicId`** — new nullable `Guid?` column (nullable so pre-existing rows are unaffected), set by `ReminderScheduler` at enqueue. EF config + migration.
- **Encryption:** register ASP.NET Core Data Protection (`AddDataProtection().PersistKeysToFileSystem(...)`) with a mode-resolved key-ring path (Local: under `.local/` via `LocalInstallPaths`; Cloud: a configured directory). An `IReminderSecretProtector` (thin wrapper over `IDataProtector`) does protect/unprotect. (Multi-instance Cloud would need a shared key ring — note for ops, not this feature.)
- **Seam changes:** new `IReminderSettingsProvider` (Application) → impl resolves per-clinic (decrypting secrets) ?? per-install `RemindersConfig`; `IReminderChannelSender.SendAsync` gains a resolved-settings parameter so senders no longer read `RemindersConfig` directly; `ReminderScheduler` and `NotificationJob` consume the provider.

## Out of Scope
- Delivery-status webhooks, opt-in/opt-out, recall cadence, two-way messaging, email — unchanged from the parent `sms-whatsapp-reminders` spec.
- Provider onboarding (WhatsApp verification / template approval / SMS sender-ID registration).
- Per-clinic provider **endpoint URLs** (the gateway/Graph base URL stays per-install) and per-clinic lead-time/retry tuning.
- Multi-instance Cloud shared Data-Protection key ring (single-VPS file-system key ring assumed).

## Edge Cases (Critical only)
- Clinic with settings row but a channel toggled off → that channel is not enqueued for that clinic even if the install has it configured.
- Clinic enables a channel with no per-clinic secret and no per-install secret → `NotConfigured` (nothing sent, no `Failed`), per AC-7.
- Legacy `Notification` rows with `ClinicId` null (enqueued before this feature) → resolved via per-install config, never crash.
- Data-Protection key unavailable/rotated so a stored secret can't be decrypted → treat as `NotConfigured` for that channel (log at Error once), never throw in the dispatcher loop.
- Secret field submitted empty on PUT → keep existing stored secret (do not wipe); explicit clear needs a distinct signal (out of scope — omit for now, document).
