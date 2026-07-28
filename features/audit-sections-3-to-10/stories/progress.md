# Progress — Audit Sections 3–10

**Story:** [story-1-audit-sections-3-to-10.md](./story-1-audit-sections-3-to-10.md) — one story, eight ordered parts
**Branch:** `feature/audit-sections-3-to-10`
**Started:** 2026-07-28
**Type:** Full (one story, eight parts)

> `/break-plan` was deliberately skipped — with a single story there is nothing to decompose. This file is the resume
> state; the steps live in [../plan.md](../plan.md).

## Status

| Part | Delivers | Depends on | Status |
|---|---|---|---|
| **P1** Appointment lifecycle & booking | 3.1, 3.2, 3.4, 5.4, 6.1, 6.9, 8.1, 8.4 | — | **in progress** — steps 1–3, 5–7, 10, 14 + a11y done (`02dcc17`, `a813268`, `767ecae`, `1574c2e`). **Remaining: the 3 migrations (SAC-blocked), the per-doctor hours editor UI, the calendar grid, `verify-schema`** |
| **P2** Finish what's built | 5.1–5.3, 5.5–5.9, 5.11, 5.12, 6.10, 6.11, 8.3 | — | **complete** — all 13 steps, AC-P2.1–2.45 |
| **P3** UX, accessibility & French | 3.3, 6.3, 7.1–7.9, 8.2, 8.6 | — | not-started |
| **P4** Stock, realtime & schema | 6.6, 6.7, 6.12, 9.1–9.6 | — | not-started |
| **P5** Build & tooling | 10.1–10.5 | warnings step **last in story** | not-started |
| **P6** Money truth & timezone | 4.1, 4.2, 5.10, 6.2, 6.8, 8.5, 9.7 | — | not-started |
| **P7** Audit trail, duplicate prevention, anonymize | 6.4 | P7a first | not-started |
| **P8** CNAM claims & reconciliation | 6.5 | **Q-1…Q-6 unanswered** | blocked |

**Bullets closed: 13 / 57 · ACs: 45 / 301** — all of P2 (§§ 5.1–5.3, 5.5–5.9, 5.11, 5.12, 6.10, 6.11, 8.3) plus
adjacent defects **A-8**, **A-9**, **A-11**, **A-12**, **A-13**, **A-14**.

## Working tree note (start of session, 2026-07-28)

Untracked/modified paths present at the start that are **not** part of this story's code changes. Files are staged
explicitly by path — never `git add -A` / `git add .`:

| Path | Note |
|---|---|
| `features/audit-sections-3-to-10/` | This feature's own artifacts (spec/plan/design/exploration/mockups/stories). Committed as documentation, separately from code. |
| `follow-up/patient-merge.md`, `follow-up/mockups/`, `follow-up/README.md` | The dropped patient-merge design, moved here deliberately. Belongs to this feature's scope decision; commit with the artifacts. |

> Note for future sessions: an earlier merge into `feature/windows-desktop-app` used `git add -A` and swept this
> feature's `exploration.md` into an unrelated commit (`b0c472e "claude files"`). Stage by path.

## ⚠️ BLOCKER, partly resolved — Smart App Control (2026-07-28)

**Resolved for the test suite.** Clearing every `bin/`+`obj/` and running `dotnet build-server shutdown`
makes freshly-built assemblies loadable again. Current state: **919 pass, 0 real failures**.

**Still blocking 279 tests.** SAC blocks `ClinicManagement.Infrastructure.dll` **specifically, by content** —
`Unblock-File` does nothing (there is no MOTW/Zone.Identifier) and copying a byte-identical copy that loaded
successfully moments earlier is also refused. Every one of the 279 failures traces to that single file: 235
`FileLoadException`, 29 `TypeInitializationException`, 2 `ReflectionTypeLoadException`, and 13 `Assert.Throws`
failures that are all inside `UnitTests.Infrastructure.*` (the expected exception arrived as a
`FileLoadException`). To verify your own work, filter to the classes that do not load Infrastructure.

**Still blocking migrations, intermittently.** `dotnet ef migrations add` fails the same way
(`FileLoadException … ClinicManagement.Infrastructure.dll … 0x800711C7`). It succeeded **once**, immediately
after a `bin` clean + build-server shutdown, then broke again — so SAC's verdict is time-varying, presumably a
cloud-reputation lookup. **Retry after a clean; do not assume a failure is permanent.**

> ⚠️ **Do not mix `dotnet-ef` versions.** The global tool is **10.0.3**; installing a local 8.0.11 and running
> `migrations add` with it emitted a spurious `AddColumn "TokenVersion"` for a column that already exists in
> both the DB and the snapshot, because the committed snapshot was written by EF 10 (`.ValueGeneratedOnAdd()`
> on that property). Applying it would have failed on "column already exists". The bad migration and its
> snapshot edit were reverted; the local manifest was removed. **Generate with the global EF 10 tool only.**

## ⛔ Original blocker as first diagnosed (kept for the record)

**Windows Smart App Control now blocks every freshly-built test assembly** with `0x800711C7`, including the
project's own `bin/Debug/net8.0/`. The documented workaround —
`dotnet build -p:OutDir=<scratch>/ ` then `dotnet vstest` — **worked earlier the same day** (it produced the
verified 1160-pass behind the P2 commit `ec1f6ff`) and then stopped, on the same paths and every new path tried
(scratch dir, a fresh scratch dir, the user profile, the project's own `bin/`).

**The trap to avoid.** `dotnet test --no-build` *appears* to pass — it reports the same **1160** as before P1 —
because it is loading the last SAC-approved DLL, which predates the new tests. That is precisely the stale-DLL
false negative `UnitTests/CLAUDE.md` warns about. A run whose count has not moved after adding tests is not a
pass.

**To unblock** (one of):
- Turn Smart App Control off (Windows Security → App & browser control → Smart App Control). It is one-way:
  Windows cannot re-enable it without a reinstall.
- Or run the suite somewhere SAC does not apply (WSL, CI — which is exactly what **AC-P5.4** exists to add).

Until then the backend gate is `dotnet build --no-incremental` only, which proves compilation but not behaviour.
**Do not build further parts on top of unverified work** — P4 and P7 in particular carry 11 migrations and a
~38-handler retrofit that must not be written blind.

## Decisions carried in from planning

| # | Decision | Where |
|---|---|---|
| 1 | **One story, eight parts** — user decision, twice. Part boundary is the commit/resume point | plan **R-1** |
| 2 | **Patient merge dropped**, replaced by duplicate *prevention*. Design preserved at `follow-up/patient-merge.md` | spec US-P7b |
| 3 | **CNAM reimbursement is its own entity**, not a `Payment` — `PaymentMethod` untouched | spec AC-P8.19 |
| 4 | **Bordereau data model now, calibrated AP1/AP2 overlay when the asset is supplied** | spec AC-P8.16 |
| 5 | **Audit trail is one `AuditEntry` table**, inline per handler, mutation fails on audit-write failure | spec AC-P7.5–7.6 |
| 6 | **Duplicate matching = persisted normalized column** (no extension, no functional index) | spec AC-P7.22 |
| 7 | **Decimal precision = full normalization** — convention + 26 deletions, not the convention alone | spec AC-P4.37 |
| 8 | **`verify-schema` console verb** is the only gate for schema-level changes | plan Testing Strategy |
| 9 | **CI is in scope** (AC-P5.4) unless explicitly declined | spec Q-9 |

## Deviations

### DEV-1: AC-P2.10's billed-act check is plan-level, not act-level — **needs a decision**
**Date:** 2026-07-28 · **Story:** 1 (P2, step 1) · **Category:** Scope
**Original Plan:** "refused when the act is billed on a non-cancelled invoice" (AC-P2.10).
**Actual Implementation:** `UnmarkTreatmentPlanItemDoneCommand.EnsureNotBilledAsync` reuses
`AmendTreatmentPlanCommand`'s guard verbatim — `IInvoiceRepository.GetTreatmentPlanLinksAsync` +
`PlanBillingRules.BilledPlanIds` — which refuses when a non-cancelled invoice represents **the devis**.
**What is NOT covered:** the narrower case where the act's own *fiche de soins* is billed on an `InvoiceLine`
(`InvoiceLine.DentalRecordId`) without a devis→facture bridge invoice existing. There is **no light repository
projection for that** — only `GetFilteredAsync`, which loads every invoice of the patient with its lines and
payments, and which § 9.7 of this same spec exists to stop calling. Covering it properly means adding a
`GetDentalRecordLinksAsync` projection mirroring `GetTreatmentPlanLinksAsync`.
**Justification for stopping here:** the plan-level guard is the precedented rule, is the one the amend/revise
handlers apply, and catches the real desync (a bridge invoice already billing the plan). Adding new repository API
surface mid-step is a significant deviation, so it is surfaced rather than taken.
**Impact:** an act whose fiche is billed but whose plan has no bridge invoice can still be un-marked. The invoice is
unaffected — only the plan's état would disagree with the money.
**Approved:** ✅ **Resolved 2026-07-28 — option (a).** Added `IInvoiceRepository.GetDentalRecordLinksAsync`, a light
projection mirroring `GetTreatmentPlanLinksAsync` exactly (`SelectMany` over lines, `Distinct`, no lines/payments
graph). `EnsureNotBilledAsync` now checks **both** routes work reaches an invoice by: the devis→facture bridge, and
a line billing the act's own fiche. AC-P2.10 now holds as written, so the spec needed no rewording. Chosen over (b)
because narrowing an approved AC is worse than adding a precedented projection — and `GetFilteredAsync`, the only
alternative, is the over-fetch § 9.7 of this same spec exists to remove.

## Auto-Approved Deviations

| Deviation | Classification | Reason |
|-----------|----------------|--------|
| `User.Update(role, email?, fullName?)` **deleted** rather than fixed in place | Trivial (internal, zero callers) | AC-P2.25 requires that a role change not null email/fullName. The method had **no caller anywhere** and was the exact trap the AC names, so it was replaced by the validated `ChangeRole`. Leaving a dead, wiping overload beside the safe one invites the next caller into the same defect. |
| A-8's English + `ex.Message` leak fixed in **`SetUserActiveCommand`** and **`ResetUserPasswordCommand`** too | Trivial (same defect class, no API change) | Step 7 builds its sibling command on `SetUserActiveCommand`'s guards, and the plan's P2 "Done when" is *no `ex.Message` in any handler this part touched*. Fixing one of a pair and leaving the other is the drift the sweep exists to prevent. Same fix in `UpdateLabWorkOrderStatusCommand` (step 12), whose generic catch was the only thing that could see an illegal-transition message. |
| `document-editor-content.tsx`'s `colorHex: "#3b82f6"` changed to a palette colour | Trivial (one literal, no API change) | Found while wiring AC-P2.36. `#3b82f6` is **not** on the `ColorHex` curated palette, so that create-procedure call threw `ArgumentException` every time it ran. Exactly the drift A-14 is about; leaving it would have been shipping a known crash next to its own fix. |
| `CUSTOM_PROCEDURE_COLORS` (`create-appointment-dialog.tsx`) left hardcoded | Trivial (comment only) | AC-P2.37 names *the* hardcoded array — the picker in `procedure-type-form-modal.tsx`, now server-fed. This second copy is a rotation **seed** for an on-the-fly procedure, all of whose values are on the palette, and fetching it would add a round-trip to the booking dialog for a colour the dentist never sees. Comment now points at the authority. |
| `mode` destructure removed from `dashboard-sidebar.tsx` | Trivial (internal, same file) | Step 8 deleted its only use; leaving the binding reads as a mode gate that no longer exists. |

## Test Regressions

_None yet._ Expect `PostVisitReviewCompletionTests` to require rewriting in P1 — it currently pins the silent-no-op
contract that AC-P1.12 supersedes. That is an intentional behaviour change, not an implementation bug.

## Learnings

### A "wire the uncalled endpoint" step is only cheap when the form the plan reuses matches the endpoint's shape
Steps 5–6 were scoped as *wiring* — the endpoints were finished, validated and `AdminOrDoctor`-gated. But the
plan form they reuse is **draft-shaped**: it replaces the whole act list and drops installment ids, because on a
draft the server replaces everything anyway. The amend endpoint is *delta*-shaped (`addItems` + `removeItemIds`)
and revises installments **by id**. Reusing the form therefore meant changing what its rows carry and locking
fields that mode cannot honour — not adding a button. **Recommendation:** when a step says "reuse form X to call
endpoint Y", diff X's payload against Y's contract before estimating. A field X drops (here the installment id)
is the tell: harmless on the path X was built for, destructive on the new one.

### Two gates over the same aggregate can legitimately disagree — write down which invariant each protects
The workspace ended up with `canAmend` (`isActive && !billed`) and `canCorrectActs` (**includes `Completed`**).
That looks like an inconsistency and would be "tidied" by the next reader, but it mirrors `EnsureAmendable` vs
`EnsureCorrectable`: one guards *doing* work, the other guards *correcting what was recorded* — and since marking
the last act done auto-completes the plan, a correction gate that excluded `Completed` could never reach the case
it exists for. **Recommendation:** when two gates over one aggregate differ, put the reason in the code at both
sites, not just in the spec.

### A display-time map can reach a *printed* document without touching the backend — if the value is a snapshot
AC-P2.42 asked for French in the printed certificat and the PDF/DOCX signature block, which are rendered
server-side. That reads like it needs a backend map. It did not: the server renders
`MedicalDocument.DoctorSpecialty`, a **snapshot supplied by the client** and never re-resolved, so mapping the one
frontend value every surface derives from covered all six — and correctly, because that column records the text
that was printed (existing rows already hold French), unlike the catalog key. **Recommendation:** before adding a
parallel map on the server, check whether the value it renders is a snapshot of what the client displayed. If it
is, one map at the client's derivation point is the whole fix — and make the map idempotent (pass unknown values
through) so re-saving historical rows is safe.

## Session log

### 2026-07-28 — P1 steps 5–7, 10, 14 + a11y (commits `a813268`, `767ecae`, `1574c2e`)

Closes **AC-P1.6, 1.20–1.21, 1.23, 1.26–1.32, 1.36–1.46, 1.53** and **A-3, A-5, A-6, A-7, A-10** (+ two more
`ex.Message` leaks found in the same files: `GetDoctorWorkingHoursQuery` and an English one in
`CreateDentalRecordCommand`).

| Delivered | Where |
|---|---|
| Real working-hours validation; `Unreadable` distinguished from `Unset` | `DTOs/WorkingHoursDto.cs` |
| `WorkingHoursResolver` — doctor → clinic → none, the one resolver (there was none) | `Common/Services/` |
| `ClinicClock` — Tunisia UTC+1, replaces two copied private helpers (**also P6 step 1**) | `Common/ClinicClock.cs` |
| `AppointmentScheduling` — the single overlap rule + the hours check | `Features/Appointments/` |
| Hours + collision enforced on create, update-on-schedule-change, every recurring occurrence | 3 handlers |
| `Appointment.BookedOutsideWorkingHours` + any-role override | Domain |
| `appointment-labels.ts` — status + gender, replacing 4 render styles and 3 colour palettes | `web/components/` |
| Status Select driven by `allowedNextStatuses`; recurring conflict list with « Replanifier »; 6 a11y labels | `web/` |

**Findings worth keeping.**
1. **`"[]"` counted as valid working hours** and wiped a clinic's real hours through `UpdateClinicCommand`;
   `SetDoctorWorkingHoursCommand` never null-checked `Normalize`, so an invalid payload silently *cleared* the
   override. Both closed.
2. **The overlap predicate had already drifted in production**: the recurring copy excluded only `Cancelled`,
   not `NoShow`, so a series refused slots the single-appointment path considered free.
3. **A series could double-book itself.** `existing` is loaded once before the loop, so occurrences were never
   checked against each other — harmless until the DB constraint lands, at which point one violation would
   abort the whole series instead of skipping one occurrence.
4. **My own new test caught a real gap in itself**: `Reschedule` *preserves* `Confirmed`/`InProgress` (the A-2
   fix), so it never attempts a transition to `Scheduled` and correctly does not throw. `Confirmed → Scheduled`
   was removed from the table and is asserted directly instead.

| Gate | Result |
|---|---|
| Backend build (`--no-incremental`) | 0 errors, 56 warnings (baseline), 0 in changed files |
| Unit suite | **919 pass / 0 real failures**; 279 SAC-blocked (see the blocker section) |
| Appointment-related classes incl. `ConcurrencyConflictTests` | **134 / 134 pass** |
| `tsc --noEmit` · `npm run build` | clean · clean, 27/27 pages |

⚠️ **`Appointment.BookedOutsideWorkingHours` has no migration yet** — the column exists in the model only.
That is the first thing to generate once SAC lets `migrations add` through.

### 2026-07-28 — P1 steps 1–3: one appointment status machine (partial P1)

Commit `02dcc17`. Closes **AC-P1.1–1.9, AC-P1.12–1.13** + **A-1**, **A-2**. Stopped here because the test gate
died (see the blocker at the top) — not at a natural part boundary, but at a **safe** one: this slice adds **no
migration**, so nothing is half-applied. The dangerous stopping points the plan names (R-1) are *inside* the
schema work, which has not started.

| Delivered | Where |
|---|---|
| `AllowedTransitions` + `NextStatusesFrom`/`CanTransition`/`FrenchLabel`; one guard every mutator funnels through | `Domain/Entities/Appointment.cs` |
| The fall-through `switch` replaced by a table lookup returning `Result.Failure` | `UpdateAppointmentCommand` |
| `AllowedNextStatuses` on the DTO, populated at all 4 mapping sites | `AppointmentDto` + 4 handlers |
| `VisitCompletionOutcome` (Completed / AlreadyCompleted / **Contradicted**) + both callers updated | `Domain/Enums/`, dental-record + medical-document handlers |
| `CancellationReason` finally reaches `Cancel()` | `UpdateAppointmentCommand` |
| New `AppointmentStatusTransitionTests` (there was **no** `Appointment` domain test file); `PostVisitReviewCompletionTests` rewritten per AC-P1.13 | `UnitTests/Domain/`, `UnitTests/Features/Documents/` |

**Three design points worth keeping.**

1. **`Confirmed → Scheduled` is deliberately absent from the table.** "Withdrawing a confirmation" has no
   clinical meaning and was already unreachable (the old switch's `Scheduled` arm only acted on a *cancelled*
   appointment). Including it would also have needed a domain method that does not exist, because `Reschedule`
   now *preserves* `Confirmed` — which is the entire point of the A-2 fix. Caught while writing the command
   layer: the first draft called `Reschedule` for that edge and would have silently not changed the status.
2. **`NoShow → Cancelled` is kept.** `CancelRecurringSeriesCommand` voids a whole series without skipping missed
   occurrences, so dropping that edge would have made it throw on rows it cancels today.
3. **`MarkVisitCompleted` returns rather than throws**, and `Contradicted` does **not** reopen the appointment.
   Throwing would jump over `CancelPostVisitReviewAsync` and leave the post-visit prompt nagging forever;
   reopening would silently un-cancel a visit, which is the invisible state change this story exists to remove.

| Gate | Result |
|---|---|
| Backend build (`--no-incremental`) | **0 errors**, 56 warnings (baseline), **0 in changed files** |
| Unit suite | ⛔ **could not run** — Smart App Control. See the blocker section. |

**Remaining in P1** (13 of 16 steps): working-hours validation + the per-doctor editor (A-5, A-10, § 5.4), hours
enforcement + the non-HTTP-writer rules, the calendar grid, the recurring conflict list (A-7), the exclusion
constraint + pre-flight + `23P01` translation, the `Type:`-prefix migration, `appointment-labels.ts` (A-6), the
dialog a11y/French pass, and the `verify-schema` verb.

**Groundwork already done for the rest of P1** (findings, not code):
- The dev DB was **6 migrations behind the code** and has been brought current (`dotnet ef database update`) —
  the app would have applied these on next start anyway.
- **R-4 pre-flight is clean: 0 pre-existing overlapping pairs** with the correct predicate
  (`Status NOT IN (5,6)` — Cancelled=5, NoShow=6; a first pass using `(4,5)` was wrong and was redone).
- The generated end-column expression is **verified against real rows**:
  `Duration * interval '1 microsecond' / 10` turns 18000000000 ticks into `00:30:00`.
- `btree_gist` **1.7 is available and not installed**, confirming the plan's superuser correction.
- **Both `Type:`-prefix rows in the DB carry trailing free text** the plan did not anticipate —
  `Type: Prothèse amovible (partielle / complète) (dents 21, 22, 23, 24)`. A naive strip would destroy the
  teeth list, so the migration must match the **longest** catalog name the note starts with and keep the
  remainder.
- Premise corrections for AC-P1.29: **`PromoteWaitingListEntryCommand` does not create appointments** (it
  promotes against a caller-supplied id, so it inherits `CreateAppointmentCommand`'s rule), and
  `GoogleCalendarSyncService` is the **only** true repository-level bypass (create at `:707`, reschedule at
  `:519`, both inner-swallowed, `doctorId` hardcoded `null` on create).
- There is **no effective-hours resolver anywhere** (exhaustive grep), and `getWorkingHours`/`setWorkingHours`
  have **zero callers** — the per-doctor feature is backend-complete and unreachable from the product.
- `WorkingHoursSerializer.Normalize` validates only JSON well-formedness: **`"[]"` is "valid"** and wipes a
  clinic's hours through `UpdateClinicCommand`, and `SetDoctorWorkingHoursCommand` silently *clears* the
  override when `Normalize` returns null (it never checks, unlike `UpdateClinicCommand:137-140`).

### 2026-07-28 — P2 steps 4–13: **P2 complete**

Continued from step 4 and finished the part in one pass. Ten steps, all thirteen of P2's capabilities now have a
caller.

| Step | Delivers | Where |
|---|---|---|
| 4 | « Supprimer » on the fiche + document rows behind `AlertDialog`, confirmation **naming the invoice** and the plan acts that will revert; `AdminOrDoctor` on **both** deletes (A-12); A-8's English + raw-exception leak fixed | `patients/[id]/page.tsx`, `DentalRecordsController`, `MedicalDocumentsController`, `DeleteMedicalDocumentCommand` |
| 5 | « Modifier le devis » — the plan form in **amend mode** (`addItems`/`removeItemIds`/`installments`), refusals in `FormErrorBanner`, revision badge live | `plan-workspace.tsx`, `treatment-plan-form-modal.tsx` |
| 6 | « Modifier l'échéancier » — new `ReviseInstallmentsModal`, locked rows shown **before** submit | `revise-installments-modal.tsx` |
| — | **AC-P2.11** (not in the numbered list but a P2 AC): « Détacher » on a `done` act; `markItemDone` client fn **deleted**, `markItemUndone` added | `plan-act-row.tsx`, `treatment-plans.ts` |
| 7 | Role change: `User.ChangeRole` + `AssignableRoles`/`NormalizeRole`, `ChangeUserRoleCommand`, `PUT /users/{id}/role`, rôle Select | `User.cs`, `Features/Users/`, `user-management.tsx` |
| 8 | Utilisateurs visible to any admin in **both** modes; reset-password hidden in Cloud with an explanation | `dashboard-sidebar.tsx`, `user-management.tsx` |
| 9 | `doctorsApi.updateProfile` (the missing client fn) + `DoctorDocumentIdentityDialog` on the roster | `doctors.ts`, `clinic-settings.tsx` |
| 10 | `POST /googlecalendar/disconnect` (`AdminOnly`, MediatR) — the first caller of `ClearGoogleCalendarConnection()` | `DisconnectGoogleCalendarCommand`, `appointments/page.tsx` |
| 11 | Palette from `GET /procedure-types/colors`; hardcoded array + "must match backend" comment deleted | `procedure-type-form-modal.tsx` |
| 12 | `LabWorkOrder` transition table + `NextStatusesFrom`, `ReceivedDate` re-stamped, UI offers only legal stages | `LabWorkOrder.cs`, `LabWorkOrderDto`, `lab-orders/page.tsx` |
| 13 | `web/lib/specialties.ts` — one shared constant, French display map, three duplicate arrays collapsed | 3 wizards + roster + 2 pickers + document editor |

**Three load-bearing design points.**

1. **Amend mode locks existing acts rather than pretending to edit them.** `POST /amend` takes `addItems` +
   `removeItemIds` only — there is no "update this act". A form that let the user retype a designation would
   have silently discarded it. So an existing act's fields are read-only and « Retirer » + « Ajouter un acte »
   is how you change one. `InstallmentRow` also had to gain `id` **and** `amountPaid`: it previously dropped
   the id, which is harmless on a draft (the server replaces the whole schedule) but on an amendment erases
   collected cash — the server refuses it outright.
2. **The correction path is gated differently from the amendment path, on purpose.** `canAmend` is
   `isActive && !billed`; `canCorrectActs` admits **`Completed`**. Marking the last act done auto-completes the
   plan, so a correction gated on an *active* plan could never reach the case it exists for — the same reasoning
   that put `EnsureCorrectable` beside `EnsureActive` in step 1.
3. **Specialties are mapped at `formData.doctorSpecialty`, one point, six printed surfaces.** The letterhead,
   the certificat sentence, the DOCX letterhead + signature block and the preview's letterhead + signature block
   all derive from it — and because that value is the snapshot persisted on the document and re-rendered by the
   server-side PDF, the **printed** certificat is French with no backend change. Correct rather than a migration:
   `MedicalDocument.DoctorSpecialty` records what was printed (existing rows are already French), unlike
   `Doctor.Specialty`, which stays the English catalog key. `specialtyLabel` passes unknown values through, so
   re-saving an old French snapshot is idempotent. Plan risk **R-11 respected** — not one `documentType` switch
   was touched.

| Gate | Result |
|---|---|
| Backend build (`--no-incremental`, full solution) | **0 errors**, 56 warnings — **0 in any file this part created or edited** (verified by grepping the warning set for each changed filename) |
| **Full unit suite** (`dotnet build -p:OutDir=…` + `dotnet vstest`) | **1160 passed, 0 failed** — +62 tests on the 1098 baseline |
| `npx tsc --noEmit` | 0 errors |
| `npm run build` | clean, **27/27** static pages |
| `verify-schema` | **not applicable** — P2 adds no migration (the two DTO additions are computed, not stored) |

**New tests (+62).** `ChangeUserRoleCommandHandlerTests` (13 — the closed set as a `[Theory]`, email/fullName
preserved, `TokenVersion` bumped, the no-op case, and four self-lockout cases incl. *deactivated other admin
does not count*), `LabWorkOrderStatusTransitionTests` (28 — every legal and illegal pair, `ReceivedDate`
re-stamping, and `NextStatusesFrom` asserted to agree with what `SetStatus` accepts so the UI cannot be offered
a transition the server refuses), `DisconnectGoogleCalendarCommandHandlerTests` (5),
`ClinicalRecordDeletionAuthorizationTests` (4), plus 2 on `MedicalDocumentTenantIsolationTests` pinning A-8's
message.

**Guard tests kept honest, as `NEXT-SESSION.md` warned:** `GoogleCalendarControllerHardeningTests` classifies
the new `Disconnect` action in both its `[Theory]` lists (its ctor also needed the new `IMediator`);
`TreatmentPlansControllerAuthorizationTests`' drift guard was already satisfied by step 1–2.
`AdminSurfaceCoverageTests` (the hardcoded array) needed no change — no new `AdminOnly` **catalog** surface was
added, and the new admin actions are pinned by the two dedicated classes above instead.

### 2026-07-28 — P2 steps 1–2: the plan-act un-mark (§ 5.3) + the A-13 role gate

**Started with P2**, not P1 — P1–P6 are independent per the plan, and P2 closes the most bullets with no schema
change, so the first commit carries no migration risk.

| Change | File |
|---|---|
| `TreatmentPlanItem.Unmark()` — returns to `Planned`, clears `DoneDate` + `LinkedDentalRecordId`; returns `bool` so the root can tell a real correction from a no-op | `Domain/Entities/TreatmentPlanItem.cs` |
| `TreatmentPlan.UnmarkItemDone(itemId)` — the exact inverse of `MarkItemDone`'s promotions | `Domain/Entities/TreatmentPlan.cs` |
| `EnsureCorrectable()` — Accepted/InProgress/**Completed** | `Domain/Entities/TreatmentPlan.cs` |
| `UnmarkTreatmentPlanItemDoneCommand` + handler | `Application/Features/TreatmentPlans/Commands/` |
| `POST /api/treatment-plans/{id}/items/{itemId}/undone`, `AdminOrDoctor` | `API/Controllers/TreatmentPlansController.cs` |
| **`MarkItemDone` gains `AdminOrDoctor`** (adjacent defect **A-13** — it had no policy at all) | same |
| Both actions reclassified in the drift guard | `UnitTests/Api/TreatmentPlansControllerAuthorizationTests.cs` |
| 8 new domain tests | `UnitTests/Domain/TreatmentPlanItemUnmarkTests.cs` |

**The load-bearing design point.** `UnmarkItemDone` deliberately does **not** use `EnsureActive`. Marking the last
act done auto-completes the devis, so a correction gated on an *active* plan could never reach the case it exists
for — and `EnsureAmendable` then refuses every amendment, which is what made one wrong fiche permanent. Hence a
separate `EnsureCorrectable` that admits `Completed`. `Unmark_Restores_The_Ability_To_Amend_The_Devis` pins it: it
asserts `RecordAmendment()` throws *before* the un-mark and succeeds after. Without the reopen the un-mark would be
cosmetic.

**Status transitions are the exact inverse of the forward path** — `Completed` → `InProgress` when another act is
still done, → `Accepted` when none are. A reopen that disagreed with `MarkItemDone` would leave the plan in a state
the forward path can never produce.

| Gate | Result |
|---|---|
| Backend build (`--no-incremental`, full solution) | **0 errors**, 56 warnings — **0 in changed files** (all pre-existing `CS8618` in Domain entities/value objects). Note the baseline is now **56**, not the 58 recorded at the § 2 merge — § 1 merged since |
| Test project build | 0 errors |
| New tests | **8 / 8 passed** (`TreatmentPlanItemUnmarkTests`) |
| Affected suites (`TreatmentPlan*`, `AdminSurface*`, `ControllerAuthorization*`) | **139 / 139 passed** — run because moving `MarkItemDone` between authorization classes could regress callers |

**DEV-1 resolved in the same session** (option a): added the light `GetDentalRecordLinksAsync` projection so
AC-P2.10's billed check covers both routes — the devis→facture bridge *and* a line billing the act's own fiche.

| Gate (re-run after DEV-1) | Result |
|---|---|
| Backend build | **0 errors**, 56 warnings, 0 in changed files |
| **Full unit suite** | **1098 / 1098 passed, 0 failed** |

> ⚠️ **Both recorded baselines were stale — corrected here.**
> **Warnings: 56**, not 58 (the § 2-merge figure). **Tests: 1098 passed / 0 failed**, not 941 / 3 — § 1's merge
> brought ~150 tests *and* fixed the three `ReminderSchedulerTests` that had been carried as known-failing since
> before § 2. **The suite is now fully green**, so any red from here is this story's own and must be treated as a
> real failure, not attributed to a pre-existing baseline.

### 2026-07-28 — Session 0 (scaffold only, no code)

Created this file and the story pointer. `/break-plan` skipped deliberately (see the note at the top). No production
code touched, no commits yet.

**Corrections applied to the approved spec during planning** — both were factual errors found by the construction-seam
explorations, corrected in `spec.md` rather than planned around:

1. **`btree_gist` needs no superuser.** It ships `trusted = true` in the bundled PG 16; the Local installer's
   `clinic_user` owns the database and Cloud runs Postgres in-stack as the bootstrap superuser. There is no
   managed-Postgres deployment in this repo. AC-P1.15 stands as written; the residual is that the GiST build takes
   `ACCESS EXCLUSIVE` while Kestrel is already serving in Local.
2. **AC-P4.37 was a no-op.** All 29 decimal properties carry an explicit `HasColumnType`, which bypasses
   facet-derived store types — the convention alone would emit zero `AlterColumn`s and leave `StockItem.UnitPrice` at
   2 decimals. Now specified as convention **plus** 26 deletions across 18 files, with a model test as the guard.

**Three guard tests do not guard what their names imply** (plan **R-9**, **R-10**, **R-14**) —
`RealtimeResourceResolverTests` is a hardcoded list so a new feature area broadcasts silently until P4 rewrites it;
`AdminSurfaceCoverageTests` is a hardcoded array so a new catalog controller is silently ungated;
`MoneyReadConsistencyTests.Wire()` hand-mirrors repository SQL, so an unmirrored filter change gives a green build
against the old rule.
