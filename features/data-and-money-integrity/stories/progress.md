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
| B | Patient delete blocks + archive | complete | `4b4a805` | yes |
| C | Appointment update stops wiping the act | complete | `f5d915e` | yes |
| D | Void a payment + invoice detail modal | complete | `d451c14` | yes |
| E | Installment ledger + plan void + receipts | complete | | |
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
| ~~Junctioned the worktree's `web/node_modules`~~ — **reverted in Part C, replaced with a real `npm ci`** | Trivial — tooling only, nothing committed | The junction type-checked fine but broke `npm run build`: `next.config.ts` sets `output: 'standalone'`, and the standalone trace step failed with `EPERM` trying to symlink the junction target. Since packaging ships the standalone output, that is a build artifact that actually matters — so the shortcut was removed and real dependencies installed. |

## Part C — notes

**Quality gate:** backend build 0 errors, 0 warnings in changed files; `tsc --noEmit` clean; `npm run build`
compiles clean and generates all 27 pages. 57 appointment tests pass.

### The fix was proven to actually catch the bug

Rather than assume the new tests were meaningful, the tri-state guard was **temporarily reverted** and the suite
re-run: **5 tests went red**, including `AppointmentSyncMappingTests.UpdateAppointment_Maps_IsSyncedToGoogle`,
which previously passed *only* because its fixture had no procedure type — it was pinning the data-loss defect
rather than the mapping it claims to cover. The guard was then restored and all 57 pass.

Both wipe triggers are covered: the edit dialog's cancel (`{ status: "cancelled" }` alone) and the AI
assistant's `cancel_appointment`, which constructs the command directly and bypasses the controller — which is
why the fix had to live on the command, not on a request DTO.

### Auto-approved deviations

| Deviation | Classification | Reason |
|-----------|----------------|--------|
| The invalid-status refusal was added as an early return rather than restructuring the `switch` | Trivial | Smallest possible diff; the existing nesting and every case are untouched. |
| `UNASSIGNED_DOCTOR = "__unassigned__"` sentinel in the edit dialog | Trivial | Radix `Select` cannot hold `value=""`. Mirrors the `"all"` sentinel already used on the appointments page. |
| Test colour changed from `#2563EB` to `#4F83CC` | Trivial | `ColorHex` validates against a curated palette and rejected the first value. Caught by the test run, not shipped. |

## Part D — notes

**Quality gate:** backend build 0 errors, 0 warnings in changed files; `tsc --noEmit` clean; `npm run build`
compiles clean, 27/27 pages. 34 tests pass — 18 new plus the existing `InvoiceEntityTests` and
`MoneyReadConsistencyTests`, which the `Cancel` guard change could have broken and did not.

**Migration:** `20260727175009_AddPaymentVoid` — six columns on `Payments` plus a partial index
`(InvoiceId, PaidOn) WHERE NOT IsVoided`, so the filtered cash read stays cheap as voids accumulate.

### Design points worth keeping in mind

- **`AmountCollected` is recomputed, never decremented.** It is a stored column while the caisse sums the
  payment rows, and nothing has ever reconciled the two — a decrement would entrench any existing drift.
  Recomputing from the live rows makes the arithmetic unfalsifiable, and the payments are always loaded with
  the invoice anyway.
- **The read-side half is the one that matters.** `GetCollectedBetweenAsync` now filters `!IsVoided`. Without
  it the caisse, dashboard and revenue KPI would over-report by the voided amount **forever** — the write side
  alone would have been worse than not shipping.
- **The void is retroactive to the original payment date**, so the day the money was recorded self-corrects.
  A void says the money was never received; booking a reversal on the void date would invent a cash movement
  that never happened.
- **`CanCancel` / `CanCreateAvoir` moved to the server.** The frontend re-derived both from
  `status + amountCollected`, which is precisely how it would now offer « Annuler » on an invoice the API
  refuses: after a full void the status is `Issued` and collected is `0`, but the voided rows are still there.
- **A fully-voided invoice becomes cancellable again** (the guard counts *live* payments). Without that
  change, keeping voided rows would have made such an invoice permanently un-cancellable — and a bridged plan
  whose invoice cannot be cancelled can never be amended or re-billed either.
- **`Cancel` now refuses a TTN-registered invoice.** Cancelling locally what the national registry still holds
  would put the clinic's books and El Fatoora permanently out of step with no trace on either side.

### Auto-approved deviations

| Deviation | Classification | Reason |
|-----------|----------------|--------|
| Part D got its **own** migration rather than folding the `Payments` columns into Part E's M3, as the plan said | Trivial | Each part stays independently deployable, which is the entire point of the part structure. The plan's actual ordering constraint — table-creating migration before the concurrency one — still holds: M3 (the `InstallmentPayments` table) lands in Part E and the xmin migration is last. |
| Extracted `PaymentDateRules` instead of inlining the date guard | Trivial | The identical rule is needed by the installment payment (Part E) and the avoir's `RefundedOn` (Part G). One implementation beats three. |
| `SourceInstallmentPaymentId` landed now, though nothing populates it until Part F | Trivial | A nullable soft-link column with no FK, so it carries no dependency on the not-yet-existing `InstallmentPayment`. Adding it now avoids a second `Payments` migration in Part F. |
| Added `canReverseFinancials` in a new `lib/auth/can.ts` | Trivial | Every existing client-side gate compares against `"admin"` alone, so **a doctor — the primary user — was denied by all of them**. Shared so the four reversal actions stop disagreeing with each other. |

## Part E — notes

**Quality gate:** backend build 0 errors, 0 warnings in changed files; `tsc --noEmit` clean; `npm run build`
27/27 pages. **875 pass / 8 fail** — see the baseline note below. The 87 tests across the new ledger suite and
every money/authorization guard suite all pass.

**Migration:** `20260727181433_AddInstallmentPaymentLedger` — the `InstallmentPayments` table, three indexes
(FK, a partial `PaidOn WHERE NOT IsVoided`, and `Installments.DueDate`, which never had one), plus a
hand-written backfill.

### ⚠️ Pre-existing test-failure baseline — VERIFIED, not caused by this work

The full suite reports **8 failures**. I checked out `22b37a1` into a throwaway worktree, built it untouched,
and ran the same suite: **the failure sets are identical**.

- `DoctorCachetTests` ×4 (2 facts + a 2-case theory)
- `ReminderSchedulerTests` ×3
- `DocumentTypeAndFilenameTests.Create_With_Supported_Type_Passes_The_Type_Guard` ×1

None touch anything this feature changes. They are recorded here rather than fixed — repairing unrelated
pre-existing failures is out of scope, and silently "fixing" them would hide whatever regressed them earlier.
**Any 9th failure in a later part is mine.**

### What actually fixes the wrong-month bug

`GetInstallmentCollectedBetweenAsync` now sums **ledger rows on their own dates**. It used to key the whole
cumulative `AmountPaid` off the single `LastPaidOn`, so 400 DT in January and 600 in February reported 0 then
1000 — and January's already-published figure **changed retroactively** when February's payment landed. The
invoice side was always event-sourced and correct; this mirrors it.

The traversal stays rooted at the clinic-filtered `TreatmentPlans` set and reaches the ledger by `SelectMany`.
**That traversal is the tenant scoping** for a grandchild with no `ClinicId`, no `DbSet` and no query filter.

### Design points

- **`AmountPaid`, `LastMethod` and `LastPaidOn` are kept but become derived.** Thirteen read sites depend on
  them. They are recomputed from the live ledger after every record and void, so the two can never drift.
  Consequence worth knowing: **`AmountPaid` is no longer monotonic**, and both `Installment.Revise` and the
  plan amendment rules key off it.
- **The plan's status is NOT walked back on a void**, unlike an invoice's. A plan's status tracks clinical
  progress — « Terminé » means every act is done, not that it is paid — so correcting a payment must not
  un-complete a treatment.
- **Receipts are per-payment.** The route gained a `paymentId` and the query prints *that* payment. It used to
  print the cumulative `AmountPaid` dated `LastPaidOn`, so a second partial payment silently reissued a
  receipt for the running total. Both receipts (invoice and installment) now also print the balance **as of
  that payment** rather than the live one — previously a reprint of the first of two receipts showed a figure
  that never applied, and after a void it would have shown a balance that had *grown*.
- **The backfill reproduces today's figures exactly** (AC-24): one row per already-paid installment, the
  cumulative amount, dated `LastPaidOn` — precisely what every cash read attributes today. It deliberately
  does **not** retro-fix the attribution, because the information to do so was never stored. The ledger fixes
  attribution from its first day forward, and `reconcile-money` now reports both computations side by side so
  a before/after run proves nothing moved.
- **Idempotent by `WHERE NOT EXISTS`** — not optional: in Local mode migrations run fire-and-forget *after*
  Kestrel is serving and a throw calls `StopApplication()`, so a non-idempotent backfill that re-ran would
  take the whole app down.
- **`COALESCE` on the backfill date.** `AmountPaid > 0` with a NULL `LastPaidOn` is unreachable through the
  domain but possible in the data, and `PaidOn` is `NOT NULL` — a bare insert would abort the entire
  migration.

### Auto-approved deviations

| Deviation | Classification | Reason |
|-----------|----------------|--------|
| `RecordInstallmentPayment` now returns the created `InstallmentPayment` | Trivial | The caller needs the new row's id to offer its receipt. Previously it returned void and the modal could only reference the échéance. |
| Added `Installments.DueDate` index in this migration | Trivial | « Créances » pulls every open installment per clinic and there was no date index at all; the plan lists it under M3 anyway. |
| `Installment.ResyncFromLedger()` (internal) | Trivial | Lets the verification pass rebuild the denormalizations from EF-loaded rows, which bypass the domain methods. Internal, no public surface. |

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
