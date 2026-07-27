# Progress: Data & Money Integrity

**Started:** 2026-07-27
**Type:** Full (one story, eleven parts)
**Branch:** `feature/data-and-money-integrity`
**Worktree:** `.claude/worktrees/data-and-money-integrity` (branched from `22b37a1`)

## Story status

| # | Story | Status |
|---|-------|--------|
| 1 | Correct the eight data-loss and money defects, end to end | in progress |

## Part status

| Part | Name | Status | Commit | Pushed |
|------|------|--------|--------|--------|
| — | Scaffold (stories, progress) | complete | `42e8ecf` | yes |
| A | Réconciliation report | complete | `c712f06` | yes |
| B | Patient delete blocks + archive | complete | | |
| C | Appointment update stops wiping the act | pending | | |
| D | Void a payment + invoice detail modal | pending | | |
| E | Installment ledger + plan void + receipts | pending | | |
| F | Devis→facture carry-over | pending | | |
| G | Avoirs readable + PDF + netting | pending | | |
| H | Patient contact optional | pending | | |
| I | Conflict detection — backend | pending | | |
| J | Conflict detection — frontend | pending | | |
| K | Documentation | pending | | |

## Working tree note (start of session)

Work is happening in an **isolated worktree** at the user's explicit request. The user's own branch
(`feature/security-hardening`) and its uncommitted work are deliberately untouched:

- ` M packaging/server/clinic-server.iss`
- `?? api/ClinicManagement.API/Maintenance/CredentialProtectionCommand.cs`
- `?? api/ClinicManagement.API/Maintenance/HardenPermissionsCommand.cs`
- `?? api/ClinicManagement.Infrastructure/Security/DbCredentialProtector.cs`
- `?? api/ClinicManagement.Infrastructure/Security/DirectoryAclHardener.cs`
- `?? api/ClinicManagement.Infrastructure/Security/LocalDataProtection.cs`
- `?? api/ClinicManagement.UnitTests/Infrastructure/Security/*Tests.cs` (3 files)
- `?? features/security-hardening/`

None of these are staged, reverted or copied into this worktree. The main working directory remains checked out on
`feature/security-hardening` for the whole session.

**Copied into the worktree** (they were untracked in the main dir and would otherwise not exist here):
`features/data-and-money-integrity/{spec,plan,exploration}.md` and `CODEBASE_AUDIT_2026-07.md`. The originals are
left in place in the main working directory.

## Setup deviations

| Deviation | Classification | Reason |
|-----------|----------------|--------|
| Created `stories/` scaffold directly instead of running `/break-plan` | Trivial | The plan contains exactly one story; `/break-plan`'s job is splitting a monolithic plan into several. Generating the three files directly avoids a round-trip that would produce the same result. |
| Worktree branched from `22b37a1` rather than the default `origin/main` | Significant — surfaced to the user before acting | `main` is **138 commits behind** `22b37a1`. Branching from it would drop the entire billing subsystem (invoices, treatment plans, credit notes) that this feature modifies, making the plan unimplementable. |
| Branch is `feature/data-and-money-integrity`, not `feature/windows-desktop-app` as `plan.md` states | Trivial | The plan was written when that was the checked-out branch. The user has since moved to `feature/security-hardening` and asked for an isolated worktree. Same base commit either way. |

## Part A — notes

**Quality gate:** `dotnet build --no-incremental` → 0 errors. 116 warnings, **all pre-existing baseline**
(`CS8618` on Domain entity/value-object EF ctors, `CS8602`/`CS8600` in existing controllers, one `CS0618` for
Hangfire's obsolete `UsePostgreSqlStorage(string)`). **0 warnings in the files this part added or changed** —
the `Program.cs` `CS0618` hit is the pre-existing Hangfire one, shifted from line 244 to 254 by the 10-line
verb insertion. Tests: 22 new, all passing via the documented `dotnet build -p:OutDir=… ` + `dotnet vstest`
workaround (`dotnet test` is Smart-App-Control-blocked on this machine).

**Files added:** `Application/Common/Interfaces/IMoneyReconciliationReader.cs`,
`Application/Common/Maintenance/MoneyReconciliationService.cs`,
`Infrastructure/Persistence/MoneyReconciliationReader.cs`, `API/Maintenance/ReconcileMoneyCommand.cs`,
`UnitTests/Common/Maintenance/MoneyReconciliationServiceTests.cs`,
`UnitTests/Api/Maintenance/ReconcileMoneyCommandTests.cs`. **Changed:** `API/Program.cs` (verb interception).

### Auto-approved deviations

| Deviation | Classification | Reason |
|-----------|----------------|--------|
| Added an `IMoneyReconciliationReader` seam (Application interface + Infrastructure implementation) that the plan's file list did not name | Trivial — forced by the architecture, resolved with the established pattern | The plan put `MoneyReconciliationService` in Application so `UnitTests` (which references Application, not Infrastructure) can test it. But the report needs cross-clinic `DbContext` access, and **Application cannot reference `ApplicationDbContext`**. The codebase's standing answer to exactly this is an interface in `Common/Interfaces` implemented in Infrastructure (`IFileStorage`, `IPdfGenerationService`, `IInternetProbe`, …). No behaviour or public API change; the comparison logic still lives in Application and is unit-tested against a mocked reader. |
| `MoneyReconciliationReader` is **not** DI-registered | Trivial | It is only ever constructed manually by the console verb, like `CertificateProvisioner` and `AdminPasswordRecoveryService`. `LEARNINGS.md` records that a registration nothing resolves "reads as wired-up when it isn't". |
| Report renders money as `0.000 DT` rather than reusing a French formatter | Trivial | Operator-facing CLI output, matching the English of the two existing verbs. Not user-facing UI. |

### Deliberately not in Part A (per the plan, not deferred work)

Two checks named in spec slice H reference things that do not exist yet, and land with the part that creates
them — exactly as the plan already says for the bridge check:

- **`Σ InstallmentPayment.Amount` vs `Σ Installment.AmountPaid`**, and the "new way" column of the monthly
  figure → **Part E**, which creates the ledger. Part A records `Σ Installment.AmountPaid` and the *current*
  monthly attribution, which is precisely the baseline Part E must reproduce (AC-24).
- **Bridged invoices carrying un-transferred plan money** → **Part F** (already stated in plan § Part A).

### Open question for the user (not blocking)

`reconcile-money` is **Local-mode only**, faithfully following the plan and the two existing console verbs.
But unlike those two, nothing about this report is Local-specific — it only reads money — and **Cloud also
applies migrations at startup**, so a Cloud operator currently has no way to verify a money migration. Say the
word and the mode guard comes out; it is a three-line change and the report is strictly read-only.

## Part B — notes

**Quality gate:** backend build 0 errors, 0 warnings in changed files; `tsc --noEmit` clean; `npm run build`
clean. 94 tests pass — 22 new for Part B plus every suite touched by the repository signature change.

**M1 + M2 landed as one migration** (`20260727145139_FixPatientAppointmentDeleteBehaviorAndAddArchive`). The
plan listed them separately; EF naturally produces one file, and one atomic change is better than two here.
Verified content: `DropForeignKey` + `AddForeignKey … ReferentialAction.SetNull` on
`FK_Appointments_Patients_PatientId` (the A1 fix, at the physical-constraint level), the three archive columns
with `defaultValue: false`, and `IX_Patients_ClinicId` correctly folded into `IX_Patients_ClinicId_IsArchived`.

### The call-site audit the plan asked for — the compiler found all ten

Adding `includeArchived` to `GetByClinicIdAsync` broke every caller, which was the point. Two of them are
**name lookups, not pickers**, and had to keep seeing archived patients:

| Call site | Decision | Why |
|---|---|---|
| `GetPatientsQuery` | exclude | Backs the patients page *and* the header search. |
| `GetPatientsToRecallQuery` | exclude | Relancing someone the clinic archived is exactly what archiving stops. |
| `GetInvoicesQuery` | **include** | Resolves names. An archived patient's invoices must still show whose they are. |
| `GetTreatmentPlansQuery` | **include** | Same, for devis. |
| `AIActionService` ×5 | exclude | The assistant must not find what the UI's own search hides. |
| `GoogleCalendarSyncService` | **include** | Critical: the next line **auto-creates a placeholder patient**. Excluding archived here would silently produce a DUPLICATE record for someone the clinic already has. |

### Auto-approved deviations

| Deviation | Classification | Reason |
|-----------|----------------|--------|
| Added `Patient.ArchiveReason` (+ column), which the plan's M2 column list omitted | Trivial | The plan itself specifies `Archive(reason)` and the spec's API contract takes `reason?`. Without a column to store it the parameter is inert. |
| Created `PatientMappingExtensions` and moved the 4 inline Patient→DTO mappings onto it | Trivial — internal, same output | The archive flag has to appear on the list, the detail and both write paths; four hand-maintained copies would have drifted immediately. Matches the co-located-static-helper convention (`InvoiceMappingExtensions`, …) and collapses the biggest single cost in Part I. |
| `GetLinkedDataCountsAsync` issues 15 `CountAsync` calls rather than one composed query | Trivial | Runs once, on a dialog open. Legible and obviously correct beats a hand-rolled UNION; each count names exactly what it guards. |
| Junctioned the worktree's `web/node_modules` to the main checkout's | Trivial — tooling only, nothing committed | `package.json` and `package-lock.json` are byte-identical between the two trees, so the dependency graph is the same. Avoids a multi-minute install to run a type check. |

## Significant deviations

_None yet._

## Learnings

### A repository signature change is the cheapest possible call-site audit
Adding `includeArchived` as a *positional* parameter before `CancellationToken` deliberately broke all ten
callers at compile time. Had it been added last with a default, every call site would have silently kept the old
behaviour — and the two name-lookup sites (`GetInvoicesQuery`, `GetTreatmentPlansQuery`) plus the Google-sync
duplicate-creation hazard would have shipped unnoticed. Prefer the breaking shape when the audit *is* the work.

### The push credential is account-scoped and the gh CLI has the wrong account
`git push` fails headlessly with `could not read Password`. `gh auth status` reports **`o-benkhalifa`**, which
gets a **404** on `oumayma-404/clinic-management` — no access. The working credential is a separate Windows
Credential Manager entry, target **`git:https://oumayma-404@github.com`**, read via `advapi32!CredRead` and fed
to git through a temporary `GIT_ASKPASS` script. A plain `git credential fill` **does not** find it, because the
generic `github.com` lookup resolves to the other account first.
