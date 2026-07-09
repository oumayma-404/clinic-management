# Feature Review: windows-desktop-app — Phase 4 (LAN Hosting & Security Gates)

**Status:** COMPLETE
**Challenged:** Yes
**Date:** 2026-07-09
**Challenged Date:** 2026-07-09
**Parent Branch:** main
**Merge Base:** 9798b95d31f55ee07f2ad5e0af5550c4c2831022
**Reviewed Scope:** Phase 4 only — commits `93d683c` (feat: LAN hosting & security gates) + `5ea3d0e` (fix: review fixes), diffed as `0fd2054..HEAD`
**Files Reviewed:** 18 changed files (+700, −64) — 13 source/config + 5 test files
**Excluded:** `features/**` (pipeline docs), `**/packages.lock.json`, `**/*-lock.json` (generated). Phases 1–3 (archived under `phase-1/`, `phase-3/`) are out of scope.
**Review method:** 4 parallel agents adapted to this repo (MediatR + `Result<T>`, no ROP/Marten) — **Code Quality & Architecture**, **Security** (added: this is the release-gate/hardening phase), **Business Logic**, **Breaking Changes**. The default ROP agent was dropped (repo does not use `Extensions.ROP`); no Frontend agent (no `web/` changes this phase).
**Phase invariant under test:** Cloud mode must be byte-for-byte behaviorally unchanged; all Phase 4 behavior additive/gated to Local mode. HTTPS cert generation + client CA-trust import are Phase 5.

## Challenge Summary

| Metric | Count |
|--------|-------|
| Original findings | 11 |
| Confirmed | 8 |
| Confirmed (adjusted) | 2 |
| Dismissed (false positive) | 1 |
| Dismissed (pre-existing) | 0 |
| **Final findings** | 10 |

**Challenge notes:**
- **Finding 3** (was Major → **Minor**): the `MedicalDocumentsController` `[Authorize]` is a genuine Cloud surface change, but the first-party FE is unaffected (every caller sends the bearer — re-verified) and it *tightens* previously-anonymous PHI endpoints (a security improvement, FE-audited). The residual risk is only a hypothetical external anonymous consumer of PHI, which should not exist. Action is release-note documentation, not a code fix → Minor.
- **Finding 10 (orig. 11)** (advice corrected): the observation (verbose fully-qualified type names in `Program.cs`) is valid, but the original advice to "add `using ClinicManagement.Infrastructure;`" is wrong — `Program.cs:3` **already** imports it, so `CorsOrigins`/`LocalRequest` just need the qualifier dropped. Only `System.IO` and `System.Security.Cryptography.X509Certificates` are genuinely missing usings.
- **Dismissed — original Finding 9** (OAuth callback persistence changed in Cloud): false positive. Verified functionally equivalent in Cloud — reads fall back to `GoogleCalendar:RefreshToken` config when no `.local` file exists, and the Singleton cache gives read-after-write; the token still survives restart (now via the `.local` file instead of the appsettings rewrite). This is the intended, test-covered removal of the appsettings-rewrite security debt (`FileGoogleTokenStoreTests` cover config fallback + read-after-write), with no observable Cloud regression. Per the verify/confirm guideline, an accurate risk note about intended, test-covered design that exposes no defect is not an actionable finding.

## Findings

### Finding 1
- **Severity:** Major
- **Category:** Security
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/Controllers/GoogleCalendarController.cs
- **Line:** 228
- **Anchor:** `GoogleCalendarController.Callback([FromQuery] string? code, string? error, string? state)`
- **Comment:** Both `Authorize` (`[AllowAnonymous]`) and `Callback` (`[AllowAnonymous]`) are anonymous, and the `state` value generated in `Authorize` (line 196, with the comment "Optional: store in session for CSRF protection") is **never validated** in `Callback` — the parameter is accepted and ignored (verified: `state` is never referenced after the method signature). In Local/LAN mode this is a PHI-exfiltration / integration-hijack vector: any unauthenticated LAN user can hit `http://clinic-server:5000/api/googlecalendar/authorize`, complete Google consent with **their own** Google account, and the callback (`_tokenStore.SaveRefreshTokenAsync`, line 337) silently overwrites the clinic's single shared refresh token with the attacker's — after which App→Google sync pushes patient appointment data to an attacker-controlled calendar. The missing `state` check also leaves the flow open to OAuth CSRF. The callback must stay anonymous (Google redirects the browser here with no bearer), so the fix is **not** to require a bearer but to (a) generate `state` bound to an authenticated initiator/session and reject the callback on mismatch, and (b) gate `authorize` initiation behind an authenticated session. Partly pre-existing, but this diff (re)affirms the `[AllowAnonymous]` carve-outs, and allow-list correctness is exactly what this release-gate phase is meant to validate.

### Finding 2
- **Severity:** Major
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/Program.cs
- **Line:** 187
- **Anchor:** top-level statements — `httpsConfigured` / HTTPS bind block (`var httpsConfigured = ...` through `if (httpsConfigured) { ... } else { ... }`)
- **Comment:** `httpsConfigured = !string.IsNullOrWhiteSpace(httpsCertPath) && File.Exists(httpsCertPath)` collapses two very different operator intents into one silent outcome. Empty `Https:CertPath` → plain HTTP is the documented safe default (correct). But when an operator **sets** `Https:CertPath` and the file is missing (typo, wrong working dir, cert not yet provisioned), `httpsConfigured` silently becomes false: the API binds plain HTTP, `UseHttpsRedirection` is skipped, and nothing is logged. For a PHI-handling clinic app this is transport failing **open** — PHI travels unencrypted over the LAN while the operator believes TLS is active. There is also **no startup log line** stating the selected transport in either branch (HTTP vs HTTPS + ports). Fix: when `CertPath` is non-empty but `File.Exists` is false, emit a clear warning (or fail startup) rather than silently downgrading, and log the chosen transport on startup so the active posture is observable. (Contrast: a present-but-unreadable/bad-password cert throws from the `X509Certificate2` constructor and fails loud — the missing-file path should be at least as visible.)

### Finding 3
- **Severity:** Minor
- **Category:** Breaking Change
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** api/ClinicManagement.API/Controllers/MedicalDocumentsController.cs
- **Line:** 21
- **Anchor:** `MedicalDocumentsController` (new class-level `[Authorize]`)
- **Comment:** Genuine Cloud-mode behavior change against the "Cloud byte-for-byte unchanged" invariant. Before Phase 4 this controller had neither `[Authorize]` nor `[AllowAnonymous]`, and Cloud's `FallbackPolicy` is null — so in **Cloud** all its endpoints (GET list, GET/{id}, POST, PUT, DELETE, generate-pdf, generate-pdf-download) were effectively anonymous. The new class-level `[Authorize]` applies in **both** modes, so in Cloud these now return 401 without a valid Auth0 bearer. Impact: the first-party frontend is **not** broken — every caller in `web/lib/api/medical-documents.ts` goes through `client.ts` helpers (auto-attach Auth0 token) or, for `generatePdfForDownload`, a raw fetch that attaches the token; there is no `<a href>`/`window.open` navigation caller lacking a token. Tightening a PHI endpoint is defensible (and was FE-audited per the story review), but per the phase invariant it must be recorded as a **deliberate Cloud surface change**, not a no-op. Confirm no external/second-party Cloud consumer relied on anonymous access.
- **Challenge note:** Severity lowered Major → Minor — the change is intentional, FE-audited, documented in the code comment (lines 16–20) and the story review, and breaks **no** first-party caller; it is a net security improvement (tightens anonymous PHI access). The only residual "break" is a hypothetical external anonymous PHI consumer that should not exist. Action is release-note documentation, not a code fix.

### Finding 4
- **Severity:** Minor
- **Category:** Breaking Change
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/Program.cs
- **Line:** 189
- **Anchor:** top-level statements — `if (httpsConfigured)` Kestrel `ConfigureKestrel`/`ListenAnyIP` bind block
- **Comment:** The HTTPS cert-binding block (`ConfigureKestrel` + `ListenAnyIP` on `Hosting:HttpPort`/`HttpsPort`, plus `AddHttpsRedirection`) is gated **only** on `httpsConfigured`, not on `isLocalAuthMode`. If a Cloud deployment ever sets `Https:CertPath` to an existing file, Kestrel is re-bound with `ListenAnyIP` on the configured ports, **overriding `ASPNETCORE_URLS`** / the host's URL binding — which could break a containerized Cloud host expecting to bind e.g. `:8080` via `ASPNETCORE_URLS`. With the shipped default (`Https:CertPath = ""`) `httpsConfigured` is false, so existing Cloud/dev startup is unaffected and the invariant holds *by default*. Recommend gating this block on `isLocalAuthMode && httpsConfigured` (the feature is LAN/Local hosting) so a stray cert path can never override Cloud's host-driven bind. (The sibling `UseHttpsRedirection` guard at line ~229 is correct.)

### Finding 5
- **Severity:** Minor
- **Category:** Security
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/appsettings.json
- **Line:** 91
- **Anchor:** `"Https": { "CertPassword": ... }`
- **Comment:** A `Https:CertPassword` key was added to the committed, git-tracked `appsettings.json` (read at `Program.cs` ~186 and passed to `new X509Certificate2(httpsCertPath!, httpsCertPassword)` ~195). It is empty today, but the slot invites an operator to paste the PFX private-key password into a tracked file — reintroducing exactly the committed-secret anti-pattern this same phase eliminates for the Google refresh token (the whole `IGoogleTokenStore` / `.local/` rationale). A leaked PFX password compromises the server's TLS private key. Fix: source the cert password from the gitignored `.local/` store, an environment variable, or user-secrets (as done for the refresh token / signing key), and drop the key from committed appsettings — or at minimum document that it must only be set in an untracked override. (Context: `appsettings.json` already commits real Google/HuggingFace secrets — the known R-7 debt — so this is consistent with, not worse than, existing debt, but runs against Phase 4's own stated goal of moving secrets out of committed config.)

### Finding 6
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Services/FileGoogleTokenStore.cs
- **Line:** 64
- **Anchor:** `FileGoogleTokenStore.SaveRefreshTokenAsync`
- **Comment:** This is a documented-thread-safe Singleton, but the atomic write uses a **fixed shared** temp path (`_filePath + ".tmp"`) and the file I/O (`WriteAllTextAsync` + `File.Move`) runs entirely **outside `_lock`** (the lock guards only the cache update at lines 68–72). Two concurrent `SaveRefreshTokenAsync` calls would write to and move the same `.tmp` path, risking an `IOException` or interleaved writes. Concurrent saves are unlikely (only the OAuth callback calls this today), but for a Singleton claiming thread safety the temp name should be unique per write (append `Guid.NewGuid().ToString("N")`) so the stage-then-move stays truly atomic. The in-memory cache under `_lock` is otherwise correct.

### Finding 7
- **Severity:** Suggestion
- **Category:** Security
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/LocalRequest.cs
- **Line:** 19
- **Anchor:** `LocalRequest.IsLoopback(HttpContext)` — `if (remoteIp is null) return true;`
- **Comment:** This is a security gate (authorizes the first-run `setup` endpoint and the Hangfire dashboard in Local mode), yet it fails **open**: a null `RemoteIpAddress` returns `true` (treated as loopback). Under the current direct-Kestrel topology `RemoteIpAddress` is always populated for real network requests, so this is not presently exploitable, but a security decision that defaults to "allow" on missing information is fragile if the hosting model ever changes (e.g. a proxy/handler that leaves the peer IP unset). Consider returning `false` on null for the security-gate use, or documenting the invariant. (Confirmed safe against `X-Forwarded-For` spoofing: no `ForwardedHeaders` middleware is registered — re-verified via grep — so `RemoteIpAddress` reflects the true socket peer.) Note: this null→true behavior is a **verbatim** carry-over from the original `AuthController` check (preserved intentionally per R-8), so changing it is a deliberate decision, not a regression fix.

### Finding 8
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/Program.cs
- **Line:** 193
- **Anchor:** `httpsConfigured` branch — port fallback literals
- **Comment:** The fallback ports `?? 5000` and `?? 5001` duplicate the same literals declared under `Hosting:HttpPort`/`HttpsPort` in appsettings.json. Since config already supplies them, either drop the inline literals (let config be the single source) or hoist them to named constants so the two places can't drift.

### Finding 9
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/Controllers/AuthController.cs
- **Line:** 86
- **Anchor:** `AuthController.Setup` (loopback check)
- **Comment:** `ClinicManagement.Infrastructure.LocalRequest.IsLoopback(...)` is called with a fully-qualified inline namespace. The file imports `ClinicManagement.Infrastructure.Auth` (line 9) but **not** the root `ClinicManagement.Infrastructure` where `LocalRequest` lives — so add `using ClinicManagement.Infrastructure;` and call `LocalRequest.IsLoopback(...)` to match the file's other imports. (Verified: the root namespace is genuinely not yet imported in this file, unlike in `Program.cs`.)

### Finding 10
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — advice corrected)
- **File:** api/ClinicManagement.API/Program.cs
- **Line:** 168
- **Anchor:** Program.cs (CORS + HTTPS/Kestrel setup)
- **Comment:** Several new types are referenced by fully-qualified name inline and repeatedly: `ClinicManagement.Infrastructure.CorsOrigins` (line 168), `System.IO.File` (line 187), `System.Security.Cryptography.X509Certificates.X509Certificate2` (line 195), and `ClinicManagement.Infrastructure.LocalRequest` (line 322, in `HangfireAuthorizationFilter`). Tidy the composition root by importing the missing namespaces and dropping the redundant qualifiers.
- **Challenge note:** Original advice ("add `using ClinicManagement.Infrastructure;`") is partly inaccurate — `Program.cs:3` **already** imports `ClinicManagement.Infrastructure`, so `CorsOrigins` and `LocalRequest` need only the qualifier **dropped**, not a new using. The genuinely-missing usings are `System.IO` (for `File`) and `System.Security.Cryptography.X509Certificates` (for `X509Certificate2`).

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| Major | 2 |
| Minor | 4 |
| Suggestion | 4 |
| **Total** | 10 |

**Major findings at a glance:**
1. OAuth `state` never validated + anonymous `authorize`/`callback` → LAN token-hijack / PHI exfiltration (Security).
2. HTTPS silently downgrades to plain HTTP when a cert path is set but the file is missing, with no log → PHI over cleartext (Business Logic).

The core release-gate mechanics were verified correct by multiple agents and re-confirmed against source during the challenge: Local-only fail-closed `FallbackPolicy` (null in Cloud), CORS enumeration/dedup + Cloud single-origin collapse, the `!isLocalAuthMode || httpsConfigured` redirect guard across all {mode}×{cert} combos, gitignored `.local/` token store with correct cache read-after-write + config fallback, loopback-only Hangfire in Local, and the `[AllowAnonymous]` coverage test asserting the exact allow-list. One original finding (Cloud OAuth-token persistence location) was dismissed as an intended, test-covered, functionally-equivalent refactor.
