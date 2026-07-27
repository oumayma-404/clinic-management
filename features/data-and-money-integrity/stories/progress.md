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
| — | Scaffold (stories, progress) | complete | — | — |
| A | Réconciliation report | pending | | |
| B | Patient delete blocks + archive | pending | | |
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

## Significant deviations

_None yet._

## Learnings

_None yet._
