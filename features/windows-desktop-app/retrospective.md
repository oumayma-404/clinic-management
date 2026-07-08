# Retrospective: Windows Desktop App — Phase 1 (Pluggable Auth + Local Accounts)

**Feature:** windows-desktop-app (Phase 1 — FR-A + FR-B)
**Date:** 2026-07-08
**Score:** Story 7 review 100/100; feature review COMPLETE & Challenged (0 Critical, 3 Major, 13 Minor, 10 Suggestion; 26 confirmed of 28 raised)

## Summary

Repackaged the Auth0-only clinic app to support an offline "Local" auth mode alongside "Cloud": local password accounts (PBKDF2 via ASP.NET `PasswordHasher`), per-install CSPRNG-signed JWTs, first-run admin bootstrap, clinic-code staff self-registration, an admin user-management UI, and an offline admin lockout-recovery console utility. Every Cloud/Auth0 path was preserved behaviorally; all Local behavior is additive and gated on `AUTH_MODE`.

## What Went Well

- **Clean mode separation:** a single `useSession()` seam and provider-gating kept the Cloud path untouched while adding Local; backend used discriminated command branches (`Password` present → Local) instead of parallel commands.
- **Security core verified sound in the challenge:** password hashing, per-install signing key handling (gitignored, CSPRNG, never in appsettings), token issuer/audience/lifetime/signature validation, the setup localhost gate, and the console-only (non-DI) recovery service all held up.
- **Strong unit coverage:** grew from 18 → 68 tests across the 8 stories, all backend business logic exercised.
- **Disciplined deviation tracking:** every deviation classified (trivial/plan-directed/defensive) and recorded in `progress.md`.

## What Could Be Improved

- **Server-side session enforcement is client-trusting** (the two root-cause Majors): stateless 12h JWT means forced-password-change and user deactivation/reset don't take effect until expiry; the sole enforcement is a user-deletable cookie. Needs a server-side gate (per-request `IsActive`, a `must_change` claim, or shorter token lifetime).
- **`secure` cookie keyed on `NODE_ENV`** would silently break login over a plain-HTTP LAN before Phase 4 HTTPS.
- **Small DRY/consistency debt:** duplicated clinic-code generator, hardcoded min-password-length literals, repeated resolve-current-admin block across four handlers, `new Random()` for the registration code.
- **Reviews skipped on 6 of 8 stories** (user choice) — findings surfaced later in the feature review rather than per-story.

## Learnings

7 learnings captured in `features/LEARNINGS.md`:

1. **Pattern:** Pluggable auth via mode-gated providers + a unified `useSession()` seam
2. **Pattern:** Discriminated command branch to add a mode without duplicating the command
3. **Pattern:** Testable maintenance/CLI logic belongs in the Application layer (UnitTests references only Application)
4. **Pitfall:** Stateless JWT means server-side state changes don't take effect until expiry
5. **Pitfall:** `secure` cookie keyed on `NODE_ENV` breaks login over plain HTTP; legacy `JwtSecurityTokenHandler` can't re-read its own `iss` on .NET 8
6. **Convention:** Per-install signing key never in appsettings + one shared resolution path; CSPRNG for all security-relevant values; one shared constant for multi-site policy values
7. **Tool:** `JwtBearer` is only referenced by the API project — add `System.IdentityModel.Tokens.Jwt` to Infrastructure explicitly
