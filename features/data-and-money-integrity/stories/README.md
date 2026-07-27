# Stories — Data & Money Integrity

**Spec:** [../spec.md](../spec.md) (APPROVED) · **Plan:** [../plan.md](../plan.md) (APPROVED)
**Branch:** `feature/data-and-money-integrity` (worktree, branched from `22b37a1`)

## Story tracker

| # | Story | Status |
|---|-------|--------|
| 1 | [Correct the eight data-loss and money defects, end to end](./story-1-data-and-money-integrity.md) | in progress |

One story by explicit user decision (plan **R-1**). It is delivered as **eleven ordered parts**; each is a vertical
increment ending at a clean build gate, and each is committed and pushed on completion.

## Part tracker

| Part | Name | Migrations | Status |
|------|------|-----------|--------|
| A | Réconciliation report | — | **complete** |
| B | Patient delete blocks + archive | M1+M2 | **complete** |
| C | Appointment update stops wiping the act | — | **complete** |
| D | Void a payment + invoice detail modal | AddPaymentVoid | **complete** |
| E | Installment ledger + plan void + receipts | M3 | pending |
| F | Devis→facture carry-over | — | pending |
| G | Avoirs readable + PDF + netting | — | pending |
| H | Patient contact optional | M4 | pending |
| I | Conflict detection — backend | M5 | pending |
| J | Conflict detection — frontend | — | pending |
| K | Documentation | — | pending |

## Ordering constraints (from the plan — do not reorder)

1. **Part A ships first.** The reconciliation report is the instrument that proves Parts E, F and H moved no closed
   month. Running it before the first migration produces the required baseline.
2. **Part H's null-safe code deploys before Part H's blanking migration.** In Local mode migrations run *after*
   Kestrel is serving, so blanking first takes patient search down for the clinic.
3. **Do not stop mid-part in D, E, F or H** — the money model is briefly half-migrated inside each.

## Base-ref note

This worktree branches from **`22b37a1`**, not `origin/main`. `main` is **138 commits behind**; branching from it
would drop the entire billing subsystem this work builds on.
