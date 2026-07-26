# Feature Specification: Tooth-First Dental Record Entry

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-26
**Scope:** Full
**Feature:** Invert the "Ajouter une fiche médicale" modal from act-first to tooth-first — click a tooth (or several), say what was done, done — and fix the pricing, dentition and payment defects that flow surfaces.

> **Implementation order is part of the spec.** Land **S1 → S2 → S3** with a clean build gate between stages. S1 and S2 are shippable value on their own; S3 lands the UI inversion on a foundation that already round-trips. Do not start S3 before S1 is green.

## Overview

A dentist reviewed the app and rejected the dental-record entry flow. Today the modal is **act-first**: you create an "Acte" row, click that row to make it « Ciblé », then scroll back up to the chart and click teeth — and clicking a tooth with the wrong row focused silently files it under the wrong act. Each act is a ~6-row stack of fields *below* the chart, so charting three acts means constant scrolling between the chart and the fields. His words: *he should be able to click a tooth, or several teeth, and put what he did in that session right away, not in fields under the teeth chart* — and multiple procedures on the same tooth in one session must be natural.

**The domain already models this.** `DentalRecordAct` is one procedure applied to N teeth with its own cost, resulting condition, surfaces and note; its own docstring says *"A tooth may appear across multiple acts (multiple treatments per session)"*. `DentalRecord.SetActs` derives the summary/cost/flat tooth list; `ToothState` gets one entry per act × tooth. So multi-procedure-per-tooth needs **no** new entity and no new endpoint — the problem is the entry surface.

Exploration also surfaced four defects that the new flow makes routine rather than rare, so they are in scope here rather than deferred:

- **B1 — silent under-billing.** One act on 3 teeth bills the procedure **once** (`patient-record-modal.tsx:227` prefills `cost = pt.defaultCost` with no multiplier; `DentalRecord.cs:121` sums act costs). Batch tooth selection is the whole point of the redesign, so this becomes the normal path.
- **B2 — mixed dentition is impossible.** `DentalRecordActParser.cs:27` rejects any tooth whose `IsAdultTooth` ≠ the record's `IsAdultTeeth` flag, so a 9-year-old with a permanent 36 **and** a deciduous 75 in one session cannot be recorded at all. Flipping the Adulte/Enfant toggle after charting also silently keeps the teeth and fails at save.
- **B3 — « Montant payé » latches on the first act.** The prefill effect (`patient-record-modal.tsx:256`) fires only while the field is `""`, so it freezes at the first act's total and goes stale as acts are added — the dentist saves a wrong « reste à payer ».
- **B4 — money precision is inconsistent.** `DentalRecords.Cost`/`AmountPaid` are `decimal(18,2)` while `DentalRecordActs.Cost` and 22 of the 26 money columns in the schema are `decimal(18,3)`. The FE input is `step="0.001"` and `formatDT` prints 3 decimals, so the UI invites millime entry that PostgreSQL then rounds away — and `unit × N` arithmetic makes third decimals routine, so `record.Cost` can drift from `sum(acts.Cost)`.

## Design Notes (the target interaction)

Two panes inside a `max-w-5xl` dialog (`lg:grid-cols-[minmax(340px,420px)_1fr]`, stacking to one column below `lg`):

```
┌ Ajouter une fiche médicale ─────────────────────────────────┐
│ ⚠ Allergies : Pénicilline     Date 26/07/2026  [Adulte|Enf] │
├──────────────────────────┬──────────────────────────────────┤
│ SCHÉMA (collant)         │ Séance — 4 actes · 1 210,000 DT  │
│  18 17 16 15 14 …        │ ┌ Sélection : 16, 26 ───────────┐│
│   ▉  ▉  ●  ○  ⌁          │ │ [Composite 2-3 faces      ▾] ││
│  48 47 46 45 44 …        │ │ 110,000 × 2 dents = 220,000  ││
│   ○  ●  ○  ○  ○          │ │ Faces M O D V L   Note …     ││
│                          │ │      [+ Ajouter  (Entrée)]   ││
│ ● sélectionnée           │ └──────────────────────────────┘│
│ ▉ traitée ce jour        │ Dent 16 ─────────────────────────│
│ ⌁ à traiter (diagnostic) │  • Dévitalisation     180,000  ✕ │
│                          │  • Couronne zircone   750,000  ✕ │
│ [Quadrant] [Arcade]      │ Dent 26 ─────────────────────────│
│ [Toute la bouche]        │  • Composite ⛓16,26   220,000  ✕ │
│                          │ Actes généraux ──────────────────│
│                          │  • Détartrage 2 arc.   60,000  ✕ │
├──────────────────────────┴──────────────────────────────────┤
│ Total 1 210,000 · Payé [1 210,000] · Reste 0 · Notes ▾ [Créer]│
└─────────────────────────────────────────────────────────────┘
```

- The **chart is a selection surface**, not a per-act targeting tool. The « Ciblé » act concept is deleted.
- The **composer** commits on `Entrée` and **keeps the selection**, so a second procedure on the same tooth is one keystroke away. `Échap` clears the selection.
- Committed acts are **grouped by tooth**, plus an « Actes généraux » group for mouth-level procedures (détartrage, panoramique, prothèse complète, orthodontie) charted with an empty selection. A multi-tooth act appears under each of its teeth with a link marker; its cost is shown once, `· inclus` on the others.
- **Per-tooth pricing is explicit:** the composer shows one editable number and a `/dent ↔ forfait` toggle, with a live `110,000 × 2 dents = 220,000` readout. Default: `perTooth = pt.resultingCondition != null && selection.length > 0` — an act that changes a tooth's state is per-tooth; one that changes nothing is a session fee. This is right for 17 of the 19 seeded procedures (it misses *Facette* and *Soin dentaire enfant*, both per-tooth but stateless), and the toggle is one click, with the billed number always on screen.
- The **procedure picker groups by `pt.description`**. Note: `ProcedureType` has **no** `Category` property — `ProcedureTypeCatalogSeed.cs:103` passes `r.Category` into the ctor's `description` parameter, and the form modal labels that field « Description (optionnel) ». So treat it as a **soft grouping hint with an « Autres » bucket**, never as a rule, and never as the forfait signal.
- The **Adulte/Enfant toggle is a pure view switch** over the input device. Acts charted on the other dentition persist and stay listed, with a « 1 acte sur dentition enfant » chip on the toggle so nothing is hidden. Flipping **clears the selection** (a selection off-screen is a lie) and **never** clears acts. This is how mixed-dentition sessions get recorded — no third chart layout.

## What Changes

### S1 — Backend: pricing model, mixed dentition, money precision

- **`DentalRecordAct`** gains `decimal? UnitCost` and `bool IsPerTooth`. `Cost` stays the authoritative total (what everything downstream already reads); the two new columns record *how it was reached* so the editor round-trips and the invoice bridge can show a quantity. Legacy rows read `UnitCost = null, IsPerTooth = false` — correctly, a forfait.
- **`DentalRecord.SetActs`** currently takes a 7-element tuple; adding two fields makes it unreadable. Introduce a `DentalRecordActInput` record in `Domain/Entities` (or `Domain/Common`) carrying `ProcedureTypeId, ProcedureName, Cost, UnitCost, IsPerTooth, ToothNumbers, ResultingCondition, Surfaces, Note` and pass that. Update `SetActs`, `DentalRecordAct`'s ctor, and both call sites.
- **`DentalRecordActParser.Parse`** drops its `isAdultTeeth` parameter and the `DentalRecordTooth.IsAdultTooth(tooth) != isAdultTeeth` check; each tooth is validated with `FdiTooth.IsValid` instead, returning a French `Result.Failure` on an invalid number. (Verified safe: `FdiTooth.IsValid` is quadrant-precise, not a loose `11..48` range, so 19/20/49 still cannot be admitted.) Update the two call sites: `CreateDentalRecordCommand.cs:90`, `UpdateDentalRecordCommand.cs:78`.
- **`DentalActInput`** gains `UnitCost` + `IsPerTooth`; **`DentalRecordActDto`** exposes both. The server does **not** recompute `Cost` from them — the client sends the resolved total, and `InvoiceCalculator.RoundMoney` remains the single rounding authority.
- **Migration**: add the two `DentalRecordActs` columns, and widen `DentalRecords.Cost`, `DentalRecords.AmountPaid` and `ProcedureType.DefaultCost` from `decimal(18,2)` to `decimal(18,3)` (B4). Widening only — no data loss, no backfill.
- **Invoice bridge**: the record → draft-invoice preset in `web/app/patients/[id]/page.tsx:1584` maps each act to `{ designation, quantity: 1, unitPriceHt: act.cost }`. Change to `quantity = act.toothNumbers.length || 1, unitPriceHt = act.unitCost ?? act.cost` when `act.isPerTooth`, else keep the current single-line shape. Include the teeth in the designation (`Composite (16, 26)`) — an act, not a diagnosis, so it does not breach the `InvoiceLine` medical-secrecy rule.
- **Code comment** on `SetActs`: act GUIDs are regenerated on every update. Verified safe today (no FK anywhere references `DentalRecordAct.Id`; `InvoiceLine.DentalRecordId` points at the *record*) — the comment exists so nobody later depends on act-id stability.

### S2 — Correctness fixes in the existing layout

- **B3:** replace the `amountPaid` prefill effect with a `paidDirty` flag — mirror `total` until the user types in the field, then stop. Keep `isInvoiced` disabling the field and its « le paiement est géré par la facture » note.
- **B4 (frontend half):** add `roundMillimes(value)` to `web/lib/format.ts` and round the FE-computed total through it, so `110.001 × 3` displays and mirrors as `330,003` rather than `330.00299999`.
- **NaN guard:** `Number(a.cost) || 0` (`patient-record-modal.tsx:291`) silently persists 0 for unparseable input. Validate before save and block with a French message.
- **Dentition toggle** becomes non-destructive (see Design Notes): it no longer risks a save-time rejection, and it clears the selection rather than the acts.
- **`patient-summary-modal.tsx`** partitions records by `record.isAdultTeeth` (lines 34, 63) to build its two read-only charts. Once a record can hold both dentitions, that drops half its teeth from one chart. Partition by **tooth number** (FDI range) instead.
- **Error surfacing:** replace the hand-rolled `err instanceof ApiError ? err.message : …` in the modal with `showErrorToast` / `getErrorMessage` from `web/lib/errors.ts` (the canonical `{ error }` contract).

### S3 — The tooth-first UI

- **New `web/components/record/use-session-acts.ts`** — a `useReducer` owning `acts`, `selection` and `editingKey`, so no `useEffect` can fight user input (the failure mode already reported in `features/fix-patient-dental-ui/reviews/feature-review.md` Finding 1). Actions: `reset(from?)`, `toggleTooth`, `selectMany(teeth, additive)`, `clearSelection`, `beginEditAct(key)`, `commitAct(draft)` (appends, or patches when `editingKey` is set), `cancelEdit`, `removeAct(key)`. `commitAct` deliberately **preserves** the selection. Acts carry a client-side `key` (incrementing counter, never the array index, never the server act id).
- **New `web/components/record/session-act-composer.tsx`** — selection summary, grouped searchable catalogue picker, one editable price + `/dent ↔ forfait` toggle with the live readout, MODVL faces, état résultant, note, commit on `Entrée`. Editing a committed act loads it here **and restores its teeth as the selection**, with a visible « Annuler la modification ».
- **New `web/components/record/session-acts-list.tsx`** — acts grouped by tooth + « Actes généraux »; per-row remove and open-in-composer.
- **`web/components/record-tooth-chart.tsx`** — `ToothPaint` renames `focused` → `selected` and gains optional `existingCondition` / `existingIsDiagnosis`; new optional `onSelectMany` and quadrant / arcade / toute-la-bouche shortcuts. Exactly two consumers, both updated (`tsc` catches the rename).
- **`web/components/patient-record-modal.tsx`** — becomes a ~250-line orchestrator: two-pane layout, alerts banner, date + dentition toggle, the three new pieces, footer summary bar (total / payé / reste / notes accordion / Créer). Props are **unchanged**, so the post-visit deep-link (`?addRecord=1&appointmentId=…`) and plan-item carry-forward keep working.
- **Odontogram overlay:** load `odontogramApi.get(patientId)` on open to paint prior state — and **filter out `entry.dentalRecordId === record?.id`**, otherwise editing a record paints its own output as "prior state" beside the live session paint. Do **not** subscribe the modal to `useClinicRealtime` (a live refetch under an open form is worse than mild staleness).
- **Plan-item link** (`patient-record-modal.tsx:183`) currently prefills "the focused act row" — focus no longer exists. It must prefill the **composer** (designation + cost + teeth → selection), keeping the existing only-if-empty guard, or the `derive-and-confirm-plan-to-record` carry-forward silently regresses.
- **Notes accordion** defaults **open** when the record already has notes or important notes, so editing never looks like it lost them.

## Acceptance Criteria

- **AC-1:** Selecting one tooth, choosing a procedure and pressing `Entrée` records an act on that tooth with no intermediate "create act row / focus it" step, and the selection is still on that tooth afterwards.
- **AC-2:** Two different procedures can be recorded on the same tooth in one session, and appear as two rows under that tooth. Saving produces two `DentalRecordAct`s and two `ToothState` entries on that tooth.
- **AC-3:** Selecting several teeth and choosing one procedure records a single act covering all of them; the composer shows `unit × n = total` and the saved act's `Cost` equals that total.
- **AC-4:** The `/dent ↔ forfait` toggle switches the same editable number between "per tooth" and "flat fee"; whichever is shown is what gets billed. Default is per-tooth when the procedure has a resulting condition and teeth are selected, forfait otherwise.
- **AC-5:** Committing with **no** teeth selected records a mouth-level act under « Actes généraux » — not an error, and no tooth is touched.
- **AC-6:** A record can hold a permanent tooth (36) and a deciduous tooth (75) in the same session and saves without error; both appear in the patient's odontogram.
- **AC-7:** Flipping Adulte/Enfant only changes which chart is displayed: already-charted acts persist and stay listed, a chip reports acts on the other dentition, and the selection is cleared.
- **AC-8:** Re-opening an **existing** record and saving it immediately leaves its total and every act cost unchanged (per-tooth acts round-trip via `UnitCost`/`IsPerTooth`; legacy rows round-trip as forfaits).
- **AC-9:** Adding a tooth to a per-tooth act during edit reprices it (`unit × new n`); doing the same to a forfait act does not change its cost.
- **AC-10:** « Montant payé » tracks the running total as acts are added, and stops tracking as soon as the user types in it. It stays disabled with the invoice note when `isInvoiced`.
- **AC-11:** The chart shows the patient's prior tooth states (« à traiter » diagnoses dashed) — **excluding** any state written by the record currently being edited.
- **AC-12:** An unparseable cost blocks save with a French message; no act is persisted with a silently-zeroed cost.
- **AC-13:** A record containing both dentitions renders its teeth in the correct chart in `patient-summary-modal` (nothing dropped).
- **AC-14:** A per-tooth act on N teeth produces a draft invoice line with `quantity = N` at the unit price, and a designation naming the teeth.
- **AC-15:** `DentalRecords.Cost` equals `sum(DentalRecordActs.Cost)` to the millime for a record whose acts carry third-decimal values.
- **AC-16:** A record's teeth and per-tooth acts survive a full create → reload → edit → save cycle, and the plan-item link still marks its step « réalisé ».
- **AC-17:** Every failure surfaces as a French toast via the shared error helper; no raw `HTTP 400` text.
- **AC-18:** The layout is single-column below `lg` and the page body never scrolls horizontally at 1366×768.

## API Contract

Routes, verbs and the tenant/ownership rules are **unchanged** (`DentalRecordsController`, `api/patients/{patientId}/dental-records`). Two additive fields:

```
POST/PUT  /api/patients/{patientId}/dental-records[/{id}]
  acts[]: {
    procedureTypeId, procedureName, cost,     // cost = the resolved total (unchanged semantics)
    unitCost: number | null,                  // NEW — the per-unit price the total was built from
    isPerTooth: boolean,                      // NEW — whether cost = unitCost × teeth
    toothNumbers[], resultingCondition, surfaces, note
  }

Response DentalRecordActDto additionally carries: unitCost, isPerTooth
```

Failures keep the canonical `{ "error": "<message>" }` shape. New/changed validation messages (French):
- invalid FDI tooth number (replaces the "ne correspond pas à la dentition sélectionnée" rejection)
- unparseable / negative cost

## Data / Schema Changes

One migration:

| Table | Change |
|-------|--------|
| `DentalRecordActs` | **add** `UnitCost decimal(18,3) NULL`, `IsPerTooth boolean NOT NULL DEFAULT false` |
| `DentalRecords` | **widen** `Cost`, `AmountPaid` → `decimal(18,3)` |
| `ProcedureTypes` | **widen** `DefaultCost` → `decimal(18,3)` |

No backfill, no data transformation, no destructive change. `DentalRecord.IsAdultTeeth` is **kept** (derived at save: `true` unless every charted tooth is deciduous) — it is now effectively vestigial, used only for the legacy display badge; removing it would break the DTO for no gain.

## Out of Scope

- The rest of the dentist's feedback. This spec covers the **record-entry surface only**; his other findings need to be captured verbatim first and specced separately.
- A brush/stamp mode (pick a procedure, then click teeth to stamp it). It is the faster idiom for repetitive work and the composer already holds a procedure, so it can be folded in later — but he asked for tooth-first, and adding a second interaction mode now dilutes the fix.
- A dedicated « Mixte » chart layout rendering permanent and deciduous teeth together (32 vs 20 teeth do not align in columns; the non-destructive toggle solves the requirement without it).
- Per-tooth surfaces or notes *within* one act. Recording "composite 2 faces on 16, 3 faces on 26" stays two acts — which the composer makes cheap.
- Splitting a multi-tooth act into one act per tooth. Rejected: it would silently double the cost of legacy multi-tooth acts on edit.
- Restructuring the dental-records **list** on the patient detail tab, the odontogram page, or the treatment-plan editor.
- Removing `DentalRecord.IsAdultTeeth`, and any change to `ToothState`, the invoice lifecycle, or CNAM/BS1.

## Edge Cases (Critical only)

- **Editing a legacy multi-tooth act.** Must load as `IsPerTooth = false, UnitCost = null` (forfait at its stored total). Never infer per-tooth from `cost / teeth.length` — inferring would double the price of any act whose stored cost happens to divide evenly. This is the single highest-risk line in the change.
- **Odontogram self-overlay.** The record being edited owns `ToothState` rows returned by `GetOdontogramQuery`; they must be filtered out of the "prior state" paint or the chart double-counts (AC-11).
- **Dentition flip with a live selection.** Clear the selection; keep the acts. A composer claiming « Sélection : 16 » while the child chart is displayed is a lie.
- **`Entrée` inside a Dialog.** Radix `Dialog` and the `Command` picker both consume Enter. Commit-on-Enter must be scoped to the composer, must not fire while the picker list is open, and every button stays `type="button"`.
- **Empty unit price on a free-text act.** Commits at 0 with an inline warning — never blocks. The dentist may price it later.
- **Double-Enter** produces two identical acts. Allowed (a procedure can legitimately repeat), but show a soft « acte identique déjà saisi » warning.
- **Multi-tooth surfaces/note** apply to every tooth in the act. Needs a one-line hint in the composer, not a model change.
- **Act GUIDs are regenerated on every update** by `SetActs`. Verified safe (no FK references them), but the session list must key on its own client keys, never on `DentalRecordActDto.Id`.

## Test Strategy

Backend tests are **part of this spec, not a follow-up**. xUnit + Moq, following `ClinicManagement.UnitTests/CLAUDE.md`: spec-ID comments, fixed UTC dates, deterministic GUIDs, `Harness` helper pattern.

**New `Features/Patients/DentalRecordActParserTests.cs`**
- `Parse_Accepts_Mixed_Dentition` — permanent 36 + deciduous 75 in one act list succeeds *(AC-6)*
- `Parse_Rejects_Invalid_Tooth_Number` — `[Theory]` over `19, 20, 0, 49, 56, 99` → `Result.Failure`, French message
- `Parse_Rejects_Blank_Procedure_Name` / `Parse_Rejects_Unknown_ResultingCondition` — existing guards still hold
- `BuildToothStates_Emits_One_Entry_Per_Act_Per_Tooth` — one act on `[16, 26]` → 2 entries carrying the record id + intervention date
- `BuildToothStates_Emits_Separate_Entries_For_Two_Acts_On_The_Same_Tooth` — **pins AC-2**
- `BuildToothStates_Skips_Null_And_Sain_Conditions` — consultation/détartrage add no odontogram noise

**New `Domain/DentalRecordActPricingTests.cs`**
- `Act_Preserves_UnitCost_And_IsPerTooth` and `Act_Cost_Is_Rounded_Through_InvoiceCalculator`
- `Record_Cost_Equals_Sum_Of_Act_Costs_With_Millimes` — **pins AC-15**
- `SetActs_Rebuilds_Derived_Teeth_Without_Duplicates` — a tooth in two acts appears once in `Teeth`

**Extend the Create/Update handler tests**
- create + update succeed with a mixed-dentition act list and write both tooth states *(AC-6)*
- update replaces this record's tooth states and clears diagnoses on treated teeth *(existing behaviour, now regression-pinned)*

**Frontend.** Per `features/LEARNINGS.md`, `web/` has no test runner and no ESLint; the gate is `npx tsc --noEmit` **and** `npm run build`, both clean. The `focused → selected` rename is deliberately breaking so `tsc` enumerates every `ToothPaint` consumer.

**Manual verification — the real acceptance gate, run with the dentist**
1. Select 16 → « Dévitalisation » → Entrée → « Couronne zircone » → Entrée. Two rows under `Dent 16`, selection still on 16 *(AC-1, AC-2)*.
2. Select 16 + 26 → « Composite 2-3 faces » → readout `110,000 × 2 dents = 220,000`; flip to forfait → the same field becomes the editable total *(AC-3, AC-4)*.
3. Commit with nothing selected → lands in « Actes généraux » *(AC-5)*.
4. Chart shows a prior « Carie » on 46 dashed; treating 46 clears that diagnosis after save *(AC-11)*.
5. Chart 36, flip to Enfant, chart 75, flip back → both acts listed, chip visible, saves clean *(AC-6, AC-7)*.
6. Open a **pre-existing** multi-tooth record → save immediately → total unchanged *(AC-8)*.
7. Edit a per-tooth act, add a tooth → reprices; do the same to a forfait act → unchanged *(AC-9)*.
8. Add acts one at a time → « Montant payé » follows the total; type in it → stops following *(AC-10)*.
9. Facturer cette intervention on a 2-tooth composite → draft line reads `2 × 110,000`, designation names the teeth *(AC-14)*.
10. From the post-visit bell → `?addRecord=1&appointmentId=…` → save → appointment goes Completed and the prompt stops *(AC-16)*.
11. Window at 1366×768 → single column, no horizontal page scroll *(AC-18)*.

> **Environment note:** `dotnet test` cannot run on this machine — Smart App Control blocks freshly-built DLLs with `0x800711C7` (environmental, not a defect). The backend gate here is a clean `dotnet build`; the new tests must be executed elsewhere or with SAC off before this is considered verified.
