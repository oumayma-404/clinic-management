# Implementation Blueprint — A bridge is an object; a gesture is a gesture

> Option 1 of `/think-solution`, 2026-09-08. Three asks from the practising dentist, plus one enum the model
> cannot currently hold. **Blueprint only — no implementation code here.**

## Summary

Three things the odontogramme and the fiche get wrong, and they share one root: **a clinical fact is being
inferred from geometry or from a mode, instead of being stated and carried.**

1. **A bridge has no identity.** `odontogram.tsx`'s `bridgeSpans` joins bridge-marked teeth by *arch adjacency*
   within `MAX_PONTIC_SITES = 3`. So two bridges placed side by side become one bar — and because
   `planned: existing?.planned || planned` propagates leftward through the merge, a **finished** crown next to a
   **planned** bridge is drawn dashed-red, *still to place*. It also fails the other way: a bridge charted on
   abutments 4 sites apart draws no travée at all. `BridgeCharting.cs:14` already says position cannot answer
   this question; `bridgeSpans` asks it anyway, one level up.
2. **Multi-select is behind a mode, and only on one of the two charts.** `useToothDragSelect` is correct and
   complete, wired to exactly one call site (`odontogram.tsx:346`, gated on `multiSelect`). The fiche's
   `RecordToothChart` has no drag at all. The one sentence that teaches the gesture
   (« Glissez pour cocher une série ») renders *only while the mode is already on*.
3. **Nothing asks the bridge question.** The pilier/pontique toggle exists (`act-card.tsx:400`) as a `text-2xs`
   word inside a tooth chip. `ponticTeeth: []` means both « no pontics » and « never asked ».
4. **A pilier can be an implant** (« intermédiaire de bridge sur implant ») and the model has no way to say so.

The fix is one shape applied three times: **state the fact, carry it, and let the drawing read it.**

---

## Slice 1 — A bridge is an object (`BridgeGroupId`)

### The model

`ToothState` gains a nullable `BridgeGroupId`. It is **not a foreign key** — it is an opaque grouping token
whose only meaning is « these rows are one bridge ».

- **Treatment rows**: `DentalRecordActParser.BuildToothStates` mints one `Guid` **per bridge act**. One act is
  one bridge — already true, already how the act prices it (`« Couronne / bridge (par élément) »`), and it is
  why no second owner of the bridge's shape is needed.
- **Diagnosis rows** (a *planned* bridge charted on the odontogramme): the multi-tooth panel charts N teeth
  with N sequential POSTs, so the group must be **minted client-side and sent on each POST**, or the teeth of
  one gesture land in N groups of one.
- **Legacy rows** carry `null` and must keep drawing exactly as they do today.

### The invariant

`BridgeGroupId` may never describe a non-bridge. The aggregate **folds rather than refuses**, exactly as
`PonticToothNumbers` already does — a client legitimately holds a stale value:

```
BridgeGroupId = BridgeCharting.IsUnit(condition) ? bridgeGroupId : null
```

### The drawing

New module `web/components/bridge-runs.ts` — **the one owner** of « which teeth are one bridge »:

```ts
export function buildBridgeRuns(
  teeth: ToothQuadrants,
  byTooth: Map<number, ToothStateDto[]>,
): Map<number, BridgeSpan>
```

Two passes, and the order is load-bearing:

1. **Grouped pass.** Group the bridge-marked entries by `bridgeGroupId`. For each group, take its first and
   last tooth **in arch order** and mark the run between them — crossing blank pontic sites with no cap,
   because the group states its own extent. `MAX_PONTIC_SITES` is not consulted, so a 6-unit bridge and a
   bridge charted on abutments only both draw correctly. `planned` is computed **per group** (any member from a
   diagnosis ⇒ the run is a plan), which kills the leftward-propagation bug outright.
2. **Ungrouped fallback.** The existing adjacency scan, run over the ungrouped remainder **only**, with every
   grouped tooth excluded from the candidate list. A new bridge can therefore never be absorbed into a legacy
   run, and a legacy run behaves byte-for-byte as today. This is also what keeps the single-tooth editor path
   working: chart 14 then 16 individually and the fallback still joins them.

`BridgeSpan` gains `runLabel: string` (« Bridge 14 → 16 · 3 éléments ») for the cell's `title`.
`isTerminal` is **not** stored — the glyph already knows: it caps wherever `toPrevious` / `toNext` is false.

`ToothSymbolGlyph` (`tooth-symbols.tsx:455`) draws the terminal cap: today the bar runs to ±55/155 (half a cell
past the edge) only on continuing sides, so two neighbouring runs already stop at the cell centre — a short
vertical tick at the stop is what makes the break read as *deliberate* rather than as a rendering gap.

---

## Slice 2 — The gesture, un-gated, on both charts

`ToothArchLayout` gains **one optional prop**:

```ts
dragSelect?: ToothDragSelectHandle   // from useToothDragSelect
```

The layout spreads `containerProps` and applies `select-none` **unconditionally when present**. It still knows
nothing about teeth, so its documented « takes no per-tooth state » contract holds — this is the propagation
fix without breaking the contract, and it is why both charts get the gesture from one edit.

**`select-none` must be unconditional**, not gated: the browser anchors a text selection on `pointerdown`,
before any movement has said this is a drag (the reason `agenda-grid-drag.ts` records).

### On the fiche (`RecordToothChart` / `patient-record-modal`)

A tap has **one** meaning here (add the tooth to the armed act), so there is **no mode at all**.

- Each tooth `<button>` carries `data-tooth={num}` (`TOOTH_CELL_ATTR`).
- `enabled: !!focusedAct && !loading` — the chart is already inert with nothing armed, and a drag on an inert
  chart must paint nothing *and* not consume the gesture.
- ⚠️ `onPaint` dispatches a **new `setTooth` action, never `toggleTooth`**. The hook *sets*; a toggle would undo
  every tooth the pointer re-crossed, which on a curve is most of them. This is the trap `paintTooth`'s own
  comment at `odontogram.tsx:330` already records.
- `Haut` / `Bas` / `Toute la bouche` stay — they answer a different question (a whole arch, not a run).

### On the odontogramme

- Drop `enabled: multiSelect` → the drag arms on its own (`MOUSE_DRAG_SLOP_PX = 4` on a mouse, the 350 ms
  long-press on a finger, which stays because the arch is an `overflow-x-auto` scroller and teeth 18–15 must
  remain reachable at 390 px).
- The first painted tooth **enters** multi-select. `didConsumeGesture()` already swallows the trailing click, so
  the anchor tooth's editor does not open at the end of a drag.
- ⚠️ **« Plusieurs dents » does not disappear — it changes job.** It becomes the selection readout
  (« 3 dents sélectionnées · Vider »), and the hint « Glissez sur plusieurs dents » becomes **permanent** above
  the arch. The comment at `odontogram.tsx:658` argues explicitly against a discoverable-only-if-you-know-it
  gesture; the owner is overriding the *gate*, not the argument. **Rewrite that comment to record the new
  decision** — do not delete it.

---

## Slice 3 — The follow-up question, and a third role

### Three roles, two lists

`DentalRecordAct` gains `ImplantPilierToothNumbers`, mirroring `PonticToothNumbers` exactly (same JSON int
array, same value comparer, same clear-when-not-a-bridge rule). Roles: **pilier** (default) · **pontique** ·
**pilier sur implant**. The two lists are mutually exclusive — writing one role removes the tooth from the
other list.

> **Decision + tripwire.** Three roles as two subset lists is at the edge of what this shape carries honestly.
> It is chosen because it needs one column and zero data migration, and because it leaves `PonticToothNumbers`'
> public API — referenced in 8 places and 2 test classes — untouched. **A fourth role demands a role map**; say
> so in the entity's doc-comment so the next person does not add a third list.

New enum member, append-only: `ToothCondition.BridgePilierImplant = 17`, added to `BridgeCharting.Units`,
`BRIDGE_UNIT_CONDITIONS`, `ConditionTreatments` (`⇒ Couronne`, like its two siblings), `CONDITION_ORDER`,
`CONDITIONS` and `TOOTH_SYMBOLS`.

`BridgeCharting.ConditionFor` takes a fourth parameter — **required, with no default**:

```csharp
public static ToothCondition ConditionFor(
    ToothCondition actCondition,
    IReadOnlyCollection<int> ponticTeeth,
    IReadOnlyCollection<int> implantPilierTeeth,   // ⚠️ required — see below
    int tooth)
```

⚠️ **No default value, deliberately.** `TreatmentPlanItemStepInput`'s fourth argument defaults to null, so a
three-argument copy compiled, read correctly and erased an osseointegration wait. A required parameter makes
every call site answer at compile time. The « no roles marked ⇒ act condition unchanged » invariant becomes
« **neither** list has anything ⇒ unchanged », which is what keeps every pre-existing row charting as it does.

### The prompt

Tri-state, **client-side only** — `BridgeCharting`'s « empty ⇒ chart unchanged » is load-bearing for every
existing row and must not acquire a third meaning server-side.

```ts
// SessionAct
ponticTeeth: number[]
implantPilierTeeth: number[]
/** Has « quelles dents sont des pontiques ? » been answered for THIS act? Seeded true for an act read back
 *  from the server (it went through this form once), so reopening a saved fiche never re-interrogates.
 *  Never sent, never stored. */
bridgeRolesAnswered: boolean
```

`bridgeRolesAnswered = cameFromServer || userAnswered` — that one line is what makes a client-side tri-state
sufficient, with no schema change.

An **inline step in the act card** (never a dialog — the fiche is already a dialog, and the repo's
dialog-depth problem is documented): bridge état + ≥2 teeth + `!bridgeRolesAnswered` ⇒

> **Quelles dents sont des pontiques ?**
> `[14 pilier ▾] [15 pilier ▾] [16 pilier ▾]`   → each cycles pilier · pontique · pilier sur implant
> **« Aucun pontique »**   **« Valider »**

« Aucun pontique » is a **real answer**, not a dismissal — it sets `bridgeRolesAnswered` with both lists empty,
which is exactly the record the product already writes and now the one the dentist has confirmed. Once
answered the step collapses to a summary line (« 14 · 16 piliers · 15 pontique — modifier »).

Actions: `setBridgeRole` (replacing `togglePontic`) and `answerBridgeRoles`. The chip in the teeth row keeps the
**word** (it is a fact about that tooth and « P » would have to be learned) but becomes a tap-to-reopen-the-step
target rather than a 2-way toggle — a 3-way cycle inside a chip hides its own options.

---

## Files to create

| Path | Purpose |
|---|---|
| `web/components/bridge-runs.ts` | `buildBridgeRuns` — the one owner of « which teeth are one bridge ». Grouped pass + ungrouped legacy fallback. |
| `api/…/Migrations/<ts>_AddToothStateBridgeGroup.cs` | `ToothStates.BridgeGroupId uuid NULL` + index; `DentalRecordActs.ImplantPilierToothNumbers` (JSON text). |
| `api/…/UnitTests/Features/Patients/BridgeGroupChartingTests.cs` | Two bridge acts in one fiche ⇒ two distinct groups; non-bridge ⇒ null. |

## Files to modify

**Backend**

| Path | Change |
|---|---|
| `Domain/Enums/ToothCondition.cs` | `BridgePilierImplant = 17`, with the same « append-only, never re-pointed » note. |
| `Domain/Entities/ToothState.cs` | `BridgeGroupId`; ctor param **optional and last**, after `source`; folded to null for a non-bridge condition. |
| `Domain/Entities/DentalRecordAct.cs` | `_implantPilierToothNumbers` + property; `SetX` clears both lists when the état stops being a bridge; role exclusivity. |
| `Domain/Entities/DentalRecordAct.cs` (`DentalRecordActInput`) | `ImplantPilierToothNumbers` — optional and **last**, after `PonticToothNumbers`. |
| `Domain/Services/BridgeCharting.cs` | `Units += BridgePilierImplant`; `ConditionFor` fourth param (required); fold order pontique → implant-pilier → act condition. |
| `Domain/Services/ConditionTreatments.cs` | `[BridgePilierImplant] = [new(Produces: Couronne)]`. |
| `Application/Features/Patients/DentalRecordActParser.cs` | Mint one `Guid` per bridge act in `BuildToothStates`; pass both role lists to `ConditionFor`; validate/normalise the new list as it already does for pontics. |
| `Application/Features/Patients/Commands/DiagnoseToothCommand.cs` | Accept `BridgeGroupId` from the request; keep it only for a bridge condition. |
| `Application/DTOs/ToothStateDto.cs` | `BridgeGroupId` on the DTO and on `DiagnoseToothInput`. |
| `Application/DTOs/DentalRecordDto.cs` | `ImplantPilierToothNumbers` on both the read DTO and the input. |
| `Application/Features/Patients/DentalRecordMappingExtensions.cs` | Map the new list back out (the same reason the pontic list is mapped: a form that cannot see a role sends it back empty and flattens the bridge). |
| `Application/Features/Patients/Queries/GetOdontogramQuery.cs` | Project `BridgeGroupId`. |
| `Infrastructure/Persistence/Configurations/ToothStateConfiguration.cs` | `BridgeGroupId` + `HasIndex`. |
| `Infrastructure/Persistence/Configurations/DentalRecordActConfiguration.cs` | The new JSON int array, same comparer as `PonticToothNumbers`. |

**Frontend**

| Path | Change |
|---|---|
| `components/tooth-arch-layout.tsx` | Optional `dragSelect` prop; spreads `containerProps`, applies `select-none`. |
| `components/odontogram.tsx` | Un-gate the drag; `bridgeSpans` → `buildBridgeRuns`; « Plusieurs dents » becomes the selection readout; permanent hint; **rewrite the `:658` comment**. |
| `components/record-tooth-chart.tsx` | `data-tooth` on every cell; accept + forward `dragSelect`. |
| `components/patient-record-modal.tsx` | Wire `useToothDragSelect` with `enabled: !!focusedAct && !loading`, `onPaint → setTooth`. |
| `components/tooth-symbols.tsx` | Terminal cap on the travée; `BridgePilierImplant` glyph — **reuse the `Implant` fixture path, do not copy it** (extract a shared draw so the two cannot diverge). |
| `components/odontogram-conditions.ts` | `BRIDGE_UNIT_CONDITIONS += "BridgePilierImplant"`; `CONDITIONS` + `CONDITION_ORDER` entries. |
| `components/record/use-session-acts.ts` | `implantPilierTeeth`, `bridgeRolesAnswered`, `setBridgeRole`, `answerBridgeRoles`, `setTooth`; extend `patchAct`'s clearing rule; **delete the orphaned `PONTIC_SEED` reference at :56**. |
| `components/record/act-card.tsx` | The follow-up step; the chip becomes a role display + reopen target. |
| `lib/api/odontogram.ts` + `lib/api/types.ts` | `bridgeGroupId` on `DiagnoseToothRequest` and `ToothStateDto`. |
| `scripts/check-responsive.mjs` | New check (below) + extend `tooth-symbol-covers-every-condition` to also assert `CONDITIONS` coverage. |

---

## Wiring / DI

Nothing new to register. No new `Features/<Area>` folder, so **no new realtime key** — everything lands in
`Features/Patients`, which `clinic-hub.ts` already declares. (A new area would fail
`RealtimeResourceResolverTests` in both directions.)

---

## Pitfalls

1. **`conditionStyle` falls back to `CONDITIONS.Sain`** (`odontogram-conditions.ts:172`). A new condition with
   no `CONDITIONS` entry renders an implant-borne pilier as **« Sain », grey, healthy** — no error, no console
   line. `tooth-symbol-covers-every-condition` guards `CONDITION_ORDER ↔ TOOTH_SYMBOLS` in both directions but
   **deliberately does not read `CONDITIONS`** ("whose keys could drift"). The honest fix is to *guard*
   `CONDITIONS` in that same check, not to keep avoiding it.
2. **The diagnosis group id must survive the partial-failure retry.** `MultiToothDiagnosisPanel.handleSave`
   sends one POST per tooth and, on partial failure, re-ticks only the teeth that did not land. A fresh id on
   retry splits one bridge into two groups. Hold it in a ref, reset it on success and on `onClearSelection`.
   Send it **only** when `isBridgeUnit(effectiveCondition)`.
3. **`onPaint` must not dispatch `toggleTooth`.** The hook sets; a toggle undoes every re-crossed tooth.
4. **`select-none` unconditional**, or a mouse drag anchors a text selection on `pointerdown`.
5. **The scaffolded migration will contain `AddColumn<uint>("xmin")` for all 38 entities.** Strip it. Check
   also for a scaffolded `DropColumn` placed above anything that reads the column.
6. **Run `dotnet run -- verify-schema` before *and* after the migration and diff it.** Nothing in `UnitTests`
   touches a database, so a migration is the one change unit tests structurally cannot verify.
7. **Build tests outside the repo**: `BaseOutputPath=<temp> dotnet test … -c Release`. Smart App Control
   intermittently refuses freshly-built in-repo test assemblies, and the running API holds `api/**/bin`.
8. **`UpdateDentalRecordCommand` deletes and rebuilds every tooth state for the fiche** (`:324`). A re-minted
   group id per save is therefore safe — the whole run is rebuilt in one transaction. Confirmed, not assumed.
9. **Check the archive and CSV paths for a `ToothState` column list.** `clinic-data-archive-and-restore` and
   the L5 CSV export/import may enumerate columns; a new column omitted there is a silent data loss on
   round-trip. This is the repo's signature defect shape — grep before assuming.
10. **The two charts are not symmetric, whatever the owner said.** On the odontogramme a tap has two possible
    meanings (open the editor / tick the tooth) and on the fiche it has one. That is why the fiche needs no
    mode and the odontogramme keeps a selection readout. Do not "unify" them into one component.
11. **Grouped teeth must be excluded from the fallback scan's candidate list**, not merely deprioritised —
    otherwise a legacy `Bridge` on 17 reaches into a grouped bridge on 14·15·16 and the merge is back.
12. **A restore reverts the schema, the password hash, the TOTP secret and `MustChangePassword`.** If a dev
    database is restored while testing this, take the base dump *after* staging.

---

## Test strategy

**Backend (xUnit + Moq — the gate is the unfiltered suite, `.claude/rules/verification.md` § 4)**

- `BridgePonticChartingTests` — extend to three roles. The two cases that must **still pass unchanged** are the
  ones the type exists for: a **pier abutment** (16 · 12 terminal, 14 an abutment, 15 · 13 pontics) and a
  **cantilever**. Add: an implant pilier charts `BridgePilierImplant`; a tooth in neither list charts `Pilier`
  when either list is non-empty; **both lists empty ⇒ the act's own condition, unchanged** (the legacy guard).
- `BridgeGroupChartingTests` (new) — two bridge acts in ONE fiche get two **distinct** group ids; a non-bridge
  act gets `null`; a bridge act whose état is later changed to `Couronne` loses both the group and the roles.
- `ToothStateTests` — `BridgeGroupId` is folded to null for a non-bridge condition (the aggregate folds, never
  refuses).
- `DiagnoseToothCommandTests` — a supplied group id is kept for a bridge condition and dropped for a `Carie`.
- `DentalRecordActParserTests` — its five existing `BuildToothStates` tests must stay green untouched.

**Frontend** — `web/` has no test runner, so the guard is a derived `check:responsive` check:

- **`bridge-run-has-one-owner`**: any file passing a `bridgeSpan=` prop must obtain it from `buildBridgeRuns`.
  Derived from the prop, not from a file list, so it covers the third chart on the day it is written — and with
  a **tripwire** that fails if the scan finds zero candidates (a guard that matches nothing holds nothing).
- Extend `tooth-symbol-covers-every-condition` to assert `CONDITIONS` coverage (pitfall 1).

**Gate, all three, after the *last* edit:** `npm run check:responsive` + `npx tsc --noEmit` + `npm run build`.

**Eye pass at 320 / 390 / 820 / 1180 / 1440 px**, one launch, collecting every finding (§ 0):

1. Two bridges side by side (14·15·16 done, 17·18 planned) — two bars, four caps, and the finished crown on 16
   is **solid**, not dashed. This is the acceptance criterion for slice 1.
2. A legacy `Bridge` pair on 14 and 16 with 15 blank — still joined, still one bar.
3. A drag on the **fiche** chart with an act armed; and with none armed (paints nothing, consumes nothing).
4. A drag on the odontogramme with the mode never touched.
5. The follow-up step at 320 px — it lands inside an already-dense dialog, so
   `never-trade-a-capability-for-space` applies: nothing existing may fold to make room.
6. A finger long-press-drag at 390 px, and horizontal scroll of the arch still reachable (`arch-clipping`).

**e2e** — `features/e2e-hot-paths` now exists and a fiche save moves eleven surfaces. A two-bridge fiche save
belongs in `scenarios.md` as a tier-0 hot path: the money, the chart and the devis all read from it.

---

# Edge cases — the pass before implementing

Twelve of these **change the design above**; they are marked ⛔ and must be folded in before any code is
written. The rest are verified-safe or notes. Everything here was checked against source, not assumed.

## ⛔ Design changes

### ⛔1. A group is minted only for a bridge act with **≥ 2 teeth**

Before `PonticToothNumbers` existed, the *only* way to record a 3-unit bridge was **the same procedure twice**
— `DentalRecordAct.cs:44` says so explicitly. Those fiches are two acts of one tooth each. If every bridge act
mints a group, each becomes a group of one, both are excluded from the ungrouped fallback, and **the travée
disappears from a workflow the docs say people used**. Existing rows are safe (they are `NULL` and stay in the
fallback) — this bites on *new* fiches entered the old way.

A one-tooth act asserts nothing about a span, so it must not claim one. Rule: ≥ 2 teeth ⇒ mint, otherwise
`null` and let adjacency answer, exactly as today.

### ⛔2. One bridge mark per tooth — the **newest** — resolved *before* grouping

`byTooth` holds many entries per tooth, sorted newest-first, and `bridgeMark` already takes the newest
(`odontogram.tsx:~356`). If the grouped pass groups *entries* instead, a tooth that carried a 2024 `Bridge` and
now carries a 2026 `BridgePilier` lands in **two groups**, both runs include it, and the `||` merge joins them
— the exact bug, reintroduced on any redone bridge.

Resolve per tooth first (newest bridge entry wins), *then* group. A tooth belongs to exactly one group.

### ⛔3. The drag arms only when the pointer reaches a **second tooth**

`MOUSE_DRAG_SLOP_PX = 4`. With the mode gone, a 4 px hand tremor or trackpad drift on the odontogramme **ticks
the tooth instead of opening its editor** — and the editor is the primary action there. The mode used to make
this impossible.

`cellAt(x, y) !== state.anchor` is both the fix and the correct semantics: a gesture that never leaves the
anchor tooth *is* a click. It also makes drag-out-and-back-to-start behave.

### ⛔4. `consumed` is set only when the gesture actually **painted** something

`end()` currently does `if (drag.current?.armed) consumed.current = true`. Combined with ⛔3, a **long press in
place** on a finger arms, paints nothing, and still swallows the trailing click — so a long press on a tooth
does **nothing at all**: no editor, no selection, no feedback. Change to `if (state.applied.size > 0)`.

### ⛔5. « Plusieurs dents » stays a real toggle, for the keyboard

The drag is pointer-only. Removing the gate removes **keyboard and AT multi-select entirely** — Space on a
tooth opens the editor, and there is no other route. The button therefore does two jobs: it is the selection
readout (`role="status"`, « 3 dents sélectionnées · Vider ») **and** still forces the mode on when activated.
The gate is what goes, not the control.

### ⛔6. `setBridgeRole` is keyed by act, never by `focused`

`togglePontic` does `if (!focused) return state` and is safe today only because it renders solely inside the
armed card. The new collapsed role summary is visible on **unarmed** cards, so « modifier » on card 2 would
silently write roles onto card 1. Two bridges in one séance is precisely the case the owner described.

### ⛔7. The prompt is always present; `answered` controls **expanded vs. collapsed**

Not appear/disappear. If it appears on the 2nd tooth and vanishes on answer, then adding a 5th tooth either
re-opens it (nagging four times while the dentist taps four teeth) or silently defaults the new tooth to pilier.
Always-rendered for a bridge act with ≥ 2 teeth: **expanded** while unanswered, **collapsed to a live summary**
once answered (« 14 · 16 · 17 piliers · 15 pontique »), so a tooth added afterwards is visibly accounted for.

### ⛔8. `ConditionFor` has **four** branches, and the fourth is the subtle one

```
tooth ∈ pontics            ⇒ BridgePontique
tooth ∈ implantPiliers     ⇒ BridgePilierImplant
either list non-empty      ⇒ act condition, promoted Bridge → BridgePilier
both lists empty           ⇒ act condition, UNCHANGED   ← the legacy guard
```

The promotion in branch 3 is today's deliberate behaviour (`BridgeCharting.cs:28` — marking a pontic on a
legacy `Bridge` act « is exactly how it becomes precise »). Branch 4 is what keeps every pre-existing row and
every non-detailed bridge charting as it always did. One test per branch, minimum.

### ⛔9. The multi-tooth **diagnosis** panel has no role control — a planned bridge is still uncharted

`MultiToothDiagnosisPanel` charts **one condition across N teeth**. So a *planned* 3-unit bridge can only be
charted as three piliers or three pontiques — the identical defect the fiche just fixed, still live on the
odontogramme, which is the surface the owner was actually looking at. Each `ToothState` row carries its own
condition, so the data model already supports it; only the panel does not ask.

**This is a fourth slice, not a note.** Without it, « select bridge, then say which are ponts » works when
recording work done and not when planning it.

### ⛔10. The run label counts the group's **teeth**, not the cells it crosses

`MIXED_TEETH` interleaves deciduous cells (`…16, 55, 15, 54, 14…`), so a 14·15·16 bridge in the mixed view
spans **five cells**. « 3 éléments » must come from `group.teeth.length`; `toIdx − fromIdx + 1` would print
« 5 éléments » on the same bridge in a different view.

### ⛔11. A contested crossed cell goes to the **newest** group — and `planned` is never `||`-ed

Two groups can only both claim a cell that carries *no* bridge mark (⛔2 makes marked cells exclusive). The
`existing?.planned || planned` merge at `odontogram.tsx:~388` is the leftward propagation that draws a finished
crown as still-to-place; per-group computation removes it, and the tie-break on a crossed cell must be
last-write-by-treatment-date, not a union.

### ⛔12. A group is clamped **per arch**

Iterate each arch and take min/max index *within that arch*. A group whose teeth span both arches — a mis-tap,
or 16 and 46 in one act — then draws two independent runs instead of one absurd bar. A bridge cannot cross the
arches; the rendering should not pretend it can. (The midline is *not* an edge case: `[...upperRight,
...upperLeft]` already makes 11 and 21 adjacent, which is correct.)

## Verified safe — checked, no action

| # | Question | Answer |
|---|---|---|
| 13 | The hidden arch below `md:` inflating `cellsIn` / breaking `sameRow` | `ToothArchLayout` **conditionally renders** (`{showUpper && …}`) — the hidden arch is not in the DOM. Clean. |
| 14 | Does a third bridge role change the money? | No. `DentalRecordInvoiceLines.cs:50` bills `IsPerTooth ⇒ ToothNumbers.Count × UnitCost`. A pontic and an implant pilier are each one element, already counted. |
| 15 | Drag leaking into the « Actes réalisés » tab | The wrapper is inside `TabsContent value="diagnostics"` (657–794); the acts tab renders `OdontogramActsChart` (796). Isolated. *(That chart draws no travée at all today — separate question, out of scope.)* |
| 16 | `patient-summary-modal`'s read-only charts picking up the drag | `dragSelect` is optional and it passes none. Safe by construction. |
| 17 | Drag beginning inside an open tooth-editor popover | `cellAt` → `closest('[data-tooth]')` → null → `drag.current = null`. Aborts. |
| 18 | Touch scroll of the arch surviving un-gated drag | Move > 10 px before 350 ms abandons the gesture outright; `preventDefault` only fires once armed. Preserved. |
| 19 | `BridgeCarryOverTests` | Unrelated — it is the devis→facture *money* bridge. Naming collision only. |
| 20 | Free `check:responsive` number | N28 is the highest. Use **N29**. |

## Notes to carry into implementation

- **Do not backfill the migration.** Backfilling groups from `DentalRecordId` + adjacency would freeze today's
  wrong inference into data, where it can never be corrected. `NULL` is the honest value for a legacy row.
- **`GetOdontogramQuery`'s projection** (`:57`) is a hand-written `Select` — `BridgeGroupId` must be added
  there or the field is silently absent on the wire with no compile error.
- **A multi-séance bridge is charted only on the last séance** (`ToothChartingRules`), so the group is minted on
  the final fiche — correct, one group. But a tooth prepared in séance 1 and absent from the *last* fiche is not
  in the group, so the run is shorter than the bridge. That is the documented
  `TreatmentPlanItemDto.TreatedToothNumbers` hazard, now with a visible symptom.
- **`ClearDiagnosesForTreatedTeethAsync` shrinks a planned group.** Plan a bridge on 14·15·16, place only 14·15:
  the planned group drops to one tooth ⇒ no bar, beside a 2-unit done bar. Honest, and better than today (which
  joins them and marks the finished half as planned) — but it is a visible change, so expect it.
- **Keep `MAX_PONTIC_SITES`** and re-document it as legacy/ungrouped only. Deleting it breaks every existing
  bridge's travée.
- **Check the archive and CSV column lists** for `ToothState` before the migration lands (pitfall 9 above).
- **Smoke the drag in the Android WebView** — `document.elementFromPoint` under the shell's overlays is the one
  thing a desktop browser cannot answer.
