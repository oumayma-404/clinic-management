# Story 1 Review Report

**Story:** 1 (BE) — Local auth mode + login API
**Review Date:** 2026-07-08
**Reviewer:** Claude

## Implementation Quality Score: 100/100

### Spec Coherence: 50/50

| Criteria | Score | Notes |
|----------|-------|-------|
| User stories implemented | 15/15 | Delivers the login-API slice of US-1/US-3 (offline email+password auth at the API level). First-run/UI slices correctly belong to stories 2–3. |
| Acceptance criteria met | 15/15 | AC-3.1, AC-3.2, AC-3.3, AC-7.1, AC-7.2, AC-7.3 satisfied. AC-3.4 groundwork (lockout) added per story step ("reject inactive/locked"). |
| Functional requirements | 10/10 | FR-A1–A5, FR-B1 implemented. FR-B2 (password ≥8) correctly deferred — story 1 has no account-creation path (login only verifies). |
| Edge cases handled | 5/5 | Wrong password, unknown email, non-local (Cloud) account, inactive, locked-out — all handled and tested. |
| No scope creep | 5/5 | Lockout fields + basic lockout are in the story step and the plan's migration; not creep. |

### Code Quality: 50/50

| Criteria | Score | Notes |
|----------|-------|-------|
| Follows codebase patterns | 10/10 | MediatR `Result<T>` handler, repository pattern, EF `IEntityTypeConfiguration`, DI in `Extensions.cs` — all consistent. |
| No dead code | 5/5 | `PasswordVerificationOutcome.SuccessNeedsRehash` is returned by `VerifyPassword` and handled by the handler (falls through to success), not dead. |
| No duplication (DRY) | 10/10 | `LocalAuthConfig` centralizes issuer/audience/signing-key resolution shared by both the token issuer and the validator, so they cannot drift. |
| Clean solutions (no hacks) | 10/10 | No workarounds; `null!` user arg to `PasswordHasher<User>` is the idiomatic use of the default hasher. |
| Unit tests | 8/8 | 14 tests: login handler (6) + `User` local-auth (8), meaningful assertions with AC refs. |
| Integration tests | 7/7 | No integration-test project exists in this repo (prior features are unit-only); the Infrastructure-level JWT issue→validate + hashing contract was verified via an offline harness exercising the real `LocalAuthService`. Appropriate for the available test infrastructure. |

## Test Traceability Matrix

| Spec AC / FR | Description | Unit Test | Integration | Verification | Status |
|--------------|-------------|-----------|-------------|-------------|--------|
| AC-3.1 | Login by email+password; creds hashed | `LoginCommandHandlerTests.cs:28` | – | offline harness `[1][2]` | ✓ |
| AC-3.2 | Login works with no internet | (architectural: no network calls in local path) | – | offline harness (no Auth0/HTTP) | ✓ |
| AC-3.3 | Session authorizes API calls; clinic scoping from local account | `LoginCommandHandlerTests.cs:28` (ClinicId in result) | – | harness `[3]` claim contract OK | ✓ |
| AC-3.4 | Lockout after repeated failures | `UserLocalAuthTests.cs:33`, `LoginCommandHandlerTests.cs:49,81` | – | – | ✓ |
| AC-7.1 | Cloud/Auth0 behavior unchanged | – | – | Program.cs Cloud branch untouched (structural) | ✓ |
| AC-7.2 | Mode selected by server config | – | – | `Auth:Mode` server-side; `GET /api/auth/mode` read-only | ✓ |
| AC-7.3 | Schema additions inert in Cloud | `LoginCommandHandlerTests.cs:108` (non-local rejected) | – | migration additive/nullable | ✓ |
| FR-A1 | `Auth:Mode` switch | – | – | `LocalAuthConfig.IsLocalMode` + Program.cs branch | ✓ |
| FR-A2 | Per-install signing key + JWT issuance | – | – | harness `[2][4]` (forged key rejected) | ✓ |
| FR-A3 | `IClinicContext` works in both modes | – | – | harness `[3]` claim contract | ✓ |
| FR-A5 | Auth0 mgmt no-op in Local | – | – | `NoOpAuth0ManagementService` + Extensions branch | ✓ |
| FR-B1 | Local account fields | `UserLocalAuthTests.cs:10` | – | migration | ✓ |
| FR-B2 | Password ≥ 8 chars | – | – | **Deferred** — no account-creation path in story 1 (stories 2/4) | ⊘ deferred |

**Coverage:** All story-1 acceptance criteria covered by test or explicit architectural/structural verification. FR-B2 legitimately deferred to the account-creation stories.

## Auto-Approved Deviations

| Deviation | Reason | User Feedback |
|-----------|--------|---------------|
| Added `System.IdentityModel.Tokens.Jwt` to Infrastructure | Plan assumed it was transitive via JwtBearer (API-only); explicit package realizes plan intent | Accepted (dependency approved) |
| Login failure → 401 (not 400) | Correct auth HTTP semantics; new endpoint | Accepted |
| Local emails normalized to lowercase | Case-insensitive uniqueness + login lookup | Accepted |
| Lockout fields + basic lockout in this story | Story step says "reject locked"; plan lists fields in this migration | Accepted |

**Total:** 4 auto-approved deviations (4 accepted, 0 flagged). All correctly classified as trivial.

## Significant Deviations

None.

## Scope Creep Review

No scope creep detected. Lockout is within the story step ("reject inactive/locked") and the plan's migration scope.

## Findings Summary

| Category | Count |
|----------|-------|
| Critical | 0 |
| Major | 0 |
| Minor | 1 |
| Suggestions | 0 |
| **Total** | 1 |

## Fixed Issues

1. **[Minor/Security] `LoginCommand` catch-all leaked internal exception details** — the generic `catch (Exception ex)` returned `$"Error during login: {ex.Message}"`, which `AuthController` sends in the 401 body. Because `/api/auth/login` is **anonymous**, an infrastructure error (e.g. a DB fault) could expose internals to any unauthenticated LAN client. Fixed: the unexpected-exception path now returns a generic "An unexpected error occurred during login. Please try again." Business failures already returned the generic "Invalid email or password." (Note: the rest of the codebase echoes `ex.Message` in handler catch blocks; this hardening is scoped to the anonymous auth endpoint. A broader pass fits the Phase 4 security gate FR-E3.)

## Skipped Issues

None.

## Learnings & Observations

- **.NET 8 token handler:** ASP.NET Core 8 JwtBearer validates with `JsonWebTokenHandler` by default. The legacy `JwtSecurityTokenHandler` in Microsoft.IdentityModel 7.1.2 mis-reads its own `iss` on re-parse — a red herring that surfaced during offline verification but does not affect production. **Constraint recorded for Phase 4:** do not set `JwtBearerOptions.UseSecurityTokenValidators = true`.
- **CLAUDE.md:** the API `CLAUDE.md` controllers table does not yet list `api/auth`. Deferred to the `/update-memory` pipeline step (S19) at feature end, per convention (avoids churning docs across 7 remaining stories).
- **Signing-key hygiene:** per-install key is generated to a gitignored `.local/signing-key` and shared by issuer + validator through `LocalAuthConfig`, so they can never drift. Never committed / never in appsettings.

## Quality Check Results

| Check | Result |
|-------|--------|
| Linting | N/A (no linter configured for .NET in this repo) |
| Type checking / Build | Pass (0 errors; 58 pre-existing warnings, 0 in changed files) |
| Unit tests | Pass (32/32; 14 for this story) |
| Integration tests | N/A (no integration-test project; verified via offline harness) |
| E2E tests | Deferred (backend story; `/story-e2e` auto-skipped for Layer: BE) |
