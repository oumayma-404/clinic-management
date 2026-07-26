# Progress: Tooth-First Dental Record Entry

**Started:** 2026-07-26
**Type:** Small (forced — see DEV-1)
**Branch:** feature/windows-desktop-app (existing; matches the branch `treatment-plan-workspace` declares)

## Status
- [x] Implementation (S1 + S2 + S3)
- [x] Quality checks (backend build, `tsc --noEmit`, `next build`)
- [ ] Tests (handled by `/test-small-feature`)

## Working tree note (start of session)
The tree held **111 uncommitted files** of unrelated in-flight work (8 `adoption-qa-*` feature folders,
`treatment-plan-workspace`, new `CreditNote`/`StockMovement` entities + 3 uncommitted migrations, doc edits).
Nothing was staged or committed by this session.

**Mid-session the user committed concurrently** — `f510f93 chore: baseline in-flight work before
treatment-plan-workspace` swept the whole tree, so this feature's S1/S2 edits are inside that commit rather
than isolated. Only `web/components/patient-record-modal.tsx` + `web/components/record/` remained uncommitted
afterwards. Flagging it because the feature is no longer reviewable as a single diff.

Also landed concurrently: `feef4d8 fix(procedure-types): generalize the prefilled procedure catalog (43 -> 19
rows)`. Re-verified against the new seed — it is still category-driven, `Category` still occupies the
`ProcedureType` ctor's `description` slot, and `CategoryResultingConditions` is unchanged, so both design
assumptions hold. The per-tooth default is now right for **17 of 19** rows (misses `Facette` and
`Soin dentaire enfant` — per-tooth but stateless). The spec's stale "36 of 42" line was corrected.

## Files Changed

### S1 — backend: pricing model, mixed dentition, money precision
- `api/ClinicManagement.Domain/Entities/DentalRecordAct.cs` — `UnitCost` + `IsPerTooth`; ctor now takes a
  `DentalRecordActInput`; `IsPerTooth` forced false with no teeth; new `DentalRecordActInput` record (file scope,
  mirroring `InvoiceTotals` in `InvoiceCalculator.cs`).
- `api/ClinicManagement.Domain/Entities/DentalRecord.cs` — `SetActs(IEnumerable<DentalRecordActInput>)`; comment
  pinning that act ids are regenerated per update so nothing may FK them.
- `api/ClinicManagement.Application/Features/Patients/DentalRecordActParser.cs` — dropped the `isAdultTeeth`
  parameter and the single-dentition rejection (now `FdiTooth.IsValid` per tooth); returns
  `List<DentalRecordActInput>`; removed the redundant `ParsedAct` pairing record; added negative cost / unit-cost
  guards on the `Result` path.
- `api/.../Commands/CreateDentalRecordCommand.cs`, `UpdateDentalRecordCommand.cs` — new parser/`SetActs` calls
  (both call sites collapsed to one line); dropped a now-unused `using`.
- `api/ClinicManagement.Application/DTOs/DentalRecordDto.cs` — `unitCost`/`isPerTooth` on `DentalRecordActDto`
  and `DentalActInput`.
- `api/.../Features/Patients/DentalRecordMappingExtensions.cs` — maps the two new fields.
- `api/.../Configurations/DentalRecordActConfiguration.cs` — `UnitCost decimal(18,3)`, `IsPerTooth` required.
- `api/.../Configurations/DentalRecordConfiguration.cs` — `Cost`/`AmountPaid` `decimal(18,2)` → `(18,3)`.
- `api/.../Configurations/ProcedureTypeConfiguration.cs` — `DefaultCost` `decimal(18,2)` → `(18,3)`.
- `api/.../Migrations/20260726200110_ToothFirstRecordPricing.{cs,Designer.cs}` + `ApplicationDbContextModelSnapshot.cs`
  — 2 `AddColumn` + 3 `AlterColumn`. **Hand-authored — see DEV-4.**
- `web/lib/api/types.ts` — `unitCost`/`isPerTooth` on both act shapes.
- `web/app/patients/[id]/page.tsx` — invoice bridge: a per-tooth act now presets `quantity = teeth`,
  `unitPriceHt = unitCost`, and the designation names the teeth.

### S2 — correctness fixes
- `web/lib/format.ts` — new `roundMillimes()` (away-from-zero, mirrors `InvoiceCalculator.RoundMoney`).
- `web/components/patient-summary-modal.tsx` — partitions worked teeth by **tooth FDI range** instead of
  `record.isAdultTeeth` (a mixed record previously lost half its teeth from one chart); two near-identical
  `useMemo`s collapsed into one.
- `web/components/tooth-multiselect.tsx` — exports `isAdultTooth(n)`.
- (`amountPaid` dirty flag, NaN-cost guard, non-destructive dentition toggle and `showErrorToast` live in the
  rewritten modal — see A1.)

### S3 — tooth-first UI
- `web/components/record/use-session-acts.ts` (new) — the session reducer.
- `web/components/record/session-act-composer.tsx` (new) — selection → procedure → `Entrée`.
- `web/components/record/session-acts-list.tsx` (new) — grouped by tooth + « Actes généraux ».
- `web/components/record-tooth-chart.tsx` — `ToothPaint.focused` → `selected`; added `existingColor` /
  `existingIsDiagnosis` painted as the tooth outline (dashed for a diagnosis).
- `web/components/patient-record-modal.tsx` — rewritten as a two-pane orchestrator. Props unchanged.

### Docs
- `web/components/CLAUDE.md`, `api/ClinicManagement.Domain/CLAUDE.md` — kept in sync per the root guide's rule.
- `features/tooth-first-record-entry/spec.md` — corrected the stale seeded-procedure count.

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| A1 — S2's modal-internal fixes (`amountPaid` dirty flag, NaN guard, non-destructive dentition toggle, `showErrorToast`) implemented directly in the S3 rewrite instead of first in the old layout | Same end state; S3 deletes the old layout, so applying them twice would be thrown-away work. Identical lines either way. |
| A2 — `DentalRecordActInput` declared at file scope in `DentalRecordAct.cs`, not a new file | Mirrors the repo's existing precedent (`InvoiceTotals` lives in `InvoiceCalculator.cs`). Spec allowed `Domain/Entities`. |
| A3 — `DentalRecordAct` ctor takes the input record rather than 11 positional args | Single caller, inside the same aggregate; 11 positional args is unreadable. Invariants stay in the ctor. |
| A4 — removed `DentalRecordActParser.ParsedAct` | `DentalRecordActInput` now carries the parsed condition, so the pairing record is redundant. Parser-local; grep confirmed zero external references. |
| A5 — negative `Cost`/`UnitCost` also validated in the parser | Same French messages as the entity guards, but on the `Result` path the layer prefers over exceptions. Same outcome. |
| A6 — dropped a now-unused `using ClinicManagement.Domain.Enums;` and an unused `cn` import | Dead imports; `noUnusedLocals` is off so the compiler stays silent. |
| A7 — selection shortcuts (Haut/Bas/Toute la bouche) live in the modal's left pane, not in `record-tooth-chart.tsx` | Keeps that component presentational as documented, so the read-only `patient-summary-modal` doesn't inherit selection chrome. |
| A8 — collapsed `patient-summary-modal`'s two duplicate `useMemo`s into one | The dentition-partitioning fix had to touch both anyway. |

## Significant Deviations

**DEV-1 — Forced small pipeline on a ~25-file feature. Approved.**
Targeted exploration put the real surface at S1 ≈ 17 files, S2 ≈ 3, S3 ≈ 6, past this skill's ~10-file envelope.
Surfaced with the concrete count plus the `treatment-plan-workspace` collision; the user chose **"Full spec now:
S1+S2+S3"** over S1+S2-only, S1-only, or holding. Not escalated to the full pipeline because it is one domain
with every decision already pinned by the spec.

**DEV-2 — Persisted the per-tooth pricing (`UnitCost`, `IsPerTooth`) instead of keeping it client-side. Approved.**
The spec's own challenge pass established that a client-only model makes edit-mode a trap (an act reopens as a
forfait, so adding a tooth silently fails to reprice) and leaves the invoice bridge unable to reconstruct
quantity × unit price. Costs one additive migration; nullable/false defaults make every legacy row read as a
flat fee, which is what it was.

**DEV-3 — No dedicated mixed-dentition chart. Approved.**
The Adulte/Enfant toggle became a pure view switch (acts on the other dentition persist, are listed, and are
reported by a chip; the selection clears on flip). 32 permanent vs 20 deciduous teeth don't align in columns, so
a third layout was the plan's highest visual risk for no functional gain — the acts list is the record.

**DEV-4 — Migration hand-authored; must be regenerated before merge.**
`dotnet ef migrations add` initially produced an **empty** migration: the API is running (PID 31192) and holds
its `bin`, so `--no-build` loaded a stale `Infrastructure.dll` (old model → empty diff), and rebuilding the API
project fails at the *copy* step with `MSB3021`/`MSB3027` (every project compiles — these are lock errors, not
compile errors). Per the skill I did not stop the running app. The empty migration was deleted (verified it had
not rewritten the snapshot, so the sibling `CreditNote`/`StockMovement` model work was untouched) and the
migration was hand-authored per the documented fallback:
- `Up`/`Down` written by hand (2 `AddColumn`, 3 `AlterColumn` — small enough to verify by eye).
- `.Designer.cs` derived **mechanically** from the updated snapshot (script-transformed: added the `Migrations`
  using, the `[Migration]` attribute, renamed the class, `BuildModel` → `BuildTargetModel`). Verified as
  snapshot + exactly 3 lines.
- Snapshot updated by hand: `IsPerTooth`/`UnitCost` in the `DentalRecordAct` block at EF's alphabetical
  positions, and the three `decimal(18,2)` → `(18,3)` changes. `StockItems.UnitPrice` was deliberately left at
  `(18,2)` (out of spec scope).

**Action before merge:** with the API stopped, run `dotnet ef migrations add ToothFirstRecordPricing`
(no `--no-build`) against a clean build and diff it against the hand-authored files, or apply it to a scratch DB
and confirm `Up`/`Down` round-trip.

## Coordination note — `treatment-plan-workspace`
That spec (APPROVED, Type Full, unimplemented, same branch) also modifies this modal: its AC-9 wants the
dental-record modal to pre-select a plan item when opened from an appointment carrying `treatmentPlanItemId`.
It should land on the new seam, not the old one:
- The plan-item `<Select>` and `handlePlanItemLink` are still in `patient-record-modal.tsx`; prefilling now goes
  through `dispatch({ type: "applyPlanItem", item })`.
- `use-session-acts.ts`'s `applyPlanItem` case already carries designation + planned cost + teeth into the
  composer and sets the chart selection, guarded by `isDraftEmpty` so it never clobbers typed input.
- So AC-9 reduces to resolving the incoming `treatmentPlanItemId` to a `PlanItemOption` and calling
  `handlePlanItemLink` with it on open.

## Quality checks
| Check | Result |
|-------|--------|
| `dotnet build ClinicManagement.Infrastructure.csproj` | 0 errors |
| `dotnet build ClinicManagement.UnitTests.csproj` (compiles Domain + Application + Infrastructure + API) | **0 errors**, 58 warnings — all pre-existing baseline families (`CS8618` 92, `CS8602` 12, `CS8981` 4, `CS8600` 4, `CS8604` 2, `CS0618` 2 on a no-incremental pass) |
| Warnings scoped to changed files | **none** |
| `npx tsc --noEmit` (web) | clean |
| `npm run build` (web) | ✓ Compiled successfully |

`dotnet test` was not run: Smart App Control blocks freshly-built DLLs (`0x800711C7`) on this machine, and
`web/` has no test runner or installed ESLint (`features/LEARNINGS.md`), so `tsc --noEmit` + `next build` is the
real FE gate.

## Deferred to /test-small-feature
The spec's full test plan, none of it written here:
- `Features/Patients/DentalRecordActParserTests.cs` — mixed dentition accepted; invalid FDI rejected
  (`[Theory]` 19/20/0/49/56/99); blank name + unknown condition still rejected; `BuildToothStates` one entry per
  act × tooth; **two acts on the same tooth → two entries** (pins AC-2); `Sain`/null skipped.
- `Domain/DentalRecordActPricingTests.cs` — `UnitCost`/`IsPerTooth` preserved and rounded through
  `InvoiceCalculator`; `IsPerTooth` forced false with no teeth; record `Cost` == Σ act costs with millimes
  (AC-15); `SetActs` rebuilds derived teeth without duplicates.
- Create/Update handler cases: mixed-dentition act list succeeds and writes both tooth states; update replaces
  this record's tooth states and clears diagnoses on treated teeth.

Manual verification (the real acceptance gate) is the 11-step script in `spec.md` — untouched, still to run
with the dentist.
