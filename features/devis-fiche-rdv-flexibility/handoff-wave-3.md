# Handoff — devis · fiche · RDV flexibility, wave 3 (G — money)

Start here in a new session. Read this file, then `notes.md` beside it (waves 1 and 2, what each did and why).

## The goal (owner's words)

« I need the treatment plan, fiche, RDV to work spotless, no bugs whatsoever … the app should not block edits,
because doctor work always needs to refine and edit along the way … unless it is nonsense. »

**Talk to her:** English, short bullets, tables, no paragraphs (`.claude/rules/response-style.md`). She asks for
progress as a table + percentage; give it that way.

## Where things stand (2026-09-23)

| | |
|---|---|
| Audit board (76 findings, root causes A–J) | https://claude.ai/artifact/93SGuXQNLHUM352jJUP5aW |
| Older 42-row board (« old NN ») | https://claude.ai/artifact/PGmXJNc91ZymEuNy9gi9LW |
| Wave 1 (A–D) | ✅ `ecd8f131` |
| Wave 2 (E + F + C4) | ✅ `e2552551` — both on `feature/security-remediation`, **not pushed** |
| Progress | 44 / 76 (~58 %) |
| Left | **wave 3 = G (12, money)** · wave 4 = H + I + J (20) |

## Owner decisions already taken — do not re-ask

1. **Total lowered below what was paid** → the doctor may reduce the amount paid on the devis, with a very short
   warning; recorded as money given back **today** (past caisse days untouched). No « avoir » detour. (G3)
2. **Reception booking a multi-séance act** → done in wave 2 (E3).
3. **Remise / « Total convenu » changed** → keep the agreed dates; take the difference off the last unpaid
   instalments. Never collapse the schedule into one lump sum. (G2)

Standing rules: no helper/explanation text under controls; never trade a capability for space; decide reversible
calls yourself; ask only when being wrong spends money or loses data.

---

## Wave 3 — G (money)

| # | Bug | Evidence | Fix |
|---|---|---|---|
| G1 | Act typed at 0 DT saved at catalogue price everywhere (toast even says 0,000); blocks editing a continuation line | `TreatmentPlanItemPricing.cs:66`; `TreatmentPlanDto.cs:466` non-nullable; `use-patient-plan-acts.ts:120`; `ContinueRecordedActCommand.cs:312` + `TreatmentPlanItem.cs:311` | `PlannedCost` nullable **on the request** (not the column); fill only when absent |
| G2 | Remise / park / restore / « Total convenu » → agreed schedule becomes one lump sum due today | `TreatmentPlan.cs:1147-1167` (callers ~1695, 1356, 1384); `AmendTreatmentPlanCommand.cs:268-271`; `use-patient-plan-acts.ts:115-126` | Decision 3: keep dates, take the difference off the last unpaid rows (add to the last one when it grows) |
| G3 | Total below collected → told « par un avoir », which does not exist for a devis | `TreatmentPlan.cs:1831-1840`, `:992-996`; `CreateCreditNoteCommand.cs:23`; `plan-workspace.tsx:~3269` | Decision 1: a « rendu » payment dated today on the devis; short warning in the dialog |
| G4 | Catalogue picker quotes a 3-tooth act once (60 instead of 180) | old 07 (`repricedFor` vs `seedCost`) | One pricing function for all doors |
| G5 | Changing a fiche's date moves note payments but not the devis payment nor the séance date | `UpdateDentalRecordCommand.cs` (MovePaymentsAsync); `CollectOnTreatmentCommand.cs:190-201`; `TreatmentPlanItemStep.cs:151-156` | Move them together |
| G6 | Discount never shown on booking screens | `plan-next-action.ts:~685` (`plannedCost` instead of `itemNetCost`) | Read `itemNetCost` |
| G7 | Write-off erases past cash from closed days; `reconcile-money` cannot see it | old 09, 10; `MoneyReconciliationReader.cs:33` | Split « owes money » from « money moved » |
| G8 | Written-off devis shows « Reste dû » + « Encaisser »; overdue the day after signing | old 13, 14; `plan-next-action.ts:~516`; `TreatmentPlanRepository.cs:~837`; `GetPatientBillingSummaryQuery.cs:~103` | Shared rules (`InstallmentLateness`) |
| G9 | Devis number minted, then the collection refused | `CollectOnTreatmentCommand.cs:228-253` | Check before minting |
| G10 | Price change on a Draft writes a payment row → « Solde à régler » with no quote | `TreatmentPlan.cs:1124, 1162-1167` | Skip re-spread without a number |
| G11 | Séance edit on a written-off plan (API) brings the debt back while still counted as a loss | `TreatmentPlan.cs:756, 869-870, 1952` | Exclude WrittenOff from re-derivation |
| G12 | Written-off devis can still be invoiced (API) | old 17; `CreateInvoiceFromTreatmentPlanCommand.cs:68` | Refuse from `PlanBillingRules` |

⚠️ Line numbers are from before wave 2 — `TreatmentPlan.cs` did not move, but the amend handler and
`use-patient-plan-acts.ts` did. Grep the symbol, not the line.

### Suggested order inside the wave

1. **G2 + G10 + G11 first** — all three are `RespreadSchedule` / `RespreadScheduleToTotal`. One owner already
   (see the trap in root `CLAUDE.md`); change the rule there, not at the callers. G10 (skip without a number) and
   G11 (skip WrittenOff) are guards at the top of that same method.
2. **G3** — new « rendu » path. It is money leaving: needs a caisse movement dated today, and `reconcile-money` must
   stay at 0. Grep every money read for how a voided / negative payment is summed before choosing « negative
   payment » vs « refund row ».
3. **G7 + G8 + G12** — write-off rules. `PlanBillingRules` + `InstallmentLateness` are the owners.
4. **G1, G4, G6** — pricing (G1 is server + client; G4, G6 are client).
5. **G5, G9** — fiche-date move and collect-before-mint.

## Things wave 2 left that touch wave 3

- **A peer (clinic-management-a6's report, another session's code) is building « Ajouter au devis » from the fiche**
  — dirty, uncommitted, in `web/components/patient-record-modal.tsx`, `record/act-card.tsx`,
  `record/use-session-acts.ts`, plus `features/devis-fiche-rdv-flexibility/complaint-new-act-onto-devis-from-fiche.md`.
  It amends the devis from the fiche and depends on **G10** (it only offers a *numbered* devis until G10 lands).
  ⚠️ **Do not commit those files as yours**; wave 2 committed only its own 15-line hunk of `patient-record-modal.tsx`
  by staging a blob built on HEAD. `ListAgents` first and coordinate before editing that modal.
- `FicheExtraPlanActs` matches an extra devis act to a fiche act **by procedure**. An act added to the devis in the
  same save will need an explicit item id — shape it when the « Ajouter au devis » work lands.
- E3 was never exercised in a browser: no reception password on this machine (`qa.secretary@ibnkhaldoun.test`
  exists, password unknown). Policy is pinned by `TreatmentPlansControllerAuthorizationTests`.

## How a wave is done (what worked in waves 1–2)

1. `ListAgents` + `stack-lease.ps1 status -Session <you>`. Web is bf's `next dev` (hot reloads — don't restart).
   API was last started by clinic-management-31; restart it only if its owner is idle (stop the PID on :5000,
   `claim api`, `dotnet run`, `record api`).
2. Blast-radius table first — append a wave-3 section to `blast-radius.md`. Money shapes: § 2 items 3, 5, 7.
3. Fix. Long multi-line edits: write a Python script to the **scratchpad** (Windows Python cannot see bash's `/tmp`).
4. Tests in a new `DevisFicheRdvFlexibilityWave3Tests.cs`. Gate:
   `BaseOutputPath=<temp> dotnet test api/ClinicManagement.UnitTests/... -c Release` (unfiltered; **4784** green
   after wave 2) + `cd web && npx tsc --noEmit && npm run check:responsive && npm run build`.
   **Plus `dotnet run -- reconcile-money` before and after** — this wave moves money; diff the two outputs.
5. Restart the API so the browser sees the new code.
6. Browser: copy `qa/arrange-2.mjs` / `qa/walk-2.mjs` to `-3` versions. Tooling: `node_modules` (playwright-core)
   + `totp.mjs` copied into the scratchpad from another session's scratchpad; `channel: 'chrome'`, headless;
   `claim browser` / `release browser`. Write `qa/run-3.md`, never overwrite earlier runs. Add wave-3 rows to
   `qa/plan.md`.
7. Commit only the wave's own files (`git diff HEAD --numstat` / `git diff --cached --numstat` per file — a peer's
   hunk in a file you touched shows up as extra lines). Dirty and NOT ours: `.claude/rules/frontend-web.md`,
   `.claude/rules/verification.md`, `.claude/skills/start-clinic/SKILL.md`, `FEATURE-OVERVIEW.md`,
   `console/tsconfig.json`, and the peer files listed above.

## Traps hit so far

- New handler dependency → **optional** constructor parameter (`IAppointmentRepository? x = null`).
- Moq returns **null** for an unstubbed `Task<IReadOnlyList<T>>` → `?? Array.Empty<T>()`.
- A new command in `Features/TreatmentPlans/Commands` must declare `SetExpectedVersion` or be added to
  `PlanVersionCoverageTests.Exempt` with a reason.
- A new controller action must be classified in `TreatmentPlansControllerAuthorizationTests`.
- `stepSignature([])` used to be `""` vs stored `null` → every save looked edited. Compare empty as null.
- A form fed a live `editingPlan` prop must save with the version it **hydrated** from, never the prop's — the
  page re-reads on every realtime event and a stale form then saves with a fresh token (200 over a colleague).
- The fiche modal's save button is « Confirmer la séance » on a new fiche, not « Enregistrer ».
- Workspace menu trigger: `aria-label="Autres actions sur ce devis"`.
- Remise endpoint is `PUT /treatment-plans/{id}/items/{itemId}/discount` `{ discountAmount }`.
- Deep links: `/appointments?appointmentId=<id>` · `/patients/<id>?editRecord=<ficheId>` ·
  `?addRecord=1&appointmentId=<id>`. Enum ints: appointment Completed 4, Cancelled 5 · plan Completed 3,
  Cancelled 4 · item Planned 0, Done 1, Withdrawn 3.

---

## Progress — session clinic-management-8f, 2026-09-23 (paused: usage limit)

**Nothing committed.** All of this is uncommitted in the working tree. Unit suite: 4798 green after the edits
(4 message checks updated in `TreatmentPlanReversibilityTests` / `InstallmentLedgerTests`: « avoir » became « Rendez » / « Arrêtez »).
`tsc` is clean.

| # | State | Where |
|---|---|---|
| G1 | ✅ code | `TreatmentPlanItemRequest.PlannedCost` is `decimal?`; `TreatmentPlanItemPricing` fills only `null` |
| G2 | ✅ code | `TreatmentPlan.RespreadSchedule` keeps dates: a lower total comes off the last unpaid rows, a higher one goes on the last unpaid row; `Installment.Resize` |
| G3 | server ✅ · **screen ❌** | negative `InstallmentPayment` (`IsRefund`, `Refund` factory) · `TreatmentPlan.RefundExcess/ExcessCollected/HasReceipts/RefundOnStop` · `PlanRefund.cs` (code `plan-total-below-collected`, `RefundMethod` on amend/discount/withdraw/stop) · caisse extrait shows it as a Sortie · no receipt · the invoice bridge refuses it · void refused · `EnsureNoLiveMoney`/`StopWouldCancel`/discard look at receipts, not the net |
| G4 | ✅ code | `odontogram-plan-seed.ts` `catalogueLineCost` / `isPerToothAct`; form `repricedFor(line, pt)` + `updateLineTeeth` |
| G5 | ✅ code | `TreatmentPlan.FollowDentalRecordDate` (+ `Installment.AmendPaymentDate`, `Item.RedateRecord`, `Step.Redate`), called from `UpdateDentalRecordCommand` when the date changes |
| G6 | ✅ code | `planItemToPreset` reads `itemNetCost`; `saveActTotal` sends net + remise |
| G7 | ✅ code | `PlanBillingRules.CashBearingPlanStatuses` (debt + WrittenOff) in the 4 cash reads + `MoneyReconciliationReader` |
| G8 | ✅ code | `displayedOutstanding`/`planNextAction` null for WrittenOff; both « en retard depuis » reads go through `InstallmentLateness.IsLate` |
| G9 | ✅ code | `CollectOnTreatmentCommand` checks the amount before minting |
| G10 | ✅ code | respread is a no-op without a number and without rows |
| G11 | ✅ code | `StatusFollowsTheWork` also excludes WrittenOff and Cancelled |
| G12 | ✅ code | `CreateInvoiceFromTreatmentPlan` refuses `!CarriesDebt`; the Issue bridge refuses WrittenOff |

### Left to do
1. **G3 screen**: `isRefund` in `web/lib/api/types.ts` `InstallmentPaymentDto`; `refundMethod` on amend/discount/withdraw/stop in
   `lib/api/treatment-plans.ts`; on error code `plan-total-below-collected` → one short confirm « Rendre X DT au patient aujourd'hui ? »
   + method (Espèces / Virement / Carte, no Chèque) → resend. Surfaces: devis form save, remise dialog, « Mettre de côté », stop dialog
   (`stopNeedsRefundFirst` branch, plan-workspace ~3269 still says « avoir »). Payment rows: a rendu reads « Rendu au patient », with no Reçu/Annuler.
2. Tests: `DevisFicheRdvFlexibilityWave3Tests.cs`, with one per G row (helpers: wave-2 file; `ProcedureType(id, clinic, name, 30, ColorHex.FromString("#2A9D8F"), defaultCost: 90m)`,
   `new DentalRecord(id, patient, clinic, date, 0m, true)`).
3. Gate (full suite + tsc + check:responsive + build) · `reconcile-money` (⚠️ no « before » was captured; the reader now includes WrittenOff plans, so a diff there is expected) ·
   wave-3 section in `blast-radius.md` + `notes.md` · API restart · browser `qa/run-3.md` · commit only these files (peer files untouched,
   incl. untracked `Application/Features/Patients/FichePlanActAdditions.cs`, which calls `RespreadScheduleToTotal(today)` and still compiles).

### Carried to wave 4 (found by clinic-management-a6's walk, 2026-09-23)
- **C4 reopen**: a fiche whose acts are ALL carried by the devis reopens with « Payé », « Mode » and « Total » showing —
  `markBilledOnPlan` marks one card and `DentalRecordDto` carries a single `TreatmentPlanItemId`, so a reopened fiche cannot
  know the other devis acts it closed. Fix: the DTO carries every linked item id, the modal marks them all. No money moves
  (both acts stored at 0, server re-imposes it; « Payé » > 0 disables the save). See a6's `qa/run-act-onto-devis.md` finding 1.

**Wave 3 is committed** — see `notes.md` § wave 3 and `qa/run-3.md` (GREEN).
