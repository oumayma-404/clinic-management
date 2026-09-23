# Blast radius — wave 1 (groups A–D)

| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `AppointmentPlanLink.ValidateManyAsync` (+ held set, status check on NEW links) | shared validation | Create + Update appointment | must re-test — create still books a live devis step; update of a held link on a closed devis saves |
| 2 | `AppointmentPlanLink.ValidateAsync` (status check) | legacy single-link path | Update (`TreatmentPlanItemId` changed) | unaffected — only runs for a changed link, which must be bookable |
| 3 | `AppointmentPlanLink.RepairHeldLinksAsync` + `ITreatmentPlanRepository.GetByItemIdAsync` | new | Update appointment only | must re-test — edit visit with done act / cancelled devis |
| 4 | `AppointmentProcedureSelection.ResolveAsync(heldRows)` | shared | Create (no held rows), Update | must re-test — archived act visit saves; new inactive act still refused |
| 5 | `DentalRecordLinker.LinkPlanItemAsync` (idempotent re-save, dead step skipped) | shared | Create + Update fiche | must re-test — fiche for step 1 then step 2; re-save on completed devis |
| 6 | `TreatmentPlan.ReleaseDentalRecord(For)` | new aggregate method | Delete fiche, Update fiche | must re-test — delete fiche on live + cancelled devis |
| 7 | `UpdateDentalRecordCommand` releases acts the fiche no longer names | fiche save | patient-record-modal (only updater) | must re-test — « Aucun », two-act séance keeps act 1 |
| 8 | `CreateDentalRecordCommand` passes resolved visit id to the linker | fiche create | the linker | must re-test — create from « Ajouter un acte » on a booked day |
| 9 | `PlanBookingRelease.ReleaseAsync` (strip before cancel, cancel only Scheduled/Confirmed) | shared | Amend, Withdraw, Reassign, + Cancel, Stop, Delete (new) | must re-test — amend removing a booked act still cancels the visit |
| 10 | `PlanBookingRelease.ReleaseStepsAsync` / `FollowActChangeAsync` / `DetachVisitAsync` / `CancelEmptiedAsync` | new helpers | SetSteps, Amend, Update fiche, Cancel/Stop/Delete | covered by unit tests + rows below |
| 11 | `AppointmentRepository.GetByTreatmentPlanItemIdsAsync` excludes disregarded visits | SQL read | projection, in-progress reader, release helpers | must re-test — devis workspace still shows booked séances |
| 12 | `TreatmentPlanRepository` next-séance subquery excludes disregarded | SQL read | « Traitements suivis » sort | unaffected — ordering only |
| 13 | `use-patient-plan-acts.ts` `heldPlanActs` / `planIdByAnyItem` | hook | edit dialog (reads), create dialog (unused) | must re-test — booking from create dialog unchanged |
| 14 | edit dialog hydration `plannedProtocol: null` | tsx | picker default split | must re-test — adding a NEW multi-séance act in edit still offers the split |
| 15 | `use-session-acts.ts` `markBilledOnPlan` (first match only) + `releaseBilledOnPlan` | reducer | patient-record-modal | must re-test — fiche from a booked devis step still locks the price |
| 16 | patient-record-modal séance picker | tsx | new fiche with a stepped devis act and no booked step | new — tier A row |
