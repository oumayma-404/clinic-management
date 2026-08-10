# Story 1: [Full] Abonnement du cabinet — entitlement, enforcement, visibility and vendor control

**Status:** APPROVED
**Story Status:** in-progress — **Parts A–F complete** (all six checkpoints green; **Part G** is the only one left).
See [stories/progress.md](./progress.md) for the gate results, the eleven logged deviations, the caught defects and each
part's executed red-proofs. The eye pass is owed for C and D — no browser automation on this machine — and the
operator walk for Part F's five verbs.
**Layer:** Full (deliberate departure from the BE/FE rule — see *Notes*)
**Depends On:** None
**Blocks:** `features/platform-console/` (that feature depends on this one and must not be started before it)

## Objective

On the hosted multi-tenant deployment, a cabinet's right to **record new work** becomes an explicit dated entitlement.
A new cabinet gets **30 free days** with no card; past its end date it becomes **read-only** — the agenda, every patient
file, every allergy, every invoice, every CSV export and every PDF keep working exactly as before, and only writes are
refused, with **HTTP 402**, a machine-readable code and a French sentence naming the date and pointing at « Abonnement ».
The cabinet can always see where it stands and how to pay, is warned four times before it stops being able to work, and
the vendor can record a received payment from the console and have the cabinet working again within minutes without
anybody signing out. Every cabinet that already exists is grandfathered open-ended, and the two other deployment kinds
behave byte for byte as they do today.

## Acceptance Criteria

_From spec:_

**Part A — the entitlement exists everywhere (US-1, US-6, US-7)**
- [x] **AC-1.1** A self-signup cabinet's entitlement runs to the end of its 30th clinic-local day, creation day = day 1 (10 Aug → 8 Sep, and it may work all of 8 Sep)
- [x] **AC-1.2** No door creates a cabinet without an entitlement; trial where subscriptions are enforced, **open-ended** where they are not
- [x] **AC-1.2a** **Both** construction sites produce one — the shared provisioning helper *and* the Auth0 branch that builds its own `Clinic`
- [x] **AC-1.4** During the trial every capability is available — a trial is not a reduced product (no work; assert nothing gates on `Trial`)
- [x] **AC-1.5** Changing the configured trial length later moves no existing cabinet's end date (**EC-12**)
- [x] **AC-6.1** Every pre-existing cabinet receives an entitlement with **no end date**
- [x] **AC-6.2** Each carries one entry recording that it was grandfathered, and why
- [x] **AC-6.3** No existing cabinet sees a banner, a warning, or a refusal as a result of the deployment
- [x] **AC-6.4** Verifiable afterwards, as **named checks in `verify-schema`**: cabinets without an entitlement = 0, and grandfathered entries = the pre-deployment cabinet count
- [x] **AC-7.1 / AC-7.2** Nothing enforced, no banner, no warning, no « Abonnement » screen on `SelfHostedLan` or `CloudBrowser`
- [x] **AC-7.3** Whether enforcement applies is decided by the deployment's **kind**; no configuration setting can flip it

**Part B — read-only, not locked out (US-4)**
- [x] **AC-4.1** Every read succeeds: agenda, patient list, every tab, odontogram, invoices, devis, la caisse, dashboard, documents, files, PDFs
- [x] **AC-4.2** Every CSV export succeeds (**EC-9**)
- [x] **AC-4.3** Printing or downloading a document the cabinet already holds succeeds
- [x] **AC-4.4** Create / modify / delete is refused with a French message naming the end date and pointing at « Abonnement »
- [x] **AC-4.5** The refusal is machine-recognisable and **never signs the user out**
- [x] **AC-4.7** Signing in works; changing a password works, including an administrator-forced change (**EC-2**)
- [x] **AC-4.8** Reading « Abonnement » works — *both halves now: the gate never inspects a GET, and Part C's two endpoints are GETs carrying `[AllowsWithoutSubscription]` as documentation of that*
- [x] **AC-4.9** Compute-only requests still succeed (CNAM estimate, CSV import dry run, render-for-download); **the AI assistant does not**
- [x] **AC-4.11** Screens experienced as reading keep working where they issue a write to do so (default file folders, push-token registration, clearing the bell)
- [x] **AC-4.10** Nothing about the refusal is silent — no case where a save appears to succeed and does not
- [x] **EC-6** A cabinet with no entitlement row is refused under the **distinct** `subscription_missing` code
- [x] **EC-10** A secretary meets the French refusal — never a rights error (the gate reads no role at all, so « vous n'avez pas les droits » is unreachable here). *The « screen she is allowed to open » half landed with Part C: `/api/subscription` is `AnyClinicRole` and `/abonnement` sits outside `buildConfigItems`' admin branch and outside `SECRETARY_HIDDEN_HREFS`.*

**Part C — visibility (US-2)**
- [x] **AC-2.1** « Abonnement » shows state, end date, days remaining, plan, price, payment instructions, contact details — *the price is the cabinet's own forfait's **plus** the deployment's published tariff for all three, since a trial cabinet has chosen no forfait (progress.md DEV-3)*
- [x] **AC-2.2** Reachable by **every** role including a secretary, outside the admin-only grouping
- [x] **AC-2.3** An administrator additionally sees the payment history (date, period covered, amount, method, reference); a corrected entry struck through with its reason — *and « Annulé » in words beside the strike-through*
- [x] **AC-2.4** Price and payment instructions are per-deployment configuration, not compiled in
- [x] **AC-2.5** An entitlement with no end date says so **in words**, not as a far-future date
- [x] **EC-11** A suspended cabinet reads « Suspendu », not « Expiré » — *including when its end date is still in the future*
- [x] **EC-13** A failed read of the screen is a retryable « Réessayer », never « aucun abonnement » — *only an explicit 404 is read as absence; a network drop is `ApiError(0)` and takes the retry path*

**Part D — banner, toast, live re-read (US-3 banner half, US-4 client half)**
- [x] **AC-1.3** The signup form **and** the verification e-mail both state « N jours d'essai gratuit, sans carte bancaire » before anything is submitted — *N is served from `Subscription:TrialDays` as `trialDays` on `GET /api/auth/mode`, never a literal (progress.md DEV-6)*
- [x] **AC-3.1** From **7 days** before the end date, a banner on every screen states the state and the date, linking to « Abonnement » — *mounted in `AppShell`, which **is** the set of chrome-ful routes (DEV-5)*
- [x] **AC-3.2** While still valid the banner is dismissible and returns the next clinic day; dismissal is **per browser**, never a server write — *keyed on the server's own `endsOn`+`daysRemaining` pair, so no browser clock is consulted*
- [x] **AC-3.3** Once ended the banner is **not** dismissible — *the control is absent, not disabled*
- [x] **AC-4.6** A refused save leaves the form open with the typed input intact — *audited by four derived scans over every `catch` in `app/` and `components/`; no site needed fixing*
- [~] **AC-5.8** The cabinet's app reflects a grant with nobody signing out or restarting, within FR-15's stated delay — *both halves now exist: the client re-read (Part D) and the grant verb (Part F). The **live** walk pairing them is owed with the operator step*
- [x] **EC-1** Midnight passes mid-consultation: reads keep working, the save is refused, the fiche stays populated, the banner appears with no reload — *the refused save **is** the event: `onSubscriptionRequired` → re-read → banner. Live walk owed*

**Part E — warnings (US-3 notification half)**
- [x] **AC-3.4** A notification at **7, 3 and 1 day(s)** before, and again on the day it ends — **four distinct** notifications, each genuinely new so it badges the bell — *deduped on the real `SubscriptionThresholdDays` column, asserted as four distinct ids; the deep-link needed two `web/` edits the plan's table did not list (progress.md DEV-8)*
- [x] **AC-3.5** Re-evaluated daily; a threshold already crossed produces **no** second row (no fifth notification, however long the countdown) — *and the wording does not churn either: it is derived from the threshold, not the live countdown*
- [x] **AC-3.6** Never reaches a locked phone as a push banner — *`StaffNotificationRules.ReachesALockedPhone → false`, the single decision point the fan-out reads*
- [x] **AC-3.7** Addressed to the whole practice, not only the administrator — *no actor and no target user, which is the mechanism rather than a policy*

**Part F — the vendor unlocks (US-5)**
- [x] **AC-5.1** A command records a payment against one cabinet by id **or** administrator e-mail, with whole months (or an explicit end date), and optional plan / amount / method / reference / note — *`--clinic` accepts either form; a grant with **no** duration is refused rather than recorded as permanent cover*
- [x] **AC-5.2** The new end date is whichever is later — current end or today — plus the duration (**EC-3**: paying early never costs days) — *it falls out of the fold's exclusive cursor; nothing in Part F computes a date*
- [x] **AC-5.3** Each payment is its own entry; entries accumulate and nothing overwrites an earlier one
- [x] **AC-5.4** The end date is always derived by folding the non-cancelled entries, so correcting **any** of them corrects the date — *asserted against an independent fold, and on a **middle** entry*
- [x] **AC-5.5** A mistaken entry is **cancelled with a written reason**, never edited or deleted; it stays visible struck through and the date recomputes, possibly into the past (**EC-4**)
- [~] **AC-5.6** Every grant and cancellation records who and when, and appears in the cabinet's activity journal — *structurally: both are `AggregateRoot`s, the interceptor takes the clinic from the row itself, and each verb declares `RunAs(<verb>)`. The live `GET /api/audit` read is owed with the operator walk*
- [x] **AC-5.7** Refused for a non-positive duration and for a cabinet that does not exist, naming which — *and an unknown **id** and an unknown **e-mail** refuse in different sentences*
- [x] **AC-5.9** A read-only report lists cabinets by state and can be scheduled — *exit 0/1/2, sharing `reconcile-money`'s codes. A suspended cabinet is listed but is **not** a finding; a cabinet with no entitlement is*
- [x] **EC-5** Two simultaneous grants **both land and are both kept** — *bounded re-fold retry rather than a surfaced 409 (progress.md DEV-10)*

**Part G — background work (FR-8)**
- [ ] **EC-7** A reminder queued before expiry for an appointment after it is **parked with a stated reason**, not sent and not deleted; extending before the visit sends it
- [ ] Scheduled backups and the daily stock-expiry alert keep running on an expired cabinet
- [ ] **FR-14** Nothing is ever deleted automatically, however long a cabinet stays expired (no work — assert no retention timer is introduced)

_Story-specific:_

- [x] `SubscriptionLedger.Fold` takes **no clock** and is a pure function of the entries; the same entries fold to the same date on two different simulated days
- [x] `ClinicSubscription.RecomputeFrom` is the **only** writer of `EndsOn` — including the trial's, which is not hand-computed — *and Part F's three commands all reach it through the one `SubscriptionRefold` helper*
- [x] Nothing under `Features/Subscriptions/` names `DeploymentProfile` (Application references Domain alone)
- [x] `MoneyReadConsistencyTests` is **unchanged**, proving the vendor's revenue never reaches la caisse, l'extrait, « Créances », the dashboard's Argent section or any patient's balance (FR-2)
- [x] `TenantScopeFilterTests` passes with **no edit** to its `UnfilteredByDesign` dictionary
- [x] No HTTP path can grant, cancel or suspend — granting oneself a subscription has no web-facing route (FR-6) — *a derived guard over the commands found by reflection, with an executed red-proof*

## Entry Criteria

Before starting this story, ensure:

- [ ] `features/clinic-subscription/plan.md` is `Status: APPROVED` and `Challenged: Yes` (it is)
- [ ] `features/clinic-subscription/spec.md` is `Status: APPROVED` and `Challenged: Yes` (it is)
- [ ] Docker services are up: `docker compose ps` shows `postgres` and `minio` healthy
- [ ] The solution builds clean from a cold state: `dotnet build api/ClinicManagement.sln`
- [ ] The unit suite is green **before** any change, so a later red is attributable: `dotnet test api/ClinicManagement.UnitTests/ClinicManagement.UnitTests.csproj -p:BaseOutputPath=$env:TEMP\clinic-testrun\`
- [ ] The frontend gate is clean before any change: in `web/`, `npx tsc --noEmit` && `npm run check:responsive` && `npm run build`
- [ ] **No `next dev` is running** when building `web/` — a `web/.next` build failure is almost always a concurrent dev server, whatever the error says
- [ ] `dotnet run --project api/ClinicManagement.API -- verify-schema` runs and its output is **saved**, as the before-half of FR-9's before/after diff
- [ ] `git diff HEAD --numstat` reviewed — this branch carries in-flight work in `web/` and `desktop/`; do not let a commit swallow another author's changes

## Steps

Seven ordered parts. Each is a vertical increment with **its own commit** and its own validation checkpoint; do not
start a part before the previous one's checkpoint is green. **Parts E and G are atomic** — see *Notes*.

### Part A — Every cabinet has an entitlement, at every door and for all of history

*Covers US-1, US-6, US-7 · FR-1, FR-2, FR-4, FR-9, FR-10, FR-13*

1. **Add the 16th deployment capability**
   - `api/ClinicManagement.Infrastructure/Deployment/DeploymentProfile.cs`: `RequiresSubscription`, **`HostedMultiTenant` only**
   - Extend the truth-table test; `DeploymentProfileCoverageTests` picks the new property up by reflection for free

2. **Create the domain: two aggregate roots, five enums, the fold, the repository**
   - `ClinicSubscription` and `SubscriptionPeriod` are both **`AggregateRoot<Guid>`** — a non-root gets **no audit row**, which would make FR-12 silently false
   - `SubscriptionPeriod` guards that **exactly one** duration form is set: `DurationMonths`, `DurationDays`, `ExplicitEndsOn`, or open-ended
   - `SubscriptionLedger.FoldWithSpans` folds over an **exclusive cursor** (« the first day not yet covered »); `Fold` is a one-line call onto it. **No clock parameter.** See plan decision 3 for the pseudocode and why a single `anchor + duration` is wrong in one of its two branches
   - Trial = `DurationDays = 30`; its `EndsOn` comes from **`RecomputeFrom([trialEntry])`**, never a hand-written `AddDays(trialDays - 1)`

3. **Add the EF configurations, the two `DbSet`s and the two `HasQueryFilter` lines**
   - Money columns carry **no** `HasColumnType`/`HasPrecision` — `ConfigureConventions` already applies `(18,3)` model-wide and an explicit annotation would be reported as drift
   - `TenantScopeFilterTests` **requires** the filters the moment `ClinicId` exists: omitting one is a hard failure, adding it needs no test edit

4. **Write `SubscriptionProvisioning.CreateForNewClinic` and call it from both construction doors**
   - Staged into each door's existing single `SaveChangesAsync` (FR-4's « one indivisible operation »)
   - ⚠️ **Two construction doors, three callers of `LocalClinicProvisioning.ProvisionAsync`** — it is a `static` helper taking its repositories as parameters, so the signature change breaks all three: `CreateClinicCommand`'s Local branch, the **`provision-clinic`** verb (container = `AddInfrastructure` **only**; already declares `UseClinic(id)`), and **`VerifyClinicSignUpCommand`** (public self-signup, the door that will create most trials)
   - Door 2 of 2 is `CreateClinicCommand`'s **Auth0/Cloud** branch, which builds its own `Clinic` and always yields an **open-ended** entitlement — which is exactly why it is easy to forget (AC-1.2a)

5. **Add the two Infrastructure seams and the config section**
   - `ISubscriptionPolicy` (`RequiresSubscription` from the **kind alone**, `TrialDays` from config) and `ISubscriptionPricing`
   - Both registered by **`AddInfrastructure`**, not `AddApplication` — the console verbs build their container from that method alone
   - ⚠️ Nothing in `Features/Subscriptions/` may name `DeploymentProfile`

6. **Hand-write the migration**
   - Two tables, `StaffNotification.SubscriptionThresholdDays`, `Notification.BlockedReason`, `PushDelivery.BlockedReason`, and the grandfathering backfill (one open-ended entitlement + one `Grandfathered` entry per existing clinic, reason recorded)
   - Hand-edit `.Designer.cs` **and** `ApplicationDbContextModelSnapshot.cs`; copy `20260807102000_AddClinicSignups` as the shape. `dotnet ef` cannot scaffold on this machine (see **R-2**)
   - Inserted **only where no entitlement exists**, so `Up()` is re-runnable

7. **Add the three `verify-schema` checks**
   - `every-clinic-has-an-entitlement` → must read **0** (a derived count over every cabinet, never a list of known doors — FR-13)
   - `subscription-end-date-matches-ledger` → clinics whose stored `EndsOn` differs from the fold must be **0**
   - `subscription-grandfathered-entries` → reported as **Info** with its count; AC-6.4's equality is established by FR-9's before/after diff, not by a figure the command can know once new cabinets arrive
   - Guard each on `requiredTable`/`requiredColumn` so a pre-migration run reports *not applicable* rather than a misleading `0`

8. **Tests**
   - `SubscriptionLedgerTests` (incl. the trial-only fold = `creationDay.AddDays(29)`, the lapsed-grant case, and clock-freedom), `SubscriptionStateReaderTests`, `SubscriptionProvisioningTests` (incl. that no config key can flip `RequiresSubscription`), `ClinicCreationEntitlementTests` (source scan scoped to Application + API, expecting exactly **two** production sites), `SubscriptionTenantIsolationTests`

**Checkpoint A** — see *Verification Steps → Part A*. Commit.

### Part B — An expired cabinet keeps its records and loses only recording

*Covers US-4 · FR-3, FR-11*

1. **Create `AllowsWithoutSubscriptionAttribute`** (mandatory `Reason`) and `SubscriptionRefusals` (the three French sentences + their codes in one place)
2. **Create `SubscriptionGateMiddleware`**, registered **after `LocalAuthEnforcementMiddleware`** — last before `MapControllers`, **not** immediately after `TenantScopeMiddleware`. Predicate order and the ordering rationale are in plan Part B step 2; the short version is that a 402 must never mask a **401** (revoked token) or a **403 `must_change_password`**
3. **Apply the attribute to FR-3's fixed set** — the table is in plan Part B step 3, each row with its reason. ⚠️ The **AI chat is not on it** (its action set books and cancels appointments); the **Google OAuth callback is not exempted** (the request that *starts* the flow is refused, so the callback is unreachable)
4. **Tests** — `SubscriptionGateMiddlewareTests` over `DefaultHttpContext` + fabricated endpoint metadata, and the derived `SubscriptionExemptionCoverageTests` including the two named facts

**Checkpoint B** — see *Verification Steps → Part B*. Commit.

### Part C — The cabinet can see where it stands and how to pay

*Covers US-2 · FR-10, FR-15 (read half)*

1. **`GetSubscriptionQuery` + `GetSubscriptionHistoryQuery`, the two DTOs, `SubscriptionController`**
   - `GET /api/subscription` is `AnyClinicRole` (AC-2.2's deliberate exception); `GET /api/subscription/history` is `AdminOnly`
   - 404 when `!RequiresSubscription`, checked in the controller **before** the mediator (`AuthController`'s `AllowsPublicClinicSignup` precedent)
   - ⚠️ The history read folds the **whole** ledger through `FoldWithSpans` and pages with **`PagedResult.FromSource`**; SQL paging cannot produce `fromDay`/`throughDay`
2. **`requiresSubscription` on `GET /api/auth/mode`**; `AuthModeDto` on the client, read strictly `=== true`
3. **`web/lib/api/subscription.ts`, `web/app/abonnement/page.tsx`, `subscription-history-table.tsx`**
   - For a non-admin, render **`<AccessDeniedCard description=… />` in place of** the history section, so its fetch never fires
4. **Nav** — `/abonnement` unconditional in `buildConfigItems`; a `ROUTE_ZONES` row (or `PageHeader`'s eyebrow says « Quotidien »); **not** added to `SECRETARY_HIDDEN_HREFS`
5. **`Subscriptions` → `RealtimeResourceResolver.ExcludedAreas`**, with a comment citing FR-15

**Checkpoint C** — see *Verification Steps → Part C*. Commit.

### Part D — The banner, the refusal toast, and the live re-read — ✅ DONE (Checkpoint D green)

*Covers US-3 (banner half), US-4 (client half), US-5 (AC-5.8) · FR-15*

1. **`client.ts`** — the three codes, the `402` French fallback, `onSubscriptionRequired` fired in the same block as `onClientTooOld`. ⚠️ **Do not touch `handleRequest`'s one-shot 401 retry** (AC-4.5: the refusal never signs the user out)
2. **`SubscriptionProvider` in `app/layout.tsx`**, owning FR-15's three triggers: an interval **only while a warning or expiry is in force**, a `window` focus listener (`web/` has none today — this is new), and an immediate re-read on any 402. Bounded per client, not per cabinet
3. **`SubscriptionBanner`** — one line wrapping to at most two, ≤ ~15 % of a 380 px-tall landscape viewport, dismissible **only while valid**, dismissal keyed on the clinic day so it returns the next day with no server write
4. **Confirm every refused save leaves its form open with input intact** (AC-4.6) — the dialogs already use `showErrorToast` and stay open; verify rather than assume, and fix any site that closes on error
5. **`HIDDEN_PATHS` += `/signup`, `/signup/verifier`**; AC-1.3's trial sentence in `setup-wizard.tsx` **and** in `SignUpClinicCommand`'s verification e-mail body
6. *(added during implementation)* **Close Part C's interim « Abonnement » rail row** — `buildConfigItems` takes
   `showSubscription`, fed from the new provider, so `SelfHostedLan` and `CloudBrowser` show no row (AC-7.1/7.2).
   Part C recorded this as deferred **to Part D** and Part D's own file table did not mention it (progress.md DEV-7)

**Checkpoint D** — see *Verification Steps → Part D*. Commit. ✅ **Green** — 8 new tests, one executed red-proof;
the eye pass is owed (no browser automation on this machine).

### Part E — The cabinet is warned before it stops being able to work — ⚠️ ATOMIC

*Covers US-3 (notification half) · FR-5*

1. **One commit, all four:** `NotificationCategory.SubscriptionExpiring = 10`, `NotificationTargetKind.Subscription = 5`, `StaffNotification.SubscriptionThresholdDays` + its `ForSubscription(...)` path, and `StaffNotificationRules.ReachesALockedPhone → false`. ⚠️ The switch **throws** on an unclassified category, so omitting the last one breaks **every** notification write in the product (**R-9**)
2. **`EnsureSubscriptionWarningAsync` / `ClearSubscriptionWarningsAsync`** on `INotificationGenerator`, both inside `SafelyAsync`, deduped on **(clinic, threshold)** — a genuinely new unread row per threshold, never a restatement. Plus the two repository siblings of `GetBackupStaleAsync`
3. **`SubscriptionWarningJob`** — daily, guarded on `RequiresSubscription`, `UseSystemWide` + `RunAs`, one bounded pass, try/catch per clinic. An extension past the window clears outstanding warnings and **re-arms** the thresholds
4. **`SubscriptionWarningTests`**

5. *(added during implementation)* **The client half of AC-3.4's deep-link** — `dashboard-header.tsx`'s
   `targetKind === "Subscription"` → `/abonnement` branch plus the panel's icon and tone entries. The plan calls
   Part E `api/`-only; both maps are loose `Record<string, …>` with a fallback and the click handler is an if/else
   over known kinds, so backend-only ships four rows that badge the bell and go nowhere (progress.md DEV-8)

**Checkpoint E** — see *Verification Steps → Part E*. Commit (one commit for the whole part). ✅ **Green** — 22 new
tests, three executed red-proofs; the operator's simulated-days walk on a real schedule is owed.

### Part F — The vendor unlocks a cabinet that has paid — ✅ DONE (Checkpoint F green)

*Covers US-5 · FR-6, FR-7, FR-12*

1. **The three commands, `SubscriptionReportService`, and the five verb wrappers** with their `Program.cs` branches. Gate each on **`MaintenanceDatabase.HasConnectionString`** — *not* a profile capability (amendment M3: the hosted deployment has no local DB tooling, and these verbs must work there above all)
2. **Each verb:** `AddInfrastructure` only, `CreateScope`, `UseSystemWide(reason)` (or `UseClinic` for a single cabinet) and `IAuditActorProvider.RunAs(CommandName)`, so FR-12's journal attributes the grant to the command, distinguishably from any clinic user. `SystemWideCallerCoverageTests` enforces this by reflection
3. **Every grant / cancel / suspend re-folds through `ClinicSubscription.RecomputeFrom`** — the one write path to `EndsOn`
4. **`SubscriptionReportCommand` exits 2** when it finds expiring/expired cabinets, 0 clean, 1 unable to run
5. **Tests** — `GrantSubscriptionPeriodCommandHandlerTests`, `CancelSubscriptionPeriodCommandHandlerTests`

**Checkpoint F** — see *Verification Steps → Part F*. Commit. ✅ **Green** — 53 new tests, two executed red-proofs;
the operator's five-verb walk is owed. It also carries `SubscriptionCabinetLookup` and `SubscriptionRefold` (shared
by the three commands) and a fix to `SystemWideCallerCoverageTests`, whose console-verb branch had never matched a
single type (progress.md DEV-10, DEV-11).

### Part G — Background work parks rather than sends or vanishes — ⚠️ ATOMIC

*Covers FR-8 · EC-7*

1. **`OutboxBlockReason` on `Notification` and `PushDelivery`** — the existing three French sentences keep their wording and gain their matching enum value
2. **`NotificationJob.DispatchAsync` and `PushDispatchJob`:** park before calling a sender when the clinic may not write
3. **Both `ReviewBlockedRowsAsync` bodies:** a `SubscriptionExpired` row is released **only when the clinic may write again**. ⚠️ Shipping the parking without this releases every parked reminder within a minute (**R-8**, FR-8's named gap) — this is why the part is atomic
4. **Confirm the scheduled backup and the daily stock-expiry alert are untouched**, and that the manual backup is on Part B's exempt list

**Checkpoint G** — see *Verification Steps → Part G*. Commit.

## Files to Create/Modify

Grouped **by part**, because that is the unit of work. The plan's own tables group the same files by layer.

### Part A

| File | Create/Modify | Purpose |
|------|---|---------|
| `Domain/Entities/ClinicSubscription.cs` | create | `AggregateRoot<Guid>`; `ClinicId` (unique), `Plan`, `EndsOn`, `IsSuspended`, `SuspensionReason`, `SuspendedAtUtc`, `SuspendedBy`; `RecomputeFrom(entries)` |
| `Domain/Entities/SubscriptionPeriod.cs` | create | `AggregateRoot<Guid>` ledger entry; one duration form, optional money fields, `RecordedOnClinicDay`, `Cancel(reason, by, whenUtc)` |
| `Domain/Services/SubscriptionLedger.cs` | create | `FoldWithSpans` (exclusive cursor, no clock) + `Fold` + `PeriodSpan` |
| `Domain/Enums/SubscriptionPeriodKind.cs` | create | `Trial=1, Paid=2, Grandfathered=3, Complimentary=4` |
| `Domain/Enums/SubscriptionPaymentMethod.cs` | create | `Transfer=1, Cash=2, Cheque=3, Card=4` — deliberately **not** the clinic's `PaymentMethod` |
| `Domain/Enums/SubscriptionState.cs` | create | `Trial, Active, Expired, Suspended` — derived, never stored |
| `Domain/Enums/SubscriptionPlan.cs` | create | `Cabinet=1, Clinique=2, SurMesure=3` — a label and a price; gates nothing |
| `Domain/Enums/OutboxBlockReason.cs` | create | `ChannelUnsupported=1 … SubscriptionExpired=4` (consumed in Part G) |
| `Domain/Repositories/IClinicSubscriptionRepository.cs` | create | `GetByClinicAsync`, `GetEntriesAsync`, `AddAsync`, `AddEntryAsync`, `UpdateAsync`, `GetClinicsWithoutSubscriptionAsync`, `GetForReportAsync` |
| `Application/Features/Subscriptions/SubscriptionProvisioning.cs` | create | `CreateForNewClinic(clinicId, requiresSubscription, clinicToday, trialDays)` — primitives only |
| `Application/Features/Subscriptions/SubscriptionStateReader.cs` | create | The one FR-1 rule: `(subscription, clinicToday) → (State, AllowsWrites, ShouldWarn, DaysRemaining?)` |
| `Application/Common/Interfaces/ISubscriptionPolicy.cs` | create | `RequiresSubscription` (kind only — AC-7.3) + `TrialDays` |
| `Application/Common/Interfaces/ISubscriptionPricing.cs` | create | Per-deployment prices, payment instructions, contact details |
| `Infrastructure/Services/SubscriptionPolicy.cs` | create | Impl over `DeploymentProfile`; **no config path** for `RequiresSubscription` |
| `Infrastructure/Services/SubscriptionPricing.cs` | create | Impl over `IConfiguration`, one accessor per section |
| `Infrastructure/Persistence/Configurations/ClinicSubscriptionConfiguration.cs` | create | Unique index on `ClinicId`; `EndsOn` as `date`; no money annotations |
| `Infrastructure/Persistence/Configurations/SubscriptionPeriodConfiguration.cs` | create | Index `(ClinicId, RecordedAtUtc)`; FK to `Clinics`; length caps |
| `Infrastructure/Repositories/ClinicSubscriptionRepository.cs` | create | Guarded `UpdateAsync`; ordered `RecordedAtUtc` then `.ThenBy(x => x.Id)` |
| `Infrastructure/Migrations/<ts>_AddClinicSubscriptions.cs` + `.Designer.cs` + snapshot | create/modify | Hand-written: two tables, three columns, the grandfathering backfill |
| `Infrastructure/Deployment/DeploymentProfile.cs` | modify | The 16th capability, `HostedMultiTenant` only |
| `Infrastructure/Persistence/ApplicationDbContext.cs` | modify | Two `DbSet`s + two `HasQueryFilter` lines |
| `Application/Features/Clinics/LocalClinicProvisioning.cs` | modify | Stage the entitlement into the same save — **signature change, three callers** |
| `Application/Features/Clinics/Commands/CreateClinicCommand.cs` | modify | The Auth0/Cloud branch — door 2 of 2 |
| `Application/Features/Auth/Commands/VerifyClinicSignUpCommand.cs` | modify | Caller of the changed helper signature |
| `API/Maintenance/ProvisionClinicCommand.cs` | modify | Caller of the changed helper signature |
| `Application/Common/Maintenance/SchemaVerificationService.cs` | modify | The three named checks |
| `Application/Common/Maintenance/ISchemaVerificationReader.cs` | modify | `DataMigrationCounts` gains three fields |
| `Infrastructure/Persistence/SchemaVerificationReader.cs` | modify | Three guarded `ScalarOrNullAsync` queries |
| `API/appsettings.json` | modify | A `Subscription` section — no secret-bearing key |
| `UnitTests/Domain/SubscriptionLedgerTests.cs` | create | The fold, incl. clock-freedom and the two arithmetic cases |
| `UnitTests/Features/Subscriptions/SubscriptionStateReaderTests.cs` | create | Thresholds, `daysRemaining == 0`, `Suspendu` beating `Expiré` |
| `UnitTests/Features/Subscriptions/SubscriptionProvisioningTests.cs` | create | Trial vs open-ended per kind; AC-1.5; AC-7.3 |
| `UnitTests/Common/ClinicCreationEntitlementTests.cs` | create | Derived source scan, scoped, expecting two sites |
| `UnitTests/Features/Subscriptions/SubscriptionTenantIsolationTests.cs` | create | The per-handler layer |

### Part B

| File | Create/Modify | Purpose |
|------|---|---------|
| `Application/Common/Authorization/AllowsWithoutSubscriptionAttribute.cs` | create | `Method\|Class`, mandatory `Reason`; beside `AuthorizationPolicies` |
| `Application/Features/Subscriptions/SubscriptionRefusals.cs` | create | The three French sentences + `subscription_required` / `_suspended` / `_missing` |
| `API/Middleware/SubscriptionGateMiddleware.cs` | create | The gate; body via `WriteAsJsonAsync(new { error, code })` |
| `API/Program.cs` | modify | Register the gate **after** `LocalAuthEnforcementMiddleware` |
| ~14 controller actions | modify | `[AllowsWithoutSubscription("<FR-3 reason>")]` per plan Part B step 3 |
| `UnitTests/Api/SubscriptionGateMiddlewareTests.cs` | create | Every status/scope combination; a GET is never inspected |
| `UnitTests/Api/SubscriptionExemptionCoverageTests.cs` | create | Derived; AI chat **not** exempt, the three compute-only POSTs exempt |
| `UnitTests/Api/ControllerAuthorizationCoverageTests.cs` | verify | Expect **no edit** — confirm rather than assume |

### Part C

| File | Create/Modify | Purpose |
|------|---|---------|
| `Application/Features/Subscriptions/Queries/GetSubscriptionQuery.cs` | create | `GET /api/subscription` |
| `Application/Features/Subscriptions/Queries/GetSubscriptionHistoryQuery.cs` | create | `GET /api/subscription/history`; whole-ledger fold + `PagedResult.FromSource` |
| `Application/Common/Models/SubscriptionDto.cs`, `SubscriptionPeriodDto.cs` | create | The spec's two wire shapes, verbatim |
| `API/Controllers/SubscriptionController.cs` | create | `AnyClinicRole` + `AdminOnly`; 404 before the mediator |
| `API/Controllers/AuthController.cs` | modify | `GetMode()` gains `requiresSubscription` |
| `Application/Common/Behaviors/RealtimeResourceResolver.cs` | modify | `ExcludedAreas += "Subscriptions"` |
| `web/lib/api/subscription.ts` | create | `get()`, `history(page, pageSize)`, DTO types |
| `web/app/abonnement/page.tsx` | create | State → price → instructions → contact; `AccessDeniedCard` in place of history for non-admins |
| `web/components/subscription/subscription-history-table.tsx` | create | `CardList` below `md:` / `<Table>` above; « Annulé » in words |
| `web/lib/api/auth.ts` | modify | `requiresSubscription?: boolean`, read `=== true` |
| `web/lib/nav.ts` | modify | `/abonnement` unconditional in `buildConfigItems` |
| `web/lib/zones.ts` | modify | `ROUTE_ZONES += ["/abonnement", "config"]` |

### Part D

| File | Create/Modify | Purpose |
|------|---|---------|
| `web/lib/subscription/subscription-context.tsx` | create | `SubscriptionProvider` + `useSubscription()`; FR-15's three triggers; per-day dismissal |
| `web/components/subscription/subscription-banner.tsx` | create | `role="status"`, never a modal, no dismiss once expired, text + icon never colour alone |
| `web/lib/api/client.ts` | modify | Three codes, a `402` row, `onSubscriptionRequired` — **not** the 401 retry |
| `web/lib/errors.ts` | modify | `isPaymentRequiredError(err)` |
| `web/app/layout.tsx` | modify | `<SubscriptionProvider>` inside `<SessionProvider>`; `<SubscriptionBanner/>` |
| `web/lib/nav.ts` | modify | `HIDDEN_PATHS += /signup, /signup/verifier` |
| `web/components/setup-wizard.tsx` | modify | AC-1.3's trial sentence, before anything is submitted |
| `Application/Features/Auth/Commands/SignUpClinicCommand.cs` | modify | AC-1.3's sentence in the verification e-mail body |

### Part E

| File | Create/Modify | Purpose |
|------|---|---------|
| `API/BackgroundJobs/SubscriptionWarningJob.cs` | create | Daily, on `StockExpiryJob`'s template |
| `Domain/Enums/NotificationCategory.cs` | modify | `SubscriptionExpiring = 10` |
| `Domain/Enums/NotificationTargetKind.cs` | modify | `Subscription = 5` (no id) |
| `Domain/Entities/StaffNotification.cs` | modify | `int? SubscriptionThresholdDays` + `ForSubscription(...)` |
| `Application/Common/Services/StaffNotificationRules.cs` | modify | `ReachesALockedPhone → false` — **same commit**, the switch throws |
| `Application/Common/Interfaces/INotificationGenerator.cs` + `Services/NotificationGenerator.cs` | modify | `Ensure…` / `Clear…`, inside `SafelyAsync` |
| `Domain/Repositories/IStaffNotificationRepository.cs` + impl | modify | The two `GetBackupStaleAsync` siblings |
| `API/Program.cs` | modify | `RecurringJob.AddOrUpdate` guarded on `RequiresSubscription`, `RemoveIfExists` in the else |
| `UnitTests/Features/Subscriptions/SubscriptionWarningTests.cs` | create | Four rows, idempotence, re-arm |
| `web/components/dashboard-header.tsx` | modify | *(added during implementation — DEV-8)* `targetKind === "Subscription"` → `/abonnement`, or AC-3.4's deep-link is not true on this client |
| `web/components/notification-panel.tsx` | modify | *(added during implementation — DEV-8)* one `CATEGORY_ICON` entry (`CreditCard`) + one `CATEGORY_TONE` entry (amber). Both maps fall back silently, so `tsc` cannot see the omission |

### Part F

| File | Create/Modify | Purpose |
|------|---|---------|
| `Application/Features/Subscriptions/Commands/GrantSubscriptionPeriodCommand.cs` | create | Records an entry, re-folds, saves |
| `Application/Features/Subscriptions/Commands/CancelSubscriptionPeriodCommand.cs` | create | Mandatory reason; re-folds, possibly into the past |
| `Application/Features/Subscriptions/Commands/SetSubscriptionSuspensionCommand.cs` | create | Suspend / unsuspend with a mandatory reason |
| `Application/Common/Maintenance/SubscriptionReportService.cs` | create | The report's core; **not** DI-registered |
| `API/Maintenance/SubscriptionGrantCommand.cs` … `SubscriptionReportCommand.cs` | create | The five verb wrappers |
| `API/Program.cs` | modify | Five verb branches, gated on `MaintenanceDatabase.HasConnectionString` |
| `UnitTests/Features/Subscriptions/GrantSubscriptionPeriodCommandHandlerTests.cs` | create | AC-5.1/5.3/5.7, EC-5 |
| `UnitTests/Features/Subscriptions/CancelSubscriptionPeriodCommandHandlerTests.cs` | create | AC-5.5, mandatory reason, nothing deleted |

### Part G

| File | Create/Modify | Purpose |
|------|---|---------|
| `Domain/Entities/Notification.cs` | modify | `OutboxBlockReason? BlockedReason`; `MarkAsBlocked(reason, sentence)`; `Unblock()` clears both |
| `Domain/Entities/PushDelivery.cs` | modify | The identical pair, beside the existing `FailureReason` |
| `API/BackgroundJobs/NotificationJob.cs` | modify | Park before dispatch; **release only when the clinic may write again** |
| `API/BackgroundJobs/PushDispatchJob.cs` | modify | The identical pair |

## Verification Steps

**Verification commands:**
```bash
# Backend build + the only automated backend gate (nothing touches a database).
# Build OUTSIDE the repo: Smart App Control intermittently refuses freshly-built in-repo test assemblies.
dotnet build api/ClinicManagement.sln
dotnet test api/ClinicManagement.UnitTests/ClinicManagement.UnitTests.csproj \
  -p:BaseOutputPath=$env:TEMP\clinic-testrun\

# Targeted runs while iterating
dotnet test api/ClinicManagement.UnitTests/ClinicManagement.UnitTests.csproj \
  -p:BaseOutputPath=$env:TEMP\clinic-testrun\ --filter "FullyQualifiedName~Subscription"

# The ONLY gate a schema change has anywhere in this product — run before AND after, and diff
dotnet run --project api/ClinicManagement.API -- verify-schema

# Frontend gate (no test runner, no working ESLint, no CI in web/) — stop any `next dev` first
cd web && npx tsc --noEmit && npm run check:responsive && npm run build
```

### Part A
- [ ] `verify-schema` reports `every-clinic-has-an-entitlement: 0` and a grandfathered count equal to the clinic count
- [ ] Diffing the saved before-run against the after-run shows the two tables, three columns, indexes and FKs and nothing else unexpected
- [ ] A new signup on a `HostedMultiTenant` config lands a `Trial` entry ending on day 30 counting the creation day as day 1
- [ ] The same code on `SelfHostedLan` and `CloudBrowser` lands an **open-ended** entitlement
- [ ] `ClinicCreationEntitlementTests` fails red when the `CreateClinicCommand` call is removed, then passes
- [ ] `TenantScopeFilterTests` passes with **no edit** to its `UnfilteredByDesign` dictionary
- [ ] `MoneyReadConsistencyTests` unchanged and green
- [ ] Unit suite green

### Part B
- [ ] With a simulated expired entitlement: every read, every CSV export and every PDF download succeeds
- [ ] `POST /api/appointments` returns 402 with `code: "subscription_required"` and a French sentence naming the date
- [ ] `POST /api/ai/chat` returns 402; the three compute-only POSTs return 200
- [ ] Sign-in and a forced password change both succeed on an expired cabinet (EC-2)
- [ ] On an expired cabinet a **revoked** token still gets **401**, and a user owing a password change still gets **403 `must_change_password`** on a non-exempt write — not 402
- [ ] `SubscriptionExemptionCoverageTests` fails red when the attribute is removed from any approved **non-GET** endpoint
- [ ] `ControllerAuthorizationCoverageTests` still green with no edit

### Part C
- [ ] A secretary can open « Abonnement » and read the state, date, price and payment instructions (AC-2.2, EC-10)
- [ ] A secretary cannot see the payment history; the non-admin path renders `AccessDeniedCard` with **no 403 toast storm**
- [ ] An open-ended entitlement says so **in words**, not as a far-future date (AC-2.5)
- [ ] A suspended cabinet reads « Suspendu », not « Expiré » (EC-11)
- [ ] A dropped network on that screen yields a retryable « Réessayer », never « aucun abonnement » (EC-13)
- [ ] Page 2 of a long history shows periods that continue page 1's rather than restarting them
- [ ] `RealtimeResourceResolverTests` green with `Subscriptions` excluded and **no** frontend key added
- [ ] Frontend gate clean; eye pass at 320 / 390 / 820 / 1180 / 1440 px

### Part D
- [ ] A grant recorded by a console verb reaches the browser within one interval, with no sign-out and no reload (AC-5.8)
- [ ] A refused save raises a French toast, leaves the form populated, and the banner appears without a reload (EC-1)
- [ ] The expired banner has no dismiss control and is not a modal; « Expiré » is legible in greyscale
- [ ] Dismissing while valid hides it for the rest of the clinic day and it returns the next day (AC-3.2)
- [ ] Banner absent entirely when `requiresSubscription` is not `true`, and on `/login` and `/signup`
- [ ] Frontend gate clean; eye pass including a **380 px-tall landscape** viewport for the ≤15 % budget

### Part E
- [ ] Simulating days −8 → 0 produces exactly **four** rows, each unread and each badging the bell
- [ ] Running the job twice on the same day adds nothing (AC-3.5)
- [ ] **No push is queued** for the category (AC-3.6)
- [ ] Extending past 7 days clears the rows; approaching again later warns again (FR-5)
- [ ] Every role receives the warning (AC-3.7)
- [ ] Notification writes in **other** categories still work — proof the `StaffNotificationRules` half landed (R-9)

### Part F
- [ ] `subscription-grant --clinic <admin-email> --months 12` on a cabinet expiring in 10 days lands on the old end date + 12 months, not today + 12 (EC-3)
- [ ] `subscription-cancel` on a **middle** entry moves the end date (AC-5.4) and may push it into the past (EC-4)
- [ ] Two grants both land and are both kept (EC-5)
- [ ] Non-positive duration and unknown cabinet each refuse with a message naming which (AC-5.7)
- [ ] Every grant, cancellation and suspension appears in `GET /api/audit` **for that cabinet** (AC-5.6, FR-12)
- [ ] `subscription-report` exits **2** with cabinets found, **0** clean, **1** unable to run
- [ ] No HTTP path can grant — grepping for a controller reference to the three commands returns nothing
- [ ] `SystemWideCallerCoverageTests` green, having auto-enrolled the new job and the five verbs

### Part G
- [ ] A reminder queued before expiry for an appointment after it is parked with the machine-readable reason and is **not** sent (EC-7)
- [ ] With the channel **fully configured and enabled**, the review pass leaves that row parked
- [ ] Extending the cabinet releases it and it dispatches on the next tick
- [ ] `GET /api/outbox` shows the parked **reminder** rows in its `Blocked` depth (it has no push section — check parked `PushDelivery` rows in the table)
- [ ] Scheduled backups and the daily stock-expiry alert still run on an expired cabinet

## Exit Criteria

This story is complete when:

- [ ] All seven checkpoints above pass, each on its own commit
- [ ] A hosted cabinet created today can work for 30 days, is warned four times, is refused only on writes after its date, and is working again within one re-read interval of a `subscription-grant`
- [ ] An expired cabinet can read and export **everything** — the EC-9 walk over all nine CSV lists completes
- [ ] `SelfHostedLan` and `CloudBrowser` are unchanged in observable behaviour: no banner, no warning, no « Abonnement » screen, no refusal (AC-7.1/7.2)
- [ ] `dotnet build api/ClinicManagement.sln` clean; unit suite green with **0 errors, 0 warnings**
- [ ] `verify-schema` clean, with the before/after diff attached to the deployment record (FR-9)
- [ ] Frontend gate clean: `npx tsc --noEmit`, `npm run check:responsive`, `npm run build`, plus the eye pass at 320/390/820/1180/1440 px and the 380 px-tall landscape viewport
- [ ] The four derived guards that should have needed no edit needed none: `TenantScopeFilterTests`, `DeploymentProfileCoverageTests`, `ControllerAuthorizationCoverageTests`, `MoneyReadConsistencyTests`
- [ ] Operator verification done (not CI-runnable): the five console verbs, the daily job's four thresholds over simulated days, the reminder-parking round trip
- [ ] The nearest `CLAUDE.md` files updated so the repo map stays accurate
- [ ] Code reviewed and approved

## Notes

**Why `Layer: Full`.** This skill's default is one layer per story. The override is deliberate: `plan.md` records
« one story instead of several, by explicit user decision », structured into seven ordered parts, logged as **R-1**
with stated split points. The steps are grouped by part so the internal ordering stays explicit. A, B, E, F and G are
`api/`-only; **D** is `web/`-only; **C** is the one genuinely mixed part.

**Two boundaries you cannot split on.**
- **Inside Part E** — `StaffNotificationRules` *throws* on an unclassified category, so the new
  `NotificationCategory` and `ReachesALockedPhone → false` must land in the same commit or **every** notification write
  breaks, not only the new one (**R-9**).
- **Inside Part G** — parking rows without the matching un-park term releases every parked reminder within a minute,
  on a cabinet that has not paid (**R-8**, FR-8's named gap).

**If the work must be divided,** **A|B|C** and **D|E|F|G** are the two natural halves; A–C is a complete, shippable
« the entitlement exists, is enforced, and is visible » increment.

**The safety window is real and worth leaning on.** After Part A ships, every pre-existing cabinet is open-ended and
every new one has 30 days, so **no cabinet anywhere can be refused for at least 30 days**. The intermediate states
between parts are safe to ship in order.

**Three traps carried from the challenge pass** (plan §*Challenge Record*), each of which compiles or passes while
being wrong:
1. Nothing in `Features/Subscriptions/` may name `DeploymentProfile` — Application references **Domain alone**.
2. The fold folds over an **exclusive cursor**; a single `anchor + duration` gives a 31-day trial or a free day on a
   lapsed grant, and a hand-written trial `EndsOn` makes `subscription-end-date-matches-ledger` red on every new
   cabinet.
3. The gate goes **after** `LocalAuthEnforcementMiddleware`, or a 402 masks a 401 and a `must_change_password` 403.

**Migration hazards (R-2).** `dotnet ef` cannot scaffold on this machine, so the migration, its `.Designer.cs` **and**
`ApplicationDbContextModelSnapshot.cs` are hand-written — an uncommitted or wrong snapshot makes every later migration
duplicate this one. Copy `20260807102000_AddClinicSignups` as the shape.

**Deploy-time values, not code.** The spec's three Open Questions are all values in the `Subscription` config section
(real prices, the French payment-instruction text, the annual figures). Align the Tarifs page and the
« Essai accompagné — 2 semaines » landing copy with the configured 30 days before go-live.
