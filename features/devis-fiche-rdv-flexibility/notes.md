# Devis · fiche · RDV never lock each other — wave 1

The audit board: https://claude.ai/artifact/93SGuXQNLHUM352jJUP5aW (76 findings, 10 root causes). Wave 1 = the
four root causes where the doctor gets stuck.

## A — a visit is judged on what it ADDS, never on what it already holds

- `AppointmentPlanLink.RepairHeldLinksAsync` repairs every link the visit already carries before validation: a
  missing plan id is derived from the act (`ITreatmentPlanRepository.GetByItemIdAsync`), a deleted step is dropped,
  a deleted devis line keeps the act and loses the link.
- `ValidateManyAsync(alreadyHeldItemIds)` skips the « still bookable? » checks for held links, and **refuses a NEW
  link** to a non-live devis (`ClosedPlanRefusal`) or a parked act (`WithdrawnItemRefusal`). Before, the server
  accepted any new link and refused every held one — exactly backwards.
- `AppointmentProcedureSelection.ResolveAsync(heldRows)` keeps an archived/deleted catalogue act the visit already
  carries (snapshot fallback). A new inactive act is still refused.
- Client: `usePatientPlanActs` exposes `heldPlanActs` / `planIdByAnyItem` (every item of every plan) for READING
  what a visit holds; `planActs` stays the offer list. Hydrated acts get `plannedProtocol: null` so a stored act is
  never re-split into a new treatment on save.

## B — a fiche re-save is not new work

- `DentalRecordLinker`: work this fiche already evidences is left alone — no re-mark, so no `EnsureActive` on a
  closed devis, and no « next pending step » advanced on every re-save. A step id the act no longer has is skipped.
- `CreateDentalRecordCommand` hands the linker the **resolved** visit (the one stored on the record), so create and
  every re-save read the same séance.
- New fiche with a stepped devis act and no booked step: a « Séance » picker (`pickableSteps`).
- ⚠️ Not changed: `PlanCarriedAct.NamesAnActTheFicheDoesNotHold` still ignores a fiche holding a free-text act —
  tightening it would refuse old plan fiches (free-text act + a catalogued one) on their next re-save.

## C — « Aucun » really detaches

- `TreatmentPlan.ReleaseDentalRecord(For)` — the aggregate's release of a fiche's evidence. On a cancelled or
  written-off devis only the pointers go (the old `UnmarkItem*` refused a void plan, which made deleting such a fiche
  fail with a generic error).
- `UpdateDentalRecordCommand` releases an act the fiche no longer names when the fiche names no devis act, or no
  longer carries that act's procedure — so a genuine two-devis-act séance keeps act 1 while act 2 is attached — and
  drops the visit's link to it (`PlanBookingRelease.DetachVisitAsync`).
- Client: `releaseBilledOnPlan` gives the card its price back on « Aucun »; the dentist's own pick marks the card;
  `markBilledOnPlan` marks ONE card (the server zeroes one act).
- ⚠️ Still open (wave 2): one fiche carrying two devis acts at once.

## D — a devis that changes lets its bookings go

- `PlanBookingRelease` is now called by Cancel, both Stop branches, Delete (draft), step removal
  (`ReleaseStepsAsync` — the visit keeps the act, loses the dead step) and an act swap on a devis line
  (`FollowActChangeAsync`). An emptied visit has its links cleared **before** it is cancelled, and only a visit still
  ahead (Scheduled/Confirmed) is cancelled — a passed slot is never turned into an absence.
- A disregarded visit (« Supprimer (créé par erreur) ») no longer counts as a booking in the devis reads.
- The handlers take `IAppointmentRepository` / `ISender` as optional constructor parameters so existing test
  construction sites compile; the container always supplies them.

Tests: `DevisFicheRdvFlexibilityTests` (14). QA: `qa/run-1.md` GREEN, 22 rows.
