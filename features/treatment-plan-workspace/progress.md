# Progress: Treatment Plan Workspace

**Started:** 2026-07-26
**Type:** Full (forced into `/implement-small-feature` by explicit user choice — see DEV-1)
**Branch:** `feature/treatment-plan-workspace`
**Worktree:** `.claude/worktrees/treatment-plan-workspace` (branched from `f510f93`)

## Status
- [x] Slice A — **backend** (read-back core + money-bug fix) — builds clean
- [x] Slice A — **frontend** (PatientPlanCard, état badges, realtime keys, types/tz fixes)
- [x] AC-8 — `InvoiceDto.treatmentPlanId` + the invoices-table « Devis » badge (session 2)
- [x] AC-9a — the patient page watches TreatmentPlans + Invoices too (session 2)
- [x] Slice A2 — make the four money reads agree (session 2)
- [x] Slice C — the workspace route (session 4)
- [ ] Slice B — sequencing, amendment, migration
- [~] Tests (see DEV-2) — **6 of the 8 named classes written** in session 3, plus `PlanBillingRulesTests` and
      the `RealtimeResourceResolver` keys. The remaining 2 are **blocked on code that doesn't exist yet**
      (slices B and C), not deferred — see "Session 3" below.
- [x] Quality checks — `dotnet build` 0 errors / 0 warnings in changed files; full unit suite
      **668 passed / 8 failed, all 8 verified pre-existing**; `tsc --noEmit` clean for every file touched

## Slice A backend — done (9 files, verified 0 errors / 0 new warnings)

Verified with `dotnet build ClinicManagement.Infrastructure.csproj` (compiles Domain + Application +
Infrastructure into an unlocked worktree `bin`), then re-run scoped to the changed files — no warning or error
in any of them. The repo's **pre-existing** baseline warning family here is **`CS8618`** (non-nullable property
uninitialised, in Domain value objects/entities) — note this is *not* the `CS8632` family the skill's InstaDeep
example describes.

| File | Change |
|---|---|
| `Domain/Repositories/IAppointmentRepository.cs` | + `GetByTreatmentPlanItemIdsAsync(clinicId, itemIds, ct)` |
| `Infrastructure/Repositories/AppointmentRepository.cs` | impl — one batched query, empty-input short-circuit, deliberately no `.Include` (first consumer of the long-unused `IX_Appointments_TreatmentPlanItemId`) |
| `Domain/Repositories/IInvoiceRepository.cs` | + `GetTreatmentPlanLinksAsync(clinicId, ct)` → tuple projection |
| `Infrastructure/Repositories/InvoiceRepository.cs` | impl — light projection, never loads `Lines`/`Payments` |
| `Domain/Entities/TreatmentPlan.cs` | + `EnsurePayable()` (Accepted \| InProgress \| **Completed**) guarding `RecordInstallmentPayment`; auto-close moved **into** `MarkItemDone` so the record-driven path and the command behave identically |
| `Application/.../MarkTreatmentPlanItemDoneCommand.cs` | removed the duplicated auto-close block + the now-unused `using ClinicManagement.Domain.Enums;` |
| `Application/DTOs/TreatmentPlanDto.cs` | + derived `ItemsDone/ItemsTotal/NextAppointmentAt/LinkedInvoice*`; item + `ScheduledAppointmentId/ScheduledAt/ScheduledAppointmentStatus` |
| `Application/Features/TreatmentPlans/TreatmentPlanWorkflowProjection.cs` **(new)** | the derivation core: `LiveStatuses` excludes Cancelled/NoShow, `PickRepresentative` (earliest upcoming else latest past), per-plan « prochaine séance » against one shared `asOfUtc` |
| `Application/Features/TreatmentPlans/TreatmentPlanMappingExtensions.cs` | workflow-aware `ToDto` overload; 2-arg overload preserved so all 8 command handlers compile untouched |
| `Application/.../Queries/GetTreatmentPlan{,s}Query.cs` | inject the two repos, build the projection once per request; **also fixed the pre-existing patient-name N+1** in the list handler (`GetByIdAsync` per patient → one `GetByClinicIdAsync`, mirroring `GetInvoicesQuery`) |

## Working tree note (start of session)

The user was working in the main checkout (`feature/windows-desktop-app`) with **131 files**
uncommitted — the adoption-QA batches A–H, tooth-first record entry, credit notes/avoir, stock
movements, and this feature's own (untracked) spec folder.

A worktree can only branch from a commit, and **every foundation this spec builds on was
uncommitted**: `Invoice.TreatmentPlanId`, `CreateInvoiceFromTreatmentPlanCommand`,
`CompleteTreatmentPlanCommand`, `CreditNote`, the `billedPlanIds` de-dup in
`GetPatientBillingSummaryQuery`, and the auto-complete block in
`MarkTreatmentPlanItemDoneCommandHandler`. A worktree off the then-HEAD (`25c6738`) would not have
compiled against the spec.

With the user's authorization, that work was committed as a baseline (`f510f93`, 131 files /
15,592 insertions) on their branch, and this worktree branched from it. Verified afterwards: the
main checkout is still on `feature/windows-desktop-app` with a clean tree, zero stashes, no resets.
Recoverable as uncommitted work with `git reset --soft HEAD~1` if the user prefers.

The user also committed `3db483e` (French onboarding error messages) themselves during the session;
the baseline sits on top of it.

**No files in this feature's commits come from that baseline** — everything below is new work in
this worktree.

## Repo-convention note (this skill's examples do not apply here)

`/implement-small-feature` documents the InstaDeep conventions. This repo differs, and per the
skill's own instruction the current repo wins:

| Skill example | This repo |
|---|---|
| ROP `Railway.Start()`, typed `NotFoundError` | `Result<T>.Success/Failure("<French message>")`; `NotFoundException` only where the contract needs a 404 |
| `IBaseRepository`, never touch `DbContext` | Repositories ctor-inject `ApplicationDbContext` directly; stage only, `IUnitOfWork` commits |
| `AbstractValidator<T>` | **Zero** FluentValidation validators; `ValidationBehavior` is inert, handlers validate inline |
| Register in `API/Module.cs` | `Infrastructure/Extensions.cs` (`AddInfrastructure`) + `Application/Extensions.cs` (`AddApplication`) |
| static `Create()` factories | Public constructors (`TreatmentPlan.cs:45`, `TreatmentPlanItem.cs:29`) |
| `pnpm typecheck` / `pnpm lint` | `npx tsc --noEmit` + `npm run build` — ESLint is not installed and `next.config.ts` disables it |

`features/LEARNINGS.md` has no `## Deviation Overrides` section, so no topic is force-gated.

## Files Changed

_(updated as work proceeds)_

## Slice A frontend — partially done (4 files)

| File | Status |
|---|---|
| `web/lib/api/types.ts` | ✅ derived fields on `TreatmentPlanDto`/`TreatmentPlanItemDto`; `updatedAt` → `?: string \| null` (was the only bare non-optional `updatedAt` in the file) |
| `web/components/treatment-plans/plan-next-action.ts` **(new)** | ✅ `planItemState` (the 4 états), `isPlanBilled`, `planNextAction`, `leadPlan` — pure, no I/O |
| `web/components/treatment-plans/treatment-plan-labels.ts` | ✅ `ITEM_WORKFLOW_LABELS`/`_BADGE_CLASS` + `PLAN_NEXT_ACTION_LABELS` and accessors |
| `web/components/treatment-plans/patient-plan-card.tsx` **(new)** | ✅ active + draft variants, hand-rolled ARIA progress bar, draft never shows « Reste », renders null with no plan |
| `web/app/patients/[id]/page.tsx` | ✅ `PatientPlanCard` mounted above the tabs; `Tabs` made controlled (`activeTab`) so the card can jump to the plans tab; « Back to Patients » → « Retour aux patients » |
| `web/components/treatment-plans/treatment-plans-table.tsx` | ✅ 4-état badge + scheduled date; **"Planifier" only when `to-schedule`** (duplicate booking now impossible); "Facturer" hidden once billed, replaced by a « Facturé — N° » badge; realtime widened to `[TreatmentPlans, Appointments, Invoices]` |
| `web/app/treatment-plans/page.tsx` | ✅ date filter now sends UTC instants (`toISOString()`) instead of timezone-naive wall-clock strings |
| `InvoiceDto` (BE + FE) + `invoices-table.tsx` | ✅ session 2 — see below |
| `web/components/patient-record-modal.tsx` (AC-9) | ⛔ **deliberately skipped** — see DEV-3 |

## Session 2 — AC-8, AC-9a and slice A2 (11 files: 7 changed, 2 new, 2 docs)

### AC-8 — invoice → plan navigation (6 files)

| File | Change |
|---|---|
| `Application/DTOs/InvoiceDto.cs` | + `Guid? TreatmentPlanId` (persisted since `20260724125528_AddInvoiceTreatmentPlanLink`, never exposed) |
| `Application/Features/Invoices/InvoiceMappingExtensions.cs` | map it — the single `ToDto`, so every invoice response gains it at once |
| `web/lib/api/types.ts` | + `treatmentPlanId?: string \| null` on `InvoiceDto` |
| `web/components/factures/invoices-table.tsx` | « Devis » badge beside the number, `<Link>` to the plan. `variant="outline"`, mirroring the plans table's « Facturé — N° » badge so the two ends of the link read as a pair |
| `web/app/treatment-plans/page.tsx` | reads `?plan=…` on mount → `highlightPlanId` |
| `web/components/treatment-plans/treatment-plans-table.tsx` | + `highlightPlanId` prop: scrolls the row into view (`block: "center"`) and rings it (`bg-accent/60`) |

**Why `?plan=` and not `/treatment-plans/{id}`:** that route is slice C and does not exist yet, so linking
there would 404 today. The deep-link target is a query param on the existing list, read with
`window.location.search` in a mount effect — **not** `useSearchParams`, which would force the page out of
static prerendering; `app/patients/page.tsx:27`, `app/appointments/page.tsx:129` and `app/documents/page.tsx:60`
all record that same decision. When slice C lands, the badge's `href` becomes `/treatment-plans/${id}` and the
`highlightPlanId` plumbing retires with the list-as-detail-view.

### AC-9a — the patient page's realtime keys (1 file)

`web/app/patients/[id]/page.tsx` — `[Patients, Appointments, Files]` → `+ TreatmentPlans, Invoices`. Verified
this actually refreshes the card and not just the tab: `treatmentPlans` is loaded by an effect keyed on
`[patientId, refreshKey]` (`:265`) and again in the main loader (`:326`), and `PatientPlanCard` is fed from
that state (`:606`). Without the two keys a peer accepting a plan or issuing its invoice left the card stale —
`RealtimeBroadcastBehavior` keys off the *command's* namespace, so neither event ever broadcasts `"patients"`.

### Slice A2 — the four money reads (5 files + 1 new + 1 new test)

**One shared helper, in Domain, not Application (AC-12c).** `Domain/Services/PlanBillingRules.cs` (new) —
`DebtBearingPlanStatuses` / `CarriesDebt`, `RepresentsItsPlan(InvoiceStatus)`, and two `BilledPlanIds`
overloads (loaded `Invoice`s for « Solde patient »; the light bridge-link tuple projection for the clinic-wide
reads). Deviation from the spec's docs list, which files it under Application — see DEV-4.

| File | Change |
|---|---|
| `Domain/Repositories/ITreatmentPlanRepository.cs` | `GetInstallmentOutstandingByPatientAsync` gains a **required** `IReadOnlyCollection<Guid> excludedPlanIds` (no default — a new money read cannot silently omit the de-dup); both aggregates re-documented around `PlanBillingRules` |
| `Infrastructure/Repositories/TreatmentPlanRepository.cs` | both aggregates: `Status != Cancelled` → `DebtBearingPlanStatuses.Contains(...)` (adds the **Draft** exclusion, AC-12b); outstanding also filters out `excludedPlanIds`. `IReadOnlyCollection.Contains` in a `.Where` is the repo's proven EF pattern (`AppointmentRepository.cs:93`) |
| `Application/.../GetPatientBillingSummaryQuery.cs` | its two inline rules replaced by the helper — same behaviour, now the shared one |
| `Application/.../GetReceivablesQuery.cs` | + one `GetTreatmentPlanLinksAsync` read → `BilledPlanIds` → passed to the plan aggregate |
| `Application/.../GetDashboardStatsQuery.cs` | same |
| `Application/.../GetCaisseSummaryQuery.cs` | **comment only** — see DEV-5 |
| `UnitTests/Domain/PlanBillingRulesTests.cs` **(new)** | 16 cases: the status tables, `DebtBearingPlanStatuses` ≡ `CarriesDebt`, draft/cancelled/standalone bridges ignored, both overloads agree |
| `UnitTests/Features/Dashboard/GetDashboardStatsQueryHandlerTests.cs` | fixed for the new signature + a `[Fact]` pinning that an issued bridge is excluded and a cancelled one is **not** |

Also refreshed `api/ClinicManagement.Domain/CLAUDE.md` (the new `PlanBillingRules` service; the
`ITreatmentPlanRepository`/`IInvoiceRepository`/`IAppointmentRepository` rows) — the rest of the docs pass
stays its own chunk.

## Quality checks so far

- **Backend:** `dotnet build ClinicManagement.Infrastructure.csproj` → **0 errors**, and re-run scoped to the
  changed files → **0 warnings**. Repo baseline warning family is **`CS8618`** (pre-existing, Domain value
  objects) — *not* the `CS8632` the skill's example describes.
- **Frontend:** `npx tsc --noEmit` reports **6 errors, all pre-existing and none mine.** Verified, not assumed:
  `git stash push -u` → tsc on the pristine `f510f93` tree → **identical 6 errors**, then `git stash pop`. They
  are all in `web/components/patient-record-modal.tsx` (`ToothPaint.focused`, `DentalActInput.isPerTooth`) —
  the baseline captured the user's tooth-first refactor mid-change, where the shared types moved ahead of that
  component. The FE gate goes green once that refactor lands on the base branch; nothing here can fix it
  without editing the file DEV-3 excludes.
- **Frontend, after the full slice-A frontend landed:** re-ran `npx tsc --noEmit` — the *only* remaining errors
  are still the 6 pre-existing ones in `patient-record-modal.tsx`. **Every file this feature touched
  typechecks clean**, including the controlled `Tabs`, the mounted card, and the widened realtime call.
- `npm run build` not run: it would fail on the same pre-existing `patient-record-modal.tsx` errors, so it
  carries no signal about this work until the user's tooth-first refactor lands on the base branch.

### Session 2 quality checks

- **Backend build:** `dotnet build ClinicManagement.Infrastructure.csproj` → **0 errors, 48 warnings**, all
  the pre-existing `CS8618` family; grepping the log for every file this session touched returns **nothing**.
  `ClinicManagement.UnitTests.csproj` also builds clean (0 errors).
- **Unit suite — it ran this time.** Smart App Control did not quarantine the assemblies on this attempt; the
  recorded workaround (`-p:OutDir=<scratch>/utbuild/` then `dotnet vstest`) worked end to end.
  **604 passed, 8 failed, 612 total.**
- **The 8 failures are pre-existing — verified, not assumed.** `git stash push -u` → rebuilt the pristine
  `f510f93` tree to a separate scratch dir → **the identical 8 failures, 587 passed / 595 total** → `git stash
  pop` (tree restored, confirmed by `git status`). The 17-test delta is exactly this session's additions
  (16 `PlanBillingRulesTests` cases + 1 dashboard `[Fact]`). The failures are in areas nothing in this
  worktree touches:
  - `Features/Doctors/DoctorCachetTests` ×4
  - `Infrastructure/Services/ReminderSchedulerTests` ×3
  - `Features/Documents/DocumentTypeAndFilenameTests.Create_With_Supported_Type_Passes_The_Type_Guard`

  They came in with the `f510f93` baseline (the user's in-flight adoption-QA work) and are **not** this
  feature's to fix — but they mean AC-26's "the full unit suite passes" cannot be signed off from this
  branch, and they are worth telling the user about since they sit on their own branch.
### Session 4 quality checks

- `dotnet build` → **0 errors**; full suite **677 passed / 8 failed / 685** — +9 (the new plan-link class),
  the same 8 pre-existing failures, no regression.
- `npx tsc --noEmit` → still exactly the **6 pre-existing** `patient-record-modal.tsx` errors. Every new and
  changed frontend file typechecks clean, including the new dynamic route.
- `npm run build` still not runnable for the same pre-existing reason (DEV-3).
- **Not verified: the workspace rendering in a browser.** AC-13 – AC-16 are implementation + manual
  verification per the spec (no FE test runner exists). Worth a click-through before the PR: open a plan from
  /factures' « Devis » badge, from the patient card, and from a plans row; check the four états each offer one
  action; hit a garbage id for the « Plan introuvable » card.

### Session 3 quality checks

- `dotnet build ClinicManagement.UnitTests.csproj` → **0 errors**.
- Full unit suite: **668 passed, 8 failed, 676 total** — the same 8 pre-existing failures, unchanged, and
  **64 new passing tests** (612 → 676). No regression in any existing class.
- The new plan/money/realtime classes run green in isolation: 97/97 on
  `--TestCaseFilter:"…TreatmentPlan|…MoneyRead|…RealtimeResource|…PlanBillingRules"`.
- No frontend change in this session, so `tsc` is unchanged from session 2.

- **Frontend (session 2):** `npx tsc --noEmit` → still exactly the **6 pre-existing** `patient-record-modal.tsx` errors
  (`ToothPaint.focused`, `DentalActInput.isPerTooth`). Every file touched this session typechecks clean,
  including the `TableRow` `ref` (React 19 passes `ref` through `React.ComponentProps<"tr">`) and the
  `next/link` badge.

## Session 3 — the test suite (6 new classes + 1 extended, 64 new tests)

The plan area had **zero** tests. It now has coverage for every slice-A/A2 rule that shipped.

| Class | Tests | Covers |
|---|---|---|
| `Domain/TreatmentPlanTests.cs` **(new)** | 12 | AC-10 (`EnsurePayable`: a Completed plan still collects; Draft/Cancelled still refuse; payment never re-opens it), AC-11 (auto-close on the last act, InProgress while acts remain), the `Accept` lump-sum seed, and the `Σ installment.Amount == TotalPlanned` invariant (AC-22b) the money reads depend on |
| `Features/TreatmentPlans/TreatmentPlanWorkflowProjectionTests.cs` **(new)** | 16 | the full derivation table — AC-1 (future live → Planifié, incl. Confirmed/InProgress), **AC-2 (cancelled and no-show un-schedule the act; a live replacement wins)**, AC-3 (earliest future, else latest past), AC-3a (a past appointment is reported but is *not* a « prochaine séance »), AC-5, AC-8 (issued bridge → Facturé, cancelled → not), AC-6 (two reads per page, every act in the batch) |
| `Features/TreatmentPlans/TreatmentPlanTenantIsolationTests.cs` **(new)** | 9 | AC-24 — get/update/accept/complete/cancel/delete/mark-done/record-payment all read as "introuvable" for another clinic's devis, each asserting `Times.Never` on `SaveChangesAsync`, `UpdateAsync` **and** `DeleteAsync`, plus list scoping. **This guard existed for every other money aggregate and was missing here entirely.** |
| `Features/TreatmentPlans/GetTreatmentPlansQueryHandlerTests.cs` **(new)** | 5 | AC-6 at handler level — 4 plans / 2 patients still issue exactly one appointments query, one invoice-links query and one patient query; `IPatientRepository.GetByIdAsync` pinned `Times.Never` (the N+1 that was removed) |
| `Features/Billing/MoneyReadConsistencyTests.cs` **(new)** | 6 | AC-12a/b/c — one fixture, three handlers. A bridged plan counts once on all three reads (this fixture reported 2 000 vs 1 000 before the fix); a Draft échéancier contributes 0 everywhere; an un-bridged plan is still counted; cancelling the bridge returns it to the balance; a *draft* bridge doesn't suppress it; both clinic-wide reads verifiably pass the billed-plan ids down |
| `Api/TreatmentPlansControllerAuthorizationTests.cs` **(new)** | 16 | AC-24a — `CancelPlan` pinned to `AdminOrDoctor` (**nothing pinned it before**), the 11 everyday actions pinned to *no* method-level policy, class-level `[Authorize]`, `No_Endpoint_Is_Anonymous`, and a drift guard asserting every action is classified by the test |
| `Common/Behaviors/RealtimeResourceResolverTests.cs` | +3 | AC-25 — `treatmentplans` for create/accept/record-payment |

**The mocks in `MoneyReadConsistencyTests` deliberately reimplement the repositories' SQL filters** (the
status filter and the excluded-plan filter). That is the point: the handlers are what was broken — they never
computed or passed a billed-plan set — so the test proves they now feed the shared rule to the repository and
arrive at the same number. `PlanBillingRulesTests` covers the rule itself, and
`GetDashboardStatsQueryHandlerTests` the wiring.

### The 2 remaining classes are blocked, not skipped

Both name code that **does not exist yet**, so they cannot compile:

- **`Features/TreatmentPlans/AmendTreatmentPlanCommandHandlerTests.cs`** — needs
  `AmendTreatmentPlanCommand`, `TreatmentPlan.AddItems/RemoveItem/ReviseInstallments`, `RevisionNumber`.
  All slice B.
- **`Features/Appointments/` — the update-path plan link (AC-17)** — needs `UpdateAppointmentCommand` to
  accept `TreatmentPlanItemId` and call the dead `Appointment.SetTreatmentPlanItem`. Slice C.

Likewise, `Domain/TreatmentPlanTests.cs` covers AC-10/AC-11 but **not** AC-18–AC-23 (`SetItemOrder`,
id-preserving `SetItems`, the guarded `MarkDone`, `RemoveItem`, `ReviseInstallments`, `RevisionNumber`), and
`TreatmentPlanTenantIsolationTests` / `TreatmentPlansControllerAuthorizationTests` cover today's verbs but not
`amend` / `revise-installments` / `items/order`. Each of those three classes carries an XML-doc note naming
exactly what must be added in the pass that adds the endpoint, so slice B/C cannot land without them.

## Session 4 — slice C, the workspace (15 files: 6 new, 9 changed)

### Backend — AC-17, the update-path plan link (2 files + 5 test-file touches)

`UpdateAppointmentCommand` now accepts `TreatmentPlanId` + `TreatmentPlanItemId`, validates the pair through
the existing `AppointmentPlanLink` and calls `Appointment.SetTreatmentPlanItem` — **which had zero callers
until now**. Rescheduling an appointment onto a different act updates the link instead of leaving a stale one.

**The field is tri-state, and that is the load-bearing decision.** `Guid?` alone cannot tell "clear this" from
"I didn't mention it", and every existing caller (edit dialog, status flips, drag-to-reschedule) sends
neither field. If absent meant clear, any unrelated edit would silently orphan the link — and
`Appointment.TreatmentPlanItemId` has **no FK**, so nothing at the DB level would catch it. Implemented with a
`TreatmentPlanItemIdSpecified` flag set from the property setter (System.Text.Json only assigns properties
present in the payload) and `[JsonIgnore]`d off the wire contract. The controller mutates the deserialized
instance (`command.Id = id`) rather than rebuilding it, so the flag survives binding — verified.

`ITreatmentPlanRepository` joins the handler's ctor; the 4 existing construction sites were updated.

### Frontend — the workspace (6 new, 7 changed)

| File | |
|---|---|
| `web/app/treatment-plans/[id]/page.tsx` **(new)** | The route. `useParams()`, standard shell, three realtime keys. Loading (« Chargement du plan de traitement… ») and « Plan introuvable » render **outside** `ClinicGuard` per `patients/[id]:341-372` — inside it, the guard's own spinner would cover a page that has already failed. A garbage id, a deleted plan and another clinic's plan all land in the same card (the API answers 404 for cross-clinic, so this page can never confirm a plan exists elsewhere). |
| `web/components/treatment-plans/plan-workspace.tsx` **(new)** | Header / actes / échéancier / parcours + the plan's actions. **Annuler moved here** from the list: it is the one destructive action on a numbered devis and needs the context of what is being voided. |
| `web/components/treatment-plans/plan-act-row.tsx` **(new)** | One act, **exactly one** primary action per état. Navigation reuses deep links that already existed: `/appointments?appointmentId=` and `/patients/{id}?addRecord=1&appointmentId=` (the post-visit link — it opens the record modal already bound to that appointment, so « Enregistrer la fiche » closes the loop in one step). |
| `web/components/treatment-plans/plan-timeline.tsx` **(new)** | « Parcours », built only from fields already on the DTO. Reuses `notification-panel.tsx:75-105`'s feed shape. |
| `web/components/treatment-plans/plan-progress-bar.tsx` **(new)** | The hand-rolled ARIA bar, extracted so the card and the workspace share one implementation (AC-17a). |
| `web/components/treatment-plans/treatment-plans-table.tsx` | **Rewritten as a list.** The "Gérer" dialog and the 8 unlabelled ghost icons are gone; rows navigate to the workspace and a labelled dropdown keeps Ouvrir / Devis PDF / Modifier / Supprimer. Gained an « Avancement » column (actes done + prochaine séance). |
| `web/components/treatment-plans/patient-plan-card.tsx` | Links into the workspace now that one exists; "+N autres" still opens the plans tab. Uses the shared progress bar. |
| `web/components/factures/invoices-table.tsx` | « Devis » badge repointed `?plan=` → `/treatment-plans/{id}`. |
| `web/app/treatment-plans/page.tsx` | The interim `highlightPlanId` deep-link plumbing removed with the badge repoint. |
| `web/app/patients/[id]/page.tsx` | + `?tab=` deep-link (same `window.location` idiom, no `useSearchParams`) so « Voir la fiche » can open the medical-records tab. |
| `web/lib/api/appointments.ts` | `update` accepts the plan-link pair, with the tri-state documented at the call site. |

### Deliberately not done in slice C

- **`PUT {id}/items/order`** (reorder) is **slice B**, not C — it needs `SequenceNumber`, which does not exist.
  The spec lists it under slice B's API contract; `NEXT-SESSION.md` previously implied it belonged here.
- **A UI for re-linking an appointment to a different act.** AC-17 is about the endpoint, and the spec's
  slice-C scope is "`UpdateAppointmentCommand` accepts and re-validates `TreatmentPlanItemId`". Building a
  picker into the edit dialog is not in scope and would collide with `patient-record-modal.tsx` (DEV-3).

### Tests (1 new class, 9 tests)

`Features/Appointments/AppointmentPlanLinkUpdateTests.cs` — **the 8th of the spec's test classes**, unblocked
by the backend change above. Move, clear, cross-clinic rejection, **cross-patient** rejection, unknown act,
act-without-plan, patient-less slot, same-act no-op, and the regression guard that an unrelated edit leaves
the link untouched. Every rejection asserts `Times.Never` on `SaveChangesAsync`.

## AC status for slice A (what a reviewer can verify)

| AC | Status |
|---|---|
| AC-1 état « Planifié » from a future live appointment | ✅ |
| AC-2 Cancelled/NoShow → back to « À planifier », bookable again | ✅ (`LiveStatuses` excludes both) |
| AC-3 earliest-future-else-latest-past representative | ✅ (`PickRepresentative`) |
| AC-3a past live appointment + not Done → « À enregistrer » | ✅ (`planItemState`) |
| AC-4 no duplicate Planifier / no duplicate Facturer | ✅ both gated |
| AC-5 progress + linked-invoice triple, no divide-by-zero | ✅ |
| AC-6 one appointments query + one invoice-links query per request | ✅ (+ patient-name N+1 removed) |
| AC-7 PatientPlanCard above the tabs, draft variant, null when no plan | ✅ |
| AC-8 `InvoiceDto.treatmentPlanId` + invoices-table « Devis » badge | ✅ (badge deep-links to `?plan=` until slice C's route exists) |
| AC-9 record modal auto-links the plan act | ⛔ DEV-3 (file under concurrent refactor) |
| AC-9a all plan surfaces watch 3 realtime keys | ✅ table + patient page (`TreatmentPlans` **and** `Invoices` both added — the page had neither) |
| AC-12a « Solde patient » / « Créances » / dashboard agree | ✅ one shared helper on all three |
| AC-12b a Draft plan's échéancier contributes 0 everywhere | ✅ Draft excluded in both repository aggregates |
| AC-12c the de-dup rule exists in exactly one helper | ✅ `PlanBillingRules`; no money read reimplements it |
| AC-24 every plan verb is clinic-scoped, asserted with `Times.Never` | ✅ existing verbs; slice B's three pending |
| AC-24a `CancelPlan` pinned to `AdminOrDoctor` by reflection | ✅ (+ a drift guard over every action) |
| AC-25 `treatmentplans` pinned in `RealtimeResourceResolverTests` | ✅ |
| AC-13/13a `/treatment-plans/{id}` renders; introuvable + loading cards | ✅ (manual click-through still owed) |
| AC-14 one primary action per act, RDV/fiche navigation | ✅ |
| AC-15 parcours: création, acceptation, séances, actes, paiements, facture | ✅ (the facture row is undated — the DTO exposes no issue date; it sorts last) |
| AC-16 no detail dialog, no unlabelled icon row | ✅ retired; rows navigate, dropdown is labelled |
| AC-17 `PUT /appointments/{id}` moves + clears the plan link | ✅ (tri-state; 9 tests) |
| AC-17a progress bar ARIA, absent at 0 acts | ✅ shared `PlanProgressBar` |
| AC-10 payment on a Completed plan | ✅ (`EnsurePayable`) |
| AC-11 auto-close identical on both paths | ✅ (moved into `MarkItemDone`) |
| AC-12 handler no longer duplicates the rule | ✅ |
| AC-17a progress bar ARIA + absent at 0 items | ✅ |

## Auto-Approved Deviations

| Deviation | Reason |
|-----------|--------|
| Derived fields are populated on the **query** paths only; command responses leave `scheduledAt`/`linkedInvoice*`/`nextAppointmentAt` null (progress counts are always set) | Internal, no contract change — the fields are already nullable. The alternative is threading `IAppointmentRepository` + `IInvoiceRepository` through all 8 plan command handlers for data the frontend immediately discards: every mutation is followed by a reload, and `RealtimeBroadcastBehavior` fires a refetch anyway. Kept as a second `ToDto` overload so existing call sites are untouched. |
| The four **états** are derived in a pure frontend helper from the fields the spec pins, rather than adding a `workflowState` string to the DTO | The spec's API Contract enumerates the item additions and deliberately does not include an état field. The hard part — *which* appointment speaks for an act, and excluding Cancelled/NoShow — is server-side and tested there; mapping three pinned fields to a label is 4 lines of presentation. Also means the badge flips from « Planifié » to « À enregistrer » as the appointment time passes without needing a refetch. No pinned shape widened. |
| `TreatmentPlanWorkflowProjection` / `TreatmentPlanWorkflow` are `public`, not `internal` | An `internal` type cannot appear in the `public` `ToDto` signature, and both sibling helpers in this layer (`DentalRecordLinker`, `AppointmentPlanLink`) are already `public static`. Also keeps the projection directly testable without `InternalsVisibleTo`. |

## Significant Deviations

### DEV-1 — Type: Full spec implemented through the small-feature skill, in one pass
- **Spec/skill said:** `/implement-small-feature` requires `Type: Small` and an ~10-file envelope;
  it instructs escalation to `/plan-feature → /break-plan → /implement-story` past that.
- **Actual:** the spec is `Type: Full` with a real surface of ~60 files across 3.5 slices, 2 additive
  columns in one migration, 26+ acceptance criteria and 8 test classes.
- **Why:** surfaced to the user with the discovered file count and three scope options (escalate /
  slice A only / whole spec in one pass); the user chose **whole spec, one pass** after being shown
  the tradeoffs (no per-story review checkpoint, one unreviewable diff, migrations possibly
  hand-authored if `dotnet ef` is blocked again).
- **Impact:** no per-story review between changing domain invariants on a numbered financial document
  and rewriting four money reads. Mitigated by the 8 test classes being in scope.
- **Approved:** Yes (explicit).

### DEV-2 — Tests written in this pass, not deferred to `/test-small-feature`
- **Skill said:** "No tests in this skill. Skip writing tests entirely."
- **Actual:** the 8 test classes named in the spec's Tests section are implemented here.
- **Why:** the user's standing instruction on this feature is "do not defer anything related to this
  fix", the spec explicitly puts tests in scope ("**In scope for this feature — not deferred to
  `/test-small-feature`**"), and the scope option the user selected counted the 8 test classes among
  the ~60 files. The plan area currently has **zero** tests, so this is greenfield rather than
  modification of an existing suite.
- **Impact:** larger diff; `/test-small-feature` becomes a no-op for this feature.
- **Approved:** Yes (explicit, twice).

### DEV-3 — AC-9 (record modal auto-links the plan act) skipped in this worktree
- **Spec said:** opening the dental-record modal from an appointment carrying `treatmentPlanItemId` should
  pre-select that plan act instead of relying on the dentist picking it from a dropdown.
- **Actual:** not implemented here.
- **Why:** the user is concurrently refactoring that exact file on the base branch — `git status` on their
  checkout shows `M web/components/patient-record-modal.tsx` plus a new untracked `web/components/record/`
  directory (a two-pane tooth-first rewrite with `use-session-acts.ts`, `session-act-composer.tsx`,
  `session-acts-list.tsx`), none of which exists in this worktree's baseline. Editing the pre-refactor version
  would guarantee a merge conflict on ~5 lines of wiring, against a file whose structure is changing wholesale.
- **Impact:** AC-9 unmet. The plan-item dropdown still works manually, so nothing regresses — the loop is just
  one manual step longer than the spec wants. The change belongs on the post-refactor modal: read the
  appointment's `treatmentPlanItemId` and dispatch the existing plan-step prefill.
- **Approved:** Yes (surfaced to the user with the colliding `git status` before skipping).

### DEV-4 — the shared money de-dup helper lives in **Domain**, not Application
- **Spec said:** the "Documentation to update" list files it under
  `api/ClinicManagement.Application/CLAUDE.md` — "`TreatmentPlanWorkflowProjection` and the shared money
  de-dup helper" — implying `Application/Features/Billing/`.
- **Actual:** `Domain/Services/PlanBillingRules.cs`, documented in `Domain/CLAUDE.md`.
- **Why:** three things pushed it down a layer. (a) `TreatmentPlanRepository` needs the debt-bearing status
  set *inside its SQL* (that is where the Draft exclusion of AC-12b has to happen); an Infrastructure repo
  implementing a **Domain** interface reaching up into an Application helper is a layering inversion, and the
  alternative — restating the statuses in the repo — is the exact duplication AC-12c forbids. (b) "Which
  documents represent patient debt" is cross-aggregate domain policy, and `Services/` already holds
  `InvoiceCalculator`, the same kind of pure money rule. (c) It stays testable with zero mocks.
  `TreatmentPlanWorkflowProjection` correctly stays in Application — it shapes DTOs.
- **Impact:** the docs-pass chunk should file it under `Domain/CLAUDE.md` (already done) instead of
  `Application/CLAUDE.md`. No behavioural difference.
- **Approved:** auto (internal placement, no contract change).

### DEV-5 — the caisse gets the Draft exclusion but **not** the billed-plan de-dup
- **Spec said:** slice A2 — "`GetReceivablesQuery`, `GetCaisseSummaryQuery` and `GetDashboardStatsQuery` gain
  the same de-dup `GetPatientBillingSummaryQuery.cs:73-85` already has".
- **Actual:** the caisse gains the Draft/Cancelled exclusion (through
  `GetInstallmentCollectedBetweenAsync`, AC-12b) but no billed-plan suppression. `GetCaisseSummaryQuery.cs`
  itself is comment-only.
- **Why:** the de-dup is a rule about **outstanding debt**; the caisse's only plan-side figure is **cash
  collected**. The spec's own out-of-scope note records that the bridge invoice starts at
  `AmountCollected = 0` — the plan's installment receipts are never copied onto it — so there is no double
  count to remove. Suppressing a bridged plan's collections would delete real money from the daily till: a
  patient who paid 800 DT in échéances before the devis was bridged would see that 800 vanish from the caisse
  for the day they paid it. That is strictly worse than the inconsistency being fixed. Verified against
  `CreateInvoiceFromTreatmentPlanCommand`, which copies lines only, never payments.
- **Impact:** AC-12a/AC-12b hold as written (they are about outstanding and about Draft plans). If the
  deferred "collected installment money surviving the bridge" fix ever carries payments onto the invoice, a
  de-dup on the collected side becomes necessary *at that moment* — the reasoning is recorded in
  `ITreatmentPlanRepository`, `PlanBillingRules` and `GetCaisseSummaryQuery` so it cannot be missed.
- **Approved:** auto (documented; deviating would introduce a money bug).

## Deferred to a later pass (recorded in the spec, not lost)
- Collected installment money surviving the devis→facture bridge (pre-existing money bug).
- `GetDashboardStatsQuery` netting out `CreditNote` refunds (pre-existing inconsistency).
- Wider patient-page localization: `Back to Patient` in `patients/[id]/files`, three raw enum names
  rendered as user-facing text, `formatFileSize` using `B`/`KB`/`MB` instead of `o`/`Ko`/`Mo`.
- Séances / visit grouping; per-act invoicing; an audit/actor field on financial documents.
