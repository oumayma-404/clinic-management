# Story 1 — Correct the eight data-loss and money defects, end to end

**Status:** in progress
**Spec:** [../spec.md](../spec.md) · **Plan:** [../plan.md](../plan.md) (US-1)

## Objective

**As** a clinic using this app daily,
**I want** records that cannot be silently destroyed, money that can be corrected, and edits that cannot overwrite
each other,
**so that** what the app tells me about a patient's balance is true and a mistake is recoverable.

Delivers all **77 acceptance criteria** in the spec, closing all eight § 1 findings of `CODEBASE_AUDIT_2026-07.md`
plus the adjacent defects that would otherwise re-break them.

## Entry criteria

- [x] `spec.md` APPROVED
- [x] `plan.md` APPROVED
- [x] Isolated worktree on `feature/data-and-money-integrity`, branched from `22b37a1`
- [x] API process stopped (required before every `dotnet ef migrations add`)
- [x] Docker (Postgres + MinIO) left running — migrations need the database up

## Parts

Each part is a vertical increment (domain → persistence → API → UI → tests), ends at a clean build gate, and is
committed **and pushed** on completion. Full step detail lives in [../plan.md](../plan.md); this file tracks status
only.

| Part | Name | Plan section |
|------|------|--------------|
| A | Réconciliation report | plan.md § Part A |
| B | Patient delete blocks + archive | plan.md § Part B |
| C | Appointment update stops wiping the act | plan.md § Part C |
| D | Void a payment + invoice detail modal | plan.md § Part D |
| E | Installment ledger + plan void + receipts | plan.md § Part E |
| F | Devis→facture carry-over | plan.md § Part F |
| G | Avoirs readable + PDF + netting | plan.md § Part G |
| H | Patient contact optional | plan.md § Part H |
| I | Conflict detection — backend | plan.md § Part I |
| J | Conflict detection — frontend | plan.md § Part J |
| K | Documentation | plan.md § Part K |

## Quality gate (every part)

| Check | Command | Requirement |
|-------|---------|-------------|
| Backend build | `dotnet build api/ClinicManagement.sln --no-incremental` | 0 errors; **0 new** warnings in changed files |
| Frontend types | `cd web && npx tsc --noEmit` | 0 errors |
| Frontend build | `cd web && npm run build` | clean |
| New/changed tests | `dotnet build -p:OutDir=<scratch>/utbuild/` then `dotnet vstest` | pass |

**Lint gate does not exist** — ESLint is not installed in `web/` and `next.config.ts` disables lint during builds.
`tsc --noEmit` + `npm run build` is the frontend gate (see `features/LEARNINGS.md`).

**`dotnet test` fails at assembly load with `0x800711C7`** (Windows Smart App Control, environmental). Use the
`dotnet build -p:OutDir=… ` + `dotnet vstest` workaround.

**A build failing only with `MSB3021`/`MSB3027`** is a file lock from a running API, not a compile error — stop the
process named in the message and rebuild.

## Verification

- After Parts A, E, F and H: run `reconcile-money` and diff against the Part A baseline. Every monthly « encaissé »
  figure must be unchanged (AC-24).
- After Part I: confirm the probe migration's `Up()` was empty before committing the concurrency migration.
