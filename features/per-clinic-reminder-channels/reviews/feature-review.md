# Feature Review: per-clinic-reminder-channels

**Status:** INCOMPLETE
**Challenged:** No
**Date:** 2026-07-17
**Parent Branch:** main (merge-base 9798b95)
**Review method:** working-tree review (feature is uncommitted in a dedicated worktree; reviewed `git diff HEAD` + untracked source files). 5 agents adapted to the MediatR/`Result<T>` + Clean Architecture stack (Code Quality, Error-Handling idiom [replaces ROP], Business Logic, Breaking Changes, Security). Generated migration `.Designer.cs` + model snapshot excluded from the reviewed diff; feature `.md` files excluded.
**Files Reviewed:** 34 files (~257 tracked lines modified + 14 new production files + 6 test files; ~2,430 diff lines)

## Findings

### Finding 1
- **Severity:** Major
- **Category:** Security
- **File:** api/ClinicManagement.Infrastructure/Extensions.cs
- **Line:** ~142
- **Anchor:** `AddInfrastructure` — the `AddDataProtection().SetApplicationName(...)` / `PersistKeysToFileSystem(keyRingPath)` block
- **Comment:** The Data Protection key ring is persisted with `PersistKeysToFileSystem(...)` but with **no key-encryption-at-rest** (`ProtectKeysWithDpapi()` / certificate). Supplying a custom key repository disables the framework's automatic at-rest key encryption, so the key-ring XML — which holds the master keys used to encrypt every clinic's SMS API key and WhatsApp access token — is written to disk in cleartext. Anyone who can read `<install>/.local/dataprotection-keys/*.xml` (or the Cloud `DataProtection:KeyRingPath`) can decrypt all per-clinic credentials; on Windows `Directory.CreateDirectory` inherits parent ACLs (potentially non-admin-readable). **Mitigating context:** `.local/` is gitignored (like the existing JWT signing key) and is excluded from the `pg_dump` backup, so DB-only theft still yields only ciphertext — the primary threat is covered. **Fix:** on Windows/Local chain `.ProtectKeysWithDpapi()` after `PersistKeysToFileSystem` (or `ProtectKeysWithCertificate(...)` for a portable option), and/or tighten the key-ring directory ACLs to the service account; at minimum document the ACL requirement.

### Finding 2
- **Severity:** Minor
- **Category:** Business Logic
- **File:** api/ClinicManagement.API/BackgroundJobs/NotificationJob.cs (root also in api/ClinicManagement.Application/Common/Models/ResolvedReminderSettings.cs)
- **Line:** ~104 (DispatchAsync send call)
- **Anchor:** `NotificationJob.DispatchAsync` / `ResolvedReminderSettings.EnabledChannels`
- **Comment:** `ResolveAsync` computes `EnabledChannels` (per-clinic toggle ?? install default) onto `ResolvedReminderSettings`, but **neither the dispatcher nor the senders ever read it** — dispatch only checks whether credentials are present. Two coupled issues: (a) the `required` `EnabledChannels` field is dead production state (only set + asserted in tests; forces every construction site to populate it); (b) a latent correctness gap — if a channel was enabled when rows were enqueued and the admin later toggles it OFF while keeping its credentials, those already-`Pending` rows still send over the now-disabled channel. The enqueue-time gate (`ReminderScheduler.ResolveEnabledChannelsAsync`) correctly honors the toggle, so this is not a strict AC violation, but the two symptoms point at one decision. **Fix (pick one):** either have `DispatchAsync` skip a row whose channel is not in `settings.EnabledChannels` (treat as NotConfigured → leave Pending), which closes the gap and makes the field meaningful; **or** drop `EnabledChannels` from `ResolvedReminderSettings` if the enqueue-time gate is deemed sufficient.

### Finding 3
- **Severity:** Minor
- **Category:** Code Quality
- **File:** api/ClinicManagement.Application/Features/Clinics/Queries/GetClinicReminderSettingsQuery.cs (and Commands/UpdateClinicReminderSettingsCommand.cs)
- **Line:** Handle method of each
- **Anchor:** `GetClinicReminderSettingsQueryHandler.Handle` / `UpdateClinicReminderSettingsCommandHandler.Handle`
- **Comment:** The caller-resolution + admin-gate block (`GetUserId()` → null check → `GetByAuth0SubAsync` → null check → `IsAdmin()` failure) is duplicated verbatim across the two new handlers (~15 identical lines), each also wrapping the body in a broad `try/catch → Result.Failure($"...: {ex.Message}")`. Consider extracting a small shared helper (e.g. resolve-current-admin returning `Result<User>`) so the admin rule lives in one place. (Note: the handler-level `IsAdmin()` re-check is defensible defense-in-depth alongside the controller `[Authorize(AdminOnly)]`; the actionable part is the cross-handler duplication. The sibling `RegenerateClinicCode` handler shares the same shape, so this is a broader pattern — keep the fix scoped.)

### Finding 4
- **Severity:** Suggestion
- **Category:** Code Quality
- **File:** api/ClinicManagement.API/BackgroundJobs/NotificationJob.cs
- **Line:** ~104
- **Anchor:** `NotificationJob.DispatchAsync`
- **Comment:** `_settingsProvider.ResolveAsync(notification.ClinicId)` is called once per pending row; each call hits `GetByClinicIdAsync` + re-reads config keys. A tick with many rows for the same clinic (or many null-ClinicId legacy rows) repeats the identical lookup — an N+1 within the tick. The provider is scoped but does no memoization. Consider caching resolution per `ClinicId` for the tick (a small `Dictionary<Guid?, ResolvedReminderSettings>`), consistent with the already-per-scope decrypt-failure de-dupe.

### Finding 5
- **Severity:** Suggestion
- **Category:** Code Quality
- **File:** api/ClinicManagement.Infrastructure/Services/ReminderSettingsProvider.cs
- **Line:** ResolveSecret calls in ResolveAsync
- **Anchor:** `ReminderSettingsProvider.ResolveAsync` / `ResolveSecret`
- **Comment:** The channel labels `"SMS"` / `"WhatsApp"` are passed as bare string literals into `ResolveSecret(...)`, where they double as the log field value and the de-dupe key in `_decryptFailuresLogged`. A `NotificationType` enum already models these channels — passing the enum (and formatting it in the log template) removes the magic strings and keeps the de-dupe key aligned with the real channel identity.

### Finding 6
- **Severity:** Suggestion
- **Category:** Code Quality
- **File:** api/ClinicManagement.Infrastructure/Services/WhatsAppSender.cs
- **Line:** SendAsync (templateLanguage fallback)
- **Anchor:** `WhatsAppSender.SendAsync`
- **Comment:** The default template language `"fr"` is a hardcoded literal in the fallback (`string.IsNullOrWhiteSpace(settings.WhatsAppTemplateLanguage) ? "fr" : ...`). Promote it to a named `private const string` (e.g. `DefaultTemplateLanguage`) for discoverability, matching the "no magic strings" convention. (Parity note: `RemindersConfig.WhatsAppTemplateLanguage` already defaults to `"fr"`, so behavior is unchanged — this is purely a constant-naming cleanup.)

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| Major | 1 |
| Minor | 2 |
| Suggestion | 3 |
| **Total** | 6 |

Agents reporting NO FINDINGS: Error-Handling idiom (dispatcher batch-isolation, decrypt→NotConfigured, best-effort scheduler, Result→HTTP mapping all verified) and Breaking Changes (all `IReminderChannelSender`/ctor/`new Notification` call sites updated; migration additive + tool-generated; DI lifetimes sound; no-settings clinics byte-for-byte unchanged).
