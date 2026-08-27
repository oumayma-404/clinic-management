# Progress: Séance passée — a visit whose slot has ended says so

**Started:** 2026-08-16
**Type:** Small
**Branch:** feature/windows-desktop-app

## Status
- [x] Implementation
- [x] Quality checks (build, tsc, check:responsive, build, full unit suite)
- [ ] Tests (handled by /test-small-feature)

## Working tree note (start of session)

Three files carried **uncommitted work by another session** when this feature started. They are an agenda
visual pass, unrelated to this change, and are **excluded from this feature's commits**:

| File | In-flight change |
|---|---|
| `web/app/globals.css` | +28 — three agenda grid ground-mark tokens (`--agenda-today/guide/closed`) |
| `web/app/appointments/page.tsx` | +22/−17 — the active-filter chip row renders only when it has a chip |
| `web/components/appointment-calendar.tsx` | +65 — `STATUS_EDGE_PAINT` / `statusEdgePaint` / `renderStatusEdge`, a per-block status strip |

Also untracked and unrelated: `.playwright-mcp/`, `FEATURE-OVERVIEW.md`.

⚠️ **`appointment-calendar.tsx` is the one overlap.** The in-flight insertion sits at line ~228 and this feature
edits ~753 and ~1494, so the two do not collide. Its `statusEdgePaint` docstring describes *this feature's* data
bug as its own reason to exist ("a blocked hour acquired « En cours »… Nobody is at the fauteuil") — a **fourth**
rendering workaround for one wrong row. It is left intact: suppressing a statut strip on a blocked slot stays
correct once the data is fixed, because a block has no lifecycle worth reporting. `STATUS_EDGE_PAINT` is a
`Record<StatusTone, string>`, exhaustive over **tones** rather than statuses, so adding a status did not break it.

## Files Changed

**Domain**
- `Enums/AppointmentStatus.cs` — `AwaitingClosure = 7`
- `Entities/Appointment.cs` — transition rows, `FrenchLabel`, `MarkAwaitingClosure()`, `Reschedule` reset
- `Repositories/IAppointmentRepository.cs` — `GetElapsedOpenAsync`

**Infrastructure**
- `Repositories/AppointmentRepository.cs` — `ElapsedCandidateQuery` + `GetElapsedOpenAsync`
- `Persistence/Configurations/AppointmentConfiguration.cs` — `HasIndex(Status, AppointmentDateTime)`
- `Migrations/20260816182154_AddAppointmentStatusDateIndex.*` — scaffolded; index only, **no `xmin`**

**Application**
- `Features/TreatmentPlans/TreatmentPlanWorkflowProjection.cs` — `LiveStatuses` += the new status
- `Common/Csv/ExportTables.cs` — the agenda's `Statut` column is French

**API**
- `BackgroundJobs/AppointmentProgressJob.cs` — `CloseElapsedAppointments`, 30-day lookback, both passes per tick

**Web**
- `components/appointment-labels.ts` — status, label, tone, `MANUALLY_SETTABLE_STATUSES`
- `components/edit-appointment-dialog.tsx` — fallback uses the manually-settable set
- `components/appointment-calendar.tsx` — blocked slots exempt from the status switches
- `lib/dashboard/day-summary.ts` — `OCCUPYING` + `chairClaim`'s `started` term

**Tests (build/green-required only — see Auto-Approved Deviations)**
- `Domain/AppointmentStatusTransitionTests.cs` — `AppointmentAt` / `MoveTo` / the `Reschedule` exclusion

**Docs** — root `CLAUDE.md`, `api/ClinicManagement.Domain/CLAUDE.md`, `api/ClinicManagement.API/CLAUDE.md`

## Quality checks

| Gate | Result |
|---|---|
| `dotnet build` Infrastructure | **0 errors**, 45 warnings — all the repo's pre-existing `CS8618`/`CS8981` baseline, none in a changed file |
| `dotnet build` API (scratch `-o`) | **0 errors**, 13 warnings, none in a changed file |
| `dotnet build` UnitTests | **0 errors** |
| `dotnet test` (full suite, `-c Release`) | **3362 passed, 0 failed** |
| `npx tsc --noEmit` | clean |
| `npm run check:responsive` | **17/17** |
| `npm run build` | succeeds |
| Eye pass 320/390/820/1180/1440 | **NOT DONE — owed.** No browser was driven this session. The change is a badge label, a tone and two filter predicates; no layout was touched, so the mechanical gate covers more of it than usual — but that is an argument for low risk, not evidence of a pass. |

## Auto-Approved Deviations

| Deviation | Reason |
|-----------|--------|
| Tone is `pending`, not the blueprint's `"warning"` | `warning` is not a `StatusTone` member — the amber tone is `active` and `InProgress` owns it. `pending`'s own docstring is "Awaiting an action or a decision", exactly what this is. The pair that must never be confused (« En cours » / « Séance passée ») stays distinct. Pre-flagged in the approved blueprint. |
| `AwaitingClosure` stays **draggable** on the agenda (blueprint said add it to the non-draggable set) | Dragging maps to `Reschedule`, which *works* from this status (drops to « Planifié ») exactly as it does from `NoShow` — which is draggable today. The real rule is "draggable iff `Reschedule` won't throw", so excluding it would have been the inconsistent choice. Correcting my own blueprint, not the spec. |
| `AppointmentStatusTransitionTests` updated here rather than deferred | Three derived guards went red on the new enum member — all one root cause: the helpers `AppointmentAt`/`MoveTo` had no path to it, and `Reschedule` needed the same documented exclusion `InProgress` already has. Translating a broken call to the new seam while preserving the original assertion, per the skill's "don't leave them red". No new scenario was written. |
| `day-summary.ts` `chairClaim` counts the new status as `started` | Not in the spec, but required to *preserve* existing behaviour: `ddb7bd8` documents "a visit started this morning and never closed still claims the chair". Keying on `inprogress` alone would silently withdraw the chair from a visit running fifteen minutes long — a dashboard regression as a side effect of a status rename. |

## Significant Deviations

**DEV-1 — the CSV `Statut` defect is wider than this feature, and only the agenda's column was fixed.**
`ExportTables` writes the raw **English** enum name in *four* exports (appointments, invoices, treatment plans,
lab orders) — the same defect L5 fixed for the « Sexe » column. Fixing the agenda's was **not optional**: without
it the export would print `AwaitingClosure`, an invented English word. The other three each need their own label
authority (`InvoiceStatus`, `TreatmentPlanStatus`, `LabOrderStatus`) and that is a wider change than this one.
Left alone, with the reason stated in a docstring at the fix site. **Not approved by the user — flagged for a
decision.** Candidate for `/capture-followup`.

## Deferred to /test-small-feature

Genuinely **new** scenarios this change enables — none exist yet:

1. **`AppointmentElapsePassTests`** (new class) — a patient visit past its end → `AwaitingClosure`; still running
   → untouched; a **blocked** slot past its end → `Completed`; blocked and running → still `InProgress`;
   `Cancelled`/`NoShow`/`Completed` → untouched **and no save issued** (assert the `IUnitOfWork` mock was never
   called — this is the empty-save/audit-spam regression the start pass's two-term guard exists for); older than
   the 30-day window → untouched; one clinic throwing does not stop the others.
2. **`AppointmentProgressQueryTranslationTests`** — add `ElapsedCandidateQuery`, so the expression tree is proven
   translatable rather than discovered at runtime.
3. **`VisitClosureRulesTests`** — an `AwaitingClosure` visit has `NextStep == Presence`; a `Completed` one missing
   its fiche still has `NextStep == Fiche`. This pair is what proves badge and worklist answer *different*
   questions.
4. **`TreatmentPlanWorkflowProjectionTests`** — an act booked into a visit that ended yesterday does not revert to
   « À planifier ».
5. **`AppointmentStatusTransitionTests`** — a direct positive assertion that `Reschedule` from `AwaitingClosure`
   lands on `Scheduled` (the exclusion added this session only stops the *refusal* sweep from failing on it).
6. **`ExportTables`** — the agenda's `Statut` column is French.
