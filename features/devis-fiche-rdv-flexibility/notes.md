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

# Wave 2 — edits lost, treatments created by accident (E, F + C4)

- **E3** `POST /treatment-plans/start` is `AnyClinicRole`: the draft has no number, no échéancier, no créance.
- **E4** `DiscardBookingPlanCommand` (`POST /{id}/discard-booking`, AnyClinicRole): a plan < 2 h old, no visit on any act, no money → Draft deleted / numbered devis cancelled (« Rendez-vous non créé »). Both booking dialogs call it when they close without the visit saved, and the create dialog on patient change.
- **E6** `planItemToPreset` preselects the first séance with no visit; a booked chip reads « déjà planifiée le … ».
- **E8** the edit dialog re-sums the length only when the ACT SET changes, not on a price or a ticked séance.
- **F1/F3** the devis form hydrates once per open (keyed on the plan id) and saves with the version it was filled from (`hydratedVersionRef`) — the page's live version let a stale form overwrite a colleague with a 200. « Recharger » re-hydrates from the server.
- **F2** parked acts are not in the form (nor in `removeItemIds`); the total is fee − remise; the remise is shown.
- **F4** the séances dialog seeds once and saves with the version captured at seed time.
- **F5/F6** editing an existing plan always goes through amend; `UpdateTreatmentPlanCommand` refuses steps/remise; Accept passes `AsConfirmed(plan)` so the catalogue is never laid back over an act set to one séance.
- **F7** `ApplyAsync(onlyItemIds)` — an amendment protocols only the acts it adds.
- **F8** a blank name on an existing act is refused (the bin removes).
- **F9** the schedule is sent only when edited (`installmentsTouched`); `stepSignature([])` is null.
- **F10** trailing unpaid rows give way when the total drops; 0 rows dropped; « Répartir le solde sur N mois ».
- **F11** séance arrows disabled whenever the move would be refused, with the reason.
- **C4** `FicheExtraPlanActs`: `additionalTreatmentPlanItems` (the visit's other acts of the same devis) are priced 0, linked and charted per act; a re-save reads them back from the devis. ⚠️ Matching is by procedure; an act ADDED to the devis in the same save (peer a6's complaint: « mettre l'acte sur le devis depuis la fiche ») would need an explicit item id — not built.

Tests: `DevisFicheRdvFlexibilityWave2Tests` (11). QA: `qa/run-2.md` GREEN.

# Wave 3 — money (G1–G12)

- **G2** `RespreadSchedule` keeps the agreed dates: a lower total comes off the LAST unpaid rows (a row emptied with no receipts goes), a higher one lands on the last unpaid row, a new auto row only when nothing is left unpaid. `Installment.Resize` changes the amount only (date and `IsAutoRaised` stay).
- **G10** no-op on an un-numbered plan with no rows. **G11** no-op on WrittenOff/Cancelled, and `StatusFollowsTheWork` excludes both — only `Reopen`/`Uncancel` reopen them.
- **G3** « Rendre au patient »: a total below what was collected is refused with code `plan-total-below-collected` (`PlanRefund`); the screen asks once (`usePlanRefundConfirm`) and resends with `refundMethod` (never Cheque). The rendu is a NEGATIVE `InstallmentPayment` dated today on the latest paid rows — every sum nets it on its own day, no past day moves. ⚠️ Not voidable, no receipt, not carried onto a note (the bridge refuses by name), `Cancel` still refused (`HasReceipts`: receipts, not the net). A row paid then fully rendu stays at 0 (deleting it would cascade its receipts away). Wired on amend, remise, « Mettre de côté », « Arrêter » (the zero-work refund branch now has « Rendre et arrêter »).
- **G1** `TreatmentPlanItemRequest.PlannedCost` is `decimal?` — only `null` takes the catalogue tarif; a typed 0 is a price.
- **G4** `catalogueLineCost` / `isPerToothAct` (odontogram-plan-seed.ts): the devis picker prices per tooth like the seed, and follows a tooth-count change on an untouched fee.
- **G5** `TreatmentPlan.FollowDentalRecordDate`: redating a fiche moves the devis money collected at it and the séances it evidences (banked cheque refused, like the note).
- **G6** booking presets read `itemNetCost`; `saveActTotal` sends net + remise, so the remise survives a re-price.
- **G7** `PlanBillingRules.CashBearingPlanStatuses` (debt + WrittenOff) for « money moved » reads; « owes money » stays `DebtBearingPlanStatuses`.
- **G8** WrittenOff: no « Reste dû », no « Encaisser »; both « en retard depuis » reads use `InstallmentLateness`.
- **G9** collection amount checked before the devis number is minted. **G12** a written-off devis cannot be invoiced (create + issue bridge).

Tests: `DevisFicheRdvFlexibilityWave3Tests` (18). reconcile-money before/after: only the 8 written-off plans join the checks (0 DT on them).

# Wave 4 — status and date edits, the catalogue, the sentences (H, I, J + C4b)

- **H1** `Appointment.Reschedule` moves a `Completed` visit (it stays Completed; never past today, refused by name). A cancelled visit is moved only by reactivating it: a real move is refused with `Appointment.CancelledCannotMoveMessage` (a seconds-only difference is still ignored). It used to be skipped and answered 200. The edit dialog locks the date/time of a cancelled visit and says why. A finished visit's redate sends no « déplacé » notice and re-enqueues no reminder.
- **H10** `Reschedule(dt, sameClinicDay)`: « En cours » moved to another clinic day goes back to `Scheduled`; the same day keeps it. The handler decides the day through `ClinicClock`.
- **H3** `TreatmentPlan.FollowTheActSet` after Add/Remove/Withdraw/Restore: an act added to (or brought back onto) a « Terminé » devis reopens it; the last act still to do removed or parked closes it. Through `StatusFollowsTheWork` — a Stopped / WrittenOff / Draft / Cancelled status is left alone.
- **H4** `TreatmentPlanItem.SetSteps` on a done step-less act converts in place: séance 1 inherits the act's date and fiche. It used to be refused.
- **H5** `DentalRecordInvoiceLines.Line.Act` — a billed fiche's edit is compared on act + quantity + price, not the designation text: 36 → 46 on a flat act saves; a different act at the same price is still refused. ⚠️ The issued note keeps its old tooth text (fiscal lines are frozen).
- **H6** « ⋯ » → « Annulé » on a finished or billed visit asks first and names the note.
- **H7** « Enregistrer la fiche » is offered on a « Terminé » visit with no fiche (deleted, or closed by hand).
- **H8** the lowered-collection refusal links to the devis (new tab, the fiche stays open).
- **H9** one rule for « when is the next séance due »: `TreatmentPlanItem.NextStepDueFromSteps` (the séance before the next one, never the latest date), read by the devis, « Traitements en cours » (`GetStepTimingsAsync`, dots from `DoneStepNumbers`) and « À rappeler » (`RecallWorklistRules.StalledSince`: protocol due date, else 14 days after the last work, else after acceptance — it counted 14 days from the signature). `RecallReason.DueSince` for a stall is now the day it stalled.
- **H11** the edit dialog has « C'est la suite d'une séance précédente ? » — same `ContinueSessionDialog`, same `materialiseTreatments` on save.
- **I1** `POST /procedure-types/{id}/activate` + « Afficher les archivés » + « Réactiver ». ⚠️ Decision: the name stays unique per clinic (a DB unique index already says so) — creating or renaming onto an archived act's name is refused with `procedure-type-archived-name`, naming the way back, instead of a second migration for a filtered index.
- **I2** renaming an act: version checked before the (now single) save, and « appointments » broadcast.
- **I3** `Doctor.IsActive` (migration `RetireDoctorsInsteadOfDeleting`, default true): leaving the roster retires, never deletes; sending a retired one back reinstates it. `useDoctors().doctors` is the active roster (pickers), `allDoctors` is history (filters, names); `doctorsForPicker` keeps a record's own retired practitioner selectable. Settings lists « Praticiens retirés » with « Réactiver ».
- **I4** delete archives when a future visit OR a devis line names the act (counts returned and toasted); only the act's own visits are read (`GetByProcedureTypeIdAsync`), not the whole clinic's.
- **J3** booked vs done: `visitActsLine` — « Réalisé » (the fiche's acts) where known (« À clôturer », « Travail non facturé », patient history), « Prévu » on a finished visit otherwise (agenda, dashboard list, invoice badge); « actes prévus » on the day ribbon, « Répartition des actes prévus », CSV « Actes prévus ». `VisitToCloseDto.RecordedProcedures` via `IDentalRecordRepository.GetActNamesAsync`. Also fixed: the phone agenda block printed « 09:00 · » with nothing after it; `appointmentActsSummary` names an act once (« Couronne + Couronne »).
- **J4** `UpdateAppointmentCommand` passes a Domain `InvalidOperationException`'s French message (TargetSite in the Domain assembly); a framework one still gets « Veuillez réessayer ».
- **J5** the devis list menu keeps « Planifier / Modifier / Facturer / Annuler » and states the rule (`amendPlanRefusal`, `billPlanRefusal`, `cancelPlanRefusal`); the devis « ⋯ » has « Facturer le devis » whenever the header does not.
- **C4b** `DentalRecordDto.TreatmentPlanItemIds` (every act of the devis the fiche carries); the patient page adds each to the modal's plan options, and `markCarriedOnPlan` marks those cards « sur le devis » (no price field). With a6's implicit « Ajouter au devis » they read as acts being ADDED, with an editable price the server then dropped.
- **Verified, not recoded**: H2 (delete a fiche on a cancelled devis → 204), J1 (« Le rendez-vous prévu pour cet acte sera libéré. » is true), J2 (wave 3).
- ⚠️ QA trap: a fiche saved with NO visit is tied by `DentalRecordVisitLink` to that day's only visit, which it then closes — fixtures that need an open visit must not share its day with such a fiche.

Tests: `DevisFicheRdvFlexibilityWave4Tests` (18) + 5 existing tests rewritten to the new rules. QA: `qa/run-4.md` GREEN, 26 rows. verify-schema + reconcile-money: no new drift.
