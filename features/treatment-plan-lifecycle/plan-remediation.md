# Implementation Plan — treatment plans, all 61 findings, one pass

**Status:** ✅ **COMPLETE** — all six phases. Browser pass GREEN (`qa/run-2.md`); what shipped is in `notes.md`.
**Source:** `reviews/feature-review.md` (challenged, 2026-09-14 — 61 findings)
**Rule:** nothing is deferred. Every finding — Critical to Suggestion — has a step below, and every step
names the file, the change and how it is proved.

---

## Where we are (read this first on a fresh session)

| Phase | State |
|---|---|
| 1 — Domain | ✅ **done**, full suite green: 4676 passed / 0 failed / 6 skipped (+30 tests) |
| 2 — Application handlers | ✅ **done**, full suite green: 4704 passed / 0 failed / 6 skipped (+28 tests) |
| 3 — Web | ✅ **done** — all 27 steps + M3's and M5's UI halves. `tsc` clean · `check:responsive` 66/66 · `npm run build` ✅ |
| 4 — Capabilities S1–S7 | ✅ **done** — all seven, server **and** browser. Suite 4711/0 · `tsc` clean · 66/66 · build ✅ |
| 5 — Derived guards | ✅ **done** — 6 of 6. Suite 4711/0/6 · `tsc` clean · `check:responsive` **70/70** · build ✅ |
| 6 — Verification + browser pass | ✅ **done** — suite 4710/0/6 · verify-schema + reconcile-money clean · 70/70 · tsc · build · browser pass **GREEN** (73 checks, `qa/run-2.md`) |

**Findings closed: 61 of 61.** — C1 · C6 · C7 · C11 · C12 · M3 · M4 · M5 · M6 · M7 · M8 · M9 · M10 · M11 · M14 ·
m1 · m2 · m3 · m4 · m5 · m6 · m7 · m8 · m9 · m10 · M1/M2 (server half; the row controls are 3.14).
**Server half landed, still needs its UI half:** C2 (dialog wording, 3.7) · C3 (button, 3.5) · C8 (list must
route to the amend modal, 3.1) · M1 · M2 (row controls, 3.14) · M5 (the « changer de patient » control) ·
M6 (the workspace header, 3.20) · the version round-trip from the client (3.27).

### What Phase 1 actually changed (uncommitted, all in `api/`)

| File | Change |
|---|---|
| `Domain/Entities/TreatmentPlan.cs` | +384 −41 — everything in Phase 1 below |
| `Application/…/Commands/ReopenTreatmentPlanCommand.cs` | passes `ClinicClock.ClinicToday()` to the new `Reopen(dueDate)` |
| `UnitTests/Domain/TreatmentPlanReversibilityTests.cs` | **new** — 20 tests covering C2 C3 C7 C8 C11 M2 M5 m3 m7 |
| `UnitTests/Domain/InstallmentLedgerTests.cs` | the cancelled-plan void test re-arranged + 2 new tests for C1 |
| `UnitTests/Domain/TreatmentPlanTests.cs` | `UpdateDetails` on a Completed plan is now **allowed** (M4) |
| `UnitTests/Domain/FollowedTreatmentLifecycleTests.cs` | the scan no longer exempts `TreatmentPlan.cs` and catches the negative spelling |
| `UnitTests/Domain/TreatmentPlanStopAndReopenTests.cs` | 7 `Reopen()` call sites now pass a date |

### New public domain API a new session must know about

| Member | Note |
|---|---|
| `Reopen(DateTime dueDate)` | **signature changed** — it re-spreads now, so the date is required |
| `Uncancel(string reason, DateTime dueDate)` | leaves `Cancelled`; keeps the number, appends the motif |
| `WithdrawItem(Guid itemId, DateTime dueDate)` | per-act « mettre de côté »; refuses the last active act |
| `RestoreItem(Guid itemId, DateTime dueDate)` | its mirror; does **not** re-derive the plan's status |
| `ReassignPatient(Guid patientId)` | refused once any act has delivered work; the invoice check is the handler's |
| `EnsureTotalCoversCollected(remedy)` · `EnsureNoLiveMoney(verb)` | private, the two shared money rules |
| `StatusAfterWork` | private; `StatusAfterWork` = all-active-done → `Completed`, else `OpenStatusFromWork` |

⚠️ **Two decisions taken during Phase 1 that are not in the original plan text:**
1. `StopWouldCancel` gaining `AmountPaid <= 0` made a numbered devis with a deposit and no delivered work
   reachable by neither branch. `StopTreatment`'s « aucun acte réalisé » refusal therefore **branches on
   money** and names the avoir instead of « annulez-le », which `Cancel` would now refuse.
2. `TreatmentPlanItem.Restore()` / `Withdraw()` stayed `internal` — the aggregate is in the same assembly, so
   the plan's earlier « make it public » line was unnecessary.

---

---

## What Phase 4 actually changed (uncommitted)

**Gate, after the last edit:** unfiltered `dotnet test -c Release` ✅ **4711 passed / 0 failed / 6 skipped** ·
`npx tsc --noEmit` ✅ · `npm run check:responsive` ✅ 66/66 · `npm run build` ✅. ⚠️ **No browser pass and no
`verify-schema` diff yet** — both are Phase 6, and this phase ships a **migration**.

### The migration

`20260914193353_AddTreatmentPlanDiscountAndWriteOff` — three additive, defaulted columns
(`TreatmentPlans.WriteOffReason`, `TreatmentPlans.WriteOffAmount`, `TreatmentPlanItems.DiscountAmount`).
**No backfill**, and none is possible or wanted: every existing act gets `DiscountAmount = 0`, so its `NetCost`
equals its `PlannedCost` and `TotalPlanned` is unchanged for every devis in every database — no échéancier
moves, no balance moves, no note moves. Checked for the scaffolded `xmin` columns; it emitted none.

### Per capability

| # | Server | Browser |
|---|---|---|
| **S1** Dupliquer | `DuplicateTreatmentPlanCommand` + `POST /{id}/duplicate` (AdminOrDoctor). Copies the **active** acts, their fees, remises, teeth, procedures and **confirmed steps**; starts `Draft`, **no number**, no échéancier, no fiche links, no billing marks | « Dupliquer ce devis » in the ⋯ menu + an `AlertDialog` naming the two facts that make it safe; the page follows the copy |
| **S2** Remise | `TreatmentPlanItem.DiscountAmount`/`NetCost` · `TreatmentPlan.TotalGross`/`TotalDiscount` · `SetItemDiscount` · `SetTreatmentPlanItemDiscountCommand` + `PUT /{id}/items/{itemId}/discount`. **`TotalPlanned` now sums the NET** — which is what makes the remise reach the échéancier, every balance and the note with nothing else learning the word. Devis PDF prints « Remise » per line + « Sous-total / Remise / Total » | « Remise » control per act row + its dialog; the cost cell shows `net` over `tarif · remise`; the money card's « Total convenu » carries the gross-minus-remise hint |
| **S3** Régler le devis | `SettleTreatmentPlanCommand` + `POST /{id}/settle` (unpoliced, like `RecordInstallmentPayment`). `CollectChairside`'s `dentalRecordId` became **nullable** — same spreading, no séance behind it | `settle-plan-modal.tsx`: one amount, **names the échéances it will land on** before the press, `useFreshVersion` for the token |
| **S4** Créance en perte | `TreatmentPlanStatus.WrittenOff = 6` (appended) · `CarriesDebt` false · `IsLive` false · `WriteOff(reason)` storing **what was abandoned** · `WriteOffTreatmentPlanCommand` + `POST /{id}/write-off` (**AdminOnly** — the only such route on this controller). `Reopen` admits it and **clears** the write-off record | « Passer la créance en perte » in ⋯ (gated on `canWriteOffPlan`) + a motif dialog; `WrittenOff` in the labels, the tones, `planStatusCounts`, `CLOSED_TO_WRITES`, the primary action and `NextSeanceBlock`'s own arm; the money card states what was given up |
| **S5** Intervalle | `TreatmentPlanItemStepDto.EarliestOn`, projected in one ordered pass (`DueFrom` needs the SIBLING's date) | « dès le 3 juin » in the État cell, on `to-schedule` only. **Never « en retard »** — nothing compares it with today |
| **S6** Rattacher | `RelinkTreatmentPlanSeanceCommand` + `POST /{id}/relink-seance`. Reads the fiche id **and its date** before releasing anything, then re-attaches — one transaction | The détacher confirm gains « Rattacher cette séance à », listing acts **and steps** (`relinkTargets`); the button becomes « Détacher et rattacher » |
| **S7** Acte en double | `DuplicateActDetection` (pure) + `TreatmentPlanDto.DuplicateActs`, on the **single-plan read only**. Keys on procedure + tooth, both sides debt-bearing | A `role="note"` warning on the acts card naming the other devis, as a link |

### Guards that fired, and what each was told

All five were **correct** and each was answered rather than silenced:

| Guard | Answer |
|---|---|
| `StartTreatmentStepsTests` (step copy has 4 args) | The comment inside the call carried commas; moved above it. The copy always passed `MinDaysAfterPrevious` |
| `StepProtocolCoverageTests` | **First entry ever** in `AppliesItElsewhere`, argued in writing: the duplicate copies the source's **confirmed** steps, and `ApplyAsync` would overwrite them with today's catalogue — the defect `ContinueRecordedActCommand` documents |
| `PlanVersionCoverageTests` | `DuplicateTreatmentPlanCommand` exempted — it **inserts**; the source is read, never written |
| `TreatmentPlansControllerAuthorizationTests` | A **third** array, `AdminOnlyActions`, with its own theory: `WriteOffPlan` is the one route narrower than AdminOrDoctor. The other four classified |
| `TreatmentPlanStopAndReopenTests` | `Reopen`'s refusal legitimately reworded (it admits `WrittenOff` now) |

### Decisions taken during Phase 4 that are not in the plan text

1. **`TotalPlanned` became the NET total** rather than a gross with a separate net beside it. Every money read
   in the product is built on that one figure, so this is what makes S2 reach the échéancier, « Créances »,
   « Solde patient », la caisse and the note d'honoraires at once. The three real call sites moved with it
   (`CreateInvoiceFromTreatmentPlanCommand` bills `NetCost`; the PDF prints both).
2. **S4 stores `WriteOffReason` + `WriteOffAmount`** — the plan asked only for a status. A loss with no reason
   is not evidence, and the figure cannot be re-derived afterwards (the plan leaves every debt read, and
   `Outstanding` goes on moving).
3. **S4 is reversible through `Reopen`, not a new verb.** A second absorbing state is the defect C3 existed to
   fix; the operation is identical (restore the parked acts, re-derive, re-spread).
4. **S4 is refused on a bridged devis** — the note carries the créance there, so writing off the plan moves
   nothing at all: a green toast over an unmoved figure.
5. **S6 is within one devis.** Moving a séance to another plan is two aggregates, two totals and two
   échéanciers — a different operation with a different blast radius.
6. **S7 is served on the workspace read only.** The list would need one extra read per row, and the answer is
   only actionable where the other document can be named and opened.
7. **S2's remise is its own command**, never a field on `amend`: that form sends every act's cost on every
   save, so folding it in would let an older caller clear a remise silently — the `DentalRecordAct` tri-state
   trap, three times paid for.

### Still open

- **Phase 6** — the whole verification pass: `verify-schema` **before/after the migration**, `reconcile-money`,
  the e2e suite, `/test-in-browser`, and the eye pass at five widths. Nothing in Phases 3 or 4 has been seen in
  a browser.

---

## What Phase 3 actually changed (uncommitted, all in `web/`)

**Gate, run after the last edit:** `npx tsc --noEmit` ✅ clean (whole tree) · `npm run check:responsive`
✅ **66/66** · `npm run build` ✅ compiled. ⚠️ **No browser pass yet** — that is Phase 6.5, and M23 · M22 · m17
are only provable at 320 px.

| File | Steps |
|---|---|
| `lib/api/types.ts` | `TreatmentPlanDto.doctorId` + `doctorName` (M6's wire half) |
| `lib/api/treatment-plans.ts` | **3.27** — `complete` · `recordInstallmentPayment` · `reviseInstallments` · `reorderItems` · `cancel` round-trip `version`; **6 new doors**: `uncancel` · `withdrawItem` · `restoreItem` · `detachNote` · `reassignPatient` · `setDoctor` |
| `components/treatment-plans/treatment-plan-labels.ts` | **M25's owner** — `installmentDueLabel()` + `installmentDueInputValue()` |
| `components/treatment-plans/plan-next-action.ts` | **3.10** (`planStatusCounts` derived from `PLAN_STATUS_LABELS`) + the 7 plan permissions: `canAmendPlan` · `canUseDraftEditor` · `canDeletePlan` · `canBillPlan` · `stopWouldCancelPlan` · `canCancelPlan` · `canUncancelPlan` |
| `components/treatment-plans/treatment-plans-table.tsx` | **3.1** · **3.2** · **3.3** · **3.4** · **3.9**½ · **3.25/3.26**. The two row menus merged into one `planMenuItems()` |
| `components/treatment-plans/plan-act-row.tsx` | **3.11** (M17) · **3.12** (M18) · **3.13** (M19) · **3.14** triggers (`PlanActWithdrawAction`, `onWithdraw`/`onRestore`) · **3.24** (m14) |
| `components/treatment-plans/plan-workspace.tsx` | **3.5** · **3.6** · **3.7** · **3.11** (M16) · **3.14** dialogs · **3.19** · **3.20** · **3.21** (m11) · **3.22** (m12) · **3.23** · **3.25** · **3.26** · **3.27** · **M3's UI** · **M5's UI** |
| `components/treatment-plans/treatment-plan-form-modal.tsx` | **3.8** (C10 — `useConflict` + « Recharger »; the hand-rolled `conflictMessage` deleted) · **3.17** (M23) · **3.25** (m15, the dialog title) |
| `components/treatment-plans/revise-installments-modal.tsx` | **3.17** (M23) · **3.18** (M25 — an auto-raised row opens **blank**; leaving it blank sends the stored instant back untouched) · **3.27** |
| `components/treatment-plans/installment-payment-modal.tsx` | **3.18** (M25) |
| `components/treatment-plans/void-installment-payment.tsx` | **3.18** (M25) — prop changed from `installmentDueDate: string` to `installment: InstallmentDto` |
| `components/treatment-plans/continue-session-dialog.tsx` | **3.16** (M22 — `DialogBody`) |
| `components/treatment-plans/patient-plans-strip.tsx` | **3.15** (M20 — « Accepter le devis » confirms) |
| `components/treatments/treatments-in-progress-list.tsx` · `app/treatment-plans/[id]/page.tsx` · `components/odontogram.tsx` | **3.9** (M12/M13 — `Patients` added to the first two, `TreatmentPlans` to the odontogram; the three false comments corrected) |

### Decisions taken during Phase 3 that are not in the plan text

1. **The plan-level predicates were extracted, not re-written per surface.** 3.1–3.4 are all rules the
   workspace already had inline; copying them into the table would have been the repo's own dominant defect
   shape. They live in `plan-next-action.ts` and both surfaces read them.
2. **`treatment-plans-table.tsx`'s two row menus are now one function.** They had already drifted
   (« Planifier la prochaine séance » existed only on the card) — the defect 3.3/3.4 would have reproduced
   twice.
3. **M3's and M5's UI halves were added**, though Phase 3 lists a step for neither. Both commands shipped in
   Phase 2 with no browser caller, which is the « named remedy with no route » the findings are about. M5 sits
   in the workspace's « ⋯ » menu beside « Changer le praticien », with a **server-side** patient search.
4. **3.7 does NOT say « Cette action est irréversible ».** With C3 shipped that is false — the devis can be
   rétabli. What *is* irreversible is the number, and that is what the sentence says. It also states the money
   fact positively (« aucun encaissement n'y est enregistré »), which C2's money term makes true.
5. **3.4's gate is `canCancelPlan`** = numbered · not cancelled · no live money · **and** the stop branch would
   not already cancel it. Offering both on one plan re-creates the two buttons asking a question the system has
   already answered.
6. **3.18 went further than « leave the date empty ».** `revise-installments-modal` is the half of M25 that
   **writes**: a pre-filled auto-raised date re-saved becomes a date the dentist appears to have typed. A blank
   row now sends its stored instant back untouched; typing one is the deliberate act of turning the balance
   into a schedule.
7. **3.23's verb was moved, not removed.** « Reprendre le traitement » leaves the filled primary on a
   `Completed` plan and lands in the « ⋯ » menu. On a `Stopped` plan it stays the primary — there it *is* the
   answer to « et maintenant ? ».

### Still open before this feature is done

- **Phase 4** — S1–S7 (duplicate · remise · régler en un paiement · write-off · interval hint · re-link ·
  duplicate-act notice).
- **Phase 5** — ⚠️ **the plan's `N38` is already taken**: `check:responsive` ships
  `act-removal-has-one-owner N38`. The two new web guards must be **N39** `plan-money-rules-have-one-owner` and
  **N40** `installment-date-has-one-owner`; the four backend guards' numbering is unaffected.
- **Phase 6** — the whole verification pass. Nothing in Phase 3 has been seen in a browser.

---

### What Phase 2 actually changed (uncommitted, all in `api/`)

| File | Change |
|---|---|
| **New** `Application/…/TreatmentPlans/PlanBridgeLookup.cs` | « does a note represent this plan? », shared by the record **and** the void paths (C12) |
| **New** `Application/…/TreatmentPlans/PlanBookingRelease.cs` | the amend handler's private `ReleaseBookingsAsync`, **moved** out so reassign reuses it — plus m10 (a `Cancelled`/`NoShow` visit's dangling plan links are cleared) |
| **New** `Application/…/Patients/ToothChartingSync.cs` | re-charts the fiches an act evidences when its done-ness changes (M10 · M11) |
| **New** 6 commands | `Uncancel` · `WithdrawTreatmentPlanItem` · `RestoreTreatmentPlanItem` · `DetachTreatmentPlanNote` · `ReassignTreatmentPlanPatient` · `SetTreatmentPlanDoctor` |
| `TreatmentPlan.cs` | `DetachNote(invoiceId)` — the plan half of « détachez-la de ce traitement » |
| `TreatmentPlanItem.cs` | `ClearBilledOnInvoice()` (internal); the stale `EnsureNotBilledAsync` citation corrected |
| 8 command files | `uint Version` + `SetExpectedVersion` (M9, and `MarkItemDone` besides the plan's five) |
| `VoidInstallmentPaymentCommand` · `SetInstallmentPaymentBankedCommand` | `when (ex is not ConflictException)` (M8) + the void's bridge refusal (C12) |
| `CompleteTreatmentPlanCommand` | drops `leaveUnrealisedActs: true` (m1) |
| `AmendTreatmentPlanCommand` | `EnsureNotBilledAsync` and its `IInvoiceRepository` **deleted** (m2 / AC-17) |
| `DeleteTreatmentPlanCommand` | two refusals, each naming a route that exists (m4) |
| `NoteCarriedActGuard` | both hand copies call `ContinuationTracking` (m9) |
| `ContinuationTracking` | `TracksStatus` + `TrackingStatuses` (the SQL-readable form) |
| `TreatmentPlanRepository` | `CountAcceptedByDateAsync` + one shared accepted-date predicate (M14); the plan-links read filters cancelled devis (m5); `CountUnansweredDraftsAsync` takes a grace cutoff (m8) |
| `RecallWorklistRules` | `NeverAnswered` + `UnansweredGraceCutoff` — one owner, and `IsStalled` now yields to it (m8) |
| `DashboardActivityReader` · `DashboardAlertsReader` | the two counts read the new owners |
| `TreatmentPlanDto` · `…MappingExtensions` · `…WorkflowProjection` | `DoctorId` + `DoctorName` (M6) |
| `TreatmentPlansController` | 6 new routes, all `AdminOrDoctor`; `complete` takes an optional versioned body |
| **New** `UnitTests/…/PlanVersionCoverageTests.cs` | the derived guard: every plan-writing command declares a version, no catch-all swallows a 409 |
| **New** `UnitTests/…/PlanReversibilityHandlerTests.cs` | 18 handler tests over the six new doors and the two money refusals |
| 5 existing test files | call sites + the two recall fixtures, whose old semantics were the defect |

⚠️ **Three decisions taken during Phase 2 that are not in the original plan text:**
1. **2.4 (C6) was already done** — `CreateInvoiceFromTreatmentPlanCommand` already raises a supplementary note
   for `TotalPlanned − Σ live linked notes` and refuses only when that difference is ≤ 0. No change was needed;
   what was corrected is the three places that still cited the deleted guard as live.
2. **`MarkTreatmentPlanItemDoneCommand` gained a version too**, beyond 2.1's list of five. It writes an existing
   plan and can auto-complete it, so `PlanVersionCoverageTests` flagged it — and the guard is right.
3. **m8's direction is reversed from the plan text.** « Un devis numéroté que le patient n'a jamais répondu »
   cannot exist (`Accept` is the only writer of `Number`, so a number means it *was* answered), so the rule is
   **« rien de livré »**: unanswered = never started, stalled = started and abandoned, and `IsStalled` yields to
   `NeverAnswered` rather than the other way round. Written the old way the worklist reason was unreachable
   while the dashboard's count of the same question fired on every fresh treatment.

## 0 · Before the first edit

| Do | Why |
|---|---|
| `stack-lease.ps1 status -Session <me>` + `ListAgents` | peers share the ports, the database and this tree |
| `dotnet run -- verify-schema` → save output | the before-half of the migration diff (S2 adds a column) |
| `git status --short` | 40+ files are already dirty; never `git add -A` |
| Write the blast-radius table below into the commit body | `regression-safety.md` § 1 |

**Blast radius — the shared symbols this plan touches**

| Symbol | Other consumers | Verdict |
|---|---|---|
| `TreatmentPlan.Cancel` | Cancel cmd, Stop cmd's branch | must re-test both |
| `RespreadSchedule` | Stop, Reopen, amend's no-schedule branch | must re-test all three |
| `StopWouldCancel` | domain, stop handler, workspace dialog | mirrored — change all three together |
| `PlanBillingRules.DebtBearingPlanStatuses` | 5 repo reads, caisse, dashboard, créances | **do not touch** — read only |
| `planItemState` / `schedulablePlanItems` | act row, workspace, booking dialogs, N26/N28 | must re-test booking |
| `TreatmentPlanDto` | 7 web surfaces + PDF | additive fields only |
| `ToothChartingRules.ChartableActs` | 2 fiche commands | adding callers, not changing it |
| `RealtimeResource` keys | `clinic-hub.ts` + `RealtimeResourceResolverTests` | both directions asserted |

---

## Phase 1 — Domain: the money invariants (C1 · C7 · C11 · C2 · C3 · M4 · m3 · m6 · m7)

One file, `api/ClinicManagement.Domain/Entities/TreatmentPlan.cs`. Land it first: everything else reads it.

| Step | Change (Phase 1 — ✅ ALL DONE) |
|---|---|
| 1.1 **C7 — one respread rule** | Move the `TotalPlanned < AmountPaid` throw **into** `RespreadSchedule:932`, at the top. Delete the duplicate from `StopTreatment:847`; keep `ReviseInstallments:1160`'s (it fires before its own rebuild). Correct `TreatmentPlanItem.Revise`'s docstring, which still asserts the old behaviour. |
| 1.2 **C11 — `Reopen` re-spreads** | `Reopen(DateTime dueDate)`: after `RecomputeTotal()`, call `RespreadSchedule(dueDate)`. Caller (`ReopenTreatmentPlanCommand`) passes `ClinicClock.ClinicToday()`. ⚠️ Never `DateTime.Today`. |
| 1.3 **C1 — `Cancel` refuses live money** | New private `EnsureNoLiveMoney(string verb)`: throws when `_installments.SelectMany(i => i.Payments).Any(p => !p.IsVoided)`, naming the amount and the avoir, in `StopTreatment`'s wording. Call it from `Cancel:1291`. |
| 1.4 **C2 — `StopWouldCancel` knows about money** | `StopWouldCancel => Number != null && AmountPaid == 0m && !_items.Any(i => !i.IsWithdrawn && i.HasDeliveredWork)`. A devis carrying a deposit now takes the **stop** branch, which 1.1 already guards. Update its docstring. |
| 1.5 **C3 — leave `Cancelled`** | New `Uncancel(string reason)`: allowed from `Cancelled` only, requires a motif, appends it to `CancellationReason` (never erases the original), sets `Status = OpenStatusFromWork`, `RevisionNumber++`, re-spreads. `Number` is untouched — the series stays gapless. |
| 1.6 **M4 — one amendable window** | `UpdateDetails:147` drops its own status list and calls `EnsureAmendable()`. Title/notes are then correctable on `Stopped` and `Completed`, exactly like prices. |
| 1.7 **m3 — reorder the live acts** | `SetItemOrder:1220` compares against `ActiveItems` (withdrawn acts keep their sequence, appended after). Message names the mismatch. |
| 1.8 **m6 — one open-status owner** | `SetItemSteps:661` calls `OpenStatusFromWork` via the same `ActiveItems.All(Done) ? Completed : OpenStatusFromWork` shape the other three callers use. Extract that expression as `private StatusAfterWork`. |
| 1.9 **m7 — one live-status list** | The four copies (`:150, 772, 814, 1327`) all read `TreatmentPlanLifecycle.LiveStatuses.Contains(Status)`. Extend `FollowedTreatmentLifecycleTests`' scan to cover `Domain/Entities`. |
| 1.10 **M2 — per-act park / restore** | `WithdrawItem(Guid itemId)` (refuses `HasDeliveredWork`, refuses the last active act) and `RestoreItem(Guid itemId)`; both `RecomputeTotal()` + `RespreadSchedule(dueDate)` + `RevisionNumber++`. `TreatmentPlanItem.Restore()` becomes `public`. This is the capability behind the driving complaint. |
| 1.11 **M5 — reassign the patient** | `ReassignPatient(Guid patientId)`: refuses when any act `HasDeliveredWork`. The invoice check belongs to the handler (2.9). |
| 1.12 **C8 (domain half)** | `SetItems:198` throws when `_items.Any(i => i.HasSteps || i.HasDeliveredWork)`, naming « Modifier les actes et les prix ». |

**Proof:** full `BaseOutputPath=<temp> dotnet test … -c Release`, unfiltered. New tests: cancel-with-money
refused · stop-with-deposit takes the stop branch · reopen leaves `Σ Amount == TotalPlanned` · uncancel from
Cancelled · per-act park/restore arithmetic · `SetItems` refusal · reassign refusal.

---

## Phase 2 — Application: handlers, concurrency, charting (C6 · C12 · M3 · M6 · M7 · M8 · M9 · M10 · M11 · M14 · m1 · m2 · m4 · m5 · m8 · m9 · m10)

| Step | Change (Phase 1 — ✅ ALL DONE) |
|---|---|
| 2.1 **M9 + M7 — version everywhere** | Add `uint Version` + `_unitOfWork.SetExpectedVersion(plan, request.Version)` to `Cancel`, `Complete`, `RecordInstallmentPayment`, `ReviseTreatmentPlanInstallments`, `SetTreatmentPlanItemOrder`. ⚠️ `0` still means « not supplied », so jobs and older callers are unaffected. Web callers pass `plan.version` (Phase 3). |
| 2.2 **M8 — stop flattening 409s** | `VoidInstallmentPaymentCommand:105` and `SetInstallmentPaymentBankedCommand:108` gain `when (ex is not ConflictException)`. Must land with 2.1 or the 409 stays unreachable. |
| 2.3 **C12 — void on a bridged plan** | Lift `RecordInstallmentPaymentCommand:111-119`'s bridge lookup into a shared `PlanBridgeLookup.RepresentingNoteAsync(...)`; call it from both. Void's refusal names the note and points at the invoice's own void. |
| 2.4 **C6 — the surplus is billable, not refused** | Do **not** restore a guard. `CreateInvoiceFromTreatmentPlanCommand` gains a supplementary-note path when `plan.TotalPlanned - linkedInvoiceTotal > 0`; it bills the delta only and links the new note to the plan. `canBill:404` already computes that condition. |
| 2.5 **m2 + AC-17** | Delete `AmendTreatmentPlanCommandHandler.EnsureNotBilledAsync` and its now-unused `IInvoiceRepository` field/ctor arg. Correct `TreatmentPlanItem.cs:235` and `ContinueRecordedActCommand.cs:243`, which cite it as live. Fix the `Application/CLAUDE.md:57` row that argues both sides. |
| 2.6 **M3 — detach the note on its own** | `DetachTreatmentPlanNoteCommand` (AdminOrDoctor) calling `TreatmentPlanBridgeRelease.DetachAsync`, so « détachez-la de ce traitement » names a command that exists. Audited. |
| 2.7 **M10 + M11 — re-chart both ways** | Extract `ToothChartingSync.ApplyAsync(plan, item, records…)` and call it from `MarkTreatmentPlanItemDoneCommand`, `UnmarkTreatmentPlanItemDoneCommand`, `UnmarkTreatmentPlanItemStepCommand` and `StopTreatmentPlanCommand`'s parking. The rule stays `ChartableActs`; only the caller set grows. |
| 2.8 **M6 — the dentist is visible and changeable** | `TreatmentPlanDto` gains `doctorId` + `doctorName`; `SetTreatmentPlanDoctorCommand` (audited). Read on the workspace header (Phase 3). |
| 2.9 **M5 — reassign handler** | `ReassignTreatmentPlanPatientCommand`: refuses on delivered work **and** on any non-cancelled linked invoice; releases bookings the way the amend handler does. |
| 2.10 **C3 — uncancel handler** | `UncancelTreatmentPlanCommand` + `POST /{id}/uncancel`, AdminOrDoctor, versioned, audited. |
| 2.11 **M1 + M2 — park/restore handlers** | `WithdrawTreatmentPlanItemCommand` / `RestoreTreatmentPlanItemCommand`, versioned. `EnsureItemRemovable`'s refusal sentence now names « mettez-le de côté » — a remedy that exists. |
| 2.12 **M14 — accepted-this-month** | `DashboardActivityReader:55` + its drill-through count `Number != null && AcceptedDate in period`, one shared predicate. Both must move together or the list disagrees with the tile. |
| 2.13 **m1 — close the `Complete` hole** | `CompleteTreatmentPlanCommand` drops `leaveUnrealisedActs: true` (that case is « Arrêter »). ⚠️ Keeps the endpoint and the automatic path — spec AC-11 / API contract. |
| 2.14 **m4 — the delete refusal names a real route** | `DeleteTreatmentPlanCommand:65` branches: `Completed`/`Stopped` → « clôturé, il se conserve » ; `Accepted`/`InProgress` → « arrêtez le traitement » (not « annulez », which C5 leaves unreachable). |
| 2.15 **m5 · m9 — one continuation predicate** | `GetPlanLinksByDentalRecordAsync:393` filters cancelled plans; `NoteCarriedActGuard:65,92` call `ContinuationTracking.Tracks` instead of its two hand copies. |
| 2.16 **m8 — one unanswered-devis rule** | `RecallWorklistRules.IsUnanswered` keys on `Number != null && AcceptedDate == null`-equivalent semantics (a quote the patient never answered), and `CountUnansweredDraftsAsync:451` calls the same rule. Two askers, one owner. |
| 2.17 **m10 — clear stale item links** | `ReleaseBookingsAsync` also nulls `TreatmentPlanItemId` on `Cancelled`/`NoShow` visits. |

**Proof:** full suite + new handler tests per step. Then `dotnet run -- reconcile-money` and
`verify-schema` (money and schema both move).

---

## Phase 3 — Web: the surfaces (C4 · C5 · C8 · C9 · C10 · M12 · M13 · M15–M20 · M22–M26 · m11–m17)

| Step | Change (Phase 1 — ✅ ALL DONE) |
|---|---|
| 3.1 **C8 — the list's edit** | `treatment-plans-table.tsx:505` passes `amendMode` (and `:679-687` renders it so). |
| 3.2 **M24** | Both draft entries gate on `isDraft && !planHasRecordedWork(p)`, matching the workspace. |
| 3.3 **C9** | Add « Facturer le devis » to the ⋯ menu, gated on `canBill`. Correct the false comment at `:893`. ⚠️ Deviates from AC-11's five-entry list — noted deliberately. |
| 3.4 **C5** | Add « Annuler le devis » back to the ⋯ menu **only** for a numbered, non-cancelled plan where the stop branch cannot reach it, with the motif field. ⚠️ AC-11 said absent everywhere — this is a deliberate reversal, recorded in `notes.md`. |
| 3.5 **C3** | « Reprendre ce devis annulé » as the primary action on `Cancelled`, wired to `/uncancel`. |
| 3.6 **C4** | `NextSeanceBlock` gains a `Cancelled` arm before `!nextAct`: « Devis annulé » + the motif. |
| 3.7 **C2** | The cancel branch states the encaissé amount and « Cette action est irréversible ». |
| 3.8 **C10** | The devis form modal moves to `useConflict` + a « Recharger » action on `FormErrorBanner`, keeping the no-silent-retry rule and the escalated second message. |
| 3.9 **M12 · M13** | Add `RealtimeResource.Patients` to `treatments-in-progress-list`, `treatment-plans-table`, `app/treatment-plans/[id]/page`; add `TreatmentPlans` to `odontogram.tsx:297`. Correct the three comments that claim the current keys suffice. |
| 3.10 **M15** | `planStatusCounts`'s `order` gains `Stopped`. Derive the array from `PLAN_STATUS_LABELS` so a fourth mirror cannot drift. |
| 3.11 **M16 · M17** | The workspace's `schedulableItems` calls `schedulablePlanItems(plan)`. The act row and card render a « mis de côté » marker with « Remettre au devis » (3.14). |
| 3.12 **M18** | The `scheduled` / `to-record` arms read the **step**'s appointment (`nextStepOf(item)?.scheduledAppointmentId ?? item.scheduledAppointmentId`), matching the badge. |
| 3.13 **M19** | The null tail returns a muted sentence naming why there is nothing to do. |
| 3.14 **M1 · M2 (UI)** | Per-act « Mettre de côté » / « Remettre au devis » on the row, with an `AlertDialog` naming what is kept. |
| 3.15 **M20** | « Accepter le devis » on the patient band routes through the same `AlertDialog` the workspace uses. |
| 3.16 **M22** | `continue-session-dialog` uses `DialogBody`. |
| 3.17 **M23** | Both échéance rows get `flex-wrap` + a real `basis-*`, and `min-w-0` on the truncating child. |
| 3.18 **M25** | The three surfaces call `InstallmentDueCell`'s owner (`installmentDueLabel`) instead of `formatDateFr(dueDate)`; the revise modal leaves an auto-raised row's date empty with « Solde à régler ». |
| 3.19 **M26** | The « pourquoi Encaisser a disparu » notice is derived from `canCollectInstallments`, naming the actual reason (facturé · brouillon · annulé). |
| 3.20 **M6 (UI)** | The workspace header shows the praticien, editable through `SetTreatmentPlanDoctorCommand`. |
| 3.21 **m11** | The detach confirm becomes an `AlertDialog`. |
| 3.22 **m12** | « Motif d'annulation (obligatoire) » + an `aria-describedby` hint on the disabled confirm. |
| 3.23 **m13** | No filled primary on a `Completed` plan; « Reprendre le traitement » moves to the ⋯ menu. |
| 3.24 **m14** | Drop the `title`; the row's visible text uses `detachOutcome`. |
| 3.25 **m15 · m16** | One `planDisplayName` / one label source: « Modifier les actes et les prix » and « Sans devis » everywhere, including the form modal title. |
| 3.26 **m17** | Money figures wrap instead of `grid-cols-2` at 320 px; the clickable `TableRow` gets a keyboard path; plan naming goes through `planDisplayName`. |
| 3.27 **2.1 client half** | Every command that gained a version round-trips `plan.version` from `web/lib/api/treatment-plans.ts`. |

---

## Phase 4 — Capabilities (S1–S7)

| Step | Change (Phase 1 — ✅ ALL DONE) |
|---|---|
| 4.1 **S1** | `DuplicateTreatmentPlanCommand` — copies items + steps, starts `Draft`, no number. Button in the ⋯ menu. |
| 4.2 **S2** | `TreatmentPlanItem.DiscountAmount` (millimes, ≤ `PlannedCost`) + `TreatmentPlan.TotalDiscount`. **Migration** — check it for the scaffolded `xmin` columns before applying. Printed on the devis PDF and reportable. |
| 4.3 **S3** | « Régler le devis » — one modal calling `CollectChairside` through a new `CollectOnPlanCommand`, spreading over the échéancier. |
| 4.4 **S4** | `WriteOffTreatmentPlanCommand(reason)` → a new `WrittenOff` status, `CarriesDebt` false, `IsLive` false. ⚠️ Appended, never inserted; `TreatmentPlanStatusCoverageTests` must be green on it. |
| 4.5 **S5** | The act row states « pas avant le 3 juin » from `MinDaysAfterPrevious`; the booking dialog warns (never refuses). Never the word « en retard ». |
| 4.6 **S6** | The detach confirm offers « …et la rattacher à : [acte · étape] », one save. |
| 4.7 **S7** | A notice (never a refusal) when the same `procedureTypeId` + tooth appears on two debt-bearing plans. |

---

## Phase 5 — Guards, so none of this comes back ✅ **done**

| Guard | Fails on | State |
|---|---|---|
| `check:responsive` **N39** `installment-date-has-one-owner` | a surface formatting an échéance's `dueDate` itself | ✅ new |
| `check:responsive` **N40** `plan-money-rules-have-one-owner` | an act's fee read from `plannedCost`; a hand-written live-money or « not cancelled » test | ✅ new |
| `check:responsive` **N41** `plan-status-maps-agree` | a devis status with a label and no tone, or the reverse | ✅ new |
| `check:responsive` **N42** `plan-surfaces-follow-the-money-keys` | a surface watching `treatmentplans` and not `patients` | ✅ new |
| `TreatmentPlanStatusCoverageTests` | `WrittenOff` unclassified by `CarriesDebt` / `LiveStatuses` | ✅ landed with Phase 4 |
| `FollowedTreatmentLifecycleTests` (extended to `Domain/Entities`) | a fifth inline copy of `LiveStatuses` | ✅ landed with Phase 1 |
| `PlanVersionCoverageTests` | a treatment-plan command with no `SetExpectedVersion` | ✅ landed with Phase 2 |

### Deviations from the plan text

1. **The numbering.** `N38` was taken (`act-removal-has-one-owner`), and `treatment-plan-labels.ts`'s own
   docstring had already promised **N39** to the installment date — so N39 is the date and N40 the money,
   the opposite of the « Still open » note. The source claim was the older one; the plan text moved.
2. **The realtime guard is N42 in `check:responsive`, not an extension of `RealtimeResourceResolverTests`.**
   That class holds a *backend↔frontend key-set* contract; « this screen must watch that key too » is a rule
   that lives entirely in `.tsx`, so it is tested where it lives (`verification.md` § 4b).
3. **Two extra checks** beyond the six: N41 (the label↔tone pair, which is the `planStatusCounts` half of the
   status guard — `planStatusCounts` itself is already derived from `PLAN_STATUS_LABELS` and needed nothing)
   and the `itemNetCost` / closed-status halves folded into N40 rather than given numbers of their own.

### ⚠️ Writing the guards found **13 live defects**, all fixed in the same pass

A guard that fires on shipped code cannot be committed, so each of these landed with it.

| # | Defect | Where |
|---|---|---|
| 1 | The plan timeline printed « Échéance du 14/03/2026 » on every payment — **no `isAutoRaised` branch at all**, i.e. a fabricated promise on 159 of the 184 rows on the dev database | `plan-timeline.tsx` |
| 2 | The échéancier card title said « **Total dû** » where the owner says « Solde à régler » — two names for one row, on two surfaces of the same screen | `plan-workspace.tsx` |
| 3 | …and its action menu a third (« du solde à régler de 400,000 DT ») | `plan-workspace.tsx` |
| 4 | The void-payment panel a fourth | `void-installment-payment.tsx` |
| 5 | The revise form a fifth, **printing the raw `2026-03-14`** into French prose | `revise-installments-modal.tsx` |
| 6 | The draft form the same, with **no auto-raised branch** | `treatment-plan-form-modal.tsx` |
| 7 | « Arrêter le traitement » listed both act columns at the **tarif** and **summed them** under « l'échéancier est ramené au total conservé » — the one figure a dentist checks before abandoning half a treatment, wrong by the whole remise (S2) | `plan-workspace.tsx` ×3 |
| 8 | « Mettre de côté » / « Remettre au devis » said « Ses X sortent du total » at the tarif | `plan-workspace.tsx` ×2 |
| 9 | The step-suggestion notice priced « pour tout le traitement » at the tarif, twice | `plan-step-suggestion-notice.tsx` |
| 10 | **The fiche de soins** stated « L'acte entier est chiffré 400,000 DT » / « 400,000 DT convenus pour tout le traitement » at the tarif, and prefilled it as the act's fee. `PlanItemOption.plannedCost` is `netCost` now — the name was the bug | `patient-record-modal.tsx`, `patients/[id]/page.tsx`, `odontogram.tsx` |
| 11 | « Encaisser » was offered on a **written-off** devis (`plan.status !== "Cancelled"`), and the collect bounces « Le plan doit être accepté » — `EnsurePayable` refuses `WrittenOff` | `plan-workspace.tsx` |
| 12 | …as were act corrections, bouncing « Ce devis est annulé » on a devis that is not annulé (`EnsureCorrectable`), and the reorder controls | `plan-workspace.tsx` ×2 |
| 13 | **La caisse, the chèques list and « Créances » did not watch `patients`** — the fiche de soins is what collects at the chair and raises the séance's note, and it is a `Features.Patients` command, so the commonest cash of the day never woke the screen whose job is « what is in the drawer right now » | `caisse/page.tsx`, `cheques-table.tsx`, `receivables-table.tsx` |

Two new owner exports carry the first six: `installmentDueSentence` (mid-sentence) and `installmentDueTitle`
(sentence-start), beside the existing `installmentDueLabel` and `installmentDueInputValue`. Three shapes
rather than a capitalisation helper, because the auto-raised branch is not a case change — « Solde à régler »
never gains the word « Échéance ». And `isPlanClosedToWrites` is exported for #11/#12.

⚠️ **N39 is anchored on the receiver's name, not on the field.** A cheque also has a `dueDate` — « encaissable
le », a day the patient wrote on the paper — and printing it is correct; the chèques table fired twice before
the scan was narrowed.

---

## Phase 6 — Verification (one pass, `verification.md` § 0)

1. `BaseOutputPath=<temp> dotnet test … -c Release` — **unfiltered**, collect every failure, fix together, re-run.
2. `dotnet run -- verify-schema` (diff against Phase 0) + `reconcile-money`.
3. `npm run check:responsive` && `npx tsc --noEmit` && `npm run build` — all three, after the **last** edit.
4. `node e2e/scripts/check-coverage.mjs`, then the `e2e` suite.
5. `/test-in-browser` with a plan covering: cancel-with-money · stop-with-deposit · uncancel · reopen balance
   on two screens · per-act park/restore · list edit of a followed draft · facturer from the menu · void on a
   bridged plan · the 409 recharger · every échéance surface at 320 px. Then `/fix-qa-findings`. Three cycles max.
6. Eye pass at 320 / 390 / 820 / 1180 / 1440 px — M22, M23, m17 are only provable there.
7. Update `features/treatment-plan-lifecycle/notes.md` and the root `CLAUDE.md` traps list.

### What Phase 6 actually found — ✅ all seven steps done, 2026-09-15

| Step | Result |
|---|---|
| 1 · unfiltered suite | ✅ **4710 / 0 / 6** |
| 2 · `verify-schema` before→after | ✅ the migration added exactly `TreatmentPlans.WriteOffAmount`, `WriteOffReason` and `TreatmentPlanItems.DiscountAmount`; **no** scaffolded `xmin`; the six pre-existing DRIFT lines identical either side |
| 2 · `reconcile-money` | ✅ no drift, monthly « encaissé » baseline unchanged |
| 3 · `check:responsive` · `tsc` · `build` | ✅ **70/70** · clean · exit 0 |
| 4 · `check-coverage.mjs` | ❌ red, and **pre-existing** — `e2e/` is untouched by this branch, so its inputs are identical to `HEAD` |
| 4 · the `e2e` suite | ⏭ skipped on the owner's instruction — it runs in CI |
| 5 · browser pass | ✅ **GREEN** — `qa/run-1.md` (RED, 3 findings) → one fix → `qa/run-2.md` (73 checks, 0 red). One cycle |
| 6 · eye pass | ✅ 320 / 390 / 820 / 1180 / 1440 **and 1536 × 730**; no horizontal overflow at any width |
| 7 · docs | ✅ `notes.md` written; root `CLAUDE.md` gained three traps and the notes-index row |

**The one product defect the pass found** — and the only code change in Phase 6 — is F1: the stop dialog
promised an « Arrêté » the server refuses on a devis carrying money with nothing realised.
`stopNeedsRefundFirst` + N40 are the fix and its guard. F2 and F3 died on a source read (deliberate /
already-correct). See `notes.md` § « A stop has THREE outcomes ».

⚠️ **A peer session landed `20260914215715_DropClinicActivityCollectedThisMonth` and an edit to
`GetPlatformSummaryQuery.cs` at ~22:57, mid-phase.** That migration is **unapplied** and unrelated to anything
here; the API was deliberately not restarted for it (`shared-stack.md` § 3). It is why the suite reads 4710
rather than the 4711 Phase 5 recorded.

---

## Sequencing

Phase 1 → 2 → 3 must be in order (each reads the one before). Phase 4 can interleave with 3 except 4.4,
which needs Phase 1's status handling. Phase 5 lands with the code it guards, never after.

**Suggested commits:** one per phase, French subject lines, each with its blast-radius table in the body.
