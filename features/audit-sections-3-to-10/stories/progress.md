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
| **P1** Appointment lifecycle & booking | 3.1, 3.2, 3.4, 5.4, 6.1, 6.9, 8.1, 8.4 | — | **complete** (`02dcc17`, `a813268`, `767ecae`, `1574c2e`, `05183d2`, `6906f83`). Both migrations landed in `6906f83` — the exclusion constraint (+ pre-flight) and the `Type:`-prefix backfill; the third turned out not to be needed. Only deferral left: the calendar row-trim (AC-P1.33, see below) |
| **P2** Finish what's built | 5.1–5.3, 5.5–5.9, 5.11, 5.12, 6.10, 6.11, 8.3 | — | **complete** — all 13 steps, AC-P2.1–2.45 |
| **P3** UX, accessibility & French | 3.3, 6.3, 7.1–7.9, 8.2, 8.6 | — | **complete** — all 13 steps, AC-P3.1–3.54. The manual walk (AC-P3.48) was done **statically**, not in a browser — stated plainly below |
| **P4** Stock, realtime & schema | 6.6, 6.7, 6.12, 9.1–9.6 | — | **not-started.** Its gate — the `verify-schema` verb — was built first and landed separately (`5586295`+1); see the session log. Design pre-work for steps 1–4 is recorded below so the next session does not re-derive it |
| **P5** Build & tooling | 10.1–10.5 | warnings step **last in story** | not-started |
| **P6** Money truth & timezone | 4.1, 4.2, 5.10, 6.2, 6.8, 8.5, 9.7 | — | not-started |
| **P7** Audit trail, duplicate prevention, anonymize | 6.4 | P7a first | not-started |
| **P8** CNAM claims & reconciliation | 6.5 | **Q-1…Q-6 unanswered** | blocked |

**Bullets closed: 34 / 57 · ACs: ~180 / 301** — all of P1 (§§ 3.1, 3.2, 3.4, 5.4, 6.1, 6.9, 8.1, 8.4), all of P2
(§§ 5.1–5.3, 5.5–5.9, 5.11, 5.12, 6.10, 6.11, 8.3) and all of P3 (§§ 3.3, 6.3, 7.1–7.9, 8.2, 8.6), plus adjacent
defects **A-1 … A-14**.

## Working tree note (start of session, 2026-07-28)

Untracked/modified paths present at the start that are **not** part of this story's code changes. Files are staged
explicitly by path — never `git add -A` / `git add .`:

| Path | Note |
|---|---|
| `features/audit-sections-3-to-10/` | This feature's own artifacts (spec/plan/design/exploration/mockups/stories). Committed as documentation, separately from code. |
| `follow-up/patient-merge.md`, `follow-up/mockups/`, `follow-up/README.md` | The dropped patient-merge design, moved here deliberately. Belongs to this feature's scope decision; commit with the artifacts. |

> Note for future sessions: an earlier merge into `feature/windows-desktop-app` used `git add -A` and swept this
> feature's `exploration.md` into an unrelated commit (`b0c472e "claude files"`). Stage by path.

## ⚠️ BLOCKER — Smart App Control (2026-07-28) · **not reproducing as of the P3 session**

> **Update, P3 session.** The full suite ran end-to-end with **no** filtering: `dotnet build -p:OutDir=<scratch>/`
> then `dotnet vstest <scratch>/ClinicManagement.UnitTests.dll` → **1209 passed / 0 failed**, including every
> `UnitTests.Infrastructure.*` class that used to fail at load. SAC's verdict really is time-varying, so the
> workaround below is still the documented path and the 279-blocked figure is **historical**, not current. Do not
> attribute a red run to SAC without re-running after a `bin`/`obj` clean + `dotnet build-server shutdown`.

## The original diagnosis (kept for the record)

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
| 8 | **`verify-schema` console verb** is the only gate for schema-level changes | plan Testing Strategy — **built 2026-07-28**, see the session log; it was scoped to P1 and silently never landed |
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

### DEV-2: a failed **recall** deep-links to the relance list, not to the patient — **taken, not asked**
**Date:** 2026-07-28 · **Story:** 1 (P3, step 2) · **Category:** Technical
**Original Plan:** AC-P3.7 — the staff notification for a failed reminder "deep-links to the patient or appointment".
**Actual Implementation:** the appointment case deep-links to the appointment as written (`StaffNotification`
already has `AppointmentId`). The **recall** case gets a new `NotificationTargetKind.Recall` that carries no id and
opens `/recalls`.
**Why not the patient:** `StaffNotification` has `ClinicId`, `AppointmentId` and `StockItemId` — **no `PatientId`**.
Deep-linking to the patient means a new nullable column, i.e. a migration. P3's own gate in the plan is
`tsc --noEmit` + `npm run build` + the walk, with **no** `verify-schema` and no migration — decision #8 makes
`verify-schema` the only gate for a schema change, so adding one here would put P3 outside the part boundary the
plan drew for it, on the part with the flakiest tooling (`migrations add` is intermittently SAC-blocked).
**Why `/recalls` is not a downgrade:** the action a failed recall demands is *re-contacting the patient*, and
AC-P3.5 has just put that patient back on the relance list. The list is where the operator acts; the patient page
is not. The notification text names the patient, so the row is still self-explanatory.
**Impact:** none on P4–P8. If a later part adds `StaffNotification.PatientId` for its own reasons, this target can
be narrowed to the patient page in one line.
**Approved:** taken under the "surface a question only if proceeding would be unsafe or useless if wrong"
instruction. Neither applies — this is a defensible reading of the AC that keeps P3 inside its stated gate.

## Auto-Approved Deviations

| Deviation | Classification | Reason |
|-----------|----------------|--------|
| `ScheduleRecallAsync` gates on **sendable** channels, not merely *enabled* ones | Trivial (tightens the same check the AC names) | AC-P3.2 is « no channel is configured ». `EnabledChannels` answers a different question: a channel toggled on with no credentials is "enabled", and its row stays `Pending` for ever at dispatch (`NotConfigured` is deliberately not a failure). Enqueuing on it would leave the patient snoozed 30 days behind a row that can never resolve — the exact defect one step later. Now filtered through the senders' own `SmsConfigured`/`WhatsAppConfigured`, so enqueue, dispatch and the admin badge cannot disagree. |
| `formatFileSize` extracted to `lib/format.ts` rather than fixed twice | Trivial (internal, no API change) | AC-P3.51 needs « o / Ko / Mo » in the files manager *and* the patient page, which each carried a byte-identical English copy. Fixing both in place is how they drifted from the French UI in the first place. |
| `procedure-types-table.tsx`'s `toFixed(2)` → `formatDT` | Trivial (one expression) | § 6.8 is P6's, but `toFixed(2)` **drops the millime** on a Tunisian amount and sits on the same line as the `DollarSign` icon this sweep replaces. Touching that line twice, once per part, is worse than fixing it once. |
| `MicOff` → `WifiOff` on the AI panel's offline banner | Trivial (one icon) | The banner is about connectivity and sat next to the real mic button, so a mic-off icon read as a microphone fault. Found while adding the speech toggle (AC-P3.25). |
| `X` → `Square` for « Arrêter la lecture » | Trivial (one icon) | `MicOff` there meant "stop speaking" while the same icon on the mic button meant "stop listening". With a real speech toggle beside it (AC-P3.25) the collision became unreadable. |
| `aria-pressed` + `aria-label` on the `tooth-multiselect` grid | Trivial (attributes only) | AC-P3.42/3.43. The tooth number was the button's accessible name, but nothing said whether it was *selected* — the grid was unusable without seeing the fill colour. |
| The invoice-cancel « Confirmer » button now requires a non-empty motif | Trivial (mirrors the existing server rule) | The handler already refuses a blank motif and the dialog already says « Un motif est requis ». While adding AC-P3.41's `<Label htmlFor>` it was cheaper to disable the button than to leave the only way to discover the rule be a 400. |
| Icon-only controls given `aria-label` in `ai-chat`, `appointment-calendar` (prev/next), `clinic-settings` (remove doctor), `document-editor-content` (remove bulletin act), and the patient page's file actions | Trivial (attributes only) | AC-P3.43's bar applied to the surfaces a brace-aware scan found unnamed, not only the ones § 7.8 listed. The scan is recorded under Learnings — a naive regex over `<Button …>` reports ~30 false positives because `=>` inside `onClick` ends the match early. |
| `User.Update(role, email?, fullName?)` **deleted** rather than fixed in place | Trivial (internal, zero callers) | AC-P2.25 requires that a role change not null email/fullName. The method had **no caller anywhere** and was the exact trap the AC names, so it was replaced by the validated `ChangeRole`. Leaving a dead, wiping overload beside the safe one invites the next caller into the same defect. |
| A-8's English + `ex.Message` leak fixed in **`SetUserActiveCommand`** and **`ResetUserPasswordCommand`** too | Trivial (same defect class, no API change) | Step 7 builds its sibling command on `SetUserActiveCommand`'s guards, and the plan's P2 "Done when" is *no `ex.Message` in any handler this part touched*. Fixing one of a pair and leaving the other is the drift the sweep exists to prevent. Same fix in `UpdateLabWorkOrderStatusCommand` (step 12), whose generic catch was the only thing that could see an illegal-transition message. |
| `document-editor-content.tsx`'s `colorHex: "#3b82f6"` changed to a palette colour | Trivial (one literal, no API change) | Found while wiring AC-P2.36. `#3b82f6` is **not** on the `ColorHex` curated palette, so that create-procedure call threw `ArgumentException` every time it ran. Exactly the drift A-14 is about; leaving it would have been shipping a known crash next to its own fix. |
| `CUSTOM_PROCEDURE_COLORS` (`create-appointment-dialog.tsx`) left hardcoded | Trivial (comment only) | AC-P2.37 names *the* hardcoded array — the picker in `procedure-type-form-modal.tsx`, now server-fed. This second copy is a rotation **seed** for an on-the-fly procedure, all of whose values are on the palette, and fetching it would add a round-trip to the booking dialog for a colour the dentist never sees. Comment now points at the authority. |
| `mode` destructure removed from `dashboard-sidebar.tsx` | Trivial (internal, same file) | Step 8 deleted its only use; leaving the binding reads as a mode gate that no longer exists. |

## Test Regressions

**P1** — `PostVisitReviewCompletionTests` was rewritten per AC-P1.13 (it pinned the silent-no-op contract AC-P1.12
supersedes). Intentional behaviour change, not a defect.

**P3** — three fixtures had to be updated for the recall behaviour change. All three are fixtures drifting behind a
shipped AC, not implementation bugs; each is named in the P3 session log's closing paragraph. The suite is
**1209 / 0 failed** after them.

## Learnings

### A step that is a *gate* for later parts must be verified as delivered, not inferred from the part's status
`verify-schema` was a P1 step. P1's commit declared the part COMPLETE, its progress row listed only the calendar
row-trim as outstanding, and P2 and P3 both wrote « `verify-schema`: not applicable — this part adds no
migration » in their gate tables. Three sessions in a row therefore *referenced* the verb without anyone noticing
it had never been written — and the one part that needs it (P4, eleven migrations) is the one where its absence
does real damage.
**Recommendation:** when a plan names a tool as the gate for a class of change, the session that lands the tool
must prove it *runs* — invoke it and paste the output into `progress.md`. And when a later part records a gate as
"not applicable", that is the moment to confirm the gate exists at all: "not applicable" and "not implemented"
look identical in a table and mean completely different things.

### A verification tool must derive its expectations from the model, or it rots exactly like the tests it replaces
My first `verify-schema` draft hardcoded the indexes and FKs to check — including P4's, which do not exist yet, so
it would have reported drift for unbuilt work *and* silently stopped growing the moment someone added an index
without editing it. That is the same defect this feature's plan already flags three times (**R-9**, **R-13**,
**R-14**: a "contract" test with a hand-maintained list that can never fail on a new case). Rewriting it to read
the EF model and diff against PostgreSQL's catalog made it shorter, self-maintaining, and immediately capable of
finding a real defect nobody had listed (`StockItems.UnitPrice`).
**Recommendation:** before writing any assertion over a schema, a route table, or a key set, ask what the
authoritative source already is and read *that*. Reserve the hand-written list for the things the source genuinely
cannot express — and comment why each one is there.

### A gate that cries wolf gets switched off — so a false positive is a defect in the gate, not noise
`verify-schema`'s first live run reported 7 missing foreign keys that PostgreSQL cannot have: the identity links
of owned types and table splitting (`Patients(Id) -> Patients`). Shipping that would have made the operator's
first experience of the tool "it reports seven problems that aren't real", after which its exit code means
nothing. The fix had to be **narrow** — `fk.IsOwnership` plus a same-table/own-primary-key test — because the
lazy version ("ignore self-references") would also have stopped verifying
`PatientFolder.ParentFolderId`.
**Recommendation:** run a new gate against real data before committing it, and treat every false positive as
blocking. Then check the fix does not widen into the real cases: an exclusion that silences a true positive is
worse than the false one it removed.

### "Learn the outcome" and "undo the wrong outcome" are one fix — shipping only the first moves the defect
§ 3.3 reads as one bug ("the toast lies"), and AC-P3.1 alone would silence it for the clinic with no channel
configured. But a clinic *with* a channel and a dead gateway still snoozed the patient 30 days on a message that
never arrived — identical silence, one step later, and now harder to find because the UI's story is consistent.
The spec caught this itself (AC-P3.5's « Enqueuing is not sending »), which is why it lands as one step.
**Recommendation:** when an AC is "report the real outcome", ask what the *reported* outcome is a proxy for. If the
report happens before the thing it describes (enqueue vs. send, accept vs. settle, submit vs. validate), the fix
needs a second half at the point the truth arrives, or you have only moved the lie downstream.

### A "partial success" needs a named resolution rule before the code, or the first branch you write becomes it
Two channels, one fails: un-snooze, or not? Both are defensible, and whichever the first `if` happens to express
becomes the product's rule silently. AC-P3.6 forced it to be stated — *left on the list unless at least one
channel actually succeeded* — and that turned out to need a **third** state the AC does not name: a sibling still
`Pending` has not resolved, so the decision must be *deferred*, not made. Without that, the first channel to fail
un-snoozes a patient whose second message is still on its way.
**Recommendation:** for any fan-out with per-branch outcomes, enumerate all-succeeded / all-failed / mixed /
**not-yet-decided** before writing the handler, and put the rule in the code at the decision point.

### A regex over `<Button …>` cannot find unlabelled icon buttons — `=>` inside `onClick` ends the match
The obvious audit for AC-P3.43 is "find `<Button>` tags that look icon-only and carry no `aria-label`/`title`".
A `[^>]*?>` regex reported **30** hits; a brace-aware scan (tracking `{}` and quotes to find the tag's real `>`)
reported **8**, and the other 22 were tags whose `onClick={(e) => …}` arrow truncated the match before the
attributes. Acting on the 30 would have meant re-adding labels that were already there and, worse, trusting the
same broken scan to certify "0 remaining".
**Recommendation:** any JSX-attribute audit needs a brace-aware tag scanner, not a regex. Ten lines, and it turns
an unusable result into a defensible one — the "0 remaining" claim in this session's walk rests on it.

### Fixing a string sweep with a broad `str.replace` corrupted the file — exactly as P1 warned
Converting `clinic-settings.tsx`'s bespoke banner to `sonner` meant rewriting ten
`setNotification({ type, message: … })` calls. A trailing ` })\n` → `)\n` pass to close the shortened calls also
hit `setOriginalClinicData({ … })`, `clinicsApi.update({ … })` and `useClinicRealtime(…)` — five unrelated call
sites left syntactically broken. Reverted and redone as ten fully-anchored replacements with a
`count == 1` assertion each. This is the **second** session to lose time to exactly this
(see P1's process note), which is why it is a Learning now rather than a note.
**Recommendation:** never write a replacement whose left-hand side is shorter than the construct it belongs to.
Anchor on the whole statement, assert the occurrence count, and prefer `Edit` — it fails loudly instead of
half-applying.

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

### 2026-07-28 — **`verify-schema` built** (P4's gate; a carried-forward P1 step)

**This session was scoped to P4 and deliberately re-scoped.** P4's own "Done when" requires
`verify-schema`, and decision #8 makes it the **only** gate for a schema-level change — but the verb **did not
exist**. It was listed in P1's remaining steps, P1's final commit declared the part COMPLETE without it, and the
P1 row said only the calendar row-trim was outstanding. It had silently fallen off. P4 lands **11 migrations**, so
building its gate first was confirmed with the user rather than assumed.

| Delivered | Where |
|---|---|
| `ISchemaVerificationReader` + the fact records (`SchemaFacts`, `SchemaSide`, `IndexFact`, `ForeignKeyFact`, `DecimalColumnFact`, `MappedDecimalFact`, `TableConstraintFact`, `DataMigrationCounts`) | `Application/Common/Interfaces/` |
| `SchemaVerificationService` — the assertions, unit-testable against a mocked reader | `Application/Common/Maintenance/` |
| `SchemaVerificationReader` — the EF-model side + the PostgreSQL catalog side | `Infrastructure/Persistence/` |
| `VerifySchemaCommand` + the `Program.cs` verb interception | `API/Maintenance/`, `API/Program.cs` |
| 24 service tests + 4 wrapper tests | `UnitTests/Common/Maintenance/`, `UnitTests/Api/Maintenance/` |

**Three design points that matter.**

1. **Model-driven, not a hand-maintained expectation list.** My first draft hardcoded the indexes and FKs to
   check. That is *precisely* the shape of bug this feature's own plan flags three times (**R-9** the realtime
   "contract" test that never fails on a new area, **R-13** `ConcurrencyConflictTests`' hardcoded DTO lists,
   **R-14** `AdminSurfaceCoverageTests`' hardcoded array) — and it would have rotted identically: P4 adds five
   indexes and two FKs, and nothing would have forced the list to grow. Rewritten to read the expected indexes,
   FKs and decimal precisions **from the EF model** and diff them against `pg_index`/`pg_constraint`/
   `information_schema`. A schema object added in a configuration file is now verified for free. Only what the
   model *cannot* express is named in the service: `btree_gist`, the exclusion constraint's partiality, the two
   rate columns, and the data-migration row counts.
2. **Indexes are matched on table + ordered columns, never on name.** EF's generated name and a hand-written
   migration's name legitimately differ (P1's exclusion constraint is hand-written), and it is the covered
   columns that decide whether a query is actually served. Name-matching would have reported every hand-written
   index as missing. Column *order* is part of the identity — `(A, B)` does not serve what `(B, A)` does.
3. **An unbuilt part reports « not applicable », not drift.** The backfill counts are nullable and each is guarded
   on its table/column existing. Two reasons: work that has not been implemented is not a regression, and a gate
   that exits non-zero for unbuilt parts trains the operator to ignore its exit code — the one thing a gate must
   never do. Reporting `0` instead would be worse still: it would claim a backfill succeeded when it never ran.

**It found a real defect on its first run.** 167 checks ok, **2 drift** — and both are the same true positive:
`StockItems.UnitPrice` is `(18,2)` on the database **and** `numeric(18,2)` in the model. That is exactly § 9.5 /
AC-P4.36, which P4 step 10 fixes. The verb currently exits **2**, correctly, and that output is the "before"
baseline P4 diffs against.

**A false-positive class found and closed while validating it.** The first live run reported 7 missing foreign
keys — `Patients(Id) -> Patients` ×6 and `ProcedureTypes(Id) -> ProcedureTypes`. Those are the identity links of
**owned types and table splitting** (`Patient.Address`/`InsuranceInfo`/`CnamInfo` live in the `Patients` row);
PostgreSQL has no such constraint. Filtered on `fk.IsOwnership` **plus** a same-table/own-primary-key check, so a
genuine self-reference like `PatientFolder.ParentFolderId` is still verified. Worth recording because a gate that
cries wolf 7 times gets ignored — and the fix had to be *narrow*, not "skip self-references".

| Gate | Result |
|---|---|
| Backend build (`--no-incremental`, full solution) | **0 errors**, 56 warnings (baseline) — **0 in any file this session created** (warning set grepped for both new type names) |
| **Full unit suite** | **1237 passed, 0 failed** (+28 on the 1209 baseline). SAC did not block |
| `verify-schema` (live DB) | runs; **167 ok / 2 drift**, exit **2** — the 2 are the genuine § 9.5 defect. Report saved for the P4 before/after diff |
| `reconcile-money` (live DB) | exit **0**, output **byte-identical** to the pre-session run — this session touched no money path |
| Frontend | **not touched** this session; no `tsc`/`build` run needed |

**Not started: P4 itself.** Steps 1–11 and the 11 migrations are next session's work, now against a working gate.

#### P4 design pre-work (explored, then reverted — do not re-derive)

I built and then reverted P4's stock domain model to keep this commit clean and green (the tree was red once
`StockItemDto` lost `ExpiryDate`). The code is saved at
`<scratchpad>/p4-prework/` (`StockBatch.cs`, `ProcedureTypeMaterial.cs`, `StockItem.cs`, both configurations, and
`p4-model-edits.patch` for the five edited files). **Three findings are the valuable part:**

1. **The material list can only hang off `ProcedureType`, not `DentalActCode`.** AC-P4.9 says "ProcedureType
   and/or DentalActCode". Only one is reachable from a saved fiche: `DentalRecordAct` carries a nullable
   `ProcedureTypeId` and **no** `DentalActCodeId`. A list on `DentalActCode` could never be consumed on fiche save
   (AC-P4.10) — it would be a second finished-but-uncallable capability, the exact class P2 existed to remove.
2. **`SetCurrentStock` must return the signed delta.** AC-P4.15 needs a `StockMovement` whenever `CurrentStock`
   changes, and the method returned `void` — which is *why* its only caller wrote no movement and Σ movements
   stopped reconciling. It also has to reconcile the batch rows to the new total, or a stock-take leaves the lots
   and the on-hand total disagreeing.
3. **`AddStock` must return its batch, and the backfill needs a separate door.** The opening batch the migration
   creates is *already* counted in `CurrentStock`, so it cannot go through `AddStock` (which increments). Hence
   `AttachExistingBatch`, which deliberately does not touch the total.

### 2026-07-28 — **P3 complete**: UX, accessibility & French

Closes **AC-P3.1–3.54** (§§ 3.3, 6.3, 7.1–7.9, 8.2, 8.6). All thirteen steps.

| Step | Delivered | Where |
|---|---|---|
| 1 | **Recall truth.** `IReminderScheduler.ScheduleRecallAsync` now returns a `RecallDispatchOutcome`; `SendRecallCommand` refuses (French, naming the settings + « Marquer comme contacté ») and leaves the patient untouched unless a row was really queued. **And the part that matters** — the dispatcher undoes the snooze once *every* channel of that send has failed (`Patient.ClearRecallSnooze`), so a partial send resolves to a stated state | `RecallDispatchOutcome.cs`, `ReminderScheduler.cs`, `SendRecallCommand.cs`, `Patient.cs`, `NotificationJob.cs`, `recalls/page.tsx` |
| 2 | **Failed reminders visible.** `INotificationGenerator.ReminderDeliveryFailedAsync` + `NotificationCategory.ReminderFailed` + `NotificationTargetKind.Recall`; all-staff (no actor exclusion) so the secretary who books sees it; `ReminderStatusDto` gains `patientName`/`appointmentAt`/`isRecall` (phone stays masked) | `INotificationGenerator.cs`, `NotificationGenerator.cs`, `ReminderStatusDto.cs`, `GetClinicReminderStatusQuery.cs`, `reminder-settings.tsx`, `notification-panel.tsx` |
| 3 | **Mobile shell.** `sheet` + `radio-group` added (zero new deps); rail → drawer below `md:`, closed by default, closes on navigation/Escape/overlay; header reflows; AI panel and the editor's 420 px column go viewport-relative; page gutters `p-4 md:p-6` across 16 pages | `ui/sheet.tsx`, `sidebar-context.tsx`, `dashboard-sidebar.tsx`, `dashboard-header.tsx`, `ai-chat.tsx`, `document-editor-content.tsx`, 16 × `page.tsx` |
| 4 | **ClinicGuard 404.** Redirect target derived from `useSession().mode` (`/login` in Local, `/auth/login` in Cloud); `returnTo` filtered so it can never point at `/auth/*` or `/bff/*` | `clinic-guard.tsx` |
| 5 | **Patients list edit action** — the missing `setSelectedPatient`/`setEditDialogOpen` callers, and the row is replaced from the dialog's returned DTO (no reload) | `patients-table.tsx` |
| 6 | **AI speech off by default** + a persistent `localStorage` toggle with `aria-pressed`; turning it off silences the reply already playing | `ai-chat.tsx` |
| 7 | **The five swallows + the sixth.** Patient-files error state, the factures KPIs' three distinct states, download + preview toasts, and the procedure-type load failure in **both** appointment dialogs (the edit one had no comment at all — C-4) | `files/page.tsx`, `factures/page.tsx`, `patients/[id]/page.tsx`, `create-`/`edit-appointment-dialog.tsx` |
| 8 | **In-flight + feedback.** « Créer le dossier » guards the *handler* (the Enter key calls the same function), dialog stays open with the typed name on failure. P1/P2's surfaces audited — all already carried in-flight state | `patient-files-manager.tsx` (+ audit) |
| 9 | **`/patients` skeleton** matching the table's six columns, per the `stats-card.tsx` precedent | `patients-table.tsx` |
| 10 | **Accessibility.** Keyboard-operable `Card`s in /documents + the files manager; `aria-label` on the icon-only delete and on every unnamed icon control found by a brace-aware scan; a real `<Label htmlFor>` on the invoice motif; a `:focus-visible` ring floor in `globals.css`; `role="status"` on each new async result; **`confirm-by-typing-dialog.tsx`** for AC-P3.47 so P7/P8 inherit one implementation | `documents/page.tsx`, `patient-files-manager.tsx`, `invoices-table.tsx`, `globals.css`, `ui/confirm-by-typing-dialog.tsx` |
| 11 | **`clinic-settings.tsx` on `sonner`** — the bespoke `fixed top-4 right-4` banner, its 4 s timer, its state and its three now-unused icons deleted | `clinic-settings.tsx` |
| 12 | **French.** The audit's list + the seven it missed + « o / Ko / Mo » (`formatFileSize`, shared) + Tunisian placeholders incl. the one at `:764` and the inline sub-form + « Push » → « Envoyer » + « Phone Number ID » decided **explicitly** with a French gloss | 9 files, `lib/format.ts` |
| 13 | **The walk** — recorded below, and honestly labelled | this file |

**Three load-bearing design points.**

1. **Fixing only the command would have moved the defect, not removed it.** AC-P3.1 (learn the enqueue's outcome)
   and AC-P3.5 (undo the snooze when the dispatch fails) are one feature. With only the first, a clinic *with* a
   channel configured but a dead gateway still snoozed the patient 30 days on a message that never arrived — the
   same silence, one step later. The batch check (`GetRecallBatchAsync` on `PatientId` + null `AppointmentId` +
   the shared `ScheduledFor`) is what makes a partial send a **stated** state: a sibling still `Pending` defers to
   a later tick, a sibling `Sent` means the patient really was reached, so the snooze stands.
2. **The cancelled-appointment void is deliberately NOT surfaced.** It is the only `Failed` row this part leaves
   silent. That row failing is the *correct* suppression of a reminder for a visit that is not happening, and
   `AppointmentCancelledAsync` has already told the staff — a second « Rappel non envoyé » beside it is exactly
   the noise that makes a feed stop being read. Stated in the code at `SurfaceFailureAsync`.
3. **The mobile drawer must not write the desktop preference.** `sidebar-context` persists `isCollapsed` only;
   `isMobileOpen` is per-visit. Sharing one persisted flag is the obvious implementation and would have let a
   phone session silently expand a rail the user had collapsed on desktop (AC-P3.18). One `renderItem(item,
   collapsed)` serves both, and the drawer always passes `false` — a phone has no hover for the collapsed rail's
   tooltips.

#### The manual walk (AC-P3.48) — **static, not in a browser**

⚠️ **Stated plainly, per plan risk R-9.** This was a **markup-level** pass: every screen below was read and its
responsive classes, tab order, accessible names and live regions checked against the AC bar. It was **not** driven
in a real browser at 375 px, and it is **not** equivalent to one. There is no frontend test runner, so nothing here
is automated either. **A human pass at 375 px and by keyboard is still owed** before the feature review.

| Screen | Verified statically | Residual |
|---|---|---|
| Every page shell (28 routes) | Rail hidden `md:`-down; drawer opens from the header only; `flex-1 … overflow-hidden` on every main column (the patient-files page was the one missing it — fixed) | — |
| `/` dashboard, `/patients`, `/stock`, `/factures`, `/creances`, `/caisse`, `/lab-orders`, `/waiting-list`, `/recalls`, `/recurring-series`, `/treatment-plans` | `p-4 md:p-6`; every `<Table>` is wrapped by `ui/table.tsx`'s own `overflow-x-auto`, so the **body** never scrolls horizontally | Tables are *cramped* at 375 px — a real responsive pass over wide tables is explicitly Out of Scope |
| `/appointments` | Toolbar already `flex-wrap`; the grid is `grid-cols-7` with `overflow-x-hidden`, so it squeezes rather than overflowing | Week view at 375 px is legible but tight — calendar responsiveness is Out of Scope |
| `/documents` | Cards `role="button"` + `tabIndex={0}` + Enter/Space + focus ring | — |
| `/documents/[type]` | Columns stack below `md:`; one scroll container stacked, two side-by-side | — |
| `/patients/[id]`, `/patients/[id]/files` | Error state replaces the manager; file actions named; « Retour au patient » French | — |
| `/settings` (clinic + reminders + backup) | `sonner` only; every `Label` has `htmlFor`; « Phone Number ID » glossed | — |
| AI assistant | Viewport-relative below `md:`; speech off by default; toggle + stop both named | — |
| New/changed dialogs (folder, invoice-cancel, appointment ×2, patient) | In-flight disabled, single effect, French labels, real `<Label>`s | — |
| Keyboard sweep | Brace-aware scan over every `.tsx`: **0** icon-only `<Button>`/`<button>` left without `aria-label` or `title` outside the vendored `ui/calendar.tsx` (react-day-picker supplies its own) | — |

#### Repo-wide English sweep (AC-P3.53) — result recorded

Ran three passes over `app/` + `components/`: JSX text nodes, `placeholder="…"`/`title="…"` attributes, and
bare English lines. **Closed as a class**, not as a list of nine files.

| Found | Resolution |
|---|---|
| `clinic-settings.tsx` — « Loading clinic settings… », « Share with coworkers… », 3 × « Edit », 3 × « Cancel », « Clinic Name », « Full Address », « Phone Number » | French |
| `procedure-types-table.tsx` — « Loading procedure types… », « Procedure Types » | French |
| `files/page.tsx` — « Back to Patient » | « Retour au patient » |
| `appointment-calendar.tsx` — « Push » | « Envoyer » (+ a full sentence in `title`/`aria-label`) |
| `patient-files-manager.tsx` — `"file" / "files"` | « fichier / fichiers » |
| `create-appointment-dialog.tsx` — « Cancel », « Duration set to … minutes (you can change it) » | French |
| `edit-appointment-dialog.tsx` — « Cancel Appointment », « Close » | French |
| `procedure-type-form-modal.tsx` — `placeholder="e.g., 70.00"` | « Ex. 70,000 » |
| `edit-patient-dialog.tsx` + `create-appointment-dialog.tsx` — 11 American placeholders | Tunisian |
| **Kept English, deliberately** — `reminder-settings.tsx` « Phone Number ID » | AC-P3.52: it is the verbatim field name in Meta's dashboard, which the operator copies from. A French gloss sits beside it |
| **Not user-visible, left alone** — `DOCTOR_SPECIALTIES` keys, weekday keys, `PaymentMethod`/status enum keys | These are storage keys with French display maps (`lib/specialties.ts` et al.). Renaming them orphans existing rows — the convention is now recorded in `web/CLAUDE.md` |

| Gate | Result |
|---|---|
| Backend build (`--no-incremental`, full solution) | **0 errors**, 56 warnings — baseline unchanged, **0 in any file this part created or edited** (warning set grouped by filename and diffed) |
| **Full unit suite** (`dotnet build -p:OutDir=…` + `dotnet vstest`) | **1209 passed, 0 failed.** SAC let the whole suite through this session — no filtering needed, nothing blocked |
| New tests (+11) | `RecallDeliveryTruthTests` (**9** — the three no-send outcomes as a `[Theory]`, the refusal's wording, the happy path, all-channels-failed → un-snoozed + « à recontacter », a partial send keeping the snooze, a still-`Pending` sibling deferring, and a throwing generator not breaking the dispatch) + 2 on `ReminderSchedulerTests` (no-channel vs. enqueued outcomes, one `ScheduledFor` per batch) |
| `npx tsc --noEmit` | 0 errors |
| `npm run build` | clean, **27/27** static pages |
| `npm run lint` | ⛔ **cannot run** — ESLint is not installed; that is P5 step 1. Unused-import check done with an ad-hoc scan instead (0 introduced) |
| `verify-schema` | **not applicable** — P3 adds no migration. `NotificationCategory.ReminderFailed` and `NotificationTargetKind.Recall` are new members of `int`-converted enums, which is not a schema change (see DEV-2 for why no column was added) |

**Test-fixture updates forced by the behaviour change** (not defects): `PatientContactOptionalTests`'
« recall still works » test now has to stub `ScheduleRecallAsync` → `Enqueued`, because an unstubbed Moq outcome
is *not* `Enqueued` and the handler correctly refuses — which is AC-P3.2 working. `NotificationJobTests`' four
`new NotificationJob(...)` sites gained the `INotificationGenerator` argument. `ReminderSchedulerTests`' harness
now supplies channel credentials, since the recall path enqueues only on a **sendable** channel.

### 2026-07-28 — P1 finished except migrations (commit `05183d2`)

Closes **AC-P1.10–1.11, 1.18, 1.25–1.26, 1.29, 1.33, 1.52, 1.54**.

| Delivered | Note |
|---|---|
| `23P01` → a French 409 with its **own** message | Below the concurrency arm (that type derives from `DbUpdateException`); matched on type *name*, not a cast, per `StartupDiagnostics` |
| AC-P1.29 rule for `GoogleCalendarSyncService` | **import-with-override-flag, never skip** — refusing would silently drop the event into a log-only catch |
| AC-P1.10/1.11 stated at the behaviour | Act returns to « À planifier »; `TreatmentPlanItem.Status` untouched; patient may reappear on relance |
| `doctor-working-hours-card.tsx` (§ 5.4) | The missing caller — `getWorkingHours`/`setWorkingHours` had zero callers |
| Calendar shading per-day from real hours + legend | Was `hour >= 8 && hour < 18`, day-blind |
| a11y on the clinic-wide hours rows; « to »/« Add Doctor » → French | AC-P1.52 already held in both dialogs (verified) |
| `CLAUDE.md` × 3 updated for the new types | Domain / Application / components |

**Deliberately deferred, with the reason.** AC-P1.33 asks the grid to render *only* the resolved open hours.
The shading is now correct, but the **rows are still 0..23**: the appointment overlay positions blocks from
midnight against a fixed `HOUR_HEIGHT`, and the file carries a load-bearing comment about exactly the drift that
re-basing would risk. Trimming the rows needs that overlay reworked — a follow-up, not something to slip into a
sweep.

| Gate | Result |
|---|---|
| Backend `--no-incremental` | 0 errors, 56 warnings (baseline), 0 in changed files |
| Targeted suite (Appointment / TreatmentPlan / Recall / Concurrency) | **233 / 233 pass** |
| `tsc --noEmit` · `npm run build` | clean · clean, 27/27 |

**Process note for next time.** Several file edits in this session were batched as python heredocs to go faster;
that cost two rework cycles — a regex pass corrupted six test files (reverted via `git checkout` and redone by
hand), and a two-edit script whose second assert failed discarded its own successful first edit. Use `Edit`:
it verifies uniqueness and fails loudly instead of half-applying.

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
