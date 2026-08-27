# Spec: Derive & Confirm — Plan → Record prefill + auto-price plan lines

**Status:** APPROVED
**Type:** Small
**Branch:** feature/windows-desktop-app (existing — per user instruction)

## Context

Closing UX pass ("Derive & confirm" direction). This is the first, tightest slice of a larger
blueprint: make the app *carry forward* data it already knows instead of re-asking. Two related
gaps at the plan↔record↔pricing seam:

- **P0-2 (backend):** A treatment-plan line linked to a dental act (`DentalActCodeId`) is created
  with whatever `PlannedCost` the client sent. Nothing seeds the price from the catalog, even
  though `DentalActCode.DefaultFee` exists, so a devis built from the odontogram/catalog needs the
  fee re-typed.
- **P0-1 (frontend):** Linking a dental record to an open plan step stamps the link but does **not**
  copy the step's designation, cost, or teeth into the record — the dentist re-types the procedure
  name, re-enters the cost, and re-clicks the teeth. Biggest chairside re-entry point.

## What Changes

### P0-2 — Auto-price treatment-plan lines from the linked act's default fee
- When a `TreatmentPlanItemRequest` has a `DentalActCodeId` **and** its `PlannedCost <= 0`, seed the
  planned cost from `DentalActCode.DefaultFee` (when the act belongs to the caller's clinic and has a
  positive fee). A user-entered positive cost is never overwritten; free-text lines (no act code) are
  untouched.
- Applies to both `CreateTreatmentPlanCommand` and `UpdateTreatmentPlanCommand`.
- Shared, tenant-checked resolution via a co-located static helper (mirrors the layer's
  static-helper convention); both handlers inject `IDentalActCodeRepository`.

### P0-1 — Prefill the dental record act from the selected plan step
- Widen `PlanItemOption` (record modal) to carry `designationFr`, `plannedCost`, `toothNumbers`.
- When the user selects a plan step in the record modal, prefill the **focused** act row's
  `procedureName` / `cost` / `toothNumbers` from that step — **only if the row is still empty**
  (never clobber typed input).
- Populate the new fields where `openPlanItems` is built on the patient page.

## Acceptance Criteria

1. Creating/updating a plan line with an act code and no cost → the line's `PlannedCost` equals the
   act's `DefaultFee`.
2. A plan line with a user-entered positive cost is unchanged; a free-text line (no act code) is
   unchanged; an act with no/zero `DefaultFee` leaves the cost as sent.
3. An act code belonging to a different clinic is ignored (no cross-tenant fee leak).
4. Selecting a plan step in the record modal fills the empty focused act row's procedure name, cost
   and teeth; a row already containing input is not overwritten.
5. Linking still marks the plan step "réalisé" on save (existing behavior preserved).
6. `dotnet build` and `npx tsc --noEmit` are clean (0 errors / 0 new warnings).

## Out of scope (later passes)
- Plan/record → invoice "Facturer" convert + reconciliation (P0-3).
- CNAM code carried onto the invoice line (P1-C).
- Completed-visit derivations, waiting-list promote-and-book, El Fatoora auto-queue, compound
  actions, prescription reissue, orphan-page deletion.
- Tests (handled by `/test-small-feature`).

## Reality checks vs. the approved blueprint
- `TreatmentPlanItemRequest.PlannedCost` is a non-nullable `decimal` (not `decimal?`), so "omitted"
  is expressed as `<= 0`, not `null`. A deliberate *gratuit* line that also carries a catalog act
  code will be auto-priced; the plan is a draft, so the dentist can re-zero it. (Auto-approved
  deviation — the request contract can't distinguish "0" from "omitted" without a nullable change,
  which would widen the DTO + FE contract and is out of this slice's scope.)
- The plan-item request carries only `DentalActCodeId` (no `ProcedureTypeId`), so seeding is from
  `DentalActCode.DefaultFee` only — the blueprint's `ProcedureType.DefaultCost` alternative doesn't
  apply here.
