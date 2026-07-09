# Retrospective: Windows Desktop App — Phase 4 (LAN Hosting & Security Gates)

**Feature:** windows-desktop-app — **Phase 4** (FR-E + AC-1.2a)
**Date:** 2026-07-09
**Duration:** 1 story, single session
**Score:** Story 1 — 100/100; feature review — 0 Critical, 2 Major, 4 Minor, 4 Suggestion (all confirmed post-challenge; 1 original finding dismissed as false positive)

## Summary

Hardened the API for offline-LAN hosting: a fail-closed `FallbackPolicy` in Local mode, loopback-only Hangfire dashboard, an exact-match `[AllowAnonymous]` allow-list guarded by a reflection test, config-driven CORS/HTTPS/bind, and replacement of the `appsettings.json` OAuth-refresh-token rewrite with a gitignored `.local/` file token store. Cloud mode is preserved unchanged. All behavior is additive and gated to Local mode.

## What Went Well

- **Release-gate mechanics verified correct across all mode×cert combinations** — Local fail-closed fallback (null in Cloud), CORS dedup + Cloud single-origin collapse, the `!isLocalAuthMode || httpsConfigured` redirect guard, loopback Hangfire, and the anonymous-endpoint allow-list.
- **Security debt actively retired**: the `appsettings.json` refresh-token regex-rewrite was removed in favor of an atomic `.local/` token store with cache read-after-write and config fallback — test-covered (`FileGoogleTokenStoreTests`).
- **Strong regression net**: `ControllerAuthorizationCoverageTests` pins the exact anonymous allow-list, so any new/renamed/removed anonymous endpoint fails the build.
- **Clean quality gate**: build 0 errors / 0 new warnings; 122/122 unit tests pass (Smart App Control did not block this run); no `web/` changes needed.
- **Effective story-review catch**: a Cloud-behavior regression (redirect guarded on `httpsConfigured` instead of mode) was caught and fixed *during* review, preserving the "Cloud byte-for-byte unchanged" invariant.

## What Could Be Improved

- **OAuth `state` is still unvalidated** and `authorize`/`callback` remain anonymous (feature-review Finding 1, Major) — a genuine LAN token-hijack / PHI-exfiltration vector to address before shipping Local mode. Carried as a confirmed finding for follow-up.
- **HTTPS fails open**: setting a cert path to a missing file silently downgrades to plain HTTP with no log (Finding 2, Major). Transport posture should fail loud/closed and be logged on startup.
- **A `Https:CertPassword` slot was added to committed `appsettings.json`** (Finding 5) — same phase that eliminated the committed-secret pattern for the refresh token. Should route through `.local/` / env / user-secrets.
- Several tidy-ups (mode-gate the Kestrel bind, unique temp-file name in the Singleton write, drop redundant fully-qualified type names) confirmed as Minor/Suggestion findings.

## Learnings

8 learnings captured in `features/LEARNINGS.md`:

1. **Pattern:** Reflection-based allow-list test as an authorization regression net
2. **Pattern:** Gate mode-invariant guards on the *mode* flag, not a *capability* flag
3. **Pitfall:** Security/transport config must fail closed and loud, not silently degrade
4. **Pitfall:** Anonymous OAuth callback + unvalidated `state` = shared-token hijack
5. **Pitfall:** Shared-Singleton "atomic" file writes need a unique temp path per write
6. **Pitfall:** Grep for *every* symbol a namespace provides before dropping its `using`
7. **Convention:** Don't add secret-bearing keys to tracked `appsettings.json` — even empty ones
8. **Tool:** A test project exercising a Web-SDK API needs an explicit ASP.NET `FrameworkReference`
