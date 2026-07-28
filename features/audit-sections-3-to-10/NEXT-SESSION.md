# Next session — resume prompt

Copy the fenced block below into a fresh Claude Code session.

---

```
/next audit-sections-3-to-10

Continue Story 1 with PART P4 (Stock, realtime & schema — §§ 6.6, 6.7, 6.12, 9.1–9.6,
ACs AC-P4.1–4.43). Read stories/progress.md FIRST for the resume state and the
corrected baselines; read plan.md's "Part P4" for the eleven steps — do not re-derive
them.

P1, P2 and P3 are COMPLETE (34 / 57 bullets). P4–P7 are independent of each other;
P8 is still blocked on Q-1 … Q-6.

CORRECTED BASELINES (spec.md and plan.md record stale ones — trust progress.md):
- Backend warnings: 56, all pre-existing CS8618/CS8602 outside changed files.
- Full unit suite: 1237 passed / 0 failed. FULLY GREEN — any red is ours.
- Smart App Control is NOT currently blocking the suite (it did earlier the same
  day). Use the OutDir + vstest workaround; if it blocks, clean bin/+obj/ and run
  `dotnet build-server shutdown` before concluding anything.

P4 IS THE MIGRATION-HEAVY PART — 11 migrations and the first schema change since P1:
- `verify-schema` NOW EXISTS — it was built last session, because it was a P1 step that
  had silently never landed. Run it before and after the batch and diff:
    cd api/ClinicManagement.API
    ASPNETCORE_ENVIRONMENT=Development dotnet run --no-launch-profile -- verify-schema
  Its CURRENT state is exit 2 with exactly 2 drift lines, both the SAME real defect:
  `StockItems.UnitPrice` is `(18,2)` in the database and `numeric(18,2)` in the model.
  That is § 9.5 / AC-P4.36, fixed by step 10 — so after P4 it must exit 0.
- It is MODEL-DRIVEN: it reads the EF model's declared indexes / FKs / decimal
  precisions and diffs them against PostgreSQL's catalog. P4's five new indexes and two
  new FKs are therefore verified automatically once their configurations exist — do NOT
  add them to any list. Only things the model cannot express are named in the service
  (btree_gist, the exclusion constraint's partiality, the two (5,2) rate exceptions, the
  backfill row counts).
- Its backfill counts are nullable and guarded on the table/column existing, so
  `stock-batch-backfill` currently reports "not applicable". Once P4's StockBatches table
  lands it becomes a real check — if it then reports items with a legacy expiry and NO
  opening batch, the backfill dropped their dates (AC-P4.8).
- `dotnet run -- reconcile-money` before and after the batch, and diff (root CLAUDE.md).
- Read each generated migration for stray `xmin` columns — EF's differ emits 38 of
  them and PostgreSQL rejects the lot (see the AddConcurrencyToken note in the root
  CLAUDE.md).
- Generate with the GLOBAL EF 10 tool only. A local 8.0.11 emitted a spurious
  `AddColumn("TokenVersion")`; so did EF 10, so read the diff either way.

STEPS 1-4 HAVE DESIGN PRE-WORK ALREADY DONE — do not re-derive it. See progress.md's
"P4 design pre-work" section; the reverted code is in the scratchpad under `p4-prework/`
(StockBatch.cs, ProcedureTypeMaterial.cs, StockItem.cs, two configurations, and
p4-model-edits.patch). The three findings that matter:
- The material list can ONLY hang off `ProcedureType`. `DentalRecordAct` has a nullable
  `ProcedureTypeId` and NO `DentalActCodeId`, so a list on `DentalActCode` could never be
  consumed on fiche save (AC-P4.10) — a second uncallable capability, the class P2 removed.
- `SetCurrentStock` must return the signed delta (that is WHY its caller wrote no
  movement), and must reconcile the batch rows or a stock-take desyncs lots from on-hand.
- The migration's opening batch is already counted in `CurrentStock`, so it cannot go
  through `AddStock` (which increments) — hence a separate `AttachExistingBatch`.

WATCH OUT:
- Step 5 REWRITES `RealtimeResourceResolverTests` from a hardcoded list to a
  reflection-derived set. That test is what stops P7/P8's new areas broadcasting
  silently, so it lands before their commands, not after. `verify-schema` is now the
  worked example of doing this right — read the model/catalog, do not hand-maintain a list.
- `AdminSurfaceCoverageTests` is a hardcoded array — a new admin surface is silently
  ungated. Add it by hand.
- `MoneyReadConsistencyTests.Wire()` hand-mirrors repository SQL. Step 11 changes the
  recall query's bounds; mirror any repository filter change into it or the suite
  passes against the old rule.
- Step 10 deletes 26 `HasColumnType` calls across 18 files. The convention alone is a
  no-op — that was corrected in the spec during planning (AC-P4.37).
- There is no frontend test runner. The FE gate is `npx tsc --noEmit` +
  `npm run build` (expect 27/27 pages). `npm run lint` CANNOT run — ESLint is not
  installed until P5 step 1.

Commit P4 as one commit at the part boundary. Stage explicitly by path — never
`git add -A`.
```

---

## Open items carried forward (not P4's, but do not lose them)

| Item | Where it belongs | Note |
|---|---|---|
| **A human pass at 375 px and by keyboard** | before `/review-feature` | P3's walk (AC-P3.48) was **static** — markup-level only. Stated plainly in progress.md per plan risk **R-9**; it is not equivalent to a browser pass and there is no frontend test runner. |
| **Calendar row-trim** (AC-P1.33) | follow-up, not a part | The shading is correct per-day, but the grid still renders rows 0..23. Re-basing needs the appointment overlay reworked (it positions blocks from midnight against a fixed `HOUR_HEIGHT`). Deferred deliberately in P1. |
| **`npm run lint`** | P5 step 1 | Cannot run at all today — ESLint is not a dependency and `next.config.ts` disables lint during build. P3 substituted an ad-hoc unused-import scan. |
| **Wide tables + the calendar grid at 375 px** | Out of Scope (spec) | They squeeze rather than overflow, so AC-P3.14 holds (the *body* never scrolls). A real responsive pass over them is explicitly excluded. |
| **P8's Q-1 … Q-6** | blocks P8's start | Q-3 is decisive: `follow-up/cnam-conventionné-bordereau.md` records the bordereau as a conventionné / tiers-payant pathway, so if this clinic is filière privée, P8 has no user and should follow patient merge into `follow-up/`. |
| **`verify-schema`'s 2 open drift lines** | **P4 step 10** | `StockItems.UnitPrice` is `(18,2)`. Not a new finding — it is § 9.5 / AC-P4.36 — but the verb now reports it, so P4 is not done until `verify-schema` exits **0**. |
| **A gate named in a plan must be proven to run** | any future part | `verify-schema` was referenced as "the gate" by P1, P2 and P3 (two of them writing « not applicable ») while it did not exist. See the new Learning in progress.md: *"not applicable" and "not implemented" look identical in a table.* |

---

## State at the end of the last session

**Branch:** `feature/audit-sections-3-to-10`, off `feature/windows-desktop-app` @ `1932acf`.

| Part | Status |
|---|---|
| **P1** Appointment lifecycle & booking | **complete**, migrations included (`6906f83`) |
| **P2** Finish what's built | **complete** (AC-P2.1–2.45) |
| **P3** UX, accessibility & French | **complete** (AC-P3.1–3.54) |
| **Its gate, `verify-schema`** | **built** — a carried-forward P1 step that had silently never landed. Model-driven; 167 ok / 2 drift (the real § 9.5 defect), exit 2 |
| P4 Stock, realtime & schema | not-started ← **next**, now against a working gate. Steps 1–4 have design pre-work recorded |
| P5 Build & tooling | not-started (its `TreatWarningsAsErrors` step is the LAST thing in the whole story) |
| P6 Money truth & timezone | not-started |
| P7 Audit trail, dup prevention, anonymize | not-started (P7a first; `confirm-by-typing-dialog.tsx` is already in place for AC-P3.47) |
| P8 CNAM claims & reconciliation | **blocked** on Q-1 … Q-6 |

**Closed so far: 34 / 57 bullets · ~180 / 301 ACs**, plus adjacent defects A-1 … A-14.

**Gates at the end of the session:** backend build 0 errors / 56 warnings / 0 in changed files ·
full unit suite **1237 passed, 0 failed** (+28) · `verify-schema` runs (167 ok / 2 drift, exit 2 — the § 9.5
defect P4 fixes) · `reconcile-money` exit 0, byte-identical to the pre-session run · frontend untouched, so no
`tsc`/`build` run was needed.
