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
| Auth | **Pluggable** by `Auth:Mode`: **Cloud** = Auth0 (JWT bearer); **Local** = self-issued email+password accounts (an offline LAN install **and** the hosted multi-tenant backend). Clinic membership resolved server-side. ⚠️ `Auth:Mode` answers *who issues tokens* only — every other deployment difference is a named capability on `Deployment:Profile`. | both |
| External | Google Calendar (two-way sync), SMS/WhatsApp reminders, Meta WhatsApp onboarding | `api/...Infrastructure/Services` |

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
├── mobile/                       Native shells rendering the server's own web bundle → CLAUDE.md
│                                   shared/bridge.md = THE `window.__clinicShell` contract
│                                   android/         = Kotlin + WebView (built; Gradle, not CI-runnable)
│                                   ios/             = Swift + WKWebView ⚠️ WRITTEN, NEVER COMPILED — no Mac here,
│                                                      so .github/workflows/ios-shell.yml (free macos-latest) is
│                                                      the first compiler it will meet. Read mobile/ios/README.md
├── packaging/                    Local/offline-LAN publish + installers (PowerShell + Inno Setup) → CLAUDE.md (+ README.md operator guide)
├── console/                      The VENDOR's private back-office (Next 15) — `platform-console`, HostedMultiTenant
│                                   only, served on its own loopback-published Caddy site behind an SSH tunnel.
│                                   Contains NO clinic surfaces: that is FR-2, not a packaging choice.
├── deploy/                       Hosted deployments (Docker + Caddy) → README.md operator guide
│                                   docker-compose.prod.yml   = CloudBrowser  (Auth0)
│                                   docker-compose.hosted.yml = HostedMultiTenant (own accounts) — `extends` prod's infra
├── backend/                      EMPTY (only .idea/) — ignore
├── .github/workflows/            ci.yml = the api · web · desktop · android gate (see below)
│                                   ios-shell.yml = the iOS shell's only compiler, path-filtered (billed macOS runner)
├── docker-compose.yml            postgres (5432) + minio (9000 API / 9001 console) — LOCAL DEV only
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

- **There is a CI gate now, and before it there was none for `api/` or `web/`**: `.github/workflows/ci.yml` runs
  five independent jobs on every push (the fifth, **console**, is `platform-console`'s and runs `web`'s exact gate
  against the second Next application) — **api** (`dotnet build` + the unit suite), **web** (`tsc --noEmit` +
  `npm run check:responsive` + `npm run build`, which *is* that project's whole gate since it has no test runner
  and no working ESLint), **desktop** (`dotnet build` on `windows-latest`; WPF/net8.0-windows cannot build
  elsewhere and cannot be *run* there either) and **android** (`./gradlew lint assembleDebug`; Lint runs with
  `warningsAsErrors` and is the module's only static gate). Four jobs rather than one matrix because they share no
  toolchain and a failure should name the stack without reading a log.
  ⚠️ **The api job matters more than it looks**: the unit suite is the *only* automated check the backend has —
  nothing in it touches a database — and it was previously run by hand on a Windows machine where Smart App
  Control intermittently refuses freshly-built test assemblies. (Building to a path **outside the repo** avoids
  that locally: `BaseOutputPath=<temp> dotnet test …` runs clean where the in-repo `bin/` does not. The same trick
  is what lets `dotnet ef migrations add` work while the dev API is running and holding `api/**/bin`.)
  ⚠️ **`mobile/ios/` is deliberately not in `ci.yml`** — it needs a macOS runner, billed on private repositories —
  so it keeps its own path-filtered workflow and its own decision about when to spend that.

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
- **What may be uploaded has one authority, and the browser is told rather than trusted (`patient-file-uploads`)**:
  `Application/Common/Files/` is the single catalog — entries keyed on **extension**, never on the declared
  content type (Windows registers none for `.stl`, `.dcm`, `.ply` or `.obj`, so a MIME allow-list could not admit
  a single STL however many types were added to it), each carrying its own cap, its French label, whether a
  browser can paint it, and a signature rule that is `Required` / `Advisory` / `None(reason)` **with an offset**,
  which is what makes DICOM's `DICM` at byte 128 expressible. All six upload doors name a profile;
  `FileContentValidation` and `UpdateDoctorProfileCommand`'s three private magic-byte copies are deleted.
  ⚠️ **The reported bug was two defects stacked**: the `.txt`-renamed-to-`.pdf` refusal was *correct*, and
  `web/lib/api/patient-files.ts` read `errorData.message` while the backend sends `{ error }` — so the French
  explanation was replaced by an English « HTTP 400: Bad Request ». Fixing it removed the last raw `fetch` from
  `lib/api/`.
  ⚠️ **`GET /api/meta/upload-policy` serves the policy the picker renders** — the `accept` string, the per-format
  caps, and the server's *own* refusal sentences — so the instant client-side refusal cannot word things
  differently from the server that re-checks it, and a widened catalog cannot leave a stale constant hiding
  formats the server would take. A failed probe leaves the picker fully open: the server is the guard, the
  pre-check is a courtesy.
  ⚠️ **A file can now be renamed, described and moved** (`PUT /api/patients/{id}/files/{fileId}`), the first
  caller of four entity methods that had shipped with none. `PatientFile.Rename` takes a **base** name and
  recomposes from the *stored* extension, so changing a file's format through the API is unrepresentable rather
  than merely refused — and the editor shows the extension as a fixed suffix for the same reason. Both PUTs are
  `AnyClinicRole`: **record yes, erase no**, the same line the clinical record is on.

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
- **Re-saving a fiche tops its note up, and la caisse's day is Tunisian (`adoption-gaps-remediation` Part 2)**: the
  bullet above shipped with two money leaks behind it. **(a)** The already-billed case was a flat refusal, so raising
  « Montant payé » on a re-save — « le patient a fini de payer », the most ordinary edit there is — put nothing in the
  till and said « enregistrée » in green. `CreateInvoiceFromDentalRecordCommand` is now **`BillDentalRecordCommand`**
  returning a typed **`DentalRecordBillingResult`**: a higher amount records the *difference* as an additional payment
  on the **same** note (`ToppedUp`), and lowering it, changing the acts after issue, or a note that is annulée /
  entièrement créditée are refusals carrying a `Result.Code`. **(b)** The payment was booked as a hard-coded `Cash`,
  so a séance settled by cheque never reached « Chèques à encaisser » and was counted under « dont espèces »; the
  fiche now carries `PaymentMethod` + the three cheque fields, through the **existing** `ChequeDetails.For`.
  ⚠️ **The load-bearing refusal is in `UpdateDentalRecordCommand`, PRE-commit — not in the billing command.**
  `DentalRecordAutoBilling` runs post-commit by design, so a refusal raised there arrives *after* the lowered amount
  or the changed act list has been saved: the user reads a French message and the edit sticks anyway, leaving the
  fiche permanently disagreeing with its own note. « Refusé » has to mean the save did not happen. Both doors call
  the one **`DentalRecordBillingGuard`** and share **`DentalRecordBillingRefusals`**' wording, so guard and backstop
  cannot drift. ⚠️ The `Contains("déjà facturée")` substring match is **deleted** — recovering an outcome by
  matching French prose meant rewording a sentence silently changed behaviour, and `AlreadyBilled` is now a
  *success* with an **informational** toast rather than a plain green one.
  ⚠️ **La caisse takes bare `YYYY-MM-DD` day keys now** (`Features/Billing/CaissePeriod`, the single authority on
  period bounds for the summary, the extrait, both exports and the dépenses list). The browser used to compose the
  instants itself — `new Date(day + "T00:00:00").toISOString()`, midnight in the **workstation's** timezone — so on a
  machine set to anything but UTC+1 « la caisse du 3 août » covered a window offset by hours from the Tunisian day.
  The clinic's day is a fact about the clinic; the browser is the one participant that cannot know it.
  ⚠️ And `ExpenseRepository`'s upper bound went `< to` → **`<= to`**: its three sibling ledgers are inclusive, so an
  expense dated on the window's last tick fell out of the extrait while the payments beside it stayed in, breaking
  `Σ movements == cashIn − refunds − cashOut` at a period boundary. Also here: **an échéancier payment can finally be
  voided from the plan workspace** — the endpoint had shipped tested and with no caller at all, so a mis-keyed
  installment payment was permanent while the identical mistake on an invoice payment was two clicks from being
  corrected. And the « cancelling the bridge hands the money back » comment, which appeared in **three** places, is
  corrected in all three: an invoice holding a non-voided payment **cannot be cancelled**, so the avoir is the only
  route.
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
- **Backup works out of the box, or says whose job it is (`backup-works-everywhere`)**: the tools are **discovered**,
  not configured — `Infrastructure/Services/PostgresToolLocator` searches explicit config → beside the app → **`PATH`**
  → the well-known per-version roots (newest first), and `api/Dockerfile` now installs `postgresql-client-16`.
  ⚠️ **The defect this closes was invisible at every layer**: both tools were reached through `Backup:PgDumpPath`,
  written by exactly **one** of the four ways this product is deployed (the Windows installer, which bundles its own
  PostgreSQL). Everywhere else it is the `""` that ships in `appsettings.json`, so « Sauvegarder maintenant », the
  hourly `BackupJob`, the pre-migration safety dump **and** the `restore-backup` verb all answered « L'outil pg_dump
  est introuvable » — for the life of the product, on every Docker deployment. `RestoreBackupCommand` held a second
  **copy** of the resolution rule under a docstring claiming to be « one rule »: the `fixes-dont-propagate` shape
  again, and both copies were broken the same way.
  ⚠️ **On the two hosted kinds the answer is not « make it work », it is « say whose job it is »**: the 17th
  capability **`BacksUpItsOwnData`** (`SelfHostedLan` only) stops the hourly job being registered and 404s the two
  write endpoints, because `deploy/`'s `backup` sidecar already dumps the database *and* the object store
  **off-server** on a schedule, and — the load-bearing half — `pg_dump` takes `--dbname` and has **no tenant
  predicate**, so on a shared database « Dr X clicks Sauvegarder » would dump every other practice's patients.
  Nothing today could exfiltrate it (no download endpoint), which is exactly why it is a capability and not a
  comment: the day somebody adds « télécharger la sauvegarde », the leak arrives with it. **A clinic still takes its
  own data out** through the per-clinic CSV exports and PDFs, which go through the tenant filter.
  ⚠️ `GET /api/backup/history` **still answers there**, reporting `managedByHost`, and the card says « Sauvegardes
  gérées par l'hébergeur » — it is the read that *explains* the absent button, and it deliberately quotes **no date**
  (the sidecar runs in another container and this application cannot observe it, so a « dernière sauvegarde » here
  would be invented).
  ⚠️ **And `DirectoryAclHardener` was locking the backup out of its own folder.** It granted three well-known SIDs
  then ran `/inheritance:r` — which is *also* where the running account's access comes from unless it is one of those
  three, so under an unelevated or de-privileged account a **successful** hardening broke the dump written
  immediately afterwards, **and** the AC-14.4 cleanup was refused for the same reason, leaving an unreadable,
  undeletable `clinic-backup-*` folder with only a logged warning. Three were found in a real destination — where,
  being `clinic-backup-*`-named and oldest, they permanently consumed `PruneOldBackupsAsync`'s deletion budget, so
  **retention had silently stopped pruning anything**. The account the process runs as is now granted too;
  `Users`/`Everyone` are excluded from that grant explicitly, so the policy is true by construction.
- **A cabinet takes its whole record out, and can put it back (`clinic-data-archive-and-restore`)**:
  `GET /api/backup/archive` streams a **per-clinic `.zip`** — a manifest, one JSON file per table, and the blobs —
  and `POST /api/backup/archive/restore` puts the missing rows back. `POST /api/platform/clinics/restore` is the
  vendor's door, re-creating a cabinet that no longer exists (its own accounts included).
  ⚠️ **This is what `pg_dump` structurally cannot be on a shared database**: that tool takes `--dbname` and has no
  tenant predicate, which is exactly why `BacksUpItsOwnData` turns it off on the two hosted kinds. The archive goes
  through the same tenant filter as every CSV export, so it is offered on **all three** profiles — and on
  `SelfHostedLan` it is additionally a *portable* copy the machine-level dump beside it is not.
  ⚠️ **The restore is additive and keyed on the original ids**, which is what makes total loss and partial loss the
  **same operation** — total loss is the case where every row is a gap. A row still present is left alone and
  counted « déjà présent »; a row that **differs** is **skipped**, never overwritten, and counted apart, so work
  done after the archive survives putting the archive back; a row that is gone is re-inserted **with its own id and
  its own document number**. Money documents are safe for that last reason: the gapless `AAAA-NNNN` sequences break
  only when a new number is minted, and nothing here mints one. A second restore is a no-op.
  ⚠️ **A 45-finding review then corrected the feature in place, and five of its fixes were data integrity rather
  than tidying.** (a) `ArchivedProperties` excluded `ValueGenerated.OnAdd`, which EF sets for **every**
  `HasDefaultValue` column *and* for a `Guid` key whose configuration omits `ValueGeneratedNever()` — so a **voided
  payment restored as live money**, archived patients un-archived, every devis lost its act ordering, and the three
  configurations without that call lost their **primary key**, making every ordonnance, certificat and antécédent
  médical unrestorable while the manifest declared their row counts. Fixing the predicate was half: EF also omits a
  store-generated column equal to the property's *sentinel*, so `ApplicationDbContext` now aligns each sentinel
  with the column's own default — model-only, no migration, and it closes the same trap for every insert in the
  product. (b) **Only `Child` tables were ordered**; the directly-owned ones were appended in the model's
  enumeration order, so on the total-loss case the feature exists for, `DentalRecord` reached the database before
  `Patient` and the restore died part way. (c) **Neither door had a transaction**, so that death committed tables
  1..*n*−1 and surfaced as a generic 500 — while the spec's own « Aucune donnée n'a été modifiée » implies it
  cannot happen. (d) **The console path validated the archive's `Clinic` row *after* the commit**, and the
  live-cabinet guard keys on the manifest's *claim* rather than on the row that lands: a hand-edited manifest left
  a practice's records committed under an id nothing points at, with no administrator, no entitlement and every
  retry answered `409 clinic_exists`. (e) **Presence was decided on the primary key alone**, so a re-minted invoice
  number met the unique index as an unhandled `DbUpdateException` instead of AC-3's « sans gap ni collision ».
  ⚠️ **Four tenancy holes closed with them**, each needing no knowledge of another tenant beyond a GUID: a crafted
  `data/Clinic.json` inserted **unbounded phantom cabinets** (`Clinic`'s identity is `Id`, which `StageInsert` does
  not re-stamp); a `Child` row's parent FK was written verbatim, and twelve archived tables carry no `ClinicId` of
  their own, so a `Payment` could be inserted against **another practice's invoice**; blob keys were taken from
  every row the file named rather than from the rows actually inserted, so `clinics/<victim>/…` created objects
  inside that tenant's prefix; and `RowsMatch` over an `IgnoreQueryFilters` probe was a **confirm-or-deny oracle**
  on arbitrary column values platform-wide. `ClinicArchiveLimits` closes the decompression bomb, and the console
  door — which had no size gate at all — shares the cabinet door's.
  ⚠️ **`restore|` was a write-only marker.** `AsRestore()` *prepends*, so it matched neither `AuditLabels.Actor`'s
  `job|` test (a restore rendered as the named admin's own e-mail — verbatim the outcome its docstring says it
  prevents, and on the console path an outside address shown to the practice as a colleague) nor
  `PlatformCounterPass`'s `job|`/`console|` exclusions, so the vendor restoring a dead cabinet made it the
  portfolio's **most active practice** the next morning. Both read the prefix now. And restored **children** left
  no ledger row at all — the interceptor writes one per aggregate *root* — so four thousand re-inserted `Payment`
  rows put money back in la caisse with nothing in « Journal d'activité »; the restorer emits a summary row per
  table, inside the same transaction. The archive's own uploaded warnings no longer reach the report either: they
  were attacker-authored prose rendered on the vendor's console as the server's own.
  ⚠️ **What is still owed is a real database.** Six of the guards above are SQL, and nothing in `UnitTests` touches
  a database — `follow-up/archive-restore-real-database-checks.md` chooses a `restore-dry-run` console verb on
  `verify-schema`'s precedent and says why not Testcontainers.
  ⚠️ **Nothing goes through a domain constructor, and it cannot.** Every PK is a GUID minted *inside* the
  constructor and half the timestamps are stamped there from `DateTime.UtcNow`, so building entities the ordinary
  way would give every restored row a new identity and today's date. `Infrastructure/Persistence/ClinicArchiveStore`
  materialises rows as **property bags driven by the EF model** instead — which also means a **new column is
  archived the day it is written**, where ~35 hand-written DTOs would be ~35 second definitions of what a row is.
  ⚠️ **The entity set is derived, not listed** (`ClinicArchiveScope`): every non-owned table with a path to a clinic,
  minus a declared exclusion set, ordered **parents before children** by walking the required FKs to a fixpoint. The
  three scopes are `Self` (the `Clinic` row, matched on its own PK — it has no `ClinicId` because it *is* the
  clinic), `Direct` (its own `ClinicId`) and `Child` (through the parent's ids). A table with none is **reported**,
  never silently dropped.
  ⚠️ **What is excluded is each a decision**: the vendor's entitlement (a cabinet could otherwise restore its own
  cover from a file it controls — FR-2), the three outboxes (a re-inserted due row would **send SMS reminders about
  visits that already happened**), the feed, the push registry, the backup ledger, the audit ledger, `User`
  (credentials do not travel in a file on a laptop), `ClinicReminderSettings` (its secrets decrypt under a key ring
  the archive does not carry, so they would restore as silently « non configuré ») and the console's own tables.
  `Clinic.GoogleRefreshToken`/`GoogleCalendarId` are **nulled** rather than the row excluded.
  ⚠️ **`IFileStorage` gained `RestoreAtKeyAsync`, deliberately not a third `UploadAsync` overload.** US-5's
  guarantee — « an unprefixed key is not something a caller can write » — is held by `ClinicStorageKeyTests`
  reflecting over every `UploadAsync` for a `Guid`, so a third overload without one would restore that defect in
  silence. This mints no key: it names the key a row **already** holds, which may legitimately be a flat pre-US-5
  one (EC-4) that composing would move out from under its own row. Existing bytes are left alone, the blob half of
  the additive rule.
  ⚠️ **AC-9's actor is a decoration, not a fourth kind.** `AuditActor.AsRestore()` wraps whoever is in scope as
  `restore|{id}`, so « qui a restauré ? » stays answerable while « ces trois mille fiches ont-elles été saisies ? »
  answers no. It cannot be `RunAs`, which is deliberately ignored while a real user is in scope — and a restore
  always has one.
  ⚠️ **The console path restores the `Clinic` row rather than provisioning a cabinet.** Provisioning first would
  make the archive's own row « présent mais différent », i.e. skipped — the practice back with its patients and its
  money but a blank name, no billing settings and no working hours. Only what an archive deliberately does not
  carry is created after: the admin (one-time password, shown once) and the entitlement, through the companion's own
  `LocalClinicProvisioning.StageEntitlementAsync`. A cabinet that is **still live** is a **409 `clinic_exists`** —
  its own admin can restore it themselves, and the vendor minting a second administrator into a working practice is
  the wrong move whatever the archive says.
  ⚠️ Both cabinet endpoints carry **`[AllowsWithoutSubscription]`** (AC-8, the AC-4.2 argument), and the download is
  a GET the gate never inspects anyway. **The archive is not encrypted**, and the card says so in French in the same
  box as the button — it is a complete copy of the practice's medical records, and that is a separate decision with
  its own key-management question rather than a password box that protects nothing.
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
- **A cabinet's right to record work is a dated entitlement (`clinic-subscription`, all 7 parts)**: on the
  hosted deployment a clinic gets **30 free days**, and past its date it becomes **read-only** — every read, every
  CSV export and every PDF keep working, and only writes are refused. Part A ships the foundation: one
  **`ClinicSubscription`** per clinic whose `EndsOn` is a full re-fold of an append-only, cancellable
  **`SubscriptionPeriod`** ledger, the 16th `DeploymentProfile` capability **`RequiresSubscription`**
  (`HostedMultiTenant` only, decided by the **kind** and by nothing an operator can set — AC-7.3), and one migration
  that grandfathers every pre-existing cabinet **open-ended** so no clinic anywhere can be refused for at least 30
  days after deployment. **Part B ships the enforcement**: `API/Middleware/SubscriptionGateMiddleware` refuses every
  non-GET request under `/api` with **402** + a code + a French sentence naming the date, unless the endpoint carries
  **`[AllowsWithoutSubscription("<reason>")]`** — see `api/ClinicManagement.API/CLAUDE.md` for the exempt set, the
  ordering rationale and the two derived guards. **Part C ships the visibility**: `GET /api/subscription`
  (`AnyClinicRole`) + `GET /api/subscription/history` (`AdminOnly`), the **« Abonnement » screen** at
  `web/app/abonnement/`, and `requiresSubscription` on `GET /api/auth/mode`. **Part D ships the client half**: the three
  402 codes + `onSubscriptionRequired` in `web/lib/api/client.ts`, **`SubscriptionProvider`** owning FR-15's three
  re-read triggers, the **`SubscriptionBanner`** on every screen, and AC-1.3's trial sentence — served from
  `Subscription:TrialDays` as `trialDays` on `GET /api/auth/mode`, never a literal. **Part E ships the warnings**:
  the daily **`SubscriptionWarningJob`** writes one in-app `StaffNotification` per threshold crossed — **7, 3, 1 and
  0 days** out, four genuinely new unread rows deduped on the new `StaffNotification.SubscriptionThresholdDays`
  column, deep-linking to « Abonnement » and **never** reaching a locked phone (AC-3.6). **Part F ships the vendor's side**: three commands
  (`Grant` / `Cancel` / `SetSuspension`) reached only by **five console verbs** — `subscription-grant`, `-cancel`,
  `-suspend`, `-unsuspend`, `-report` — plus `SubscriptionReportService`. **Part G ships the background half**: SMS,
  WhatsApp and OS push all stop, and a queued row is **parked with a stated reason** rather than sent or discarded, so
  extending the entitlement before the visit still gets the reminder out (EC-7).
  ⚠️ **Part G's load-bearing half is the *un-park*, not the park.** Both outboxes already had a non-terminal
  `Blocked` status that survives the purge and carries a French sentence — but the pass that returns rows to the queue
  asks only whether the **channel** can send (is there a sender · is it enabled for this clinic · are its credentials
  present), and a row parked for expiry passes all three, so it would be released and dispatched **within a minute**
  on a cabinet that has not paid. Hence the machine-readable **`OutboxBlockReason`** beside the prose (recovering an
  outcome by matching French text is the defect this repo deleted in `adoption-gaps-remediation`) and hence the two
  halves shipping in **one commit**. One **`OutboxSubscriptionGate`** is consulted from all four places — dispatch and
  review, in each queue — rather than a condition written twice per queue, and it is asked for *every* parked row so a
  channel-parked row is not released into a queue about to park it again.
  ⚠️ **A cabinet with no entitlement row keeps sending, unlike at the HTTP gate.** Fail-closed is right there — a
  missing row must not become a way to write for ever — but nothing in the outbox is an authorization decision: the
  work was recorded legitimately while the cabinet could write, and silencing a practice's reminders over our own
  bookkeeping fault is invisible to the practice and unfixable by it. That fault is surfaced where it can be acted on
  (`verify-schema`'s `every-clinic-has-an-entitlement`, `subscription-report`).
  ⚠️ **The scheduled backup and the daily stock-expiry alert are untouched, deliberately** (FR-8): an unbacked-up
  medical record is a liability regardless of who has paid, and it is the one consequence paying late cannot undo. The
  **manual** backup carries `[AllowsWithoutSubscription]` for the same reason — refusing it while the automatic one
  runs would be incoherent. And **nothing is ever deleted** however long a cabinet stays expired (FR-14): both purges
  drop terminal rows only, so `Blocked` was out of scope the moment it existed.
  ⚠️ **Part E's four rows are the opposite of the two ensure/clear alerts beside them, and deliberately.**
  `StockExpiringSoon` and `BackupStale` keep **one** row and reword it; rewording **does not clear who has read it**,
  so once the owner has read « 7 jours » the « 3 jours », « 1 jour » and « dernier jour » restatements would stay read
  and never badge the bell again — AC-3.4's last three warnings invisible to exactly the person paying attention.
  Hence a real dedupe **column** rather than a message prefix, and hence the wording being derived from the
  **threshold** and not from the live countdown: a message rebuilt from « days remaining » differs every day, so the
  ensure would restate and make every open browser refetch on every daily pass.
  ⚠️ **Two states are left exactly as they are.** A **suspended** cabinet is not warned (`SubscriptionStateReader`
  surfaces no countdown for one — EC-11 — and « se termine dans 3 jours » sends a practice suspended for another
  reason to pay for something that will not unblock it), and an **expired** one is neither warned again nor has its
  rows **cleared**: it is now meeting a refused save, and those four rows are what explain it. Only an extension past
  the window withdraws them — which is what **re-arms** the thresholds, so a cabinet that renews and later approaches
  expiry again is warned all four times again (FR-5).
  ⚠️ **The job takes « today » as a parameter** (the Hangfire entry point resolves `ClinicClock.ClinicToday()` and
  calls it), for the reason `SubscriptionStateReader` does: the four thresholds and the midnight they turn on are
  otherwise untestable, and midnight is the only boundary that matters for a date that arrives by itself.
  ⚠️ **The banner mounts in `AppShell`, not in `app/layout.tsx`** where the plan put it: `AppShell` is `flex h-dvh`,
  so a strip above it makes the document taller than the viewport — the page scrolls as a whole and the phone's
  bottom bar goes off screen, which also makes the spec's « ≤ 15 % of a 380 px landscape viewport » budget
  unmeetable. As a flex sibling of `<main>` it costs no height maths, exactly as `BottomNav` already does. It is
  also what makes « no banner on `/login` or `/signup` » **structural**: the six routes that render no shell are
  precisely those two plus `/setup`, `/join`, `/change-password` and `/signup/verifier`.
  ⚠️ **The per-day dismissal is keyed on the server's own `endsOn|daysRemaining` pair, never on a date the browser
  computes.** « The next clinic day » is a fact about Tunis, and a workstation on any other timezone would bring the
  banner back hours early or late — the defect `todayLocalIso()` exists to prevent one layer over. `daysRemaining`
  decrements at Tunisian midnight, so the pair changes exactly when it should and needs no clock at all.
  ⚠️ **The 402 hook is the one that changes nothing about the failing call.** Unlike 426 (`<ClientVersionGate>` takes
  the screen) and `must_change_password` (routed, and its English message replaced), a subscription refusal carries
  the gate's own French sentence naming the date, so it travels on verbatim to `showErrorToast` and the form stays
  open with everything typed still in it (AC-4.6). It must never touch `handleRequest`'s one-shot 401 retry — the
  account is fine, and the refusal never signs anybody out (AC-4.5).
  ⚠️ **Part C's interim rail row is closed here**: `buildConfigItems` now takes `showSubscription`, fed from the
  provider, so `SelfHostedLan` and `CloudBrowser` show no « Abonnement » row at all (AC-7.1/7.2). `lib/zones.ts`
  keeps the full set — it builds the route→icon map and needs every destination that can render — which is why the
  parameter defaults to *showing* the row.
  ⚠️ **« Abonnement » is reachable by a secretary, and that is a deliberate exception** to the product's rule that a
  secretary sees no clinic-wide money screen (AC-2.2): the amounts are what the practice owes its software *vendor*,
  none of it appears in la caisse or a patient's balance (FR-2), and the person who meets the refused save chairside is
  usually not the person who pays. What stays `AdminOnly` is the payment **history**, not the screen.
  ⚠️ **`GET /api/subscription` reads the ledger; the gate deliberately does not.** The entitlement row carries one date
  and no memory of where it came from, so « is the cover in force the free **trial**? » needs the fold — which is
  exactly why `SubscriptionStateReader.Read` takes `isTrial` as a parameter. The gate stays one indexed row.
  ⚠️ **`Subscriptions` is on `RealtimeResourceResolver.ExcludedAreas`** (FR-15): the state is learned by a **re-read**,
  never a broadcast, because neither moment that changes it can push one — a vendor grant runs in a separate process
  with no caller's token to derive a clinic from, and an entitlement ending at midnight has no actor at all.
  ⚠️ **The vendor's verbs are verbs and not endpoints, and FR-6 is held by a derived guard.** A cabinet able to
  extend its own entitlement over HTTP would not have one, so no controller references the three commands —
  `SubscriptionVendorCommandReachabilityTests` asserts that over the commands it finds by *reflection*, and also
  that every verb is actually dispatched by `Program.cs`, since a missing branch boots the **web host** instead and
  reads to an operator as « the command did nothing ». All five gate on `MaintenanceDatabase.HasConnectionString`
  rather than on a capability (amendment M3), and each declares its own tenant scope — `UseClinic(id)` for the four
  that act on one cabinet, `UseSystemWide` for the report.
  ⚠️ **EC-5's race is resolved by a bounded re-fold retry, not by surfacing the 409.** « Two simultaneous grants both
  land and are both kept », and yet `ClinicSubscription.Version` is mapped onto `xmin`, so the second writer's UPDATE
  matches nothing and raises `ConflictException`. `SubscriptionRefold` retries the whole fold up to five times
  (`IssueInvoiceCommand`'s precedent) — correct **only because `EndsOn` is derived**, so whoever saves last computes
  the same date from every entry. The suspension command deliberately does *not* use it: it touches no ledger, so a
  lost update there is an ordinary conflict and 409 is right.
  ⚠️ **`subscription-report --clinic <id|email>` is the only thing in the product that prints a period id**, and
  `subscription-cancel` takes one — without that mode a mis-keyed grant older than the current session would be
  uncorrectable. It shares `reconcile-money`'s exit codes; a **suspended** cabinet is listed but is not a *finding*
  (an alarm that is always on is one nobody reads) while a cabinet with **no entitlement** is, because that is
  FR-13's failure state rather than a state anyone chose.
  ⚠️ **A granted cabinet keeps its four expiry notifications for up to 24 h**, deliberately: the banner clears within
  one 5-minute re-read because it reads the entitlement directly (AC-5.8), and the bell rows are withdrawn by Part
  E's daily pass. Clearing them from the grant would force every verb to register a no-op `IRealtimeNotifier`, since
  `INotificationGenerator`'s only implementation of that seam is the API's SignalR notifier.
  ⚠️ **A 52-finding review pass then corrected the feature in place, and four of its fixes changed behaviour rather
  than tidying it.** (a) **The fold was wrong in two of its three branches**: a month duration clamped on the
  *exclusive* cursor, so 31 Jan + 1 month ended **27** Feb where the spec, the Domain guide and the test's own comment
  all say 28 — a day lost unpredictably, in the vendor's favour, with the test's expectations pinning the defect; and
  an `--until` entry set the cursor **unconditionally**, so a mistyped year silently revoked months a cabinet had paid
  for, with a success message. A recorded entry can now only ever *extend* cover, and `SubscriptionPeriod.Create`
  refuses an end date before its own recorded day or beyond five years. (b) **Part G's un-park re-armed the very
  starvation it was invented to fix**: the review scan had no per-clinic bound, and an expired cabinet's rows never
  clear and are never purged, so past the batch size they occupied every review tick for ever and another practice's
  channel-parked rows were never released. Both blocked scans now carry the due scan's fair-share loop. (c) **The gate
  answered 402 to an unroutable `/api` path** — `GetEndpoint()?.…is null` read « nothing matched » as « declared no
  exemption » — so a mistyped URL was answered with the loudest thing it can say. (d) **`users/{id}/reset-password` is
  now exempt** (a forgotten password otherwise costs an expired cabinet the *reads* AC-4.1/4.2 guarantee, with no
  other recovery on a hosted deployment) while **`users/{id}/status` is exempt in one direction only** — its recorded
  reason is offboarding, but the action also re-activates, which the handler now refuses.
  ⚠️ **One documented decision was deliberately reversed**: a warning row naming a **superseded** end date is now
  withdrawn rather than kept as feed history, because the bell otherwise showed « 1 jour … 21/08 » beside
  « 3 jours … 22/08 » — two live claims about one date. A pure countdown escalation with the date unchanged still
  keeps both rows, and the new row is still genuinely new rather than a rewrite (which would carry read markers).
  ⚠️ **`deploy/` now carries the ten `Subscription__*` variables**: on the only kind that enforces, « Abonnement » was
  unconfigurable, so the screen a 402 points a chairside user at rendered « Aucun tarif n'est publié ». The five
  vendor verbs are documented in `deploy/README.md` beside `verify-schema` — they are the only way to grant time.
  Three findings were deferred with the remedy chosen: `follow-up/subscription-review-deferred.md`.
  ⚠️ **Interim state until Part D**: `/abonnement` is in `buildConfigItems` unconditionally, so `SelfHostedLan` and
  `CloudBrowser` show one rail row whose page says « cette installation ne fonctionne pas par abonnement ». Both
  endpoints **404 before the mediator** there, so nothing behind them is resolved; Part D's provider removes the row.
  ⚠️ **Reads are untouched by construction, not by a list**: the gate never inspects a GET/HEAD/OPTIONS, so « every
  read, every CSV export and every PDF keep working » holds for every read that exists *and* every read added later.
  An allow-list of readable endpoints would have to be kept complete, and the day it was not, an expired cabinet would
  lose part of its own records.
  ⚠️ **The gate goes after `LocalAuthEnforcementMiddleware`, not beside `TenantScopeMiddleware`** — one block earlier
  and a **402 masks the 401** of a revoked token and the **403 `must_change_password`** of a forced password change,
  so a deactivated colleague is told the subscription lapsed and a user owing a password change is sent to
  « Abonnement » instead of to the screen that unblocks them. It is correct in isolation and wrong only in *position*,
  which is why `SubscriptionGateMiddlewareTests` asserts the ordering against `Program.cs`'s own source.
  ⚠️ **A caller who is not a cabinet passes**, rather than meeting `subscription_missing`: no clinic in scope means no
  entitlement to find, and that fault code would otherwise land on precisely the vendor-console endpoints whose whole
  purpose is to *end* a refusal.
  ⚠️ **`SubscriptionLedger.Fold` takes no clock and folds on an EXCLUSIVE cursor**, and both halves are load-bearing.
  Passing « today » in — the naive reading of « the later of the current end or today, plus the duration » — makes the
  answer depend on when it is recomputed, so a lapsed entry restarts from today and `verify-schema` flaps daily. And
  a recorded day is an inclusive *start* while a running end is an inclusive *end*, so a single `anchor + duration`
  over both is wrong in one of the two cases whichever way it is written: a **31-day** trial (AC-1.1 says 10 Aug →
  8 Sep) or a one-day grant on a lapsed cabinet. Consequently **the trial's own date is not written directly either**
  — provisioning builds the entry and calls `ClinicSubscription.RecomputeFrom`, which is the *only* writer of
  `EndsOn`; a hand-computed `creationDay.AddDays(trialDays - 1)` disagrees with its own fold by one day and turns
  `subscription-end-date-matches-ledger` red on every new cabinet.
  ⚠️ **Two construction doors, three callers of the helper.** `LocalClinicProvisioning.ProvisionAsync` is a `static`
  taking its repositories as parameters, so the signature change breaks all three (`CreateClinicCommand`'s Local
  branch, the **`provision-clinic`** verb — container = `AddInfrastructure` **only**, which is why the repository and
  the policy are registered there — and **`VerifyClinicSignUpCommand`**, the public self-signup that will create most
  trials). Door 2 of 2 is `CreateClinicCommand`'s **Auth0/Cloud** branch, which builds its own `Clinic`, never
  reaches the helper, and always yields an **open-ended** entitlement — which is exactly why it is the door easiest
  to forget, and why `ClinicCreationEntitlementTests` derives the door set by scanning for `new Clinic(` instead of
  listing today's two.
  ⚠️ **The vendor's money is never the clinic's** (FR-2): separate tables and a separate
  `SubscriptionPaymentMethod` enum, and `MoneyReadConsistencyTests` is **unchanged** — a subscription payment reaches
  neither la caisse, l'extrait, « Créances », the dashboard's Argent section nor any patient's balance.
- **Multi-tenancy**: every request is scoped to a clinic. The **authoritative** check is per-request in the handlers — the clinic is resolved from the DB user record (`ICurrentClinicResolver`/`IClinicContext` → DB lookup of the `sub`, not purely from the JWT claim) and each loaded aggregate's `ClinicId` is re-verified. Since `cloud-security-and-tenant-isolation` (PR #11) there is **also** a second layer: EF Core **global query filters** on **21** clinic-owned aggregate roots. ⚠️ **They were fail-open — and therefore inert — until `multi-tenant-cloud` US-2 (Part B).** They are now fed by a three-valued **`ITenantScope`** (`Unset` | `Clinic(id)` | `SystemWide(reason)`) through `ICurrentClinicProvider`, and **only `Unset` refuses**: a path that never established a scope reads **nothing** instead of every clinic. The scope is set per request by `TenantScopeMiddleware` from the **DB-resolved** `User.ClinicId` — never from the JWT claim (amendment C3′: the Cloud claim is written by an Auth0 Action outside this repo, and a stale token used to be harmless under fail-open but would now mean zero rows with no error). Everything that reads with no HTTP context says so explicitly: the five recurring jobs, the startup scope and the three DB-touching console verbs call `UseSystemWide(reason)`; `PdfGenerationJob` and the App→Google dispatcher call `UseClinic(id)` because they handle exactly one record. Pinned by `TenantScopeFilterTests` (derived over every filtered root) and `SystemWideCallerCoverageTests` (derived over « reads a filtered entity with no HTTP context »). ⚠️ **The seven clinical children of `Patient` used to carry no `ClinicId` at all** (`MedicalDocument`, `DentalRecord`, `PatientMedicalHistory`, `PatientFamilyHistory`, `PatientFile`, `PatientFolder`, `ToothState`), so no filter was possible and the per-handler check was their **only** layer. They now each carry one, **denormalised from their patient**, and are filtered like every other clinic-owned table — which the derived `TenantScopeFilterTests.Every_Clinic_Owned_Table_Is_Either_Filtered_Or_A_Named_Decision` enrolled them into for free the moment the column appeared. `*TenantIsolationTests` + `ClinicalRecordTenantIsolationTests` still hold the per-handler layer. ⚠️ **Denormalised means the two can disagree, and nothing in the model can say they must not** — so `verify-schema` gained **`clinical-child-clinic-matches-patient`**, one figure over all seven tables catching both failure directions: a backfill that covered nothing (rows left at `Guid.Empty`, whose symptom is not an error but a patient record that reads as *empty*) and a write path that names the wrong clinic (the row is visible, to the wrong practice). The constructors take `clinicId` as a **required positional parameter** right after `patientId` for the same reason: a new write path that forgets it is a compile error, not a silent leak. Every caller passes the **patient's own** `ClinicId` — already tenant-checked one line above — never the caller's, so the invariant holds by construction rather than by discipline. It was **not** filtered through the `Patient` navigation: that puts a correlated subquery on the hottest reads in the product, and every other filtered entity states its clinic as a column. ⚠️ **SignalR hub methods run with no scope** (HTTP middleware does not run per invocation); `ClinicHub` is safe only because it reads `User`, which is unfiltered. See `Infrastructure/Persistence/ApplicationDbContext.cs`.
- **Three deployment topologies, one capability per question (`multi-tenant-cloud` US-1 / Part A)**: `Deployment:Profile`
  resolves to a **`DeploymentProfile`** — a `DeploymentKind` plus **15** named capabilities — and every mode branch in
  the solution asks one of them. It replaced `LocalAuthConfig.IsLocalMode`, a single boolean answering a dozen
  unrelated questions at ~30 call sites; two profiles happened to agree on all of them, so one flag sufficed, and a
  third does not. Absent ⇒ derived from `Auth:Mode` (`Local` → `SelfHostedLan`, else `CloudBrowser`); an
  **unrecognised value fails startup loud**, because falling back would hand a hosted deployment Auth0 login on a typo.

  | Kind | What it is | Status |
  |---|---|---|
  | `SelfHostedLan` | the clinic's own Windows PC serving its LAN: its data, its disk, its self-signed certificate, local accounts | **built** (`windows-desktop-app`, 5 phases) |
  | `HostedMultiTenant` | **one hosted backend serving many clinics**, each reached over the internet, on the product's own accounts — no Auth0 | **built** (`multi-tenant-cloud`, Parts A–C + F; D/E outstanding) |
  | `CloudBrowser` | one hosted backend reached by a browser, with Auth0 as the identity provider | **built** (the original path) |

  ⚠️ **`HostedMultiTenant` runs with `AUTH_MODE=local`** — it owns its accounts — so anything that asked « is this
  Local? » to mean « is this a clinic's own PC? » was already wrong there. That is the whole reason the profile exists,
  and `DeploymentProfileTests` asserts the two shipped kinds reproduce the old `IsLocalMode` truth table exactly.
  Deploy assets: `deploy/docker-compose.hosted.yml` + `deploy/.env.hosted.example`; operator guide in
  [`deploy/README.md`](deploy/README.md).
- **The hosted runtime can be watched, and it cannot race itself (`multi-tenant-cloud` US-6 / Part F)**: five hardenings
  that share one premise — **in a datacentre nobody is looking at the console.**
  - **`GET /health`** (anonymous, un-rate-limited, every profile) answers what a TCP probe cannot: the database round
    trips, and the file storage is reachable through the new `IFileStorage.ProbeAsync`. ⚠️ **Storage down is `Degraded`
    (200), not `Unhealthy`** — a clinic with no object storage still books, records and collects; grading it unhealthy
    would pull every instance out of rotation and turn a partial outage into a total one, and restarting the API does
    not bring MinIO back. The body carries check **names and statuses only**; reasons go to the log, never to an
    anonymous caller.
  - **`GET /api/outbox`** (`AdminOnly`) reports the depth of the three background queues — reminders (pending / **due**
    / blocked / failed) and document emails — each with **the age of its oldest waiting row**. ⚠️ That age is
    the diagnosis, not the count: « 40 pending » is meaningless when a reminder for next Tuesday is *supposed* to be
    waiting, while a row due three hours ago says the dispatcher is not running. It exists because `/hangfire` is
    loopback-only in **both** modes and behind a reverse proxy every request arrives from the proxy container — correct
    as security, total as blindness — and because US-2's stated risk (R-1) is that a job with no tenant scope reads
    **nothing** and logs a clean run. Each queue's predicates are **copies of its own dispatcher's**, clause for
    clause, or the read would report a backlog nothing will ever drain.
  - **The login limiter is keyed on the submitted account**, with the address as a second and looser ceiling. Per
    address alone is a lockout waiting to happen once a deployment is reached over the internet: a whole practice
    arrives through **one** public NAT address, so one colleague mistyping ten times spent everybody's budget.
    ⚠️ It could not be a compound `account+address` key — that hands one attacker a fresh budget per address — so the
    named policy partitions on the account while the global limiter partitions the same request on its address
    (`RateLimiting.IsAnonymousAuthPath`). The email is lifted out of the body by `AuthAttemptAccount` **before** the
    limiter, since the partitioner is synchronous and runs long before model binding; **anything unreadable falls back
    to the address**, so `auth/refresh` and a malformed body are bounded exactly as they were before.
  - **`Security:EnforceCsp`** promotes the CSP from report-only to enforcing. Default **false in every profile** and
    deliberately *not* derived from the kind: what makes enforcing safe is that somebody walked these pages in this
    deployment. (Checked against Next's own policy first — `web/next.config.ts` emits **no** CSP in either branch, so
    there is nothing to intersect with.)
  - **`MigrationLock`** wraps the startup migrate-and-backfill block in a PostgreSQL **session-level advisory lock**:
    EF Core 8 takes none, so two containers starting together both apply the same migrations and the loser fails
    part-way, leaving a schema that is neither old nor new. Advisory rather than a lock table (a table would need the
    migration it protects, and a crashed holder would wedge the next deploy for ever); ⚠️ **`pg_advisory_lock`, never
    the `xact` variant**, which would release at the first commit *inside* the migration.
  - **`DataProtection:KeyRingPath` is required in `HostedMultiTenant` and fails startup without it.** The framework
    fallback is per-instance and ephemeral: it works, and then the first redeploy replaces the ring, so every clinic's
    encrypted reminder credentials become undecryptable and each channel reports « non configuré » with
    nothing in any log tying that to a deployment. A path with **no durable volume** behind it produces the identical
    symptom, which no code can detect — that half is stated beside the volume in the compose file.
  - **The three read-only/recovery verbs gate on the connection string, not the profile** (amendment M3):
    `verify-schema`, `reconcile-money` and `reset-admin-password` run no PostgreSQL binary, so `HasLocalDbTooling` was
    the wrong question. It mattered twice — `verify-schema` is the **only** gate a schema change has anywhere in this
    product, and a hosted clinic's locked-out admin had no recovery once `provision-clinic` could create one.
    ⚠️ **`restore-backup` keeps its profile gate**, because its safety interlock (« refuse while the app is
    listening ») looks for a listener on *this* machine and in a container the API listens in a sibling — so the check
    would pass silently while `pg_restore --clean` drops tables under a live application.
- **Every blob knows whose it is (`multi-tenant-cloud` US-5 / Part E)**: new storage keys are
  **`clinics/{clinicId}/…`**, composed in exactly one place — `Infrastructure/Storage/ClinicStorageKey` — for
  **both** backends. The defect was not only the flat keys: « which clinic owns this blob » had **two** answers.
  Four upload sites prefixed a path of their own with a bare `{clinicId}/` (the logo, a doctor's cachet, and the
  two artifacts of the electronic-invoicing subsystem of the day) while four wrote `{guid}-{timestamp}` with no
  clinic in it at all — the patient files and
  the three PDF paths — so on a hosted backend most of the object store was one undifferentiated pile, and a
  third convention was one new upload away.
  ⚠️ **The enforcement is the signature, not a convention**: both `IFileStorage.UploadAsync` overloads now
  **require** a `Guid clinicId`, so an unprefixed key is not something a caller can write, and the second
  overload's path is **relative to the clinic** (adding a clinic segment of your own yields
  `clinics/{id}/{id}/logo`). `ClinicStorageKeyTests` derives that off the interface rather than listing today's
  overloads — a third overload with no clinic id fails it, which was checked by adding one.
  ⚠️ **The clinic is a parameter rather than something the storage reads off `ITenantScope`.** The tempting
  version works for every HTTP path and fails silently for the one that matters: an outbox job uploads under
  **`UseSystemWide`**, where there is no clinic in scope at all.
  ⚠️ **Reading is deliberately asymmetrical — there is no backfill (amendment M2).** `DownloadAsync`/`DeleteAsync`
  take the stored key **verbatim**, so every pre-Part-E row keeps resolving; composing on the read side would
  strand all of them. That is also why the plan's pitfall was a *verification* task rather than a code change:
  `PdfGenerationService` loads a practitioner's cachet from the `doctorCachetKey` snapshotted into the document's
  `ContentJson`, and a key-format assumption there fails **silently** — the renderer falls back to a plain
  signature line. It reads the stored value and was left alone.
  ⚠️ One consequence worth knowing: the logo and cachet keys were **deterministic**, so a re-upload used to
  overwrite in place. It now lands on a new key, which is why `UpdateDoctorProfileCommand` gained a post-commit
  best-effort delete of the superseded blob (the logo path already deleted the old key before uploading).
- **The vendor has a private console, and it cannot read a patient record (`platform-console` Part 1)**: a
  **fourth surface** on this product — a second identity population (`PlatformAccount`, its own tokens, a
  mandatory TOTP second factor), a **second Kestrel listener** on an unpublished port reached over an SSH tunnel,
  and a second Next application (`console/`) that contains none of the clinic bundle. Gated on the 16th
  capability, **`DeploymentProfile.ServesPlatformConsole`** (`HostedMultiTenant` ✓ only). Part 1 delivers reaching
  it and signing in; **Part 2 adds the portfolio and the counters behind it** (see the bullet below); the three
  writes wait on
  `features/clinic-subscription/`.
  ⚠️ **Binding the console listener can take the whole product offline, and that is the trap this part is built
  around.** In `HostedMultiTenant` there is no cert file, so `Program.cs` never called `ConfigureKestrel` at all —
  `ASPNETCORE_URLS` alone binds 5000 — and an explicit Kestrel endpoint **overrides that configuration
  wholesale**. A bare `ListenAnyIP(consolePort)` would unbind 5000, Caddy's `/api/*` would stop resolving, and the
  entire product would go dark while the console worked perfectly. `ConsoleListenerPlanning` resolves **both**
  ports and they are bound in **one** call; EC-4's collision check is derived from the ports *actually resolved*,
  never from `Hosting:HttpPort`/`HttpsPort`/`WebPort` — none of those three is set in the hosted compose file, so
  a check written against them cannot fire in the one profile the console exists on.
  ⚠️ **A port bind is not a scoped surface**: every mapped route answers on every bound port, so `ConsolePortGate`
  is what does the work — and unlike `TrustPortGate` it refuses **both** directions (a console path anywhere but
  the console port, and anything but a console path on it), matched with `StartsWithSegments` so
  `/api/platform-ish` cannot slip through. It is registered **unconditionally**: with the console off the port is
  `0` and every console path 404s everywhere, which is what AC-1.8's « absent, not present-and-refusing » means.
  ⚠️ **AC-1.4 is a property of the signature, not of a policy.** The console's tokens carry their own key, issuer
  and audience (`PlatformAuthConfig`, which **throws** rather than borrowing `Auth:Local:SigningKey`), so each
  scheme fails the other's validation and a token on the wrong surface is **401, not 403**. The
  `AuthorizationPolicies.PlatformConsole` policy **pins its scheme** — without that it authenticates against the
  default (clinic) one and rejects every console token — while the four clinic policies keep **no** explicit
  scheme, and that asymmetry is what makes the refusal true in both directions.
  ⚠️ **Console requests skip `AccountStateMiddleware`, `LocalAuthEnforcementMiddleware` and
  `TenantScopeMiddleware`** (a console principal has no `User` row), so two middlewares fill the holes that
  creates: **`PlatformAccountStateMiddleware`** re-reads the account and refuses a deactivation or a stale
  `TokenVersion` on the **next** request (AC-1.6) — without it the deactivation verb would leave a revoked account
  with full cross-cabinet access until its token expired, ⚠️ **which is exactly what it did until Part 7 fixed it;
  see that bullet before trusting this sentence's history** — and **`PlatformTenantScopeMiddleware`** declares
  `UseSystemWide("platform console")`, because an `Unset` scope reads **zero rows with no error**, which is
  indistinguishable from a portfolio where every cabinet is idle (EC-12).
  ⚠️ **`AuditActor.Console(accountId)` lands here rather than with the first console write.** `AuditActorProvider`
  returns the token's `sub` first, so consulting `IPlatformSessionContext` *before* `IClinicContext` is what stops
  a console write being recorded as a bare GUID — indistinguishable from a clinic user in that cabinet's journal,
  and invisible to Part 2's `console|` counter exclusion, which would then match nothing. Both failures are
  silent, and both later parts consume a seam that is already correct.
  ⚠️ Accounts are created **only** by `platform-account create` (AC-8.5) — no MediatR command, because a handler
  is one attribute away from being callable over HTTP — and it prints a one-time password plus an enrolment secret
  shown **once**. The enrolment response carries the recovery codes, also once; a recovery code is **spent even
  when the sign-in it accompanied fails** (AC-1.3b), while a *wrong password* spends none, or knowing an address
  would be enough to burn all eight. Operator runbook in [`deploy/README.md`](deploy/README.md).

- **The portfolio is a counter table, not a query over the ledger (`platform-console` Part 2)**:
  `GET /api/platform/clinics` lists every cabinet with its **real activity** beside it — patients, staff accounts,
  RDV pris (30 j), enregistrements (7 j / 30 j), jours actifs, dernier enregistrement, dernière connexion and what
  the cabinet itself collected this month — filterable (« dormant »), searchable and paged, under a summary strip
  (`GET /api/platform/summary`). Behind it: **`ClinicActivityDay`** (one cabinet, one clinic-local day — the durable
  history Part 3's six-month trend reads) and **`ClinicActivitySnapshot`** (one cabinet, the row the list JOINs),
  both written by the daily **`ClinicActivityCounterJob`**.
  ⚠️ **Two tables rather than one, and the snapshot exists because of AC-2.4a**: the list filters and sorts *on
  activity*, so every figure must exist for every cabinet **before** a page is cut — a figure folded after the page
  was selected would sort a window rather than the portfolio, and « les cabinets dormants » would mean « ceux de
  cette page ». It is one bounded `Clinics ⋈ snapshot` LEFT JOIN, so the read is bounded by the number of cabinets
  rather than by the busiest practice's whole history (EC-11). A **LEFT** join because a cabinet the pass has never
  reached must still appear — with its counters stated as **unknown**, never as zeros (EC-15).
  ⚠️ **« Saves » counts only people at the cabinet, and both exclusions are silent failures otherwise** (AC-2.2):
  `job|…` actors (backups, reminder dispatch, expiry passes — they write into *every* cabinet's ledger every day,
  so counting them makes the emptiest practice read as active) **and the console's own `console|…` actor**, or
  granting a dormant cabinet a subscription makes it read as active the next morning — on exactly the cabinet the
  « dormant » filter just surfaced. `PlatformCounterPass` is pure and matches on **`AuditActor`'s own prefix
  constants**, never a retyped literal.
  ⚠️ **Not every figure is audit-derived, and one of them must not be.** `patients` is a `COUNT` over the cabinet's
  patients — *never* audit `Insert` rows: the ledger only exists since `adoption-qa-i`, so an established practice
  would read as nearly empty, which is wrong in the direction of « barely used », i.e. exactly the churn signal the
  list exists to give. `users`/`lastLoginAt` come from `IUserRepository.GetStaffSummaryAsync`, and
  `clinicCollectedThisMonthDt` from **`PlatformCollectedReader`** — the same repository predicates la caisse sums,
  through `PlanBillingRules.BilledPlanIds`. That makes the console the **fifth** money read, and
  `MoneyReadConsistencyTests` was extended to pin it equal to `caisse.CashIn − caisse.Refunds`: the vendor quoting
  a practice a turnover its own caisse contradicts is the worst possible place for drift.
  ⚠️ **`PlatformReadShape` is the whole of AC-7.2, and the tenant filter explicitly is not (AC-7.2a).** The filter
  is *lifted* on this surface by design — a portfolio is a cross-cabinet read — so the guarantee is carried by a
  **closed set of returned field names**, checked by `PlatformReadShapeTests`, which reflects over every
  `Features.Platform` request's response type, recurses into nested DTOs and **fails the build** on any name
  outside it. Names, not types: a type allow-list is satisfied by adding a field to a type already on it, which is
  precisely how a patient's name would arrive. Asserted in **both** directions, so an unused allowance is a hole
  that fails too.
  ⚠️ **The counter job rewrites the whole 30-day window each night, not only yesterday** — the audit rows are
  already in hand for the snapshot, so the day rows are nearly free, and it makes the history self-healing: a
  container down for three days would otherwise leave permanent holes in a trend nothing can reconstruct, since
  the window the pass reads is itself 30 days. Idempotent through the unique `(ClinicId, Day)` index.
  ⚠️ **Subscriptions are deliberately absent, from one clearly-named place.** `PlatformSubscriptionPlaceholder`
  keeps `plan`/`state`/`endsOn`/`daysRemaining` **null** and reports `subscriptionDataAvailable: false`, off which
  the screen hides the state filters and says so; the « par date de fin » sort is not offered at all rather than
  silently sorting by something else. Part 4 **deletes** that file — the compiler then lists every caller — rather
  than widening it into a console-side entitlement fold, which is the FR-4 violation this feature is defined
  around. ⚠️ `verify-schema` gained three checks; the plan's `clinic-activity-day-unique-per-clinic-day` was
  **replaced**, because the unique index makes it unfalsifiable and the index is already diffed for free — see
  `features/platform-console/stories/progress.md` DEV-4.
  ⚠️ **All four « Abonnement » filters were dead from Part 2 until a user reported it, and the defect was one missing
  method parameter**: `PlatformPortfolioController.ListClinics` bound `dormant`/`q`/`sort`/`page`/`pageSize` and **not
  `state`**, so the console sent `?state=expired`, model binding had nowhere to put it, `ListPlatformClinicsQuery.State`
  stayed null and the list narrowed nothing. Every layer behind the hop was correct — the SQL predicate, `ParseState`,
  the chips — and `PlatformPortfolioQueryTests` asserts the *handler* forwards every filter **it is given**, which it
  did. A dropped filter also fails silently: the list still answers, with more cabinets than were asked for. The fix is
  the parameter plus `PlatformPortfolioControllerTests`, whose second case is **derived** (every settable property of
  the query must have a parameter to arrive on) so the next filter cannot be dropped the same way; both halves were
  proven red. This is `fixes-dont-propagate`'s neighbour — a correct rule wired to one caller fewer than it has.
  ⚠️ Three adjustments landed with it. The list now defaults to **the newest cabinet first** (`ParseSort`'s fallback and
  the console's `DEFAULT_PORTFOLIO_SORT` are the *same* value, which is what keeps the default out of the URL), a table
  row is **clickable** — as an *addition* to the « Ouvrir » link, which stays because a `<tr>` with an `onClick` has no
  keyboard path and no accessible role — and the row carries the cabinet's **administrator's e-mail**
  (`PlatformClinicRowDto.AdminEmail`, already an allowed `PlatformReadShape` name from the fiche). That last one is
  resolved by **`IUserRepository.GetPrimaryAdminContactsAsync`**, one batched read over the page, and the single-cabinet
  `GetPrimaryAdminContactAsync` the fiche uses now **delegates to it**: « which admin is the contact? » is a precedence
  rule (active first, then the founder, then deterministic), and two expressions of it would drift into the list naming
  one person and the fiche naming another, with both screens looking right on their own.

- **The console records what it looked at, and the record is readable (`platform-console` Part 3)**:
  `GET /api/platform/clinics/{id}` opens one cabinet — the list's own figures, a **six-month trend** off
  `ClinicActivityDay`, and the administrator's name, address and whether that account is still active — and
  `GET /api/platform/access-log` + `/journal` serve the console's own append-only **`PlatformAccessEntry`** ledger,
  paged, newest first, filterable by console account and by cabinet.
  ⚠️ **The detail read is recorded and the list read is deliberately not** (AC-3.5): one list read touches every
  cabinet, so a row per cabinet per page load would drown every reading anyone wants — including this one. That
  asymmetry is held by a test asserting on `ListPlatformClinicsQueryHandler`'s **constructor**, because « I ran the
  list and no row appeared » passes just as happily when the ledger is broken for every caller.
  ⚠️ **`GetPlatformClinicDetailQuery` is a query that writes, and its ledger row is *not* best-effort** — the one
  place in this codebase where a failed side effect fails its operation. `INotificationGenerator` swallows because
  the operation it follows has already committed; here the operation **is** what is being recorded, and « every
  detail read is recorded » is false the moment an unrecorded read succeeds. A `Command` was rejected for a
  mechanical reason too: `RealtimeBroadcastBehavior` derives its key from the namespace, so one under `.Commands`
  would broadcast into a clinic group on every page load.
  ⚠️ **`PlatformAccessEntry` has no FK to `Clinics` and none to `PlatformAccounts`** — the opposite of its two Part-2
  siblings, which cascade on purpose. Those are measurements *of* a cabinet; this records what the **vendor** did,
  and « who opened the file of the practice that has since been closed? » is the first row an audit would want. Hence
  the denormalised `ClinicName`/`AccountEmail`, and hence its entries in `TenantScopeFilterTests.UnfilteredByDesign`
  (its `ClinicId` is the cabinet *looked at*, not the row's owner) and in the audit interceptor's exclusion list
  (auditing a ledger records the writing of a record, and a mere *read* would appear in the practice's own
  « Journal d'activité » as a mutation of its data).
  ⚠️ **AC-3.2's payment history is explicitly deferred, and the screen says so rather than showing an empty table** —
  an empty « Historique des paiements » asserts that this cabinet has never paid, a claim about the cabinet, where
  the truth is a claim about the console. Same for EC-14: no end date is shown at all, because until the entitlement
  ledger exists « sans échéance » and « nous ne pouvons pas le lire » are indistinguishable and the second is what is
  true. Both sentences come from `PlatformSubscriptionPlaceholder`, which Part 4 deletes.
  ⚠️ On the client, the trend **states every value as text beside the bars** (reading a figure off a 40 px column is
  guessing, and the vendor's next question is always « by how much? »), a month with `daysMeasured == 0` is hatched
  and labelled « non mesuré » rather than drawn flat, and `components/ui/pager.tsx` was **extracted** so the journal
  and the portfolio cannot drift on links-not-buttons and disabled-step-as-text.

- **The vendor records a payment and the cabinet unlocks (`platform-console` Part 4)**:
  `POST /api/platform/clinics/{id}/subscription-periods` — the console's **only** write, on its own
  `PlatformSubscriptionsController` so `PlatformPortfolioController`'s « read-only by construction » stays a
  checkable claim. The portfolio's entitlement column, its five state filters, its « par date de fin » sort, the
  summary strip's state counts and the detail's payment history all become real, and
  `PlatformSubscriptionPlaceholder` is **deleted** — the compiler listed its callers, which is what it was for.
  ⚠️ **The console computes no date** (AC-4.2). It reuses the companion's own pieces — `SubscriptionCabinetLookup`,
  `SubscriptionPeriod.Create`, `SubscriptionRefold` — rather than sending `GrantSubscriptionPeriodCommand`, and the
  reason is **atomicity**: that command commits on its own, so the FR-5 access-ledger row would be a second
  transaction, and a payment recorded with no ledger row behind it is the « an unattributable action must not
  aboutir » Part 3 settled for reads. Staging the ledger row before `SubscriptionRefold`'s single save is the only
  shape in which AC-4.7 and AC-7.3 are true of the same instant. An explicit transaction was rejected too — the
  refold retries on `ConflictException`, and a failed statement aborts the ambient transaction.
  ⚠️ **« En essai » had to become a SQL predicate, and the obvious column was unstorable.** AC-2.4a requires every
  figure the portfolio filters on to exist before a page is cut, and folding N cabinets' ledgers is the unbounded
  read EC-11 forbids — but « is the cover in force **today** the trial? » is a function of the ledger *and of
  today*, while `RecomputeFrom` is deliberately clock-free. The storable form is **`ClinicSubscription.LatestCoverKind`**,
  the kind of the last non-cancelled entry in fold order: a pure function of the ledger, written by `RecomputeFrom`
  alone and re-derived by `verify-schema`'s **`subscription-cover-kind-matches-ledger`** through the *real* fold.
  The filter ANDs it with the state terms, so a lapsed trial is excluded by « expiré » regardless of its kind.
  `IsOnTrial` **moved** out of `GetSubscriptionQuery` into `SubscriptionTrial` — the console is its second caller.
  ⚠️ **A double-click produces one entry, and the guard is a partial-unique index** on
  `PlatformAccessEntry.IdempotencyKey` — never the handler's read-first check, which two simultaneous submissions
  both pass. The key lives on the access ledger rather than in a table of its own because every console write
  already produces exactly one row there, in the same transaction; the row also names the `SubscriptionPeriodId`
  it created, so a replay returns the **first** outcome instead of guessing. A lost race replays rather than
  surfacing the unique violation (EC-5), and two *different* grants both land and are both kept (EC-6).
  ⚠️ **`Platform` joins `RealtimeResourceResolver.ExcludedAreas`**: a console account belongs to no clinic, so the
  behaviour's audience would be nobody — silently — and a new key fails the contract test in both directions.
  AC-4.4a is dropped for the reason `Subscriptions` is already excluded (progress.md DEV-12).
  ⚠️ **The console cannot grant open-ended cover**, because the companion refuses it in its own handler: a cabinet
  that should never expire is grandfathered by a migration. EC-14 is met on the **read** side — « Sans échéance »
  is said in words wherever a null end date appears.

- **A mis-keyed payment is corrected, never erased (`platform-console` Part 5)**:
  `POST /api/platform/clinics/{id}/subscription-periods/{entryId}/cancellation` strikes one ledger entry through with
  a **mandatory motif** and the cabinet's end date recomputes — possibly into the past, at which point the practice
  becomes read-only again. The entry is **kept**, struck through *and* marked « Annulé » in words, carrying its motif,
  its canceller (`console|{accountId}`) and the moment; `PlatformAccessAction.CancelledPeriod` arrives with the write
  that produces it, and Part 4's shape is reused verbatim — the companion's own pieces, with the FR-5 access row
  staged before `SubscriptionRefold`'s single save.
  ⚠️ **AC-5.3's « from which date » is computed by re-folding the real ledger with that one entry marked cancelled**,
  and it travels **on the detail read** (`PlatformSubscriptionEntryDto.IfCancelled`) rather than behind a preview
  endpoint — so the confirmation cannot open without the sentence, which a preview call that can fail would allow.
  The naive client-side form (« the current end minus this entry's duration ») is right only when the entry is the
  *latest* one: the fold advances on an **exclusive cursor**, so removing a **middle** entry shortens every stretch
  after it. `isTrial` comes from the *previewed* fold too, since cancelling a paid entry can hand the cover back to
  the trial. The preview and the write are held equal by a test that runs both over one ledger, proven red.
  ⚠️ **Cancelling a cabinet's ONLY entry yields « sans échéance », not « expiré »** — `FoldWithSpans` starts at null,
  so a wholly-cancelled ledger folds to no end date, which the state reader reads as *no expiry*. It is the
  companion's own semantics, left alone (FR-4), and unreachable in practice because every cabinet is provisioned with
  an opening entry (FR-13). It is why EC-7's fixture seeds a lapsed **trial** beside the grant: a one-entry fixture
  asserts the opposite of EC-7 and passes.
  ⚠️ **No idempotency key here, unlike the grant, and that is deliberate.** A double-click on « Enregistrer le
  paiement » is the vendor's own repeated action, so replaying the first outcome is what they wanted (AC-4.6) — but an
  entry already struck through was struck through by **somebody**, and which colleague and for what motif is a fact
  the vendor needs. So « déjà annulée » is a **refusal** carrying `period_already_cancelled` (409) and the dialog
  re-reads the fiche so that motif and author appear beside it. ⚠️ And it is a **POST, not a DELETE**: nothing is
  deleted, and `DELETE` would advertise the opposite to every reader of the controller.

- **A cabinet is stopped for abuse, and never told it has expired (`platform-console` Part 6)**:
  `POST /api/platform/clinics/{id}/suspension` (mandatory motif) and `…/suspension/lifting` make a cabinet read-only
  **independently of what it has paid**, and `PlatformAccessAction` closes with `Suspended`/`Unsuspended`. No
  migration and no model change: the action column is `HasConversion<int>()` and the entitlement already carried
  `SuspensionReason`/`SuspendedAtUtc`/`SuspendedBy`.
  ⚠️ **Nothing here touches the ledger, and that is the whole of AC-6.4.** « Unsuspending restores whatever
  entitlement the cabinet had » is not a restore step — it is a property of never having spent anything, so
  `SetClinicSuspensionFromConsoleCommand` deliberately does **not** use `SubscriptionRefold` (no entry changed ⇒ no
  date to re-fold ⇒ a lost update is an ordinary 409, the companion's own reasoning). The response echoes the
  unchanged `endsOn` precisely so that is checkable on the screen that did it.
  ⚠️ **The load-bearing case is a lift landing on a cabinet that is still expired.** The outcome is read back through
  `SubscriptionStateReader`, never asserted from the button pressed: a naive `MakesReadOnly = IsSuspended` passes
  **16 of the 17** new tests and tells the vendor a practice can work again when its next save will be refused.
  Proven by writing that exact line and watching one test — and only that one — go red.
  ⚠️ **One command with a `bool Suspend`, two endpoints.** It mirrors the companion's own
  `SetSubscriptionSuspensionCommand` rather than the story's planned suspend/unsuspend pair, because two handlers
  would be two copies of « resolve · mutate · stage the access row · save » — the `fixes-dont-propagate` shape. The
  direction stays in the **URL** so no truncated body can flip it, and which journal action is recorded is decided in
  one place.
  ⚠️ **Re-suspending is a 409, not a re-statement**: the entitlement holds exactly one motif, one author and one
  moment, so a second `Suspend` would overwrite a colleague's reasoning with no trace of it anywhere — changing a
  motif is lift-then-suspend, and both halves land in the journal. **Lifting a cabinet that is not suspended is a
  409 too**, because `Unsuspend` clears nothing there: a silent success would record an action that never happened
  and read on the fiche as having released a cabinet whose real problem is its end date, which the refusal names.
  ⚠️ **The two journal rows are the only durable record.** `Unsuspend` clears the trail off the entitlement by
  design, so `GET /api/platform/clinics/{id}`'s new `suspension` object explains a *live* suspension only, and
  « qui a suspendu ce cabinet en mars ? » is answerable at `/journal` and nowhere else.
  ⚠️ **On the client it is its own section, not a control under « Abonnement et paiements »** — that placement *is*
  AC-6.3. A « Suspendre » button beside the payment history presents suspension as a billing lever, and a vendor who
  reads it that way reaches for a **cancellation** instead, which is not reversible. « Suspendu » is stated in words
  with the motif quoted, so a greyscale printout and a screen reader get the same facts as a colour.

- **The verification found the hole the six parts before it could not (`platform-console` Part 7)**: Part 7 adds no
  feature — it is the schema gate run before and after the migration batch and diffed, the operator runbook in
  [`deploy/README.md`](deploy/README.md), and the paragraph a clinic can be *sent* about what the vendor sees. Its
  step 51 says « confirm the two "cannot look the same" requirements **by trying them** », and trying them is what
  earned this bullet.
  ⚠️ **`PlatformAccountStateMiddleware` was inert in production for the whole life of the feature**, so AC-1.6's two
  revocations and AC-8.1's « one-time » password were absent while every layer reported them present. It read
  `context.User`, which for a **console** token is never populated where it runs: `UseAuthentication` authenticates
  only the *default* (clinic) scheme — which a console token fails **by design**, that being AC-1.4 — and the
  console's own scheme is authenticated inside `AuthorizationMiddleware`, because the policy pins it, i.e. *after*
  this middleware. So the account id resolved to null on every request and everything passed through. Proven over
  the wire: signed in, ran `platform-account --deactivate`, called `/api/platform/summary` again with the same
  token — **HTTP 200, the whole portfolio**. The fix authenticates the console scheme in the middleware and every
  check reads *that* principal; `HasCurrentTokenVersion` was still reading `context.User` after the first attempt,
  which the new test caught.
  ⚠️ **Its tests passed throughout because they set `context.User` by hand** — the one thing production does not do.
  That is the general lesson: a middleware whose subject is established by a *pinned* authentication scheme cannot be
  unit-tested through `DefaultHttpContext.User` without asserting the very arrangement that is broken. The new cases
  install a stub `IAuthenticationService` instead, so they exercise the call production makes.
  ⚠️ **EC-12 and EC-15 both hold, and the second was proven twice.** With the database frozen the portfolio read
  answers **500 + a French sentence** and the page renders « je n'ai pas pu lire » — never an empty table, because
  « aucun cabinet » and « je n'ai pas pu lire » are the same picture and opposite facts. A deployment whose counter
  pass has never run reports « jamais mesuré » on screen (`neverMeasured: 4`, `dormant: 0`) **and** as
  `verify-schema`'s `clinic-activity-snapshot-covers-every-clinic` — the one figure that says « the pass has not run »
  rather than « these practices are idle ».
  ⚠️ **The before/after schema diff is the clean result the workflow is for**: 11 drifts, every one of them a
  `MISSING` index or FK in the four new tables, going to **0** — plus `subscription-cover-kind-matches-ledger` and
  the two counter checks moving from « not applicable — does not exist yet » to green, which independently
  re-derives Part 4's `LatestCoverKind` through the *real* fold.

- **The vendor buys the WhatsApp messages and a cabinet spends them (`vendor-whatsapp-messaging-quota`, Parts 0–5)**:
  each practice gets a **monthly allowance of WhatsApp appointment reminders**, counted one per message actually
  sent; past it a reminder is **held, never dropped** — it goes out when the vendor tops the cabinet up. Gated on
  the 18th capability **`SellsVendorMessaging`** (`HostedMultiTenant` only). SMS is never counted, and nothing but
  WhatsApp reminders is affected.
  ⚠️ **The month's allowance is a stored snapshot of a fold, and that is the design's whole risk.**
  `MessagingAllowanceEntry` is an append-only ledger (a standing figure, plus one-off top-ups per named month) and
  `Domain/Services/MessagingAllowanceLedger` folds it — **pure, total and clock-free**, with the month a
  *parameter*. `ClinicMessagingMonth` stores that answer per (cabinet, Tunisian month) because the vendor console
  filters and sorts the whole portfolio on consumption-against-allowance **before a page is cut**, which folding N
  ledgers cannot serve. Nothing in the model can say the two agree, so `verify-schema` gained
  **`monthly-allowance-matches-ledger`**, re-deriving every row through the **real** fold and reporting **both**
  directions — a snapshot *above* the fold lets a cabinet send messages nobody allocated, one *below* holds
  reminders it has paid for. `subscription-end-date-matches-ledger`'s reasoning, one dimension over.
  ⚠️ **`null` and `0` are opposite facts and every layer keeps them apart.** No entry reaching a month folds to
  **null** — « our bookkeeping is wrong », held under its own `OutboxBlockReason` and its own French sentence —
  while `0` is a cabinet the vendor decided sends no WhatsApp reminders. Likewise a **missing counting row**
  (« non mesuré ») is not « 0 rappel envoyé »: the row is *provisioned* for every cabinet rather than created on
  first send (FR-1a), which is what makes a quiet month distinguishable from a broken counter.
  ⚠️ **A raise applies this month and a lowering waits for the next; a cancellation reaches every month it fed,
  the current one included.** The vendor states an amount and never a month — `MessagingAllowancePlan.Decide` is
  the server's single answer, extracted rather than written twice because the console wrapper cannot send the
  vendor command (it commits on its own, and the journal row would be a second transaction). ⚠️ A standing figure
  of **zero is a lowering** and defers; turning a cabinet off *now* is a cancellation.
  ⚠️ **Enforcement is a term on the existing `OutboxMessagingGate`, asked at dispatch *and* at un-park** — never a
  second gate — so both call sites inherit it, and Part 4's template term slots in ahead of the allowance terms
  (a cabinet meeting two conditions is told the one it can act on). It reads **nothing at all** where the
  capability is off, and it passes a cabinet with **no stored template status**: reading null as `NotSubmitted`
  would have held every WhatsApp reminder on the deployment the day it shipped, for a template Meta approved long
  ago.
  ⚠️ **The counting increment is staged into the dispatcher's own per-row save**, so a crash loses the send and the
  unit or neither. The month row is a plain `Entity<Guid>` and deliberately **not** an `AggregateRoot`: it is
  incremented minutely, and `AuditSaveChangesInterceptor` writes one row per mutated root — the audited artefact
  is the ledger **entry**.
  **Surfaces**: the practice sees « il vous reste N rappels » + a twelve-month history on `/rappels` and is warned
  at **80 / 95 / 100 %** (all crossed thresholds, deduped on a real column, withdrawn by one reconciling call);
  the vendor gets two portfolio columns, a « presque épuisé » filter whose threshold is **one** figure end to end,
  a cabinet-file section, and three console verbs — **`messaging-grant` / `messaging-cancel` / `messaging-report`**
  (exit 0/1/**2**, `--month` answering for a **closed** month). Operator runbook in
  [`deploy/README.md`](deploy/README.md).
  ⚠️ **Meta's own side is Part 4**: Embedded Signup moved **v3 → v4** (extracted to
  `web/lib/hooks/use-whatsapp-embedded-signup.ts` so two surfaces cannot drift back to handling only `FINISH`), the
  reminder **template is submitted post-exchange** and its status has two writers — a signed
  `message_template_status_update` webhook **and** a reconciling daily poll, neither a substitute for the other —
  and `WhatsAppSender.Classify` reads Meta's refusals off the **full** body to tell a throttle from a stopped
  number. The template carries **one** body variable, not two: the sender sends one pre-rendered French sentence,
  and formatting inside the sender would move the wording away from `ReminderScheduler`, where
  `ReminderMessage.AnnouncesStaleMoment` reads it.
  ⚠️ **Still owed, and none of it is a code gap**: no `Meta:AppId`/`AppSecret` exists on any deployment, so the
  template has never been submitted, the webhook never called by Meta and the v4 popup never opened — everything is
  verified against documented payload shapes and ~150 unit tests. See
  [`follow-up/vendor-messaging-open-questions.md`](follow-up/vendor-messaging-open-questions.md), which also carries
  the Graph `v21.0 → v26.0` bump and the two Meta rules the spike could not settle.

- **A clinic can let itself in, and nothing exists until the email is answered (`clinic-self-signup`)**: a hosted
  clinic used to exist only because an operator ran `provision-clinic`. `POST /api/auth/signup` (anonymous) writes a
  pending **`ClinicSignup`** and emails a link; `POST /api/auth/signup/verify` consumes it and provisions the clinic +
  admin through the **existing** `LocalClinicProvisioning.ProvisionAsync` — its third caller, and it needed no change.
  Gated on the 15th capability, **`DeploymentProfile.AllowsPublicClinicSignup`** (`HostedMultiTenant` ✓ only), reported
  to the browser as `publicSignupEnabled` on `GET /api/auth/mode`; pages `/signup` + `/signup/verifier`.
  ⚠️ **It does not reopen the door US-3 closed, and the two capabilities are opposite questions.**
  `AllowsSelfRegistration` is « may a stranger *join an existing clinic* with its six-character code? » — a shared
  password everyone who ever worked there knows — and stays **`false`**. This one hands out no shared secret: the gate
  is a fresh 32-byte CSPRNG token (`RandomNumberGenerator`), SHA-256 in the row and plaintext **only in the email**,
  single-use and 24 h. Reading either flag as the other is a security decision made by accident.
  ⚠️ **`ClinicSignup` carries no `ClinicId`** — a signup exists precisely because its clinic does not — so it is outside
  the EF tenant filter *by construction* and needs no `TenantScopeFilterTests` entry, whose clinic-owned set is derived
  from that very column. It is also the one table with **no FK and nothing that cascades it away**, which is why the
  purge is opportunistic on the signup path (no new job) and why `verify-schema` gained
  **`clinic-signup-has-no-orphans`**.
  ⚠️ **The response is byte-identical whether the address is free, already an account, or already pending** — one
  neutral French sentence, so the endpoint is not an enumeration oracle; only the password-length rule refuses
  differently, and a length rule is a fact about what was typed. Verification's four failure causes (expired, unknown,
  malformed, **now-taken**) share **one** refusal, and the now-taken case still *spends* the row.
  ⚠️ **Verification issues no session** — no token, no cookie: receiving an email is not knowing the password, and the
  password is the credential the visitor already chose. The admin is created `IsActive` with `MustChangePassword`
  **false**, unlike `provision-clinic`'s printed one-time password.
  ⚠️ **`ITransactionalEmailSender`/`SmtpTransactionalEmailSender` is the first email path bound to no clinic**, and it
  reads the per-install `Notification:Smtp:*` (`SmtpConfig`) rather than `ResolvedReminderSettings` — those resolve
  *per clinic*, and there is none. Routing it through `IReminderSettingsProvider` would compile and stop working. It is
  deliberately **not** an outbox either (every queue keys on `ClinicId`, and the visitor is waiting): an unconfigured
  host is a French refusal **before** anything is written, never a 202 over an email nobody can send. The link comes
  from `FrontendUrl` via `IPublicAppUrlProvider` — an Application-side seam because that project references no
  configuration package at all. No new config key.
- **Pluggable auth (`Auth:Mode` = `Cloud` | `Local`)**: Cloud is the original Auth0 path; **Local** (for offline Windows/LAN installs) issues its own HS256 JWTs against local email+password accounts. Backend seam: `ILocalAuthService`/`LocalAuthService` (+ per-install signing key via `LocalAuthConfig`), a mode-branched JWT setup in `Program.cs`, and `AuthController` (`login`/`setup`/`register`/`mode`/`change-password`). `CreateClinicCommand`/`JoinClinicCommand` branch to a Local path when a `Password` is present. Frontend seam: a single `useSession()` context (`web/lib/auth/session.tsx`) backed by either `CloudSessionProvider` (Auth0) or `LocalSessionProvider` (HttpOnly cookie), gated on `AUTH_MODE`. All Local behavior is additive; the Cloud path is unchanged. Offline admin lockout recovery is a console command (`dotnet run -- reset-admin-password`), not a web endpoint. *All 5 phases of the offline-Windows repackaging are complete — see `features/windows-desktop-app/`.*
  ⚠️ **The Local session slides, and it takes two halves to do so (`mobile-native-shells` P2)**: `RefreshTokenCommandHandler` mints a **fresh** refresh credential on every exchange *and* `web/app/bff/auth/token` re-sets the HttpOnly cookie with it — a backend-only change would rotate a credential nobody stores and no user would feel. Before this, the cookie kept the token issued at login, so a staff member working through the day was asked for their password again twelve hours in. It is **sliding expiry, not revoking rotation**: the superseded credential stays valid until its own expiry (stateless, nothing stores it — two tabs exchanging at once must both keep working), and `User.TokenVersion` is still the only revocation. `web/lib/auth/session-cookie.ts` is now the **single writer** of both `local_session` and `local_must_change_password`; they are written together because a sliding session would otherwise outlive the forced-password-change flag, leaving an app that looks usable while `LocalAuthEnforcementMiddleware` 403s every call.
- **A backgrounded phone still knows, and a lock screen learns nothing (`mobile-native-shells` P6)**: OS push, from
  the registry to the dispatcher. `DeviceRegistration` is **unique on its token**, which is what makes **rebinding**
  one deterministic write rather than a 409 — a shared reception tablet hands the app the *same* token to whoever
  signs in, and a second row would mean the colleague who left keeps receiving notifications on a device somebody
  else is holding. `PushDelivery` is the outbox, drained by the minutely `PushDispatchJob` on `NotificationJob`'s
  template (connectivity-gated, bounded per tick **and per clinic**, with the non-terminal **`Blocked`** status L3
  had to invent after the reminder queue starved — here from the start).
  ⚠️ **A push carries no message.** The payload is a category, a *fixed* French phrase for it (« Nouveau
  rendez-vous ») and opaque routing ids — no patient name, act, tooth, amount or free text; the rendered body stays
  in `StaffNotification` behind the app's own authentication, and the push is the doorbell for it. « The label equals
  the feed row's title » is held by comparing the two rows one call produces, not by a constant.
  ⚠️ **Five of the nine categories reach a locked phone**, and the line is *time-critical to a person*, not
  importance: booking, cancelling, rescheduling, the ~24 h reminder and the post-visit review. Low stock, expiring
  stock, a stale backup and a failed reminder stay in-app — waking a dentist at home for a box of gloves is how the
  OS permission gets revoked, and revoking it costs the five that matter. `StaffNotificationRules` **throws** on an
  unclassified category rather than defaulting either way.
  ⚠️ **The fan-out is a decorator over `INotificationGenerator`**, so one hook reaches every category the feed has
  or will have — editing twelve call sites is the `fixes-dont-propagate` shape. The feed is always written first and
  the push queued inside a swallow-and-log: the whole chain is a post-commit side effect of an operation that has
  already committed (AC-55).
  ⚠️ **Eligibility is re-checked at dispatch**, because a banner bypasses every request-time guard: the device may
  have been deregistered, its token rebound to a colleague, or the appointment cancelled since. And **the capability
  question is split on purpose** — `DeploymentProfile.PermitsOsPush` answers the deployment *kind* (so
  `SelfHostedLan` is ✗ whatever an operator configures) while `IOsPushAvailability` ANDs in the per-install FCM/APNs
  credentials, keeping `DeploymentProfile`'s « no operator setting can flip a capability » invariant intact.
- **The clinic runs on a phone, and the phone is a shell not a second frontend (`mobile-native-shells` Part 4)**:
  `mobile/android/` is a thin Kotlin `WebView` shell rendering the hosted server's **own** web bundle — five French
  states (`WebPage` · `Connecting` · `ServerAddress` · `Unreachable` · `UpdateRequired`), a runtime-configurable
  address, and `window.__clinicShell`. **`mobile/shared/bridge.md` is THE contract** (not `web/types/clinic-shell.d.ts`,
  which describes only what the bundle consumes today), and a change to its method set edits that file **and** bumps
  the shell's version. Phase 1's set is `saveFile` · `print` · `onPushToken`; every web-side read is a feature
  detection, so with the object absent — every browser — behaviour is byte-identical to the pre-bridge app, which
  AC-26 verifies by **deleting** it at runtime.
  ⚠️ **The address is never compiled in**, because one build serves a clinic's own PC on a LAN *and* a hosted
  backend on the internet; `ServerConfig.parseAddress` is a faithful port of `desktop/ServerConfig.cs`'s, so the two
  clients cannot disagree about what a typed address means. `network_security_config.xml` trusts **user-installed
  CAs** — without it the self-signed `SelfHostedLan` certificate makes that whole topology unreachable — while
  `onReceivedSslError` is **not** overridden anywhere, so a bad certificate still fails loudly.
  ⚠️ **Three omissions are load-bearing.** `onReceivedHttpError` is deliberately unhandled: a status means the
  server *answered*, and what it answered with is the app's own French error page — replacing that with a shell
  state is the blank app AC-74 forbids (same reason the launch probe reads a 404 on
  `/api/meta/client-requirements` as « no floor »). `android:configChanges` must list every configuration the
  activity handles or rotation destroys the WebView and AC-23 is unachievable from inside the web app. And insets
  are consumed as **padding on the root** rather than drawn under: targetSdk 35 forces edge-to-edge on Android 15,
  and whether a given WebView reports the navigation bar through `env(safe-area-inset-bottom)` is version-dependent.
  ⚠️ **A WebView cannot see its page's `fetch` responses**, so anything needing a response body is `web/` work, not
  shell work: the in-session 426 (Part 3) and the `must_change_password` 403 (AC-76) both live in
  `web/lib/api/client.ts`. The latter's login path was never broken — the `local_must_change_password` cookie plus
  `middleware.ts` cover it — but an **admin resetting the password of somebody already signed in** writes no cookie,
  so every call 403'd and surfaced the middleware's **English** sentence verbatim. `onMustChangePassword` now routes
  to `/change-password`, and it is the one place `client.ts` replaces a server-sent message.
  ⚠️ Not CI-runnable, operator-verified (as `desktop/` is) and in **neither** the `.sln` **nor** `web/`. Android Lint
  runs with `warningsAsErrors` as the module's only static gate. The **hardware walk is owed**, and `applicationId`
  is **provisional** — the bundle id is one of Part 8's deferred decisions and cannot change after first submission.
- **A phone that has been in a pocket is unlocked, not signed out (`mobile-native-shells` P7 step 2)**: the Local
  session's 30-minute inactivity limit used to clear the cookie and drop the user on `/login`, which on a phone —
  where the OS lock is already the barrier that matters — costs a dentist the fiche they had open several times a
  day. In a shell it now **pauses**: `window.__clinicShell.confirmIdentity()` (bridge version **1.1.0**, Phase 4's
  one new method) asks the OS to confirm the device owner, and `web/components/session-lock-gate.tsx` covers the
  app while it does. On success the timer re-arms and **the cookie is never cleared** — AC-57 says so explicitly,
  because a passing banner and a destroyed session look identical from outside.
  ⚠️ **The gate is opaque and the app stays mounted behind it**, and both halves are load-bearing: unmounting
  `children` would reload the page the resume exists to preserve, while a translucent one would leave a patient's
  record readable to whoever dismisses the OS prompt — which is the entire thing the limit is for.
  ⚠️ **Three attempts, and a dismissal counts as one.** Not tidiness: the cookie deliberately stays valid, so the
  counter is the only bound on how long a live session can sit behind a client-side overlay. `unavailable` (no
  enrolled biometric, no device credential, Android < 28) falls through **immediately** to the ordinary password
  screen — no error and no dead control (AC-60) — and **nothing is stored on the device** (AC-59): the shell asks
  the OS a yes/no question, and the session resumed is the one already in the WebView's cookie store.
  ⚠️ **`@JavascriptInterface` is synchronous**, so the result comes back through a separate global
  (`__clinicShellDeliverIdentityResult`) resolving a pending request by id — `onPushToken`'s shape, and outside
  `__clinicShell` for the same reason: deleting the bridge must not leave a live resolver.
- **A stale app says so, once, instead of failing screen by screen (`mobile-native-shells` P3)**: a native shell sends
  **`X-Client-Version`**; `ClientVersionMiddleware` refuses a build below the operator's `Clients:MinimumShellVersion`
  with **426** and `code: "client_too_old"`, and `<ClientVersionGate>` turns that into one full-screen « Mise à jour
  requise » with the store link. `GET /api/meta/client-requirements` publishes the floor, the current release and both
  store URLs — anonymous, and **the one `/api` route exempt from the floor**, because otherwise the single endpoint
  that says where to update is the single endpoint a refused client cannot read. `Models/ClientRequirements` is both
  the DTO that route returns *and* the object the middleware measures against, so the floor a client is told about is
  the floor it was refused by.
  ⚠️ Three things it must **not** do, and each is why the code looks as it does. (a) It runs **before**
  `UseAuthentication` — a stale shell's *login* has to 426, not 401, because 401 reads as « signed out » and a login
  screen the app can never get past is worse than the refusal. (b) It is scoped to **`/api`**: the front door also
  serves the web app, and 426-ing the page would replace the French update state with raw JSON. (c) **Anything
  unreadable passes** — no header (every browser, every server-side BFF hop), a malformed version, an unset or
  typo'd floor. A mistyped setting must refuse nothing, never everything.
  ⚠️ The client half was **not** just adding a header: fourteen raw-`fetch` sites across eight modules hand-wrote
  their own `Authorization` object, so every PDF, CSV export and patient-file upload would have carried the token
  and silently omitted the version — the floor covering part of the app only. They all now call the one exported
  **`apiHeaders(token, contentType)`** in `client.ts`, and the `api-headers` check fails on a `Bearer` literal
  anywhere else. This is `fixes-dont-propagate` caught at the moment the helper gained a second job.
- **Local-disk file storage (Phase 2)**: the single `IFileStorage` seam is mode-branched — `LocalDiskFileStorage` (Local, blobs under `FileStorage:BasePath`) vs `MinioFileStorage` (Cloud + hosted). Additive; Cloud unchanged. Since `multi-tenant-cloud` US-5 both compose their keys through the same `ClinicStorageKey` — see that bullet above.
- **Connectivity awareness (Phase 3, Local mode)**: internet reachability is judged by the **server** (LAN clients may have no egress). `IInternetProbe`/`InternetProbe` (Singleton, cached) backs an anonymous, Local-only `GET /api/connectivity` (404 in Cloud) that the frontend polls via `ConnectivityProvider`/`useConnectivity()`. The one internet-dependent feature — Google Calendar — visibly disables offline and auto re-enables; appointments not yet pushed to Google show a "non synchronisé" badge + manual "Push to Google" (`AppointmentDto.IsSyncedToGoogle`). Cloud gets a static "online" default and behaves as before.
- **LAN hosting & security gates (Phase 4, Local mode)**: hardens the API for offline-LAN hosting, all additive/gated to Local (Cloud byte-for-byte unchanged). (a) Fail-closed authorization — `AuthorizationPolicies.ConfigurePolicies(options, isLocalMode)` installs a `FallbackPolicy = RequireAuthenticatedUser()` in Local, so anything without an explicit `[AllowAnonymous]` returns 401; the exact allow-list is pinned by `ControllerAuthorizationCoverageTests`. (b) Loopback-only `/hangfire` (`HangfireAuthorizationFilter` + `LocalRequest.IsLoopback`, shared with the `setup` gate / AC-1.2a) — since `cloud-security-and-tenant-isolation` this is loopback-only in **both** modes (Cloud no longer authorizes everyone). (c) Config-driven CORS (`CorsOrigins` — LAN origins via `Cors:AllowedOrigins`), HTTPS cert binding + guarded redirect, and Kestrel bind (`Https:*`/`Hosting:*`) in `Program.cs`. (d) The OAuth callback no longer rewrites appsettings; Google refresh tokens are stored **per-clinic on the `Clinic` entity** (`GoogleRefreshToken`/`GoogleCalendarId`, DB). *(Phase 5 closed the Phase-4 cert-downgrade gap — a set-but-missing `Https:CertPath` now fails startup loud instead of dropping to HTTP. `cloud-security-and-tenant-isolation` then closed the OAuth `state` gap: `connect` mints CSRF `state` into a server cache + HttpOnly double-submit cookie, validated on `callback`; the old singleton `IGoogleTokenStore`/`FileGoogleTokenStore` was retired for per-clinic DB storage.)*
- **Packaging, installers & manual backup (Phase 5, Local mode)**: turns the app into a self-contained offline-LAN Windows product, all additive/gated to Local (Cloud unchanged). (a) **Same-origin front door** — in Local, Kestrel is the *single* browser-facing HTTPS endpoint: `/api/*` runs in-process and a **YARP** catch-all reverse-proxies every other route (pages, `/_next/*`, `/bff/*`) to the co-located Next server on loopback; the web build ships with relative `NEXT_PUBLIC_API_URL=/api`, so TLS terminates once and no server IP is baked in. Frontend BFF auth routes moved `/api/auth/*` → **`/bff/auth/*`** to avoid colliding with the proxied `/api/*`. (b) **Self-generated HTTPS** — `CertificateProvisioner` mints a CA + SAN server cert into `.local/` on first boot (idempotent); HTTP binds loopback-only, HTTPS (5001) is the only LAN port. (c) **Windows service + startup diagnostics** — `UseWindowsService()` auto-start; `StartupDiagnostics` turns DB-down / port-in-use into clear French messages + non-zero exit (console/log/Event Log). (d) **One-click backup** — admin-only `POST /api/backup` → `PgDumpBackupService` (`pg_dump` custom-format dump + file-storage copy to a timestamped folder; fails loud, never a silent partial). (e) **`LocalInstallPaths`** anchors `.local/`, `Files/`, `logs/` to the install dir (a service's CWD is `System32`). (f) **`desktop/`** WebView2 shell + **`packaging/`** publish script and Inno Setup server/client installers (bundled PostgreSQL 16, Node, NSSM, CA-trust import) — **operator-verified (R-1)**, not CI-runnable.
- **Google Calendar sync is asymmetric + per-clinic**: each clinic uses its **own** Google connection (`Clinic.GoogleRefreshToken`/`GoogleCalendarId`) — no shared account. App→Google runs inline on appointment create/update (using the appointment's clinic). Google→App is implemented but has **no scheduled job** — the disabled `GoogleCalendarSyncJob` class + its commented recurring registration were removed as dead scaffolding (`french-localization-and-cleanup`; `Program.cs` keeps only a defensive `RemoveIfExists`). Google→App runs only via the manual `GoogleCalendarController` endpoint (scoped to the caller's clinic).
- **Background jobs**: Hangfire is wired; **two minutely recurring jobs** — **`NotificationJob`** (SMS/WhatsApp appointment-reminder dispatcher), connectivity-gated and no-op until configured (a `Reminders` channel + credentials), and **`AppointmentProgressJob`**, which moves a booked visit to **`InProgress`** once its own slot has begun (14:00→15:00 reads « En cours » at 14:16 with nobody pressing anything). The second is deliberately **not** connectivity-gated — it writes a status, so it must work on an offline LAN install — and it **only ever starts** a visit: leaving a slot is not evidence the patient came, so « Terminé » and « Absent » stay human decisions. ⚠️ Its selection is split across two layers because `Duration` is persisted as **ticks** behind a value converter, so `AppointmentDateTime + Duration` has no SQL translation and the database's own trigger-maintained `AppointmentEndDateTime` is deliberately unmapped: the query cuts a **one-day** window in SQL and the exact end-of-slot test runs over that bounded set. See `api/ClinicManagement.API/CLAUDE.md` for the residual that leaves and the two traps behind it (the broadcast key is asked of `RealtimeResourceResolver` rather than typed, and `Appointment.CanTransition` counts a self-assignment as legal). **Plus two daily**: **`StockExpiryJob`** (approaching-expiry stock alerts, `audit-sections-3-to-10`) and **`SubscriptionWarningJob`** (`clinic-subscription` Part E — the four expiry thresholds, registered **only** where `RequiresSubscription`, with a `RemoveIfExists` in the else). Neither daily one is connectivity-gated: their alerts are in-app, so they must work on an offline LAN install. It exists as a *job* rather than a write hook because expiry is crossed by the **passage of time** — the box nobody has touched is exactly the case the alert is for, and no write happens on the day it enters the lead window; a write-only trigger would have been a notification that never fires for its main case. On-demand `PdfGenerationJob` also fires. (`AISummaryJob` was removed in `reliability-and-polish`; the calendar-sync job in `french-localization-and-cleanup`.)
- **Billing / CNAM / treatment plans (deep, fully-wired subsystems)**: the product's richest area, largely French already. **Billing** — `Invoice`/`InvoiceLine`/`Payment` (notes d'honoraires: draft→issued→paid, VAT + stamp, per-clinic billing settings), `InvoicesController` + `BillingController` (unified per-patient `solde patient`). **Treatment plans / devis** — `TreatmentPlan`/`TreatmentPlanItem` + payment `Installment` schedule (`TreatmentPlansController`), seeded from the odontogram by a **frontend** prefill (there is no backend seed command). Since `treatment-plan-workspace` the loop is **bidirectional**: a plan derives each act's état from the appointments pointing at it and shows the invoice that bills it (`TreatmentPlanWorkflowProjection`, two batched reads per request), has its own workspace route `/treatment-plans/[id]`, orders its acts (`SequenceNumber`), and can be **amended after acceptance** (`RevisionNumber`) instead of cancelled and retyped — unless a non-cancelled invoice already represents it. **CNAM** — `CnamNomenclatureEntry`/`CnamLetterValue`/`DentalActCode` (`CnamNomenclatureController`/`DentalActsController`) back the **BS1 bulletin** reimbursement estimate + overlay renderer (`CnamBs1BulletinRenderer`, Infrastructure). Also `Medication`/`MedicationActiveIngredient` (ordonnance picker), `ToothState` (odontogram), `DashboardController` (the composed dashboard read — see below). The **CNAM/medication/dental-act catalogs are per-clinic** (each clinic seeded the same defaults via `IClinicCatalogSeeder`, then edits stay private) — despite some stale "global reference data" docstrings still in those entities.
- **Clinical-workflow-depth operational features (built)**: beyond scheduling, the app has an **interactive odontogram** (chart/diagnose per tooth — `OdontogramController`, `ToothState`), **recurring appointment series** (`RecurringAppointment`, above), a **waiting list / salle d'attente** (`WaitingListController`, promote-to-appointment), **lab work orders / bons de prothèse** (`LabOrdersController`, status lifecycle), **patient recall / relance** (`RecallController` + `Clinic.RecallIntervalMonths`, SMS/WhatsApp send), a **caisse / expenses** ledger (`ExpensesController` + `BillingController` caisse summary), and a **post-visit-review** prompt (a `StaffNotification` category deep-linking staff to record a finished visit). Each is clinic-scoped with a matching page under `web/app/`.
- **Dead-code cleanup (`french-localization-and-cleanup`)**: the **domain-events pipeline was removed** — `Domain/Events/*`, `IDomainEvent`, and `AggregateRoot`'s event list were dead (`SaveChangesAsync` never drained events; zero `INotificationHandler`s). Side effects are produced inline post-commit via `INotificationGenerator`/`IReminderScheduler`. *(`RecurringAppointment` is no longer merely "reserved" — the clinical-workflow-depth feature wired it: `AppointmentsController` `GET/POST recurring` + `POST recurring/{id}/cancel`, `RecurringAppointmentRepository`, expanded into `Appointment` rows.)*
- **Clinic-scoped SignalR realtime (built)**: every mutating command auto-broadcasts a `entityChanged` event to the caller's `clinic-{id}` group via the Application `RealtimeBroadcastBehavior` → `IRealtimeNotifier` → API `ClinicHub` (`/hub/clinic`); the resource key is derived from the command's namespace (so new commands broadcast for free). The frontend subscribes with `useClinicRealtime(resource, refetch)` (`web/lib/realtime/`) so a peer's edit live-refreshes appointments, patients, stock, invoices, treatment plans, catalogs, users, notifications, la caisse, and — since `audit-sections-3-to-10` closed audit § 9.1 — the **salle d'attente, lab orders, relances, recurring series, « Mon profil » and the patient's documents tab**. The **dashboard** now subscribes to all nine keys its figures depend on (it watched only four, so the à-traiter counts and la caisse went stale under a peer's edit). Excluded areas: Auth, AI, Backup, Connectivity, and all queries. ⚠️ The two sides are now held together by a **contract test**, not by discipline: `RealtimeResourceResolverTests` reflects over every `IRequest` for the emitted set, **parses `clinic-hub.ts`** for the declared set, and fails unless they are equal **in both directions**. It replaced a hardcoded `[InlineData]` table that stayed green for the whole period five keys (`expenses`, `doctors`, `laborders`, `recall`, `waitinglist`) were broadcast with nothing listening — the derived-vs-listed lesson `verify-schema` also embodies.
- **In-app staff notification center (built)**: a real, clinic-scoped in-app feed — header bell + unread badge → panel (newest-first, per-user read/unread, mark-all-read, deep-links), live over the SignalR `"notifications"` key above. Backed by a **`StaffNotification`** aggregate (one shared row per event) + per-user **`NotificationRead`** markers (no write-time fan-out). Notifications are generated **best-effort, post-commit** by an `INotificationGenerator`/`NotificationGenerator` seam called inline from the appointment/stock command handlers (appointment created/cancelled/rescheduled, ~24h reminder, not-low→low stock crossing) — a generation failure logs at Error but **never** fails/rolls back the core operation. This is **in-app only**; the dormant email/SMS `Notification` entity + `NotificationService` stay untouched. The actor who caused an event is excluded from their own feed.
- **Real outbound SMS/WhatsApp reminders** (feature `sms-whatsapp-reminders`): the previously-dormant `Notification` outbox is now live for **SMS + WhatsApp** appointment reminders — `IReminderChannelSender` (`HttpSmsSender`/`WhatsAppSender`) + `RemindersConfig`/`ReminderSchedule`/`ReminderPhone`, enqueued best-effort post-commit by `IReminderScheduler`/`ReminderScheduler` from the appointment handlers and dispatched by the connectivity-gated minutely `NotificationJob`. Secrets come from env (or per-clinic, encrypted). Per-clinic settings (`ClinicReminderSettings` + `IReminderSettingsProvider`) override the per-install config: channel toggles, sender identity, **gateway/Graph URLs, lead-time tiers and the message wording** (all admin-editable in `reminder-settings.tsx` — `reliability-and-polish`), so a channel can be turned fully on without a server-config edit. The settings GET returns a per-channel `effectiveStatus` (`configured`/`not_configured`) that drives a "sendable vs. warning" badge (a WhatsApp OAuth "Connecté" downgrades to a warning when the resolved settings still can't send), and `GET /api/clinics/reminder-status` surfaces the recent outbox rows (sent/pending/failed + reason).
- **The patient AI summary is gone, and the claim that used to be here was false on both halves (`adoption-qa-i-access-control-and-audit` I4)**: `GET /api/patients/{id}/ai-summary` was documented as "on the patient detail page … connectivity-gated" while having **zero callers** in `web/` (the button was removed and the endpoint outlived it) and **no** `IInternetProbe` gate — so on an offline LAN install it hung ~205 s on a `HttpClient` with no timeout before failing. What it did do was POST a patient's full name, allergies, every medical- and family-history entry, and every dental record — teeth, money and all free-text notes — to `router.huggingface.co`, with no record cap, no consent flag, no audit of which patient was sent, and a class-level `[Authorize]` as its only gate, so any secretary could trigger it. It was **deleted** rather than fixed (endpoint, `GetPatientAiSummaryQuery`, `PatientAiSummaryDto`, `patientsApi.getAiSummary`): keeping it would have required a policy, a probe gate, a timeout, a record cap and an audit row to restore a feature no screen asked for. `IHuggingFaceAIService` is **gone too** — its last caller, the AI chat, was deleted with the whole subsystem (see the Infrastructure guide's « AI — removed »), so no code in this product now reaches an inference endpoint at all. *(The old placeholder `PatientSummaryService`/`IPatientSummaryService` + the disabled `AISummaryJob`, the never-registered `GoogleAIService`/`IGoogleAIService`, and the dormant email `NotificationService`/`INotificationService` were removed as dead code in `reliability-and-polish` — there is no AI backend at all any more, and the live outbound reminders go through the `IReminderChannelSender` senders below.)*
- **`ValidationBehavior` is inert**: no FluentValidation validators exist; handlers validate inline and return `Result.Failure`.
- **Frontend data-wiring**: the dashboard, `appointment-list`, the stock feature, and the notification center are all API-wired (`dashboardApi`, `useAppointments`, `stockApi`, `notificationsApi`/`useNotifications`). The header **search** is a live patient lookup (type → results → open the patient). *(The orphan `notifications-list` sample component and the redundant `dental-chart` tooth chart were removed in `reliability-and-polish`; the read-only summary chart now reuses `record-tooth-chart`.)*
- **Security posture (mostly hardened by `cloud-security-and-tenant-isolation`, PR #11)**: committed `api/.../appsettings.json` secrets were **retired** — DB connection string, `GoogleCalendar:ClientSecret`, WhatsApp/SMS/Meta tokens are now empty strings + `// SECRET` comments; supply real values via env / user-secrets / `.local/` / the installer's `appsettings.Production.json`. An empty DB connection string fails startup loud (`return 1`). The Hangfire filter is loopback-only in **both** modes; the Google OAuth `state` is validated; Google refresh tokens live per-clinic in the DB. **Residual (Cloud)**: the authorization `FallbackPolicy` stays null (a Cloud controller without `[Authorize]` is still anonymous — Local fails closed), and a few non-secret real values remain in config (`Auth0:Domain/Audience`, `GoogleCalendar:ClientId`; `Auth0:ManagementApi` still has `YOUR_*` placeholders). Treat with care.

## Root-level setup / reference docs

| File | Topic |
|------|-------|
| `README.md` | (minimal) |
| `AUTH0_SETUP.md` | Auth0 tenant/app configuration |
| `GOOGLE_CALENDAR_SETUP.md` / `_FR.md` | Google Calendar OAuth setup (EN/FR) |
| `GOOGLE_CALENDAR_SYNC_ARCHITECTURE.md` | Calendar sync design |
| `SYNC_TESTING_GUIDE.md` | How to test calendar sync |
| `api/CLINIC_MANAGEMENT_FLOW.md`, `MULTI_CLINIC_SETUP.md`, `ROLE_ASSIGNMENT_IMPLEMENTATION.md`, `ENTITY_UPDATE_GUIDE.md`, `IMPLEMENTATION_SUMMARY.md` | Backend feature/process docs |
| `packaging/README.md` | Local/offline-LAN operator guide (Phase 5): publish + installers, bundled PostgreSQL, backup/restore, admin recovery, per-AC verification checklist |

> When code changes, update the nearest `CLAUDE.md` so this map stays accurate.
