# Next session — resume prompt

Copy the fenced block below into a fresh Claude Code session.

---

```
/next audit-sections-3-to-10

FINISH PART P4. Its schema half is done, applied and verified; what remains is pure code
with NO migration. Read stories/progress.md's latest session log first — the "Still open
in P4" table is the exact work list. Do not re-derive it, and do not re-touch the schema.

P1, P2, P3 are COMPLETE. `verify-schema` (P4's gate) landed in `edecd23`. P4's schema
batch landed in the commit after it and `verify-schema` now exits 0.

STILL OPEN IN P4 — six items, none touching schema:
 1. Restock dialog captures expiry + batch number (AC-P4.2). The API already accepts both
    (`stockApi.restock` takes expiryDate/batchNumber/reason) and the model stores them per
    lot — only the two dialog inputs are missing. `stock-table.tsx` has the adjust dialog
    (`openAdjust(item, "restock")`).
 2. Approaching-expiry notification (AC-P4.6). `Clinic.StockExpiryLeadDays` (default 30),
    `StockItem.HasStockExpiringSoon` and `StockItemDto.isExpiringSoon` all exist, and the
    stock table already highlights it — nothing GENERATES the notification. Follow the
    edge-triggered low-stock precedent in `INotificationGenerator`.
 3. Material-list editor in the act catalog (AC-P4.14). `ProcedureType.SetMaterials` and
    the `ProcedureTypeMaterials` table exist; there is no command and no admin UI, so a
    list can currently only be seeded directly into the database. Needs a command + the
    procedure-type form modal. Admin-only, and remember R-14 (`AdminSurfaceCoverageTests`
    is a hardcoded array — add any new admin surface by hand).
 4. STEP 5 — rewrite `RealtimeResourceResolverTests` into a reflection-derived exact-set
    contract test that also parses `clinic-hub.ts` and asserts BOTH directions, with a
    named allow-list constant for intentional emit-only / listen-only keys
    (AC-P4.23–4.25). `verify-schema` is now the worked example of doing this right:
    derive from the authoritative source, never hand-maintain the list.
    Emitted keys today: appointments, clinics, cnamnomenclature, dentalacts, doctors,
    documents, expenses, files, invoices, laborders, medications, notifications, patients,
    proceduretypes, recall, stock, treatmentplans, users, waitinglist.
    `Features/AISummary/Commands` is an EMPTY folder — no IRequest, so it must not appear.
 5. STEP 6 — wire the orphans (AC-P4.20–4.22, 4.26). Missing from `clinic-hub.ts`:
    doctors, laborders, recall, waitinglist. Declared with NO subscriber: documents
    (A-15) — give it one or remove it. Pages still needing subscriptions:
    /waiting-list, /lab-orders, /recalls, /recurring-series, « Mon profil ».
    /caisse, the dashboard and /creances already have array-form ones.
 6. STEP 11 — bound the recall query (AC-P4.41–4.43). Push the date bounds to SQL;
    identical results before and after (a behaviour change is a defect); archived patients
    stay excluded. `MoneyReadConsistencyTests.Wire()` hand-mirrors repository SQL, so
    mirror any repository filter change into it or the suite passes against the old rule.

BASELINES (trust progress.md, not spec.md/plan.md):
- Backend warnings: 56, all pre-existing outside changed files.
- Full unit suite: 1275 passed / 0 failed. FULLY GREEN — any red is ours.
- Frontend: tsc clean, `npm run build` 27/27. `npm run lint` CANNOT run until P5 step 1.
- SAC not blocking. Use the OutDir + vstest workaround.

DO NOT REGRESS THE SCHEMA GATE. Before committing, re-run:
    cd api/ClinicManagement.API
    ASPNETCORE_ENVIRONMENT=Development dotnet run --no-launch-profile -- verify-schema
It must stay **exit 0**. It is model-driven, so if you add an index or FK in a
configuration it will be checked automatically — and if you add one without a migration it
will (correctly) fail.

Commit as the second half of P4, flipping the P4 row to complete. Stage explicitly by
path — never `git add -A`. No broad find/replace whose left-hand side is shorter than the
construct it belongs to.
```

---

## Open items carried forward (not P4's, but do not lose them)

| Item | Where it belongs | Note |
|---|---|---|
| **A human pass at 375 px and by keyboard** | before `/review-feature` | P3's walk (AC-P3.48) was **static** — markup-level only. Stated plainly in progress.md per plan risk **R-9**; it is not equivalent to a browser pass and there is no frontend test runner. |
| **Calendar row-trim** (AC-P1.33) | follow-up, not a part | The shading is correct per-day, but the grid still renders rows 0..23. Re-basing needs the appointment overlay reworked (it positions blocks from midnight against a fixed `HOUR_HEIGHT`). Deferred deliberately in P1. |
| **`npm run lint`** | P5 step 1 | Cannot run at all today — ESLint is not a dependency and `next.config.ts` disables lint during build. P3 and P4 substituted an ad-hoc unused-import scan. |
| **Wide tables + the calendar grid at 375 px** | Out of Scope (spec) | They squeeze rather than overflow, so AC-P3.14 holds (the *body* never scrolls). A real responsive pass over them is explicitly excluded. |
| **P8's Q-1 … Q-6** | blocks P8's start | Q-3 is decisive: `follow-up/cnam-conventionné-bordereau.md` records the bordereau as a conventionné / tiers-payant pathway, so if this clinic is filière privée, P8 has no user and should follow patient merge into `follow-up/`. |
| **A gate named in a plan must be proven to run** | any future part | `verify-schema` was cited as "the gate" by P1, P2 and P3 (two of them writing « not applicable ») while it did not exist. See the Learning in progress.md: *"not applicable" and "not implemented" look identical in a table.* |

---

## State at the end of the last session

**Branch:** `feature/audit-sections-3-to-10`, off `feature/windows-desktop-app` @ `1932acf`.

| Part | Status |
|---|---|
| **P1** Appointment lifecycle & booking | **complete**, migrations included (`6906f83`) |
| **P2** Finish what's built | **complete** (AC-P2.1–2.45) |
| **P3** UX, accessibility & French | **complete** (AC-P3.1–3.54) |
| **`verify-schema`** (P4's gate) | **complete** (`edecd23`) — model-driven; **exit 0** |
| **P4** Stock, realtime & schema | **partial** ← finish next. Steps 1, 3, 4, 7, 8, 9, 10 + step 2's backend/table + the migration are **done and verified**. Six code-only items remain (see the resume prompt) |
| P5 Build & tooling | not-started (its `TreatWarningsAsErrors` step is the LAST thing in the whole story) |
| P6 Money truth & timezone | not-started |
| P7 Audit trail, dup prevention, anonymize | not-started (P7a first; `confirm-by-typing-dialog.tsx` is already in place for AC-P3.47) |
| P8 CNAM claims & reconciliation | **blocked** on Q-1 … Q-6 |

**Closed so far: ~40 / 57 bullets**, plus adjacent defects A-1 … A-18.

**Gates at the end of the session:** backend build 0 errors / 56 warnings / 0 in changed files ·
full unit suite **1275 passed, 0 failed** (+38) · **`verify-schema` exit 0** (was exit 2 — the § 9.5
`StockItem.UnitPrice` defect is closed) · **`reconcile-money` exit 0, byte-identical** to the pre-session
baseline · `tsc --noEmit` clean · `npm run build` clean at 27/27.
