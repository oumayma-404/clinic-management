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

# Blast radius — wave 2 (groups E, F + C4)

| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `POST /treatment-plans/start` → `AnyClinicRole` | policy | booking dialogs (both), authorization guard test | must change — guard test moved in the same commit |
| 2 | `DiscardBookingPlanCommand` + `discardUnbookedTreatments` | new | create + edit dialog close, create-dialog patient change | must re-test — E4a/E4b |
| 3 | `planItemToPreset.preselectedStepId` (first unbooked step) | shared builder | workspace « Planifier », « Actes du devis », suggestion notice | must re-test — booking a devis step still preselects |
| 4 | `PlanStepOption.bookedAt` | additive field | picker chips only | unaffected — additive |
| 5 | edit dialog `durationTouched` reset keyed on the act set | tsx | edit dialog only | must re-test — adding an act still re-sums |
| 6 | `treatment-plan-form-modal` hydrate-once, version ref, remise, `installmentsTouched`, « Répartir », blank-name refusal | tsx | workspace amend, plans table edit, patient page create | must re-test — F1/F2/F3/F9/F10 rows |
| 7 | `stepSignature([])` → null | helper | amend no-change check | must re-test — F9 |
| 8 | plans table edit → always amend; `canUseDraftEditor` adds steps; `canDeletePlan` own test | predicates | table menu, workspace delete | must re-test — draft still deletable |
| 9 | `UpdateTreatmentPlanCommand` refuses steps / remise | handler | draft editor (no longer opened for existing plans) | unaffected — only API callers |
| 10 | `TreatmentPlanStepProtocol.ApplyAsync(onlyItemIds)` + `AsConfirmed` | shared | amend (added acts only), Accept (AsConfirmed) | covered by unit tests; IssueDevis/Collect already echoed |
| 11 | `plan-item-steps-dialog` seed once + captured version, arrows disabled | tsx | workspace séances dialog | must re-test — detach still re-seeds |
| 12 | `FicheExtraPlanActs` in Create + Update fiche; `additionalTreatmentPlanItems` | fiche save | patient-record-modal (only writer) | must re-test — C4 row; single-devis-act fiche unchanged (extras empty) |

# Blast radius — wave 3 (G — money)

| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `TreatmentPlan.RespreadSchedule` (keeps dates; no-op without number/rows or when WrittenOff/Cancelled) | shared rule | discount, withdraw, restore, stop, reopen, uncancel, amend, duplicate, peer's `FichePlanActAdditions` | must re-test — remise on a typed schedule; stop → reopen balances; a6's suite green |
| 2 | `RespreadScheduleToTotal` recomputes the total first + optional `refundMethod` | public entry | amend, duplicate, peer's fiche addition | unaffected — idempotent recompute, optional param |
| 3 | `StatusFollowsTheWork` excludes WrittenOff/Cancelled | status predicate | unmark ×2, `SetItemSteps` | must re-test — detach on a live devis still re-derives |
| 4 | negative `InstallmentPayment` (rendu) + `Installment.RecordRefund/Resize(0)/VoidPayment` guards | money ledger | caisse sum/rows/by-method, dashboard, revenue, reconcile, receipt PDF, bridge, cheques, DTO | must re-test — caisse extrait shows a Sortie; sums stay net; receipt + bridge refuse by name; never a cheque |
| 5 | `EnsureNoLiveMoney` / `StopWouldCancel` / discard read receipts, not the net | shared guards | cancel, stop, discard-booking | unaffected when no rendu exists (same answer) |
| 6 | `PlanBillingRules.CashBearingPlanStatuses` in 4 cash reads + `MoneyReconciliationReader` | SQL filters | caisse, extrait, « dont espèces », cheques, reconcile | must re-test — reconcile before/after: +8 WrittenOff plans, 0 DT moved |
| 7 | both « en retard depuis » reads through `InstallmentLateness` | reads | Créances, Solde patient header | unaffected for typed schedules; auto rows stop reading late |
| 8 | `TreatmentPlanItemRequest.PlannedCost` nullable | tri-state request field | create, update, amend, start, fiche additions | must re-test — form always sends a number; blank never reaches the wire |
| 9 | `CollectOnTreatment` checks before minting | handler | fiche save chairside collection | covered by unit test |
| 10 | `FollowDentalRecordDate` from `UpdateDentalRecordCommand` | fiche save | the fiche modal (only updater) | must re-test — redate a fiche with a devis payment |
| 11 | web `planItemToPreset` net cost; `saveActTotal` sends net + remise | booking prefill | both booking dialogs | must re-test — a remise shows on the booking card and survives a re-price |
| 12 | web `displayedOutstanding`/`planNextAction` WrittenOff; `catalogueLineCost`; `usePlanRefundConfirm` (4 call sites) | tsx | workspace, form, odontogram seed | must re-test — rendu dialog on remise / amend / withdraw / stop |

# Blast radius — wave 4 (H, I, J + C4b)

| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `Appointment.Reschedule(dt, sameClinicDay)` — Completed moves, InProgress resets on another day | domain mutator | `UpdateAppointmentCommand` (2 sites), status-transition tests | must re-test — move a « Terminé » visit; drag an « En cours » to tomorrow |
| 2 | `UpdateAppointmentCommand` cancelled-date refusal + future guard + J4 domain catch | handler | every edit-dialog save, agenda drag, quick actions, 4 internal senders (status only) | must re-test — edit notes on a cancelled visit still saves (seconds diff ignored) |
| 3 | `TreatmentPlan.FollowTheActSet` after Add/Remove/Withdraw/Restore | status re-derive | amend, fiche « Ajouter au devis » (a6), park/restore, stop | must re-test — amend adds act to « Terminé » devis; a6's suite green |
| 4 | `TreatmentPlanItem.SetSteps` converts a done step-less act | domain | séances dialog, amend protocol | must re-test — split a done act into 3 |
| 5 | `DentalRecordInvoiceLines.Line.Act` + `SameLines` on act/qty/price | billing guard | fiche re-save on a billed fiche, `BillDentalRecord`, billable-lines read | must re-test — tooth 36→46 on a billed flat act saves; act swap still refused |
| 6 | `TreatmentPlanItem.NextStepDueFromSteps` + `GetStepTimingsAsync` | shared rule + read | devis item DTO, « Traitements en cours », recall facts, in-progress sort key (SQL) | must re-test — list loads (SQL translation), dots per séance |
| 7 | `RecallPlanFact.LastWorkOn/NextStepDueFrom`, `RecallWorklistRules.StalledSince` | recall rule | « À rappeler » list, its `alwaysInclude` set | must re-test — list loads; a recent séance on an old devis is not chased |
| 8 | `TreatmentInProgressDto.DoneStepNumbers` | additive field | treatments-in-progress-list dots | must change — web reads it |
| 9 | `ProcedureTypeRefusals.ArchivedName` on create + rename | refusal | procedure form (create/edit) | must change — web offers « Réactiver » on the code |
| 10 | `POST procedure-types/{id}/activate` | new endpoint | admin catalogue list | must change — list gets « Archivés » filter + action |
| 11 | `DeleteProcedureTypeCommand` → `ProcedureTypeDeletion` body | wire shape | `procedureTypesApi.delete` (only caller) | must change — toast reads the counts |
| 12 | `UpdateProcedureTypeCommand` version-first + « appointments » broadcast | handler | catalogue edit, agenda realtime | unaffected — one save instead of two, same key the fiche save already emits |
| 13 | `Doctor.IsActive` + migration `RetireDoctorsInsteadOfDeleting` | column | roster save, `DoctorDto` (status read), every picker via `useDoctors` | must change — pickers show active only, names resolve on all; verify-schema before/after |
| 14 | `DentalRecordDto.TreatmentPlanItemIds` + `DentalRecordPlanLinkRow.CarriedItemIds` | additive field | fiche modal reopen (`markBilledOnPlan`) | must change — modal marks every carried act |
