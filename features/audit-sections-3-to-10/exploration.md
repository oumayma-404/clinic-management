# Exploration — Audit Sections 3–10

**Date:** 2026-07-27
**Branch:** `feature/windows-desktop-app` @ `1932acf` — contains **both** the merged audit § 2 work (PR #15) **and
the merged audit § 1 work** (`feature/data-and-money-integrity`, merged after this exploration ran).
**Source:** `CODEBASE_AUDIT_2026-07.md` §§ 3–10, written at `22b37a1` (pre-both-merges)
**Method:** 8 parallel read-only exploration agents, one per subsystem cluster. Every finding re-verified against
current source.

> ⚠️ **Line numbers below were captured at `e073ba7`, before the § 1 merge.** They are correct for every finding
> outside the money/appointment files. The § 1 merge moved lines in the files listed in § 7 — **re-read those before
> writing an AC against them**. Verified drifts so far: `IssueInvoiceCommand.cs:72 → :77`,
> `AcceptTreatmentPlanCommand.cs:61 → :62`, `UpdateAppointmentCommand.cs:249 → :315`,
> `GetReceivablesQuery.cs:87 → :88`, `CreateInvoiceFromTreatmentPlanCommand.cs:81 → :82`.

> **Read this before the spec.** The audit is organised by *symptom* (silent no-op, localization, UX…), so its
> sections interleave the same files. This document is organised by *verdict* and then by *subsystem*, which is how
> the spec's parts are grouped.

---

## 1. Headline corrections to the audit

These change the scope, the count, or the fix itself. Each is verified.

| # | Correction | Evidence |
|---|---|---|
| C-1 | **§ 6 has 12 bullets, not the index's 9.** True §§ 3–10 total is **57**, not 54. Same undercount class the § 2 spec caught (index said 12, section had 14). | `CODEBASE_AUDIT_2026-07.md:222-257` — count the `- [ ]` items |
| C-2 | **§ 4.1's stated symptom is not reachable through the UI.** Both callers already send browser-local bounds; only the `??` server defaults are UTC. | `web/app/caisse/page.tsx:73-78`, `web/lib/hooks/use-dashboard-stats.ts:24-31` |
| C-3 | **§ 6.2 is already fixed on the § 1 branch.** Its AC-42 makes the dashboard net avoirs via the same `GetRefundedBetweenAsync` the caisse uses; AC-40 adds `CreditedTotal`. Speccing it today writes a fix § 1 contradicts. | `features/data-and-money-integrity/spec.md` AC-40, AC-42, AC-43, `:416` |
| C-4 | **§ 7.5 lists five paths, not six.** The likely intended sixth is the sibling dialog. ~12 further same-class swallows exist beyond the audit's list. | `web/components/edit-appointment-dialog.tsx:130-132` |
| C-5 | **§ 10.2's ICU claim is refuted.** The DLLs are `icu*67.dll`, but `postgres.exe` FileVersion is **16.9** — EDB genuinely bundles ICU 67.1 with PG 16. The probe-list defect stands; the directories are **gitignored**, so they are local artifacts, not vendored content. | `.gitignore:28-34`; Win32 version resources |
| C-6 | **§ 10.3's 46 reconciles with § 2's 58 baseline** — no discrepancy. 46 CS8618 + 6 CS8602 + 2 CS8600 + 1 CS8604 + 1 CS0618 + 2 CS8981 = 58. | `features/security-hardening/stories/progress.md:65` |
| C-7 | **§ 6.4 and § 6.5 are each feature-sized**, not 🟡 P2 bullets. § 6.4 is hard-blocked on § 1; § 6.5 needs domain input the repo does not contain. See § 6 below. | |
| C-8 | **§ 5.9 is a one-token fix.** `/users` was never mode-gated — only the nav link is. | `web/app/users/page.tsx:16,29` vs `web/components/dashboard-sidebar.tsx:86` |
| C-9 | **§ 8.6 is one component, not two.** `edit-patient-dialog.tsx` *is* the create form (mounted with `patient={null}`). | `web/app/patients/page.tsx:87-90` |
| C-10 | **§ 8.2 and § 8.6 both undercount.** Seven more English strings in `clinic-settings.tsx` alone; one more American placeholder. | listed in § 5 below |

### Line drift caused by the § 2 merge

| Audit cited | Actual now | File |
|---|---|---|
| `:73` | `:75` | `GetDashboardStatsQuery.cs` (§ 4.1) — `ILogger` field added |
| `:198` | `:214` | `procedure-types-table.tsx` (§ 8.5) — and it is `toFixed(2)`, dropping the millime |
| `:71` | `:76` | `ProcedureTypesController.cs` (§ 10.4) — `AdminOnly` + 3-line comment added |
| `:244` | `:274` | `Program.cs` CS0618 (§ 10.4) |
| `:30` | `:55` | `User.cs` CS8618 (§ 10.3) |
| `API/Migrations` | `Infrastructure/Migrations` | § 10.4 CS8981 — wrong project in the audit |

---

## 2. Verdict table — all 57 findings

**Legend:** ✅ confirmed as written · ⚠️ confirmed but the audit's framing is wrong · ❎ refuted · 🔒 blocked on § 1

### § 3 — Silent no-ops (4)

| # | Finding | Verdict | Current location |
|---|---|:--:|---|
| 3.1 | « Terminé » no-op from Planifié/Confirmé | ✅ | `UpdateAppointmentCommand.cs:249-255`; UI `edit-appointment-dialog.tsx:674` |
| 3.2 | « Cancel » no-op on Completed | ✅ | `UpdateAppointmentCommand.cs:256-264`; button `edit-appointment-dialog.tsx:703` |
| 3.3 | « Rappel envoyé » with no channel | ✅ | `SendRecallCommand.cs:58-59`; `ReminderScheduler.cs:83-87`; `recalls/page.tsx:198-204` |
| 3.4 | Double-booking check-then-insert | ✅ | `CreateAppointmentCommand.cs:156-171`; `UpdateAppointmentCommand.cs:307-333` |

### § 4 — Timezone (2)

| # | Finding | Verdict | Current location |
|---|---|:--:|---|
| 4.1 | Caisse/dashboard UTC day boundary | ⚠️ | `GetCaisseSummaryQuery.cs:59`, `GetDashboardStatsQuery.cs:75,95` — **defaults only**; both UI callers override (C-2) |
| 4.2 | Numbering year from `UtcNow.Year` | ✅ | `IssueInvoiceCommand.cs:72`, `AcceptTreatmentPlanCommand.cs:61` — no override, fully reachable |

### § 5 — Built but unreachable (12)

| # | Finding | Verdict | Current location |
|---|---|:--:|---|
| 5.1 | Devis amend unreachable | ✅ | `treatment-plans.ts:105` (0 callers); `TreatmentPlansController.cs:98-105`; badge `plan-workspace.tsx:168` |
| 5.2 | `reviseInstallments` unreachable | ✅ | `treatment-plans.ts:109-113`; `TreatmentPlansController.cs:108-116` |
| 5.3 | No un-do for a réalisé act | ✅ | `TreatmentPlanItem.cs:107-108`; auto-complete `TreatmentPlan.cs:276-279`; `treatment-plans.ts:101` (0 callers) |
| 5.4 | Per-dentist working hours no UI | ✅ | `doctors.ts:25,28` (0 callers); `DoctorsController.cs:66-71,74-80` |
| 5.5 | No Google Calendar disconnect | ✅ | `Clinic.cs:171-176` (declaration only); no controller action |
| 5.6 | No UI to delete a fiche de soins | ✅ | `DentalRecordsController.cs:70-86`; `dentalRecordsApi.delete` 0 callers |
| 5.7 | No UI to delete a medical document | ✅ | `MedicalDocumentsController.cs:224-238` |
| 5.8 | No role change | ✅ | `User.cs:100-106` (0 callers); `UsersController.cs` has 3 actions only |
| 5.9 | User mgmt invisible in Cloud | ⚠️ | `dashboard-sidebar.tsx:86` — **nav link only**; page + controller already work in Cloud (C-8) |
| 5.10 | CNAM math duplicated | ✅ | `CnamNomenclatureController.cs:51`; `cnam-nomenclature.ts:53,69-77,97-111` |
| 5.11 | `PUT /doctors/{id}` no caller | ✅ | `DoctorsController.cs:42-47`; **no client function exists at all** |
| 5.12 | `/procedure-types/colors` no caller | ✅ | `ProcedureTypesController.cs:135-140`; duplicate `procedure-type-form-modal.tsx:29-41` |

### § 6 — Product gaps (12 — the index says 9)

| # | Finding | Verdict | Current location |
|---|---|:--:|---|
| 6.1 | Working hours advisory only | ✅ | zero hours references in `Features/Appointments/`; grid `appointment-calendar.tsx:21-29` |
| 6.2 | Dashboard vs caisse after refund | ✅❎ | **CLOSED by the § 1 merge** — `GetDashboardStatsQuery.cs:115-118` now nets refunds. Regression AC only (C-3, § 7) |
| 6.3 | Failed reminders invisible | ✅ | `GetClinicReminderStatusQuery.cs:58-61,91-102`; `ReminderStatusDto.cs:16-28`; `NotificationJob.cs:148-164` |
| 6.4 | No audit trail | 🔒 | feature-sized + blocked on § 1 — see § 6 below |
| 6.5 | CNAM stops at an estimate | ⚠️ | feature-sized, needs domain input — see § 6 below |
| 6.6 | Stock expiry unreachable | ✅ | `StockItem.cs:17-18` ↔ `stock-table.tsx:159`; `StockItemDto.cs:5-20` omits both |
| 6.7 | Stock never consumed by an act | ✅ | zero `StockItem` refs outside `Features/Stock/` |
| 6.8 | invoice↔appointment link never set | ✅ | `invoices.ts:19` declared; `invoice-form-modal.tsx:190` never sends it |
| 6.9 | Recurring conflicts count-only + NoShow | ✅ | `recurring-series/page.tsx:162-163`; `CreateRecurringSeriesCommand.cs:181-183` |
| 6.10 | `LabWorkOrder.SetStatus` no rules | ✅ | `LabWorkOrder.cs:92-98` |
| 6.11 | Dental-record delete orphans links | ✅ | `DeleteDentalRecordCommand.cs:37-76`; `InvoiceLineConfiguration.cs:36,46`; `TreatmentPlanItemConfiguration.cs:55` |
| 6.12 | `UpdateStockItemCommand` no ledger row | ✅ | `UpdateStockItemCommand.cs:75-80`; `StockItem.cs:108-115` |

### § 7 — Frontend UX (9)

| # | Finding | Verdict | Current location |
|---|---|:--:|---|
| 7.1 | Unusable on a phone | ✅ | `dashboard-sidebar.tsx` **0 responsive classes**, `:125-129`, default expanded `sidebar-context.tsx:13`; `dashboard-header.tsx:141`; `ai-chat.tsx:676`; `document-editor-content.tsx:1640` |
| 7.2 | ClinicGuard → Next 404 | ✅ | `clinic-guard.tsx:33`; no `app/auth/**`; loop via `middleware.ts:26-32` + `login/page.tsx:83-86` |
| 7.3 | Patients list has no edit | ✅ | `patients-table.tsx:34-35,296-301` — setters never called |
| 7.4 | AI auto-speaks, no mute | ✅ | `ai-chat.tsx:300-303`; only control `:684-690` |
| 7.5 | Swallowed error paths | ⚠️ | **five**, not six (C-4): `files/page.tsx:33-35`, `factures/page.tsx:35-37`, `patients/[id]/page.tsx:498-500`, `:470-472`, `create-appointment-dialog.tsx:279-282` |
| 7.6 | Double-click makes two folders | ✅ | `patient-files-manager.tsx:602-607`, handler `:179-199` |
| 7.7 | `/patients` blank, no skeleton | ✅ | `patients/loading.tsx:1-3` — the only `loading.tsx` in the repo |
| 7.8 | Accessibility, six sub-items | ✅ | `documents/page.tsx:100-108`; `patient-files-manager.tsx:475-479,526-531,562-574`; both dialogs' date/time labels; `invoices-table.tsx:504-509` |
| 7.9 | Bespoke banner vs sonner | ✅ | `clinic-settings.tsx:134,264-270,539-557` — file imports sonner nowhere |

### § 8 — French localization (6)

| # | Finding | Verdict | Current location |
|---|---|:--:|---|
| 8.1 | Raw English enum status | ✅ | `edit-appointment-dialog.tsx:355,369`; `patients/[id]/page.tsx:1170`; `appointment-list.tsx:73`; Sexe `:1438` |
| 8.2 | English buttons/headings | ⚠️ | audit's 15 confirmed **+ 7 more** (C-10) |
| 8.3 | Specialty catalog English-only | ✅ | `clinic-settings.tsx:65-73`, `setup-wizard.tsx:23-31`, `join-wizard.tsx:16-24`; **reaches printed documents** |
| 8.4 | `Type: ` prefix persisted | ✅ | writer `create-appointment-dialog.tsx:426,428`; **reader** `edit-appointment-dialog.tsx:193-196,264,266` |
| 8.5 | Currency formatting bypass | ✅ | `lab-orders/page.tsx:83`; `procedure-types-table.tsx:213-214`; `app/page.tsx:23`; `patient-files-manager.tsx:350-354` |
| 8.6 | American placeholders | ⚠️ | one component covers create + edit (C-9); audit missed `:764` |

### § 9 — Realtime, schema & performance (7)

| # | Finding | Verdict | Current location |
|---|---|:--:|---|
| 9.1 | Five orphan broadcast keys | ✅ | `clinic-hub.ts:15-30` (14 keys); **plus a 15th dead key the other way** — see § 4 below |
| 9.2 | `Doctor`/`StockItem` unfiltered | ✅ | `ApplicationDbContext.cs:97-130` (17 filters); stale comment `:79-82` |
| 9.3 | Reminder outbox unindexed/unbounded | ✅ | `NotificationRepository.cs:26-29`; `NotificationConfiguration.cs` has **zero** `HasIndex` |
| 9.4 | `StockMovement.ClinicId` unindexed | ✅ | `StockMovementConfiguration.cs:16,24` — only index is `(StockItemId, CreatedAt)` |
| 9.5 | `StockItem.UnitPrice` is `(18,2)` | ✅ | `StockItemConfiguration.cs:55-56` — the lone exception among 27 money columns |
| 9.6 | Recall query unbounded | ✅ | `GetPatientsToRecallQuery.cs:52-53`, filters in memory `:55-98` |
| 9.7 | N+1 + heavy billed-guard | ✅ | `GetReceivablesQuery.cs:87`; `CreateInvoiceFromTreatmentPlanCommand.cs:81` |

### § 10 — Build & tooling (5)

| # | Finding | Verdict | Current location |
|---|---|:--:|---|
| 10.1 | `npm run lint` broken | ✅ | `eslint.config.mjs:1-3`; `package.json:67-77`; mask `next.config.ts:16`. **And there is no CI at all** |
| 10.2 | Unpinned toolchain downloads | ⚠️ | script confirmed `fetch-build-tools.ps1:35-53,69-75`; ICU claim refuted (C-5) |
| 10.3 | 46 × CS8618 | ✅ | exact, 21 files — breakdown in § 7 below |
| 10.4 | Nine more warnings | ✅ | all located; three line-drifts (see § 1) |
| 10.5 | Startup seeding no retry | ⚠️ | confirmed **and worse** — see § 3 below |

---

## 3. Adjacent defects the audit missed

Per the repo's standing rule (`MEMORY.md` "No deferring in-scope work"), these belong in the same spec — each would
re-break, mask, or block a listed finding.

### Appointment lifecycle

| ID | Defect | Location | Why it must be in scope |
|---|---|---|---|
| A-1 | `Appointment.Confirm()` has **no `Completed` guard** — a finished visit can be pushed back to `Confirmé` | `Appointment.cs:69-76` | Fixing § 3.2 without it leaves a second illegal exit from `Completed` |
| A-2 | `Appointment.Reschedule()` force-sets `Status = Scheduled`, silently downgrading `Confirmé`/`En cours` | `Appointment.cs:144` | Any date change through § 3.1's path silently loses status |
| A-3 | Overlap window is `[start − 1 day, start + duration]` — a long appointment starting >24 h earlier is never loaded | `CreateAppointmentCommand.cs:158-160` | § 3.4's fix is incomplete without it |
| A-4 | The overlap guard is skipped entirely when `DoctorId` is null | `CreateAppointmentCommand.cs:156` | ditto |
| A-5 | Working-hours JSON has **zero** validation — `[{"day":"Blorp","from":"99:99","to":"aa"}]` round-trips and persists | `WorkingHoursDto.cs:27-49`; setters `Clinic.cs:150-154`, `Doctor.cs:109-113` | § 6.1 would enforce garbage; § 5.4's UI would write it |
| A-6 | `edit-patient-dialog.tsx:474` can persist the literal `"Unknown"` gender — a 4th value beyond Male/Female/Other | `edit-patient-dialog.tsx:474` | § 8.1's Sexe map must handle it |

### § 2 residuals sitting inside §§ 3–10 files

These are audit § 2 item 14 (`{ex.Message}` leaks) that the AC-13.2 sweep did not reach. They are in files this
spec edits, so they are cheaper to fix here than to leave. **They do not absorb § 2's remaining P4/P5 work.**

| ID | Site | Note |
|---|---|---|
| A-7 | `CreateRecurringSeriesCommand.cs:230` | raw `ex.Message`; the sweep fixed `Create`/`UpdateAppointmentCommand` but not this sibling |
| A-8 | `DeleteMedicalDocumentCommand.cs:98` | `$"Error deleting medical document: {ex.Message}"` — English **and** a leak |
| A-9 | `DeleteDentalRecordCommand.cs:44` | `"Unable to resolve current clinic"` — English leak |
| A-10 | `SetDoctorWorkingHoursCommand.cs:78` | raw `ex.Message` in the handler § 5.4's UI will call |

> `progress.md:14,362` records **~79 other `{ex.Message}` sites** and the never-created `ErrorMessageLeakGuardTests`
> (AC-13.6) as § 2's outstanding work. That stays § 2's. Only the four above are in this spec's blast radius.

### Reachability / domain

| ID | Defect | Location |
|---|---|---|
| A-11 | `User.Update(role, email = null, fullName = null)` — called with one argument it **silently nulls email and full name**, and never validates the role string | `User.cs:100-106` |
| A-12 | `DeleteDentalRecordCommand` and `DeleteMedicalDocumentCommand` carry **no role policy** — any `secretary` can delete a fiche or an ordonnance | `DentalRecordsController.cs:12`; `MedicalDocumentsController.cs:21` |
| A-13 | `POST /treatment-plans/{id}/items/{itemId}/done` also has **no role policy** | `TreatmentPlansController.cs:84` |
| A-14 | `GET /procedure-types/colors` returns bare hexes with **no names**, so § 5.12 still needs a client-side label map | `ProcedureTypesController.cs:138` |

### Stock / realtime / schema

| ID | Defect | Location |
|---|---|---|
| A-15 | **`documents` is a 15th dead key in the opposite direction** — declared in `clinic-hub.ts:19`, zero subscribers | `clinic-hub.ts:19` |
| A-16 | `StockMovement.Reason` is **never populated** — the ctor takes it, the column exists, both write sites omit it | `RestockStockItemCommand.cs:57-59`; `ConsumeStockCommand.cs:63-65` |
| A-17 | `StockMovementType` has only `Consume`/`Restock` — § 6.12's fix needs a third member | `StockMovementType.cs:4-8` |
| A-18 | **No central decimal convention** — all 27 money columns declare precision inline. A `ConfigureConventions` + `HavePrecision(18,3)` makes § 9.5 unrepeatable rather than fixed once | no `ConfigureConventions` anywhere |
| A-19 | `RealtimeResourceResolverTests` is a *contains*-style theory, not an exact set — a forgotten new area silently passes | `RealtimeResourceResolverTests.cs:35-52` |

### Money / timezone

| ID | Defect | Location |
|---|---|---|
| A-20 | `payment-modal.tsx:36` pre-fills the payment date from `new Date().toISOString()` — the **UTC** date, so between 00:00 and 01:00 Tunis it defaults to *yesterday*. Un-overridable, and the real § 4.1 symptom | `payment-modal.tsx:36,61` |
| A-21 | `TunisiaTimeZone` + `ResolveTunisiaTimeZone()` are **duplicated byte-for-byte** in two files, used only for display formatting | `NotificationGenerator.cs:26,303-322`; `ReminderScheduler.cs:27,246-264` |
| A-22 | 5 × `DateTime.Today` in `AIActionService` — **server-machine-local**, a third convention, worse than `UtcNow` | `AIActionService.cs:384,484,496,501,520` |
| A-23 | Same UTC-day defect class, un-overridable, in four more reads | `GetPatientBillingSummaryQuery.cs:90`, `GetReceivablesQuery.cs:94`, `GetPatientsToRecallQuery.cs:94`, `GetPatientAiSummaryQuery.cs:199` |

### Build / startup

| ID | Defect | Location |
|---|---|---|
| A-24 | **`BackfillAsync` is never called in Local mode** — its only call site is inside `if (!isLocalAuthMode)`. Local installs get no admin backfill on any path | `Program.cs:494,509` |
| A-25 | The Local seeder is fire-and-forget `Task.Run`, and its catch calls `StopApplication()` — a transient DB blip **kills the Windows service** rather than skipping the seed | `DeferredStartupService.cs:44,61,75-79` |
| A-26 | No `.editorconfig`, no `Directory.Build.props`, no `TreatWarningsAsErrors` in any of the 6 `.csproj` — the 58 warnings can never fail a build | all `.csproj` |

---

## 4. § 9.1 — the definitive realtime mapping

Key source: `RealtimeResourceResolver.cs:46-47` lowercases the `Features/<Area>/` segment.
Excluded areas (`:19-22`): `Auth`, `AI`, `Backup`, `Connectivity`.

**Orphans — backend emits, frontend has no key (5 areas, 17 commands):**
`doctors` (2 cmds) · `expenses` (3) · `laborders` (4) · `recall` (4) · `waitinglist` (4)

**Dead key — frontend declares, nothing subscribes (1):** `documents` (`clinic-hub.ts:19`)

**Pages that mutate and never subscribe:**

| Page | Mutations | Key needed |
|---|---|---|
| `waiting-list/page.tsx` | `:185,200,223,243` | `waitinglist` + `appointments` |
| `lab-orders/page.tsx` | `:177,180,380,399` | `laborders` |
| `caisse/page.tsx` | `:149,433,436` | `expenses` + `invoices` |
| `recalls/page.tsx` | `:73,185,193,201` | `recall` |
| `recurring-series/page.tsx` | `:160,402` | `appointments` |
| `mon-profil-content.tsx` | `:94` | `doctors` |
| `creances/page.tsx` | read-only | `invoices` + `treatmentplans` |
| `app/page.tsx` (dashboard) | read-only | `appointments` + `invoices` |
| `documents/[type]/page.tsx` | doc CRUD | `documents` (key exists, unused) |

`/waiting-list` is the worst case — the canonical two-user screen, emits on all four mutations, refreshes only local state.

**How the contract test must change** (it currently asserts C# against a C# copy of the frontend list, so a missing
frontend key is structurally undetectable):
1. Replace the `[InlineData]` list with a reflection scan over every `IRequest` in `*.Features.<Area>.Commands`,
   asserting the resulting **set** equals a hard-coded expected set — the `ControllerAuthorizationCoverageTests` /
   `TreatmentPlansControllerAuthorizationTests` exact-set pattern.
2. Parse `web/lib/realtime/clinic-hub.ts` and assert **both** directions: every emitted key has a listener, and every
   declared key has an emitter (this is what catches `documents`). Intentional exemptions go on a named allow-list.

---

## 5. Full string inventories

### § 8.2 — English strings (audit's 15, confirmed at current lines)

`edit-appointment-dialog.tsx:406` "Date & Time" · `:707` "Cancel Appointment" · `:710` "Close"
`create-appointment-dialog.tsx:727` "Date & Time" · `:975` "Duration set to N minutes" · `:984` "Clear" · `:1098` "Cancel"
`clinic-settings.tsx:502` "Loading clinic settings..." · `:869` "Add Doctor" · `:734,879,970` "Cancel" ×3
`procedure-types-table.tsx:111` "Loading procedure types..." · `:124` "Procedure Types"
`patients/[id]/files/page.tsx:76` "Back to Patient" · `appointment-calendar.tsx:116` "Push"
`patient-files-manager.tsx:486` "file"/"files"

### § 8.2 — seven the audit missed

`clinic-settings.tsx:534` "Share with coworkers to join this clinic" · `:578,770,916` "Edit" ×3 · `:588` "Clinic Name"
· `:627` "Full Address" · `:643` "Phone Number"
`reminder-settings.tsx:582` "Phone Number ID" (Meta field — arguably technical, decide explicitly)
`patient-files-manager.tsx:350-354` `" B"`/`" KB"`/`" MB"` → should be `o`/`Ko`/`Mo`

### § 8.6 — placeholders

`edit-patient-dialog.tsx` — `:571` John · `:587` Doe · `:665` john.doe@email.com · `:679` 123 Main Street ·
**`:764` "Hypertension, Type 2 Diabetes" (missed by the audit)** · `:776` Penicillin, Shellfish ·
`:973` Blue Cross Blue Shield · `:984` BCBS-123456789 · `:995` Group-12345
Also `create-appointment-dialog.tsx:605` John, `:618` Doe (inline new-patient sub-form) ·
`procedure-type-form-modal.tsx:215` "e.g., 70.00"
Already correct: `edit-patient-dialog.tsx:705` "Tunis", `:648` "Ex. 20 123 456 (ou +216…)"

### § 10.3 — CS8618 per file (46 total, 21 files)

`MedicalDocument.cs` 8 · `Patient.cs` 5 · `Address.cs` 4 · `Doctor.cs` 3 · `PatientFile.cs` 3 · `StockItem.cs` 3 ·
`InsuranceInfo.cs` 2 · `LabWorkOrder.cs` 2 · `Notification.cs` 2 · `PatientFamilyHistory.cs` 2 · `ProcedureType.cs` 2 ·
`ColorHex.cs` 1 · `Email.cs` 1 · `PhoneNumber.cs` 1 · `Clinic.cs` 1 · `Expense.cs` 1 · `PatientFlag.cs` 1 ·
`PatientFolder.cs` 1 · `PatientMedicalHistory.cs` 1 · `RecurringAppointment.cs` 1 · `User.cs` 1

Every one is a non-nullable reference property left unassigned by the `private X() { }` EF constructor.

---

## 6. The two feature-sized items (§ 6.4, § 6.5)

### § 6.4 — Audit trail

The bullet is really **three** unrelated things:

1. **Archive / anonymize on delete** — archive is **already built by § 1** (`IsArchived`, `ArchivedAt`,
   `ArchiveReason`, `Archive`/`Unarchive`, `POST /patients/{id}/archive`, `/unarchive`, `/deletion-check`, migration,
   UI, tests). Anonymize does not exist and no spec has ever asked for it — **zero** GDPR/RGPD code in the repo.
2. **Patient merge** — § 1 killed it deliberately (`spec.md:615-616`: *"Merge is a separate problem with its own
   conflict rules"*). It needs the audit trail to exist first.
3. **The trail proper.**

**Blocked on § 1, not merely colliding.** § 1 owns every seam the trail needs:

| Seam | What § 1 does |
|---|---|
| `Entity<TId>` | adds `public uint Version` mapped to `xmin` |
| `OnModelCreating` | inserts a 36-line reflection loop over `GetEntityTypes()` |
| `UnitOfWork.SaveChangesAsync` | wraps it in `catch (DbUpdateConcurrencyException)` → `ConflictException` |
| `IUnitOfWork` | adds `SetExpectedVersion(object, uint)` |
| ~120 handler files | rewrote every `catch (Exception ex)` to `when (ex is not ConflictException)` |
| `Payment` / `InstallmentPayment` | already carry `VoidedByUserId` + `VoidedByName` — the first-ever actor fields |

An audit writer placed at `ApplicationDbContext.SaveChangesAsync:245` would run **inside** § 1's concurrency catch,
so an audit-write failure would surface as a bogus 409.

Also relevant: `ICurrentUserService` **does not exist**. `IClinicContext.GetUserId()` (`IClinicContext.cs:21`) gives
the id with no new dependency, but the *name* needs an extra `IUserRepository` hit — § 1's `VoidPaymentCommand.cs:88-89`
is the precedent. ~94 handlers inject only `ICurrentClinicResolver` and hold no `IClinicContext`.
No entity anywhere has `CreatedBy`/`UpdatedBy` (grep: zero matches). Five entities have **neither** timestamp:
`Installment`, `InvoiceLine`, `TreatmentPlanItem`, `PatientFile`, `NotificationRead` — four are children of the
aggregates in scope.

**Honest sizing (trail only, 4 aggregates):** 1 entity, 2 migrations, 3 endpoints, 1 retention job, 2 UI screens,
**38 command handlers touched**, plus a cross-cutting interceptor and an unresolved retention decision.

### § 6.5 — CNAM reconciliation

Nothing submission-shaped exists. `MedicalDocument` has no status, no submission date, no reference number — its
only lifecycle field is `bool IsDraft`, and `cnam-bulletin-soins/spec.md:32` locked in "no schema change".
`DentalActCode.RequiresAccordPrealable:28` is stored and read by **nothing**.

**Deferred three times, independently:**
- `cnam-bulletin-soins/spec.md:36` — *"Electronic submission / télétransmission to CNAM; tiers-payant flows."*
- `cnam-bulletin-soins/spec.md:38` — *"Bordereaux batch generation & reconciliation (AP1/AP2)…"*
- `cnam-bs1-official-overlay/spec.md:39` — *"No electronic/online CNAM submission — paper fill-and-print only."*
- `cnam-nomenclature-lookup/spec.md:36` — *"claim submission; tiers-payant / bordereau / télétransmission"*

**Honest sizing:** ~5 new entities (`CnamClaim`, `CnamBordereau`, `CnamBordereauLine`, `CnamReimbursement`,
`CnamRejection`), ~4 migrations, ~12 endpoints, ~5 UI screens, a second calibrated official-form overlay (AP1/AP2 —
which nobody has; getting BS1's coordinates right was an entire feature), and a `PaymentMethod` enum change whose
blast radius is `PaymentMethodLabels.cs:8` (silent English fallback), 5 `Enum.TryParse` sites, 4 entities, and **two
independent frontend duplications** (`invoice-labels.ts:11-18`, `caisse/page.tsx:45-52`).

The deeper problem: a CNAM receipt is a **third-party** payment, and the caisse and dashboard currently treat every
`Payment` as patient cash-in. Adding a third money source to two reads that § 6.2 already says disagree is a real risk.
And it needs domain knowledge the repo does not contain (partial payments, rejection motifs, resubmission rules,
accord préalable, AP1 vs AP2, tiers-payant).

**Honest first slice if it must be sliced:** *"record what CNAM actually paid against a claim"* — claim +
reimbursement entities, 1 migration, 4 endpoints, 2 screens, no bordereau, no AP1/AP2 form, **no enum change**
(keep the CNAM receipt out of `Payment` until the caisse/dashboard question is settled).

---

## 7. The § 1 merge — RESOLVED, no gating needed

**§ 1 merged into `feature/windows-desktop-app` on 2026-07-27, after this exploration ran.** HEAD is `1932acf`;
`git merge-base --is-ancestor feature/data-and-money-integrity HEAD` returns true. The collision analysis that
produced a "gated final part" plan is **obsolete** — every one of the four entangled findings is now unblocked, and
one of them is closed outright.

> ✅ **The merge landmine did not fire.** `GetDashboardStatsQuery.cs` kept the § 2 `ILogger` field (`:40,50,59`) and
> the AC-13.2 catch (`:145-146` — `LogError` + « Erreur lors du chargement du tableau de bord. Veuillez réessayer. »).
> PR #15's exception-leak fix survived the merge.

| Finding | Post-merge verdict | Evidence at `1932acf` |
|---|---|---|
| § 4.1 | **open, implement normally** | Defaults still UTC; the blocks moved but the defect is unchanged |
| § 4.2 | **open, implement normally** | `IssueInvoiceCommand.cs:77` and `AcceptTreatmentPlanCommand.cs:62` both still `DateTime.UtcNow.Year` |
| § 6.2 | **CLOSED by § 1 — verify only** | `GetDashboardStatsQuery.cs:38,48,57` inject `ICreditNoteRepository`; `:115-118` computes `invoiceCollected + installmentCollected - refunds`. Spec this as a regression AC, not a fix |
| § 6.8 | **open, implement normally** | `invoice-form-modal.tsx` still never sends `appointmentId` |
| § 9.7a | **open** | `GetReceivablesQuery.cs:88` — the N+1 `GetByIdAsync` survived the merge |
| § 9.7b | **open** | `CreateInvoiceFromTreatmentPlanCommand.cs:82` — still `GetFilteredAsync` |
| § 8.5 | **open** | untouched |
| § 5.10 | **open** | untouched |

### What § 1 changed that this spec must now build **on**, not around

| § 1 asset (now in the tree) | Consequence here |
|---|---|
| `Entity<TId>.Version` mapped to PostgreSQL `xmin` for all 38 entities, **no schema change** | § 6.4's audit trail must not re-add a version concept. The `AddConcurrencyToken` migration has a **deliberately empty `Up()`** — EF's differ emits 38 × `AddColumn<uint>("xmin")`, which PostgreSQL rejects. It exists for the model snapshot only. Any new migration must not "helpfully" re-add those columns |
| `ConflictException` translated once in `UnitOfWork.SaveChangesAsync` → HTTP 409 | An audit writer must sit **outside** that catch, or an audit-write failure becomes a bogus 409 |
| `when (ex is not ConflictException)` on every catch that **returns a Result** (log-only catches deliberately still swallow) | Every new handler in this spec must follow the same rule |
| `IUnitOfWork.SetExpectedVersion`; six aggregates round-trip `Version`; `0` means "not supplied" and skips the check | Keeps the AI dispatcher, Google→App sync and the jobs working. New write paths inherit this |
| **Tri-state generalized** to `ProcedureTypeId`/`DoctorId`/`Notes`/`DoctorName` (`UpdateAppointmentCommand.cs:79,100,228,234,250`) | **§ 3.2's worst collateral is gone** — cancelling an appointment no longer wipes its act. A-1…A-4 still stand |
| `Payment` / `InstallmentPayment` carry `VoidedByUserId` + `VoidedByName` | The first actor fields. § 6.4's trail should subsume or align with them, not duplicate |
| `Patient.IsArchived` + archive/unarchive/deletion-check | § 6.4's archive limb is **done**. Merge and anonymize remain |
| Event-sourced `InstallmentPayment` ledger | § 4.1's installment-month defect (audit § 1.7) is closed; the day-boundary defect is not |
| `reconcile-money` console verb, exit 0/1/2 | A precedent for any maintenance verb this spec adds |

§ 1 introduced **no** clock/timezone abstraction — nothing to reuse, and § 4.1/§ 4.2 must not write a third copy of
`ResolveTunisiaTimeZone` (A-21).

**Constraint for any local-day helper:** `ApplicationDbContext.cs:135-166` installs a `ValueConverter` on every
`DateTime` and treats `DateTimeKind.Unspecified` as UTC on write. A helper must return an explicit UTC instant via
`TimeZoneInfo.ConvertTimeToUtc(localMidnight, TunisiaTimeZone)`, never a bare local `DateTime`.

---

## 8. Repo conventions the spec must follow

### Spec / plan / story house style

- **Frontmatter:** `**Status:**` · `**Challenged:**` · `**Type:** Full|Small` · `**Created:**` · `**Scope:**` ·
  `**Source:**` · `**Branch:**` · `**Feature:**`. (§ 2's spec omits `Type` — an inconsistency; include it.)
- **AC numbering:** two live schemes. `AC-<US>.<n>` (security-hardening) vs flat `AC-1…AC-77` (§ 1).
  **With 57 findings, use `AC-<part>.<n>`** — flat numbering to AC-300 is unreadable.
- **AC phrasing:** present indicative, one falsifiable claim, name the file/symbol/status code/French string, and
  **say why** when counter-intuitive. Bold the load-bearing word. French UI text in « guillemets ».
- **Traceability table:** `| # | Sev | Audit finding | Story |` with centred `Sev`, severity emoji, and rows the audit
  does not contain marked `—` in `#` and bolded `**Not in the audit**`. Add a `Part` column for 57 rows.
- **Parts:** vertical increments, never technical-layer groupings; each ends committable; a part boundary is the
  commit point and the split point if a session runs long (mitigation for **R-1**). Each plan part ends `**Done when:**`.
- **Risks:** one table `| ID | Risk | Likelihood | Impact | Part | Mitigation |`; IDs bolded, **never renumbered** when
  appended out of order.
- **Deviations:** in `stories/progress.md`, two tiers — full `### DEV-n` blocks (Date/Story/Category/Original Plan/
  Actual/Justification/Impact/Approved) and an `## Auto-Approved Deviations` table for trivia.
- **Test plans are written inline**, not deferred to a skill. `## Verification & Tests` with `| Area | Test type |
  What it pins |`. No `test-plan-*.md` exists anywhere; do not invoke `/plan-e2e-tests` or `/plan-api-tests`.
- **`features/LEARNINGS.md` has no process learnings** — every entry is technical, and it is stale. The sizing and
  estimation lessons live in the feature artifacts (`security-hardening/plan.md:252` R-1,
  `data-and-money-integrity/plan.md:550` R-15, `security-hardening/stories/progress.md:333-341`).

**LEARNINGS entries that shape ACs here:** `:39-43` reflection-based exact-set allow-list test (the model for § 9.1's
contract test) · `:97-101` security gates fail closed and loud · `:33-37` one client-wrapper seam, not N call sites ·
`:179-183` one shared constant for a policy value enforced in >1 place · `:185-189` the central error parser must
cover every server error shape · `:225-229` the FE gate is `tsc --noEmit` + `npm run build`, nothing else.

### Test conventions

- Folders: `Api/`, `Common/`, `Domain/`, `Features/<Area>/`, `Hubs/`, `Infrastructure/`. ~117 classes (the
  CLAUDE.md's "~90" is stale). The project references **Application, Infrastructure and API**.
- Classes `<Subject>Tests.cs`; methods `Pascal_Snake_Case`. **Spec-ID traceability is mandatory** — class-level
  `<summary>` and per-test `// [AC-n]` comments.
- Handler pattern: fixed GUIDs `aaaa…`/`bbbb…`, `Mock<T>` fields, a `Handler()` factory, `NullLogger<T>.Instance`,
  assert `IsSuccess`/`IsFailure` + entity state + `Verify(SaveChangesAsync, Times.Never/Once)`.
  **No FluentAssertions, no DB, no `Guid.NewGuid()` in stable assertions.**
- **Guard tests that will fail the build when this spec adds surfaces:**
  `ControllerAuthorizationCoverageTests` (exact `[AllowAnonymous]` set) ·
  `AdminSurfaceCoverageTests` (rule-based, added by § 2 P4.6) ·
  `TreatmentPlansControllerAuthorizationTests` (`Every_Action_Is_Classified_By_This_Test` — **any new action on that
  controller fails until classified**) · `CnamControllerAuthorizationTests` · `MedicationsControllerAuthorizationTests` ·
  `AuthorizationPoliciesTests` · `RealtimeResourceResolverTests` · 8 × `*TenantIsolationTests` ·
  `MoneyReadConsistencyTests` (its `Wire()` **hand-reimplements repository SQL** — a new repo filter must be mirrored
  there or the suite passes while production is wrong) · `RateLimitingTests` (added by § 2 P3.1).
- **`ErrorMessageLeakGuardTests` (AC-13.6) was never created** — unclaimed § 2 work sitting in this spec's path.
- **Zero frontend tests and zero frontend test infrastructure.** No jest/vitest/playwright/cypress, no `__tests__/`,
  no `e2e/`. FE ACs are covered by implementation + `tsc` + `npm run build` + deferred manual verification.

### Quality gates and current baselines

| Check | Command | Requirement |
|---|---|---|
| Backend build | `dotnet build api/ClinicManagement.sln --no-incremental` | 0 errors; **0 new** warnings in changed files |
| Frontend types | `cd web && npx tsc --noEmit` | 0 errors |
| Frontend build | `cd web && npm run build` | clean — expect **27/27 static pages** |
| Tests | `dotnet build -p:OutDir=<scratch>/utbuild/` then `dotnet vstest` | pass |

- **Warning baseline: 58** on this branch (`progress.md:65,370`). Test project builds 0/0. The § 1 worktree reports
  116 from base `22b37a1` — different scope; **re-measure before asserting**.
- **Test baseline: 941 passed / 3 failed** (`progress.md:371`). The 3 are `ReminderSchedulerTests` — pure-Moq,
  unrelated, not time-dependent (`progress.md:77,384`).
- **Suite flake:** PDF-render tests share process-wide QuestPDF/`Bs1FontResolver` state and are order-sensitive; one
  run reported 18 failures, three consecutive runs then reported a stable baseline (`progress.md:185`). Judge against
  repeated runs.
- **Never use `dotnet test --no-build` after changing production code** — it runs stale DLLs and produced a
  false negative during § 2 (`progress.md:280`).
- `dotnet test` is Smart-App-Control-blocked (`0x800711C7`) — environmental. Use the `build -p:OutDir` + `vstest` path.
- `MSB3021`/`MSB3027` are file locks from a running API, not compile errors.
- **Lint cannot run** and there is **no CI** — nothing in this repo will ever run these tests automatically.
- `packaging/` is out of reach of every gate — operator-verified only (`packaging/CLAUDE.md:3`).

### Migrations

```bash
dotnet ef migrations add <PascalCaseName> --project api/ClinicManagement.Infrastructure --startup-project api/ClinicManagement.API
```
- Latest is `20260727174753_AddUserTokenVersion`; new ones must sort after it.
- **R-3 hazard:** `migrations add` silently emits an **empty** migration when the API is running. Stop the API first,
  read every generated file before committing, never pass `--no-build`.
- Style templates: index `ExpenseConfiguration.cs:27` (composite clinic-scoped) and `InvoiceConfiguration.cs:83`
  (status + next-attempt — the closest analogue to § 9.3's outbox index); query filter `ApplicationDbContext.cs:126`;
  FK-to-Clinic `StockItemConfiguration.cs:21-26`.

### Frontend conventions

- **Toasts:** `sonner`, `import { toast } from "sonner"`. Canonical pair `invoices-table.tsx:139,143`. Shared helper
  **`showErrorToast(err, fallback)` already exists** at `web/lib/errors.ts:26` — § 7.5's fix is applying it.
- **No i18n layer.** The house pattern is a co-located `*-labels.ts` const map (`invoice-labels.ts`,
  `treatment-plan-labels.ts`, `odontogram-conditions.ts`) — that is what § 8.1's missing `AppointmentStatus` map
  should be. § 8.3's precedent is `setup-wizard.tsx:33-40`'s `weekdayLabelsFr` (English storage keys, French labels).
- **No `Sheet`, `Drawer` or `Skeleton` component exists**, and there is no `matchMedia`/`useMediaQuery` anywhere in
  `web/`. But `vaul@^1.1.2` and `@radix-ui/react-dialog@1.1.4` are already installed, so § 7.1's mobile nav costs
  **zero new dependencies** — add via the shadcn CLI per `web/components/CLAUDE.md`.
- **Only skeleton in the codebase:** `stats-card.tsx:29-33` (`animate-pulse rounded bg-muted` + `aria-label="Chargement"`).
- **Destructive actions:** `AlertDialog` + `[itemToDelete, dialogOpen, deleting]` state triple; complete model at
  `procedure-types-table.tsx:37-42,67,90-105,228-236,250-268`.
- **Role gating:** no hook or helper — literal `const isAdmin = user?.role === "admin"` from `useSession()`, repeated
  in 12 files. Two idioms: a page-level lock card (`app/users/page.tsx:27-50`) and control-level `{isAdmin && (…)}`.
  **Financial reversals deliberately do not gate client-side** — call and surface the 403
  (`plan-workspace.tsx:232-235`).
- **`formatDT`** at `web/lib/format.ts:8-15` — `fr-TN`, always exactly 3 decimals, `" DT"` suffix. Companions
  `roundMillimes`, `formatDateFr`, `formatDate`, `formatDateTime`.

### Branching

- Convention `feature/<kebab-name>` matching `features/<same-name>/`. Branch **off `feature/windows-desktop-app`**,
  PR back into it (PR #13 and PR #15 are the two instances).
- **Not `main`** — 138+ commits behind, would drop the entire billing subsystem.
- **Not `22b37a1`** — would drop all of § 2's work this spec now depends on.
- Commits: `<type>(<scope>): <lowercase imperative>`, often carrying the part/AC/risk id in parentheses.
- Record a `## Base-ref note` in `stories/README.md` and a "Working tree note" listing untracked paths excluded from
  every commit. **Stage explicitly by path — never `git add -A`.**

### Open coordination item

`CODEBASE_AUDIT_2026-07.md` is still **untracked**. § 2's progress note (`:25`) records that committing it was
flagged to the user rather than done unilaterally. § 1's spec (`:711-724`) also plans to rewrite the same six guides
this spec will touch (`CLAUDE.md` ×4, `packaging/README.md`, the audit itself).
