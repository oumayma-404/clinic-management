# Implementation Plan: Security Hardening (Audit Section 2)

**Status:** APPROVED
**Challenged:** Yes
**Created:** 2026-07-27
**Spec:** [spec.md](./spec.md) — APPROVED, Challenged: Yes
**Structure:** **One story**, by explicit user decision. See [Story shape](#story-shape) and risk **R-1**.

## Overview

Implements all 14 findings from `CODEBASE_AUDIT_2026-07.md` § 2 plus US-14 (the backup-output exposure surfaced during `/challenge-spec`). Nothing here adds a user-visible capability. Success is that the clinic notices only that a secretary can no longer change the TVA rate, and that everyone signs in once on the upgrade.

Four decisions were settled in the planning interview and are load-bearing for everything below:

| Decision | Choice | Why |
|---|---|---|
| Part order | **Risk-first** — installer P0s first | Four P0s are live on already-shipped clinic PCs |
| Per-source lockout state | **`IMemoryCache`**, per-instance limit documented | 15-min transient window; no migration; durable per-account backstop already exists |
| Durable session credential | **Second JWT with a refresh audience** | Satisfies AC-5.12/5.13 for free — the cookie stays a decodable JWT, just not API-accepted |
| DPAPI credential encryption | **New API console verb** | Matches `provision-cert` / `reset-admin-password`; testable C# instead of untestable Inno Pascal |

Five further decisions came out of `/challenge-plan` (2026-07-27) and are equally load-bearing:

| Decision | Choice | Why |
|---|---|---|
| Client-IP resolution | **Forward XFF, trust loopback only, keep `IsLoopback` on the raw peer** | Without it both halves of US-4 are defeated — see **[The client-IP chain](#the-client-ip-chain-read-before-implementing-p3)** |
| ACL implementation | **One C# class behind a `harden-permissions` console verb** | The `.iss` and the backup service must not carry two copies of the same policy |
| CSP delivery | **Kestrel owns it in Local; `next.config.ts` only in Cloud** | Two CSP headers make the browser enforce the intersection, not what was tested |
| Coverage gaps | **AC-1.7, AC-2.6, AC-4.5, AC-11.7 added** | Four acceptance criteria had no plan step |
| XFF propagation | **Read the inbound header, never Next's own `request.ip`** | The natural implementation silently reproduces the bug it fixes |

---

## The client-IP chain (read before implementing P3)

US-4 is keyed on the client address, and **in Local mode nothing in the request path sees the browser's address by default.** Verified: `web/app/bff/auth/local-login/route.ts` calls `fetch(${API_INTERNAL_URL}/auth/login)` server-side over loopback forwarding no client headers, and the API has **no** ForwardedHeaders middleware anywhere. Left alone, `RemoteIpAddress` is `127.0.0.1` for every login placed through the UI — which does not merely weaken US-4, it **inverts** it: the per-IP limiter would bucket the entire clinic as one source, so staff signing in at 08:00 share one budget and the limiter *becomes* the clinic-wide DoS it exists to prevent, while AC-4.2's per-source lockout collapses back to per-account.

The chain has **two** proxy hops:

```
browser 192.168.1.42
  → Kestrel / YARP        adds X-Forwarded-For: 192.168.1.42
  → Next BFF local-login  request.ip = 127.0.0.1   ✗ never use this
                          read the inbound XFF     ✓ forward it onward
  → API (loopback)        ForwardedHeaders, KnownProxies = loopback only
                          resolved client IP = 192.168.1.42  ✓
```

Three rules, all mandatory:

1. **The BFF propagates the inbound `X-Forwarded-For`** that YARP set. It must **not** derive an address from its own request — that is the loopback Kestrel hop, and using it silently reproduces the original bug with a limiter that *looks* correct.
2. **The API trusts forwarded headers from loopback only** (`KnownProxies`), so a LAN client's own `X-Forwarded-For` is discarded. A direct LAN hit on `/api/auth/login` keeps its real peer address, so both entry paths resolve correctly.
3. **`LocalRequest.IsLoopback` keeps reading the raw peer address, never the forwarded one.** It gates the first-run `setup` endpoint *and* `/hangfire`; resolving it from a forwarded header would let a LAN client spoof `127.0.0.1` through both, turning this hardening change into privilege escalation. While in the file, also fix its `null ⇒ true` fail-open default — per the LEARNINGS pitfall *"Default security gates to deny on missing/ambiguous input."*

Because a wrong implementation fails **silently and plausibly**, it must be pinned: a test asserting two distinct browser addresses land in two distinct buckets, a test that a missing or garbage XFF does not collapse everyone into one shared bucket, and a log of the resolved client IP on the auth path so the posture is observable in the field. This is the same trap LEARNINGS already records against this very file — *"A reverse-proxy/loopback hop makes request-scheme-derived security decisions on the internal leg."*

## Story shape

The user explicitly chose **one story**. This plan honors that and does not re-propose a split. To keep it implementable, the story is structured into **five ordered, dependency-respecting parts**. Each part is a *vertical* increment that ends in a working, committable state — not a technical-layer grouping. `/implement-story` should land and commit part by part; a part boundary is the natural split point if the story proves too large in one session (risk **R-1**).

| Part | Spec stories | Findings | Verifiable by | Depends on |
|---|---|---|---|---|
| **P1** Installer filesystem posture | US-1, US-2, US-3 | 🔴×4 | Operator only (R-1 of `packaging/`) | — |
| **P2** Backup output posture | US-14 | 🔴 | `dotnet test` + operator | P1 (reuses its ACL helper) |
| **P3** Auth & session | US-4, US-5 | 🔴 + 🟠 | `dotnet test` + manual | — |
| **P4** Authorization | US-6, US-7, US-8, US-9 | 🟠×3 + 🟡 | `dotnet test` | — |
| **P5** Hygiene | US-10, US-11, US-12, US-13 | 🟠 + 🟡×3 | `dotnet test` + manual page walk | — |

P3, P4 and P5 are mutually independent; only P2's dependency on P1 is real (it reuses the ACL helper P1 introduces).

---

## Files to Modify / Create

### P1 — Installer filesystem posture

| File | Change |
|---|---|
| `packaging/server/clinic-server.iss` | `[Dirs]`: drop the add-only `Permissions: service-modify` reliance. `[Code]`: **no `icacls` logic in Pascal** — call the new `harden-permissions` verb with **checked exit codes**; revoke the `initdb` grant immediately after `initdb` succeeds inside `SetupPostgres` **and on the failure path** (AC-1.7); `EstablishDbCredentials` gains the protect/read verb calls; `CurStepChanged` gains a `HardenPermissions` step after `SetupPostgres` returns true |
| `api/ClinicManagement.Infrastructure/Security/DirectoryAclHardener.cs` | **New.** The **single** implementation of the ACL policy (break inheritance; grant `LocalSystem`/`NetworkService`/`Administrators` only). Used by the console verb *and* by P2's backup service — this is what makes P1→P2 a real dependency rather than a nominal one |
| `api/ClinicManagement.API/Maintenance/HardenPermissionsCommand.cs` | **New.** `harden-permissions <paths…>` console verb over `DirectoryAclHardener` |
| `api/ClinicManagement.API/Maintenance/CredentialProtectionCommand.cs` | **New.** `protect-credentials` / `read-credentials` console verbs (Local-only, direct-file, exit code), mirroring `ProvisionCertCommand` |
| `api/ClinicManagement.API/Program.cs` | Intercept the **three** new verbs alongside `reset-admin-password` / `provision-cert`, before the web host boots |
| `api/ClinicManagement.Infrastructure/Security/DbCredentialProtector.cs` | **New.** Machine-scoped DPAPI protect/unprotect of the credentials payload + plaintext→ciphertext migration. Testable core |
| `packaging/README.md` | New « Permissions » verification steps; the AC-3.5 residual on `appsettings.Production.json`; the AC-2.11 « Mise hors service » procedure |
| `packaging/CLAUDE.md` | Update the key-facts list for the new ACL posture and the two new console verbs |

### P2 — Backup output posture

| File | Change |
|---|---|
| `api/ClinicManagement.Infrastructure/Services/PgDumpBackupService.cs` | Call **P1's `DirectoryAclHardener`** on the `clinic-backup-<ts>` folder **before** writing into it; detect non-fixed drives → warning; ACL failure on a fixed drive → fail loud + delete the partial folder (extends the existing partial-cleanup path) |
| `api/ClinicManagement.Application/DTOs/BackupResultDto.cs` | Add the optional French `warning` field |
| `api/ClinicManagement.Application/Features/Backup/Commands/BackupNowCommand.cs` | Pass the warning through |
| `web/components/clinic-settings.tsx` | Surface the backup warning in the UI, not just the log |

### P3 — Auth & session

| File | Change |
|---|---|
| `api/ClinicManagement.API/Program.cs` | `AddRateLimiter` + `UseRateLimiter`; per-IP policy on the anonymous auth endpoints; global per-user policy; **exemptions** for `/api/connectivity`, the Google callback and `/hub/*`; 429 body shaped as `{ error }` with `Retry-After`. Also `UseForwardedHeaders` with `KnownProxies` **restricted to loopback** — see [The client-IP chain](#the-client-ip-chain-read-before-implementing-p3) |
| `api/ClinicManagement.Infrastructure/LocalRequest.cs` | `IsLoopback` must keep reading the **raw peer address**, never a forwarded one (it gates `setup` + `/hangfire`); fix its `null ⇒ true` fail-open default |
| `web/app/bff/auth/local-login/route.ts` | Propagate the **inbound** `X-Forwarded-For` to the API. Must not use `request.ip` — that is the loopback Kestrel hop |
| `web/lib/errors.ts` (+ `client.ts`) | 429 → a distinct actionable French message carrying the retry delay, not the generic failure toast (AC-4.5) |
| `api/ClinicManagement.Domain/Entities/User.cs` | Token version field; bumped in `SetPassword` and `Deactivate`; **not** in `UpgradePasswordHash` |
| `api/ClinicManagement.Infrastructure/Migrations/` | **New migration** for the version column |
| `api/ClinicManagement.Infrastructure/Auth/LocalAuthService.cs` | Stamp the version claim; issue the two token kinds (access ~30 min at the API audience, refresh at `clinic-refresh`) |
| `api/ClinicManagement.Infrastructure/Auth/LocalAuthConfig.cs` | Refresh audience + the two lifetimes, through the existing single resolution path |
| `api/ClinicManagement.API/Middleware/LocalAuthEnforcementMiddleware.cs` | Compare the version claim on the account it already loads (no new query); reject **versionless** tokens (AC-5.15) |
| `api/ClinicManagement.API/Controllers/AuthController.cs` | Refresh-exchange endpoint; login returns both tokens |
| `api/ClinicManagement.Application/Features/Auth/Commands/LoginCommand.cs` | Per-(account, source) attempt counting via a new abstraction; preserve lockout-before-password, the generic `InvalidCredentialsError`, rehash-on-login and inactive-only-after-correct-password |
| `api/ClinicManagement.Application/Common/Interfaces/ILoginAttemptTracker.cs` + Infrastructure impl | **New.** `IMemoryCache`-backed, keyed `(userId, clientIp)`, 15-min sliding TTL |
| `web/app/bff/auth/local-login/route.ts` | Store the **refresh** JWT in `local_session`; never store the access token |
| `web/app/bff/auth/token/route.ts` | Exchange the cookie's refresh token for an access token instead of returning the cookie verbatim |
| `web/app/bff/auth/session/route.ts` | Keep decoding the cookie for `{name,email,role}` — works unchanged because the refresh token is still a JWT (AC-5.12) |
| `web/lib/api/client.ts` | Cache the access token in memory; on 401, renew **once** and retry the failed request (AC-5.7) |
| `web/lib/hooks/use-auth-token.ts` | Stop holding a token for the component lifetime; renew on expiry |
| `web/lib/realtime/clinic-hub.ts` | `accessTokenFactory` renews; reconnect on expiry (AC-5.8) |
| `web/lib/api/{billing,clinics,doctors,invoices,patient-files,treatment-plans,medical-documents}.ts` | Seven modules each hold a private copy of the `/bff/auth/token` fetch — route them through the shared `client.ts` helper so renewal exists in one place (see **R-4**) |
| `packaging/README.md` | The clinic-wide re-login on upgrade (AC-5.16) |

### P4 — Authorization

| File | Change |
|---|---|
| `api/ClinicManagement.Application/Features/Clinics/Commands/UpdateClinicCommand.cs` | Extend the existing TTN desired-vs-current gate to matricule fiscal, TVA applicable/rate, timbre enabled/amount; refuse **before** the logo upload |
| `api/ClinicManagement.API/Controllers/ClinicsController.cs` | `AdminOnly` on `PUT doctors` |
| `api/ClinicManagement.API/Controllers/ProcedureTypesController.cs` | `AdminOnly` on POST / PUT / DELETE / `initialize-defaults`; reads untouched |
| `api/ClinicManagement.API/Controllers/RecallController.cs` | `AdminOnly` on `PUT settings`; list + per-patient actions untouched |
| `api/ClinicManagement.Application/Features/Doctors/Commands/SetDoctorWorkingHoursCommand.cs` | Own-or-admin, copied from `UpdateDoctorProfileCommand:93`; check before mutation |
| 10 catalog command files (US-9 table) | DB-resolved clinic + `ClinicId` verification: `Update`/`Deactivate`/`Confirm` × DentalActs, CnamNomenclature (incl. `UpdateCnamLetterValueCommand`), Medications |
| `web/components/{clinic-settings,procedure-types,recalls}` + `dashboard-sidebar.tsx` | Hide/disable the newly admin-only controls for non-admins (AC-7.4) |

### P5 — Hygiene

| File | Change |
|---|---|
| `api/ClinicManagement.Infrastructure/Extensions.cs` | MinIO: treat empty **and** known-default as not configured → fail loud outside Development; warn once in Development (AC-10.5) |
| `api/ClinicManagement.API/appsettings.json` | MinIO keys → empty + `// SECRET` |
| `api/ClinicManagement.Application/Features/Files/Commands/UploadPatientFileCommand.cs` | Allow-list + magic bytes + 25 MB cap + empty check, **before** the blob write; normalize `image/jpg` |
| `api/ClinicManagement.Application/Common/FileContentValidation.cs` | **New.** Shared allow-list + signature check, extracted from the cachet logic in `UpdateDoctorProfileCommand` so both paths share one implementation |
| `api/ClinicManagement.API/Controllers/PatientFilesController.cs` | `nosniff` + `Content-Disposition: attachment` on download |
| `api/ClinicManagement.API/Middleware/SecurityHeadersMiddleware.cs` | **New.** Placed **before** `MapReverseProxy` so it covers proxied Next pages too (AC-12.5) |
| `api/ClinicManagement.API/Controllers/DoctorsController.cs` | Remove the now-redundant inline `nosniff` (AC-12.9) |
| `web/next.config.ts` | `headers()` for the Cloud page responses (AC-12.6), **conditioned on `AUTH_MODE`** so the Local build emits none — Kestrel owns the CSP there. Two CSP headers would make the browser enforce the intersection rather than the tested policy |
| `web/components/patient-files-*.tsx` | State « PDF, PNG, JPEG · 25 Mo max » **before** the user picks a file, and reject an oversize selection client-side (AC-11.7) |
| 6 handler files (US-13) | French messages + `LogError`; sweep for other `{ex.Message}` leaks |

### Tests

| File | Covers |
|---|---|
| `UnitTests/Infrastructure/Security/DbCredentialProtectorTests.cs` | Round-trip, plaintext migration, undecryptable → operator error |
| `UnitTests/Infrastructure/Services/PgDumpBackupServiceAclTests.cs` | Harden-before-write, fail-loud + partial cleanup, removable-drive warning |
| `UnitTests/Features/Auth/LoginAttemptTrackerTests.cs` | Per-source isolation, per-account backstop, TTL expiry |
| `UnitTests/Features/Auth/TokenVersionTests.cs` | Four bump events; **`UpgradePasswordHash` does not bump**; versionless rejected |
| `UnitTests/Api/RateLimitingTests.cs` | 429 + `Retry-After`; connectivity / callback / hub exempt |
| `UnitTests/Api/ClientIpResolutionTests.cs` | **New, and the one that matters most in P3.** Two distinct browser addresses → two distinct buckets; a LAN client's own `X-Forwarded-For` is discarded; missing/garbage XFF does not collapse everyone into one bucket; `IsLoopback` still rejects a spoofed `X-Forwarded-For: 127.0.0.1` on `setup` and `/hangfire` |
| `UnitTests/Infrastructure/Security/DirectoryAclHardenerTests.cs` | The single ACL policy: inheritance broken, no `Users`/`Everyone`, service retains write, failure surfaces rather than fails open |
| `UnitTests/Api/ProcedureTypesControllerAuthorizationTests.cs`, `ClinicsControllerAuthorizationTests.cs`, `RecallControllerAuthorizationTests.cs` | New per-controller policy tests, modelled on `CnamControllerAuthorizationTests` |
| `UnitTests/Api/ControllerAuthorizationCoverageTests.cs` | **Extend** with the AC-7.5 role-policy coverage guard — exact-set assertion, per the LEARNINGS pattern |
| `UnitTests/Features/{DentalActs,CnamNomenclature,Medications}/*TenantIsolationTests.cs` | **New.** All 10 commands, **query filter inactive** |
| `UnitTests/Features/Clinics/UpdateClinicBillingGateTests.cs` | Per-field gate, nothing persisted, unchanged-value not a change |
| `UnitTests/Features/Doctors/SetDoctorWorkingHoursAuthorizationTests.cs` | Own / admin / other / cross-clinic |
| `UnitTests/Common/FileContentValidationTests.cs` | Each type, magic mismatch, cap, empty |
| `UnitTests/Api/ErrorMessageLeakGuardTests.cs` | No client-facing failure carries raw exception text |
| `UnitTests/Infrastructure/MinioConfigurationTests.cs` | Empty / default / real × Development / Production / Local |

---

## Implementation Story

### US-1: Close every finding in audit § 2

As a **clinic owner**, I want the security findings from the July 2026 audit closed, so that patient records, the JWT signing key and the clinic's legal billing settings are not exposed to anyone who can reach the clinic PC or the clinic network.

Delivered in five ordered parts. Each ends committable.

#### Part 1 — Installer filesystem posture (US-1, US-2, US-3 · 🔴×4)

1. Build `DirectoryAclHardener` (Infrastructure) — the **one** implementation of the policy: `/inheritance:r` then explicit grants for `LocalSystem`, `NetworkService`, `Administrators`. Expose it as the `harden-permissions` console verb. The `.iss` calls the verb and **checks its exit code**, aborting with a French message on failure — per the LEARNINGS pitfall *"An orchestration script must check every external step's exit code and abort loudly"*, written about this exact file. No ACL logic lives in Pascal.
2. Inside `SetupPostgres`, immediately after `initdb` returns 0, revoke the `*S-1-5-32-545` grant recursively — **and on the failure path too**, so an aborted install never leaves `pgdata` world-readable while the operator believes it simply failed cleanly (AC-1.7). The grant stays scoped to the step that genuinely needs it (EC-1).
3. Harden `{app}\api\.local`, `{app}\api\Files`, `{app}\api\logs` and `{app}\pgdata` from a new `HardenPermissions` step in `CurStepChanged`, after `SetupPostgres` succeeds. Verify service write access afterwards and abort if it is lost (EC-2).
4. Delete or relocate `{app}\initdb.log` on success (AC-2.8).
5. Build `DbCredentialProtector` (Infrastructure) + the `protect-credentials` / `read-credentials` verbs (API/Maintenance), intercepted in `Program.cs` alongside the existing two. Wire `EstablishDbCredentials` to them, including the plaintext→ciphertext migration on upgrade.
6. Update `packaging/README.md` (permissions checks, the AC-3.5 residual, « Mise hors service ») and `packaging/CLAUDE.md`.

**Done when:** a fresh install and an upgrade-over-previous both pass the operator checklist — `icacls` shows no `Users`/`Everyone` on all four paths, a non-admin is denied on `signing-key` / a blob / a log, `db-credentials` is ciphertext, and PostgreSQL starts, the API connects, upload and login work. Plus: a file uploaded and a log written **after** install inherit the tightened ACL (AC-2.6), and a deliberately failed install leaves no `pgdata` grant behind (AC-1.7).

#### Part 2 — Backup output posture (US-14 · 🔴)

1. Call Part 1's `DirectoryAclHardener` directly — no second implementation. This is the whole of P2's dependency on P1.
2. In `PgDumpBackupService`, harden the timestamped folder **immediately after `Directory.CreateDirectory`, before the dump** (AC-14.2).
3. Detect a non-fixed destination drive → proceed and return the French warning (AC-14.3). ACL failure on a fixed drive → fail loud and delete the partial folder, extending the existing cleanup path (AC-14.4).
4. Thread the warning through `BackupResultDto` → `BackupNowCommand` → `clinic-settings.tsx` so it is visible, not just logged.

**Done when:** a backup to a local folder shows no `Users`/`Everyone`; a backup to a USB drive completes with the warning on screen; a forced ACL failure leaves no partial folder.

#### Part 3 — Auth & session (US-4, US-5 · 🔴 + 🟠)

0. **Client IP first.** Implement [The client-IP chain](#the-client-ip-chain-read-before-implementing-p3) *before* the limiter — XFF propagation in the BFF, loopback-only `ForwardedHeaders` in the API, `IsLoopback` left on the raw peer. Building the limiter first means building it against an address that is always `127.0.0.1`, and it will look like it works.
1. **Rate limiting.** `AddRateLimiter`/`UseRateLimiter` in `Program.cs`: per-IP sliding window on the anonymous auth endpoints, generous global per-user policy elsewhere, and explicit exemptions for `/api/connectivity` (polled every 15 s per tab — EC-5), the Google OAuth callback and `/hub/*`. 429 renders `{ error }` with `Retry-After`, surfaced in the UI as a distinct French message with the retry delay (AC-4.5). Limits config-driven with secure defaults (AC-4.6).
2. **Per-source lockout.** `ILoginAttemptTracker` + `IMemoryCache` impl keyed `(userId, clientIp)`. `LoginCommand` counts per source; `User.FailedLoginAttempts` stays as the durable per-account backstop at a higher threshold. Preserve every property the audit marked verified-clean: lockout-before-password, generic `InvalidCredentialsError`, rehash-on-login, inactive disclosed only after a correct password.
3. **Token version.** Field on `User` + migration. Bump in `SetPassword` (the single choke point all four password paths already funnel through) and `Deactivate`. **Do not** bump in `UpgradePasswordHash` — that is the AC-5.11 trap, and a test pins it.
4. **Two token kinds.** `LocalAuthService` issues an access JWT (~30 min, API audience, version claim) and a refresh JWT (`clinic-refresh` audience, longer lifetime). The API's bearer validation rejects the refresh audience, so a stolen cookie cannot call the API (AC-5.5).
5. **Enforcement.** `LocalAuthEnforcementMiddleware` compares the version against the account it already loads — no new query — and rejects versionless tokens (AC-5.15).
6. **Frontend.** `local-login` stores the refresh JWT; `/bff/auth/token` exchanges it for an access token; `/bff/auth/session` and `middleware.ts` keep working unchanged because the cookie is still a decodable JWT (AC-5.12/5.13). `client.ts` renews once on 401 and retries. Consolidate the seven per-module token fetches onto the shared helper (**R-4**).
7. Document the one-time clinic-wide re-login in `packaging/README.md`.

**Done when:** two browser addresses land in two distinct rate-limit buckets (the proof the client-IP chain works); a hostile source locks only itself out while a colleague signs in; a password change kills an existing token immediately; a login that upgrades a hash does **not**; a page open for an hour submits successfully with no visible interruption; the hub still live-refreshes; `setup` and `/hangfire` still reject a LAN client sending `X-Forwarded-For: 127.0.0.1`.

#### Part 4 — Authorization (US-6, US-7, US-8, US-9 · 🟠×3 + 🟡)

1. Extend `UpdateClinicCommand`'s existing desired-vs-current gate to the five legal/billing fields; refuse before the logo upload; unchanged values are not a change (EC-11).
2. `AdminOnly` on `PUT /api/clinics/doctors`, the four procedure-type writes, and `PUT /api/patients/recalls/settings`.
3. Own-or-admin in `SetDoctorWorkingHoursCommand`, copying `UpdateDoctorProfileCommand:93`; cross-clinic still reads as not-found.
4. Add the DB-resolved clinic check to all **10** catalog commands in the US-9 table.
5. Hide/disable the newly admin-only controls for non-admins.
6. Extend `ControllerAuthorizationCoverageTests` with the role-policy coverage guard as an **exact-set** assertion, so both additions and removals trip it.

**Done when:** a secretary gets 403 on all six endpoints and cannot change any legal billing field; all 10 catalog commands refuse a cross-clinic id **with the query filter inactive**; the guard test fails on an unclassified new action.

#### Part 5 — Hygiene (US-10, US-11, US-12, US-13 · 🟠 + 🟡×3)

1. MinIO: scrub `appsettings.json` to empty + `// SECRET`; fail loud outside Development on empty or known-default; warn once in Development. Gate on `IHostEnvironment.IsDevelopment()` — and per the LEARNINGS pitfall *"Gate mode-invariant guards on the mode flag, not a capability flag"*, gate on the environment itself, never on "does a credential happen to look real".
2. Extract `FileContentValidation` from the cachet logic; apply it in `UploadPatientFileCommand` **before** the blob write; add `nosniff` + `attachment` on download; pre-existing out-of-allow-list files still download (EC-13).
3. `SecurityHeadersMiddleware` before `MapReverseProxy`, and **skip the CSP if the header is already present** so a duplicate can never silently narrow the policy. Condition `next.config.ts`'s `headers()` on `AUTH_MODE` so Local emits exactly one CSP (Kestrel's) and Cloud exactly one per response (Next's for pages, Kestrel's for `/api/*`). CSP **report-only** first; page walk against a **production** build (EC-14) including the three named traps — `@vercel/analytics`, `blob:` exports, the inline PDF; then flip to enforcing. HSTS in Cloud; Local config-gated, default off (AC-12.7).
4. Replace the six raw-exception messages with French text + `LogError`; sweep for others; add the leak guard test.

**Done when:** a Production start with `minioadmin` fails loud while `docker compose up` + `dotnet run` still works on a fresh clone; a renamed `.exe` declared as PDF is refused with no blob written; every page is clean under the enforcing CSP; no client-facing failure carries exception text.

---

## Testing Strategy

Follows the spec's Verification & Tests table. Per part:

- **P1** — unit tests for `DbCredentialProtector`; everything else is the `packaging/README.md` operator checklist (R-1: not CI-runnable). Both the fresh-install **and** upgrade-over-previous paths must be walked; a non-admin local account is required to verify denial.
- **P2** — unit tests with a faked filesystem/ACL seam for ordering, fail-loud and cleanup; one operator `icacls` check on a real backup.
- **P3** — unit + integration for the limiter, tracker and version semantics; manual verification for silent renewal, the 401-retry, and hub reconnection.
- **P4** — pure unit tests, plus the extended reflection guard. The tenant-isolation tests **must** run with the query filter inactive; a test that passes only with it active proves nothing (AC-9.3).
- **P5** — unit tests for validation and MinIO config; manual page walk for the CSP.

Existing suites that must stay green: `ControllerAuthorizationCoverageTests`, `AuthorizationPoliciesTests`, all `*TenantIsolationTests`, `TreatmentPlansControllerAuthorizationTests`.

> **Environmental note:** Smart App Control is ON on this machine and `dotnet test` fails at assembly load (`0x800711C7`) on freshly-built DLLs. That is environmental, not a defect. Tests must still be written; if the runner cannot execute locally, say so explicitly rather than reporting a pass.

## Risk Register

| ID | Risk | Likelihood | Impact | Part | Mitigation |
|----|------|:---:|:---:|---|---|
| **R-1** | **One story is oversized.** 14 spec stories, ~45 files, 5 subsystems (Inno Pascal, ASP.NET middleware, EF migration, MediatR handlers, Next.js) in a single story — chosen deliberately by the user. Risk is context exhaustion mid-story leaving a half-applied security change. | High | Med | all | Parts are ordered and each ends committable. **Split at a part boundary** if a session runs long — commit the completed parts and resume at the next. Never leave a part half-applied. |
| **R-2** | **Installer changes are not CI-verifiable** (`packaging/` R-1). A wrong ACL bricks a clinic's install or, worse, silently leaves it exposed while reporting success. | Med | High | P1 | Every `icacls` exit code checked, abort loud on failure (never fail open). Verify service write access after tightening. Walk both fresh-install and upgrade paths on a real Windows box before shipping. |
| **R-3** | **DPAPI makes an existing cluster unrecoverable.** A machine rebuild destroys the DPAPI key while `pgdata` survives → the credentials file cannot be decrypted and the DB is unreachable. | Low | High | P1 | Exactly the EC-4 path: report the existing French guidance (restore from backup, or deliberately delete `pgdata`) and abort. Never regenerate passwords against a live cluster. Document that the credentials file must be backed up. |
| **R-4** | **Token renewal has seven bypasses.** `billing.ts`, `clinics.ts`, `doctors.ts`, `invoices.ts`, `patient-files.ts`, `treatment-plans.ts` and `medical-documents.ts` each hold a private copy of the `/bff/auth/token` fetch. Miss one and it silently keeps using an expired token — a 401 the user sees as a random failure. | High | Med | P3 | Consolidate all seven onto the shared `client.ts` helper as part of P3, not as follow-up. LEARNINGS already flags this shape: *"Route every HTTP call through one client wrapper."* Grep for `bff/auth/token` and assert one call site remains. |
| **R-5** | **CSP breaks a page only in production.** Dev-server behaviour differs from a production build; a missed inline style or the hydration payload shows the clinic a blank screen. | Med | Med | P5 | Report-only first (AC-12.2), full page walk against a **production** build (EC-14), the three named traps checked deliberately, only then flip to enforcing. |
| **R-11** | **The client-IP chain is implemented wrong and fails silently.** Using Next's `request.ip`, or omitting `ForwardedHeaders`, yields a limiter that looks correct while bucketing the whole clinic as loopback — inverting US-4 into the DoS it was meant to prevent. Two proxy hops make this the single easiest thing to get wrong in P3. | High | High | P3 | Implement the chain as step 0, before the limiter. `ClientIpResolutionTests` asserts two addresses → two buckets. Log the resolved client IP on the auth path so the posture is observable rather than assumed. |
| **R-12** | **The forwarded-header fix becomes privilege escalation.** `LocalRequest.IsLoopback` gates `setup` and `/hangfire`; if it resolves from a forwarded header, a LAN client spoofs `127.0.0.1` and walks through both. | Med | High | P3 | `IsLoopback` reads the raw peer address only — never the forwarded one. `KnownProxies` restricted to loopback so a LAN client's header is discarded. Both pinned by `ClientIpResolutionTests`. |
| **R-13** | **Duplicate CSP headers in Local** (Next's + Kestrel's) make the browser enforce the intersection, so a page passes the tested policy and still breaks. | Med | Med | P5 | `next.config.ts` `headers()` conditioned on `AUTH_MODE`; the middleware skips the CSP when one is already present. Page walk verifies exactly one CSP header per response in both modes. |
| **R-6** | **Rate limiter 429s legitimate traffic.** The anonymous connectivity poll (15 s × tabs × clients) or a whole clinic signing in at 08:00 behind one NAT address trips the limit — the app looks offline or nobody can log in. | Med | High | P3 | Explicit exemptions for `/api/connectivity`, the OAuth callback and `/hub/*`. Limits config-driven (AC-4.6). Test the burst case (EC-6) before shipping. |
| **R-7** | **Version bump in the wrong place logs everyone out on login.** `UpgradePasswordHash` sits immediately beside `SetPassword`; bumping there invalidates the token the login just issued. | Med | High | P3 | AC-5.11 plus a dedicated test. Called out at the top of the P3 checklist. |
| **R-8** | **MinIO fail-loud breaks developer onboarding.** Already caught once during `/challenge-spec`; a strict re-reading during implementation could reintroduce it. | Med | Med | P5 | AC-10.5 is an acceptance criterion, not a nicety. Smoke check: fresh clone → `docker compose up` → `dotnet run` must work with no extra setup. |
| **R-9** | **Local-mode changes regress Cloud, or vice versa.** Five of the parts touch mode-gated code paths. | Med | Med | P3, P5 | Gate on the **mode flag**, never a capability flag (LEARNINGS). Keep `ControllerAuthorizationCoverageTests` green. Test the MinIO and HSTS matrices across both modes. |
| **R-10** | **Cloud multi-instance weakens the per-source counter.** `IMemoryCache` is per-instance, so N instances give an attacker N× the per-source budget. | Low | Low | P3 | Accepted and documented. Irrelevant for Local (single server). The durable per-account backstop (AC-4.3) still bounds the total. |

## Breaking Changes

1. **Every user signs in once after the upgrade** (AC-5.15). Versionless tokens are rejected. Documented in `packaging/README.md`.
2. **A secretary loses four abilities**: changing legal/billing clinic settings, rewriting the practitioner roster, writing the procedure-type catalog, and changing the clinic recall interval. Intended; UI updated so the controls are not merely failing.
3. **A doctor can no longer change another practitioner's working hours.** Intended.
4. **Patient-file uploads reject formats accepted before** (anything outside PDF/PNG/JPEG, or over 25 MB). Existing stored files still download (AC-11.9).
5. **A Cloud/Production deployment with empty or default MinIO credentials now fails to start** instead of running on defaults. Deliberate; requires the operator to set real credentials, and rotation of any deployment that ran on `minioadmin` (AC-10.6).
6. **`/bff/auth/token` changes semantics** — it exchanges rather than echoes. Any caller not routed through `client.ts` must be migrated (R-4).

## Migrations

| Migration | Content | Reversible |
|---|---|---|
| Token version on `User` | One column, defaulted for existing rows | Yes — dropping the column reverts to versionless behaviour |

No other schema change. The per-source lockout counter is deliberately cache-only (no table). File storage, patient, appointment and billing schemas are untouched.

**Data migration outside EF:** existing installs carry a **plaintext** `.local\db-credentials`. P1 migrates it to ciphertext in place on upgrade, and the reinstall path must read both forms during the migration release (AC-3.3).
