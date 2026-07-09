# Phase 5 — Implementation Progress

**Feature:** windows-desktop-app (Phase 5 — Packaging, Installers & Manual Backup)
**Plan:** [../plan.md](../plan.md) (APPROVED · Challenged 2026-07-09)
**Branch:** `feature/windows-desktop-app`

## Session scope

**Story 1 is one `Layer: Full` story delivered in 7 slices (S1–S7).**

- **Session 1 (2026-07-09):** slices **S1–S4** (all testable in-repo) — backend/web changes, committed (5fc7b16).
- **Session 2 (2026-07-09):** the deferred packaging slices **S5–S7** — WebView2 desktop shell, server
  installer, client installer + expanded `packaging/README.md`. These are **committed-but-not-executed /
  operator-verified (R-1)**: this environment cannot run a Windows installer or a WebView2 shell. The shell
  **compiles clean** (`dotnet build` → 0/0); the installers + end-to-end stand-up are proven by the operator
  against the `packaging/README.md` checklist. **No S1–S4 code was touched** (verified: `git status` shows only
  new `desktop/` + `packaging/` files and doc/gitignore edits).

## Working tree note (start of session)

Only `features/windows-desktop-app/plan.md` and `features/windows-desktop-app/stories/` were untracked at
session start — the Phase 5 planning artifacts. No unrelated changes. Nothing excluded.

## Slice Status

| Slice | Scope | Status |
|-------|-------|--------|
| S1 | One-click admin backup (service + API + UI) | done (backend builds, FE typechecks; restore doc pending packaging/README.md) |
| S2 | Windows service + startup-failure messaging | done (backend builds; StartupDiagnostics tests pass) |
| S3 | HTTPS cert self-gen + fail-loud transport | done (backend builds; CertificateProvisioner tests pass) |
| S4 | Same-origin Kestrel front door (YARP + BFF relocation + FE URL fixes) | done (backend builds; web `tsc` + `npm run build` pass; `/bff/auth/*` routes confirmed) |
| S5 | WebView2 desktop shell | done (committed; `dotnet build` 0/0; runtime operator-verified R-1) |
| S6 | Server installer (publish-server.ps1 + clinic-server.iss) | done (committed; PS parses under PS5.1; ISCC/stand-up operator-verified R-1) |
| S7 | Client installer (clinic-client.iss) + README checklist | done (committed; ISCC/CA-import operator-verified R-1) |

## Final quality gate (S1–S4)

- **Backend:** `dotnet build ClinicManagement.sln` → 0 errors; 57 warnings = the exact pre-change baseline
  (0 new; the `Program.cs:187` hit is the pre-existing Hangfire `UsePostgreSqlStorage` obsolete warning,
  shifted down by added lines).
- **Frontend:** `npx tsc --noEmit` → 0 errors; `npm run build` → success (standalone). Route list confirms
  `/bff/auth/*` (relocated) and no `/api/auth/*`.
- **Unit tests:** 15/19 pass; 4 blocked by the environmental SAC DLL block (see test-run note above).
- **Not runnable here:** full-stack behavioral verification (front door proxying, cert-served HTTPS,
  live backup against a real pg_dump/PostgreSQL) requires Docker + a Windows install — deferred to operator
  verification per `packaging/README.md` (R-1). S5–S7 packaging artifacts not started this session.

## Final quality gate (S5–S7, Session 2)

- **Desktop shell:** `dotnet build desktop/ClinicManagement.DesktopShell.sln -c Release` → **0 errors, 0 warnings**
  (WebView2 restored; WPF built). Running the shell requires a Windows install + WebView2 runtime → operator-verified (R-1).
- **publish-server.ps1:** `[Parser]::ParseFile` → **0 errors** (validated it parses under Windows PowerShell 5.1,
  not only pwsh 7). Non-ASCII `→`/`—` replaced with ASCII so `powershell.exe`'s codepage guess on the no-BOM
  file can't break parsing.
- **Inno Setup `.iss` (server + client):** review-only; **cannot compile here** (no ISCC in this environment, R-1).
  Saved UTF-8 **with BOM** so ISCC (Unicode) reads the French dialog strings without mojibake.
- **No backend/web rebuild needed:** S5–S7 touched **zero** `api/`/`web/` source, so the Session-1 green gate
  (`dotnet build ClinicManagement.sln` 0/57-baseline; web `tsc`+`build`) is unchanged by construction.

## Auto-Approved Deviations

| Deviation | Classification | Reason |
|-----------|----------------|--------|
| Introduced shared `LocalInstallPaths` helper (Infrastructure) instead of inlining `AppContext.BaseDirectory` at each R-6 site | Trivial | Internal helper extraction (pure path resolution); no API/behavior change; used by the same Local-only paths the plan names. |
| BFF routes relocated with `git mv` (history preserved) rather than delete+recreate | Trivial | Same files/content; mechanical move only. |
| S6: config sanitization (blank Google/HuggingFace/Auth0 secrets, set Local mode) added to `publish-server.ps1` operating on the *published* `appsettings.json` | Trivial | Build-output-only step; no source/behavior change; realises the plan's "scrub secrets from shipped config" (US-6 step 5 / FR-F4) at the publish boundary rather than in committed source. |
| S5: `desktop/ClinicManagement.DesktopShell.sln` created as a standalone solution + `%AppData%\ClinicManagement\server.json` chosen for the address store (plan named the store path but not the sln layout) | Trivial | New isolated files; no effect on other code; store path is exactly the plan's AC-2.2 location. |

## Significant Deviations

### DEV-1: Restore/build-env docs written into a new `packaging/README.md` this session
**Date:** 2026-07-09
**Story:** 1 (slices S1/S2/S4)
**Category:** Scope
**Original Plan:** `packaging/README.md` is listed under the packaging slices (S5–S7).
**Actual Implementation:** Created `packaging/README.md` now with the S1 manual-restore procedure (AC-8.4 /
FR-G3), the S2 packaged `reset-admin-password` invocation, and the S4 Local build-env matrix. Installer
build/install sections + the per-AC operator checklist remain for the S5–S7 session.
**Justification:** AC-8.4 (documented restore) is an S1 exit criterion and must be satisfied this session;
S2/S4 also call for doc entries. Deferring all docs to S5–S7 would leave S1–S4 acceptance criteria unmet.
**Impact:** None negative — the file is additive and will be expanded, not rewritten, in the packaging session.
**Approved:** Auto (documentation of already-approved slice scope; no code/behavior impact).

### DEV-2: DesktopShell kept as a standalone solution, NOT added to `api/ClinicManagement.sln`
**Date:** 2026-07-09 (Session 2)
**Story:** 1 (slice S5)
**Category:** Scope / structural
**Original Plan:** US-5 / S5 step 5 says "Add the project to the solution" (the backend `api/ClinicManagement.sln`).
**Actual Implementation:** Created `desktop/ClinicManagement.DesktopShell.sln` (standalone) instead; the shell is
built/published only via `packaging/publish-server.ps1`. `api/ClinicManagement.sln` is untouched.
**Justification:** The shell is a Windows-only WPF + WebView2 GUI (`net8.0-windows`), a different SDK/target
from the backend `net8.0` services. Adding it to the backend solution would make the verified-green S1–S4
quality gate (`dotnet build ClinicManagement.sln`) also build a desktop GUI on every backend build — risking
baseline drift/native-asset restore issues — and muddy a backend solution with a client. The publish script
covers buildability/discoverability the plan intended.
**Impact:** Positive — backend gate stays byte-for-byte; shell still builds (own sln, 0/0) and publishes.
No behavior change to any shipped app.
**Approved:** Yes — user chose "Keep it standalone (Recommended)" via AskUserQuestion, 2026-07-09.

## Test run note (S1–S3)

`dotnet test` on the new unit tests: **15/19 passed**. The 4 "failures" are the documented environmental
**Smart App Control** block (`0x800711C7`, `Application Control policy has blocked this file`) on the
freshly-rebuilt `ClinicManagement.Domain.dll` — hit only by `BackupNowCommandHandlerTests` (it constructs a
`User` from Domain), identical in shape to the already-passing `ResetUserPasswordCommandHandlerTests`. Not a
logic defect (R-1 / project memory `smart-app-control-blocks-tests`). Passing: all `CertificateProvisioner`
(4), `StartupDiagnostics` (8), and `PgDumpBackupService` (3) tests — cert SANs/CA-signing/idempotency,
startup DB-down/port-in-use classification, and backup fail-loud pre-checks all verified green.

## Review (2026-07-09)

Story 1 **reviewed** — see [`../reviews/story-1.md`](../reviews/story-1.md). Score **100/100**. Backend
build 0 errors / 57-baseline warnings; web `tsc` 0 errors. **1 finding (Minor, security-hardening), fixed
during review:** the auth session cookie shipped without `Secure` on the HTTPS front-door deployment because
the BFF login handler runs on the internal plain-HTTP loopback hop and the installer omitted the
`AUTH_COOKIE_SECURE` override — fixed in `packaging/server/clinic-server.iss` + `packaging/README.md` +
the `web/app/bff/auth/local-login/route.ts` comment. S5–S7 remain operator-verified per `packaging/README.md`
(R-1). Slice statuses below advance from `done (implemented)` to **reviewed**.

## Learnings

- **YARP reverse proxy must be added pre-`builder.Build()`** for cert config, but the provisioner also runs
  pre-Build (Kestrel needs the cert before DI exists) — so `CertificateProvisioner` is instantiated directly
  in `Program.cs` with a `NullLogger`, and DI-registered only for completeness. (S3)
- **`Microsoft.Extensions.Hosting.WindowsServices` 8.0.1 pins `System.Diagnostics.EventLog` 8.0.1** — adding
  EventLog at 8.0.0 trips `NU1605` (downgrade-as-error). Match the transitive version. (S2)
