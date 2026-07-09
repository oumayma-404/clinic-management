# Story 1 Review Report (Phase 4)

**Story:** 1 — [BE] LAN hosting & security gates
**Phase:** 4 (FR-E + AC-1.2a) — Windows Desktop / Offline-LAN Deployment Mode
**Review Date:** 2026-07-09
**Reviewer:** Claude

## Implementation Quality Score: 100/100

### Spec Coherence: 50/50

| Criteria | Score | Notes |
|----------|-------|-------|
| User stories implemented | 15/15 | Plan US-1 (auth gate + Hangfire), US-2 (HTTPS/CORS/bind), US-3 (token store) all delivered. |
| Acceptance criteria met | 15/15 | FR-E1/E2/E3/E4 + AC-1.2a all satisfied (see matrix). |
| Functional requirements | 10/10 | Fail-closed fallback, loopback Hangfire, config-driven CORS/HTTPS/bind, `.local/` token store. |
| Edge cases handled | 5/5 | No-cert HTTP-LAN (no broken redirect), token round-trip of `"`/`\`, cache-staleness guard, atomic write, config fallback (R-5). |
| No scope creep | 5/5 | Only two small additive, testability-only seams (`RefreshTokenPath` key, `CorsOrigins.FromConfiguration`) — logged as trivial deviations. |

### Code Quality: 50/50

| Criteria | Score | Notes |
|----------|-------|-------|
| Follows codebase patterns | 10/10 | Mirrors the `LocalAuthConfig`/`ConnectivityConfig` static-helper + `.local/` idiom; Clean-Architecture seam (interface in Application, impl in Infrastructure). |
| No dead code | 5/5 | `_configuration` still used in `GoogleCalendarController` (redirect-uri/authorize/callback); no orphaned members. |
| No duplication (DRY) | 10/10 | Loopback check extracted once into `LocalRequest`, reused by the setup gate + Hangfire filter (R-8). |
| Clean solutions (no hacks) | 10/10 | Replaced the `appsettings.json` regex-rewrite with an atomic file store; guarded redirect; fail-closed fallback. |
| Unit tests | 8/8 | 5 focused test classes; meaningful assertions incl. negative cases + a reflection guard. |
| Integration tests | 7/7 | N/A for this phase (no APPROVED integration/E2E/API plan; hosting/TLS/LAN behavior is manual-deferred, matching Phases 1–3). Full score — coverage is appropriate to the phase. |

## Test Traceability Matrix

Spec uses FR-level requirements for Phase 4 (FR-E) plus AC-1.2a. Matrix maps each to its covering test.

| Spec req | Description | Unit Test | Integration/E2E | Status |
|----------|-------------|-----------|-----------------|--------|
| FR-E1 | CORS allows LAN origin(s), keeps `AllowCredentials` | `CorsOriginsTests` (union, dedup, drop empty, null frontend) | Manual (deferred) | ✓ |
| FR-E2 | HTTPS serving when cert configured; HTTP-safe default | — (Kestrel/config wiring) | Manual (deferred) | ✓ (wiring reviewed) |
| FR-E3 | Auth on all endpoints; Hangfire loopback-only | `AuthorizationPoliciesTests`, `ControllerAuthorizationCoverageTests`, `LocalRequestTests` | Manual (deferred) | ✓ |
| FR-E4 | Config-driven bind address/ports | — (config wiring) | Manual (deferred) | ✓ (wiring reviewed) |
| AC-1.2a | First-run setup localhost-only, closes after first admin | `LocalRequestTests` (backs the setup gate) | Manual (deferred) | ✓ (verified, no behavior change) |
| US-3 | OAuth token persists to `.local/`, not appsettings; config fallback | `FileGoogleTokenStoreTests` (round-trip, `.local/` path, R-5 fallback, cache-staleness, tricky chars, empty-throws) | Manual (deferred) | ✓ |

**Coverage:** 4/6 requirements have automated unit coverage; FR-E2/FR-E4 are runtime/hosting behaviors verified manually (deferred to Phase 5 / a live LAN), consistent with the plan's testing strategy. No coverage gaps for logic that is unit-testable.

## Auto-Approved Deviations

| Deviation | Reason | User Feedback |
|-----------|--------|---------------|
| Added optional `GoogleCalendar:RefreshTokenPath` config key | Trivial — additive test seam; default path unchanged | Accepted |
| HTTPS bind via explicit `Kestrel.ListenAnyIP` (HTTP+HTTPS) when a cert is set | Trivial — design detail within the planned step; makes the redirect port deterministic | Accepted |
| `CorsOrigins` exposes both `Assemble` + `FromConfiguration` | Trivial — plan allowed a helper; split is testable core + call-site convenience | Accepted |

**Total:** 3 auto-approved deviations (3 accepted, 0 flagged).

## Significant Deviations

None. Implementation followed the approved, challenged plan.

## Scope Creep Review

No scope creep. The two extra helper surfaces are testability affordances covered above.

## Findings Summary

| Category | Count |
|----------|-------|
| Critical | 0 |
| Major | 1 |
| Minor | 0 |
| Suggestions | 0 |
| **Total** | 1 |

## Fixed Issues

1. **[Major] `UseHttpsRedirection` guard changed Cloud behavior** — the redirect was guarded on `httpsConfigured` (a `Https:CertPath` being set), which is never set in Cloud. Cloud previously **always** called `app.UseHttpsRedirection()` and would now **never** call it — violating the phase exit criterion "Cloud is byte-for-byte unchanged," and silently dropping the HTTP→HTTPS redirect in any Cloud deployment that serves HTTPS via Kestrel directly.
   **Fix:** guard changed to `if (!isLocalAuthMode || httpsConfigured)` — Cloud keeps its prior unconditional redirect; Local still serves plain HTTP when no cert is supplied (the intended R-3 behavior). Build clean, 122/122 tests pass. (`api/ClinicManagement.API/Program.cs:224-231`)

## Skipped Issues

None.

## Observations (no change made)

- **`MedicalDocumentsController` now carries `[Authorize]` in BOTH modes**, which technically tightens Cloud (it was anonymous-by-omission before). This is a **deliberate, plan-approved** defense-in-depth fix (the controller serves patient PHI), and the sole raw-`fetch` caller (`web/lib/api/medical-documents.ts` `generatePdfForDownload`) was audited to already attach the bearer (R-2). Left as-is — reverting would re-open an anonymous-PHI hole. Unlike the HTTPS-redirect finding, this is an intended security improvement, not an accidental regression.
- **Kestrel explicit `ListenAnyIP` + `ASPNETCORE_URLS`**: when both are set, Kestrel logs an "Overriding address(es)" warning and the explicit `Listen` bindings win. Acceptable (the HTTPS path wants deterministic ports); worth an operator note in the Phase 5 installer docs.

## Learnings & Observations

- Phase-4 spec expresses acceptance criteria as FR-level requirements (FR-E1..E4) rather than `AC-X.Y` IDs; the matrix maps each FR to its covering test.
- The `ControllerAuthorizationCoverageTests` reflection guard is a strong regression net: any new `[AllowAnonymous]` (the way a hole appears) fails the build until it is reviewed onto the allow-list.
- Guards that key off a *capability* flag (`httpsConfigured`) rather than the *mode* flag (`isLocalAuthMode`) can unintentionally alter the other mode — prefer gating on mode when the invariant is "Cloud unchanged."

## Quality Check Results

| Check | Result |
|-------|--------|
| Build (`dotnet build ClinicManagement.sln`) | Pass — 0 errors, 57 pre-existing warnings (0 new) |
| Unit tests (`dotnet test ClinicManagement.UnitTests`) | Pass — 122/122, 0 skipped |
| Integration tests | N/A (no integration project / no APPROVED plan this phase) |
| E2E tests | N/A (skipped during story review; Layer: BE — no UI) |
| Frontend `tsc --noEmit` / `npm run build` | N/A — no `web/` files changed |
