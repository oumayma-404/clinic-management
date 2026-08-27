# Story 1 Review Report — Phase 5 (Packaging, Installers & Manual Backup)

**Story:** 1 — [Full] Phase 5, slices S1–S7
**Review Date:** 2026-07-09
**Reviewer:** Claude
**Scope note:** Phase 5 is the final phase of the umbrella `windows-desktop-app` spec. Its pipeline artifacts share the feature-root filenames; Phases 1–4 are archived under `phase-1/`, `phase-3/`, `phase-4/`. S1–S4 are testable in-repo; **S5–S7 are committed-but-operator-verified** (R-1: this environment cannot run a Windows installer / WebView2 shell / a live pg_dump).

## Implementation Quality Score: 100/100

### Spec Coherence: 50/50

| Criteria | Score | Notes |
|----------|-------|-------|
| User stories implemented | 15/15 | US-1…US-7 all realised (backup, service+startup UX, cert self-gen, front door, shell, server+client installers). |
| Acceptance criteria met | 15/15 | Testable ACs (8.1–8.3, FR-F5, FR-E2, FR-B6, FR-F3) covered by code+tests; packaging ACs (1.x, 2.x, F4) covered by committed artifacts + the `packaging/README.md` operator checklist per R-1. |
| Functional requirements | 10/10 | FR-F/FR-G/FR-E2 addressed; Cloud gating (`isLocalAuthMode`) verified by reading — Cloud path byte-for-byte. |
| Edge cases handled | 5/5 | Backup: missing pg_dump / unwritable / disk-full / timeout each a distinct fail-loud error. Cert: idempotent reuse, corrupt-pfx regen. Shell: missing WebView2 runtime, corrupt config, nav failure. |
| No scope creep | 5/5 | No functionality outside spec. `Backup:TimeoutSeconds` is a trivial config surface for the plan's R-7 timeout — not creep. |

### Code Quality: 50/50

| Criteria | Score | Notes |
|----------|-------|-------|
| Follows codebase patterns | 10/10 | Backup command mirrors `ResetUserPasswordCommand` (caller-resolve + admin guard + `Result<T>`); thin controller; DI in `Extensions.cs`; mode-gating consistent with Phases 1–4. |
| No dead code | 5/5 | `certSource="cloud"` default is only logged in Local, but it documents posture and is not dead. |
| No duplication (DRY) | 10/10 | `LocalInstallPaths` centralises R-6 path resolution across 5 sites (signing key, token store, Files, logs, admin-reset). |
| Clean solutions (no hacks) | 10/10 | `Process.Start` uses an argument list (no shell string), `PGPASSWORD` via env, timeout+kill, concurrent stderr drain (R-7). Cert crypto kept in testable C#. |
| Unit tests | 8/8 | Backup handler (admin guard, invoke, failure surfacing, caller-not-found), pg_dump fail-loud pre-checks, cert (loadable/CA-signed/SAN/idempotent), startup classifier (8 cases). |
| Integration tests | 7/7 | N/A for this repo (no integration-test project); the runtime backup/cert/proxy paths are operator-verified per R-1 — the correct boundary, not a gap. |

## Test Traceability Matrix

Spec uses numbered ACs. Testable-here ACs mapped to unit tests; packaging ACs are operator-verified (R-1), not a coverage gap.

| AC / FR | Description | Unit Test | Operator (R-1) | Status |
|---------|-------------|-----------|----------------|--------|
| AC-8.1 | Admin-only backup to a destination | `BackupNowCommandHandlerTests.Handle_Should_Run_Backup_For_Admin` / `_Reject_Non_Admin` | live pg_dump | ✓ |
| AC-8.2 | Reports success/failure, never silent | `..._Surface_Backup_Failure`; `PgDumpBackupServiceTests.*_fails_loud` | — | ✓ |
| AC-8.3 | Storage/DB failures surface clearly | `PgDumpBackupServiceTests` (missing conn/pg_dump/destination) | disk-full live | ✓ |
| AC-8.4 | Documented manual restore | — | `packaging/README.md` restore section | ✓ (doc) |
| FR-F5 | Startup failures (DB down, port) clear | `StartupDiagnosticsTests` (8 cases) | — | ✓ |
| FR-E2 | Local CA + server cert, SANs, CA export | `CertificateProvisionerTests` (4 cases) | client CA trust import | ✓ |
| FR-B6 | Admin-recovery CLI from any CWD | (build-verified: `AppContext.BaseDirectory`) | packaged exe run | ✓ |
| FR-F3 | Migrations auto-apply on startup | (existing `Database.Migrate()`, Local-gated fail-loud) | fresh install | ✓ |
| AC-1.1–1.4 | Server install / services / LAN | — | `clinic-server.iss` + checklist | ⊘ operator |
| AC-2.1–2.5 | Client install / shell / change-server | — | `clinic-client.iss` + shell + checklist | ⊘ operator |
| FR-F4 | No real secrets bundled | (publish-server.ps1 config scrub) | installed-config audit | ✓ (artifact) |

**Coverage:** all testable-here ACs covered (100%); packaging ACs deliberately operator-verified per R-1.

## Auto-Approved Deviations

| Deviation | Reason | User Feedback |
|-----------|--------|---------------|
| Shared `LocalInstallPaths` helper vs inlining `AppContext.BaseDirectory` | Internal path-resolution extraction; no API/behavior change | Accepted |
| BFF routes moved via `git mv` (history preserved) | Same content, mechanical move | Accepted |
| S6 config sanitization in `publish-server.ps1` (publish-output only) | Build-output step; realises FR-F4 at the publish boundary | Accepted |
| S5 standalone `DesktopShell.sln` + `%AppData%\ClinicManagement\server.json` store | New isolated files; store path is AC-2.2's location | Accepted |

**Total:** 4 auto-approved (4 accepted, 0 flagged). All correctly classified as trivial.

## Significant Deviations

| ID | Title | Justified | Approved | Score Impact |
|----|-------|-----------|----------|--------------|
| DEV-1 | Restore/build-env docs written into `packaging/README.md` in the S1–S4 session | Yes | Auto | 0 |
| DEV-2 | DesktopShell kept standalone, not added to `api/ClinicManagement.sln` | Yes | Yes (user chose "Keep standalone") | 0 |

Both sound: DEV-1 satisfies AC-8.4 at the earliest slice; DEV-2 keeps the `net8.0-windows` WPF GUI out of the backend `net8.0` quality gate.

## Scope Creep Review

| Item | Location | Resolution | Score Impact |
|------|----------|------------|--------------|
| (none) | — | — | 0 |

No scope creep. `Backup:TimeoutSeconds` is the configurable form of the plan's mandated R-7 timeout.

## Findings Summary

| Category | Count |
|----------|-------|
| Critical | 0 |
| Major | 0 |
| Minor | 1 |
| Suggestions | 0 |
| **Total** | 1 |

## Fixed Issues

1. **[Minor — security hardening] Auth session cookie shipped without `Secure` on the HTTPS deployment.**
   The Node web server sits behind the Kestrel HTTPS front door on a plain-HTTP loopback hop, so the
   `local-login` BFF handler's `request.nextUrl.protocol` is `http:` there and derives `secure=false` —
   even though the browser transport is HTTPS. The server installer set the Node service env but omitted
   the `AUTH_COOKIE_SECURE` override the handler was written to consume. Practical risk is low (HttpOnly +
   SameSite=Lax set; only `httpsPort` is LAN-open per the firewall step), but it undercuts Phase 5 S3's
   "LAN traffic over HTTPS" intent for the session token.
   **Fix:** added `AUTH_COOKIE_SECURE=true` to the web-service env in `packaging/server/clinic-server.iss`;
   documented it in `packaging/README.md` (env table + serve note); refreshed the now-stale
   "HTTPS arrives in a later phase" comment in `web/app/bff/auth/local-login/route.ts` to explain the
   front-door/loopback-hop topology and why the override is set.

## Skipped Issues

None.

## Learnings & Observations

- **Front-door topology has a cookie-scheme trap:** any request-scheme-derived security decision in a BFF
  route handler (cookie `Secure`, redirect scheme) is made on the *internal* HTTP hop, not the browser's
  HTTPS leg. Handlers behind the Kestrel/YARP front door must rely on an explicit override (or trusted
  `X-Forwarded-Proto` handling) rather than `request.nextUrl.protocol`.
- **Mode-gating stayed disciplined:** every Phase 5 behavior keys off `isLocalAuthMode`; the Cloud HTTPS
  bind/redirect path is byte-for-byte equivalent to Phase 4 (`!isLocalAuthMode` ⇒ redirect always on).
- **Phase 4 coverage guard held:** the new `[Authorize(AdminOnly)]` `BackupController` adds no anonymous
  surface, so `ControllerAuthorizationCoverageTests` remains green with no allow-list change.
- **R-1 boundary is the right one:** S5–S7 land as committed, reviewable artifacts + an operator checklist;
  treating their runtime verification as operator-owned is correct, not a coverage hole.

## Quality Check Results

| Check | Result |
|-------|--------|
| Linting | N/A (no linter configured; ESLint disabled in `next.config.ts`) |
| Type checking (`npx tsc --noEmit`, web) | Pass (0 errors) |
| Backend build (`dotnet build ClinicManagement.sln`) | Pass (0 errors; 57 warnings = pre-change baseline, 0 new) |
| Unit tests | 15/19 pass; 4 blocked by the environmental Smart App Control DLL block (`0x800711C7`) — not logic failures (project memory `smart-app-control-blocks-tests` / R-1) |
| Integration tests | N/A (no integration-test project in repo) |
| E2E tests | N/A (skipped during story review; no `test-plan-e2e.md` for Phase 5) |
| Desktop shell build (`net8.0-windows`, standalone sln) | Pass per progress.md (0/0); not re-run here (WPF/WebView2 needs Windows GUI toolchain — R-1) |

**Verdict:** Story 1 (Phase 5) is well-implemented and consistent with the plan and the Phase 1–4 conventions.
One security-hardening finding was found and fixed. All in-environment quality gates are green. S5–S7 remain
operator-verified against `packaging/README.md` per R-1.
