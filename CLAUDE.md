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
| Auth | **Pluggable** by `Auth:Mode`: **Cloud** = Auth0 (JWT bearer); **Local** = self-issued email+password accounts (an offline LAN install **and** the hosted multi-tenant backend). Clinic membership resolved server-side. ⚠️ `Auth:Mode` answers *who issues tokens* only — every other deployment difference is a named capability on `Deployment:Profile`. | both |
| External | Google Calendar (two-way sync), SMS/WhatsApp reminders, Meta WhatsApp onboarding | `api/...Infrastructure/Services` |

⚠️ **There is no AI subsystem.** No code in this product reaches an inference endpoint: the chat, the patient
AI summary and every service behind them were deleted. See
[`features/adoption-qa-i-access-control-and-audit/notes.md`](features/adoption-qa-i-access-control-and-audit/notes.md).

⚠️ **Three deployment kinds, and almost every design decision turns on one of them**: `SelfHostedLan` (a
clinic's own Windows PC serving its LAN), `HostedMultiTenant` (one hosted backend, many clinics, the product's
own accounts) and `CloudBrowser` (one hosted backend, Auth0). They are a `DeploymentProfile` of ~18 **named
capabilities** — never a boolean. See
[`features/multi-tenant-cloud/notes.md`](features/multi-tenant-cloud/notes.md).

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

- [`ARCHITECTURE.md`](ARCHITECTURE.md) — There is a CI gate now, and before it there was none for `api/` or `web/` · Multi-tenancy · Pluggable auth (`Auth:Mode` = `Cloud` | `Local`) · Google Calendar sync is asymmetric + per-clinic · Background jobs · Billing / CNAM / treatment plans (deep, fully-wired subsystems) · Clinical-workflow-depth operational features (built) · Dead-code cleanup · Clinic-scoped SignalR realtime (built) · In-app staff notification center (built) · Real outbound SMS/WhatsApp reminders · Security posture (mostly hardened by `cloud-security-and-tenant-isolation`, PR #11)

**Money, and the ledgers behind it**

- [`data-and-money-integrity`](features/data-and-money-integrity/notes.md) — Optimistic concurrency, solution-wide · Money is correctable, not immutable · Patient records resist destruction · `reconcile-money` (Local-mode console verb)
- [`caisse-extrait`](features/caisse-extrait/notes.md) — La caisse has a statement, and it is a read · A session's payment reaches the till
- [`adoption-gaps-remediation`](features/adoption-gaps-remediation/notes.md) — Re-saving a fiche tops its note up, and la caisse's day is Tunisian (Part 2)
- [`audit-sections-3-to-10`](features/audit-sections-3-to-10/notes.md) — `verify-schema` (Local-mode console verb) · Tunisia is UTC+1, and `ClinicClock` is the only thing that knows it (P6) · A visit knows whether it was billed (P6) · One CNAM calculator (P6)

**The clinical loop**

- [`visit-closure-worklist`](features/visit-closure-worklist/notes.md) — A séance is not finished until three things are answered, and the app now asks
- [`multi-act-appointments`](features/multi-act-appointments/notes.md) — A séance is several acts, and the scalars are derived
- [`patient-file-uploads`](features/patient-file-uploads/notes.md) — What may be uploaded has one authority, and the browser is told rather than trusted

**Reads, lists and catalogues**

- [`list-pagination`](features/list-pagination/notes.md) — Every list read is a page, and search is a database question
- [`dashboard-insights`](features/dashboard-insights/notes.md) — The dashboard is a composed read, not a KPI bag
- [`stock-fournisseurs`](features/stock-fournisseurs/notes.md) — A fournisseur is a record with a number, not a name on a row

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
- **Never recover an outcome by matching French prose.** Branch on a `Result.Code` or an enum member's own
  name — a `Contains("déjà facturée")` once made rewording a sentence change behaviour.
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
| `AUTH0_SETUP.md` | Auth0 tenant/app configuration |
| `GOOGLE_CALENDAR_SETUP.md` / `_FR.md` | Google Calendar OAuth setup (EN/FR) |
| `GOOGLE_CALENDAR_SYNC_ARCHITECTURE.md` | Calendar sync design |
| `SYNC_TESTING_GUIDE.md` | How to test calendar sync |
| `api/CLINIC_MANAGEMENT_FLOW.md`, `MULTI_CLINIC_SETUP.md`, `ROLE_ASSIGNMENT_IMPLEMENTATION.md`, `ENTITY_UPDATE_GUIDE.md`, `IMPLEMENTATION_SUMMARY.md` | Backend feature/process docs |
| `packaging/README.md` | Local/offline-LAN operator guide (Phase 5): publish + installers, bundled PostgreSQL, backup/restore, admin recovery, per-AC verification checklist |

> When code changes, update the nearest `CLAUDE.md` so this map stays accurate.

> When code changes, update the nearest `CLAUDE.md` so this map stays accurate — and put the *reasoning* in
> `features/<slug>/notes.md` or `ARCHITECTURE.md`, never here. This file is a map, and it stops working the
> moment it becomes a changelog.
