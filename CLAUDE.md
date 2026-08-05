# Clinic Management — Repo Guide

Full-stack **dental/medical clinic management** system (Tunisia-targeted: French UI labels, Tunisian governorates). Multi-tenant by clinic, with patient records, appointments + Google Calendar sync, medical/dental documents, file storage, and an AI assistant.

> **Read this first.** Each major folder has its own `CLAUDE.md` with details. This file is the map — jump to the right sub-guide instead of re-reading source.

## Stack at a glance

| Layer | Tech | Location |
|-------|------|----------|
| Frontend | Next.js 15 (App Router), React 19, TypeScript, Tailwind v4, shadcn/ui, Auth0 | `web/` |
| Backend API | .NET 8, Clean Architecture, ASP.NET Core, MediatR (CQRS), Hangfire | `api/` |
| Database | PostgreSQL 16 (EF Core) | docker-compose `postgres` |
| Object storage | MinIO (S3-compatible) | docker-compose `minio` |
| Auth | **Pluggable** by `Auth:Mode`: **Cloud** = Auth0 (JWT bearer); **Local** = self-issued email+password accounts for offline LAN installs. Clinic membership resolved server-side. | both |
| External | Google Calendar (two-way sync), HuggingFace (AI chat), SMS/WhatsApp reminders, TTN « El Fatoora » e-invoicing, Meta WhatsApp onboarding | `api/...Infrastructure/Services` |

## Layout

```
clinic-management/
├── api/                          .NET 8 Clean Architecture solution (ClinicManagement.sln)
│   ├── ClinicManagement.Domain/         → CLAUDE.md  (entities, value objects, repo interfaces)
│   ├── ClinicManagement.Application/     → CLAUDE.md  (CQRS features, MediatR pipeline, DTOs, Result<T>)
│   ├── ClinicManagement.Infrastructure/  → CLAUDE.md  (EF Core, repos, external services, DI)
│   ├── ClinicManagement.API/             → CLAUDE.md  (controllers, SignalR hubs, background jobs, Program.cs startup)
│   └── ClinicManagement.UnitTests/       → CLAUDE.md  (~90 xUnit+Moq classes mirroring every layer; guard tests)
├── web/                          Next.js frontend
│   ├── (root)                            → CLAUDE.md  (stack, routing, API/auth integration)
│   ├── components/                       → CLAUDE.md  (feature components + shadcn/ui primitives)
│   └── lib/                              → CLAUDE.md  (API client layer, hooks, realtime, utils)
├── desktop/                      WPF + WebView2 thin client shell (Local mode, Phase 5) → CLAUDE.md
├── packaging/                    Local/offline-LAN publish + installers (PowerShell + Inno Setup) → CLAUDE.md (+ README.md operator guide)
├── backend/                      EMPTY (only .idea/) — ignore
├── docker-compose.yml            postgres (5432) + minio (9000 API / 9001 console)
└── *.md (root setup docs, see below)
```

The **dependency direction** in `api/` is strict Clean Architecture: `API → Application → Domain`, with `Infrastructure` implementing Application's outbound interfaces. Domain has no infrastructure dependencies.

## Where things live (quick index)

- **A REST endpoint / route** → `api/ClinicManagement.API/Controllers/` (30 controllers). Controllers are thin MediatR pass-throughs.
- **Business logic / a use case** → `api/ClinicManagement.Application/Features/<Area>/{Commands,Queries}/` (handlers).
- **An entity / business rule** → `api/ClinicManagement.Domain/Entities/`.
- **DB schema / a query implementation / EF config** → `api/ClinicManagement.Infrastructure/Persistence/` + `Repositories/`.
- **An external integration** (Google Calendar, AI, files, notifications) → `api/ClinicManagement.Infrastructure/Services/`.
- **Realtime (live refresh across clients)** → SignalR `ClinicHub` at `/hub/clinic` (`api/ClinicManagement.API/Hubs/`), fed by the Application `RealtimeBroadcastBehavior`; frontend `web/lib/realtime/`.
- **A page / screen** → `web/app/<route>/page.tsx` (App Router).
- **A UI component** → `web/components/` (feature) or `web/components/ui/` (shadcn primitives).
  ⚠️ **Before writing any frontend code, read `.claude/rules/frontend-web.md`** — the device + UX contract this
  app is held to (usable at 320 px · 44 px targets on a **coarse pointer**, not on a breakpoint · a table has a
  card form · a heavy dialog becomes a sheet in `dvh` · no capability removed by a layout decision), and the
  gate: `npm run check:responsive` + `npx tsc --noEmit` + `npm run build`, then an eye pass at
  320/390/820/1180/1440 px. There is no test runner, no working ESLint and no CI in `web/`, so that *is* the
  gate. It is the directive form of `web/CLAUDE.md`'s conventions, not a second copy of them.
- **Frontend → backend calls** → `web/lib/api/` (per-resource modules over `client.ts`).
- **A backend test** → `api/ClinicManagement.UnitTests/` (xUnit + Moq, one folder per layer).

## Running locally

```bash
docker compose up -d              # postgres + minio
cd api/ClinicManagement.API && dotnet run    # API (default http://localhost:5000)
cd web && npm install && npm run dev          # frontend (http://localhost:3000)
```
Frontend talks to the API via `NEXT_PUBLIC_API_URL` (default `http://localhost:5000/api`). EF migrations live in `Infrastructure/Migrations`.

## Key architectural notes (verified, may surprise you)

- **Optimistic concurrency, solution-wide (`data-and-money-integrity`)**: `Entity<TId>.Version` is mapped in the
  `ApplicationDbContext` loop onto PostgreSQL's **`xmin` system column**, giving all 38 entities a concurrency
  token with **no schema change**. A losing write raises `DbUpdateConcurrencyException`, translated **once** in
  `UnitOfWork.SaveChangesAsync` into `ConflictException` → **HTTP 409** with the canonical `{ error }` body.
  Two things are easy to get wrong here: (a) the handler catch-alls must carry
  `when (ex is not ConflictException)` or a 409 is flattened into a generic failure — only catches that
  *return a Result* were filtered, since a log-only catch is a best-effort post-commit side effect that must
  still swallow; (b) the check must run against the version **the user was editing**, so the six round-tripped
  aggregates (Patient, Appointment, Invoice, TreatmentPlan, DentalRecord, Clinic) send `Version` back on the
  update and the handler calls `IUnitOfWork.SetExpectedVersion` — a version of `0` means "not supplied" and
  skips the check, which is what keeps the AI dispatcher, Google→App sync and the jobs working.
  ⚠️ The `AddConcurrencyToken` migration has a **deliberately empty `Up()`**: EF's differ emits 38 ×
  `AddColumn<uint>("xmin")`, which PostgreSQL rejects (`column name "xmin" conflicts with a system column
  name`). It is committed for its **model snapshot** only.
- **Money is correctable, not immutable (`data-and-money-integrity`)**: invoice payments and treatment-plan
  installment payments can be **voided** (motif + actor + moment recorded; the row is kept and struck through,
  and a reprinted receipt is stamped « REÇU ANNULÉ »). The installment side required an **event-sourced
  `InstallmentPayment` ledger** — a single cumulative `AmountPaid` has nothing to void, and dating it by one
  `LastPaidOn` also booked revenue into the wrong month. **Avoirs** are now readable, listable and printable,
  and are netted in *both* branches of the revenue read and in the dashboard KPI. Issuing a **devis→facture**
  bridge invoice **carries the plan's collected payments across**, with the read-side de-dup extended from
  outstanding to cash — the two had to land together or the money is either doubled or erased.
- **Patient records resist destruction (`data-and-money-integrity`)**: deleting a patient is **refused** when
  anything is attached (the message names the real counts), and **archiving** (`Patient.IsArchived`) is the
  escape hatch. Contact details are genuinely optional — `Email`/`PhoneNumber` are nullable and the four
  sentinel literals (`noemail@example.com`, `0000000000`, `unknown@example.com`, `000-000-0000`) are retired.
  The `PUT /api/appointments/{id}` partial-update wipe is closed by generalizing the tri-state pattern to
  `ProcedureTypeId`/`DoctorId`/`Notes`/`DoctorName`; `UpdatePatientCommand`'s contact fields use the same
  mechanism, so a field can finally be **cleared** rather than only overwritten.
  **And a patient can no longer be created twice by accident**: `CreatePatientCommandHandler` runs
  `Features/Patients/PatientDuplicateIndex` (name+DOB · name when no DOB was supplied · phone through
  `PhoneNumber.ToE164`, names folded through `SearchTerm.Normalize`) before it builds anything, refusing with
  **`patient_duplicate`** so the client can offer « Créer quand même » (`AllowDuplicate`) — advisory, because two
  people genuinely share names and the « Nouveau patient » form is also where a walk-in is registered with nothing
  but one. ⚠️ **The matching was not new — its reach was.** It had lived as a private nested class of
  `PatientImportPlanner`, so the CSV import (the least-used door) checked while the patient form and the appointment
  dialog's inline « Nouveau patient » did not; it was **moved**, not copied, so « what counts as the same person » has
  one answer. This is the `fixes-dont-propagate` shape again. On the client the load-bearing half is separate and
  worse: `create-appointment-dialog`'s `performCreate` **re-ran `patientsApi.create` on every retry**, and it is
  retried by design (slot-taken, out-of-hours and past-time all re-submit) — so one « créer quand même » on a taken
  slot created the patient a second time. It now remembers the created id in a ref and reuses it, and the new-patient
  fields go read-only once that patient is committed, since the record exists and the form can no longer reach it.
- **`reconcile-money` (Local-mode console verb)**: `dotnet run -- reconcile-money` prints a per-clinic
  reconciliation — the two payment ledgers against their stored denormalizations, per-plan échéancier sums,
  monthly collected computed the **old and the new way** (the line that proves the ledger migration moved no
  closed month), orphan and sentinel counts, over-credited invoices and duplicate bridge invoices. Exit code
  **0** clean / **1** couldn't run / **2** drift found; it never mutates. Run it before and after the migration
  batch and diff.
- **`verify-schema` (Local-mode console verb)**: the sibling gate for **schema** changes, added by
  `audit-sections-3-to-10`. Nothing in the test project touches a database, so a migration is the one class of
  change unit tests structurally cannot verify — an index can be missing, an exclusion constraint can be
  non-partial, a backfill can cover zero rows, and the whole suite still passes. `dotnet run -- verify-schema`
  reads the **EF model** (its declared indexes, FKs and decimal precisions) and diffs it against PostgreSQL's own
  catalog, so a schema object added in a configuration file is verified for free — deliberately **not** a
  hand-maintained expectation list, which is the failure mode the plan's R-9/R-13/R-14 all describe. On top of the
  diff it asserts what the model cannot express: `btree_gist` is installed, the appointment exclusion constraint
  exists **and is partial** (a non-partial one makes a cancelled slot permanently unbookable), the two VAT-rate
  columns keep `(5,2)` while every other decimal is `(18,3)`, and the per-migration backfill row counts. Indexes
  are matched on **table + ordered columns, never on name** (a hand-written migration's name legitimately differs
  from EF's). Same exit codes and the same before/after-and-diff workflow as `reconcile-money`; read-only.
  Logic in `Application/Common/Maintenance/SchemaVerificationService.cs`, both-sides reader in
  `Infrastructure/Persistence/SchemaVerificationReader.cs`.

- **Tunisia is UTC+1, and `ClinicClock` is the only thing that knows it (`audit-sections-3-to-10` P6)**: the solution had
  **no clock abstraction** — 292 × `DateTime.UtcNow`, 5 × `DateTime.Today` (the *server machine's* zone, a third
  convention) and two byte-identical private copies of a timezone helper. `Application/Common/ClinicClock.cs` is now the
  single authority: `ClinicToday`/`ClinicYear`, `StartOfLocalDayUtc`/`EndOfLocalDayUtc`, and the three P6 additions
  `LastTickOfLocalDayUtc`/`LocalDayRangeUtc`/**`TodayRangeUtc`** ("what a query means by today"). It closed the audit's
  only 🔴: **invoice, devis *and* avoir numbers took their year from `DateTime.UtcNow.Year`**, so a note issued at 00:30
  on 1 January Tunis was numbered into the fiscal year that had just closed — and a document number is legal identity,
  gapless per year, with no correcting it afterwards. Same root cause, six more places: la caisse's « aujourd'hui »
  default, the four reads that take **no date arguments** at all (« Solde patient », « Créances », les relances, the AI
  summary — so no caller could compensate), and the AI assistant's « demain ».
  ⚠️ Two traps worth knowing. (a) `EndOfLocalDayUtc` is the *next* midnight (**exclusive**) while every money read is
  inclusive on both ends — use `LastTickOfLocalDayUtc`, or a midnight payment lands in **both** adjacent periods
  (finding #20). (b) On the client the equivalent is **`todayLocalIso()`** (`web/lib/format.ts`), never
  `new Date().toISOString().slice(0, 10)`: `toISOString` converts to UTC first, so for the first hour of every Tunisian
  day it pre-filled *yesterday* — that was the one genuinely user-visible symptom, a payment taken at 00:30 booked to the
  previous day and, on the 1st, the previous month.
- **A visit knows whether it was billed (`audit-sections-3-to-10` P6)**: `Invoice.AppointmentId` had existed since the
  invoice was written — accepted by the command, returned by the DTO, mapped by EF — and **nothing had ever populated
  it**, so « cette consultation a-t-elle été facturée ? » had no answer on any screen. The write side now validates the
  id against the caller's clinic **and** the invoice's patient (a column nobody writes is a column nobody validates);
  the read side is `IInvoiceRepository.GetAppointmentLinksAsync` → **`AppointmentInvoiceLinks`**, one batched projection
  feeding `AppointmentDto.InvoiceId`/`InvoiceNumber`. It is a shared helper rather than inline code because both
  appointment reads must agree on *which* invoice counts: a **cancelled** note does not bill the visit (« Facturé » with
  no money behind it, and it would hide the action to raise a replacement), and an issued one beats a stray draft.
  Unlike its two clinic-wide siblings (`GetTreatmentPlanLinksAsync`, `GetDentalRecordLinksAsync`) it is **bounded by the
  caller's id set** — the agenda has a date window, and annotating one week must not read every appointment-linked
  invoice the clinic has ever raised. ⚠️ Note what is still **not** money: a fiche de soins carries `Cost`/`AmountPaid`
  that **no money read touches** — encaissements are invoice `Payment` rows plus plan `InstallmentPayment` rows, minus
  avoirs, and nothing else. A visit is financially invisible until a note d'honoraires or an échéance exists.
- **One CNAM calculator (`audit-sections-3-to-10` P6)**: the reimbursement estimate existed **twice** — the tested
  backend `CnamReimbursementCalculator` and a client-side copy in `web/lib/api/cnam-nomenclature.ts` with its own
  `CHILD_RATE`/`ADULT_RATE`, guaranteed to drift the first time CNAM moved a rate or a band edge. The client copy is
  deleted; the BS1 editor calls a new **batch** endpoint (`POST /api/cnam-nomenclature/reimbursement-estimates`). Batch,
  not the existing single-act GET, because the editor shows a live estimate **per act row** — N requests per keystroke is
  what made a client-side copy attractive in the first place. Each item carries its own `careDate` (the rate turns on age
  *at the care date*, and a bulletin's acts can straddle a birthday). Still editor-only: never persisted, never on the
  BS1 PDF.
- **La caisse has a statement, and it is a read (`caisse-extrait`)**: `GET /api/billing/caisse/ledger` returns the
  **« extrait de caisse »** — every movement behind the totals, oldest first, with a running period balance. Before it,
  la caisse showed three figures over a table of **expenses only**: the money-*out* side was itemised while
  « Encaissé », the bigger number, was opaque, and no screen anywhere listed what made it up.
  ⚠️ **There is no `CashMovement` table, deliberately.** `GetCaisseLedgerQuery` merges the four ledgers that already
  exist (invoice `Payment`, devis `InstallmentPayment`, `CreditNote`, `Expense`) through the **same repository
  predicates the totals sum** — the two row-level reads (`GetPaymentsBetweenAsync`,
  `GetInstallmentPaymentsBetweenAsync`) are predicate-for-predicate copies of their sum siblings, including the
  billed-plan de-dup. A movement table written by each money path is double bookkeeping: the day one write site
  forgets, the statement and the totals disagree and nothing can say which is right. Reading the same rows makes
  **`Σ movements == cashIn − refunds − cashOut == net`** an assertion a test holds (`CaisseLedgerTests`), which the
  table design cannot offer. A **voided** row is listed with its motif and actor and excluded from the balance
  (§ 1 keeps a void visible, struck through); `RunningBalance` is **window-relative** and labelled
  « Solde de la période », not an account balance.
  **`CaisseSummaryDto.CashIn` is now gross and `Refunds` is its own field** — it used to absorb avoirs silently,
  which stopped working the moment a statement listed a refund as money leaving: the lines could not sum to the
  total above them. The dashboard's Argent section gained the same split in the same change (the two are held equal
  by `MoneyReadConsistencyTests`), so `Net = Collected − Refunds − Expenses` on both. A refund-only window now reads
  honestly (`cashIn` 0, negative net) instead of reporting a *negative cash-in*, which is not a thing a till has.
- **A session's payment reaches the till (`caisse-extrait`)**: `POST /api/invoices/from-dental-record/{id}` prices a
  fiche de soins' acts, **issues** the note d'honoraires and — when `paidNow` is supplied — records that payment, in
  **one transaction**. It closes the trap named in the invoice↔visit note above: `DentalRecord.AmountPaid` was read
  by nothing but the fiche's own display, so a dentist could type an amount, see it on screen, and it would never
  reach la caisse, the dashboard or the patient's balance. Cash lives in exactly two ledgers and the fix is to make
  the fiche produce a real payment on a real numbered document — **not** to teach a fourth read about a fourth source.
  ⚠️ Two things to know. (a) Unlike the devis bridge this does **not** produce a draft: a payment requires an
  `Issued` invoice, so a **gapless number is consumed** and a mis-keyed amount is corrected by an **avoir**, never an
  edit — which is why every validation (amount, method, date, over-payment against the TTC the invoice *will* freeze
  via `InvoiceCalculator.Compute`) runs **before** the transaction opens. (b) The per-tooth pricing rule (quantity ×
  unit price vs. one flat fee) **moved** out of the browser into `DentalRecordInvoiceLines` — it lived inline in the
  patient page to seed a form, and two implementations of how recorded work becomes money is the § 5.10 defect in a
  new place. The old prefilled `InvoiceFormModal` path on the fiche is replaced by `bill-dental-record-dialog.tsx`,
  which shows the acts read-only and lets the server price them.
- **The dashboard is a composed read, not a KPI bag (`dashboard-insights`)**: `GET /api/dashboard?period=Today|Week|Month`
  returns four sections — comparable **Activité** (RDV honorés, nouveaux patients, **taux d'absence**, devis acceptés) and
  **Argent** (encaissé, facturé, dépenses, net), the point-in-time **créances** total, the **À-traiter** counts across the
  operational subsystems, and a 6-month collected **trend**. It closes the two items `features/live-dashboard/` explicitly
  deferred (delta computation and card drill-down). A thin handler fans out to four **section readers**; two primitives do
  the load-bearing work. **`DashboardPeriod`** is the *single* authority on period arithmetic — it derives the current
  **and** previous bounds through `ClinicClock`, because a comparison whose halves came from two different rules is not a
  comparison (the client used to send six boundaries; it now sends only a period key). ⚠️ Its `ToInclusive` is the last
  **tick** of the window, not the next midnight: `ClinicClock.EndOfLocalDayUtc` is *exclusive* while the money reads are
  inclusive on both ends, so the raw bound counts a midnight payment in **both** adjacent periods — finding #20 re-armed.
  **`PeriodComparison`** is the one shape of a comparable figure, and distinguishes a real `0` from an *undefined* value:
  a period with no appointments has **no** taux d'absence, and rendering `0 %` would assert perfect attendance.
  ⚠️ The readers are awaited **sequentially** — they share the request's `DbContext`, so `Task.WhenAll` throws.
  **Every figure is a link to the filtered records it counted**, and the KPI→route mapping lives in exactly one place
  (`web/lib/dashboard-links.ts`, an exhaustive `Record`, so a KPI with no destination is a `tsc` error). Making that true
  required nine destinations to learn filters — including a genuinely new created-date filter on `/patients`, a
  **date-range mode** on `/caisse` (day-only before), a **stage filter** on `/lab-orders` (it had none), and an expiring
  filter on `/stock`. Two links carry a trap worth knowing: « Devis acceptés » counts by `AcceptedDate` and so filters on
  `acceptedFrom`/`acceptedTo` (`from`/`to` bound *creation* — a different set of devis), and « Taux d'absence » sends
  `status=NoShow,Cancelled` because that pair *is* the rate's numerator. `MoneyReadConsistencyTests` was extended to pin
  dashboard-vs-caisse agreement, so the money section is now the **fourth** read held to the one figure.
- **Every list read is a page, and search is a database question (`list-pagination`)**: the lists had **no paging
  anywhere** — no backend primitive, no `Skip`/`Take`, no pager — so every table fetched a clinic's entire history and
  filtered it in the browser. `Domain/Common/Paging.cs` is now the single authority: **`PagedResult<T>`** (items +
  `TotalCount`, because « N résultats » and the page count are the same number and a page carrying only its own rows
  cannot tell the client whether there is more) and **`PageRequest`** (clamps, never rejects — a stale bookmark asking
  for page 4 of a 3-page list should show rows, not a French error). It lives in **Domain** for one structural reason:
  the repository interfaces are there and that project has zero references.
  ⚠️ **`paging: null` is a first-class case, not a large page** — the pickers, the header lookup, the AI dispatcher and
  every money **total** legitimately read everything, and modelling that as "page 1 of size `int.MaxValue`" would put a
  bogus `LIMIT 2147483647` in the SQL. On the client the mirror image is `list()` (unwrapped, `T[]`) vs `listPaged()`.
  **The load-bearing half is that search moved into SQL.** Free-text filters were in-memory C#, which was *equivalent*
  to searching the clinic only because the handler held the clinic; over a page it silently answers a different
  question — a patient on page 7 reads as « aucun résultat ». `Application/Common/SearchTerm.cs` normalises the term
  (and **escapes LIKE wildcards** — an unescaped `%` matches every row, so the filter appears to do nothing) and
  `Infrastructure/Persistence/SqlSearch.cs` maps PostgreSQL **`unaccent`** so `Béchir` matches `bechir` in the
  database. A normalised persisted column was rejected: it is a write-path obligation on eleven aggregates, and any
  writer that forgets it produces a row invisible to search — indistinguishable from the record not existing.
  ⚠️ Three traps. (a) **Every paged read must order on a unique column last** (`.ThenBy(x => x.Id)`); `OFFSET` over a
  non-unique sort may show a row on two pages and skip another, which looks like "a record vanished".
  (b) **An in-memory filter and a SQL page cannot coexist** — the catalogs' `category`/`q`, the patients' flag filter
  and the lab orders' patient filter all moved into the repository, because filtering an already-cut window shrinks
  pages unpredictably. (c) Paging a list bought nothing while its **companion read** stayed unbounded: the invoice and
  devis lists loaded *every* patient of the clinic to resolve names (now `GetByIdsAsync` over the page), and the
  recurring-series list read *every* appointment to count occurrences (now one `GROUP BY` over the page's ids).
  **Two reads page in memory, deliberately**: « Créances » and the « extrait de caisse » are ordered *unions* of
  several ledgers, so no single query knows a row's position — `PagedResult.FromSource` is for exactly those, and the
  statement's `RunningBalance` is computed over the **whole window before** filtering or paging, because « Solde de la
  période » is a fact about a movement's place in the period, not about the current page.
- **A séance is several acts, and the scalars are derived (`multi-act-appointments`)**: an `Appointment` held **one**
  `ProcedureTypeId`, so « détartrage + deux obturations » — one visit — could only be typed into the notes: invisible
  to the colour, the duration, the fiche de soins proposal and the devis. It now owns an **`AppointmentProcedure`**
  child collection, and `ProcedureTypeId`/`ProcedureDurationMinutes`/`ProcedureColorHex`/`TreatmentPlanItemId` are a
  **derived snapshot of the first row** (`Appointment.SetProcedures` re-derives all four). Keeping the scalars is the
  point: the agenda paints one colour, `ProcedureType.Appointments` is a real FK, and every existing read keys off
  them — none had to learn about a list to stay correct.
  ⚠️ Four traps. (a) **`SetProcedureType` now means "this visit has exactly this one act"** — it replaces the list.
  `UpdateProcedureTypeCommand` therefore calls **`RefreshProcedureSnapshot`** instead; the old call would have deleted
  the other acts of every séance using a renamed procedure. (b) The devis read-back must group on
  **`LinkedTreatmentPlanItemIds`**, not the scalar: two plan acts booked into one visit are two child links, and
  keying on the scalar left the second reading « À planifier » forever, offering to book a visit that already exists
  (`TreatmentPlanWorkflowProjection`, and `IAppointmentRepository.GetByTreatmentPlanItemIdsAsync` matches child rows).
  (c) A child row's `ProcedureTypeId` is **nullable**: a hand-typed devis line has no catalog act, and refusing it
  would mean a grouped séance carried only the links of the acts that happen to match the catalogue. Such a row takes
  its name from the plan step's désignation and contributes **no** duration. (d) On the wire, `procedures` is
  **tri-state on update** — omit it to leave the acts alone, `[]` to clear them; cancelling posts `{ status }` alone,
  so conflating the two would delete every act on every cancellation (the same defect the `ProcedureTypeId` tri-state
  fixed). Duration defaults to the **sum** of the acts, client- and server-side. **Grouping is a UI decision, not a
  stored one**: there is no séance entity — two acts sharing an appointment *are* one séance, which is why the plan
  can display the grouping (`plan-act-row`'s « séance de N actes ») with no extra field. In the plan workspace the
  user ticks acts and chooses « Planifier ensemble » (one RDV) or « Planifier séparément » (one each), and mixed
  splits fall out of repeating the gesture; `verify-schema` gained `appointment-act-rows`, pinning that no
  appointment names an act with no row behind it.
- **Who may do what, and who did it (`adoption-qa-i-access-control-and-audit`)**: the product had three
  authorization policies defined and **never applied** — `DoctorOnly`, `SecretaryOnly`, `DoctorOrSecretary`, zero
  usages for the whole life of the product — while **33 endpoints carried a bare `[Authorize]`** (any authenticated
  user, any role) and **20 controllers carried no policy at all**, including la caisse, les créances, the
  dashboard, patient delete/archive, the odontogram and every clinical note. They stayed green because the guard
  test only asserted that a policy *existed*. Nor was it a hidden-menu-with-a-live-API case: `web/lib/nav.ts`
  shipped « Tableau de bord » and the whole « Finances » group to every role, and the three finance pages contained
  no `role` reference. **Every one of the 32 controllers now carries a class-level named policy and no bare
  `[Authorize]` remains**, over a vocabulary of four — `Authenticated` (the onboarding surface, which in Cloud is
  reached *before* the role is in the JWT), `AnyClinicRole`, `AdminOrDoctor`, `AdminOnly`.
  ⚠️ **The load-bearing distinction is not "lock the money down"**: a secretary must be able to take a payment and
  read *one patient's* balance — that is reception's job — but must not read clinic-wide aggregates. Per-patient
  money: yes. Clinic-wide money: no. So `POST /api/invoices/{id}/payments` and
  `GET /api/patients/{id}/billing-summary` stay **deliberately open**, while `billing/caisse`, `/caisse/ledger`,
  `billing/receivables`, `invoices/revenue` and the whole dashboard are `AdminOrDoctor`.
  ⚠️ **The second distinction was added later, and it reverses one of I1's own decisions: the clinical record is
  `AnyClinicRole` to read and record, `AdminOrDoctor` to delete from.** I1 put fiches de soins, the odontogramme,
  the antécédents and the medical documents wholly behind `AdminOrDoctor` under the heading « clinical authorship
  and clinical free text » — including the `GET`s, which it forked on explicitly and decided the strict way. The
  result was that a secretary opening a patient hit « Vous n'avez pas les droits » on « Dossiers médicaux » and
  « Documents » before touching anything, and a practising dentist's account is that the assistant(e) is who fills
  much of the record in. **The old line was also never true of the code around it**: `PUT /api/patients/{id}` is
  `AnyClinicRole` and writes `Allergies`, `MedicalHistory`, `Notes` and `ImportantNotes`, and `POST /api/patients`
  inserts `PatientMedicalHistory` rows outright — so reception could always type a patient's medical history
  through « Modifier » while being refused a *read* of the same text one tab over. The boundary did not protect
  the data; it chose which door reception had to use. **Record yes, erase no** is the replacement, and the four
  deletes (fiche · document + blob · antécédent médical · antécédent familial) are now the *only* thing gating
  it — `ClinicalRecordAccessTests` states the charter as data and fails on an unclassified new action.
  ⚠️ **Widening the write surface was only safe because two other things already existed**: `AuditSaveChangesInterceptor`
  attributes every mutation (so a secretary-recorded fiche is answerable at `GET /api/audit`), and
  `PractitionerAttribution` puts the caller **last**, so clinical *credit* goes to the visit's dentist or is left
  honestly `null` — never to whoever typed it.
  ⚠️ **And opening document authorship needed a real fix, not a policy edit.** `PractitionerRenderSnapshot` resolved
  the cachet + n° d'ordre CNOMDT from the **caller's** `Doctor` record, so a document authored by anyone without one
  — reception, or an admin who is not a dentist — rendered with *no* practitioner identity, silently, on the class of
  document whose entire purpose is to carry it. `ResolveAsync` now takes an **`IssuingDoctorId`** and resolves
  chosen practitioner → caller → none, tenant-checking the chosen id against the clinic roster (a foreign or stale
  one *falls through*, exactly like `PractitionerAttribution.Resolve`); the editor sends the practitioner the user
  already picked, and the `doctors[0]` fall-back it kept for the four free-form types is gone, since with reception
  authoring it had become the *normal* path rather than a near-unreachable one. **No schema change**: the resolved
  snapshot has always been persisted into the document's `ContentJson`, so the missing piece was a selector on the
  request, not the persisted `DoctorId` the code's own note said it would need. `IssuingDoctorId` is therefore
  deliberately **not** stripped from the render payload the way the four reserved values are — it is a selector
  checked against the caller's own roster, while `doctorCachetKey` is a storage key the unauthenticated
  `PdfGenerationJob` later dereferences.
  ⚠️ **`AnyClinicRole` includes `admin`, and that is why it exists** rather than the spec's `DoctorOrSecretary`:
  `CreateClinicCommand` makes a clinic's creator an **admin** and links the single dentist's `Doctor` record to that
  same account, so in the common Tunisian practice the owner-dentist's role is `admin` — and a literal
  `{doctor, secretary}` policy on the agenda, the patient list or the till would have locked the owner out of their
  own practice, which is strictly worse than the defect being fixed.
  **The audit ledger** is the other half: a `SaveChangesInterceptor` writing one row per mutated **aggregate root**
  (actor, clinic, entity, action, and a compact changed-field summary for updates and deletes), read through
  `GET /api/audit` (`AdminOnly`, paged). Before it there was **no audit trail of any kind** — zero hits for
  `CreatedBy`, `ModifiedBy`, `DeletedBy`, `AuditLog`, `IAuditable`, `SaveChangesInterceptor` — and the only
  attributable actions in the entire product were voiding a payment and voiding an installment (an avoir recorded
  no actor). It is an interceptor rather than `CreatedBy`/`ModifiedBy` on `Entity<TId>` because those would be a
  write-path obligation on 38 entities, any writer that forgets one produces an unattributed row indistinguishable
  from a legitimate one, and they answer nothing about a delete — the question most often asked. See
  `Infrastructure/Persistence/AuditSaveChangesInterceptor.cs` for the forced two-phase shape and its one stated
  imprecision. **Self-registration** no longer mints a live account either: `POST /api/auth/register` creates it
  **pending** an admin's activation, since the only secret was a 6-character clinic code shown on a settings screen
  and known to everyone who ever worked at the practice. And `GET /api/patients/{id}/ai-summary` was **deleted** —
  see the AI-summary bullet below.
- **A reminder queue that cannot starve, and never announces the wrong day (`adoption-qa-l` L3)**: the outbox had
  two individually-defensible decisions that together stopped the whole install sending. A row whose channel was
  disabled or unconfigured was left `Pending` *on purpose* (« so it sends once the operator configures it ») and
  the purge deliberately never deleted a `Pending` row — but the dispatch scan is `Pending && due`, **oldest
  first**, `.Take(50)`, so unsendable rows accumulate at the *front* and past the batch size consume every tick
  for ever. There was no clinic dimension either, so one practice starved the others.
  The fix is a **new non-terminal status, `NotificationStatus.Blocked`**: the row survives and records why
  (both original intentions) while leaving the scan, and `NotificationJob.ReviewBlockedRowsAsync` returns it to
  the queue once the channel is sendable — so the status is not a one-way door. `GetDueForDispatchAsync` adds a
  **per-clinic bound** (clinics served oldest-due-first, capped per tick; the single-clinic install keeps the flat
  query it had), `ReminderScheduler` now checks sendability at **enqueue** on the appointment path too, and
  « N rappels bloqués » is a counter + a filter chip on `/rappels` with the reason on each row.
  ⚠️ Two more things live here. **`ReminderSchedule.ComputeSendTimesUtc` returns every future tier**, not the
  largest — it returned one `DateTime?` while the settings screen invited « Ex. 24, 6 », so for a no-show problem
  the 6 h nudge was the one being discarded — with idempotency on **(appointment, channel, tier)** where the
  tier's identity on the wire *is* its send instant, and a **quiet-hours floor** (21:00→08:00 clinic-local) that
  moves a send **earlier first**: an 08:00 appointment booked ~22 h ahead resolved to 02:00, and 21:00 the evening
  before reaches the patient whereas 08:00 *is* the appointment. And **`GoogleCalendarSyncService` finally has an
  `IReminderScheduler`** — it called `Reschedule()` and committed straight through the repository, so a visit moved
  in Google kept the reminder frozen at the old day; `ReminderMessage.AnnouncesStaleMoment` is the dispatcher-side
  backstop that makes every *future* write-path omission harmless, and it shares its formatter with the scheduler
  that writes the body so the two cannot drift.
- **Backup is automatic, verified, and restorable (`adoption-qa-l` L4)**: the entire protection used to be a button
  someone had to remember, whose documented default destination **failed on a fresh install** (the service threw
  when both the argument and `Backup:DefaultDestination` were empty — and the installer wrote that key as `""`),
  that recorded nothing about when it last ran, and that was never verified readable. Now: an **hourly**
  `BackupJob` (not daily — the hour lives on the clinic, and an hourly check also covers the PC switched off at
  02:00), `pg_restore --list` **verification whose failure fails the backup**, a real install-relative default
  destination with a **same-volume warning**, a **`BackupRuns` ledger** behind « Dernière sauvegarde réussie » +
  `GET /api/backup/history` + an ensure/clear staleness notification, retention that prunes oldest-first and
  **never empties the folder**, a pre-migration backup that **aborts the migration if it fails**, and a
  **`restore-backup` console verb**. See `api/ClinicManagement.API/CLAUDE.md` for the verb's ordering guarantees
  and `packaging/README.md` for the operator view. ⚠️ The installer now writes **two** config files split by
  ownership (`appsettings.Install.json` machine-derived and rewritten · `appsettings.Production.json`
  operator-owned and never truncated) — it used to truncate the operator's file on every upgrade.
- **A cheque has an identity, and it travels with the money (`adoption-qa-l` L8, slice A)**: post-dated cheques are
  ubiquitous in Tunisian practice and `PaymentMethod.Cheque` was a **bare enum value** — `Payment`,
  `InstallmentPayment`, `CreditNote` and `Expense` each carried `Amount`/`Method`/`PaidOn` and nothing else. For money
  *out* the number could go in an expense's description; for money **in** there was no free-text field of any kind, so
  « quel chèque, de quelle banque, encaissable quand ? » had nowhere to live. Both payment ledgers now carry
  `ChequeNumber`/`ChequeBankName`/`ChequeDueDate`, and `Domain/ValueObjects/ChequeDetails.cs` is the **one** guard:
  details on a non-cheque method are refused there, not by a CHECK constraint (a second copy of the rule whose failure
  would be a 500 instead of the French refusal) — so `verify-schema` **verifies** the invariant instead
  (`cheque-details-only-on-cheques`, over both ledgers), while the six columns and two partial indexes are diffed
  against the catalog for free.
  ⚠️ **The load-bearing call site is the devis→facture bridge** (`IssueInvoiceCommand`): it carries an installment
  payment onto the invoice, and a cheque left behind there would vanish from any « chèques à encaisser » view — the
  plan side stops being counted the moment the bridge invoice is issued, so the row that still has to be banked would
  become the one row nothing lists. `InstallmentPayment.ToChequeDetails()` rebuilds it **through** `ChequeDetails.For`,
  re-checking the invariant on the way across rather than trusting it. ⚠️ Two smaller traps: the index filters key on
  `ChequeDueDate IS NOT NULL`, **not** `Method = 1` (equally selective by the invariant, and the enum form would bake
  an ordinal into SQL where no compiler checks it); and `ChequeDueDate` is a **calendar day** sent as a bare
  `YYYY-MM-DD` — `toISOString()` would shift a cheque due on the 1st into the previous month. All three fields stay
  **optional even for a cheque** (refusing money genuinely received to enforce a field is the wrong trade), so a
  cheque with no due date is counted as its own group rather than dropped. Client side: `components/factures/cheque-fields.tsx`
  is the single conditional sub-form and `chequePaymentFields()` the single payload builder — it clears the fields
  when the method is not `Cheque`, which is what makes "the server refuses details on a cash payment" unreachable
  rather than merely unlikely.
- **Data comes back in as CSV, and the dry run is the product (`adoption-qa-l` L5, import half)**: a dentist arriving
  with 3 000 patients in a spreadsheet used to type them in by hand — the spec names that as the single thing that
  stops most switchers. `POST /api/patients/import/preview` → mapping → `POST /api/patients/import`, both
  **`AdminOrDoctor`**, multipart, scoped to patients. `Application/Common/Csv/CsvReader.cs` is the reader half of the
  writer above it, and **nothing about it is symmetrical**: the writer controls its one shape, the reader is handed
  whatever the previous software produced, so the **delimiter** (`;`/`,`/tab, counted on the header record only), the
  **encoding** (invalid UTF-8 falls back to Latin-1 — a BOM-less Excel file on a French Windows is cp1252, and
  decoding it as UTF-8 dies on the first « é ») and the line ending are all *detected*.
  ⚠️ **The preview is a `Query` and the commit a `Command`, and that is not stylistic**: `RealtimeBroadcastBehavior`
  derives its key from the namespace, so a dry run in `Commands` would announce an import that has not happened. The
  same mechanism is why the commit does **not** `Send` a `CreatePatientCommand` per row (3 000 broadcasts, 3 000
  refetches in every open browser) — the *rules* are shared instead of the pipeline, by extracting
  **`PatientFromRequest.Build`** out of `CreatePatientCommandHandler`, which is now its only other caller. That is
  what makes the spec's « reuse `CreatePatientCommand`'s validation rather than a parallel path » literally true.
  ⚠️ Three more decisions worth knowing. (a) **One `SaveChangesAsync` per row**, because « all-or-nothing per *row*,
  never a silent partial commit » is unachievable with one save for the file — one refused row would take the other
  2 999 — with `IUnitOfWork.StopTracking` detaching each committed row so EF does not re-scan 3 000 entries on every
  later save. (b) **Nothing is staged server-side**: both calls carry the file, so a mapping change re-runs the whole
  dry run and the *identical* `PatientImportPlanner` produces both the preview and the commit — a preview built by
  other code is a promise the commit need not keep. (c) **Duplicate matching is deliberately eager** (name+DOB, name
  alone when the row supplies no DOB, or phone through `PhoneNumber.ToE164`) and skips by default, including matches
  against *earlier rows of the same file*: a false positive costs one « Créer quand même » tick, while a false
  negative is permanent — this product has **no patient merge and no soft delete**. Phones are normalised to `+216`
  E.164 on the way in, which the hand-typed write path notably does **not** do (`PhoneNumber`'s ctor only trims); the
  spec names that standing defect and forbids replicating it. The « Sexe » export column now writes « Homme »/« Femme »
  through `PatientGender` (both directions in one file) — it had been emitting the raw `Male` into a French file, one
  column over from a `YesNo` that was translated on purpose.
- **Data leaves the product as CSV (`adoption-qa-l` L5, export half)**: there were **zero** occurrences of `csv`
  anywhere in the repo and zero of « Exporter » in `web/`, so the only way data left was a `pg_dump` readable by
  PostgreSQL tooling and nothing else — the owner could not leave with their own data, or hand their accountant
  anything. `Application/Common/Csv/` is the single authority: **UTF-8 with a BOM** and a **`;`** delimiter (Excel
  on Windows reads BOM-less UTF-8 in the system codepage, and its list separator follows the fr locale — get
  either wrong and the file is mojibake in one column), money through `InvoiceCalculator.RoundMoney` and dates
  through `ClinicClock`. ⚠️ **An export re-sends the screen's own query with `paging: null`**, which the paging
  primitive models as a first-class case — so « honours the current filters, exports the whole filtered set, never
  the current page » is true by construction rather than by discipline. Money exports are `AdminOrDoctor`, matching
  the reads they export. All nine lists carry the button — `/patients`, `/factures`, `/treatment-plans` and `/caisse`
  in their `PageHeader`, and `/creances`, `/stock`, `/lab-orders`, `/appointments` and la caisse's « Dépenses » card
  **beside the filters they export**, because those components own their own filter state and a copy lifted to page
  level would be a second authority on what is on screen. ⚠️ One deliberate superset: the agenda's CSV covers the
  whole window and every statut, since « Terminés »/« Annulés » *reveal* rather than narrow — honouring them would
  make the ordinary export of a past week omit almost every appointment in it, and `Statut` is a column in the file.
- **La caisse says *how* the money came in, and a cheque has somewhere to be chased (`adoption-qa-l` L8 slice B)**:
  `CaisseSummaryDto.CashInByMethod` splits « Encaissé » per `PaymentMethod`, the « extrait » takes a `method` filter,
  and **`GET /api/billing/cheques`** (`AdminOrDoctor`, `/cheques`) lists every cheque the clinic holds across *both*
  payment ledgers, soonest-due first. Before it, four scalars summed across every method meant the owner closing the
  drawer could not tell the notes in it from a post-dated cheque nobody has banked — the one distinction a cash count
  is made against.
  ⚠️ **The breakdown is a `GROUP BY` sibling of the very SUMs that produce `CashIn`**, predicate for predicate — *not*
  a grouping of the statement's rows, which carry voided payments and would make the lines silently disagree with the
  total above them. All four methods are always present in enum order, zeros included: « Espèces 0,000 » on a day of
  cheques is a true statement about the drawer, and an absent row is not a statement at all. ⚠️ The `method` filter is
  applied **after** the running balance, beside the search term and for the identical reason. ⚠️ The cheques list
  applies the **bridged-plan de-dup**, and there it is load-bearing rather than consistency theatre: the devis→facture
  bridge carries a cheque onto the invoice, so without it one physical cheque is listed twice and the duplicate is
  indistinguishable from a second genuine cheque of the same amount from the same bank. ⚠️ **A cheque leaves that list
  only by being voided** — the product records a cheque's *receipt*, never its clearing at the bank — which is why the
  four bucket counts (en retard / bientôt / plus tard / **sans date**) are the headline and the screen says so out
  loud. On the client, the per-method figures **are** the filter's control (`cash-in-by-method.tsx`), the same
  figure-links-to-its-records rule the dashboard follows.
- **A reimbursement estimate now knows what is left (`adoption-qa-l` L10)**: `Domain/Services/CnamPlafond` is the
  single authority on the CNAM **annual ceiling** — the dependants barème, the dedicated soins-dentaires allowance,
  and which act categories are *hors plafond* (prothèse) — and `GET /api/patients/{id}/cnam-ceiling` reports the
  ceiling, what this clinic has consumed of it in the **clinic** year, and what remains. There were zero repo hits for
  `plafond`/`ceiling` before it, so « Remboursement indicatif » told a patient who had exhausted their ceiling in
  March exactly what it told one who had never claimed.
  ⚠️ **Every figure is an estimate for two independent reasons, and both are DTO fields rather than each screen's own
  wording**: `ceilingIsDefault` (the 2024 amounts are two agreeing Tunisian outlets with no official CNAM page
  retrieved, so they are a *default* that `CnamInfo.AnnualCeilingOverride` always beats) and `seesThisClinicOnly`
  (the clinic counts only its own acts, so « reste » is an **upper bound**). ⚠️ Consumption is measured from **issued
  invoices**, because nothing records a BS1 submission with an amount — so the figure lags a bulletin the caisse has
  not paid and leads one it refused. ⚠️ `ComputeCeilingConsumptionAsync` is a member on the existing
  `ICnamBillingCalculator`, not a second calculator, and it applies **no cap**: clamping to what the clinic charged
  would under-report consumption on a discounted invoice and so over-state the ceiling left.
- **An arrêt de travail is printed on the caisse's own form (`adoption-qa-l` L11)**: `CnamArretTravailRenderer` is the
  **second overlay renderer**, on `CnamBs1BulletinRenderer`'s pattern, stamping `DocumentTypes.ArretTravail` onto the
  genuine CNAM **P 061** (`Assets/P61.pdf`). « arrêt de travail » previously appeared *once* in the whole repository —
  as a description string on the generic certificat tile — so a dentist either hand-wrote it or printed a free-text
  certificat the caisse does not accept.
  ⚠️ **`Assets/P61.pdf` is a normalised copy** of the bundled scan with the rotation baked into the content stream as
  A4 landscape, so every coordinate in the renderer matches what a ruler on the printout measures. ⚠️ **Which of the
  three bundled PDFs is current was settled by reading them** (`P61_2024.pdf`; `CMIATMP.pdf` is the AT/MP form, which
  P 061's own header excludes) but **not** by an official publication — recalibration is expected if a caisse uses
  another revision, and **printing onto real paper is still owed**. ⚠️ `ArretTravailValidation` applies the K-series
  lessons from the start (mandatory duration/date, a **chosen** practitioner never `doctors[0]`, one of code
  conventionnel / n° d'ordre), and the **motif is deliberately never printed**: P 061's front carries no diagnosis
  field and is what the patient hands their employer. In the editor **`isOfficialForm`** now names what this document
  and the BS1 share — an iframe PDF preview (so Print goes through the iframe), no practitioner fall-back, a pre-Save
  gate, and **no Word export**, since a `.docx` of a pre-printed form is the letterhead alone.
- **Money and clinical work know who earned them (`adoption-qa-l` L9)**: `Invoice`, `TreatmentPlan` and `DentalRecord`
  gained a nullable **`DoctorId`** with a real FK, `WaitingListEntry.PreferredDoctorId` became one, and
  `Application/Common/PractitionerAttribution` is the single precedence rule that fills them — explicit → the visit's
  practitioner → the caller's own `Doctor` record, each checked against the clinic's roster. `DoctorId` had existed on
  exactly three entities, none of them carrying money, and `Features/Dashboard/` contained **zero** occurrences of
  `Doctor`. A practitioner filter now narrows `/factures` and the dashboard's **Argent** section.
  ⚠️ **The caller is the *last* resort, not the first**: a secretary recording a dentist's work must not credit
  themselves — and in the common single-dentist practice the owner *is* the caller, which is exactly where that
  fall-back is right. ⚠️ **The attribution travels with the money and is never re-derived**: the fiche→facture and
  devis→facture bridges copy the source's practitioner verbatim, because they bill work that already happened and
  re-resolving would credit the *biller*. ⚠️ **Nullable means nullable** — historical rows and visits booked with no
  practitioner have none, so an unattributed row is *excluded* under a filter rather than silently included (two
  dentists' filtered totals must not exceed the clinic's). ⚠️ **The dashboard filter narrows two figures of five, and
  the DTO says so** (`ClinicWideOutgoings`, `CollectedInvoicesOnly`): an expense has no practitioner, so a narrowed
  « Net » would be one dentist's income minus everybody's costs. ⚠️ The migration **nulls orphaned
  `PreferredDoctorId` values before adding its FK** — the column was unconstrained for the product's whole life, and
  `AddForeignKey` over such a row aborts the upgrade after the schema is half-applied. This is **attribution, not
  authorization**: per-practitioner data scoping is deliberately out of scope. `verify-schema` gained
  `practitioner-attribution-backfill`, because a backfill is the one thing invisible to every other layer.
- **Multi-tenancy**: every request is scoped to a clinic. The **authoritative** check is per-request in the handlers — the clinic is resolved from the DB user record (`ICurrentClinicResolver`/`IClinicContext` → DB lookup of the `sub`, not purely from the JWT claim) and each loaded aggregate's `ClinicId` is re-verified. Since `cloud-security-and-tenant-isolation` (PR #11) there is **also** a defense-in-depth backstop: EF Core **global query filters** on ~15 clinic-owned aggregate roots, fed the JWT `clinic_id` via `ICurrentClinicProvider` (fail-open — inactive when no clinic is in scope, so jobs/CLI/auth flows still work). See `Infrastructure/Persistence/ApplicationDbContext.cs`. Tenant-isolation is pinned by `*TenantIsolationTests` in the test project.
- **Pluggable auth (`Auth:Mode` = `Cloud` | `Local`)**: Cloud is the original Auth0 path; **Local** (for offline Windows/LAN installs) issues its own HS256 JWTs against local email+password accounts. Backend seam: `ILocalAuthService`/`LocalAuthService` (+ per-install signing key via `LocalAuthConfig`), a mode-branched JWT setup in `Program.cs`, and `AuthController` (`login`/`setup`/`register`/`mode`/`change-password`). `CreateClinicCommand`/`JoinClinicCommand` branch to a Local path when a `Password` is present. Frontend seam: a single `useSession()` context (`web/lib/auth/session.tsx`) backed by either `CloudSessionProvider` (Auth0) or `LocalSessionProvider` (HttpOnly cookie), gated on `AUTH_MODE`. All Local behavior is additive; the Cloud path is unchanged. Offline admin lockout recovery is a console command (`dotnet run -- reset-admin-password`), not a web endpoint. *All 5 phases of the offline-Windows repackaging are complete — see `features/windows-desktop-app/`.*
- **Local-disk file storage (Phase 2)**: the single `IFileStorage` seam is mode-branched — `LocalDiskFileStorage` (Local, blobs under `FileStorage:BasePath`) vs `MinioFileStorage` (Cloud). Additive; Cloud unchanged.
- **Connectivity awareness (Phase 3, Local mode)**: internet reachability is judged by the **server** (LAN clients may have no egress). `IInternetProbe`/`InternetProbe` (Singleton, cached) backs an anonymous, Local-only `GET /api/connectivity` (404 in Cloud) that the frontend polls via `ConnectivityProvider`/`useConnectivity()`. The two internet-dependent features — AI chat + Google Calendar — visibly disable offline and auto re-enable; appointments not yet pushed to Google show a "non synchronisé" badge + manual "Push to Google" (`AppointmentDto.IsSyncedToGoogle`). Cloud gets a static "online" default and behaves as before.
- **LAN hosting & security gates (Phase 4, Local mode)**: hardens the API for offline-LAN hosting, all additive/gated to Local (Cloud byte-for-byte unchanged). (a) Fail-closed authorization — `AuthorizationPolicies.ConfigurePolicies(options, isLocalMode)` installs a `FallbackPolicy = RequireAuthenticatedUser()` in Local, so anything without an explicit `[AllowAnonymous]` returns 401; the exact allow-list is pinned by `ControllerAuthorizationCoverageTests`. (b) Loopback-only `/hangfire` (`HangfireAuthorizationFilter` + `LocalRequest.IsLoopback`, shared with the `setup` gate / AC-1.2a) — since `cloud-security-and-tenant-isolation` this is loopback-only in **both** modes (Cloud no longer authorizes everyone). (c) Config-driven CORS (`CorsOrigins` — LAN origins via `Cors:AllowedOrigins`), HTTPS cert binding + guarded redirect, and Kestrel bind (`Https:*`/`Hosting:*`) in `Program.cs`. (d) The OAuth callback no longer rewrites appsettings; Google refresh tokens are stored **per-clinic on the `Clinic` entity** (`GoogleRefreshToken`/`GoogleCalendarId`, DB). *(Phase 5 closed the Phase-4 cert-downgrade gap — a set-but-missing `Https:CertPath` now fails startup loud instead of dropping to HTTP. `cloud-security-and-tenant-isolation` then closed the OAuth `state` gap: `connect` mints CSRF `state` into a server cache + HttpOnly double-submit cookie, validated on `callback`; the old singleton `IGoogleTokenStore`/`FileGoogleTokenStore` was retired for per-clinic DB storage.)*
- **Packaging, installers & manual backup (Phase 5, Local mode)**: turns the app into a self-contained offline-LAN Windows product, all additive/gated to Local (Cloud unchanged). (a) **Same-origin front door** — in Local, Kestrel is the *single* browser-facing HTTPS endpoint: `/api/*` runs in-process and a **YARP** catch-all reverse-proxies every other route (pages, `/_next/*`, `/bff/*`) to the co-located Next server on loopback; the web build ships with relative `NEXT_PUBLIC_API_URL=/api`, so TLS terminates once and no server IP is baked in. Frontend BFF auth routes moved `/api/auth/*` → **`/bff/auth/*`** to avoid colliding with the proxied `/api/*`. (b) **Self-generated HTTPS** — `CertificateProvisioner` mints a CA + SAN server cert into `.local/` on first boot (idempotent); HTTP binds loopback-only, HTTPS (5001) is the only LAN port. (c) **Windows service + startup diagnostics** — `UseWindowsService()` auto-start; `StartupDiagnostics` turns DB-down / port-in-use into clear French messages + non-zero exit (console/log/Event Log). (d) **One-click backup** — admin-only `POST /api/backup` → `PgDumpBackupService` (`pg_dump` custom-format dump + file-storage copy to a timestamped folder; fails loud, never a silent partial). (e) **`LocalInstallPaths`** anchors `.local/`, `Files/`, `logs/` to the install dir (a service's CWD is `System32`). (f) **`desktop/`** WebView2 shell + **`packaging/`** publish script and Inno Setup server/client installers (bundled PostgreSQL 16, Node, NSSM, CA-trust import) — **operator-verified (R-1)**, not CI-runnable.
- **Google Calendar sync is asymmetric + per-clinic**: each clinic uses its **own** Google connection (`Clinic.GoogleRefreshToken`/`GoogleCalendarId`) — no shared account. App→Google runs inline on appointment create/update (using the appointment's clinic). Google→App is implemented but has **no scheduled job** — the disabled `GoogleCalendarSyncJob` class + its commented recurring registration were removed as dead scaffolding (`french-localization-and-cleanup`; `Program.cs` keeps only a defensive `RemoveIfExists`). Google→App runs only via the manual `GoogleCalendarController` endpoint (scoped to the caller's clinic).
- **Background jobs**: Hangfire is wired; **two minutely recurring jobs** — **`NotificationJob`** (SMS/WhatsApp appointment-reminder dispatcher) and **`EInvoiceOutboxJob`** (TTN « El Fatoora » e-invoice outbox dispatcher), both connectivity-gated and no-op until configured (a `Reminders` channel + credentials; a clinic enabling e-invoicing) — **plus one daily**, **`StockExpiryJob`** (approaching-expiry stock alerts, `audit-sections-3-to-10`). The daily one is deliberately **not** connectivity-gated: its alert is in-app, so it must work on an offline LAN install. It exists as a *job* rather than a write hook because expiry is crossed by the **passage of time** — the box nobody has touched is exactly the case the alert is for, and no write happens on the day it enters the lead window; a write-only trigger would have been a notification that never fires for its main case. On-demand `PdfGenerationJob` also fires. (`AISummaryJob` was removed in `reliability-and-polish`; the calendar-sync job in `french-localization-and-cleanup`.)
- **Billing / CNAM / treatment plans / e-invoicing (deep, fully-wired subsystems)**: the product's richest area, largely French already. **Billing** — `Invoice`/`InvoiceLine`/`Payment` (notes d'honoraires: draft→issued→paid, VAT + stamp, per-clinic billing settings), `InvoicesController` + `BillingController` (unified per-patient `solde patient`). **Treatment plans / devis** — `TreatmentPlan`/`TreatmentPlanItem` + payment `Installment` schedule (`TreatmentPlansController`), seeded from the odontogram by a **frontend** prefill (there is no backend seed command). Since `treatment-plan-workspace` the loop is **bidirectional**: a plan derives each act's état from the appointments pointing at it and shows the invoice that bills it (`TreatmentPlanWorkflowProjection`, two batched reads per request), has its own workspace route `/treatment-plans/[id]`, orders its acts (`SequenceNumber`), and can be **amended after acceptance** (`RevisionNumber`) instead of cancelled and retyped — unless a non-cancelled invoice already represents it. **CNAM** — `CnamNomenclatureEntry`/`CnamLetterValue`/`DentalActCode` (`CnamNomenclatureController`/`DentalActsController`) back the **BS1 bulletin** reimbursement estimate + overlay renderer (`CnamBs1BulletinRenderer`, Infrastructure). **El Fatoora (TTN) e-invoicing** — invoices queue an e-invoice dispatched by the recurring `EInvoiceOutboxJob`. Also `Medication`/`MedicationActiveIngredient` (ordonnance picker), `ToothState` (odontogram), `DashboardController` (the composed dashboard read — see below). The **CNAM/medication/dental-act catalogs are per-clinic** (each clinic seeded the same defaults via `IClinicCatalogSeeder`, then edits stay private) — despite some stale "global reference data" docstrings still in those entities.
- **Clinical-workflow-depth operational features (built)**: beyond scheduling, the app has an **interactive odontogram** (chart/diagnose per tooth — `OdontogramController`, `ToothState`), **recurring appointment series** (`RecurringAppointment`, above), a **waiting list / salle d'attente** (`WaitingListController`, promote-to-appointment), **lab work orders / bons de prothèse** (`LabOrdersController`, status lifecycle), **patient recall / relance** (`RecallController` + `Clinic.RecallIntervalMonths`, SMS/WhatsApp send), a **caisse / expenses** ledger (`ExpensesController` + `BillingController` caisse summary), and a **post-visit-review** prompt (a `StaffNotification` category deep-linking staff to record a finished visit). Each is clinic-scoped with a matching page under `web/app/`.
- **Dead-code cleanup (`french-localization-and-cleanup`)**: the **domain-events pipeline was removed** — `Domain/Events/*`, `IDomainEvent`, and `AggregateRoot`'s event list were dead (`SaveChangesAsync` never drained events; zero `INotificationHandler`s). Side effects are produced inline post-commit via `INotificationGenerator`/`IReminderScheduler`. *(`RecurringAppointment` is no longer merely "reserved" — the clinical-workflow-depth feature wired it: `AppointmentsController` `GET/POST recurring` + `POST recurring/{id}/cancel`, `RecurringAppointmentRepository`, expanded into `Appointment` rows.)*
- **Clinic-scoped SignalR realtime (built)**: every mutating command auto-broadcasts a `entityChanged` event to the caller's `clinic-{id}` group via the Application `RealtimeBroadcastBehavior` → `IRealtimeNotifier` → API `ClinicHub` (`/hub/clinic`); the resource key is derived from the command's namespace (so new commands broadcast for free). The frontend subscribes with `useClinicRealtime(resource, refetch)` (`web/lib/realtime/`) so a peer's edit live-refreshes appointments, patients, stock, invoices, treatment plans, catalogs, users, notifications, la caisse, and — since `audit-sections-3-to-10` closed audit § 9.1 — the **salle d'attente, lab orders, relances, recurring series, « Mon profil » and the patient's documents tab**. The **dashboard** now subscribes to all nine keys its figures depend on (it watched only four, so the à-traiter counts and la caisse went stale under a peer's edit). Excluded areas: Auth, AI, Backup, Connectivity, and all queries. ⚠️ The two sides are now held together by a **contract test**, not by discipline: `RealtimeResourceResolverTests` reflects over every `IRequest` for the emitted set, **parses `clinic-hub.ts`** for the declared set, and fails unless they are equal **in both directions**. It replaced a hardcoded `[InlineData]` table that stayed green for the whole period five keys (`expenses`, `doctors`, `laborders`, `recall`, `waitinglist`) were broadcast with nothing listening — the derived-vs-listed lesson `verify-schema` also embodies.
- **In-app staff notification center (built)**: a real, clinic-scoped in-app feed — header bell + unread badge → panel (newest-first, per-user read/unread, mark-all-read, deep-links), live over the SignalR `"notifications"` key above. Backed by a **`StaffNotification`** aggregate (one shared row per event) + per-user **`NotificationRead`** markers (no write-time fan-out). Notifications are generated **best-effort, post-commit** by an `INotificationGenerator`/`NotificationGenerator` seam called inline from the appointment/stock command handlers (appointment created/cancelled/rescheduled, ~24h reminder, not-low→low stock crossing) — a generation failure logs at Error but **never** fails/rolls back the core operation. This is **in-app only**; the dormant email/SMS `Notification` entity + `NotificationService` stay untouched. The actor who caused an event is excluded from their own feed.
- **Real outbound SMS/WhatsApp reminders** (feature `sms-whatsapp-reminders`): the previously-dormant `Notification` outbox is now live for **SMS + WhatsApp** appointment reminders — `IReminderChannelSender` (`HttpSmsSender`/`WhatsAppSender`) + `RemindersConfig`/`ReminderSchedule`/`ReminderPhone`, enqueued best-effort post-commit by `IReminderScheduler`/`ReminderScheduler` from the appointment handlers and dispatched by the connectivity-gated minutely `NotificationJob`. Secrets come from env (or per-clinic, encrypted). Per-clinic settings (`ClinicReminderSettings` + `IReminderSettingsProvider`) override the per-install config: channel toggles, sender identity, **gateway/Graph URLs, lead-time tiers and the message wording** (all admin-editable in `reminder-settings.tsx` — `reliability-and-polish`), so a channel can be turned fully on without a server-config edit. The settings GET returns a per-channel `effectiveStatus` (`configured`/`not_configured`) that drives a "sendable vs. warning" badge (a WhatsApp OAuth "Connecté" downgrades to a warning when the resolved settings still can't send), and `GET /api/clinics/reminder-status` surfaces the recent outbox rows (sent/pending/failed + reason).
- **The patient AI summary is gone, and the claim that used to be here was false on both halves (`adoption-qa-i-access-control-and-audit` I4)**: `GET /api/patients/{id}/ai-summary` was documented as "on the patient detail page … connectivity-gated" while having **zero callers** in `web/` (the button was removed and the endpoint outlived it) and **no** `IInternetProbe` gate — so on an offline LAN install it hung ~205 s on a `HttpClient` with no timeout before failing. What it did do was POST a patient's full name, allergies, every medical- and family-history entry, and every dental record — teeth, money and all free-text notes — to `router.huggingface.co`, with no record cap, no consent flag, no audit of which patient was sent, and a class-level `[Authorize]` as its only gate, so any secretary could trigger it. It was **deleted** rather than fixed (endpoint, `GetPatientAiSummaryQuery`, `PatientAiSummaryDto`, `patientsApi.getAiSummary`): keeping it would have required a policy, a probe gate, a timeout, a record cap and an audit row to restore a feature no screen asked for. `IHuggingFaceAIService` stays — the AI **chat** is its live, gated caller. *(The old placeholder `PatientSummaryService`/`IPatientSummaryService` + the disabled `AISummaryJob`, the never-registered `GoogleAIService`/`IGoogleAIService`, and the dormant email `NotificationService`/`INotificationService` were removed as dead code in `reliability-and-polish` — HuggingFace is the sole wired AI backend, and the live outbound reminders go through the `IReminderChannelSender` senders below.)*
- **`ValidationBehavior` is inert**: no FluentValidation validators exist; handlers validate inline and return `Result.Failure`.
- **Frontend data-wiring**: the dashboard, `appointment-list`, the stock feature, and the notification center are all API-wired (`dashboardApi`, `useAppointments`, `stockApi`, `notificationsApi`/`useNotifications`). The header **search** is a live patient lookup (type → results → open the patient). *(The orphan `notifications-list` sample component and the redundant `dental-chart` tooth chart were removed in `reliability-and-polish`; the read-only summary chart now reuses `record-tooth-chart`.)*
- **Security posture (mostly hardened by `cloud-security-and-tenant-isolation`, PR #11)**: committed `api/.../appsettings.json` secrets were **retired** — DB connection string, `GoogleCalendar:ClientSecret`, `HuggingFace:ApiKey`, WhatsApp/SMS/Meta tokens are now empty strings + `// SECRET` comments; supply real values via env / user-secrets / `.local/` / the installer's `appsettings.Production.json`. An empty DB connection string fails startup loud (`return 1`). The Hangfire filter is loopback-only in **both** modes; the Google OAuth `state` is validated; Google refresh tokens live per-clinic in the DB. **Residual (Cloud)**: the authorization `FallbackPolicy` stays null (a Cloud controller without `[Authorize]` is still anonymous — Local fails closed), and a few non-secret real values remain in config (`Auth0:Domain/Audience`, `GoogleCalendar:ClientId`; `Auth0:ManagementApi` still has `YOUR_*` placeholders). Treat with care.

## Root-level setup / reference docs

| File | Topic |
|------|-------|
| `README.md` | (minimal) |
| `AUTH0_SETUP.md` | Auth0 tenant/app configuration |
| `GOOGLE_CALENDAR_SETUP.md` / `_FR.md` | Google Calendar OAuth setup (EN/FR) |
| `GOOGLE_CALENDAR_SYNC_ARCHITECTURE.md` | Calendar sync design |
| `SYNC_TESTING_GUIDE.md` | How to test calendar sync |
| `HUGGING_FACE_SETUP.md` | AI provider setup (HuggingFace — the sole wired AI backend) |
| `api/CLINIC_MANAGEMENT_FLOW.md`, `MULTI_CLINIC_SETUP.md`, `ROLE_ASSIGNMENT_IMPLEMENTATION.md`, `ENTITY_UPDATE_GUIDE.md`, `IMPLEMENTATION_SUMMARY.md` | Backend feature/process docs |
| `packaging/README.md` | Local/offline-LAN operator guide (Phase 5): publish + installers, bundled PostgreSQL, backup/restore, admin recovery, per-AC verification checklist |

> When code changes, update the nearest `CLAUDE.md` so this map stays accurate.
