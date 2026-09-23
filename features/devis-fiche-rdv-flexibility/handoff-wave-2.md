# Handoff — devis · fiche · RDV flexibility, wave 2 onwards

Start here in a new session. Read this file, then `notes.md` beside it (what wave 1 did and why).

## The goal (owner's words)

« I need the treatment plan, fiche, RDV to work spotless, no bugs whatsoever … the app should not block edits,
because doctor work always needs to refine and edit along the way … unless it is nonsense. »

**Talk to her:** English, short bullets, tables, no paragraphs (`.claude/rules/response-style.md`). She asks
for progress as a table + percentage; give it that way.

## Where things stand (2026-09-23)

| | |
|---|---|
| Audit board (all 76 findings, 10 root causes A–J, with fixes) | https://claude.ai/artifact/93SGuXQNLHUM352jJUP5aW |
| Older 42-row board (rows referenced below as « old NN ») | https://claude.ai/artifact/PGmXJNc91ZymEuNy9gi9LW |
| Wave 1 (A–D) | ✅ committed `ecd8f131` on `feature/security-remediation`, not pushed |
| Progress | 28 / 76 fixes (~37 %) |
| Plan | 4 waves, one commit each: **2 = E + F** · 3 = G (money) · 4 = H + I + J |

## Owner decisions already taken — do not re-ask

1. **Total lowered below what was paid** → the doctor may reduce the amount paid on the devis, with a very short
   warning; recorded as money given back **today** (past caisse days untouched). No « avoir » detour. (wave 3)
2. **Reception booking a multi-séance act** → keep the automatic split as now (doctor can turn it into one
   séance); fix the 403 by letting reception create the draft treatment. (wave 2, E3)
3. **Remise / « Total convenu » changed** → keep the agreed dates; take the difference off the last unpaid
   instalments. Never collapse the schedule into one lump sum. (wave 3)

Also standing rules from memory: no helper/explanation text under controls; never trade a capability for space;
decide reversible calls yourself.

---

## Wave 2 — E + F (edits lost, treatments created by accident)

Already done in wave 1: E1 (stored acts hydrate `plannedProtocol: null`), E2 (plan memo cleared on close),
E5 (plan memo + devis rows dropped on patient change), E7 (suggestion dismissal reset on close).

| # | Bug | Evidence | Fix |
|---|---|---|---|
| E3 | Secretary booking any act with a catalogue protocol → 403 « Le traitement n'a pas pu être préparé » | `TreatmentPlansController.cs:169-170` (`/start` is AdminOrDoctor); `materialiseTreatments` in `use-patient-plan-acts.ts` | Decision 2: open `/start` to `AnyClinicRole` (check the authorization guard tests), keep the split |
| E4 | Slot taken / out of hours → « Retour » leaves a numbered, accepted devis (continuation door) with a live debt, never named | `create-appointment-dialog.tsx:~1049` (`withMintedDevis` only on the generic branch); `ContinueRecordedActCommand.cs:397` | Best: create plans in the same server call as the visit. Minimal: name it on every refusal branch + cancel a numbered orphan / delete a Draft orphan when the dialog closes without a visit |
| E6 | « Planifier la suite » preselects a séance already booked → two visits on one step; the second fiche is silently skipped, work recorded at 0 DT | `plan-next-action.ts:~696` (`nextStepId` ignores bookings) | Preselect the first **unbooked** séance; show « déjà planifiée le X » |
| E8 | Typing an agreed price / ticking a séance resets the visit length | `edit-appointment-dialog.tsx:~1193` resets `durationTouched`; effect `:490` | Reset only when the act set changes |
| F1 | Amend form open + anyone saves anything → typed edits wiped, next save 409 | `app/treatment-plans/[id]/page.tsx:69-77`, `treatment-plan-form-modal.tsx:369-455` (hydrates on `editingPlan` identity), `use-fresh-version.ts:74` | Hydrate once per open, keyed on plan id |
| F2 | Plan with a remise or a parked act (= every Stopped plan) → every amend refused, even a title change; parked acts shown as live | form `:378-412`, `:730-733`, `:838`; `TreatmentPlan.cs:~1810`, `:1487-1491`; mapping `TreatmentPlanMappingExtensions.cs:175,231-233` | Total on `netCost`, filter `isWithdrawn`, show the remise |
| F3 | 409 → « Recharger » refreshes only the version; the stale form then overwrites the colleague | form `:1057-1070`, `:934-946` | Re-hydrate on reload |
| F4 | Séances dialog: a colleague's added séance silently deleted (no 409) | `plan-item-steps-dialog.tsx:155-167, 242` | Capture the version when the dialog opens |
| F5 | Draft editor drops newly typed séances and resets the remise to 0 (200, green toast) | `UpdateTreatmentPlanCommand.cs:76` | Carry steps + discount through, or refuse |
| F6 | « Modifier le brouillon » always refused on a crown/bridge/implant draft; « Accepter le devis » puts back removed séances | old 19, 20 | Browser test must match server; one acceptance door |
| F7 | Adding one act silently re-cuts another act back into catalogue séances | old 23; `TreatmentPlanStepProtocol.ApplyAsync` | Apply the template to added acts only |
| F8 | Clearing an act's name deletes it and cancels its visit, no confirm | form `:808`, `:869`, `:488-503` | Refuse a blank name on an existing line |
| F9 | Save with no change → « Devis modifié » + a new revision on the PDF | form `:823`, `:908-918`; handler `:135-143`, `:281` | Send the schedule only when edited |
| F10 | Lowering the total makes the last instalment 0 → refused; schedule must match to the millime by hand | form `:838-863`; `Installment.cs:93-101`; old 24 | Drop zero rows; « Répartir sur N mois » |
| F11 | Reorder arrows clickable and do nothing when a later séance is done | old 25 | Disable + say why |
| C4 | One fiche can close only one devis act; a visit with 2 devis acts reopens the same fiche on act 1 | `patient-record-modal.tsx:710-717`; `app/patients/[id]/page.tsx:806-827, 1396-1401` | Let one fiche carry several devis acts (server: `DentalRecordLinker` + `PlanCarriedActPricing` zero one act per linked item). Carried over from wave 1 |

## Wave 3 — G (money)

| # | Bug | Evidence | Fix |
|---|---|---|---|
| G1 | Act typed at 0 DT saved at catalogue price everywhere (toast even says 0,000); also blocks editing a continuation line | `TreatmentPlanItemPricing.cs:66`; `TreatmentPlanDto.cs:466` non-nullable; `use-patient-plan-acts.ts:120`; `ContinueRecordedActCommand.cs:312` + `TreatmentPlanItem.cs:311` | `PlannedCost` nullable; fill only when absent |
| G2 | Remise / park / restore / « Total convenu » → agreed schedule becomes one lump sum due today | `TreatmentPlan.cs:1147-1167` (callers ~1695, 1356, 1384); `AmendTreatmentPlanCommand.cs:268-271`; `use-patient-plan-acts.ts:115-126` | Decision 3 |
| G3 | Total below collected → told « par un avoir », which does not exist for a devis | `TreatmentPlan.cs:1831-1840`, `:992-996`; `CreateCreditNoteCommand.cs:23`; `plan-workspace.tsx:~3269` | Decision 1 |
| G4 | Catalogue picker quotes a 3-tooth act once (60 instead of 180) | old 07 (`repricedFor` vs `seedCost`) | One pricing function for all doors |
| G5 | Changing a fiche's date moves note payments but not the devis payment nor the séance date | `UpdateDentalRecordCommand.cs` (MovePaymentsAsync); `CollectOnTreatmentCommand.cs:190-201`; `TreatmentPlanItemStep.cs:151-156` | Move them together |
| G6 | Discount never shown on booking screens | `plan-next-action.ts:~685` (`plannedCost` instead of `itemNetCost`) | Read `itemNetCost` |
| G7 | Write-off erases past cash from closed days; `reconcile-money` cannot see it | old 09, 10; `MoneyReconciliationReader.cs:33` | Split « owes money » from « money moved » |
| G8 | Written-off devis shows « Reste dû » + « Encaisser »; overdue the day after signing | old 13, 14; `plan-next-action.ts:~516`; `TreatmentPlanRepository.cs:~837`, `GetPatientBillingSummaryQuery.cs:~103` | Shared rules (`InstallmentLateness`) |
| G9 | Devis number minted, then the collection refused | `CollectOnTreatmentCommand.cs:228-253` | Check before minting |
| G10 | Price change on a Draft writes a payment row → « Solde à régler » with no quote | `TreatmentPlan.cs:1124, 1162-1167` | Skip re-spread without a number |
| G11 | Séance edit on a written-off plan (API) brings the debt back while still counted as a loss | `TreatmentPlan.cs:756, 869-870, 1952` | Exclude WrittenOff from re-derivation |
| G12 | Written-off devis can still be invoiced (API) | old 17; `CreateInvoiceFromTreatmentPlanCommand.cs:68` | Refuse from `PlanBillingRules` |

## Wave 4 — H + I + J

| # | Bug | Evidence | Fix |
|---|---|---|---|
| H1 | Move a « Terminé » / cancelled visit's date → 200 « enregistré », date not moved | `UpdateAppointmentCommand.cs:247-260`; `Appointment.cs:634` | Allow it (doctor corrects a wrong day) or lock the field with a reason |
| H2 | Add an act to a « Terminé » devis → stays « Terminé »; act can't be booked, fiche refused | `TreatmentPlan.cs:1197-1224`; amend handler; `plan-act-row.tsx:293, 423` | Re-derive status after add/restore |
| H3 | Done 1-séance act → 3 séances refused | `TreatmentPlanItem.cs:571-575` | Convert in place: séance 1 inherits the act's fiche |
| H4 | Billed fiche: fixing a tooth number on a flat act refused | `DentalRecordInvoiceLines.cs` (Designation includes teeth); `DentalRecordBillingGuard.Check` | Compare act + qty + price |
| H5 | « ⋯ » → « Annulé » on a visit with fiche + paid note: one tap, no confirm, counted as absence | `appointment-quick-actions.tsx:76-93`; edit dialog cancel `:846-849` | Confirm + warn |
| H6 | Deleting a fiche leaves the visit « Terminé », « Enregistrer la fiche » gone | `app/patients/[id]/page.tsx:518-519` | Offer the button whenever the visit has no fiche |
| H7 | Lowering chairside collection: save disabled, reason shown, no link to the devis | `patient-record-modal.tsx:~2693` | Add the link (or wave 3 decision 1 removes the lock) |
| H8 | « Traitements en cours » dots wrong for out-of-order séances; « stalled » from day 15 | old 21, 22 | Each séance's own date / due date |
| H9 | « En cours » visit moved to a later day stays « En cours » | `Appointment.cs:630-653` | Reset to planned |
| H10 | Existing visit can't become « suite d'une séance précédente » | old 32 | Same control in edit |
| I1 | Archiving an act is one-way (no reactivate, hidden, name blocked) — visits are no longer frozen since wave 1 | `ProcedureType.cs:280` `Activate()` unused; `procedure-types-table.tsx:92`; `ProcedureTypeRepository.cs:108` | « Réactiver » + archived filter + name check on active only (copy dental acts / medications) |
| I2 | Renaming an act: agenda on other desks keeps the old name; also skips the 409 | `UpdateProcedureTypeCommand.cs:226-259` | Broadcast `appointments`; `SetExpectedVersion` before the first save |
| I3 | Retiring a practitioner = hard delete (wipes who earned past money); still bookable otherwise | `UpdateDoctorsCommand.cs:~126`; `GetUserStatusQuery.cs:110` | `Doctor.IsActive` (migration) |
| I4 | Delete-act dialog doesn't mention devis; usage check loads every visit of the clinic | `procedure-types-table.tsx:590-594`; `DeleteProcedureTypeCommand.cs:69` | Count devis lines; SQL `Any` |
| J1 | Eleven screens show the booked act, not the done one (« À clôturer » worst) | `GetVisitsToCloseQuery.cs:200` | Label « prévu » / « réalisé » |
| J2 | Domain refusals shown as « Erreur… Veuillez réessayer » | `UpdateAppointmentCommand.cs:~701` | Pass the French domain message (⚠️ not EF's English ones) |
| J3 | Ten controls vanish with no reason; « Facturer » missing from the devis menu | old 34, 35 | Keep control, state rule |
| J4 | Long explanation texts: cancel-devis dialog, « Acte non terminé » helper line | wave-1 eye pass | Cut, per the no-explanation rule |
| J5 | Also on the old board, still open: 26 (followed treatment shows « À accepter »), 31 (verify-schema false alarm), 33 (per-tooth switch on a devis line), 36 (séance editor limits), 40 (legacy accept takes no version), 41 (devis screen 409 has no « Recharger ») | old board | see old board |

Left alone deliberately in wave 1: `PlanCarriedAct.NamesAnActTheFicheDoesNotHold` (fiche with a free-text act) —
tightening it would refuse old plan fiches on re-save.

---

## How each wave is done (what worked in wave 1)

1. `ListAgents` + `stack-lease.ps1 status`. The API was last restarted by `clinic-management-d9`; if its owner is
   idle you may restart it (stop the PID on :5000, `claim api`, `dotnet run`, `record api`). Web is a `next dev`
   (owner bf) — it hot-reloads, don't restart it.
2. Write the blast-radius table first (`blast-radius.md`, append a wave section).
3. Fix. For long multi-line edits, write a small Python script to the scratchpad and run it — a bash heredoc
   carrying French text failed once mid-session.
4. Tests beside `api/ClinicManagement.UnitTests/Features/TreatmentPlans/DevisFicheRdvFlexibilityTests.cs`.
   Gate: `BaseOutputPath=<temp> dotnet test api/ClinicManagement.UnitTests/... -c Release` (unfiltered; 4773 green
   after wave 1) + `cd web && npx tsc --noEmit && npm run check:responsive && npm run build`.
5. Restart the API so the browser sees the new code.
6. Browser: `qa/arrange.mjs` (fixtures over the API on throwaway « QA? Flex » patients) → `qa/walk.mjs`
   (one launch, all rows, no source edits) → `qa/eyepass.mjs` (320 · 390 · 820 · 1180 · 1440 · 1536×730) and
   **open the PNGs** — she checks. Add wave-2 rows to `qa/plan.md`; write `qa/run-2.md`, never overwrite run-1.
   Tooling: `npm i playwright-core` into the session scratchpad, `channel: 'chrome'`, headless; `totp.mjs` there
   (the TOTP code is in `/clinic-browser`); `claim browser` / `release browser`.
7. Commit only the wave's own files (`git diff HEAD --numstat` first). Files dirty before this work and NOT ours:
   `.claude/rules/frontend-web.md`, `.claude/rules/verification.md`, `.claude/skills/start-clinic/SKILL.md`,
   `FEATURE-OVERVIEW.md`, `console/tsconfig.json`.

## Traps hit in wave 1 (save yourself the time)

- A new handler dependency breaks positional test constructions → add it as an **optional** constructor parameter
  (`IAppointmentRepository? x = null`); MS DI still injects it.
- Moq returns **null** for a `Task<IReadOnlyList<T>>` it wasn't set up for → guard with `?? Array.Empty<T>()`.
- In the fiche modal, a Radix Select's options must be matched under `[role=listbox]` — the inline act catalogue
  behind it also renders `role=option` rows.
- In a `.mjs` walk, a line starting with `/regex/.test(...)` after a line with no `;` is parsed as division.
- `/appointments?appointmentId=<id>` opens the edit dialog; `/patients/<id>?editRecord=<ficheId>` reopens a fiche;
  `?addRecord=1&appointmentId=<id>` records one from a visit.
- Enum ints: appointment Completed = 4, Cancelled = 5 · plan Completed = 3, Cancelled = 4 · item Planned = 0,
  Done = 1, Withdrawn = 3.
