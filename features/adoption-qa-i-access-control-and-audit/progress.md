# Progress: Adoption QA — I (access control and audit)

**Started:** 2026-08-03
**Type:** Small (forced — the traced surface is ~65 files; scope boundary confirmed with the user, see DEV-0)
**Branch:** `feature/audit-sections-3-to-10` (existing; see "Branch note")

## Status
- [x] I1 — the role matrix, applied
- [x] I4 — the orphaned AI-summary endpoint retired
- [x] I5 — self-registration creates a pending account
- [x] I6 — the audit ledger (code, migration applied to the live DB, `verify-schema` extended)
- [x] I3 — client-side mirror
- [x] Quality checks (backend + frontend + schema; see « Quality checks » — the eye pass could not be run here)
- [x] Docs — the five `CLAUDE.md` files that this feature made stale
- [x] Tests (`/test-small-feature` — see « Test Plan » and « Tests Run »)

> **Implementation and tests complete. Nothing is committed** — the user commits manually, and the tree is mixed
> with the in-flight `audit-sections-3-to-10` work (see the working-tree note).

## Branch note

The spec pins « new, off `main` ». The working tree carries **208 uncommitted files** from
`audit-sections-3-to-10`, so checking out `main` would either refuse or drag that work along. The user chose to
implement on the current branch. Nothing is committed by this skill.

## Working tree note (start of session)

208 modified/untracked files unrelated to this feature were already present when this session started
(the in-flight `audit-sections-3-to-10` work: `ProcedureType` categories, reminder settings, PDF/document
renderers, `SchemaVerification*`, and ~70 `web/` pages). **Exclude every one of them from this feature's
commits** — stage only the paths listed under "Files Changed" below, explicitly, never `git add -A`.

⚠️ **Files carrying BOTH this feature's changes and the in-flight ones — diff by hunk before staging:**
`api/ClinicManagement.Infrastructure/Extensions.cs`, `.../Persistence/ApplicationDbContext.cs`,
`.../Migrations/ApplicationDbContextModelSnapshot.cs`, `api/ClinicManagement.Domain/Entities/User.cs`,
`api/ClinicManagement.Application/Extensions.cs`, `CLAUDE.md`, and the four other `CLAUDE.md` files.
⚠️ `api/ClinicManagement.API/Controllers/{MedicalDocuments,ProcedureTypes}Controller.cs` and
`web/lib/api/patients.ts` likewise.

## The role decision (I1), recorded per the spec's request

**`GET /api/patients/{id}/dental-records` → `AdminOrDoctor`** (the spec's recommendation, chosen). Clinical
notes are the most sensitive text in the product, and reception can still tell a visit was billed from
`AppointmentDto.InvoiceId`, which already exists.

## What landed, item by item

### I1 — the role matrix (done)

**Every one of the 32 route controllers now carries a class-level named policy; zero bare `[Authorize]` remain**
(verified by grep). The final vocabulary is four policies — `Authenticated`, `AnyClinicRole`, `AdminOrDoctor`,
`AdminOnly` — and each is genuinely applied somewhere, which is what lets I2's both-directions assertion pass
with no exemption list.

Class-level assignments:

| Policy | Controllers |
|---|---|
| `Authenticated` (role-less by design) | `Auth`, `Clinics` — the onboarding trio (`user-status`, `POST /clinics`, `POST /clinics/join`) is reached by a principal whose role is not yet in the JWT |
| `AnyClinicRole` | `AI`, `Appointments`, `Billing`, `CnamNomenclature`, `Connectivity`, `DentalActs`, `Doctors`, `Invoices`, `LabOrders`, `Medications`, `Notifications`, `PatientFiles`, `Patients`, `ProcedureTypes`, `Recall`, `Stock`, `TreatmentPlans`, `Trust`, `WaitingList` |
| `AdminOrDoctor` | `Dashboard`, `DentalRecords`, `DocumentEmails`, `Expenses`, `MedicalDocuments`, `Odontogram`, `PatientFamilyHistory`, `PatientMedicalHistory` |
| `AdminOnly` | `Audit` (new), `Backup`, `GoogleCalendar`, `Users` |

Per-action forks added on top (the spec's matrix, plus the ones tracing the routes turned up):

- **Billing** — `billing/receivables`, `billing/caisse`, `billing/caisse/ledger` → `AdminOrDoctor`.
  `patients/{id}/billing-summary` and the payment-receipt PDF **stay open** (the spec's one hand-verify row).
- **Invoices** — `revenue` → `AdminOrDoctor`; `DELETE {id}` → `AdminOrDoctor`.
  `POST {id}/payments` **stays open** (the other half of that hand-verify row).
- **Patients** — `DELETE {id}` and `{id}/deletion-check` → `AdminOnly`; `archive`/`unarchive` → `AdminOrDoctor`.
- **Stock** — `DELETE {id}` → `AdminOnly`. **Expenses** — `DELETE {id}` → `AdminOnly`.
- **PatientFiles** — both `DELETE`s → `AdminOrDoctor`; upload/list/download stay open.
- **Doctors** — `PUT {id}` and `PUT {id}/working-hours` → `AdminOrDoctor`; `/me` and the reads stay open.
- **Clinics** — `PUT` (clinic + billing settings) → `AdminOnly`; `reminder-log` and `logo` → `AnyClinicRole`.
- **GoogleCalendar** — class `AdminOnly`, with `status` → `AnyClinicRole`.
- **TreatmentPlans** — authoring (`POST`, `PUT`, `accept`, `complete`, `items/order`, `DELETE`) →
  `AdminOrDoctor`; collecting and printing stay open.
- **Auth** — the redundant bare `[Authorize]` on `change-password` was **removed** so it inherits the class's
  `Authenticated`.

Three judgement calls worth re-reading if they turn out wrong:

1. **`GET /api/clinics/reminder-log` → `AnyClinicRole`**, not `AdminOnly`. Its own doc comment already argued
   « deliberately not AdminOnly — reading the log is what a secretary fielding "je n'ai reçu aucun message"
   needs to do », so the existing intent was honoured rather than overridden.
2. **`GET /api/googlecalendar/status` → `AnyClinicRole`.** Every role's agenda calls it on mount to decide
   whether to draw the « non synchronisé » badge; gating it with its admin-only siblings would put a 403 in the
   console on every reception page load and silently switch the badge off. It returns whether the secrets are
   *present*, never their values.
3. **`PUT /api/treatment-plans/{id}/items/order` → `AdminOrDoctor`**, reversing the comment that used to call
   reordering « cosmetic ». The sequence is the treatment sequence — it is what the workspace proposes booking
   next. The old comment justified itself by matching « the unpoliced accept/complete », which are no longer
   unpoliced, so leaving it would have left a stale rationale behind too. Comment rewritten.

### I4 — the AI-summary endpoint retired (done)

Deleted: the `PatientsController` action, `GetPatientAiSummaryQuery` (211 lines), `PatientAiSummaryDto`,
`patientsApi.getAiSummary`, and the **false claim** in root `CLAUDE.md` (« on the patient detail page …
connectivity-gated » — wrong on both halves). Replaced with a paragraph naming what it actually did.
`IHuggingFaceAIService` stays: the AI **chat** is its live caller, and no method was orphaned (verified —
`ChatAsync` is the interface's only member).

Doc references stripped from `api/ClinicManagement.Application/CLAUDE.md` (4 places) and `web/lib/CLAUDE.md`.

### I5 — self-registration creates a pending account (done)

- **`User.CreateSelfRegistered(...)`** — a named sibling of `CreateLocalUser` that produces the account
  **inactive**. A separate factory rather than an `isActive: false` argument on purpose: the only other caller
  is first-run `setup`, which must stay active (a pending first admin locks the clinic out of itself before it
  has anyone to approve them — the spec's own edge case), and a defaulted boolean would put the
  security-relevant difference somewhere a caller can omit.
- **`User.IsPendingActivation`** = `!IsActive && LastLoginAt == null` — **derived, so I5 needs no column**.
  It distinguishes « never let in » from « switched off after use », which matters because `« Inactif »` on a
  five-minute-old registration reads as a bug in the registration the person just completed. See DEV-4.
- **`LoginCommand`** now returns the pending-specific French message instead of « Ce compte a été désactivé ».
- **`ClinicUsersPageDto`** (new) — a page of staff **plus a clinic-wide `PendingActivationCount`**, mirroring
  `ReceivablesPageDto`/`CaisseLedgerDto`. Counting the loaded page would report « 0 en attente » whenever the
  pending colleagues sort onto page 2 — exactly the case the number is for. Backed by
  `IUserRepository.CountPendingActivationAsync`, deliberately **not** narrowed by the search term.
- **`/users`** — a `role="status"` banner (plural-aware), an « N en attente » badge on the card title, a third
  per-row badge « En attente d'activation » in **both** the card and the table tree, « Activer le compte »
  instead of « Réactiver », and a confirm dialog that names the role the person chose and warns the account
  gives access to patient records.
- **`join-wizard.tsx`** — the local branch no longer redirects to `/login` (where correct credentials would be
  refused and read as a broken registration). It renders a « Demande envoyée » terminal state that says an
  admin must activate, with a link to the login page.

### I6 — the audit ledger (code + schema done; one item outstanding)

New:

- `Domain/Enums/AuditAction.cs` — `Insert | Update | Delete`, deliberately not business-operation names.
- `Domain/Entities/AuditEntry.cs` — `ClinicId?`, `UserId`, `UserEmail?`, `EntityType`, `EntityId`, `Action`,
  `ChangedFields?`, `OccurredAt`; caps the summary at 512 chars in the entity, not the caller.
- `Domain/Repositories/IAuditEntryRepository.cs` + `Infrastructure/Repositories/AuditEntryRepository.cs` —
  filtered, paged, `OrderByDescending(OccurredAt).ThenBy(Id)` (a single save writes several rows in one tick,
  and `OFFSET` over a non-unique sort would show a row twice and skip another).
- `Application/Common/Interfaces/IAuditActorProvider.cs` (+ the `AuditActor` record struct) and two
  implementations: `AuditActorProvider` (claims-reading, registered by `AddApplication`) and
  `ProcedureAuditActorProvider`→**`ProcessAuditActorProvider`** (no dependencies, registered by
  `AddInfrastructure` with `TryAdd` so the **console verbs** — which build from `AddInfrastructure` alone —
  can still resolve a `DbContext`). Both are resolve-once so one operation carries one actor.
- `Infrastructure/Persistence/AuditSaveChangesInterceptor.cs` — the load-bearing piece. Collects in
  `SavingChangesAsync` (a `Deleted` entry is gone from the tracker afterwards), writes in `SavedChangesAsync`
  through a **separate** `DbContext` from its own scope, so an audit failure logs at Error and cannot roll back
  the clinical/money operation — and is not a nested save on the observed context. Audits **aggregate roots
  only** (derived from `AggregateRoot<>`, not a name list), excluding `AuditEntry` itself and `Notification`
  (the minutely reminder outbox would bury a clinic's real history in machine noise within a day).
- `Infrastructure/Persistence/Configurations/AuditEntryConfiguration.cs` — **no FK to `Clinics` or `Users`**,
  deliberately: evidence must outlive its subject, and the rows that matter most are about deleted accounts.
- `Application/DTOs/AuditEntryDto.cs` (+ `AuditPageDto`, `AuditEntityTypeOptionDto`),
  `Application/Features/Audit/AuditLabels.cs` (French wording, server-side like the caisse ledger's),
  `Application/Features/Audit/Queries/GetAuditEntriesQuery.cs`,
  `API/Controllers/AuditController.cs` (`GET /api/audit`, `AdminOnly`, read-only by construction).
- **`RunAs` wired into all four Hangfire jobs** (`NotificationJob`, `EInvoiceOutboxJob`, `StockExpiryJob`,
  `PdfGenerationJob`) and into the `reset-admin-password` console verb, per the spec's « write the actor as the
  job name rather than skipping the row ».

**`verify-schema` extended (the spec's last I6 item).** Two checks under a new « Audit ledger » section, and
deliberately **only** two: `AuditEntryConfiguration` declares both indexes, so the model-driven diff already
verifies them for free (they appear as `AuditEntries(ClinicId, OccurredAt)` / `(EntityType, EntityId)` under
« Indexes ») and repeating them here would be the hand-maintained expectation list the whole verb exists to
avoid. What is left is the residue the model cannot express:

1. **`audit-ledger-clinic-nullable`** — if a migration ever made `ClinicId` `NOT NULL`, every job/CLI mutation
   with no clinic in scope would fail its insert *inside the interceptor's own swallow-and-log*, so the ledger
   would stop recording non-interactive mutations with nothing on any screen to say so.
2. **`audit-ledger-has-no-foreign-keys`** — the one assertion in the whole report that looks for something
   **absent**, and it is here because `VerifyForeignKeys` only diffs model to database: it reports a *missing* FK
   and can never see an *extra* one. A well-meaning `AuditEntries.ClinicId -> Clinics ON DELETE CASCADE` would
   erase a clinic's audit history along with the clinic, and nothing else in the codebase would notice.

Both report « not applicable » (named, not dropped) before the table exists — the same rule the stock-batch
phases follow, because a check that silently vanishes is indistinguishable from one that was forgotten.

**Run against the live database, and both pass:**

    Audit ledger
      [  ok ] audit-ledger-clinic-nullable: AuditEntries.ClinicId is nullable - an unattributable mutation can still be recorded
      [  ok ] audit-ledger-has-no-foreign-keys: AuditEntries references nothing - the ledger outlives the clinics and accounts it describes

The run's only DRIFT line is `overlapping-appointment-pairs: 2` — pre-existing dev seed data that this check is
designed to report as a fact for a human, unrelated to this feature.

**Migration: generated, applied, and verified against the real database.**
`20260803153257_AddAuditEntries` (+ `.Designer.cs` + the model snapshot). The `xmin` trap did **not** bite:
`CreateTable` lists an `xmin` column in the C#, but Npgsql omits it from the emitted SQL (confirmed with
`dotnet ef migrations script`), unlike the `AddColumn` form that forced `AddConcurrencyToken`'s empty `Up()`.
`dotnet ef database update` succeeded, and `\d "AuditEntries"` shows all nine columns plus
`IX_AuditEntries_ClinicId_OccurredAt` and `IX_AuditEntries_EntityType_EntityId`.
⚠️ `dotnet ef` **worked on this machine** this session — the `smart-app-control-blocks-tests` blocker affects
`dotnet test`, not the EF tooling.

### I3 — client-side mirror (done)

**`web/lib/nav.ts` owns the one comparison**, so the rail, the drawer and the phone's bottom bar cannot disagree:
`hidesClinicWideMoney(role)` and `isNavItemVisible(href, role)` over a `SECRETARY_HIDDEN_HREFS` set of
`/`, `/factures`, `/caisse`, `/creances`. `buildNavSections` was **widened to take the role** (the spec's word)
rather than gaining a second parameter — an `isAdmin` boolean cannot express « a secretary sees less than a
doctor », which is the distinction I1 turns on. A section whose every item is hidden is **dropped**, not rendered
empty: « Finances » with no rows under it advertises exactly the capability the gate withholds.

Three consumers updated: `dashboard-sidebar.tsx` (now passes `user?.role`), `lib/zones.ts`
(`buildNavSections("admin")` for its widest-set icon map — a display lookup that grants nothing), and
**`bottom-nav.tsx`**, which the spec did not mention and which mattered most: its first tab is « Accueil » → `/`,
the *leftmost, thumb-nearest* control on every phone screen, and for reception it had become a permanent 403. Its
module-level lookup still spans the full `baseSections` so its throw-on-renamed-href guard survives; only the
rendered set is filtered.

**`components/ui/access-denied-card.tsx`** (new) — the three finance pages' refusal. Shared rather than a fourth
copy: the three admin catalog pages had each grown their own, and six hand-written access refusals is how the
wording drifts and one of them keeps a `red-*` literal. The `backHref` is a **parameter** defaulting to
`/appointments`, not a hardcoded `/`: a secretary refused « Caisse » must not be sent to « Tableau de bord »,
which refuses them too — two dead ends in a row is how a person concludes the software is broken rather than
restricted.

⚠️ **`/factures`, `/caisse` and `/` gate with a wrapper component, not a branch inside the page.** Their bodies
open `useState`/`useEffect` and fetch on mount, so a branch would still fire every request for a secretary —
three 403s and their French error toasts *on top of* the refusal card. Not mounting the body is what makes the
refusal the only thing that happens. `/creances` is small enough to branch inline.

**DEV-3 implemented**: `/` redirects a secretary to `/appointments` with `router.replace` (so Back does not bounce
them straight back in), rendering nothing but a loading line while the session resolves or the redirect is in
flight — a flash of the dashboard shell, or of a refusal card about to be replaced, is worse than a blank moment.

## Files Changed

**New (all this feature's, safe to stage wholesale)**

    api/ClinicManagement.API/Controllers/AuditController.cs
    api/ClinicManagement.Application/Common/Interfaces/IAuditActorProvider.cs
    api/ClinicManagement.Application/Common/Services/AuditActorProvider.cs
    api/ClinicManagement.Application/Common/Services/ProcessAuditActorProvider.cs
    api/ClinicManagement.Application/DTOs/AuditEntryDto.cs
    api/ClinicManagement.Application/DTOs/ClinicUsersPageDto.cs
    api/ClinicManagement.Application/Features/Audit/AuditLabels.cs
    api/ClinicManagement.Application/Features/Audit/Queries/GetAuditEntriesQuery.cs
    api/ClinicManagement.Domain/Entities/AuditEntry.cs
    api/ClinicManagement.Domain/Enums/AuditAction.cs
    api/ClinicManagement.Domain/Repositories/IAuditEntryRepository.cs
    api/ClinicManagement.Infrastructure/Migrations/20260803153257_AddAuditEntries.cs
    api/ClinicManagement.Infrastructure/Migrations/20260803153257_AddAuditEntries.Designer.cs
    api/ClinicManagement.Infrastructure/Persistence/AuditSaveChangesInterceptor.cs
    api/ClinicManagement.Infrastructure/Persistence/Configurations/AuditEntryConfiguration.cs
    api/ClinicManagement.Infrastructure/Repositories/AuditEntryRepository.cs
    web/components/ui/access-denied-card.tsx

**Deleted (I4)**

    api/ClinicManagement.Application/DTOs/PatientAiSummaryDto.cs
    api/ClinicManagement.Application/Features/Patients/Queries/GetPatientAiSummaryQuery.cs

**Modified — this feature only**

    api/ClinicManagement.API/BackgroundJobs/{EInvoiceOutbox,Notification,PdfGeneration,StockExpiry}Job.cs
    api/ClinicManagement.API/Maintenance/AdminPasswordResetCommand.cs
    api/ClinicManagement.API/Controllers/{AI,Appointments,Auth,Billing,Clinics,CnamNomenclature,
      Connectivity,Dashboard,DentalActs,DentalRecords,Doctors,Expenses,GoogleCalendar,Invoices,
      LabOrders,Medications,Notifications,Odontogram,PatientFamilyHistory,PatientFiles,
      PatientMedicalHistory,Patients,Recall,Stock,TreatmentPlans,Trust,Users,WaitingList}Controller.cs
    api/ClinicManagement.Application/Common/Authorization/AuthorizationPolicies.cs
    api/ClinicManagement.Application/DTOs/ClinicUserDto.cs
    api/ClinicManagement.Application/Features/Auth/Commands/LoginCommand.cs
    api/ClinicManagement.Application/Features/Clinics/Commands/JoinClinicCommand.cs
    api/ClinicManagement.Application/Features/Users/Queries/ListUsersQuery.cs
    api/ClinicManagement.Domain/Repositories/IUserRepository.cs
    api/ClinicManagement.Infrastructure/Repositories/UserRepository.cs
    api/ClinicManagement.UnitTests/Api/NotificationJobTests.cs                        (compile fix only, DEV-5)
    api/ClinicManagement.UnitTests/Common/Maintenance/SchemaVerificationServiceTests.cs  (compile fix, DEV-5)
    api/ClinicManagement.UnitTests/Features/Recall/RecallDeliveryTruthTests.cs        (compile fix only, DEV-5)
    web/app/factures/page.tsx
    web/app/creances/page.tsx
    web/components/bottom-nav.tsx
    web/components/dashboard-sidebar.tsx
    web/components/join-wizard.tsx
    web/components/user-management.tsx
    web/lib/api/users.ts
    web/lib/zones.ts

**Modified — MIXED with the in-flight `audit-sections-3-to-10` work, diff by hunk**

    CLAUDE.md                                                        (the access-control bullet + I4)
    api/ClinicManagement.API/CLAUDE.md                               (policy table + the Auth column, 18 rows)
    api/ClinicManagement.API/Controllers/{MedicalDocuments,ProcedureTypes}Controller.cs
    api/ClinicManagement.Application/CLAUDE.md
    api/ClinicManagement.Application/Common/Interfaces/ISchemaVerificationReader.cs   (AuditLedgerFacts)
    api/ClinicManagement.Application/Common/Maintenance/SchemaVerificationService.cs  (VerifyAuditLedger)
    api/ClinicManagement.Application/Extensions.cs                   (IAuditActorProvider registration)
    api/ClinicManagement.Domain/CLAUDE.md
    api/ClinicManagement.Domain/Entities/User.cs                     (CreateSelfRegistered, IsPendingActivation)
    api/ClinicManagement.Infrastructure/CLAUDE.md
    api/ClinicManagement.Infrastructure/Extensions.cs                (interceptor + repository + actor floor)
    api/ClinicManagement.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs
    api/ClinicManagement.Infrastructure/Persistence/ApplicationDbContext.cs          (AuditEntries DbSet)
    api/ClinicManagement.Infrastructure/Persistence/SchemaVerificationReader.cs      (ReadAuditLedgerFactsAsync)
    web/CLAUDE.md
    web/components/CLAUDE.md
    web/lib/CLAUDE.md
    web/lib/api/patients.ts                                          (getAiSummary removed)
    web/app/page.tsx                     (the secretary redirect, on top of the dashboard-redesign work)
    web/app/caisse/page.tsx              (the role wrapper, on top of the caisse work)
    web/lib/nav.ts                       (the role gating, on top of the « Plans de traitement » rename)

## Auto-Approved Deviations

| Deviation | Reason |
|-----------|--------|
| `GET /api/clinics/reminder-log` gated `AnyClinicRole`, not `AdminOnly` like its `reminder-status` sibling | The action's existing doc comment already argues, at length, that a secretary must be able to read the delivery log. Honouring stated intent, not overriding it. |
| `GET /api/googlecalendar/status` gated `AnyClinicRole` under an `AdminOnly` class | Every role's agenda calls it on mount; gating it would 403 on every reception page load and silently hide the « non synchronisé » badge. Returns secret *presence*, never values. |
| `PUT /api/treatment-plans/{id}/items/order` gated `AdminOrDoctor` (was deliberately unpoliced) | Its own justification — « matches the unpoliced accept/complete » — stopped being true once those became `AdminOrDoctor`. The sequence is a clinical decision. Comment rewritten rather than left stale. |
| The redundant bare `[Authorize]` on `POST /api/auth/change-password` deleted rather than replaced | The class's `Authenticated` is exactly right and the bare attribute said the same thing while looking like an omission. A forced post-reset change may have no role in the JWT yet. |
| `Doctor` record still created for a self-registered *pending* doctor | Deferring it would make activation a two-step create, and the row confers no access (no login). Noted under « Known limitations », not silently. |
| French labels for the audit ledger built server-side (`AuditLabels`) | Same decision as the « extrait de caisse » — and stronger here, since the entity names are CLR type names a client cannot translate without duplicating the map. |
| `web/components/ui/access-denied-card.tsx` extracted rather than a fourth hand-written Lock card | The spec says the finance pages get « the same Lock-card treatment the catalog pages already use ». Three more copies of an access refusal is the defect shape this repo keeps finding. Internal scope; no behaviour change to the existing three. |
| `bottom-nav.tsx` made role-aware (the spec named only `nav.ts` and the three pages) | Its first tab is `/`, which I1 gated. Leaving it would have left reception a permanent 403 on the most-tapped control of the phone UI — I3's intent applied to the surface it did not enumerate. |
| `SchemaFacts` gained a 7th positional parameter | Required to carry the audit-ledger facts the spec's own I6 line asks `verify-schema` to assert. Additive; one construction site in production, one in tests (DEV-5). |

## Significant Deviations

### DEV-0 — Forced-small scope, confirmed rather than auto-escalated
- **Spec:** `Type: Small`.
- **Reality:** the traced surface is **~65 files** — I1 alone is ~28 controllers, because I2's derived
  assertion requires *every* action to carry an explicit policy, not just the 20 controllers in the matrix.
- **Chosen:** all six items in one pass (user's call, offered alongside three narrower boundaries and
  escalation to the full pipeline). **Final count: 17 new files, 2 deleted, ~60 modified.**
- **Approved:** Y

### DEV-1 — `AnyClinicRole` replaces `DoctorOrSecretary` wherever the matrix names it
- **Spec:** the matrix assigns `DoctorOrSecretary` to Appointments, WaitingList, Recall-contact, Patients
  create/read/update, `GET /api/patients/{id}/billing-summary` and `POST /api/invoices/{id}/payments`.
- **Defect in the spec:** `DoctorOrSecretary` is exactly `{doctor, secretary}` and
  `RoleAuthorizationHandler` grants **no implicit admin**. But `CreateClinicCommand.cs:184` makes the clinic's
  creator an **`admin`**, and `:352` links a `Doctor` record to that same admin for the
  « cabinet à un seul dentiste » case. So in the common Tunisian practice the **owner-dentist's role is
  `admin`** — and applying the matrix literally locks them out of their own agenda, their patient records and
  the till. That is strictly worse than the defect being fixed, and it breaks the spec's own critical edge
  case (« Reception must still be able to take money » — the owner is the other half of that counter).
- **Implemented:** a new `AuthorizationPolicies.AnyClinicRole` = `RoleRequirement("admin","doctor","secretary")`,
  used everywhere the matrix says `DoctorOrSecretary`, and also for the genuinely-everyone endpoints that carried
  only a bare `[Authorize]` (the notification bell, « Mon profil », the catalog reads the pickers use). The
  secretary exclusion the spec is *actually* about is carried entirely by `AdminOrDoctor`, which is unchanged.
- **Impact:** the final policy set is four — `Authenticated`, `AnyClinicRole`, `AdminOrDoctor`, `AdminOnly`.
- **Approved:** Y

### DEV-2 — `DoctorOnly`, `SecretaryOnly` and `DoctorOrSecretary` are deleted
- **Spec:** I2 asserts defined policies == applied policies **in both directions**.
- **Reality:** after DEV-1 none of the three has an honest use — no endpoint in the product sensibly excludes
  the admin/owner.
- **Implemented:** deleted from `AuthorizationPolicies`, and one constant **added**: `Authenticated`
  (authenticated, no role required). That is not a fig leaf — in Cloud the role reaches the JWT only after
  `app_metadata` is written, which happens *after* joining a clinic, so `user-status`, `POST /clinics` and
  `POST /clinics/join` are genuinely reached by a role-less principal and any `RoleRequirement` would break
  Cloud onboarding outright. Naming that state is what lets the coverage guard demand an explicit policy on
  every action without lying.
- **Approved:** Y

### DEV-3 — A secretary's landing route
- **Gap:** the spec gates `GET /api/dashboard` to `AdminOrDoctor` and hides « Tableau de bord » from the rail,
  but `/` is where login lands. It does not say what a secretary sees there.
- **Implemented:** `/` redirects a secretary to `/appointments` — reception's actual first screen — instead of
  rendering a Lock card on the route every morning starts at. `router.replace`, so Back does not bounce them in.
- **Approved:** Y

### DEV-4 — `AuditEntry.ClinicId` is nullable, and I5's pending state adds no column
- **Spec:** the Data/Schema section lists `AuditEntries.ClinicId` without a `?`, and states « No column changes
  to existing entities ».
- **Two decisions, same reasoning.** (a) `ClinicId` is **nullable**: a console verb or a job can mutate a row
  with no clinic derivable from it, and writing `Guid.Empty` would put a sentinel into the
  `(ClinicId, OccurredAt)` index that reads as a real clinic to every query — the same class of defect as the
  four placeholder contact literals `data-and-money-integrity` retired. (b) I5's pending state is **derived**
  (`!IsActive && LastLoginAt == null`) rather than a new `User` column, which honours the spec's own
  no-column-changes line.
- **Impact:** `GET /api/audit` filters by the caller's clinic, so an unattributed row is not on the admin's
  screen — acceptable, and the row still exists for a DB-level read. `verify-schema`'s
  `audit-ledger-clinic-nullable` check exists specifically to stop a later migration from tightening the column
  and silently killing the ledger's job/CLI rows. The derived pending flag has one blurred case (an admin
  deactivating an account before its owner's first login reads as « en attente »), which resolves in the honest
  direction: it still cannot log in, and « Activer » is still the fix.
- **Approved:** Y (implemented; flagging for review)

### DEV-5 — Three test files edited (compile fixes only)
- `NotificationJob`'s constructor gained `IAuditActorProvider` (5 call sites in `NotificationJobTests` and
  `RecallDeliveryTruthTests`), and `SchemaFacts` gained a 7th positional parameter (1 site in
  `SchemaVerificationServiceTests`). All broke at **compile** time, so `dotnet build` could not stay at 0 errors
  without touching them.
- **Implemented:** a permissive `new Mock<IAuditActorProvider>().Object` at each job site, and a defaulted
  `auditLedger` parameter on the schema test's `Arrange` helper (defaulting to « table exists, ClinicId
  nullable », so the audit checks pass and individual tests can override that one facet — the same
  one-facet-at-a-time shape as every other parameter there). **No scenario was added, changed or removed.** Per
  this skill's policy these are build-required mechanical fixes and are auto-approved.
- **Approved:** Y

## Known limitations (deliberate, not oversights)

1. **A pending self-registered *doctor* still gets a `Doctor` row**, so they appear in the practitioner picker
   before an admin approves them. No access is conferred (no login). Deferring the row would make activation a
   two-step create; out of the spec's scope.
2. **An audit row survives a rolled-back explicit transaction.** `SavedChangesAsync` fires when `SaveChanges`
   returns, which for the few handlers using `IUnitOfWork.BeginTransactionAsync` is before the commit. The
   direction of the error is deliberate — over-recording an attempt is a reading problem, under-recording a
   real change is the failure the ledger exists to prevent — and it is documented in the interceptor's own
   class comment.
3. **No audit UI.** The spec asks only for `GET /api/audit`; I3 does not mention a screen. The endpoint returns
   its own French labels and filter options, so a screen can be built later without touching the backend.
4. **The three admin catalog pages still carry their hand-written Lock cards.** `ui/access-denied-card.tsx` now
   exists and the three finance pages use it; retrofitting `/cnam-nomenclature`, `/dental-acts` and
   `/medications` is a ~10-line mechanical change per page, deliberately left out of this feature's diff. Worth
   doing next time one of them is opened.

## Quality checks

**Backend — green.** `dotnet build ClinicManagement.sln` → **0 errors**. 14 warnings, every one pre-existing and
**none in a file this feature created or changed** — verified by enumerating a clean `--no-incremental` build:
they are the repo's `CS8618` EF-private-constructor family across `Domain/Entities` and `Domain/ValueObjects`,
plus one `CS8604` in `UpdateDentalRecordCommand`. `AuditEntry.cs` does not appear (its private constructor
initialises its strings).

⚠️ **Two build gotchas met on the way, worth knowing:**

- **Do not mix `-p:BaseOutputPath=<scratch>` with a plain `dotnet ef` build.** They share `obj/`, and the
  cross-contaminated intermediate state produced three phantom `CS0103` errors about a type
  (`CnamConventionTariffs`) that was present, public and correctly imported. A plain `dotnet build` was clean.
- The console verbs must be run from **where the build actually put the exe**. After the mixed-output builds,
  `bin/Debug/net8.0/` held only satellite culture folders; `dotnet build <Host>.csproj -o <fresh dir>` is the
  reliable way to get a runnable host. `verify-schema` also needs `Auth__Mode=Local` (it refuses to run in
  Cloud) and `ASPNETCORE_ENVIRONMENT=Development` (for the connection string).

**Schema — verified against the real database**, which is stronger than the usual gate for a migration:
`docker compose up -d` then `dotnet ef database update` then `\d "AuditEntries"` shows the nine columns and both
indexes, and `verify-schema` in Local mode reports both new « Audit ledger » checks `ok`. See I6 above on the
`xmin` question, and on `dotnet ef` working fine here — the `smart-app-control-blocks-tests` blocker is
`dotnet test`, not the EF tooling.

**Frontend — green.**

| Check | Result |
|---|---|
| `npx tsc --noEmit` | **0 errors** |
| `npm run build` | **succeeded** — compiled in 9.8s, 28/28 static pages generated |
| `npm run check:responsive` | **all 11 enforced checks passed** |
| `npm run lint` | cannot run in this repo — ESLint is in the script but not in `devDependencies`, and `next.config.ts` sets `eslint.ignoreDuringBuilds`. Documented in `web/CLAUDE.md`; not a gap introduced here |

⚠️ **The eye pass at 320/390/820/1180/1440 px was NOT performed — there is no browser in this environment.** Per
`DEVICE-CONTRACT.md` that is reported rather than claimed. What was done instead: the mechanical gate above, plus
a deliberate grep of every frontend file this feature touched against § 1's traps — no `h-screen`/`min-h-screen`,
no `text-[Npx]`, no ungated `grid-cols-2+`, no unprefixed `max-w` on a `DialogContent`/`SheetContent`, no `vh` in
a sheet height, no double-opacity `bg-x/N/N`, and `min-h-11` on both controls this feature adds (the refusal
card's button and the « Demande envoyée » button). **A human should still walk the four changed surfaces** —
`/users` (the pending banner + the third badge), the « Demande envoyée » screen, a finance page's refusal card,
and a secretary's phone bottom bar (now 3 tabs + « Plus »).

**Runtime DI — verified, which a build cannot do.** The audit interceptor is wired into `AddDbContext`, so a
misresolved dependency would break *every* request rather than fail to compile. Both paths were exercised:

- **Console-verb path** — `verify-schema` builds its container from `AddInfrastructure` alone and ran to
  completion, which is direct proof the `TryAdd`'d `ProcessAuditActorProvider` floor works. Without it that verb
  could not have resolved a `DbContext` at all.
- **API path** — the host was started against the live database and logged « Database migrations applied; API
  fully ready. », i.e. `ApplicationDbContext` was constructed *with* the interceptor attached and the real
  claims-reading `AuditActorProvider` resolved from `AddApplication`.

⚠️ **The host does not currently finish booting, for a reason outside this feature.** After the migrations,
`DeferredStartupService` → `ClinicCatalogSeeder.CorrectSupersededDefaultsAsync` throws a
`TypeInitializationException` from `CnamCatalogSeed.BuildLetterValues()` (an `ArgumentNullException` on a null
`source`). Both files involved carry uncommitted parallel work — `CnamCatalogSeed.cs` is modified and
`Domain/Services/CnamConventionTariffs.cs` is brand new and untracked, both from
`audit-sections-3-to-10`/`cnam` work — and this feature touches neither. Flagged here because it will block
anyone trying to run the app locally until that seed data is finished, and because it is **not** an audit-ledger
regression: the ledger's own wiring is confirmed above, on the log line that precedes the crash.

⚠️ **A note on the frontend gate's timing.** Earlier in the session `tsc` reported 9 errors, all in
`components/document-editor-content.tsx` — 1,195 lines of uncommitted in-flight work from
`audit-sections-3-to-10` referencing symbols absent at `HEAD` too. That file was completed by the parallel work
before the final run, and the numbers above come from a clean run after it. Nothing in this feature touched it.

## Test Plan

Framework: **xUnit + Moq**, `api/ClinicManagement.UnitTests`. There is no integration/E2E project and nothing here
touches a database, so everything below is a unit test — per the suite's own guide, a migration is gated by
`verify-schema` instead, which is why I6's schema half is covered through `SchemaVerificationServiceTests` rather
than by a DB test.

| Spec item | Action | Target | Notes |
|---|---|---|---|
| **I2** (the spec's highest-value test) | **Add scenarios** | `Api/ControllerAuthorizationCoverageTests.cs` | 5 new derived guards: every action resolves to a named policy; applied ⊆ defined; **defined ⊆ applied**; both-directions equality; every defined policy registered in both modes; no bare class-level `[Authorize]` |
| **I2** (replace the "exists" assertion) | **Modify** | `Common/Authorization/AuthorizationPoliciesTests.cs` | `Named_role_policies_are_registered_in_both_modes` **deleted** — it is the assertion that stayed green while three policies rotted. Replaced by role-*content* pins (`AnyClinicRole` admits admin; `Authenticated` has **no** role requirement; `AdminOnly` is exactly one) + a `Retired_policies_are_not_reintroduced` theory |
| **I1** fallout | **Modify** | `Api/TreatmentPlansControllerAuthorizationTests.cs` | 6 actions moved from "no method policy" to `AdminOrDoctor`; the class-level assertion now demands the **named** `AnyClinicRole`, not just any `[Authorize]`; `RecordInstallmentPayment` pinned as must-never-tighten |
| **I6** interceptor | **New** | `Infrastructure/Persistence/AuditInterceptorTests.cs` | 16 tests over the real `ChangeTracker` |
| **I6** entity | **New** | `Domain/AuditEntryTests.cs` | 9 tests: invariants, the nullable clinic, summary truncation |
| **I6** read side + actor | **New** | `Features/Audit/AuditLedgerReadTests.cs` | 21 tests: clinic scoping, the clinic-local inclusive window, the tolerant action filter, labels, both actor providers |
| **I6** schema | **Add scenarios** | `Common/Maintenance/SchemaVerificationServiceTests.cs` | 6 tests for the two new « Audit ledger » checks (+ the build-required `Arrange` parameter, DEV-5) |
| **I5** registration | **Modify + add** | `Features/Clinics/JoinClinicLocalRegisterTests.cs` | `Register_Should_Create_Local_Account` **inverted** (`IsActive` true → false) and renamed; +5 scenarios incl. the spec's « first-run setup admin is never pending » edge case |
| **I5** login message | **Add scenarios** | `Features/Auth/LoginCommandHandlerTests.cs` | pending vs deactivated wording are different messages |
| **I5** pending count | **Add scenarios** | `Features/Users/ListUsersQueryHandlerTests.cs` | 4 tests: whole-clinic count, unaffected by the search term, per-row flag, paging envelope survives the wrapper DTO |

### Coverage notes — items with no unit surface (accounted for, not dropped)

- **Spec item 4 — « `TokenVersion` is bumped on a role change (assert it) ».** **Already covered**, and left alone:
  `Features/Users/ChangeUserRoleCommandHandlerTests.Handle_Bumps_TokenVersion` plus its no-op counterpart predate
  this feature. The spec asked for an assertion, not an implementation; duplicating it would add a second place to
  maintain the same claim.
- **I3 (the client-side mirror).** No frontend test framework exists in `web/` — no runner, no CI, and `npm run
  lint` cannot run (ESLint is scripted but not installed). Covered by the gate that does exist and was run:
  `npx tsc --noEmit` (0 errors), `npm run build` (28/28 pages), `npm run check:responsive` (11/11), plus the
  diff-vs-DEVICE-CONTRACT grep. Per the skill's own table, an FE-only change in a repo with no FE runner is
  recorded here rather than given a contrived test.
- **I4 (the retired AI-summary endpoint).** A deletion. Its coverage is the compile: the endpoint, query, DTO and
  client method are gone and the solution builds at 0 errors, which is the whole assertion available. There was
  never a test for the endpoint to update.
- **The `GET /api/audit` controller.** A thin MediatR pass-through; its policy is pinned by the derived guard and
  its behaviour by `AuditLedgerReadTests` over the handler. Following the repo's own precedent for thin wrappers.

## Tests Run

Run via the Smart-App-Control workaround the suite's guide documents — `dotnet build … -p:OutDir=<scratch>` then
`dotnet vstest` on the built DLL. **SAC did not block this session.**

| Suite | Filter | Result |
|-------|--------|--------|
| Unit — the 10 classes this feature writes or touches | the 10 `FullyQualifiedName~…` filters | **162 passed, 0 failed** |
| Unit — full suite (regression check) | none | 1763 passed, **37 failed** — see below |

**None of the 37 is in a class this feature wrote or touched.** All belong to the parallel in-flight
`audit-sections-3-to-10` work, in three families: free-text filters that moved into SQL so the handler mocks now
return everything (`GetMedicationsQueryHandlerTests`, `GetCnamNomenclatureQueryHandlerTests`,
`GetStockItemsQueryHandlerTests`, three `*TenantIsolationTests` list cases, `PatientContactOptionalTests`),
in-progress renderers/schedulers (`LiaisonRenderContentTests`, `ReminderScheduler`/`ReminderSchedule`,
`CreditNoteReadTests`, `GetTreatmentPlansQueryHandlerTests`), and a brand-new untracked test class of theirs
(`InvoiceDebtIsAgedTests`).

**The 7 failures this feature had caused are fixed** — 6 × `TreatmentPlansControllerAuthorizationTests`
(the I1 policy move) and `JoinClinicLocalRegisterTests.Register_Should_Create_Local_Account` (the I5 inversion).
Both were behaviour changes the spec asked for, so the tests were updated to the new contract rather than the
code to the old one.

> **Baseline movement, for honesty:** the pre-existing failure count was **51 of 1537** when this pass started and
> is **37 of 1800** now. The parallel work fixed some of its own (`CnamCatalogSeedTests`,
> `CnamBs1BulletinRendererTests`) while adding others during the session, so the number is not stable — what is
> stable is that no failing class is one of mine.

### Two things I changed that were not this feature's

1. **`Features/Billing/InvoiceDebtIsAgedTests.cs` — one `using` added.** A brand-new *untracked* file from the
   parallel work referenced `ReceivableDto` without `using ClinicManagement.Application.DTOs;`, which broke the
   **whole test project's compile** and so blocked this pass entirely. One import, no scenario touched.
   ⚠️ Side effect worth knowing: its 5 tests now *run*, and 5 of them fail. They were not failing before because
   they could not execute. That is their author's to resolve — I have not touched their assertions.
2. **The verification described below** temporarily edited two production files and restored them (verified).

### The guards were proven non-vacuous

A derived guard that cannot fail is theatre, so both new directions were checked against the **actual original
defect** rather than trusted. Two defects were injected and then reverted:

| Injected | Result |
|---|---|
| An unused `DoctorOnly` policy constant + registration (the exact shape of the three that rotted) | `Every_Defined_Policy_Is_Applied_Somewhere`, `Defined_And_Applied_Policy_Sets_Are_Equal_In_Both_Directions` and `Retired_policies_are_not_reintroduced` all **failed** |
| `ExpensesController` reverted to a bare `[Authorize]` | `Every_Action_Resolves_To_A_Named_Policy_Or_Is_Approved_Anonymous` and `No_Controller_Carries_A_Bare_Class_Level_Authorize` both **failed** |

5 red in total, then both files restored and re-verified. So the pre-I1 codebase would have failed this guard —
which is the only evidence that matters for a test whose whole purpose is catching that state.

### A note on the two reflection-based tests

`AuditInterceptorTests` invokes the private `Collect`/`FlushAsync` and reads the private `_pending`/
`ExcludedEntityTypes`. That is deliberate and bounded: the interceptor's decisions are made against EF's
`ChangeTracker`, **attaching entities opens no connection**, and driving the same logic through a real
`SaveChangesAsync` would need a live database — which this suite does not have and which its guide forbids adding.
The alternative was no coverage of the ledger's core logic at all. Same licence
`RecallQueryTranslationTests` already takes for `ToQueryString()`.

⚠️ One test was **rewritten during this pass** because the first version was circular: it re-walked the
`AggregateRoot<>` base chain itself and then asserted its own re-walk, so it would have passed whatever the
interceptor did. It is now `The_Exclusion_List_Is_Still_Only_The_Two_Documented_Types`, which pins the only
hand-maintained part; the roots-get-a-row / children-do-not behaviour is asserted for real by running the actual
collection phase over an actual change tracker.

### Test bugs found and fixed during the run (no production defect)

Both were mine, in the tests, and neither indicated anything wrong with the feature:

- `AuditEntryTests.Accepts_A_Null_Clinic` passed a `null` through a helper whose `clinicId ?? ClinicId` coalesced
  it straight back to the default — so it could not express the case under test and would have passed against a
  *non-nullable* column. Rewritten to construct the entity directly. (The production null path is separately
  proven by `An_Unattributable_Mutation_Is_Recorded_With_A_Null_Clinic`, which runs the real interceptor.)
- `AuditInterceptorTests` guessed four entity signatures wrong (`Patient.UpdateBasicInfo`, `Invoice.AddLine`, and
  the `Invoice`/`Notification` constructor shapes). ⚠️ These surfaced only after a **clean** rebuild: a concurrent
  build from the parallel session had locked `obj/`, and `vstest` happily ran the previous DLL and reported
  162 green. Worth remembering — *a passing run immediately after a failed build is a stale run.*

**No production code was changed by this pass.** Every failure it produced was either a test that needed updating
to a deliberate behaviour change (the 7) or a bug in a test I had just written (the 2).

### Two environment blockers hit while verifying — both transient, both worked around

Recorded because the next person will meet them, and because each one can masquerade as a red suite:

1. **Smart App Control flipped mid-session.** An earlier run was clean; a later rebuild of
   `ClinicManagement.Application.dll` was then blocked with `0x800711C7`, and `vstest` reported
   « No test matches the given testcase filter » rather than an error — i.e. **a SAC block looks like an empty
   filter, not like a failure**. Building to a *fresh* `OutDir` cleared it, which matches the guide's note that
   SAC's verdict is time-varying. ⚠️ Never read « no test matches » as a pass.
2. **The parallel session was editing shared code concurrently.** Twice the solution simply did not compile —
   once on locked `obj/` artifacts, once mid-edit on `IBackupService`/`PgDumpBackupService` (their L4d
   backup-ledger work). Waiting and rebuilding resolved both. ⚠️ The dangerous shape here is the *first* one:
   after a failed build, `vstest` happily ran the **previous** DLL and reported 162 green while my file had 7
   compile errors. The final numbers above come from a run whose build printed `Build succeeded.` in the same
   invocation.

**Final verification:** clean build (`Build succeeded.`, 0 errors) → **162 passed, 0 failed** on the ten targeted
classes, re-run against the current state of the tree after the parallel work settled.
