# Stories — Data & Money Integrity

**Spec:** [../spec.md](../spec.md) (APPROVED) · **Plan:** [../plan.md](../plan.md) (APPROVED)
**Branch:** `feature/data-and-money-integrity` (worktree, branched from `22b37a1`)

## Story tracker

| # | Story | Status |
|---|-------|--------|
| 1 | [Correct the eight data-loss and money defects, end to end](./story-1-data-and-money-integrity.md) | **complete** |

One story by explicit user decision (plan **R-1**). It is delivered as **eleven ordered parts**; each is a vertical
increment ending at a clean build gate, and each is committed and pushed on completion.

## Part tracker

| Part | Name | Migrations | Status |
|------|------|-----------|--------|
| A | Réconciliation report | — | **complete** |
| B | Patient delete blocks + archive | M1+M2 | **complete** |
| C | Appointment update stops wiping the act | — | **complete** |
| D | Void a payment + invoice detail modal | AddPaymentVoid | **complete** |
| E | Installment ledger + plan void + receipts | M3 | **complete** |
| F | Devis→facture carry-over | — | **complete** |
| G | Avoirs readable + PDF + netting | — | **complete** |
| H | Patient contact optional | M4 | **complete** |
| I | Conflict detection — backend | M5 (snapshot-only) | **complete** |
| J | Conflict detection — frontend | — | **complete** |
| K | Documentation | — | **complete** |

## Ordering constraints (from the plan — do not reorder)

1. **Part A ships first.** The reconciliation report is the instrument that proves Parts E, F and H moved no closed
   month. Running it before the first migration produces the required baseline.
2. **Part H's null-safe code deploys before Part H's blanking migration.** In Local mode migrations run *after*
   Kestrel is serving, so blanking first takes patient search down for the clinic.
3. **Do not stop mid-part in D, E, F or H** — the money model is briefly half-migrated inside each.

> **Closed.** All eleven parts landed; 938 backend tests pass, `tsc` and `npm run build` are clean. One
> deviation from the plan is worth carrying forward: **M5 is not the empty no-op the plan assumed**. EF emits
> 38 × `AddColumn("xmin")`, which PostgreSQL rejects, so `AddConcurrencyToken` ships with a hand-emptied `Up()`
> and is kept purely for its model snapshot.

## Base-ref note

This worktree branches from **`22b37a1`**, not `origin/main`. `main` is **138 commits behind**; branching from it
would drop the entire billing subsystem this work builds on.
