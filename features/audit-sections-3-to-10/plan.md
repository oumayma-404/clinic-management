# Implementation Plan: Audit Sections 3–10

**Status:** APPROVED
**Approved:** 2026-07-28
**Challenged:** No — not through `/challenge-plan`. Informed by 4 construction-seam explorations, which overturned two
spec claims (see [Overview](#overview)).
**Created:** 2026-07-28
**Spec:** [spec.md](./spec.md) — APPROVED, Challenged: Yes (8-lens completeness pass)
**Design:** [design.md](./design.md) — APPROVED (2 novel screens; merge's design moved to `follow-up/`)
**Exploration:** [exploration.md](./exploration.md) + 4 further construction-seam explorations folded in below
**Branch:** `feature/audit-sections-3-to-10` (off `feature/windows-desktop-app` @ `1932acf`, which contains **both**
the merged audit § 1 and § 2 work)
**Structure:** **One story**, by explicit user decision — see [Story shape](#story-shape) and risk **R-1**.

---

## Overview

One story, **US-1**, covering all 57 audit bullets of §§ 3–10 plus the 26 adjacent defects. The user chose a single
story over a split; that decision is honored and recorded as **R-1** rather than re-litigated.

To keep it implementable, US-1 is structured as **eight ordered parts (P1–P8)**. Each part is a *vertical* increment
— domain → persistence → API → UI → tests — never a technical-layer grouping. Each ends at a clean build gate and is
a natural commit boundary, so `/implement-story` can land the story incrementally and resume at a part boundary.

### Approach decisions (settled, not open)

| Decision | Choice | Why |
|---|---|---|
| Story count | **One story, eight parts** | User decision, twice. Parts are the commit and resume boundary (**R-1**) |
| Double-booking | PostgreSQL **exclusion constraint**, partial on `Status NOT IN (Cancelled, NoShow)` | A check-then-insert cannot be made safe by widening the check. `btree_gist` is `trusted = true` in the bundled PG 16, so it installs in both modes with no superuser |
| Appointment type (§ 8.4) | Carried by **`ProcedureTypeId`**, the field that already exists | Avoids a new column. The migration must handle three row classes because the dialog writes *both* the prefix and `procedureTypeId` from independent state |
| Decimal precision | **Full normalization** — convention **plus** deleting 26 redundant `HasColumnType` calls | The convention alone is a **no-op**: explicit `HasColumnType` bypasses facet-derived store types, so the differ emits nothing and `StockItem.UnitPrice` stays at 2 decimals |
| Duplicate matching | **Persisted normalized column** + bounded in-memory distance check | This schema has **no expression/functional index anywhere** and EF cannot express `lower(...)`. `pg_trgm` means raw SQL with no precedent; in-memory-only means loading every patient, which is § 9.6 in this same spec |
| Audit-trail seam | **Inline per handler**, before each handler's own `SaveChangesAsync` | A `SaveChanges` hook cannot satisfy AC-P7.5: `NotificationGenerator` shares the scoped `DbContext` and flushes post-commit inside a swallowing catch, so it would flush staged audit rows outside the transaction |
| Audit-write failure | **The mutation fails** (AC-P7.6) | A clinical record that saved with no trail is what P7 exists to prevent |
| Schema verification | A read-only **`verify-schema` console verb** | Nothing in the test project touches a database — see [Testing Strategy](#testing-strategy) |
| CNAM reimbursement | Its **own entity**, not a `Payment` | Third-party money. `PaymentMethod` is untouched, so the caisse and dashboard reads are untouched |

### Four findings from exploration that change the work

1. **The precision convention is a no-op as the spec first wrote it.** All 29 decimal properties carry an explicit
   `HasColumnType`. Corrected in the spec (AC-P4.37–4.41); the real change is 26 deletions across 18 config files.
2. **`btree_gist` needs no superuser.** It ships `trusted = true`; the Local installer's `clinic_user` owns the
   database, and Cloud runs Postgres in-stack as the bootstrap superuser. There is no managed-Postgres path in this
   repo at all. The spec's earlier warning was wrong and is corrected.
3. **`RealtimeResourceResolverTests` does not fail on a new feature area.** It is a hardcoded `[InlineData]` list, so
   a new `Features/Audit/Commands` silently broadcasts `"audit"` with no frontend listener and nothing notices. This
   is exactly the defect § 9.1 describes, and AC-P4.23–4.25 replace the test with a reflective exact-set contract.
4. **`AdminSurfaceCoverageTests` is also a hardcoded array**, so a new catalog-like controller is silently uncovered.
   Any new admin surface must be added to `CatalogControllers` or `GatedActions()` by hand.

---

## Story shape

The user chose **one story**. This plan honors that and does not re-propose a split. The story is worked through in
**eight ordered, dependency-respecting parts**. Each is a vertical increment that ends in a working, committable
state. `/implement-story` should land and commit part by part; a part boundary is the natural split point if the
story proves too large in one session (risk **R-1**).

| Part | Covers | Bullets | Verifiable by | Depends on |
|---|---|:--:|---|---|
| **P1** Appointment lifecycle & booking | 3.1, 3.2, 3.4, 5.4, 6.1, 6.9, 8.1, 8.4 | 8 | `dotnet vstest` + `verify-schema` + page walk | — |
| **P2** Finish what's built | 5.1–5.3, 5.5–5.9, 5.11, 5.12, 6.10, 6.11, 8.3 | 13 | `dotnet vstest` + page walk | — |
| **P3** UX, accessibility & French | 3.3, 6.3, 7.1–7.9, 8.2, 8.6 | 13 | `tsc` + `npm run build` + manual walk | — |
| **P4** Stock, realtime & schema | 6.6, 6.7, 6.12, 9.1–9.6 | 9 | `dotnet vstest` + `verify-schema` | — |
| **P5** Build & tooling | 10.1–10.5 | 5 | `npm run lint` + build | last, see below |
| **P6** Money truth & timezone | 4.1, 4.2, 5.10, 6.2, 6.8, 8.5, 9.7 | 7 | `dotnet vstest` | — |
| **P7** Audit trail, duplicate prevention, anonymize | 6.4 | 1 | `dotnet vstest` + `verify-schema` | P7a first |
| **P8** CNAM claims & reconciliation | 6.5 | 1 | `dotnet vstest` + `verify-schema` | **Q-1…Q-6** |
| | **Total** | **57** | | |

**Ordering rules, load-bearing:**

- **P1 internal order is fixed:** the un-mark (from P2's US-P2a) → the soft-link cleanup → the delete-fiche button.
  Adding the delete button first is what makes § 6.11's orphaned links *reachable*. This crosses P1/P2, so
  **P2's US-P2a and US-P2b land together, un-mark before delete.**
- **P7a before the rest of P7 and all of P8.** The trail is what AC-P7.26, AC-P7.31 and AC-P8.27 record into.
- **P5's `TreatWarningsAsErrors` step is the very last thing in the whole story** — it can only flip once the count
  is zero, and every other part adds code. P5's other steps (lint, pinning, seeding retry) can land any time.
- **P8 cannot start** until Q-1…Q-6 are answered. Everything else is unblocked.

P1, P2, P3, P4, P6 are otherwise mutually independent and may be reordered.

---

## Conventions every part must follow

Extracted from the codebase; these are not negotiable choices, they are how this repo works.

### Adding an Application feature area — the full file set

Reference slice: `Features/Expenses` (the most recent complete clinic-scoped CRUD area).

| # | File | Note |
|---|---|---|
| 1 | `Domain/Entities/<X>.cs` | `AggregateRoot<Guid>`, private EF ctor, all `private set`, French invariant messages |
| 2 | `Domain/Repositories/I<X>Repository.cs` | `CancellationToken` last; mutations only **stage** |
| 3 | `Infrastructure/Persistence/Configurations/<X>Configuration.cs` | **Auto-discovered** by `ApplyConfigurationsFromAssembly` — no registration |
| 4 | `ApplicationDbContext` `DbSet<X>` | **Only if a repository queries it as a root.** Children reached via a parent navigation get none — that is why there is no `DbSet<Payment>`/`<InvoiceLine>` |
| 5 | `ApplicationDbContext` query-filter line | `HasQueryFilter(e => !IsClinicScoped \|\| e.ClinicId == ScopedClinicId)`. Children get **no** filter |
| 6 | `Infrastructure/Repositories/<X>Repository.cs` | **Copy `ExpenseRepository.UpdateAsync` verbatim** — see the hazard below |
| 7 | `Infrastructure/Extensions.cs` DI line | `services.AddScoped<I<X>Repository, <X>Repository>();` |
| 8 | Migration | See the migration rules below |
| 9 | `Features/<Area>/{Commands,Queries}/*.cs` | Request **and** handler in the same file. MediatR is **assembly-scanned** — never hand-registered |
| 10 | `Application/DTOs/<X>Dto.cs` | Hand-written `ToDto()` extension in the same file. **No AutoMapper anywhere** |
| 11 | `API/Controllers/<X>Controller.cs` | `ApiControllerBase`, inject only `IMediator`, `result.IsFailure ? HandleFailure(result) : Ok(result.Value)` |
| 12 | `web/lib/api/<x>.ts` + `web/lib/api/types.ts` | Plain thunk object over `apiGet/Post/Put/Delete`; **no try/catch**, errors propagate as `ApiError` |
| 13 | `web/lib/realtime/clinic-hub.ts` | A new area **must** add its key — the resolver derives it from the namespace automatically and nothing else will catch the omission |

> ⚠️ **The repository `UpdateAsync` hazard.** Copy this shape exactly, from `ExpenseRepository.cs:55-68`: attach
> **only** when the entity is `Detached`. Calling `Update()` on a tracked entity re-marks every property modified;
> calling it on a never-loaded detached one makes the `xmin` token read as `0`, producing `WHERE xmin = 0`, zero
> matched rows, and **a 409 for a conflict that never happened**.

### What § 1 landed that all new code must satisfy

- **`Entity<TId>.Version`** is mapped to PostgreSQL `xmin` by a reflection loop in `OnModelCreating`. A new entity
  deriving from `Entity<TId>`/`AggregateRoot<TId>` gets it free — but it **must be a real table**. Do not add an
  `Entity<>`-derived type mapped to a view or keyless entity; the loop would try to map `xmin` on it. Owned types,
  shared-CLR types and plain non-`Entity` classes are skipped automatically.
- **Every catch-all that returns a `Result` must carry `when (ex is not ConflictException)`.** 152 such catches exist
  today. A catch that only *logs* a best-effort post-commit side effect deliberately does **not** carry it.
- **Version round-trip**, for any aggregate a user holds open in a form: add `uint Version` to the read DTO *and* the
  update command, populate it in the mapper, call `_unitOfWork.SetExpectedVersion(entity, request.Version)` before
  save, mirror `version: number` in the TS type. Six DTOs do this today. **`ConcurrencyConflictTests` pins them via
  hardcoded `[InlineData]` lists — a new round-tripped DTO must be added there by hand.**
- `SetExpectedVersion` sets `OriginalValue`, not `CurrentValue`. `0` means "not supplied" and skips the check.

### Migration rules

```bash
dotnet ef migrations add <Name> --project api/ClinicManagement.Infrastructure --startup-project api/ClinicManagement.API
```

- **Stop the API first.** `migrations add` silently emits an **empty** migration when the API is running.
- **Read every generated file before committing** and delete any `AddColumn<uint>("xmin", …)` — § 1's differ emits
  38 of them and PostgreSQL rejects the column name outright.
- **Never pass `--no-build`.**
- Latest on this branch is `20260727195934_AddConcurrencyToken`; new migrations must sort after it.
- **Cloud applies migrations before Kestrel serves** (`Program.cs`, synchronous `Database.Migrate()`).
- **Local applies them *after* Kestrel is serving**, fire-and-forget in `DeferredStartupService`, and **any throw
  calls `StopApplication()`**. Therefore: *every data migration must be idempotent* (`WHERE NOT EXISTS`,
  `WHERE … IS NULL`), and destructive/lossy steps go **last** in the batch.

**Precedents to copy, all raw SQL inside `Up()`** — there is no write-side console-verb precedent in this repo:

| Need | Precedent |
|---|---|
| Raw DDL EF cannot express | `20260723120623_MergeSnapshotReconcile.cs:24-27` (interval→bigint with an explicit `USING` cast) |
| Data rewrite | `20260727194256_MakePatientContactOptional.cs:52-60` (`UPDATE … WHERE … IN (…)`) |
| Idempotent backfill | `20260727181433_AddInstallmentPaymentLedger.cs:57-89` (`INSERT … SELECT … WHERE NOT EXISTS`, `COALESCE` every NOT NULL target) |
| Filtered unique index | EF: `InvoiceConfiguration.cs:32-37` · migration: `20260717174602_AddInvoicesAndClinicBilling.cs:131-136` |
| Boolean-predicate filter | `PaymentConfiguration.cs:59-60` — `.HasFilter("NOT \"IsVoided\"")` |

**No precedent exists for:** exclusion constraints, check constraints, computed/generated columns, `CREATE EXTENSION`,
or expression/functional indexes. P1's exclusion constraint and P7's normalized-column index are the first of their
kind here and must be written as raw `Sql(...)` with a comment stating why EF cannot express them.

### Guard tests — what fails the build, and what silently does not

| Guard | Auto-covers new code? | What a new surface must do |
|---|---|---|
| `ControllerAuthorizationCoverageTests` | **Yes** — fully reflective | Carry no `[AllowAnonymous]`, or be added to `ExpectedAnonymous` |
| `ConcurrencyConflictTests` (Version/setter) | **Partly** — reflective for entities, hardcoded for DTOs | Add new round-tripped DTOs/commands to the `[InlineData]` lists |
| `TreatmentPlansControllerAuthorizationTests` | **Yes** — has a drift guard | Any new action on that controller must be classified into one of two arrays |
| `AdminSurfaceCoverageTests` | **No** — hardcoded array | Add a new catalog controller to `CatalogControllers`, or a one-off write to `GatedActions()` |
| `CnamControllerAuthorizationTests`, `MedicationsControllerAuthorizationTests` | **No** — no drift guard | Add new actions by hand |
| `RealtimeResourceResolverTests` | **No** — hardcoded `[InlineData]` | **This is what P4 fixes.** Until then, a new area broadcasts silently |
| `RateLimitingTests` | **Yes** for `/api/*` (globally limited) | Only if you add an exempt prefix must you add an `[InlineData]` |
| `MoneyReadConsistencyTests` | **No** — `Wire()` hand-reimplements repository SQL | Any repository filter change must be mirrored in `Wire()`, or the suite passes against the *old* rule |
| `*TenantIsolationTests` | **No** | Every new clinic-scoped verb needs a case; copy `CatalogTenantIsolationTests` (mock the repo to return an `OtherClinic` row — that *is* "filter inactive") |

Authorization policies available: `DoctorOrSecretary`, `DoctorOnly`, `SecretaryOnly`, `AdminOnly`, `AdminOrDoctor`.
Adding a policy is free; the four pins above compare `authorize.Policy` for **equality**, so gating an already-pinned
action with a new composite policy breaks them.

### Frontend conventions

- **A new admin page = 6 files**: `app/<x>/page.tsx` (copy `app/dental-acts/page.tsx` — `isLoading` checked *before*
  `isAdmin` or the Lock card flashes), a table component, a form modal, `lib/api/<x>.ts`, a `types.ts` interface, and
  a nav entry. Nav gating is a spread-conditional array in `configItems`.
- **`useClinicRealtime` opens one WebSocket per call** — use the array form for multiple keys, not multiple calls.
- **Label maps** are `.ts` (no React), `Record<string, string>` keyed by the backend enum name, every accessor
  `MAP[k] ?? k`. Copy `factures/invoice-labels.ts`. **Fold `invoices-table.tsx`'s local `statusBadgeClass` into its
  label module while doing this** — it is a fourth copy of the pattern waiting to drift.
- **`useConflict` + `FormErrorBanner`** cost per dialog: 2 imports, 1 hook call, `reset()` on open, wrap the catch,
  1 JSX line. Note `conflict.isConflict` is exposed and **used by nothing** — no call site wires the reload
  affordance, so § 1's conflict UX is thinner in practice than its ACs imply.
- **New primitives**: `cd web && npx shadcn@latest add sheet radio-group`. Both Radix deps are already present.

### Edit-risk ranking for the most-touched files

| File | Lines | Risk |
|---|--:|---|
| `web/components/document-editor-content.tsx` | 2527 | **Highest.** Branches on `documentType` in ~20 places across validate/payload/PDF/docx paths |
| `web/app/patients/[id]/page.tsx` | 1807 | High by size, but risk is contained per-tab (8 tabs); the shared loader at `:194-206` feeds all of them |
| `web/components/clinic-settings.tsx` | 1174 | Low if additive (append a `<Card>` in the tail); high if you touch working-hours state |
| `web/components/create-appointment-dialog.tsx` | 1136 | Moderate — long form, many interdependent `useState` |
| `web/components/edit-appointment-dialog.tsx` | 789 | Low-moderate — one component, flat state |
| `web/components/factures/invoices-table.tsx` | 672 | Moderate — many status-gated row actions |
| `web/components/stock-table.tsx` · `patients-table.tsx` | 411 · 428 | Low |
| `web/components/dashboard-sidebar.tsx` | 191 | Trivial |

---

# US-1: Close every finding in audit §§ 3–10

**As a** clinic using this software day to day
**I want** every action to either happen or tell me why not, every finished feature to be reachable, the app to work
on a phone, and the French to be French
**so that** I can trust the system without checking the database.

> **Sizing.** This story is deliberately oversized at the user's explicit request (**R-1**). It is structured into
> eight ordered parts; each is a vertical increment ending at a clean build gate and is a valid commit and resume
> point. **Do not stop mid-part in P1 (the `Type:` migration + the exclusion constraint), P4 (the precision
> normalization + the batch backfill), or P7 (the audit-trail retrofit across 38 handlers)** — the schema or the
> handler set is briefly half-migrated inside each.

---

## Part P1 — Appointment lifecycle & booking integrity

**Delivers:** every appointment action either happens or says why; double-booking becomes impossible; working hours
are real; the schedule speaks French. ACs **AC-P1.1–1.54**.

1. **One status machine in the domain.** Add a declared legal-transition set to `Appointment` and make it the only
   authority. Fix the guards: `Confirm()` refuses from `Completed`/`Cancelled` (A-1); `Reschedule()` preserves
   `Confirmed`/`InProgress` and explicitly does not preserve `NoShow` (A-2); add a legal exit from `Completed`.
   Tests: one per legal and illegal transition.
2. **Make the command layer ask the domain.** Replace `UpdateAppointmentCommand`'s fall-through `switch` with a call
   into the transition set; an illegal transition returns `Result.Failure` naming both statuses, never HTTP 200.
   This is AC-P1.1–1.4 and AC-P1.5–1.7.
3. **Handle the two `MarkVisitCompleted` callers.** They are post-commit best-effort helpers whose catch only logs
   and whose fiche has already committed. Distinguish *already `Completed`* (idempotent — still cancel the post-visit
   review, still broadcast) from *`Cancelled`/`NoShow`* (surface it). **Rewrite `PostVisitReviewCompletionTests`,
   which currently pins the silent no-op** — a test still passing here means it was pinning the defect.
4. **Answer the downstream questions.** `TreatmentPlanWorkflowProjection` lists `Completed` in `LiveStatuses` and
   `GetPatientsToRecallQuery` derives `lastVisit` from it. Decide and state what a `Completed → Cancelled` transition
   does to the plan act's état and to the recall list (AC-P1.10–1.11).
5. **Working-hours validation first.** `WorkingHoursSerializer` rejects unknown weekdays, unparseable `HH:mm`,
   `From >= To`, duplicate days. Existing unparseable rows surface in the editor as « Horaires existants illisibles »
   and in a one-off startup report — **not** as "no hours configured", which means *unrestricted*.
6. **The per-dentist hours editor** (§ 5.4) — wire the existing `GET`/`PUT /api/doctors/{id}/working-hours` into the
   clinic-settings doctors roster and « Mon profil ». Empty list clears the override and the UI says so. Fix the
   `ex.Message` leak in `SetDoctorWorkingHoursCommand` (A-10).
7. **Enforce hours on booking** — create, update, every recurring occurrence, with an override available to **any
   role that can book** (not admins only), recorded on the appointment, and a refusal that returns the user to the
   dialog with input intact. One shared resolution helper read by the guard, the editor and the calendar.
8. **Decide the non-HTTP writers** (AC-P1.29). `GoogleCalendarSyncService` creates and reschedules appointments
   directly through the repository; so do the waiting-list promote path and `AIActionService`. State per writer
   whether the refusal applies and what happens — inbound Google must not silently drop a Sunday appointment into a
   swallowed catch.
9. **The calendar grid** renders resolved open hours instead of `0..23`; both dialogs' hour pickers match; existing
   out-of-hours appointments still render.
10. **Recurring series** — return and *render* the conflicting dates with a « Replanifier » action per skipped
    occurrence; exclude `NoShow` as well as `Cancelled`; collapse the two overlap predicates into one helper; fix the
    `ex.Message` leak (A-7).
11. **The exclusion constraint.** Raw `Sql(...)` migration: `CREATE EXTENSION IF NOT EXISTS btree_gist`, add a
    generated end column (`Duration` is `bigint` ticks), then the partial exclusion constraint over
    `(DoctorId, [start,end))` `WHERE "Status" NOT IN (5,6)`. Decide and state the `DoctorId IS NULL` behaviour —
    PostgreSQL `=` never matches NULL, so an unassigned appointment is silently exempt unless coalesced.
    Translate `23P01` into AC-P1.14's French failure; § 1 only translates `DbUpdateConcurrencyException`, so today the
    loser would get a 500.
12. **Keep recurring skip-and-report working under the constraint.** `CreateRecurringSeriesCommand` adds every
    occurrence then calls one `SaveChangesAsync`, so a single violation would abort the whole series. Use a
    per-occurrence savepoint or pre-check-and-retry.
13. **Pre-flight before the constraint** (R-4): report every pre-existing pair the *partial* constraint would reject,
    by id. A cancelled-then-rebooked slot is legitimate history and must not abort the migration. Fail loud with the
    ids; never delete a row.
14. **French labels.** New `web/components/appointment-labels.ts` with all six statuses + a `GENDER_LABELS` map;
    delete the string-mangling `statusDisplay`; route the badge, the `Select` and the calendar legend through it;
    close the `"Unknown"` gender case (A-6).
15. **The `Type:` prefix migration.** Destination is `ProcedureTypeId`. Handle three row classes explicitly — prefix
    with no id (match and set), prefix with a *different* id (existing id wins, free text stays in `Notes`), prefix
    matching no catalog row (stays in `Notes`, counted). Idempotent, per-class row counts reported. **Remove the
    reader in `edit-appointment-dialog.tsx` in the same change as the writer.**
16. **Dialog English + a11y** — the § 8.2 subset in both appointment dialogs, plus `htmlFor`/`id` on the date/time
    block (AC-P1.53) and the a11y bar on the new hours editor and conflict list (AC-P1.54).

**Done when:** every transition test passes; a concurrent double-book yields one row and a French failure;
`verify-schema` reports the constraint and the extension present; the `Type:` migration reports its three counts and
is idempotent on a second run; `dotnet build` 0 errors / 0 new warnings; `tsc` clean.

---

## Part P2 — Finish what's built

**Delivers:** thirteen finished server-side capabilities get a caller. ACs **AC-P2.1–2.45**.

> **Internal order is fixed (Rule 1):** un-mark (steps 1–2) → soft-link cleanup (step 3) → delete buttons (step 4).
> Adding the delete button first is what makes § 6.11's orphaned links reachable.

1. **Un-mark a plan act.** New domain method returning the item to `Planned` and clearing
   `LinkedDentalRecordId`; **reopening the plan to `Accepted`** if it had auto-completed (without this the un-mark is
   cosmetic — `EnsureAmendable` still refuses); refused when the act is billed on a non-cancelled invoice.
2. **Role-gate the mark-done endpoint** (A-13) — it has no policy today, so a secretary can close a devis act.
3. **Dental-record delete cleanup** — clear `TreatmentPlanItem.LinkedDentalRecordId` (returning the act to `Planned`,
   reopening the plan) and `InvoiceLine.DentalRecordId`, **in the same transaction as the delete**. The invoice's
   amount and number are untouched.
4. **Delete buttons for fiche and document**, behind the standard `AlertDialog`, with confirmation copy that names the
   invoice when billed and warns when a plan act will revert. Role-gate both endpoints (A-12) and fix both message
   leaks (A-8 English + raw exception, A-9 English).
5. **Amend a devis** — « Modifier le devis » on the plan workspace, same condition as « Facturer le devis », posting
   to the existing endpoint. Surface every existing server refusal in the form via `FormErrorBanner`; **call
   unconditionally and surface the 403**, matching the established financial-reversal precedent.
6. **Revise the échéancier** — « Modifier l'échéancier » on the Échéancier card; locked rows shown *before* submit.
7. **Role change** (§ 5.8) — validate against the closed set (A-11), **do not null email/full name** (`User.Update`
   defaults both to `null` today), self-lockout guard, and **bump `TokenVersion`** or the old role stays live.
8. **Un-gate user management in Cloud** — delete `mode === "local" &&` from `dashboard-sidebar.tsx:86`. Hide or
   disable « Réinitialiser le mot de passe » in Cloud, since that command correctly refuses non-local accounts.
9. **Edit another practitioner** — add the missing client function for `PUT /api/doctors/{id}` (none exists, which is
   why it is unreachable) and surface CNOMDT + cachet in the doctors roster.
10. **Google Calendar disconnect** — new `AdminOnly` endpoint calling the existing `ClearGoogleCalendarConnection()`,
    beside « Importer depuis Google », behind an `AlertDialog`. Already-synced appointments keep their event ids.
11. **Colour palette from the API** — consume `GET /api/procedure-types/colors`; keep French labels client-side since
    the endpoint returns bare hexes (A-14); delete the hardcoded array and its "must match backend" comment.
12. **Lab-order transitions** — declared table, `ReceivedDate` re-stamped on a legal re-receive, illegal transitions
    return French failures, the UI offers only legal next states, existing rows in any state still load.
13. **Specialties in French** — display-time map, English storage keys retained (the `weekdayLabelsFr` precedent),
    the three duplicated arrays collapsed to one shared constant, unknown values rendered verbatim. **Note this
    reaches `document-editor-content.tsx`'s printed certificat and signature block** — the highest-risk file.

**Done when:** every newly-reachable endpoint has a caller and a tenant-isolation case; un-mark → delete → cleanup
verified in that order; no `ex.Message` in any handler this part touched; `dotnet build` clean.

---

## Part P3 — UX, accessibility & French

**Delivers:** the app stops lying about reminders, works on a phone, and every action gives feedback. ACs
**AC-P3.1–3.54**. **Gate is `tsc --noEmit` + `npm run build` + a documented manual walk** — there is no frontend test
runner.

1. **Recall truth** — `SendRecallCommand` learns whether anything was enqueued and fails with a French message when no
   channel is configured, *without* stamping contacted or snoozing. Then the part that matters: **a dispatch that
   later reaches `Failed` clears the snooze and returns the patient to the list** (AC-P3.5), or the defect just moves
   one step later. Partial multi-channel sends resolve to a stated state.
2. **Failed reminders visible** — generate an in-app notification on `NotificationStatus.Failed` via the existing
   `INotificationGenerator` seam; add patient name + appointment date to `ReminderStatusDto` (phone stays masked);
   visible to the secretary who books, not only admins; generation failure never fails the job.
3. **Mobile shell** — `npx shadcn@latest add sheet`; sidebar becomes a drawer below `md:`, closed by default, closing
   on navigation and Escape; header reflows; the AI panel and the document-editor column stop being fixed-width;
   the persisted desktop collapse preference survives.
4. **ClinicGuard 404** — redirect to `/login`, which exists; `returnTo` never points at a page-less route; the target
   is derived from session mode so Cloud is unchanged.
5. **Patients list edit action** — call the setters for the already-mounted `EditPatientDialog`; refresh the row
   without a full reload.
6. **AI speech off by default**, with a persistent discoverable toggle that survives reload.
7. **The five swallowed errors** + the sibling in `edit-appointment-dialog.tsx` (the audit's missing sixth), all via
   the **existing** `showErrorToast` helper. Sweep the ~12 further same-class swallows or list them as intentional.
8. **In-flight and feedback, generalized** (AC-P3.47) — every mutating action this spec adds is disabled while in
   flight, single-effect on double-submit, toasts on success, `showErrorToast` on failure with the dialog left open.
   Includes the folder double-click fix.
9. **`/patients` skeleton** following the only precedent (`stats-card.tsx`'s `animate-pulse rounded bg-muted`).
10. **Accessibility** — keyboard-operable cards in /documents and the files manager, `aria-label` on the icon-only
    delete, a real `<Label>` on the invoice cancellation reason, visible focus, `role="status"` + `aria-live` for
    async results (the repo has **one** `aria-live` region and zero `role="status"` today), and the same bar applied
    to every new surface. Add `radio-group` while here.
11. **`clinic-settings.tsx` uses `sonner`** — delete the bespoke banner and its 4 s timer.
12. **French** — the audit's list plus the seven it missed, « o / Ko / Mo » file sizes, Tunisian placeholders in
    `edit-patient-dialog.tsx` (which **is** the create form) including the one at `:764`, an explicit decision on
    « Phone Number ID », and a recorded repo-wide sweep so this closes as a class.
13. **The manual walk** (AC-P3.52) — every screen this spec touches or creates, at 375 px and by keyboard, recorded
    in `progress.md`.

**Done when:** `tsc --noEmit` 0 errors; `npm run build` clean at 27/27 pages; the walk is recorded; no user-initiated
action fails with only a `console.error`.

---

## Part P4 — Stock, realtime & schema

**Delivers:** stock tells the truth, every screen that mutates live-refreshes, and the schema stops being
internally inconsistent. ACs **AC-P4.1–4.43**.

1. **Per-batch stock** — a `StockBatch` child carrying expiry and batch number (the two scalar columns on
   `StockItem` are *overwritten* by each `AddStock`, so the spec's original "already models it" claim was wrong).
   FEFO consumption order stated. Migration folds existing values into one opening batch, idempotently.
2. **Expiry surfaced** — dialog captures it, `StockItemDto` returns batches, the table shows the earliest relevant
   expiry, at-or-past-expiry is visually distinguished, an approaching-expiry notification with a configurable lead
   time.
3. **`UpdateStockItemCommand` writes a `StockMovement`** whenever `CurrentStock` changes; a third
   `StockMovementType` member for a manual correction; **`Reason` populated at every write site** (all three pass
   `null` today); concurrency via the inherited `Version` rather than a second mechanism.
4. **Stock consumed by an act** (§ 6.7 — this had zero ACs until the completeness pass caught it): a material list on
   `ProcedureType`/`DentalActCode`, consumption fired on fiche save, **opt-in per act** so an act with no list behaves
   exactly as today, insufficient stock recorded and surfaced rather than blocking the visit, and the whole thing
   best-effort post-commit like `INotificationGenerator`.
5. **The realtime contract test, rewritten** — reflect over every `IRequest` in `*.Features.<Area>.Commands`, project
   through the resolver, assert the resulting **set** equals a declared set; then parse `clinic-hub.ts` and assert
   **both** directions. This is what catches the five orphans *and* the dead `documents` key, and what stops P7/P8's
   new areas broadcasting silently.
6. **Wire the five orphans + the dead key** — `clinic-hub.ts` declares every emittable key; `/waiting-list`,
   `/lab-orders`, `/caisse`, `/recalls`, `/creances`, `/recurring-series`, the dashboard and « Mon profil » subscribe
   to what their data depends on (array form — one WebSocket per hook call).
7. **Query filters for `Doctor` and `StockItem`**, fail-open in the same shape as the existing 17; correct the stale
   comment claiming filters apply to three entities.
8. **Reminder outbox** — index for `Status == Pending && ScheduledFor <= now` (closest precedent is
   `InvoiceConfiguration.cs:83`, the same outbox shape), a bounded batch size like `EInvoiceOutboxJob`, and a
   retention purge that never deletes a `Pending` row.
9. **`StockMovement.ClinicId`** gains an index and an FK to `Clinic`.
10. **Precision, properly** — add `ConfigureConventions` `HavePrecision(18,3)` **and delete the 26 redundant
    `HasColumnType` calls** across 18 files; normalize `StockItem.UnitPrice`; retain explicit annotations on
    `Clinic.VatRate` and `Invoice.VatRate` with a comment; add the model test asserting every mapped decimal resolves
    to `(18,3)` except those two. Read the migration for stray `xmin` columns.
11. **Recall query bounded** — date bounds pushed to SQL, identical results before and after, archived patients still
    excluded.

**Done when:** Σ movements reconciles with on-hand for post-change history; the contract test fails if a key is added
on either side alone; `verify-schema` reports the indexes, the FK and the precision; `dotnet build` clean.

---

## Part P5 — Build & tooling

**Delivers:** the build can fail. ACs **AC-P5.1–5.16**. **The `TreatWarningsAsErrors` step is the last thing in the
entire story.**

1. **Lint runs** — add `eslint` `^9.26.0` (the floor where the `eslint/config` subpath used by `eslint.config.mjs`
   exists) and `eslint-config-next` pinned to the exact Next version `15.5.9`, since it ships in lockstep.
2. **Fix or explicitly waive** the existing violations. A lint that passes because everything is disabled is not a
   gate.
3. **Remove `eslint.ignoreDuringBuilds`** — only after step 2, or the build breaks for everyone.
4. **CI** (AC-P5.4) — a workflow running the backend build, the suite and the frontend gate on push. This is net-new
   scope and is listed as such; without it the ~40 tests this story adds never run again. If declined, it moves to
   Out of Scope in the same edit.
5. **Pin and verify the toolchain** — one PostgreSQL version instead of probing five URLs, checksums on both
   downloads with a loud abort, and an assertion that the staged runtime matches what the installer claims. Correct
   the audit's ICU claim (it is PG 16.9; EDB genuinely bundles ICU 67.1).
6. **Warnings to zero** — the 46 CS8618 in Domain (21 files; `MedicalDocument.cs` alone has 8) chosen per property,
   not blanket-suppressed; then the remaining 12, of which four are the same `result.Value.Id` pattern best fixed
   once by adding `[MemberNotNullWhen(false, nameof(Value))]` to `Result.IsFailure`.
7. **Seeding survives a blip** — bounded retry, a clear log line when skipped, still idempotent. **Make
   `BackfillAsync` run in Local mode** (A-24 — its only call site is inside `if (!isLocalAuthMode)`, so no Local
   install has ever run it). **Stop a seeding failure killing the Windows service** (A-25 — the catch calls
   `StopApplication()` today).
8. **`Directory.Build.props` + `.editorconfig`** — neither exists. Then, **last**, `TreatWarningsAsErrors`.

**Done when:** `npm run lint` succeeds on a clean `npm ci`; the backend builds with 0 warnings; a deliberate new
warning fails the build.

---

## Part P6 — Money truth & timezone

**Delivers:** Tunisia is UTC+1 and the money reads agree. ACs **AC-P6.1–6.23**. The § 1 gate is lifted — § 1 merged.

1. **One clinic-timezone helper** replacing the two byte-identical private copies of `ResolveTunisiaTimeZone()`
   (A-21), plus a local-day-boundary helper returning an **explicit UTC instant** — `ApplicationDbContext` treats
   `DateTimeKind.Unspecified` as UTC on write, so a bare local `DateTime` would be silently reinterpreted.
2. **Local-day defaults** in `GetCaisseSummaryQuery` and `GetDashboardStatsQuery`, plus the four un-overridable reads
   (A-23) and the 5 `DateTime.Today` in `AIActionService` (A-22 — server-machine-local, a third convention).
3. **`payment-modal.tsx` pre-fills the local date** (A-20) — the real, un-overridable § 4.1 symptom.
4. **Numbering from the clinic-local year** in `IssueInvoiceCommand` and `AcceptTreatmentPlanCommand`, with tests
   pinning a **fixed** year rather than recomputing `UtcNow.Year` — § 1 flagged that the existing test can never
   detect a wrong-year defect. The numbering-collision retry is unaffected.
5. **§ 6.2 regression AC only** — § 1 already nets refunds in the dashboard; add the test so it cannot silently
   regress.
6. **invoice↔appointment link** — the form sends `appointmentId` when raised from an appointment context; an invoice
   shows its visit and a visit shows its invoice; optional, so invoices raised standalone are unchanged.
7. **One CNAM calculator** — the frontend calls the endpoint and the client-side duplicate is deleted; the estimate
   stays editor-only, never persisted, never on the BS1 PDF; a failed call degrades visibly.
8. **Currency** — `formatDT` in `lab-orders` and `procedure-types-table` (the latter is `toFixed(2)`, dropping the
   millime), the `DollarSign` icon replaced, explicit `fr-TN` on dashboard counters.
9. **The two query fixes** — batch-load patients in `GetReceivablesQuery`; use the light
   `GetTreatmentPlanLinksAsync` projection in the already-billed guard. **Mirror any repository filter change into
   `MoneyReadConsistencyTests.Wire()`** or the suite passes against the old rule.

**Done when:** a payment at 00:30 Tunis books to the right day and month in caisse, dashboard and the modal default;
an invoice issued 00:30 on 1 January carries the new year; `MoneyReadConsistencyTests` green.

---

## Part P7 — Audit trail, duplicate prevention & anonymize

**Delivers:** who changed what, duplicates prevented at source, and a patient can be anonymized. ACs
**AC-P7.1–7.36**. **P7a (steps 1–5) lands before the rest.**

1. **`AuditEntry`** — clinic, actor id + name snapshot, UTC timestamp, entity type + id, action, changed-fields diff.
   Clinic-scoped with the global filter, indexed `(ClinicId, EntityType, EntityId, OccurredAt)` and
   `(ClinicId, OccurredAt)`, append-only.
2. **The inline seam** — each audited handler writes its entry before its own `SaveChangesAsync`, in the same
   transaction. **Not a `SaveChanges` hook**: `NotificationGenerator` shares the scoped `DbContext` and flushes
   post-commit inside a swallowing catch, which would flush staged rows outside the transaction. An audit-write
   failure **fails the mutation** with a French message.
3. **Scope + actor** — Patient, Appointment, Invoice, TreatmentPlan and their owned children, attributed to the root;
   the exclusion declared in one reviewable place. `IClinicContext.GetUserId()` gives the id with no new dependency;
   the *name* needs an `IUserRepository` hit (`VoidPaymentCommand.ResolveActorNameAsync` is the precedent). **~38
   handlers touched.**
4. **Synthetic actors and `ClinicId` for non-HTTP writers** — `AIActionService`, `GoogleCalendarSyncService`,
   `NotificationJob`, the e-invoice outbox, `ClinicCatalogSeeder`, the recovery verb. "Nobody changed this" must not
   be a possible answer, and a `Guid.Empty` clinic would be invisible to the admin log's filter.
5. **Collection-diff shape and retention** — a collection-replacing mutation records the net change, not "the whole
   collection changed"; a retention purge job with a stated default.
6. **The two read surfaces** — an « Historique » view on patient/invoice/devis/appointment, and an admin-only paged,
   filterable clinic-wide log.
7. **Duplicate prevention** — a persisted normalized column (lower-cased, accents stripped, parts ordered) with an
   ordinary index, idempotently backfilled; a bounded in-memory distance check over the candidates; a **non-blocking**
   warning with openable matches; on **both** the patient form and the booking dialog's inline sub-form (the path
   that actually creates duplicates); archived patients included and shown as archived; degrades to no warning on a
   slow lookup; clinic-scoped with a tenant-isolation case; proceeding past a warning is audited.
8. **Anonymize** — type-to-confirm on the patient's full name; the removed/retained split stated; **the audit-diff
   scrub** (the one stated exception to append-only, itself audited); `MedicalDocument.PatientName` snapshots
   handled; refused while an e-invoice is `Pending` (TEIF sends `GetFullName()` as the legal buyer at dispatch);
   atomic, with blob removal **after** the DB commit; invoice numbers survive; cannot be un-archived.

**Done when:** an audited mutation with a failing audit write returns a French failure and commits nothing; a
duplicate name+DOB warns and is still creatable; anonymize leaves no identifying value in `AuditEntry`;
`verify-schema` reports the table, both indexes and the normalized column.

---

## Part P8 — CNAM claims, bordereau & reconciliation

**Delivers:** what CNAM was claimed and what it actually paid. ACs **AC-P8.1–8.30**.

> ⛔ **Blocked.** Q-1…Q-6 must be answered first — rejection motifs, resubmission rules, tiers-payant, accord
> préalable, AP1 vs AP2, and whether a CNAM receipt reduces the patient's balance. **Q-3 is decisive:**
> `follow-up/cnam-conventionné-bordereau.md` already records the bordereau as a conventionné/tiers-payant pathway,
> "deliberately out of scope for all current CNAM features". If this clinic is filière privée, P8 has no user.

1. `CnamClaim` — patient, originating BS1, acts with coefficients, amount claimed, care date, status as a declared
   **transition table** (§ 6.10 in this same spec is `SetStatus` being a bare assignment). Created from a BS1 without
   modifying the `MedicalDocument`. Amount computed by the **backend** calculator so estimate and claim cannot
   disagree. Reads `RequiresAccordPrealable`, which is stored today and read by nothing.
2. `CnamBordereau` + `CnamBordereauLine` — per-clinic sequential number taking its year from the **clinic-local**
   date (do not reintroduce AC-P6.4's defect in a new sequence), filtered-unique index following the invoice-number
   precedent. **Name the enforcement mechanism for "one non-cancelled bordereau per claim"** — a partial unique index
   cannot read the parent's status, so it needs a denormalized line status kept in sync, or a trigger.
3. Batching UI — checkbox column + sticky bar with a running `formatDT` total (`table.tsx` already ships the unused
   `data-[state=selected]` and `[&:has([role=checkbox])]` hooks). Unbatchable claims disabled with the reason inline.
4. Finalize, print, and **cancel-before-submission** returning claims to the pool — without which one mis-click at
   month end strands them. `canFinalize`/`canDeposit`/`canCancel` ship **on the DTO**, not re-derived client-side
   (the comment at `invoices-table.tsx:370-372` documents the bug that produced this rule).
5. `CnamReimbursement` + `CnamRejection` — **not** a `Payment`, so `PaymentMethod` and both money reads stay
   untouched; explicit allocation; partial payment first-class; over-payment flagged not clamped; resubmission
   reconciled with the one-bordereau rule (**Q-2**).
6. Reconciliation screen + the patient's CNAM position replacing the single indicative figure; « estimation » never
   used for a claimed or received number.
7. New realtime keys declared, new actions classified by the authorization guards, tenant-isolation cases added.

**Done when:** Q-1…Q-6 are answered; claimed / received / rejected / outstanding reconcile per bordereau; the
contract and authorization guards pass.

---

## Testing Strategy

### The honest constraint

**No test in this repository touches a database, in any form.** Verified: zero matches across the test project for
`UseInMemoryDatabase`, `UseSqlite`, `UseNpgsql`, `new ApplicationDbContext`, `DbContextOptionsBuilder`,
`Testcontainers` or `Respawn`. Everything is Moq against `Domain.Repositories` interfaces plus `IUnitOfWork`, or pure
domain assertions. There is no integration or E2E .NET project, and `web/` has **no test runner at all**.

The suite says so itself — `ConcurrencyConflictTests`' header: *"A database is out of reach here, so these tests pin
the parts that are pure logic and the parts that break silently."*

**Therefore, and stated plainly rather than implied:**

| Thing | Automated coverage |
|---|---|
| 13 schema changes, 3 data migrations | **None.** Operator-verified + `verify-schema` |
| The exclusion constraint, the filtered indexes, the FK | **None.** Same |
| `xmin` concurrency actually conflicting | **None** — only the C# contract around it |
| Every frontend AC | **None.** `tsc` + `npm run build` + the documented manual walk |
| Repository SQL | Only via hand-mirrored mocks (`MoneyReadConsistencyTests.Wire()`) |
| Handler logic, domain rules, DTO/command shape, controller attributes, namespace-derived keys | **Yes**, Moq + reflection |

### The `verify-schema` console verb

Because of the above, this plan adds a read-only verb following the `reconcile-money` template exactly — same
four-part split, `AddInfrastructure` **only** (never `AddApplication`, so the clinic query filters stay inactive and it
reads across every clinic), exit **0** clean / **1** couldn't run / **2** drift found.

It asserts: the `btree_gist` extension exists; the exclusion constraint exists and is partial; every index this plan
adds exists; every mapped `decimal` is `(18,3)` except the two annotated rate columns; the `Type:`-prefix migration
left no matching rows behind; the stock-batch backfill covered every item with a legacy expiry; the normalized-name
column is populated for every patient. **Run it before and after the migration batch and diff the output.**

Its logic lives in the Application layer behind a reader seam and is **not** DI-registered — exactly like
`MoneyReconciliationService` — so it *is* unit-testable with a mocked reader, and the wrapper (verb name, arg parsing,
exit codes) gets its own test.

### Per-part test additions

| Area | Type | What it pins |
|---|---|---|
| Appointment status machine | Domain + handler | Every legal and illegal transition, incl. A-1 and A-2; `NoShow` on reschedule |
| `MarkVisitCompleted` callers | Handler | Idempotent on already-`Completed`; surfaced on `Cancelled`/`NoShow`. **Rewrites `PostVisitReviewCompletionTests`** |
| Double-booking | Handler | The guard's arguments and the `23P01` → French translation. The constraint itself is operator-verified |
| Working hours | Domain + handler | A-5's invalid JSON refused; resolution order; the no-hours-configured no-op; the non-HTTP-writer rule |
| `Type:` migration | Application service + test | Three row classes; idempotent; a legitimate `Type:` note untouched |
| Plan amend / revise / un-mark | Handler | Every existing `Result.Failure`; un-mark reopens an auto-completed plan; billed act refused |
| Dental-record delete cleanup | Handler | Both soft links cleared in one transaction |
| Role change | Handler | A-11's null-wipe; closed role set; self-lockout; `TokenVersion` bump |
| Recall no-op | Handler | No channel → failure, no snooze, no contacted stamp; **dispatch-failure clears the snooze** |
| Realtime contract | **Reflection guard** | Exact set, both directions, parsing `clinic-hub.ts`. Replaces the hardcoded theory |
| Stock | Handler | FEFO order; Σ movements reconciles; `Reason` populated; the third movement type; act-consumption opt-in and shortfall |
| Decimal convention | Model test | Every mapped `decimal` resolves to `(18,3)` except the two annotated rate columns |
| Timezone | Unit | Local-day boundaries; **fixed year**, never `UtcNow.Year` |
| Money reads | `MoneyReadConsistencyTests` | Stays green — **and `Wire()` must be hand-updated for any repository filter change** |
| Audit trail | Handler | Same-transaction commit; mutation fails on audit failure; synthetic actor and `ClinicId` for non-HTTP writers; collection-diff shape |
| Duplicate detection | Handler | Transliteration variance; archived included; clinic-scoped; non-blocking; degrades on timeout |
| Anonymize | Handler | Audit-diff scrub; e-invoice refusal; atomicity ordering; invoice numbers survive |
| CNAM | Domain + handler | Claim transition table; one-bordereau enforcement; partial payment; over-payment flagged |
| New clinic-scoped verbs | `*TenantIsolationTests` | A case each — copy `CatalogTenantIsolationTests` (mock returns an `OtherClinic` row) |
| New controller actions | Coverage guards | Classified, or the build fails |

**Conventions:** xUnit + Moq, no database, no FluentAssertions, `Pascal_Snake_Case` names, class-level `<summary>` and
per-test `// [AC-n]` comments, deterministic GUIDs (`aaaa…`/`bbbb…`) and fixed UTC dates.

### Quality gate (every part)

| Check | Command | Requirement |
|---|---|---|
| Backend build | `dotnet build api/ClinicManagement.sln --no-incremental` | 0 errors; **0 new** warnings in changed files |
| Frontend types | `cd web && npx tsc --noEmit` | 0 errors |
| Frontend build | `cd web && npm run build` | clean — expect 27/27 static pages |
| Tests | `dotnet build -p:OutDir=<scratch>/utbuild/` then `dotnet vstest` | pass |
| Schema | `verify-schema` | exit 0 |
| Lint | `cd web && npm run lint` | **P5 onward only** |

**Baselines, re-measured before asserting:** 58 backend warnings at the § 2 merge (the § 1 worktree reported 116 from
a different base). Suite **941 passed / 3 failed**; the 3 are `ReminderSchedulerTests`, pure-Moq and unrelated.

**Environmental caveats.** `dotnet test` fails at assembly load with `0x800711C7` (Windows Smart App Control) — use
the `build -p:OutDir` + `vstest` path. **Never `--no-build` after changing production code** — it runs stale DLLs and
produced a false negative during § 2. `MSB3021`/`MSB3027` are file locks from a running API, not compile errors. The
PDF-render tests share process-wide QuestPDF state and are order-sensitive — judge against repeated runs.
`packaging/` is operator-verified only.

---

## Risk Register

| ID | Risk | L | I | Part | Mitigation |
|---|---|:-:|:-:|:--:|---|
| **R-1** | **One story is oversized.** 57 bullets, 26 adjacent defects, 8 subsystems, ~14 migrations — chosen deliberately by the user. Context exhaustion mid-story could leave a half-applied change | High | High | all | Parts are ordered and each ends committable. **Split at a part boundary** if a session runs long. Never stop mid-part in P1, P4 or P7 |
| **R-2** | **§ 6.4 is a feature inside a feature** even after merge was dropped — 1 entity, 2 migrations, 4+ endpoints, 2 UI surfaces, **~38 handlers touched** | High | High | P7 | P7a first is a hard rule. If it must be cut, the trail alone is the defensible core |
| **R-3** | **§ 6.5 is blocked on domain input this repo does not contain**, and a pre-existing follow-up already calls it a separate pathway | High | High | P8 | Q-1…Q-6 answered before P8 starts. Q-3 may retire P8 entirely |
| **R-4** | **The exclusion constraint may reject data that exists today** — and a naive pre-flight would also flag legitimate cancelled-then-rebooked history | Med | High | P1 | Pre-flight counts only pairs the **partial** constraint would reject; fails loud with ids; never deletes |
| **R-5** | **The `Type:` migration is irreversible** and touches free text users wrote. The dialog writes both the prefix *and* `procedureTypeId` from independent state, so conflicting rows exist today | Med | High | P1 | Three explicit row classes, nothing discarded, per-class counts reported, verified against a restored backup first |
| **R-6** | **The GiST index build takes `ACCESS EXCLUSIVE`** — and in Local, migrations run *after* Kestrel is serving | Med | Med | P1 | Schedule the migration batch for a maintenance window in Local; note that Cloud applies before serving |
| **R-7** | **A failed migration in Local calls `StopApplication()`** — a non-idempotent backfill that re-ran would take the whole app down | Med | High | P1, P4, P7 | Every data migration idempotent (`WHERE NOT EXISTS` / `IS NULL`); destructive steps last in the batch |
| **R-8** | **The precision normalization is 26 deletions across 18 files.** Missing one leaves the convention un-enforced for that column, silently | Med | Med | P4 | The model test (AC-P4.39) is the guard — it fails if any mapped decimal is not `(18,3)` bar the two exceptions |
| **R-9** | **`RealtimeResourceResolverTests` does not fail on a new area.** P7 and P8 add areas; if either lands before P4's rewrite they broadcast silently | Med | Med | P4, P7, P8 | Either land P4's contract-test rewrite before P7/P8, or add the keys by hand and accept the gap until P4 |
| **R-10** | **`MoneyReadConsistencyTests.Wire()` is a hand-mirror of repository SQL.** A filter change not mirrored gives a **green build against the old rule** | Med | High | P6 | Treat `Wire()` as part of the repository's public contract — change both or neither |
| **R-11** | **`document-editor-content.tsx` is 2527 lines and branches on `documentType` in ~20 places.** P2's specialty change reaches its printed certificat and signature block | Med | Med | P2 | Change the display map only; do not touch the `documentType` switches. Verify the printed output by hand |
| **R-12** | **Enforcing working hours could stop a clinic booking** | Med | High | P1 | The no-hours-configured no-op is the safety valve, plus the any-role override |
| **R-13** | **`ConcurrencyConflictTests`' DTO lists are hardcoded.** A new round-tripped DTO silently gets no coverage | Low | Med | P4, P7, P8 | Add to the `[InlineData]` lists as part of adding the DTO |
| **R-14** | **`AdminSurfaceCoverageTests` is a hardcoded array** — a new catalog-like controller is silently ungated | Med | High | P4, P8 | Add new admin surfaces to `CatalogControllers`/`GatedActions()` in the same commit |
| **R-15** | **`TreatWarningsAsErrors` breaks other branches** | Med | Low | P5 | Last step of the entire story, after the count is zero. Announce it |
| **R-16** | **No CI means every gate is manual**, so this story's ~40 tests may never run again after it lands | High | Med | P5 | AC-P5.4 adds CI. If declined it moves to Out of Scope explicitly |
| **R-17** | **Frontend ACs have no automated verification** — P3 is almost entirely frontend | High | Med | P3 | The documented manual walk (AC-P3.52) recorded in `progress.md`. Not equivalent to tests; stated plainly |
| **R-18** | **Scope creep.** A 57-finding story invites "while we're here" | High | Med | all | The spec's Out of Scope section is the boundary. Anything new goes to `follow-up/`, not into this story |

---

## Breaking Changes

1. **Appointment status transitions now return `Result.Failure` where they previously returned 200.** Any client
   relying on the silent success is relying on a lie — but it *is* a contract change.
2. **Booking outside working hours is refused** once hours are configured. A clinic that has never opened the
   settings screen is unaffected (no hours = unrestricted), which is the majority case on day one.
3. **Double-booking now fails at the database.** Anything that previously created overlapping appointments — including
   inbound Google sync and the waiting-list promote path — must handle a refusal.
4. **`Appointment.Notes` no longer carries a `Type: ` prefix**, and the migration rewrites existing rows.
5. **Three endpoints gain role policies** they should always have had: delete-fiche, delete-document, mark-item-done.
   A `secretary` who could do these can no longer.
6. **`StockItemDto` gains batches**; the two scalar expiry fields move to a child.
7. **`ReminderStatusDto` gains patient name and appointment date.**
8. **`PatientBillingSummaryDto`'s single indicative CNAM figure becomes estimated / claimed / received** (P8 only).
9. **A role change bumps `TokenVersion`**, so the target user's current token stops working immediately.
10. **`TreatWarningsAsErrors`** will fail any branch carrying a warning.

## Migrations

Ordered. Destructive and lossy steps last, per the Local fire-and-forget constraint.

| # | Migration | Part | Notes |
|---|---|:--:|---|
| 1 | Appointment end column + `btree_gist` + partial exclusion constraint | P1 | Raw `Sql(...)`; no precedent in this repo; `ACCESS EXCLUSIVE` (**R-6**); pre-flight first (**R-4**) |
| 2 | Appointment out-of-hours override marker | P1 | Plain column |
| 3 | **Data:** `Type:` prefix → `ProcedureTypeId` | P1 | Idempotent, three row classes, per-class counts (**R-5**) |
| 4 | `StockBatch` table + indexes | P4 | |
| 5 | **Data:** fold legacy `ExpiryDate`/`BatchNumber` into one opening batch | P4 | `WHERE NOT EXISTS` |
| 6 | Act → material list join table | P4 | |
| 7 | `StockMovementType` third member | P4 | Enum stored as `int`; no DDL, but the model snapshot moves |
| 8 | `Notifications` `(Status, ScheduledFor)` index | P4 | Precedent: `InvoiceConfiguration.cs:83` |
| 9 | `StockMovement.ClinicId` index + FK to `Clinic` | P4 | Precedent: `StockItemConfiguration.cs:21-26` |
| 10 | Decimal precision normalization | P4 | One legitimate `AlterColumn` (`StockItem.UnitPrice`); **read for stray `xmin`** |
| 11 | `AuditEntry` table + 2 indexes | P7 | |
| 12 | `Patient` normalized-name column + index | P7 | First index of its kind here — no functional-index precedent |
| 13 | **Data:** backfill normalized names | P7 | Idempotent |
| 14 | `Patient` anonymization state | P7 | |
| 15 | CNAM: `CnamClaim`, `CnamBordereau`, `CnamBordereauLine`, `CnamReimbursement`, `CnamRejection` + filtered unique index + the line-status denormalization | P8 | Blocked on Q-1…Q-6 |

**Every one of these:** stop the API before `migrations add`, read the generated file, delete any
`AddColumn<uint>("xmin")`, never `--no-build`. Run `verify-schema` before and after and diff.

## Documentation to update on completion

Root `CLAUDE.md` (status machine, hours enforcement, audit trail, CNAM subsystem, the realtime contract) ·
`Domain/CLAUDE.md` (new aggregates + transition tables) · `Application/CLAUDE.md` (new areas, the clock helper) ·
`UnitTests/CLAUDE.md` (new guard tests; also correct the stale "~90 classes" → ~117 and the stale "references only
Application") · `web/CLAUDE.md` + `web/components/CLAUDE.md` (responsive shell, label-map convention, `Sheet`) ·
`packaging/README.md` (pinned toolchain, checksums, `verify-schema`) · `Infrastructure/CLAUDE.md` (correct its claim
that `AddLocalAuthUserFields` adds a *lowercased-email* partial index — the index is on the raw column) ·
`CODEBASE_AUDIT_2026-07.md` (tick closed items; correct the § 6 count, the grand total 74 → 77, the § 10.4 "9 more",
and the ICU claim).
