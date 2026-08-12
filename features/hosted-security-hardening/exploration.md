# Exploration — hosted-security-hardening

**Gathered:** 2026-08-11
**Profile under examination:** `HostedMultiTenant` (hosted multi-tenant cloud backend, `AUTH_MODE=local`, own accounts), reached by a browser, the WPF+WebView2 desktop shell, the Android WebView shell and the iOS WKWebView shell.

> **Why this file exists.** The feature ships in four parts and **each part starts in a fresh session**. This is the carried context: every verified fact, with `file:line`, so a part can begin without re-deriving the state of the world. Read your part's section plus § 0.
>
> Everything here was verified by reading the source on 2026-08-11. Nothing in it is a proposal.

---

## § 0. Facts every part needs

### 0.1 The trust boundary today

| Hop | State | Evidence |
|---|---|---|
| client → Caddy | **TLS**, Let's Encrypt, HSTS `max-age=31536000; includeSubDomains` (no `preload`) | `deploy/Caddyfile:14-16, 18, 27` |
| Caddy → api | cleartext `http://api:5000` | `deploy/Caddyfile:30-32`; `deploy/docker-compose.hosted.yml:44` |
| api → Postgres | cleartext, **no `sslmode` anywhere in the repo** | `deploy/docker-compose.hosted.yml:61`; repo-wide grep for `sslmode`/`SslMode`/`Ssl Mode` → **0 hits** |
| api → MinIO | cleartext, `MinIO__UseSSL: "false"` | `deploy/docker-compose.hosted.yml:91`; `api/ClinicManagement.Infrastructure/Extensions.cs:242-251` |
| api → console site | cleartext `http://api:5443` | `deploy/docker-compose.hosted.yml:190`; `deploy/Caddyfile:98-115` |

Postgres and MinIO publish **no host ports** (`deploy/docker-compose.prod.yml:45-46`), so the exposure is container-local + host-level.

**No certificate pinning anywhere**, in any client. Searched `pinning`, `publicKeyHashes`, `pin-sha256`, `ServerCertificateCustomValidation`, `TrustServerCertificate` → only unrelated prose. This is **correct and should stay** — pinning a 90-day Let's Encrypt cert across three app stores is an outage generator.

### 0.2 At rest — nothing is encrypted

- Postgres volume and MinIO volume are plain Docker named volumes. No LUKS, no `dm-crypt`, no `pgcrypto`, no TDE, no `MINIO_KMS_*`. All → **0 hits**.
- **Exactly 4 encrypted columns exist in the whole product**, all integration/2FA secrets, all via ASP.NET Data Protection:
  | Column | Protector | Purpose string |
  |---|---|---|
  | `ClinicReminderSettings.SmsApiKeyEncrypted` | `ReminderSecretProtector` | `Infrastructure/Services/ReminderSecretProtector.cs:16-25` |
  | `ClinicReminderSettings.WhatsAppAccessTokenEncrypted` | same | — |
  | `ClinicReminderSettings.SmtpPasswordEncrypted` | same | — |
  | `PlatformAccount.ProtectedTotpSecret` | `PlatformSecretProtector` | `ClinicManagement.PlatformConsole.TotpSecret.v1`, `Infrastructure/Security/PlatformSecretProtector.cs:24` |
- **Zero PHI columns are encrypted.** Patient names, phone, e-mail, address, `Allergies`, `MedicalHistory`, `Notes`, `ImportantNotes`, `PatientMedicalHistory`, `PatientFamilyHistory`, `DentalRecord`, `ToothState`, `MedicalDocument.ContentJson`, `Invoice`, `Payment` — all plaintext. This is a **deliberate, decided** scope boundary; see § 0.6.
- `Clinic.GoogleRefreshToken` is stored **cleartext** (`Domain/Entities/Clinic.cs:218`; column `text`, no converter, `Infrastructure/Persistence/Configurations/ClinicConfiguration.cs:65-66`). It is redacted from the export archive (`ClinicArchiveScope.cs:91-99`) but not from the database.
- The **Data Protection key ring is plaintext XML on a Docker volume**: `DataProtection__KeyRingPath: /keys` (`deploy/docker-compose.hosted.yml:80`), volume `dataprotection_keys` (`:111-112`, `:254`). `ProtectKeysWithDpapi` applies **only** when `RunsAsWindowsService && OperatingSystem.IsWindows()` — false here (`Infrastructure/Security/LocalDataProtection.cs:92-108`). Startup **throws** if the path is unset (`:67-75`).
- **No KMS / Vault / HSM anywhere.** `PersistKeysToFileSystem` is the only persistence provider (`LocalDataProtection.cs:95`).
- All secrets arrive as **env vars** from the gitignored `deploy/.env`. No `secrets:` block, no `*_FILE` indirection → 0 hits.

### 0.3 Identity today

- **Clinic accounts have no second factor.** `LoginCommand` is `(Email, Password)` only (`Application/Features/Auth/Commands/LoginCommand.cs:15-17`). `User` has no TOTP columns. Every `ITotpService` reference lives under `Features/Platform/Auth/`.
- Access token: HS256, **30 min** (`Infrastructure/Auth/LocalAuthConfig.cs:21, 52-53`). Claims `sub`, `clinic_id`, `role`, `jti`, `token_version`, optional `email`/`name`.
- Refresh token: **a stateless JWT, nothing is stored**, 12 h, audience `{Audience}-refresh` so it is rejected as a bearer (`LocalAuthConfig.cs:20, 61-62`; `LocalAuthService.cs:104-120`). Sliding rotation mints a fresh one on every exchange and **the superseded one stays valid until its own expiry — deliberately**, because two tabs exchanging at once must both keep working (`Features/Auth/Commands/RefreshTokenCommand.cs:17-21, 80-83`).
- **`User.TokenVersion` is the only revocation lever** (`Domain/Entities/User.cs:187, 231, 279`; checked per request by `LocalAuthEnforcementMiddleware`, on refresh at `RefreshTokenCommand.cs:65-68`).
- Password hashing: `new PasswordHasher<User>()` = ASP.NET Identity v3 = **PBKDF2-HMAC-SHA256, 100 000 iterations** on .NET 8. **No `PasswordHasherOptions` configured anywhere** (0 hits). Rehash-on-login is wired (`LocalAuthService.cs:48`).
- `Application/Common/PasswordPolicy.cs` — `MinLength = 8`. No complexity, no breach list, no history. The client mirrors it as a literal `MIN_PASSWORD_LENGTH = 8` (`web/components/change-password-form.tsx:11`).
- **Lockout: two tiers, both before password verification.** In-memory per (account, source) 5 / 15 min (`Infrastructure/Auth/LoginAttemptTracker.cs:26, 29`) + durable per account 50 / 15 min (`Domain/Entities/User.cs:19, 21, 217, 263-270`). Both return the same French sentence.
- **Rate limiting** (`API/Startup/RateLimiting.cs`): anonymous-auth policy 30 / 300 s partitioned `account:{email}|{ip}` (falling back to `ip:` when the body is unreadable), a per-address ceiling 150 / 300 s, and a global API limiter 600 / 60 s keyed `user:{sub}` else `ip:{ip}`. Account capture runs before the limiter (`Program.cs:738`; `Startup/AuthAttemptAccount.cs`).
- **No CAPTCHA** of any kind — `captcha`, `recaptcha`, `hcaptcha`, `turnstile` → 0 hits.
- Session cookie `local_session` holds the **refresh** token: `{ httpOnly: true, secure, sameSite: 'lax', path: '/' }`, no `__Host-` prefix, no `Domain` (`web/lib/auth/session-cookie.ts:55`). `secure` is forced by `AUTH_COOKIE_SECURE: "true"` (`docker-compose.hosted.yml:169`).
- The three shells **store no credential natively** — no Keychain, no Android Keystore, no DPAPI (0 hits in all three). Each relies on its WebView's own cookie store.

### 0.4 Integrity today

- **The audit ledger is an ordinary table.** No trigger, no `REVOKE`, no RLS, no hash chain, no `PreviousHash`. The app connects as the DB **owner** (`POSTGRES_USER`). Migration `20260803153257_AddAuditEntries.cs:14-42`; config `Configurations/AuditEntryConfiguration.cs`.
- **No ordering guarantee.** `OccurredAt` is `DateTime.UtcNow` read once per save (`AuditSaveChangesInterceptor.cs:193-194`), `Id` is a random v4 GUID with `ValueGeneratedNever`. There is no sequence and no `bigint identity`. The only order is applied at read time: `.OrderByDescending(OccurredAt).ThenBy(Id)` (`AuditEntryRepository.cs:67-70`), which is stable for paging but **not causal**.
- Audit writes are **swallow-and-log at Error** on both phases (`AuditSaveChangesInterceptor.cs:243-250, 416-426`) — the audited operation commits regardless.
- `GET /api/backup/archive` streams a complete unencrypted PHI zip and **leaves no trace** — no audit row, no ledger row, no dedicated rate limit. See § 4.2.

### 0.5 Browser surface today

- `Security:EnforceCsp` **is set nowhere**: not in `api/ClinicManagement.API/appsettings.json` (there is no `"Security"` section at all), not in `deploy/docker-compose.hosted.yml`, not in `deploy/.env.hosted.example`. Only `Security__TrustedProxies__0` is set. ⇒ **`/api/*` has served `Content-Security-Policy-Report-Only` for the life of the deployment.**
- There is **no `report-uri` / `report-to` anywhere** (0 hits repo-wide), so the report-only policy reports to a browser console and nowhere else.
- Page responses get an **enforcing**, byte-identical CSP from Caddy (`deploy/Caddyfile:63-68`). The policy carries `'unsafe-inline' 'unsafe-eval'` on `script-src`; the middleware's own docstring says enforcing it "does not stop XSS".
- **No `Permissions-Policy`, no COOP/COEP/CORP anywhere.** The console site (`deploy/Caddyfile:98-115`) has **no CSP and no HSTS** — three headers only.
- `web/next.config.ts:26-41` returns `[]` for headers when `AUTH_MODE === 'local'`, which is this profile.
- `console/next.config.ts` is 9 lines, `output: "standalone"` only, **no `headers()`**.
- `UseHttpsRedirection()` is registered (`Program.cs:688-691`) but `AddHttpsRedirection(HttpsPort)` is called only in the two certificate-bearing branches (`:614, :630`) and no `HTTPS_PORT` is set in the compose file ⇒ **silently a no-op**.
- **`UseForwardedHeaders` is deliberately absent** (`Infrastructure/TrustedProxies.cs:23`, `ClientIp.cs:16`, `LocalRequest.cs:14`). Consequence: `Request.IsHttps` is false for every proxied request, so the API's own HSTS never fires (`SecurityHeadersMiddleware`, the `context.Request.IsHttps` guard).
- CORS resolves to exactly **one** origin, `https://${DOMAIN}` (`Infrastructure/CorsOrigins.cs:52-57`; `docker-compose.hosted.yml:75`), same-origin with the pages, so CORS is not exercised by the browser here.

### 0.6 Scope boundary decided before this spec

Settled by `/think-solution` on 2026-08-11 — **Option 1, Infrastructure & identity hardening**. Explicitly **out of scope**:

- **Application-level PHI field encryption.** Rejected because it breaks `Application/Common/SearchTerm.cs` + `Infrastructure/Persistence/SqlSearch.cs` (PostgreSQL `unaccent` free-text search, documented as load-bearing: "a patient on page 7 reads as « aucun résultat »"), `Features/Patients/PatientDuplicateIndex` (name+DOB / name / phone) and SQL ordering on name for paged reads.
- **Moving Postgres / object storage to managed services.** A hosting and cost decision, orthogonal to the code.

---

## § 1. Part 1 — Identity

### 1.1 The TOTP implementation to mirror (do not re-invent)

**`ITotpService`** — `Application/Common/Interfaces/ITotpService.cs:9`. Two members: `string GenerateSecret()` (`:16`, docstring pins that only the bootstrap verb may call it) and `bool VerifyCode(string base32Secret, string code)` (`:26`, docstring pins "one step either side").

**`TotpService`** — `Infrastructure/Auth/TotpService.cs:19`. Library **`Otp.NET` 1.4.1** (`ClinicManagement.Infrastructure.csproj:30`). `SecretBytes = 20` (160 bits, `:22`). `VerificationWindow(previous: 1, future: 1)` (`:24`) ⇒ a code is valid at most 90 s. **SHA-1 / 6 digits / 30 s step are Otp.NET defaults and are deliberately unconfigured** (`:9-11`: "those *are* the algorithm every authenticator app implements"). `VerifyCode` never throws — a malformed secret or code returns `false` inside `try/catch (ArgumentException)`, because it runs on an anonymous endpoint and a 500 would distinguish a corrupted account from a wrong password (`:28-49`). Registered `AddSingleton<ITotpService, TotpService>()` at `Infrastructure/Extensions.cs:166`, i.e. inside `AddInfrastructure`, which is what lets a console verb resolve it.

**`PlatformRecoveryCode`** — `Domain/Entities/PlatformRecoveryCode.cs:25`. `Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"` (no `0/O/1/I`), `Length = 20` (100 bits), `CountPerEnrolment = 8` (`:29-35`). `Hash(code)` = hex SHA-256 of `Normalize(code)`; `Normalize` strips spaces and `-`, trims, uppercases (`:70-74, 93-97`). Plain SHA-256 rather than PBKDF2 is reasoned at `:14-19` (100-bit CSPRNG secret; a per-row salt would drag a hasher into Domain). `Consume()` throws on a second call — deliberately not idempotent (`:103-112`).

**A recovery code is spent even when the sign-in it accompanied fails** — `RedeemPlatformRecoveryCodeCommand.cs`: password verified first (`:60-66`) so a wrong password burns nothing, then `ConsumeRecoveryCode` (`:68`), then **its own `SaveChangesAsync` at `:76` before** the `IsActive` check at `:78`. Tested `PlatformAuthTests.cs:284` and its mirror `:300`.

**`PlatformLoginCommand` check order** (`:60-117`) — this is the order to reproduce:
1. load by normalised e-mail; null → `invalid_credentials` (401)
2. **lockout before password** → `too_many_attempts` (429)
3. password; failed → `RecordFailedLogin()` + save → `invalid_credentials`
4. `!IsActive` → `account_disabled` (401) — disclosed only after a correct password
5. `!IsTotpEnrolled` → **`totp_enrolment_required` (403)**, carrying nothing else
6. blank code → **`totp_required` (401)**
7. wrong code → `RecordFailedLogin()` + save → **`invalid_credentials`** — a present-and-wrong code is indistinguishable from a wrong password
8. `SuccessNeedsRehash` → upgrade hash (does **not** bump `TokenVersion`)
9. success

An undecryptable secret **refuses and logs**, never bypasses (`VerifyTotp`, `:124-136`).

**`PlatformAuthRefusals`** — `Features/Platform/Auth/PlatformAuthRefusals.cs:24-32`, with `MessageFor` (`:39-51`, null for unknown) and a reflection-derived `AllCodes` (`:54-59`). Status mapping lives in the **controller** (`PlatformAuthController.StatusFor:110-122`), not in the `Result.Code`.

| Code | French | Status |
|---|---|---|
| `invalid_credentials` | Identifiants invalides. | 401 |
| `totp_required` | Code de vérification requis. | 401 |
| `totp_enrolment_required` | Ce compte doit d'abord enrôler son second facteur. | 403 |
| `totp_invalid` | Code de vérification invalide. | 400 |
| `totp_already_enrolled` | Le second facteur est déjà enrôlé pour ce compte. | 409 |
| `account_disabled` | Ce compte a été désactivé. | 401 |
| `too_many_attempts` | Trop de tentatives. Réessayez dans quelques minutes. | 429 |
| `password_policy` | Le mot de passe doit contenir au moins {MinLength} caractères. | 400 |

**⚠️ No QR code is produced anywhere for TOTP.** Repo-wide grep for `otpauth` → **0 hits**. The console operator types the raw base32 string. `Infrastructure/Services/QrCodeGenerator.cs` **does** exist with one live caller, `API/Controllers/TrustController.QrCode` (`:131-138`), the LAN trust page.

**⚠️ Untested today:** `PlatformSecretProtector` (no test file), `PlatformAccountProvisioning` (no test file), `PlatformAccountCommand` argument parsing (no test file).

### 1.2 The console-verb pattern (for `reset-user-totp`)

The closest mirror is **`PlatformAccountCommand`** (`API/Maintenance/PlatformAccountCommand.cs:36`), which already has a `--reset-totp` mode.

- Arg parsing `:45-59`: `ConsoleArgs.ReadOption(args, "--email")` + `args.Contains("--reset-totp", OrdinalIgnoreCase)`; `CountTrue(create, deactivate, resetTotp) != 1` → `Usage()`. `ConsoleArgs.cs:17-31` is the only `--flag value` reader, and **a value starting with `--` reads as absent**.
- Wiring `:61-101`: `InstallConfiguration.BuildForConsoleVerb()` → `MaintenanceDatabase.HasConnectionString(configuration, "<description>")` → `new ServiceCollection().AddLogging().AddInfrastructure(configuration)` → scope → `IAuditActorProvider.RunAs(CommandName)` → `ITenantScope.UseSystemWide(reason)` → the work.
- **The gate is a configured connection string, not a capability** (amendment M3). `MaintenanceDatabase.HasConnectionString` has exactly one member and writes a French-free English sentence to **stderr** on failure (`Maintenance/MaintenanceDatabase.cs:28-39`). Hosted invocation is `docker exec clinic-api-prod dotnet ClinicManagement.API.dll <verb>`.
- **`SubscriptionVerbs.BuildProvider` uses `AddInfrastructure` and never `AddApplication`** (`:31-43`) — `AddApplication` registers the claims-reading `AuditActorProvider` over the process one, whose `IClinicContext` needs an `IHttpContextAccessor` that does not exist in a verb.
- Exit codes: 0 / 1 for most; `verify-schema`, `reconcile-money` use **2** for "ran and found drift"; `subscription-report` uses 2 for findings.
- **The dispatch trap:** `Program.cs:20-160` is 16 independent `if (args.Length > 0 && string.Equals(args[0], X.CommandName, OrdinalIgnoreCase)) { return await X.RunAsync(args); }` blocks. There is **no default arm and no unknown-verb error path** — nothing after `:160` inspects `args`, so a verb with no branch **boots the web host** and reads to an operator as "the command did nothing". `SubscriptionVendorCommandReachabilityTests.cs:83-101` guards this for the five `Subscription*Command` types **only**; `platform-account`, `verify-schema`, `count-activity` etc. are **not** covered by it.
- ⚠️ Verb output language is inconsistent today: `reset-admin-password`, `provision-clinic`, `verify-schema`, `reconcile-money`, `platform-account` render **English**; `harden-permissions`, `protect/read-credentials`, `restore-backup` and the `subscription-*` dates render **French**.

### 1.3 The client half

**The six shell-less routes are shell-less by *not importing `AppShell`*** — there is no layout, no route group and no flag. `web/app/layout.tsx:93-131` mounts only the providers. Stated as load-bearing at `web/components/app-shell.tsx:98-100`. ⚠️ A new enrolment route therefore inherits **every provider**, including `LocalSessionProvider`, which fires `/bff/auth/session`, arms the 30-minute inactivity timer and subscribes to `onMustChangePassword`.

**`web/middleware.ts`** — `PUBLIC_ROUTES = ['/login','/setup','/join','/signup','/signup/verifier']` matched by **exact path** (`:8`, `.includes`), which is why `/signup` and `/signup/verifier` are listed separately. The local branch gates on **cookie presence alone** (`:36-39`) — no signature, expiry or decode. The forced-change gate is `mustChange && pathname !== '/change-password'` (`:43-46`) and **short-circuits before `next()`**, so any second forced gate placed after it never runs for a user who owes a password change. `frontDoorRedirect` (`:16-20`) builds an **absolute** URL from `x-forwarded-host`/`x-forwarded-proto` because a relative `Location` throws `ERR_INVALID_URL` behind YARP.

**`web/lib/auth/session-cookie.ts` is the single cookie writer.** `writeSessionCookies` (`:48-64`) always writes `local_session` **and** either sets or clears `local_must_change_password` — the two are written together (`:59-63`). `clearMustChangeCookie` (`:73-75`) is **exported with no caller**. `isSecure` (`:32-36`) prefers `AUTH_COOKIE_SECURE` over the request scheme.

**The BFF login route has exactly two exits and discards `code`** — `web/app/bff/auth/local-login/route.ts`: 429 + `{error}` + `Retry-After` (`:39-45`), everything else flattened to **401 + `{error}`** (`:47-50`, comment: "so the endpoint never discloses more"), or 200 + `{mustChangePassword}` **with the cookie already written** (`:55-65`). It never reads or forwards `code`. Contrast the console's `console/app/bff/session/route.ts:61-68`, which relays `{error, code}` and states at `:13-15` that flattening it "would remove the one thing that makes AC-1.3a reachable".

**`LocalLoginForm` reads only `res.ok` and `data?.error`** (`web/app/login/page.tsx:82-87`) and navigates with a full `window.location.href` on success (`:94`). It has no branch on any body field and no second mode.

**The client refusal surface** — `web/lib/api/client.ts`:
| Hook | Trigger | Replaces message? | Routes? |
|---|---|---|---|
| `onClientTooOld` (`:125-130`) | status **426**, status only | no | no — `<ClientVersionGate>` takes the screen |
| `onSubscriptionRequired` (`:171-176`) | `code ∈ {subscription_required, _suspended, _missing}`, **code not status** | **no — passes through verbatim** | no — provider re-reads |
| `onMustChangePassword` (`:147-152`) | `code === 'must_change_password'` (403) | **yes — the one place `client.ts` replaces a server message** (`:180-186`) | **yes** — `LocalSessionProvider` does `window.location.href = '/change-password'`, guarded against a loop (`session.tsx:176-181`) |

`handleRequest` (`:499-536`) has a **one-shot 401 retry**, only when the caller did not supply a token, calling `getAccessToken(true)` to force a real renewal. `apiHeaders(token, contentType)` (`:680-696`) is the **single writer** of clinic-API headers; `scripts/check-responsive.mjs`'s `api-headers` check fails on a `Bearer` literal anywhere else (`app/**/route.ts` exempt as a role).

**The pattern for "a gate takes the screen"** exists twice, byte-identically: `web/components/session-lock-gate.tsx:112-117` and `web/components/client-version-gate.tsx:74-80` — `role="alertdialog" aria-modal="true" aria-labelledby` + `fixed inset-0 z-50 flex h-dvh items-start justify-center overflow-y-auto bg-background p-4` + `<Card className="my-auto w-full max-w-md">`. Four stated decisions: opaque `bg-background` not a scrim (the app behind must not be readable); `h-dvh`; `items-start` + `overflow-y-auto` + `my-auto` on the child (§ 11's vertical clipping trap — `items-center` inside a scroller clips); `z-50` clears the toaster. Both write `min-h-11` explicitly on buttons because `size="lg"` is only `h-10` = 40 px.

**The console already ships a complete TOTP prompt *and* enrolment screen** in one component with four modes — `console/app/login/sign-in-form.tsx`, `type Mode = "login" | "enrol" | "recovery" | "codes"` (`:20`). Its header comment (`:10-18`) states why one component: an account told « enrol your factor first » must arrive at the enrolment form **with its address and password intact**, and the transition is driven by the refusal's `code`, "never through a French sentence this file would have to match". Enrolment success **stops the flow** on the codes screen rather than signing in (`:75-81`). The TOTP field is `type="text"` + `inputMode="numeric"` + `autoComplete="one-time-code"` — `type="number"` would eat a leading zero (`:190-191`). The recovery field is deliberately **not** `type="password"` (`:173`) because it is copied from paper.

**There is no step-up / re-authentication UI anywhere in `web/`** (0 hits for TOTP, MFA, two-factor, authenticator, one-time-code, step-up). The only password re-entry is `change-password-form.tsx`, and that is a credential for the change itself, not a gate. Sensitive actions today use ordinary `AlertDialog` confirms or `ui/confirm-by-typing-dialog.tsx` (type a phrase, not a password).

### 1.4 The frontend gate (`.claude/rules/frontend-web.md`)

Usable at **320 px**, at a **380 px viewport height**, at **200 % zoom**. 44 px minimum on a **coarse pointer** (`coarse:`), never gated on a breakpoint. `h-dvh`/`min-h-dvh`, never `h-screen`. Text-entry primitives keep `text-base md:text-sm` (overriding with `text-sm` makes iOS zoom). Failure → `showErrorToast`, **form left open with input intact**. An inline async result → `role="status"`. No English string reaches a user.

Gate commands, in `web/`:
```bash
npm run check:responsive
npx tsc --noEmit
npm run build
```
Then an eye pass at **320 / 390 / 820 / 1180 / 1440**, plus landscape phone, plus with a keyboard. ⚠️ `npm run lint` **cannot** be the gate — `eslint` is in the script but not in `devDependencies`. There is no test runner and no CI in `web/`.

---

## § 2. Part 2 — Transit

### 2.1 What binds what

`Program.cs:616-672`, the hosted `else` branch: `httpsConfigured` is false (no `Https:CertPath`), so `ConsoleListenerPlanning.Resolve` (`:642`) yields both ports and **both are bound in one `ConfigureKestrel` call** — `ListenAnyIP(publicPort)` and `ListenAnyIP(consolePort)` (`:647-653`), neither with `UseHttps`. ⚠️ This shape is load-bearing: an explicit Kestrel endpoint **overrides `ASPNETCORE_URLS` wholesale**, so a bare `ListenAnyIP(consolePort)` would unbind 5000 and take the whole product dark while the console worked perfectly.

`API/Startup/ConsolePortGate` refuses **both** directions — a console path on the public port and a non-console path on the console port — matched with `StartsWithSegments`. Registered unconditionally (`Program.cs:724-733`).

### 2.2 The middleware pipeline, verbatim from `Program.cs`

```
:688-691 if (!profile.SelfHostsFrontDoor) app.UseHttpsRedirection();
:692     app.UseCors("AllowAll");
:697     app.UseMiddleware<SecurityHeadersMiddleware>();
:704-717 if (profile.ExposesTrustEndpoints && trustPort > 0) → TrustPortGate 404
:724-733 ConsolePortGate 404                                     // unconditional
:738     app.UseAuthAttemptAccountCapture();
:743     app.UseRateLimiter();
:745     app.UseMiddleware<ExceptionMiddleware>();
:751     app.UseMiddleware<ClientVersionMiddleware>();
:753     app.UseAuthentication();
:759     app.UseMiddleware<AccountStateMiddleware>();
:766     app.UseMiddleware<PlatformAccountStateMiddleware>();
:768     app.UseAuthorization();
:774     app.UseMiddleware<PlatformTenantScopeMiddleware>();
:779     app.UseMiddleware<TenantScopeMiddleware>();
:784-787 if (profile.EnforcesTokenState) app.UseMiddleware<LocalAuthEnforcementMiddleware>();
:794     app.UseMiddleware<SubscriptionGateMiddleware>();
:796     app.MapControllers();
```
Rationale comments at `:755-758, :776-778, :781-783, :789-793`: a 402 must never mask a 401/403, which is why the subscription gate is after, not beside, the tenant scope. Ordering is pinned by `SubscriptionGateMiddlewareTests` and `AccountStateEnforcementTests` **against `Program.cs`'s own source**.

### 2.3 Fail-loud precedent, and the trap

`LEARNINGS.md:45` — **gate mode-invariant guards on the *mode*, not on a *capability* flag**: the `httpsConfigured` trap. `DeploymentProfile.cs:33-39` states the class invariant that **every capability is derived from `Kind` and nothing else — no operator setting may flip one**, and `DeploymentProfileTests.cs:250-257` cites it as the reason.

`LEARNINGS.md:97` — **security/transport config must fail closed and loud**, including `LocalRequest.IsLoopback` returning `true` on a null `RemoteIpAddress` (a gate defaulting to allow).

Existing precedents for a startup throw: `LocalDataProtection.cs:67-75` (key-ring path required in this profile), `PlatformAuthConfig.cs:26, 69` + `Program.cs:389-392` (console signing key, no fallback to the clinic key), `DeploymentProfile.Resolve` (`:262-282`, an unrecognised profile value **throws** naming the valid values), and the empty-DB-connection-string `return 1`.

### 2.4 Certificates

**There is no `init/` folder and no init container anywhere in `deploy/`.** `deploy/` contains: `.env` (gitignored), `.env.example`, `.env.hosted.example`, `Caddyfile`, `README.md`, `docker-compose.prod.yml`, `docker-compose.hosted.yml`, `docker-compose.selfhosted-lan.yml`, `lan-cert.sh` (mode 755), `backup/{Dockerfile,backup.sh,entrypoint.sh}`, `postgres/{Dockerfile,pitr-backup.sh,pitr-entrypoint.sh}`, `rclone/.gitkeep`.

**`CertificateProvisioner` is the `SelfHostedLan` CA + SAN minter** and is **not reusable here**: it runs **pre-`Build()`**, so it has no DI and takes a real logger (`LEARNINGS.md:51`), and it is Windows-service-shaped. `provision-cert` is its verb, gated on `profile.SelfSignsCertificate` with **no DB gate** (`ProvisionCertCommand.cs:40-46`).

---

## § 3. Part 3 — Custody

### 3.1 The key ring

`Infrastructure/Security/LocalDataProtection.cs` — `:67-75` throws when `DataProtection:KeyRingPath` is unset in this profile; `:92-108` applies `ProtectKeysWithDpapi` **only** under `RunsAsWindowsService && IsWindows()`; `:95` `PersistKeysToFileSystem` is the only provider. Volume `dataprotection_keys` → `/keys` (`docker-compose.hosted.yml:80, 111-112, 254`).

**⚠️ The two operator instructions contradict each other today:**
- `deploy/README.md:55-56` — "Back up the `dataprotection_keys` volume **alongside** `postgres_data`."
- `deploy/docker-compose.hosted.yml:248-253` and `deploy/.env.hosted.example:83-88` — back it up **SEPARATELY, never into the same archive**.

The ring decrypts every clinic's SMS/WhatsApp/SMTP credentials and every console TOTP secret. It is **not** mounted into the backup sidecar (`docker-compose.prod.yml:173-176` mounts `minio_data`, `backups`, `./rclone` only), so nothing automated backs it up.

### 3.2 The backup sidecar

`docker-compose.hosted.yml:215-221` extends `docker-compose.prod.yml:155-177`.
- `deploy/backup/backup.sh:15` — `pg_dump --format=custom --no-owner --no-privileges` of the **whole cluster DB (all tenants)**.
- `:24` — `tar czf` of the entire MinIO volume.
- `:29` — `rclone copy` to `${BACKUP_REMOTE}` using an operator-supplied `./rclone/rclone.conf` mounted read-only. Default in the example env `offsite:clinic-backups` (`.env.hosted.example:49`).
- **Not encrypted by the sidecar.** No `gpg`, no `openssl enc`, no `--crypt`; the Dockerfile installs only `rclone tar gzip` (`deploy/backup/Dockerfile:5`). An rclone *crypt* remote would be invisible here — `rclone.conf` is gitignored.
- Credentials plaintext in env: `PGPASSWORD`, `PGUSER` (`docker-compose.prod.yml:167-172`). Schedule busybox `crond`, default `0 2 * * *` (`deploy/backup/entrypoint.sh:6-12`).

**WAL-G PITR** ships WAL + base backups to S3 with **no encryption** — `WALG_PGP_KEY` / `WALG_LIBSODIUM_KEY` are absent (`docker-compose.prod.yml:36-42, 206-212`; `.env.hosted.example:53-67`). Feature docs at `features/postgres-pitr/`.

**In-app backup is off in this profile**: `BacksUpItsOwnData` is `SelfHostedLan`-only (`DeploymentProfile.cs:225`), so the two write endpoints 404 and `BackupJob` is not registered. `GET /api/backup/history` still answers, reporting `managedByHost`.

### 3.3 The secret-protection pattern to extend

`IPlatformSecretProtector` (`Application/Common/Interfaces/IPlatformSecretProtector.cs:16`) — `string Protect(string)`, `bool TryUnprotect(string, out string)`. **Returns a bool deliberately rather than a nullable somebody could `??` past** (`:14`). Impl `Infrastructure/Security/PlatformSecretProtector.cs:22`: its own purpose string so reminder ciphertext and TOTP ciphertext are not interchangeable (`:11-14`); `TryUnprotect` sets `secret = string.Empty` first, catches everything, logs a French sentence naming the recovery verb, and **never throws and never yields the input** (`:39-62`). Registered `AddSingleton` in `AddInfrastructure` (`Extensions.cs:169`), which is what lets a console verb resolve it.

`ReminderSecretProtector` (`Infrastructure/Services/ReminderSecretProtector.cs:16-25`) is the sibling. `DbCredentialProtector` (`Infrastructure/Security/DbCredentialProtector.cs`) is reachable only from the Windows-installer verbs `protect-credential`/`read-credential`.

---

## § 4. Part 4 — Evidence & surface

### 4.1 The "a read that must be recorded" precedent

**`PlatformAccessEntry`** (`Domain/Entities/PlatformAccessEntry.cs:22-24`) — `AggregateRoot<Guid>` with **no mutator at all**, so append-only by construction. **No FK to `Clinics`, none to `PlatformAccounts`** (`Configurations/PlatformAccessEntryConfiguration.cs:18-20`, migration `20260810200159_AddPlatformAccessLedger.cs:17-23`) — "evidence does not hang off its subject"; a cascade would delete exactly the audit row worth having. Hence denormalised `AccountEmail`/`ClinicName`. Idempotency guard is a **partial-unique index**, never a read-first check:
```csharp
builder.HasIndex(e => e.IdempotencyKey).IsUnique()
    .HasFilter("\"IdempotencyKey\" IS NOT NULL");   // :44-47
```
Blank key collapses to null in the ctor (`:104-105`) because PostgreSQL treats each NULL as distinct.

**`PlatformAccessLedger.RecordAsync`** (`Application/Features/Platform/PlatformAccessLedger.cs:36-60`) **stages** and leaves the commit to the caller, so on a read path only the row is saved and on a write path it rides the same transaction. `RequireAccountId` **throws** when no console account is in scope (`:67-76`). Stated at `:15-20`: *"This is not a post-commit best-effort side effect like `INotificationGenerator`: AC-7.3 says every detail read **is** recorded, so a read that could not be attributed must not succeed."*

**`GetPlatformClinicDetailQuery`** (`:103-113`) stages the row then saves; its catch is `when (ex is not ConflictException)` → French `Result.Failure`, i.e. a failed ledger write **surfaces as a failed read**. It is a **`Query` not a `Command`** because `RealtimeBroadcastBehavior` derives its key from the namespace and one under `.Commands` would broadcast into a clinic group on every page load (`:20-33`).

### 4.2 The archive path

`API/Controllers/BackupController.cs:33` class-level `[Authorize(AdminOnly)]`; `GET archive` at `:152-168` carries `[AllowsWithoutSubscription(...)]` and is **not** gated on `BacksUpItsOwnData`.

`BuildClinicArchiveQuery` re-checks `caller.IsAdmin()` in the handler (`:76-80`) and resolves the clinic from the **DB user record** (`:62-86`).

**⚠️ It is buffered in memory, not streamed** (`:88-102`): `ZipArchive` in Create mode seeks back to write each entry's directory record and an HTTP body is forward-only, so the whole archive is held **twice** (a `MemoryStream` plus `.ToArray()`) plus the `FileContentResult`. **There is no size cap on the download path** — the upload path has `Backup:ArchiveMaxSizeMb`, default 1024 (`BackupController.cs:44, 221-229`), but the download has none. Blobs dominate, so a multi-GB cabinet is a multi-GB LOH allocation.

Contents (`Application/Features/Backup/Archive/ClinicArchiveFormat.cs`): `manifest.json`, `data/<Entity>.json` per table as indented property bags, `blobs/<storage key verbatim>`. **Not encrypted, stated deliberately** (`:16-19`). The table set is **derived from the EF model, not listed** (`Infrastructure/Persistence/ClinicArchiveScope.cs:11-15, 231-241`); `Excluded` at `:56-77`; `Redacted` = `Clinic.GoogleRefreshToken`, `GoogleCalendarId` at `:91-99`.

**Recording today: none.** No audit row (the interceptor only sees `SaveChanges`, and the GET writes nothing), no `PlatformAccessEntry`, no dedicated rate limit — it falls to the **global** limiter, 600 requests / 60 s per `sub`, so one admin may pull 600 full-cabinet archives a minute. The only trace is one `LogInformation` (`BuildClinicArchiveQuery:97-99`).

**⚠️ LIVE DEFECT, uncommitted work on this branch:** `Application/Features/Backup/Archive/ClinicArchiveRestorer.cs:79` —
```csharp
if (outcome.Restored > 0)
{
    store.ForgetRestoredRows(); // RED PROOF — revert
    await unitOfWork.SaveChangesAsync(cancellationToken);
    store.ForgetRestoredRows();
}
```
`ForgetRestoredRows()` is `ChangeTracker.Clear()`, so the staged inserts are discarded **before** the save: the restore reports rows as restored and persists none. Fix independently of this feature.

### 4.3 CSP — what enforcing costs today

`API/Middleware/SecurityHeadersMiddleware.cs` reads `Security:EnforceCsp` **once at construction** (a per-request read would let a config reload change the header a page's assets are already loading under). The policy is written on `Response.OnStarting` (the response may already be streaming after `next()`), and a `ContainsKey` guard never overwrites an upstream policy — **two CSP headers make the browser enforce their intersection**.

**⚠️ Enforcing `script-src 'self'` today would break the app**: `web/app/layout.tsx:4, 127` renders `<Analytics />` from `@vercel/analytics/next`, which loads a **third-party script origin**. That breaks under an enforcing `script-src 'self'` / `connect-src 'self'` **before any nonce work**. Authored inline script is otherwise clean — `dangerouslySetInnerHTML` → **0 matches**, `next/script` or `<script` in `web/` → **0 matches**. The remaining nonce hazards are framework-generated: `next-themes`' pre-hydration inline script (the reason for `suppressHydrationWarning`, `layout.tsx:94-98`), Next's own hydration payload, and `next/font/google` injecting inline `<style>`.

`SecurityHeadersMiddlewareTests` asserts 7 cases (report-only by default in all three profiles, the flag changes only the header **name**, an upstream policy survives, the baseline three are always present, HSTS never over plain HTTP). Its harness needs a `RecordingResponseFeature` that **replays `OnStarting`** because `DefaultHttpContext.StartAsync` never invokes them. **Not asserted:** the policy string itself, HSTS-on over HTTPS, or byte-identity with `deploy/Caddyfile`.

### 4.4 Logging

Serilog (`appsettings.json:2-32`): Console + **rolling File** `logs/clinic-management-.log`, daily, `retainedFileCountLimit: 7`, no size cap, `Default: Information`, plain-text output template. No destructuring policy, no masking sink, no `Filter` section. **In hosted the `api` service mounts only `dataprotection_keys:/keys`** (`docker-compose.hosted.yml:111-112`) — no logs volume, no log-driver override, no aggregation, so logs live on the container's ephemeral layer plus stdout.

**Every PHI log statement** (exhaustive over `api/`; 8 of 11 are at `Information` or above and therefore written to the file):

| File:line | Level | Template |
|---|---|---|
| `Infrastructure/Services/PdfGenerationService.cs:487` | **Information** | `Generating payment receipt PDF for {Patient}` |
| `PdfGenerationService.cs:589` | **Error** | `Error generating receipt PDF for {Patient}` |
| `PdfGenerationService.cs:598` | **Information** | `Generating avoir PDF {Number} for {Patient}` |
| `Infrastructure/Services/GoogleCalendarSyncService.cs:77` | Debug | `Appointment found: Patient={PatientName}, …` |
| `GoogleCalendarSyncService.cs:329` | Debug | `Extracted patient name from event: {PatientName}` |
| `GoogleCalendarSyncService.cs:628` | **Information** | `Found patient by ID … {PatientName}` |
| `GoogleCalendarSyncService.cs:679` | **Information** | `Patient '{PatientName}' not found. Creating…` |
| `GoogleCalendarSyncService.cs:699` | **Warning** | `Cannot extract patient name from '{PatientName}'…` |
| `GoogleCalendarSyncService.cs:731` | **Information** | `Created new patient: '{PatientName}' (ID: {PatientId})…` |
| `GoogleCalendarSyncService.cs:736` | **Information** | `Found matching patient: '{PatientName}' (ID: {PatientId})…` |
| `GoogleCalendarSyncService.cs:792` | **Information** | `Created appointment … for patient {PatientName}` |

Adjacent: `HuggingFaceAIService.cs:161` logs a raw model payload; `SmtpDocumentEmailSender.cs:85` logs `{FileName}`, and document file names composed by `DocumentFileNaming` can embed a patient name. **No log emits `{Email}`, `{FirstName}`, `{LastName}`, `{FullName}` or an unmasked `{Phone}`.**

**The masking precedent** is `Infrastructure/Services/ReminderPhone.Mask` (`:24-33`) — keeps the last 3 digits, `"(none)"` for empty. It has exactly **two** production call sites, both `LogDebug` on the not-configured branch (`HttpSmsSender.cs:31`, `WhatsAppSender.cs:35`). A distinct user-facing masker lives at `ReminderStatusMapper.cs:40`, whose comment separates the two.

---

## § 5. Conventions any part must honour

### 5.1 The derived-guard-test house style

Canonical example: `api/ClinicManagement.UnitTests/Common/TenantScopeFilterTests.cs`.

1. State the criterion in the **class docstring**.
2. Derive the candidate set by **reflection or a `SolutionSources` scan**, never a folder or name list. `Common/SolutionSources.cs` — `Root([CallerFilePath])` walks up to `ClinicManagement.sln` (`:18-31`, deliberately not `AppContext.BaseDirectory` because of the SAC scratch-`OutDir` workaround); `CsFiles(root)` (`:43-67`) skips `bin`/`obj`.
3. `Assert.NotEmpty(candidates)` — "found nothing" must not read as "nothing was wrong".
4. Exceptions as a `Dictionary<name, reason>` asserted **equal in both directions**, so it fails on a new violation **and** on a stale exemption.
5. A companion test that every exemption still names something real.
6. An **executed red-proof** in the same file (`The_Guard_Rejects_…`).

Excerpt (`TenantScopeFilterTests.cs:167-184`):
```csharp
[Fact]
public void Every_Clinic_Owned_Table_Is_Either_Filtered_Or_A_Named_Decision()
{
    using var db = Unset();
    var clinicOwned = db.Model.GetEntityTypes()
        .Where(e => e.FindProperty("ClinicId") is not null || e.ClrType == typeof(Clinic)).ToList();
    var unfiltered = clinicOwned.Where(e => e.GetQueryFilter() is null)
        .Select(e => e.ClrType.Name).Distinct().OrderBy(n => n).ToList();
    Assert.Equal(UnfilteredByDesign.Keys.OrderBy(n => n), unfiltered);
}
```

Existing tests that **parse a non-C# file** — the precedent for a compose-file guard: `RealtimeResourceResolverTests` parses `web/lib/realtime/clinic-hub.ts` via `[CallerFilePath]` (`:103`); `CnamClosedSetContractTests` parses `web/lib/cnam.ts` (`:66`). Existing tests that assert **against `Program.cs`'s own source**: `AccountStateEnforcementTests:178-181`, `SubscriptionGateMiddlewareTests`, `MigrationLockTests:29-68`, `SubscriptionVendorCommandReachabilityTests:83-101`.

### 5.2 Adding an 18th `DeploymentProfile` capability

`Infrastructure/Deployment/DeploymentProfile.cs`. Five edits: **(a)** a `bool` ctor parameter (`:46-64`), **(b)** an assignment (`:66-83`), **(c)** a public get-only `bool` property with an XML doc stating *why each ✗ is its own decision*, **(d)** one literal per kind in the `For(kind)` switch (`:289-378`) with an inline reason, **(e)** a row in `DeploymentProfileTests.ExpectedMatrix` (`:36-74`) — `Every_capability_is_covered_by_the_matrix` (`:183-190`) reflects every `bool` property and fails without one. If true of `HostedMultiTenant` alone, it also needs an entry in `hostedOnlyCapabilities` (`:118-123`) or `Both_pre_existing_kinds_reproduce_the_old_IsLocalMode_truth_table` (`:95`) fails.

The 17 today: `UsesLocalAccounts`, `FailClosedAuthz`, `EnforcesTokenState`, `UsesDiskStorage`, `SelfHostsFrontDoor`, `SelfSignsCertificate`, `RunsAsWindowsService`, `DefersMigrations`, `RunsStartupBackfills`, `ExposesTrustEndpoints`, `HasLocalDbTooling`, `ExposesMetaOnboarding`, `AllowsSelfRegistration`, `AllowsPublicClinicSignup`, `ServesPlatformConsole`, `RequiresSubscription`, `BacksUpItsOwnData`. Plus `PermitsOsPush(DevicePlatform)`, a **method** precisely so it stays outside the reflected `bool` matrix (`:240-250`).

**The class invariant** (`:33-39`): every capability is derived from `Kind` and nothing else. `Common/DeploymentProfileCoverageTests.cs` additionally blocks any new `IsLocalMode` branch outside its `AllowedFiles`.

### 5.3 Adding a `verify-schema` check

Logic in `Application/Common/Maintenance/SchemaVerificationService.cs` (**not DI-registered**, `:49-51`); both-sides reader in `Infrastructure/Persistence/SchemaVerificationReader.cs`. `RunAsync` (`:87-102`) calls `VerifyExtensions`, `VerifyBookingConstraint`, `VerifyIndexes`, `VerifyForeignKeys`, `VerifyDecimalPrecision`, `VerifyAuditLedger`, `VerifyDataMigrations`, `VerifySubscriptions`.

Shape of one check (`:314-318`, via the `Add(check, count, detail, ok)` helper at `:520-531`):
```csharp
Add("type-prefix-removed", counts.AppointmentsWithTypePrefixRemaining,
    n => n == 0 ? "0 appointment note(s) still start with a 'Type: ' prefix"
                : $"{n} appointment note(s) still carry a 'Type: ' prefix",
    n => n == 0);
```
`count is null` ⇒ `NotApplicableIn(scope, check, why)` → **Info, never Drift** (`:637-646`) — "a part not implemented is not a regression". Verb `VerifySchemaCommand`, exit **0** clean / **1** couldn't run / **2** drift; renders `[DRIFT]` / `[  ok ]` grouped by scope; best-effort saves the text beside `Backup:DefaultDestination`, which never changes the exit code.

Indexes are matched on **table + ordered columns, never on name**. A check that re-derives something should call the **real** production code, not re-express it in SQL — see `subscription-cover-kind-matches-ledger` calling `SubscriptionLedger.FoldWithSpans` (`:596`).

### 5.4 Migrations

Pattern for a schema+backfill migration: **every `AddColumn` / `CreateIndex` first, the raw `migrationBuilder.Sql` backfill last**, idempotent by construction, with a comment stating what rule it reproduces and what it deliberately does not. Canonical example `20260810223151_AddPlatformConsoleWrites.cs:33-84`, paired with `SchemaVerificationService.cs:586-609`. `20260810121221_LabOrderAppointmentLink` records that EF emitted a destructive statement first and it was **reordered below the additive ones**.

⚠️ Scaffolding hazards (`LEARNINGS`, `CLAUDE.md:94-97`, `features/adoption-gaps-remediation/stories/story-1:485-487`): use `-p:BaseOutputPath=<temp>` (a running dev API holds `api/**/bin`), **never** `--no-build`, and **commit the model snapshot with each migration** — an uncommitted snapshot makes the next `migrations add` re-emit the previous migration's changes. In PowerShell, never end a `BaseOutputPath` argument with a backslash inside double quotes (the trailing `\"` escapes the quote and MSBuild silently builds to `bin/` reporting success). ⚠️ Several migrations in the tree were **hand-authored** because `migrations add` emitted an empty file while the API was running.

Startup application is inside a **session-level PostgreSQL advisory lock**, `MigrationLock.LockKey = 5_314_072_026_000_001` (`Program.cs:844-884`; `Startup/MigrationLock.cs:36-89`) — `pg_advisory_lock`, never the `xact` variant, with the command timeout set to infinite for the duration.

⚠️ `AddConcurrencyToken` (`20260727195934`) has a **deliberately empty `Up()` and `Down()`** — EF emits 38 × `AddColumn<uint>("xmin")`, which PostgreSQL rejects outright. It is kept for its **model snapshot** only.

### 5.5 Result codes and French wording

`Application/Common/Models/Result.cs` — `Result.Failure(error, code?)`, `Result<T>.FailureFrom(failure)` (`:56-57`) which re-wraps **preserving `Code`** (a hand-written `Failure(inner.Error!)` drops it). Policy at `:13-21`: *"Do not add a code unless a caller genuinely branches on it — an unused code is a contract nobody is honouring."*

`API/Controllers/ApiControllerBase.HandleFailure` (`:27-41`) emits `{ error }`, plus `code` only when non-blank. **The status code is chosen by the action, never derived from `Code`.** `ExceptionMiddleware` maps exception → status and **never emits a `code`**.

Naming: lower `snake_case`, aggregate-or-subsystem prefix then state — `subscription_required`, `archive_invalid`, `clinic_not_found`, `period_already_cancelled`, `patient_duplicate`, `slot_taken`.

French conventions: the sentence and the code live in the **same file** ("three copies is how a reworded message silently stops matching the code" — `SubscriptionRefusals.cs:9-12`, naming the deleted `Contains("déjà facturée")` defect); **say what still works before what does not**; **name the route out**; name the document, never a bare id; guillemets `« … »` for screen names; dates `dd/MM/yyyy` invariant; money `0.000` with `.` → `,`. Generic fallback is the shared `ErrorMessages.Generic`, never a literal.

### 5.6 The audit actor

`AuditActor` (`Application/Common/Interfaces/IAuditActorProvider.cs:50-113`): `ProcessPrefix = "job|"`, `ConsolePrefix = "console|"`, `RestorePrefix = "restore|"`. `AsRestore()` **decorates** rather than replaces and is idempotent, so composition is `restore|console|{guid}`. Resolution order (`Common/Services/AuditActorProvider.cs:71-92`): **console session → JWT clinic user → declared process name → Unknown**, cached per scope on first read; `RunAs` is a no-op after the first read.

---

## § 6. Untested / undocumented gaps noticed in passing

Not scope, but worth knowing while working nearby:

- `PlatformSecretProtector`, `PlatformAccountProvisioning` and `PlatformAccountCommand` argument parsing have **no test files**.
- `SecurityHeadersMiddlewareTests` does not pin the CSP **string**, nor its byte-identity with `deploy/Caddyfile:63-68`, which the middleware's own docstring says "must be changed together".
- `SubscriptionVendorCommandReachabilityTests`' dispatch guard covers the five `Subscription*Command` types only; every other verb can be silently undispatched.
- `web/lib/auth/session-cookie.ts:73-75` `clearMustChangeCookie` is exported with **no caller**.
- Ten migrations still carry the scaffolded `xmin = table.Column<uint>(…)` line inside `CreateTable`; only `AddClinicSubscriptions` documents removing it.
- `api/ClinicManagement.API/appsettings.json` still carries two real non-secret values: `GoogleCalendar.ClientId` (`:113`) and `Auth0.Domain`/`Audience` (`:217-218`).
- `api/ClinicManagement.API/appsettings.Development.json` commits dev fixtures including `Console.SigningKey` (`:16`), `minioadmin/minioadmin` (`:20-21`) and a DB password (`:35`). Not loaded in Production.
