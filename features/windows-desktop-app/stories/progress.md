# Phase 5 — Implementation Progress

**Feature:** windows-desktop-app (Phase 5 — Packaging, Installers & Manual Backup)
**Plan:** [../plan.md](../plan.md) (APPROVED · Challenged 2026-07-09)
**Branch:** `feature/windows-desktop-app`

## Session scope (2026-07-09)

**Story 1 is one `Layer: Full` story delivered in 7 slices (S1–S7).** Per the multi-phase rule, the user
scoped **this session to slices S1–S4** (all testable in-repo). Slices **S5–S7 (packaging: WebView2 shell,
Inno Setup installers, publish orchestration) are deferred** to a follow-up session — they are
committed-but-not-executed / operator-verified (R-1) and add no value to attempt here.

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
| S5 | WebView2 desktop shell | **deferred (next session)** |
| S6 | Server installer | **deferred (next session)** |
| S7 | Client installer | **deferred (next session)** |

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

## Auto-Approved Deviations

| Deviation | Classification | Reason |
|-----------|----------------|--------|
| Introduced shared `LocalInstallPaths` helper (Infrastructure) instead of inlining `AppContext.BaseDirectory` at each R-6 site | Trivial | Internal helper extraction (pure path resolution); no API/behavior change; used by the same Local-only paths the plan names. |
| BFF routes relocated with `git mv` (history preserved) rather than delete+recreate | Trivial | Same files/content; mechanical move only. |

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

## Test run note (S1–S3)

`dotnet test` on the new unit tests: **15/19 passed**. The 4 "failures" are the documented environmental
**Smart App Control** block (`0x800711C7`, `Application Control policy has blocked this file`) on the
freshly-rebuilt `ClinicManagement.Domain.dll` — hit only by `BackupNowCommandHandlerTests` (it constructs a
`User` from Domain), identical in shape to the already-passing `ResetUserPasswordCommandHandlerTests`. Not a
logic defect (R-1 / project memory `smart-app-control-blocks-tests`). Passing: all `CertificateProvisioner`
(4), `StartupDiagnostics` (8), and `PgDumpBackupService` (3) tests — cert SANs/CA-signing/idempotency,
startup DB-down/port-in-use classification, and backup fail-loud pre-checks all verified green.

## Learnings

- **YARP reverse proxy must be added pre-`builder.Build()`** for cert config, but the provisioner also runs
  pre-Build (Kestrel needs the cert before DI exists) — so `CertificateProvisioner` is instantiated directly
  in `Program.cs` with a `NullLogger`, and DI-registered only for completeness. (S3)
- **`Microsoft.Extensions.Hosting.WindowsServices` 8.0.1 pins `System.Diagnostics.EventLog` 8.0.1** — adding
  EventLog at 8.0.0 trips `NU1605` (downgrade-as-error). Match the transitive version. (S2)
