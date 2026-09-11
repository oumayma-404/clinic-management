# Clinic Management — Repo Guide

Full-stack **dental/medical clinic management** system (Tunisia-targeted: French UI labels, Tunisian
governorates). Multi-tenant by clinic, with patient records, appointments + Google Calendar sync,
medical/dental documents, file storage, billing and CNAM.

> **Read this first.** This file is the **map**: where things live, how to run it, and where the reasoning is
> written down. Each major folder has its own `CLAUDE.md`; cross-cutting design lives in
> [`ARCHITECTURE.md`](ARCHITECTURE.md); each shipped feature has `features/<slug>/notes.md`. Jump to the right
> one instead of re-reading source.

## Stack at a glance

| Layer | Tech | Location |
|-------|------|----------|
| Frontend | Next.js 16 (App Router), React 19, TypeScript, Tailwind v4, shadcn/ui | `web/` |
| Backend API | .NET 8, Clean Architecture, ASP.NET Core, MediatR (CQRS), Hangfire | `api/` |
| Database | PostgreSQL 16 (EF Core) | docker-compose `postgres` |
| Object storage | MinIO (S3-compatible) | docker-compose `minio` |
| Auth | **Self-issued** email+password accounts, HS256 JWTs signed with a per-install key — on the offline LAN install **and** the hosted multi-tenant backend alike. Clinic membership resolved server-side. ⚠️ Auth0 (`Auth:Mode=Cloud`) is **retired** along with the `CloudBrowser` deployment kind; `Auth:Mode` has one value and every deployment difference is a named capability on `Deployment:Profile`. | both |
| External | Google Calendar (two-way sync), SMS/WhatsApp reminders, Meta WhatsApp onboarding | `api/...Infrastructure/Services` |

⚠️ **There is no AI subsystem.** No code in this product reaches an inference endpoint: the chat, the patient
AI summary and every service behind them were deleted. See
[`features/adoption-qa-i-access-control-and-audit/notes.md`](features/adoption-qa-i-access-control-and-audit/notes.md).

⚠️ **Two deployment kinds, and almost every design decision turns on one of them**: `SelfHostedLan` (a
clinic's own Windows PC serving its LAN) and `HostedMultiTenant` (one hosted backend, many clinics, the
product's own accounts). They are a `DeploymentProfile` of ~18 **named capabilities** — never a boolean. See
[`features/multi-tenant-cloud/notes.md`](features/multi-tenant-cloud/notes.md).

⚠️ There was a third, `CloudBrowser` (one hosted backend, Auth0), and it is **retired** — the profile, the
`@auth0/nextjs-auth0` dependency, both session providers, the management-API client and its no-op twin are all
gone. `Auth:Mode` no longer selects anything, and an absent `Deployment:Profile` with a non-`Local` auth mode
now **throws** rather than guessing between two profiles that disagree about accounts, storage, subscriptions
and the second factor. What deliberately stayed is the `User.Auth0Sub` **column name** — it is the token
subject, written and read on every authenticated request, so the name is a legacy of where it came from and
not a live dependency.

## Layout

```
clinic-management/
├── api/                          .NET 8 Clean Architecture solution (ClinicManagement.sln)
│   ├── ClinicManagement.Domain/         → CLAUDE.md  (entities, value objects, repo interfaces)
│   ├── ClinicManagement.Application/     → CLAUDE.md  (CQRS features, MediatR pipeline, DTOs, Result<T>)
│   ├── ClinicManagement.Infrastructure/  → CLAUDE.md  (EF Core, repos, external services, DI)
│   ├── ClinicManagement.API/             → CLAUDE.md  (controllers, SignalR hubs, background jobs, Program.cs startup)
│   └── ClinicManagement.UnitTests/       → CLAUDE.md  (~90 xUnit+Moq classes mirroring every layer; guard tests)
├── e2e/                          Playwright HOT-PATH suite — the money/fiche/devis gate `UnitTests` structurally
│                                   cannot have (nothing there touches a database) and `web`'s gate cannot see
│                                   (tsc + check:responsive + build all passed while every Completed plan threw
│                                   during render). Arranges over the API, ACTS in the browser, asserts on the
│                                   coupled reads. Runs in CI's `e2e` job on a bootstrapped database, and as a
│                                   READ-ONLY smoke after a hosted deploy. → features/e2e-hot-paths/
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
│                                   docker-compose.prod.yml   = the shared INFRA base (certs, postgres, minio,
│                                                               caddy, backup, pitr) — not runnable alone since
│                                                               CloudBrowser's own api/web services were removed
│                                   docker-compose.hosted.yml = HostedMultiTenant (own accounts) — `extends` prod's infra
│                                   docker-compose.registry.yml = overlay setting `image:` on api/web/console so the
│                                                               server pulls them instead of building; nothing else
├── backend/                      EMPTY (only .idea/) — ignore
├── .github/workflows/            ci.yml = the api · web · desktop · android gate (see below)
│                                   ios-shell.yml = the iOS shell's only compiler, path-filtered (billed macOS runner)
│                                   client-installer.yml = « Client release ». On a `desktop/**` change landing on main
│                                                       (or a `client-v*` tag): builds the shell, packs a **Velopack**
│                                                       feed with `vpk`, scp's it to the VPS's `deploy/updates/`, and
│                                                       tags the release. Clients then self-update silently — per-user,
│                                                       delta (~160 KB), no UAC. `windows-latest`; `contents: write`
│                                   deploy-hosted.yml = `workflow_dispatch` deploy to a VPS: build the three heavy
│                                                       images → GHCR → ssh `pull` + `up -d --no-build` → /health →
│                                                       verify-schema. Builds THROUGH compose, so `web`'s build args
│                                                       have one home. See deploy/README.md § Déploiement depuis GitHub
├── docker-compose.yml            postgres (5432) + minio (9000 API / 9001 console) — LOCAL DEV only
└── *.md (root setup docs, see below)
```

The **dependency direction** in `api/` is strict Clean Architecture: `API → Application → Domain`, with `Infrastructure` implementing Application's outbound interfaces. Domain has no infrastructure dependencies.

## Where things live (quick index)

- **A REST endpoint / route** → `api/ClinicManagement.API/Controllers/` (40 controllers). Controllers are thin MediatR pass-throughs.
- **Business logic / a use case** → `api/ClinicManagement.Application/Features/<Area>/{Commands,Queries}/` (handlers).
- **An entity / business rule** → `api/ClinicManagement.Domain/Entities/`.
- **DB schema / a query implementation / EF config** → `api/ClinicManagement.Infrastructure/Persistence/` + `Repositories/`.
- **An external integration** (Google Calendar, files, notifications, SMS/WhatsApp) → `api/ClinicManagement.Infrastructure/Services/`.
- **Realtime (live refresh across clients)** → SignalR `ClinicHub` at `/hub/clinic` (`api/ClinicManagement.API/Hubs/`), fed by the Application `RealtimeBroadcastBehavior`; frontend `web/lib/realtime/`.
- **A page / screen** → `web/app/<route>/page.tsx` (App Router).
- **A UI component** → `web/components/` (feature) or `web/components/ui/` (shadcn primitives).
  ⚠️ **Before writing any frontend code, read `.claude/rules/frontend-web.md`** — the device + UX contract this
  app is held to (usable at 320 px · 44 px targets on a **coarse pointer**, not on a breakpoint · a table has a
  card form · a heavy dialog becomes a sheet in `dvh` · no capability removed by a layout decision), and the
  gate: `npm run check:responsive` + `npx tsc --noEmit` + `npm run build`, then an eye pass at
  320/390/820/1180/1440 px. There is no test runner and no working ESLint in `web/`, so that *is* the gate —
  and it is what CI's `web` job runs. It is the directive form of `web/CLAUDE.md`'s conventions, not a second
  copy of them.
- **Frontend → backend calls** → `web/lib/api/` (per-resource modules over `client.ts`).
- **A backend test** → `api/ClinicManagement.UnitTests/` (xUnit + Moq, one folder per layer).
- **How to verify a change** → [`.claude/rules/verification.md`](.claude/rules/verification.md) — one full pass
  over every affected area, collect *all* failures, fix them together, re-run. The unfiltered suite is the gate
  (a `--filter` once hid 21 failures for a whole feature), a failing check is a probe bug until excluded, and
  « no error string » is not « loaded ». `frontend-web.md` is its rendering-side twin.

## Running locally

```bash
docker compose up -d              # postgres + minio
cd api/ClinicManagement.API && dotnet run    # API (default http://localhost:5000)
cd web && npm install && npm run dev          # frontend (http://localhost:3000)
```
Frontend talks to the API via `NEXT_PUBLIC_API_URL` (default `http://localhost:5000/api`). EF migrations live in `Infrastructure/Migrations`.

## Architecture notes — what shipped, and why it is that way

Each line is one notes file, followed by the notes inside it. They are **verified, and they may surprise you**:
they record the decision that is easy to undo by accident, not the happy path. They lived in this file until
they reached 27,000 words, which every session paid to load — `spec.md` is what was asked for, `stories/` is
how it was built, `notes.md` is what shipped.

**How the system works** — cross-cutting, belonging to no one feature

- [`e2e-hot-paths`](features/e2e-hot-paths/findings.md) — **the coupling written down**: `coupling-matrix.md`
  is the eleven surfaces one fiche save moves, the eight writers, and every guard with the remedy it names;
  `scenarios.md` is 268 hot-path scenarios (125 tier-0) plus an appendix of the **probe traps** that each
  produced a convincing false defect report; `findings.md` is what the 2026-09-08 pass found.

- [`ARCHITECTURE.md`](ARCHITECTURE.md) — There is a CI gate now, and before it there was none for `api/` or `web/` · Multi-tenancy · Pluggable auth (`Auth:Mode` = `Cloud` | `Local`) · Google Calendar sync is asymmetric + per-clinic · Background jobs · Billing / CNAM / treatment plans (deep, fully-wired subsystems) · Clinical-workflow-depth operational features (built) · Dead-code cleanup · Clinic-scoped SignalR realtime (built) · In-app staff notification center (built) · Real outbound SMS/WhatsApp reminders · Security posture (mostly hardened by `cloud-security-and-tenant-isolation`, PR #11)

**Money, and the ledgers behind it**

- [`data-and-money-integrity`](features/data-and-money-integrity/notes.md) — Optimistic concurrency, solution-wide · Money is correctable, not immutable · Patient records resist destruction · `reconcile-money` (Local-mode console verb)
- [`caisse-extrait`](features/caisse-extrait/notes.md) — La caisse has a statement, and it is a read · A session's payment reaches the till
- [`patient-outstanding-breakdown`](features/patient-outstanding-breakdown/notes.md) — « Solde dû » says what it is made of, and each row is settled where it is read
- [`adoption-gaps-remediation`](features/adoption-gaps-remediation/notes.md) — Re-saving a fiche tops its note up, and la caisse's day is Tunisian (Part 2)
- [`audit-sections-3-to-10`](features/audit-sections-3-to-10/notes.md) — `verify-schema` (Local-mode console verb) · Tunisia is UTC+1, and `ClinicClock` is the only thing that knows it (P6) · A visit knows whether it was billed (P6) · One CNAM calculator (P6)

**The clinical loop**

- [`patient-form-density`](features/patient-form-density/notes.md) — La fiche patient tient sur onze lignes, et la denture en a trois
- [`visit-closure-worklist`](features/visit-closure-worklist/notes.md) — A séance is not finished until three things are answered, and the app now asks
- [`calendar-import-revert`](features/calendar-import-revert/notes.md) — An import was a run, a run can be undone — and then the import was retired · A séance leaves the list without claiming anything about it
- [`multi-act-appointments`](features/multi-act-appointments/notes.md) — A séance is several acts, and the scalars are derived
- [`bridge-identity-and-tooth-gesture`](features/bridge-identity-and-tooth-gesture/notes.md) — A bridge's extent cannot be read off the arch either · The gesture stopped being a mode · The pontique question is now asked, and there are three roles · Three roles as two subset lists, and a fourth would not fit
- [`multi-seance-treatment-steps`](features/multi-seance-treatment-steps/notes.md) — An échéance nobody agreed to is not late · An act's end state is charted when the act is FINISHED · A séance remembers the teeth the last one treated · A séance says what it WAS · The header is one action and a menu · Deux surfaces annonçaient l'étape SUIVANTE comme si elle avait eu lieu
- [`appointment-negotiated-price`](features/appointment-negotiated-price/notes.md) — A price agreed on the telephone is the price billed
- [`prescription-fiche-de-soins`](features/prescription-fiche-de-soins/notes.md) — La séance prescrit, et l'ordonnance est une vraie ordonnance · Un examen est une ordonnance DISTINCTE · On peut voir le document sur place · Elle n'efface jamais · Sexe et poids sont retirés
- [`patient-file-uploads`](features/patient-file-uploads/notes.md) — What may be uploaded has one authority, and the browser is told rather than trusted
- [`clinic-file-decoders`](features/clinic-file-decoders/notes.md) — A file you upload is a file you can look at: HEIC, TIFF and ZIP decode in the browser, and every hosted file finally carries a thumbnail
- [`dicom-interactive-viewer`](features/dicom-interactive-viewer/notes.md) — A radiograph you can read, not just look at: window/level, zoom, frame scrolling and a ruler that refuses to invent millimetres
- [`mesh-interactive-viewer`](features/mesh-interactive-viewer/notes.md) — The scan you can turn, measure and mark: STL, PLY and OBJ get a thumbnail and a viewer, and every length says which unit it assumed
- [`large-file-transfer`](features/large-file-transfer/notes.md) — A download is read as it is sent, not copied into the server first (Part 1 of four; the other three are named and not started)

**Reads, lists and catalogues**

- [`list-pagination`](features/list-pagination/notes.md) — Every list read is a page, and search is a database question
- [`dashboard-insights`](features/dashboard-insights/notes.md) — The dashboard is a composed read, not a KPI bag
- [`stock-fournisseurs`](features/stock-fournisseurs/notes.md) — A fournisseur is a record with a number, not a name on a row

**Getting back in**

- [`password-recovery-gaps`](features/password-recovery-gaps/notes.md) — A forgotten password has a way back its owner can take alone, and it never touches the second factor · The vendor's console can re-credential an account, and says so in the journal · `platform-account --reset-password`

**Who may do what, and whose data it is**

- [`adoption-qa-i-access-control-and-audit`](features/adoption-qa-i-access-control-and-audit/notes.md) — Who may do what, and who did it · The patient AI summary is gone, and the claim that used to be here was false on both halves (I4)
- [`multi-tenant-cloud`](features/multi-tenant-cloud/notes.md) — Three deployment topologies, one capability per question (US-1 / Part A) · The hosted runtime can be watched, and it cannot race itself (US-6 / Part F) · Every blob knows whose it is (US-5 / Part E)
- [`windows-desktop-app`](features/windows-desktop-app/notes.md) — Local-disk file storage (Phase 2) · Connectivity awareness (Phase 3, Local mode) · LAN hosting & security gates (Phase 4, Local mode) · Packaging, installers & manual backup (Phase 5, Local mode)

**The vendor's side of the product**

- [`platform-console`](features/platform-console/notes.md) — The vendor has a private console, and it cannot read a patient record (Part 1) · The portfolio is a counter table, not a query over the ledger (Part 2) · The console records what it looked at, and the record is readable (Part 3) · The vendor records a payment and the cabinet unlocks (Part 4) · A mis-keyed payment is corrected, never erased (Part 5) · A cabinet is stopped for abuse, and never told it has expired (Part 6) · The verification found the hole the six parts before it could not (Part 7) · The vendor can put a lost authenticator right, and the journal finally names who did
- [`clinic-subscription`](features/clinic-subscription/notes.md) — A cabinet's right to record work is a dated entitlement (all 7 parts)
- [`vendor-whatsapp-messaging-quota`](features/vendor-whatsapp-messaging-quota/notes.md) — The vendor buys the WhatsApp messages and a cabinet spends them (Parts 0–5)
- [`clinic-self-signup`](features/clinic-self-signup/notes.md) — A clinic can let itself in, and nothing exists until the email is answered

**Backup, archive and recovery**

- [`backup-works-everywhere`](features/backup-works-everywhere/notes.md) — Backup works out of the box, or says whose job it is
- [`clinic-data-archive-and-restore`](features/clinic-data-archive-and-restore/notes.md) — A cabinet takes its whole record out, and can put it back
- [`clinic-recovery-points`](features/clinic-recovery-points/notes.md) — Something now PRODUCES an archive, and the practice is told when none has left the building

**Reaching people, and the devices they use**

- [`adoption-qa-l-residual-blockers`](features/adoption-qa-l-residual-blockers/notes.md) — A reminder queue that cannot starve, and never announces the wrong day (L3) · Backup is automatic, verified, and restorable (L4) · A cheque has an identity, and it travels with the money (L8, slice A) · Data comes back in as CSV, and the dry run is the product (L5, import half) · Data leaves the product as CSV (L5, export half) · La caisse says *how* the money came in, and a cheque has somewhere to be chased (L8 slice B) · A reimbursement estimate now knows what is left (L10) · An arrêt de travail is printed on the caisse's own form (L11) · Money and clinical work know who earned them (L9)
- [`mobile-native-shells`](features/mobile-native-shells/notes.md) — A backgrounded phone still knows, and a lock screen learns nothing (P6) · The clinic runs on a phone, and the phone is a shell not a second frontend (Part 4) · A phone that has been in a pocket is unlocked, not signed out (P7 step 2) · A stale app says so, once, instead of failing screen by screen (P3)

## Traps that bite (each one fails silently)

The notes above whose cost is highest and whose symptom is *not* an error. Read the linked note before
touching the area.

- **EF scaffolds an `xmin` column PostgreSQL refuses.** `Entity<TId>.Version` maps onto the system column, so
  the differ emits `AddColumn<uint>("xmin")` for all 38 entities. Three migrations ship a deliberately **empty
  `Up()`**, kept for their model snapshot alone. Check any new migration for it — and for a scaffolded
  `DropColumn` placed *above* a backfill that reads the column it drops.
- **A handler catch-all must carry `when (ex is not ConflictException)`** or a 409 is flattened into a generic
  failure. Only catches that *return a `Result`* are filtered; a log-only post-commit catch must still swallow.
- **`Version == 0` means "not supplied" and skips the concurrency check.** That is what keeps the jobs and the
  Google→App sync working — and what leaves a forgotten round-trip silently unprotected.
- **A write NOBODY MADE still spends the concurrency token somebody is holding, and the 409 it produces names a
  person.** `xmin` is per-**row**, so any write ages every open form's version whoever made it — and
  `AppointmentProgressJob` writes an appointment **every minute** (« En cours » at its slot's start minute,
  « Séance passée » at its end). Production, 2026-09-05: the edit dialog read the row at 15:50:04, the job wrote
  it at 15:50:05, the user pressed Enregistrer at 15:50:32 and was told « cet enregistrement a été modifié par
  quelqu'un d'autre pendant votre saisie » — one clinic, one user, one session. `Appointment.VersionBeforeAutoAdvance`
  + `AutomaticWriteInterceptor` (which decides from the **audit actor**, so a new writer cannot forget to
  participate) make the job's own comment true at last: « a user's concurrent edit legitimately wins ». ⚠️ Its
  amplifier is the frontend half and it cost far more than the one refused save: a dialog that catches a 409 into
  a plain `setError` is **poisoned for good** — the version it holds never moves, so every later click repeats the
  refusal (six of them over 81 minutes, until the user reloaded the page). Any form that round-trips a version
  goes through `useConflict`, which offers the « Recharger » that the server's own sentence tells the user to do.
- **Never `DateTime.UtcNow` or `DateTime.Today`.** `ClinicClock` is the only thing that knows Tunisia is UTC+1.
  `EndOfLocalDayUtc` is the *next* midnight (exclusive) while every money read is inclusive at both ends — use
  `LastTickOfLocalDayUtc`, or a midnight payment lands in two adjacent periods.
- **On the client, `todayLocalIso()`** (`web/lib/format.ts`), never `new Date().toISOString().slice(0, 10)`:
  for the first hour of every Tunisian day the latter pre-fills *yesterday*, and on the 1st, last month.
- **An update DTO is tri-state.** Omitting a key means "unchanged"; `[]` or an explicit null means "clear".
  Conflating them deletes data — `{ status }` alone on an appointment would drop every act of the séance.
- **Every paged read orders on a unique column last** (`.ThenBy(x => x.Id)`). `OFFSET` over a non-unique sort
  shows one row twice and skips another, which reads as "a record vanished".
- **`paging: null` is a first-class case**, not a very large page — the pickers, the lookups and every money
  *total* legitimately read everything. And free-text search belongs in **SQL** (`SearchTerm` + `SqlSearch`'s
  `unaccent`); filtering an already-cut page answers a different question and reports « aucun résultat ».
- **Nothing in `UnitTests` touches a database**, so a migration is the one change unit tests structurally
  cannot verify. Run `dotnet run -- verify-schema` before and after a migration batch and diff it; add
  `reconcile-money` when money is involved. Both are read-only: exit 0 clean / 1 can't run / 2 drift found.
- **Build tests outside the repo**: `BaseOutputPath=<temp> dotnet test …`. Smart App Control intermittently
  refuses freshly-built in-repo test assemblies, and the same trick lets `dotnet ef migrations add` run while
  the dev API holds `api/**/bin`.
- **An `Unset` tenant scope reads zero rows with no error** — indistinguishable from an idle clinic. Anything
  without an HTTP context (a job, a console verb, startup) must declare `UseSystemWide(reason)` or
  `UseClinic(id)`.
- **A new `Features/<Area>` folder emits a realtime key** that `web/lib/realtime/clinic-hub.ts` must declare;
  `RealtimeResourceResolverTests` compares the two sets in both directions and fails either way.
- **A followed treatment is a `Draft`, and « Draft » now means two opposite things depending on who is asking.**
  « Suivre ce traitement » creates an un-numbered plan that is **clinically live and financially inert**, and
  `AdvanceAfterWorkRecorded` keeps it a Draft however many séances it records. Ask
  `TreatmentPlanLifecycle.IsLive` (C#) or `isPlanLive` (TS) — never a hand-written
  `Status == Accepted || Status == InProgress`, which excluded it in **seven** places: four `.tsx` writers that
  N23 caught, the SQL behind « Traitements en cours » that N23's `.tsx`-only scan structurally could not reach,
  and both halves of the recall worklist (a stalled followed treatment was never chased, while every followed
  treatment older than 14 days was reported as « devis présenté, jamais répondu » — a quote that cannot exist,
  since `Accept` is the only writer of `Number`). ⚠️ Its financial twin is the **opposite** rule:
  `PlanBillingRules.CarriesDebt(Draft)` is false, both installment reads filter on `DebtBearingPlanStatuses`,
  and **an un-numbered plan must never wear a status that carries debt** — `OpenStatusFromWork` keys on
  `Number is null` for that reason, after the guard was applied to one of four status writers and
  « Arrêter le traitement » → « Reprendre le traitement » turned a followed treatment into an `Accepted` devis
  with a null number and a live créance for a total nobody had quoted.
- **A multi-séance act is split BY DEFAULT, and the treatment is created when the booking is SAVED.**
  `resolvePlannedProtocols` is the only place that default lives, and `SelectedAct.plannedProtocol` is
  tri-state (`undefined` = undecided · `null` = one séance · a list = the confirmed séances). It replaced a
  « Suivre ce traitement » button that created the plan on press, and therefore needed a patient id: the create
  dialog passed the control only once one was selected and the **edit** dialog never passed it at all — so the
  sentence « cet acte se fait normalement en 3 séances » rendered above **no control** whenever the act was
  picked before the patient, on a « Nouveau patient » walk-in, or on any reopened visit. Two dentists, the same
  act, one button. ⚠️ Deferring to the save also means a `createdPlansRef` memo is mandatory: both dialogs
  re-run their save **from the top** on every confirmation (slot taken, out of hours, past time), so without it
  one « créer quand même » leaves two identical treatments — `createdPatientIdRef`'s scar, one object over.
  ⚠️ Derived on render, **never seeded by an effect**: the only channel back to the host is `onChange`, and the
  edit dialog's also resets `durationTouched`. `check:responsive`'s N26 holds the materialisation.
- **A step input that COPIES a step must pass all four arguments.** `TreatmentPlanItemStepInput`'s fourth is
  `MinDaysAfterPrevious` and it **defaults to null**, so a three-argument copy compiles, reads correctly, and
  erases the interval — from *every* step of the act, since `SetSteps` replaces the list. That is what
  `SetTreatmentPlanItemStepsCommand` did: renaming one step of an implant wiped its osseointegration wait, with
  the client sending the field all along and the symptom being `RecallWorklistRules` reporting a healthy
  implant as abandoned. `StartTreatmentStepsTests` scans for it, flagging a *copy* and not a synthesis.
- **Money for a multi-séance act goes on the TREATMENT, never on the séance's note d'honoraires.** The act is
  priced once, so it sits on the fiche at **0** — imposed server-side by `PlanCarriedActPricing`, not merely
  offered — and what the patient hands over at the chair is `AmountCollectedOnPlan`, a **second** money field that
  is never folded into « Payé ». Collecting on a treatment with no devis **mints its number**
  (`CollectOnTreatmentCommand`), because a `Draft` has no échéancier and la caisse cannot see one. Overtyping
  that 0 is how a 250 DT act with 150 collected left the patient owing 250 on the devis **and** 100 on an
  unlinked note: an invoice raised from a fiche carries `dentalRecordId` and **no `TreatmentPlanId`**, so
  `PlanBillingRules.BilledPlanIds` cannot de-duplicate it.
- **A note that REPRESENTS a devis may not be attached to one holding money the note does not bill.** The
  bridge is all-or-nothing — `BilledPlanIds` drops the *whole* plan from « Solde patient », « Créances », la
  caisse and the dashboard the moment a real note names it — so « Montant du travail restant » priced on the
  billed continuation path put a live debt exactly where nothing looks: a 30 DT coiffage billed on note
  2026-0016, continued at 10 DT, left the patient's balance reading **0**. `AmendTreatmentPlanCommand`
  already refuses that state in as many words (« acts added afterwards would be invisible in every balance »);
  `ContinueRecordedActCommand` reached it by adding the line *before* attaching. It now prices the
  already-billed act **0** and leaves the note **unattached** whenever there is new money, so the two
  documents stay disjoint. ⚠️ Gated on `PlanBillingRules.RepresentsItsPlan`, never on « is there a note » — a
  **Draft** note represents nothing and its plan still carries its own balance, so the 0 would lose the 30
  instead of saving the 10.
- **A plan minted inside a booking dialog is booked through `schedulablePlanItems`, never `plan.items[0]`.**
  That is the gate `planIdByItem` is built from, and a priced « travail restant » makes the continuation's
  *first* act `Done` on creation — so `items[0]` was dropped from the map and the save was refused outright
  with « Le plan de traitement est requis pour lier l'acte. », after showing the finished 30 DT act in place
  of the 10 DT being booked and offering no step at all. `attachPlanAct`'s own doc had said « never
  `plan.items[0]` » since the day it was written; `check:responsive`'s N28 is what holds it.
- **A restoration records work that is DONE, and the chart asserted it from the FIRST séance.** A multi-séance
  act's step-1 fiche carried the catalogue's `ResultingCondition`, so a tooth read « Implant » weeks before the
  implant existed — measured as 7 rows on the live database, every one from a step 1 of 2, three of them claiming
  implants and two claiming teeth were *gone* mid-extraction. Its twin fired in the same breath:
  `ClearDiagnosesForTreatedTeethAsync` deleted the « à traiter » that said the work was still needed, so the chart
  both claimed the work was finished and forgot it had been asked for. `ToothChartingRules` withholds the end
  state until the act is `Done`, and both fiche commands now link the plan step **before** charting because only
  the aggregate can say whether the step just marked was the last. ⚠️ Its companion is
  `TreatmentPlanItemDto.TreatedToothNumbers`: with the chart written at the END, teeth entered on an early séance
  and absent from the last fiche would chart **nothing at all**.
- **An échéance the system raised is not a date anybody promised**, and reading it as one made every devis in the
  database « En retard » from the day after signature (25 of 27 unpaid rows, cancelled and already-invoiced ones
  included). `Accept` writes one lump-sum row dated at the acceptance instant so a payment has somewhere to live;
  `Installment.IsAutoRaised` is what tells it from a schedule a dentist typed, and `InstallmentLateness` is the
  one rule — it needs the plan's status, its note, its unrealised work and the clinic's day, so it is computed
  server-side onto `InstallmentDto.IsOverdue` and never re-derived from `dueDate`.
- **A step rank derived as `stepsDone + 1` is the step still to COME, and a bare « 2 / 3 » is read as
  progress.** The odontogramme's tooth tooltip printed « séance 2 sur 3 · essai de l'armature à planifier » on a
  couronne whose only delivered séance was the préparation — a false clinical claim on the one diagram read at a
  glance — and the arithmetic is wrong outright when the séances are recorded out of protocol order, which
  `DentalRecordLinker` deliberately allows. State a **count phrased as a count** plus the *name* of the step
  actually carried out (« 1 étape sur 3 faite : Préparation »), and keep « prochaine étape » to the planning
  surfaces that say so. The word goes beside the **visible** figure — an `sr-only` label was already right on
  both surfaces where this was measured while the sighted reader had nothing. `check:responsive`'s N31 holds it.
- **A dentition is THREE states, and `isAdultDentition` is not the one that picks an arch.** `DentitionType`
  gained `Mixed` (« denture mixte », 6–12 ans, beside « temporaire » under 6 and « définitive » from 13), and
  the trap is that `isAdultDentition` answers `value !== "Child"` — so `Mixed` reads as **adult** there, which
  is right for its own question (« may permanent teeth be charted? ») and catastrophic for the other one: an
  eight-year-old's chart would open on the permanent arch with her deciduous teeth unreachable, **and nothing
  would error**. `dentitionViewFor` is the reader that maps all three one-to-one; `isAdultDentition` has no
  call sites outside `lib/dentition.ts` and should stay that way. ⚠️ `Mixed = 2`, **appended never inserted** —
  the enum persists through `HasConversion<int>()`, so slotting it between `Child` and `Adult` where it belongs
  in reading order would repoint every stored row. ⚠️ The stored keys stay `Child`/`Adult`; only the **labels**
  became « Denture temporaire / mixte / définitive ».
- **The fiche de soins EMITS the séance's ordonnance, inside its own transaction, and never deletes it.**
  `FicheOrdonnanceEmitter` runs *before* `SaveChangesAsync` in both fiche commands — deliberately not with
  the post-commit side effects beside it, because those (stock, the note d'honoraires, marking the visit
  complete) are **derived** and may fail without the record being wrong, while a prescription is **entered
  clinical data**: a dentist who types an antibiotic, reads « fiche enregistrée » and has no ordonnance has
  lost work silently. ⚠️ Clearing the section and re-saving leaves the existing ordonnance **standing** —
  the paper may be in the patient's hand, `DeleteMedicalDocumentCommand` is `AdminOrDoctor` while the fiche
  is open to reception, and patient records resist destruction. ⚠️ `Prescription` is **tri-state**: absent
  means « unchanged » and is not even read (which is what keeps every older caller clear of a document),
  present-and-empty means « nothing prescribed ». Calling the emitter unconditionally failed **15** existing
  tests, every one a fiche with no prescription at all.
- **A médicament and an examen may NOT share a sheet, so one fiche emits up to TWO ordonnances.**
  Médicaments → `prescription`; examens (bilan, radio, avis) → the `examens` type. « Le médecin formule sur des
  **ordonnances distinctes** les prescriptions de médicaments … et les examens de laboratoire », and three
  practical facts make that a rule: the sheets go to different people (pharmacie, laboratoire, imagerie), an
  examen prescription is single-use so one sheet cannot serve two destinations, and CNAM reimburses per line
  against *that* line's prescription. Our `prescription` document is specifically a **médicament form** —
  `ordonnance-certificat-norms` built it to R.5132-3 with voie, quantité and renouvellement — so a panoramique
  printed on it is an imaging request on a drug prescription. ⚠️ **This reverses the version that shipped
  first** (« an examen is a line of the same ordonnance »), which was chosen because it touched no line
  formatter; cheapness is not a medical argument. `FicheOrdonnanceEmitter.Split` is the only place the sorting
  lives, and the aperçu shares it. ⚠️ **Still no line formatter changed** — `ExamenContent` prints an examen
  verbatim, so the printed médicament sentence's **three** contractually-identical copies are untouched.
  ⚠️ **No migration**: a pre-split ordonnance holds its examens in `content.medications` marked
  `kind: "examen"`, the fiche reads them back as examens, and the next save moves them onto their own sheet —
  nothing is touched until a human reopens the séance. `kind` therefore stays on the médicament wire (an
  **absent** kind is a médicament, and `Normalize` is the only place that lives) while the `examens` array
  carries none. ⚠️ **No `renewals` on the examens sheet** — renewal is a dispensing concept and an examen
  prescription is single-use. ⚠️ Both sheets print the title « ORDONNANCE »; only the app label
  (« Demande d'examens ») separates them, because that is where a human has to choose. ⚠️ The JSON is
  **camelCase** and that is a requirement: the document editor reads it back with `med.timesPerDay`,
  case-sensitively, while the C# reader is case-**insensitive** — so a PascalCase write empties every posology
  and no server test notices. ⚠️ And `PractitionerRenderSnapshot.ApplyTo` re-serialises with the default
  encoder, so the stored bytes hold non-ASCII as `\uXXXX` escapes; a relaxed encoder upstream is undone, and
  only a test asserting on the raw string breaks. `check:responsive`'s N32 `prescription-line-has-one-owner`
  holds the one-composer half; N33 `document-type-set-has-one-owner` holds the **five** mirrors of the type set
  (`DocumentTypes`, `DOCUMENT_TEMPLATES`, `DocumentFileNaming`, the PDF title map, the editor heading map),
  because a type missing from one renders as its raw key with no error — which shipped once, as
  `arret-travail` in a patient's own Documents tab.
- **A document is READ where it is referred to, and EDITED in the Documents module.**
  `components/documents/document-preview-dialog.tsx` is the read surface — before it, the only thing in this
  app that rendered a saved `MedicalDocument` was the full editor page, so « Ouvrir l'ordonnance » on a séance
  row navigated away from the patient file. It frames the **server-rendered PDF**, never a second HTML
  rendering: the editor's A4 block was the tempting source and would have been a fourth copy of a legal
  document's layout. ⚠️ Its `apercu` mode renders what the fiche is **about to** save, composed by
  `FicheOrdonnanceEmitter.ComposeAsync` — the save's own method — so it works before the first save and cannot
  differ from the paper; it carries **no Imprimer and no Envoyer**, because a printed ordonnance for an unsaved
  séance is a legal paper with no record behind it. ⚠️ **« Modifier » routes by `MedicalDocumentDto.DentalRecordId`**:
  a document a fiche owns is recomposed from the section on that fiche's next save. ⚠️ `GET /medical-documents/{id}/pdf`
  was missing until this — the only renderer took the whole document *in the body*, so every surface wanting to
  show a saved one had to re-implement `MedicalDocumentPdfMapping.FlattenContent` in the browser.
- **A document's own `xmin` does NOT protect it from the fiche's save, and believing it did cost a real
  overwrite.** `FicheOrdonnanceEmitter` loads the document *inside* the fiche's transaction, so its tracked copy
  always carries the CURRENT token and the update always succeeds; the stale copy lives in the browser's
  section state, where `xmin` can never see it. Measured end to end: a colleague's correction to a médicament's
  name, made through `/documents/prescription` while the fiche modal sat open, was reverted by the next fiche
  save — green toast, no refusal, edit gone. The fix is the ordinary one for this codebase — the modal
  round-trips `prescriptionDocumentVersion` / `examensDocumentVersion` (the tokens it read on open) and the
  emitter declares them through `SetExpectedVersion`; **0 means « not supplied »**, so every older caller and
  every server-internal writer is unaffected. ⚠️ Each sheet carries its **own** token: one document's edit must
  not refuse the other's save. ⚠️ And a 409 is only half a fix — the fiche modal was the one dialog in the app
  rendering `FormErrorBanner` **without** the `action` that `useConflict.isConflict` exists to drive, so the
  refusal said « Rechargez » with no control to do it. That is the poisoned-dialog trap two bullets up, and it
  mattered less while the only 409 came from the fiche's own row.
- **`MedicalDocument.AppointmentId` cannot join a séance to its ordonnance, and `DentalRecordId` is why.**
  The appointment is null on any fiche entered outside the agenda and on every day `DentalRecordVisitLink`
  refuses to guess — precisely the fiches charted from the patient's own page. The new column is nullable,
  un-FK'd and indexed (`AppointmentId`'s exact shape); **nothing was backfilled**, and the read falls back to
  `DentalRecordId IS NULL AND AppointmentId IN (…)` for legacy rows. ⚠️ That null test is load-bearing: drop
  it and an ordonnance already claimed by one fiche is claimed again by every fiche sharing its visit.
- **`Doctor.Specialty` is an English key, and the server needed its own French map the day it began issuing
  documents.** Until the fiche emitted an ordonnance, every document was composed by the browser, which sent
  an already-translated `doctorSpecialty` through `web/lib/specialties.ts`. A server-composed one has only
  the stored key, so without `DoctorSpecialtyLabels` an ordonnance prints « Orthodontist » under the
  prescriber's name on a French legal document. The two maps are a **pair**, held in both directions by
  `check:responsive`'s N32 `specialty-labels-have-one-owner`.
- **« Sexe » and « Poids » are gone from the ordonnance, deliberately.** A Tunisian dental ordonnance does
  not carry them, and `patientSex` was prefilled from the patient record so it printed on every one ever
  issued. Withdrawn from `DocumentIdentity.PatientLines` (the one render owner), `MedicalDocumentPdfData`,
  `MedicalDocumentPdfMapping` and the editor's nine sites. ⚠️ **No migration**: legacy documents keep both
  keys in `ContentJson` and re-rendering one now omits two lines it used to print. ⚠️ Read the tombstone at
  the render site before putting them back — `ordonnance-certificat-norms`' spec calls sexe R.5132-3-mandatory
  and is otherwise still accurate, so a reader working from it will re-add them.
- **Inside a `RecordSection`, one un-shrinkable child sets the width of EVERY sibling.** Its body is a
  `display: grid`, so the single implicit track is sized by the widest child's min-content — and `Button` is
  `whitespace-nowrap shrink-0`, which `flex-1` does **not** remove (different tailwind-merge groups, so the
  element gets `flex: 1 1 0%` *and* `flex-shrink: 0`). Two add buttons whose labels could neither shrink nor
  wrap measured **253 px** of min-content in a **231 px** box at 320 px, and every other row of the section —
  the prescription lines, the renouvellement field, the closing sentence — was pushed to 253 px with them and
  ran past the dialog's edge. Reported as « it's beyond bounds, the writing and cards ». Spell out `shrink`,
  give the buttons a real `basis-*` with `flex-wrap`, and put `min-w-0` on any row holding a `truncate`
  descendant — `white-space: nowrap` makes its min-content the whole string. ⚠️ `tsc`, `check:responsive` and
  `npm run build` were **all green** while this was live; only the eye pass at 320 px found it.
- **A country selector whose choice never leaves the browser, and two refusal sentences that are byte-identical.**
  `PhoneNumber.ToE164(raw, region)` has taken a region since international-phone-numbers shipped and its docstring
  says « the country selector passes the user's own choice » — **not one production call site passed one**, so the
  server validated every number against Tunisia while the form offered 250 countries. Measured in production
  2026-09-11: « France » + `06 12 34 56 78` (valid, and accepted by the browser's own pre-check) refused on save;
  `+33 6 12 34 56 78` accepted, which is the only reason the feature looked half-alive. ⚠️ It was undiagnosable
  because `PhoneRefusals.Invalid` and `PHONE_ERROR_FR` are **the same sentence word for word** while both
  docstrings claimed they were deliberately distinct — so a server refusal was indistinguishable from the client
  pre-check. ⚠️ `PhoneRuleCorpusTests` and `check:responsive`'s `phone-rule-matches-the-corpus` were green
  throughout and *contain this exact case*: a corpus pins a **function**, and nothing pinned that the callers hand
  it the region. ⚠️ The region is a **write-time input that must be stored**, not merely validated with —
  `PhoneNumber.E164` / `Supplier.PhoneE164` are the stored normalisation, because a national number cannot be
  re-derived without its country and accepting one the product cannot dial is the quieter half of the same defect
  (no WhatsApp, no reminder, a `tel:` link dialling a French national number from a Tunisian handset). Nothing is
  backfilled; both fall back to re-deriving, which is exactly what legacy rows already resolved to.
- **In the Windows shell, EVERY failed navigation used to mean « the clinic server is unreachable ».** The panel
  it raises replaces the whole application, so raising it is a claim that the server is down — but
  `NavigationCompleted` reports a failure for at least four things that say nothing of the kind: a `tel:` or
  `mailto:` link (no scheme a WebView can complete), a navigation cancelled on purpose, **a request that turned
  into a download** (WebView2 reports one as `ConnectionAborted` on the clinic's own origin), and any navigation
  superseded by the next. One tap on a patient's phone number took the app down and named a server that was
  answering `/health` with 200. ⚠️ `CoreWebView2NavigationCompletedEventArgs` carries **no URI**, only a
  navigation id — which is precisely how the handler came to blame `_config.BaseUrl` for failures that never
  addressed it. The test is now positive and narrow (the clinic app's own document, remembered from
  `NavigationStarting`), never a list of failures to forgive. See `desktop/CLAUDE.md` → `ExternalNavigation.cs`.
- **Never recover an outcome by matching French prose.** Branch on a `Result.Code` or an enum member's own
  name — a `Contains("déjà facturée")` once made rewording a sentence change behaviour.
- **The fiche de soins prices a booked act from the CATALOGUE, not from the appointment's row.** Both prefill
  paths (`applyAppointment`, `addFromProcedure`) resolve `procedureTypeId` back to a `ProcedureTypeDto` and
  read `defaultCost`, so an act's negotiated `AgreedCost` has to be threaded through explicitly or it reverts
  to the tarif in silence — and a saved `procedures` list that omits the prices restores every act to its
  tarif, because `SetProcedures` replaces the whole list. `check:responsive`'s N14 holds the prefill half.
- **A thumbnail paints the stored PREVIEW, so asking about the ORIGINAL is asking the wrong question.**
  `FileThumbnail`'s gate required the original to be browser-previewable *and* under 8 Mo — the first hid every
  HEIC and TIFF whose stand-in was ready to serve, the second was left over from when the component downloaded
  the original. Neither is an error anywhere; the row just shows an icon. Its twin: `PatientFileDto.HasPreview`
  decides whether the browser *asks* and `DownloadPatientFilePreviewQuery` decides what to *serve* — they go
  through `PatientFilePreviewPolicy` because a disagreement is silent in both directions.
- **A DICOM's `MONOCHROME1` inversion must be decided in ONE place, and applied in one.** Rendered as
  `MONOCHROME2` a radiograph is a photographic negative of itself — bone dark, air bright — which reads as a
  *finding*; applied **twice** it is the original again, correct-looking and wrong beside every `MONOCHROME2`
  file in the drawer. `lib/files/dicom/study.ts` decides it, `dicom/window.ts`'s **unexported** LUT builder
  applies it, and `check:responsive`'s `monochrome1-has-one-owner` fails on a second file comparing the
  literal. The flattened preview and the interactive viewer are two consumers of that one pipeline, never two
  copies of it.
- **No mesh format records a unit, so a length in a 3D viewer is an ASSUMPTION and must be shown as one.** STL,
  PLY and OBJ hold bare floats; « 48,2 » is 48,2 of whatever the exporter had in mind. Dental scanners write
  millimetres, which makes mm the right default and never a right claim — a model exported in centimetres looks
  identical and measures ten times wrong. `lib/files/mesh/measure.ts` makes the unit a control, grades its
  caveat on whether the bounding box is a plausible dental size, and — the part that actually works — the viewer
  shows the model's own dimensions in the chosen unit at all times, because « 63,0 × 34,1 × 11,8 mm » is an arch
  and « 6,3 × 3,4 × 1,2 mm » is not. The view buttons are geometric (« Dessus ») and never anatomical
  (« occlusale ») for the same reason: these formats record no orientation either.
- **A WebGL context is not released by `dispose()`, and the 17th one blanks a viewer somebody else has open.**
  Chrome allows sixteen; asking for another kills the oldest, with no error anywhere. Only `forceContextLoss()`
  hands one back immediately, and the renderer that matters is the *thumbnail* one — built per file on the way
  up, so a dozen models dropped at once reach the limit in a single gesture.
  `check:responsive`'s `webgl-context-is-given-back` holds it.
- **A format's own signature outranks another format's claim, and the reverse order refused real files.** The
  DICOM standard leaves its 128-byte preamble unspecified, so an exporter may put a TIFF header there — making
  `II*\0` at 0 and `DICM` at 128 an ordinary, valid DICOM. `FileUploadValidator`'s advisory branch cross-checked
  first and refused those with « le fichier a peut-être été renommé ». Advisory means *absence* proves nothing;
  presence is still affirmative.
- **A decoder that needs a `blob:` Worker fails only in production.** `worker-src` is inherited from
  `default-src 'self'` unless declared, and a dev server sends no CSP at all — so libheif works on the laptop
  and shows a grey icon on the VPS. The policy exists in **four** byte-identical copies
  (`SecurityHeadersMiddleware`, both Caddy sites, `console/next.config.ts`).
- **`IFileStorage.UploadAsync` requires a `Guid clinicId`**, so an unprefixed key is unwritable; but
  `DownloadAsync`/`DeleteAsync` take the stored key **verbatim**, because pre-US-5 rows hold flat keys.
- **An unloaded collection navigation is empty, not stale**, and a domain property over it answers confidently
  and wrongly. `UserRepository`'s two account reads never `Include`d `RecoveryCodes`, so « Sécurité » read
  « 0 code inutilisé » over eight live codes, a regeneration *added* eight instead of replacing them, and
  `ConsumeRecoveryCode` refused every code the account owned — four silent failures, no exception in any of them,
  and a mock-repository suite that hands back an in-memory aggregate cannot see it. There is no lazy loading and
  no `AutoInclude` in this solution. `RecoveryCodeLoadingCoverageTests` is the derived guard.
- **A scroll container must be `relative`, or it does not clip its own `absolute` children.** Tailwind's
  `sr-only` *is* `position: absolute`, so with `AppShell`'s `<main>` left static every screen-reader-only line
  below the fold resolved against `<body>`, escaped the page scroller and made the *document* taller than
  `h-dvh` — a third scrollbar onto blank space (1168 px on the dashboard at 1440×900, 2611 px at 390×844).
  `check:responsive`'s `page-scroller-contains-its-absolutes` holds it.
- **An act the TREATMENT prices takes no share of the séance total, and « Total » wrote straight past the
  lock.** Such an act is 0 by rule — `act-card` **withholds the price field altogether**, printing « Aucun
  honoraire sur cette séance » in its place (a card reading « 0,000 DT » « is the third of the séance's zeros
  and it says nothing »), and `PlanCarriedActPricing` imposes the same 0 server-side — but `distributeSessionTotal` filtered on `isActNamed` alone, so typing 150
  into « Total » moved the locked field to « 150,000 » on screen and the save silently put it back. On a
  **mixed** séance it is quieter and worse: the typed total is split between a carried couronne and a real
  détartrage, so the détartrage is under-billed by whatever share went to the act that cannot hold it. Its twin
  is the *display* rule and the two conditions are different: the price field and the `/dent · forfait` switch
  are withheld **per act** (`billedOnPlan`), while « Total » and « Payé » are withheld **per séance** and only
  when *every* act is carried (`seanceIsWhollyOnTreatment`, which is structural — « all named acts are
  carried » — never `grandTotal === 0`, a test that was also true of a détartrage nobody had priced yet).
  `check:responsive`'s N27 holds the arithmetic half.
- **`TreatmentPlanItemDto.treatedToothNumbers` is the whole SÉANCE's teeth, not the act's**, because it is
  derived from the fiches an act's steps produced and a fiche records every tooth of the visit. So « which teeth
  is this treatment on? » must read the devis LINE first and fall back to the treated ones only when it names
  none (`teethUnderTreatment`) — the union was tried and measured wrong: an « Extraction simple » quoted on 13
  and 43 whose first séance was recorded on a fiche naming **13, 27, 36, 37, 43** put a « traitement en cours »
  ring on three teeth with no treatment on them. ⚠️ The priority is the **opposite** of `openPlanItems`', which
  seeds a fiche's chart and legitimately prefers the teeth actually worked on.
- **A treatment is `item.Status == Planned` until its first fiche lands**, so a filter on `InProgress` hides
  it on exactly the day it is created. « Traitements suivis » accepts `Planned` **when the act has steps** —
  `Steps.Any()` is what keeps the widening honest, since an act with no protocol goes Planned → Done and has
  never belonged there. It orders unbooked-before-booked and then by what is due, which is what makes the
  screen's three groups contiguous rather than interleaved; the « is this séance booked? » subquery must answer
  **exactly** what `TreatmentsInProgressReader` answers (per **step**, and with no « from today » floor — an
  `AwaitingClosure` visit is still a standing booking) or the list groups by one rule and sorts by another.
- **One act charts ONE state across all its teeth — except a bridge, which is the only exception in the
  product.** A three-unit bridge is one act (« Couronne / bridge (par élément) », priced per element, so one
  devis line and the right total) whose 14 and 16 are *piliers* and whose 15 is a *pontique*. With one
  `ResultingCondition` per act it charted **three abutments and no pontic** — anatomically impossible — and the
  odontogramme then drew a travée across the run, which made it look deliberate. Entering it as the same
  procedure twice was the only way to say it and nothing suggested that. `DentalRecordAct.PonticToothNumbers`
  records the answer and `BridgeCharting.ConditionFor` folds it — in **four** branches, and the fourth is
  load-bearing: ⚠️ **with NEITHER role list populated an act charts exactly as before**, which is what makes it
  safe against every existing row. « Both lists empty » is the test, never « the pontique list is empty », now
  that `ImplantPilierToothNumbers` is the second one. ⚠️ **Never infer the roles from position**: a pier abutment
  is crowned in the *middle* of the span, a cantilever hangs past the last abutment, and 12 · 11 · 21 sorts to
  11 · 12 · 21, so « the middle one » is the wrong tooth.
- **And a bridge's EXTENT cannot be inferred from position either — the product made the same mistake one level
  up.** `odontogram.tsx` joined bridge-marked teeth by arch adjacency within three intervening sites, so a bridge
  on 14·15·16 beside one on 17·18 drew **one five-unit bar** — and because the run's `planned` flag was OR-ed
  across the merge, a **finished** crown on 16 was drawn dashed-red, *asserted as not yet in the mouth*. It also
  failed the other way, drawing nothing for abutments four sites apart. `ToothState.BridgeGroupId` states the
  answer (minted per bridge **act**, and per **gesture** for a planned one) and `web/components/bridge-runs.ts`
  is the one reader. ⚠️ **A group is minted only for an act with ≥ 2 teeth**, or the two-acts-of-one-tooth
  workflow that predates `PonticToothNumbers` silently loses its travée; ⚠️ **one bridge mark per tooth, the
  newest, resolved before grouping**, or a redone bridge lands in two groups and they merge again; ⚠️ ungrouped
  rows keep the old adjacency scan and **grouped teeth are excluded from it**; ⚠️ and the migration must
  **never backfill** — inferring a group from adjacency would freeze today's wrong answer into data. Held by
  `check:responsive`'s N30.
- **A `PopoverContent` that caps its own height DISABLES the cap it looks like it is tightening.** The base reads
  `--radix-popover-content-available-height` — the room Radix measures between the anchor and the edge — and
  tailwind-merge lets a caller's `max-h-[…]` win over it, while `dvh` measures the viewport instead. Measured on
  the tooth editor: 394 px of room, 590.8 px allowed, a 428 px panel at `y = -35` with its heading off screen and
  *nothing to scroll*. `select.tsx` and `dropdown-menu.tsx` had the correct cap all along and `popover.tsx` did
  not, for 77 call sites — the repo's own defect shape, at the primitive layer.
- **Before any frontend code, read [`.claude/rules/frontend-web.md`](.claude/rules/frontend-web.md).** `web/`
  has no test runner and `npm run lint` cannot run (eslint is scripted but not installed), so the gate is
  `npm run check:responsive` + `npx tsc --noEmit` + `npm run build`, then an eye pass at
  320/390/820/1180/1440 px.
## Two standing facts with nowhere better to live

- **`ValidationBehavior` is inert**: no FluentValidation validators exist; handlers validate inline and return
  `Result.Failure`.
- **Frontend data-wiring**: the dashboard, `appointment-list`, the stock feature and the notification center are
  all API-wired (`dashboardApi`, `useAppointments`, `stockApi`, `notificationsApi`/`useNotifications`). The
  header **search** is a live patient lookup (type → results → open the patient). *(The orphan
  `notifications-list` sample component and the redundant `dental-chart` tooth chart were removed in
  `reliability-and-polish`; the read-only summary chart now reuses `record-tooth-chart`.)*
## Root-level setup / reference docs

| File | Topic |
|------|-------|
| `README.md` | (minimal) |
| `GOOGLE_CALENDAR_SETUP.md` / `_FR.md` | Google Calendar OAuth setup (EN/FR) |
| `GOOGLE_CALENDAR_SYNC_ARCHITECTURE.md` | Calendar sync design |
| `SYNC_TESTING_GUIDE.md` | How to test calendar sync |
| `api/CLINIC_MANAGEMENT_FLOW.md`, `MULTI_CLINIC_SETUP.md`, `ROLE_ASSIGNMENT_IMPLEMENTATION.md`, `ENTITY_UPDATE_GUIDE.md`, `IMPLEMENTATION_SUMMARY.md` | Backend feature/process docs |
| `packaging/README.md` | Local/offline-LAN operator guide (Phase 5): publish + installers, bundled PostgreSQL, backup/restore, admin recovery, per-AC verification checklist |

> When code changes, update the nearest `CLAUDE.md` so this map stays accurate.

> When code changes, update the nearest `CLAUDE.md` so this map stays accurate — and put the *reasoning* in
> `features/<slug>/notes.md` or `ARCHITECTURE.md`, never here. This file is a map, and it stops working the
> moment it becomes a changelog.
