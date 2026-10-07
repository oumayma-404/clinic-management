# Copie sur le PC du cabinet — blueprint

> Status: **blueprint** (2026-10-06, `/think-solution`). Nothing implemented.
>
> ⚠️ **Partly superseded by [`spec.md`](spec.md)** (same day): the name is **« PC de secours »** (« PC du cabinet »
> already means the self-hosted server); devices switch **already signed in** (Part C's « sign in once » is
> withdrawn); typed forms **survive** a switch; a silent PC is unlocked by the cabinet's **other devices**;
> return after **2 min** of stable internet; saves the PC never received before the cut are **listed, never
> overwritten**, and a number is never issued twice — the lease section below has a window it does not close.
> Read the spec for behaviour.
>
> ⚠️ **Also stale after `/challenge-spec` (2026-10-06)** — spec § « Challenge decisions »: the PC takes over only
> while it reaches the cabinet's box, and only devices on the cabinet's network unlock a silent PC (the lease below
> has neither); a note/devis/avoir number is final on the cloud only once the PC holds it (the async feed alone
> re-issues it); a form carried across a switch is refused once and « Recharger » keeps the typing; a cloud
> restored from an older backup is never followed — the PC sends it the gap (the Part D digest « re-seed » must not
> run then); accounts, sign-ins and vendor actions stay live on the cloud during a cut and are never overwritten
> at handback; a one-time key on every save; the archive copy and file mirror **keep** running on the PC; devices
> find the PC after its address changes; the promotion code is issued without the console.
> Chosen by the owner over « the PC is the server, the cloud a copy » (no outside access, one PC is a single
> point of failure) and over « both write, merge later » (duplicate invoice numbers, double SMS, lost deletes —
> already refused in `multi-tenant-cloud/plan.md:443` and `offline-drafts/spec.md`).

## What it does

- **The cloud stays the main copy.** Every device works on `app.apexa.tn` as today, from anywhere.
- **One PC in the cabinet (« le PC relais ») holds a live copy** of that clinic: every row and every patient file,
  a few seconds behind the cloud.
- **Internet cut at the cabinet** → after ~90 s the PC takes over: staff in the cabinet keep working on it,
  everything included (fiches, RDV, factures, caisse). The cloud goes read-only **for that clinic only**.
- **Internet back** → the PC sends its changes up, hands control back, and the devices return to the cloud.
- **Cloud lost for good** → one command turns the PC into an ordinary local server, with everything up to the
  last few seconds.

**The rule that makes it safe: only one side may write at any moment.** No merge exists anywhere in this design.
Invoice, devis and avoir numbers stay continuous because the side that writes always holds the whole series.

## Limits agreed with the owner (2026-10-06)

| Limit | Why it cannot go |
|---|---|
| During a cut, people outside the cabinet read but cannot write | two writers = two `2026-0042` |
| Internet **and** the PC down together → nobody in the cabinet can work | same as today |
| Cloud dies suddenly → the last 1–2 s may be missing from the PC | copying is never instant |
| The PC must be on during opening hours | off, it catches up when on, but cannot take over |
| ~90 s before the PC takes over | safety gap so both never write |
| Staff sign in once on the PC when it takes over | the PC cannot mint cloud tokens (see Part C) |
| A plain browser does not switch alone | the Windows / Android apps do; a browser opens the PC's address |
| SMS and Google Agenda wait for the internet | sent at handback |
| Accounts (new user, password, 2FA) are changed online only | the cloud owns accounts |
| PC dies during a cut + admin forces the cloud back → that cut's work on the PC is listed for re-entry | rare; the only path that can lose typing |

## Decisions taken (reversible — say if wrong)

| Decision | Value | Why |
|---|---|---|
| Relays per clinic | **one** | one lease holder; a second one is a later part |
| Opt-in | the Windows app **offers it** to an admin after the push (one click + one Windows prompt); « Paramètres » has the same button | patient data on a PC is the clinic's choice, and installing a database needs admin rights once |
| Takeover | **automatic**, 90 s after the last cloud answer | nobody at the desk should have to know to press a button |
| Handback | **automatic**, after 30 s of stable internet | same |
| Files | **all** copied to the PC | « no limitation » — disk is cheap; the installer checks free space |
| Force the cloud back | admin + authenticator code, with a warning naming the PC | the only lossy action, so it is explicit |
| Name in the UI | « PC du cabinet » · « Copie à jour il y a 3 s » · « Mode cabinet » | « Hors ligne » already means « server has no internet » |
| `offline-drafts` | stays parked; a clinic with a PC relais does not need it | it covers clinics with no PC kept on |

---

## How it works — the four mechanisms

### 1. The change log (cloud, and the PC while it writes)

- Every `SaveChanges` that adds, modifies or deletes a row **in the relay scope** appends one `ClinicChange` row
  in the **same transaction**: `(ClinicId, Seq, Table, EntityKey, Op, Origin)`.
- `Seq` is per clinic and assigned under a per-clinic `pg_advisory_xact_lock` — the `AuditChainAppender` shape —
  so **commit order = Seq order**. A plain sequence would let a reader see 11 committed before 10 and skip 10
  forever.
- It records **which key changed**, never the row's values. The feed reads the current row at send time, so the
  log is immune to migrations and a key changed 40 times is sent once.

### 2. The feed (cloud → PC)

- Seed: one snapshot = `ClinicArchiveStore.ExportAsync` + the clinic's highest `Seq`, **read in the same
  RepeatableRead transaction**.
- Then `GET /api/relay/changes?after={seq}`: distinct keys since `seq` → their **current** rows (archive JSON:
  EF property names) or a tombstone when the row is gone, plus the new high-water. Idempotent: applying a page
  twice is harmless.
- The cloud pings the PC over SignalR when a clinic's log grows; a 10 s poll is the fallback.
- The PC applies upserts in the archive plan's FK order, deletes in reverse order, one transaction per page,
  then stores `AppliedSeq`.

### 3. The lease (who may write)

| Side | Rule | Clock |
|---|---|---|
| PC | heartbeats every 10 s while caught up (« Armed ») | — |
| Cloud | refuses writes for the clinic when the PC is Armed **and** silent for > 60 s | its own, from heartbeat receipt |
| PC | takes over when no cloud answer for **90 s** (60 + 30 margin) | its own monotonic clock, from the last **ack received** |

- Each side measures elapsed time on its own clock from one exchange, so wall clocks never need to agree.
- Measuring from the ack (not the send) is what puts the PC's takeover **after** the cloud's fence.
- The fence is a **predicate** (`Armed && now − LastSeen > 60 s`), never a flag a job flips: nothing can be late.
- Clean PC shutdown → « Disarm » → the cloud never fences.

### 4. Handback (PC → cloud)

1. 30 s of stable cloud answers → the PC refuses new writes for the duration (seconds).
2. If the cloud runs a newer build, the PC updates **first** (its offline rows migrate with its database).
3. The PC sends **current rows of every key in its own log since takeover** + tombstones, files first, then rows
   in FK order. Same reason as the feed: current state survives a migration; a value log would not.
4. The cloud applies them as **authoritative** (it was fenced, so nothing on its side can be newer), appends the
   PC's journal rows to the clinic's audit chain (sealed by the cloud, marked « via PC du cabinet »), and
   releases the fence.
5. Catch-ups: Google push for every handed-back appointment; reminder rows reach the cloud's outbox and
   `NotificationJob` sends or fails them by its existing staleness rules (`NotificationJob.cs:368-402`).

---

## Part A — La copie (cloud → PC, read-only) — usable alone

Delivers: the PC always holds the clinic; if the cloud is lost for good, nothing is lost but the last seconds.

### Files to create

- `api/ClinicManagement.Domain/Entities/ClinicChange.cs` — the log row. `Op` enum `Upsert | Delete`;
  `Origin` enum `Cloud | Relay`.
- `api/ClinicManagement.Domain/Entities/ClinicRelay.cs` — aggregate: `ClinicId`, `Label`, `GrantHash`,
  `PublicKey` (for secret re-wrap), `State` (`Seeding | Standby | Armed | Holding | HandingBack | Retired`),
  `LeaseEpoch`, `LastSeenAtUtc`, `AppliedSeq`, `PairedAtUtc`. Methods `Heartbeat(appliedSeq, now)`, `Disarm()`,
  `ForceReclaim(actor)`, `BeginHandback()`, `CompleteHandback()`. One relay per clinic (unique `ClinicId`
  where not Retired).
- `api/ClinicManagement.Domain/Services/ClinicWriteLease.cs` — the **one** predicate:
  `bool IsCloudWritable(ClinicRelay? relay, DateTime nowUtc)` and
  `bool RelayMayTakeOver(TimeSpan sinceLastAck)`. The 60 s / 90 s live here and nowhere else.
- `api/ClinicManagement.Infrastructure/Persistence/ClinicRelayScope.cs` — **derived** from
  `ClinicArchiveScope.Resolve(model)` plus a **delta with a reason per entry** (same style as `Excluded`):
  - **added:** `User` (the clinic's own, `User.ClinicId`), `ClinicSubscription`, `SubscriptionPeriod`,
    `Notification`, `StaffNotification`, `NotificationRead`, `NotificationDismissal`, `UserDashboardPreference`;
  - **still excluded:** `AuditEntry` (handled at handback), `DataProtectionKey`, `ClinicArchiveGrant`,
    `ClinicRecoveryPoint`, `BackupRun`, `PushDelivery`, `DeviceRegistration`, `ClinicReminderSettings`,
    session tables, vendor tables, `ClinicActivity*`, `ClinicChange` itself.
- `api/ClinicManagement.Infrastructure/Persistence/ClinicChangeCaptureInterceptor.cs` — collects in
  `SavingChanges[Async]` (like `AuditSaveChangesInterceptor.cs:126-141`); clinic = entity's own `ClinicId`,
  else the parent resolved through the plan's FK chain (tracker first, then DB; `OriginalValues` for a delete).
- `api/ClinicManagement.Infrastructure/Persistence/ClinicChangeAppender.cs` — assigns `Seq` under
  `pg_advisory_xact_lock(5315, clinic)`; called from `ApplicationDbContext.SaveChangesAsync` beside the audit
  chaining (`ApplicationDbContext.cs:646-676`).
- `api/ClinicManagement.Infrastructure/Persistence/ClinicRelayRowStore.cs` — reads current rows by key set
  (reusing `ClinicArchiveStore`'s per-table reader and JSON shape) and applies rows **authoritatively**
  (upsert + delete). ⚠️ A new mode beside `RestoreTableAsync`, never a change to it: archive restore stays
  additive.
- `api/ClinicManagement.Application/Features/Relay/` — `PairRelayCommand`, `IssueRelayPairingCodeCommand`,
  `RetireRelayCommand`, `RelayHeartbeatCommand`, `GetRelaySnapshotQuery`, `GetClinicChangesQuery`,
  `GetRelayStatusQuery`, `GetRelayDigestQuery` (per-table row-hash digest, see Part D).
- `api/ClinicManagement.API/Controllers/RelayController.cs` — `/api/relay/*`, thin MediatR pass-through.
  Relay endpoints authenticate with the **relay grant** (the `ClinicArchiveGrant` pattern: hashed secret,
  exchanged for a short token with a new `LocalAuthScopes.ClinicRelay`). ⚠️ Not the archive endpoint: it is
  rate-limited to 3 per 10 min (`RateLimiting.cs:115`).
- `api/ClinicManagement.API/Controllers/RelayController.cs` also serves files **by id** to the relay
  (`GET /api/relay/files/{fileId}`), refusing anything outside the grant's clinic.
- `api/ClinicManagement.API/BackgroundJobs/RelayFeedJob.cs` — **runs on the PC only**: seed if needed, pull
  pages, download new files into `LocalDiskFileStorage` at the **same storage key** (keys are compatible,
  `ClinicStorageKey.cs:31-44`), heartbeat.
- `api/ClinicManagement.API/BackgroundJobs/PruneClinicChangesJob.cs` — cloud: delete log rows acked by the PC,
  or older than 7 days for clinics with no PC (a PC that far behind re-seeds).
- `api/ClinicManagement.API/Maintenance/PromoteRelayCommand.cs` — console verb `promote-relay`: writes
  `Deployment:Profile=SelfHostedLan` to `appsettings.Install.json`, marks the relay Retired locally, prints what
  must be re-entered (SMS/WhatsApp credentials, Google link). Refuses while the cloud answers.
- Migration: `ClinicChanges` (PK `(ClinicId, Seq)`), `ClinicRelays`.
- `web/components/clinic-relay-settings.tsx` — « Paramètres → PC du cabinet »: pair (code shown once), state,
  « Copie à jour il y a 3 s », retire. Admin only, authenticator code to pair.
- `console/`: a « Copie PC » column on the clinic list (ok · en retard · jamais vue).

### Files to modify

- `api/ClinicManagement.Infrastructure/Deployment/DeploymentProfile.cs` — third `DeploymentKind`:
  **`ClinicRelay`**, plus four named capabilities (never a kind check):
  - `PublishesChangeFeed` — Hosted ✓ · LAN ✗ · Relay ✗
  - `HonoursRelayLease` — Hosted ✓ · LAN ✗ · Relay ✓ (the PC refuses writes unless Holding)
  - `MirrorsCloudClinic` — Relay only
  - `DispatchesOutboxes` — Hosted ✓ · LAN ✓ · Relay ✗ (the PC never sends SMS/push/mail)
  - Relay values for the existing ones: as `SelfHostedLan` (disk storage, own front door, self-signed CA,
    Windows service, trust endpoints, DB tooling), except `AllowsSelfRegistration` ✗,
    `BacksUpItsOwnData` ✗ (it **is** the copy), `RequiresSubscription` ✓ (mirrored rows),
    `RequiresAdminSecondFactor` ✓ (same accounts as the cloud). `PermitsOsPush` ✗.
- `api/ClinicManagement.UnitTests/Infrastructure/Deployment/DeploymentProfileTests.cs` — the matrix tuple gains
  a third column; **add a test over `Enum.GetValues<DeploymentKind>()`**, since nothing enumerates kinds today
  (a fourth kind would pass silently).
- `api/ClinicManagement.Infrastructure/Extensions.cs:113-119` — register `ClinicChangeCaptureInterceptor` and
  `ClinicFenceInterceptor` (Part B) beside the audit one.
- `api/ClinicManagement.Infrastructure/Security/UserSecretProtector.cs` — `RewrapForRelay(protectedSecret,
  relayPublicKey)`. The feed replaces `User.ProtectedTotpSecret` with a value only the PC can open; the PC
  re-protects it under its own ring. `Clinic.GoogleRefreshToken*` stays redacted.
- `api/ClinicManagement.Application/Common/Behaviors/RealtimeResourceResolver.cs:48-51` — add `Relay` to
  `ExcludedAreas` (no screen refreshes on a heartbeat).
- `api/ClinicManagement.API/Program.cs:1166-1332` — job registration by capability: on the PC only
  `RelayFeedJob` (+ the Part B set while Holding); `process-notifications` and `dispatch-os-push` gated by
  `DispatchesOutboxes`.
- `packaging/setup/clinic-setup.iss` — third role **« PC du cabinet (copie du cloud) »** beside SERVER/POSTE:
  same bundle as SERVER, writes `Deployment:Profile=ClinicRelay`, asks for the pairing code, checks free disk
  against the clinic's file volume. Postes keep importing the CA as today.

### Pitfalls

- **A write that skips `SaveChanges` is invisible to the log.** 11 `Execute*` calls exist today, all on tables
  outside the scope (sessions, push, notifications, signups, purge, audit). A **derived guard test** scans for
  `ExecuteUpdate/ExecuteDelete/ExecuteSql*` against any relay-scoped entity and fails — or the next one is the
  repo's « correct helper, one call site of four » shape.
- **Migrations and startup backfills write without the interceptor.** They run on both sides with the same
  code, so they must be **deterministic** (no `now()`, no random). Say so in `Infrastructure/CLAUDE.md`.
- **DB-level cascades** delete children EF never tracked. Correct by construction: the PC applies the parent's
  tombstone and its own identical FK cascades. Do not « fix » this by loading children.
- **Int-keyed owned rows** (`PatientPhoneNumbers`, DB-generated, `PatientConfiguration.cs:86`): applied with
  their owner as a whole list (delete + insert, no ids), or the PC's and the cloud's ints collide at handback.
- **The feed applier must not feed itself.** Its writes carry `Origin = Cloud` and are never handed back; its
  actor (`job|relay-feed`) is skipped by the audit interceptor, or the PC's journal fills with copies.
- **An `Unset` tenant scope reads zero rows.** Every relay job declares `UseClinic(relay.ClinicId)`.
- **Lock order:** take the audit chain lock and the change lock in one fixed order, or two writers deadlock.
- **Version lockstep:** the feed answers `409 relay_version_mismatch` when the PC's build or last migration
  differs; the PC updates before pulling. Server updates are « re-run the installer » today
  (`packaging/README.md:590-595`), so the PC gets a **silent self-update** from `/api/relay/installer`
  (Inno `/VERYSILENT`; the installer already stops and restarts services, `.iss:1271-1278`).

---

## Part B — La relève (the PC writes during a cut)

### Files to create

- `api/ClinicManagement.Infrastructure/Persistence/ClinicFenceInterceptor.cs` — the **safety net**: refuses any
  `SaveChanges` touching a fenced clinic's rows with `ClinicFencedException` (cloud), and any local write on the
  PC unless Holding (except the feed applier). Exempts `AuditEntry` and `ClinicChange`. Catches jobs, which the
  middleware cannot.
- `api/ClinicManagement.API/Middleware/RelayLeaseGateMiddleware.cs` — the **sentence**: `/api` writes for a
  fenced clinic → **423** `{ error, code: "clinic_on_relay" }`; on the PC when not Holding → 423
  `relay_standby`. Same shape and placement as `SubscriptionGateMiddleware.cs:104-118`.
- `api/ClinicManagement.Application/Features/Relay/Commands/` — `BeginHandbackCommand`,
  `ApplyHandbackPageCommand`, `CompleteHandbackCommand`, `ForceReclaimCommand` (admin + authenticator code;
  journal row naming the PC).
- `api/ClinicManagement.API/BackgroundJobs/RelayLeaseJob.cs` (PC) — every 5 s: takeover when
  `RelayMayTakeOver`, handback when the cloud is stably back, detects a forced reclaim (cloud epoch > own) and
  moves that cut's keys to « À reprendre ».
- `web/components/relay-reentry-list.tsx` — « À reprendre » (PC only, after a forced reclaim): each record the
  PC holds that the cloud never received, read-only, for manual re-entry.

### Files to modify

- **Every recurring job that writes clinic rows skips a fenced clinic** (cloud) and runs only while Holding (PC):
  `start-running-appointments`, `post-monthly-expenses`, `flag-expiring-stock`, `process-notifications`.
  The fence interceptor guarantees correctness; the skip keeps a fenced clinic from failing a whole run.
  ⚠️ `AppointmentProgressJob` groups by clinic — confirm each clinic saves separately, or one fenced clinic
  aborts every clinic's minute.
- `api/ClinicManagement.Infrastructure/Services/AppointmentGoogleSyncDispatcher.cs` — a « catch up these
  appointments » entry used by `CompleteHandbackCommand` (no catch-up pass exists today).
- `api/ClinicManagement.Infrastructure/Persistence/AuditChainAppender.cs` — append the PC's journal rows at
  handback with their original actor and time, `Origin = Relay`. The PC keeps its own chain as proof.
- Account commands (create user, password, 2FA enrol/reset, role) refuse on the PC with
  `accounts_online_only`.

### Pitfalls

- **Measure takeover from the ack received**, never from the heartbeat sent — the send time is earlier than the
  cloud's receipt and would let the PC write before the cloud fences.
- **`MonthlyExpenseJob` has no unique index on (series, month)** (`ExpenseConfiguration.cs:27`): if a cut spans
  05:00 on the 1st, confirm the cloud's next run sees the PC's posted row and does not post rent twice.
- **`xmin` differs per side**: a form open across a switch gets one 409 → « Recharger » through `useConflict`.
  Correct; do not suppress it.
- **Handback is a status transition with a concurrent writer** (`regression-safety.md` § 2): refuse PC writes
  for its whole duration, and release the cloud fence only in `CompleteHandbackCommand`'s transaction.
- **Subscription expired during a cut**: the PC enforces the mirrored entitlement; the cloud re-checks at
  handback and still accepts the rows (refusing them would lose clinical records).

---

## Part C — Les appareils suivent

### The one-click offer (how a clinic gets its PC without an installer hunt)

After the push, the Windows app updates itself (Velopack). On a PC where an **admin** is signed in to the cloud
and the clinic has **no PC du cabinet yet**, the app shows once:

> **Garder une copie du cabinet sur ce PC ?**
> Si internet coupe, le cabinet continue de travailler sur ce PC. Ce PC doit rester allumé pendant les heures
> d'ouverture.
> [Oui] [Plus tard] [Pas sur ce PC]

- **Oui** → authenticator code (same step-up as pairing) → the app downloads the installer from the cloud and
  starts it **with one Windows permission prompt** → install, pairing and first copy run on their own, with
  « Copie en cours (40 %) » in the app until done.
- **Plus tard** → asked again after 7 days. **Pas sur ce PC** → never on this PC (stored locally).
- The offer disappears on every device the moment a PC is paired.
- A PC with a battery gets one extra line: « Ce PC est un portable : s'il quitte le cabinet, la copie ne pourra
  pas prendre le relais. » A warning, not a refusal.
- **Not offered** in a browser (it cannot install anything) or on Android (a phone cannot be the PC du cabinet).

Files:
- `web/components/relay-install-offer.tsx` — the offer (web owns the words and the role check); mounted in
  `app-shell.tsx` beside the banners; shown only when `window.__clinicShell?.installRelay` exists.
- `mobile/shared/bridge.md` — new **optional** method `installRelay(pairingCode): Promise<"started" | "refused"
  | "failed">`. Feature-detected; Android does not implement it.
- `desktop/ClinicManagement.DesktopShell/RelayInstaller.cs` — downloads `/api/relay/installer`, checks its
  SHA-256 against the value the cloud sent, writes the pairing code to a temp file readable only by the current
  user, launches the installer elevated (`runas`), reports back through the bridge.
- `packaging/setup/clinic-setup.iss` — silent parameters `/RELAY /PAIRFILE=<path>`; deletes the file after
  reading it.

Pitfalls:
- **The pairing code never goes on the command line** — every process can read another's arguments. File,
  current-user ACL, deleted on read, code valid 10 minutes and once.
- **Two admins press « Oui » on two PCs at once** → the unique « one non-retired relay per clinic » index
  (Part A) refuses the second with « Un autre PC du cabinet est déjà en cours d'installation ». `Seeding` counts
  as non-retired.
- **UAC refused** → nothing installed, the offer stays (« Plus tard » semantics), no half-paired relay: pairing
  happens inside the installer, never before it.

### Files to modify

- `desktop/ClinicManagement.DesktopShell/ServerConfig.cs` — an optional `Relay` (host, port, CA fingerprint),
  **learned from the cloud** (`GET /api/relay/for-this-clinic`), never typed.
- `desktop/ClinicManagement.DesktopShell/MainWindow.xaml.cs:366-424` — when the clinic document fails
  **and** the relay says Holding → navigate to the relay instead of the « injoignable » panel; when the relay
  says Standby and the cloud answers → navigate back. Reuse `IsClinicServerUnreachable`, do not widen it.
- `mobile/android/.../shell/ServerConfig.kt` + `MainActivity.kt:353-355` — same rule. `mobile/shared/bridge.md`
  gains nothing (the switch is native).
- `web/lib/connectivity/connectivity.tsx` — a fourth state `onRelay` (cloud: clinic fenced; PC: Holding).
- `web/components/app-shell.tsx:102` — `RelayBanner` beside `SubscriptionBanner`:
  - cloud, fenced: « Le cabinet travaille sur son PC depuis 10:42 — lecture seule ici jusqu'au retour d'internet. »
  - PC, Holding: « Mode cabinet — tout est enregistré sur ce PC et partira dans le cloud au retour d'internet. »
  - browser on the cloud, cloud unreachable, relay address known: a link « Ouvrir le PC du cabinet ».
- `web/lib/api/client.ts` — 423 `clinic_on_relay` / `relay_standby` mapped to that sentence (branch on `code`).

### Pitfalls

- **Sign-in on the PC is its own**: HS256 tokens cannot be verified there without letting the PC mint cloud
  tokens for every clinic. One sign-in per cut is the price; asymmetric tokens would remove it later.
- **The PC's CA** must be trusted by Android's WebView, not only Windows — check what the LAN install does today
  before assuming.
- Banner text is a width question at 320 px (`frontend-web.md`).

---

## Part D — It is measured, not claimed

- `GetRelayDigestQuery` — per-table row count + hash over (key, row JSON) for one clinic, on both sides. The PC
  compares hourly while Standby; any mismatch → alert + re-seed of that table. **This is the derived guard in
  production**: « the copy matches » is a number.
- Alerts (reuse `server-loss-recovery` Part 3's email path to console accounts — one channel, not two):
  PC not seen for > 24 h · lag > 15 min during opening hours · digest mismatch · handback failed. Clinic admin
  gets the same as a bell row.
- CI job **`relay-roundtrip`** (Linux, two API instances + two Postgres; the PC side runs with
  `Deployment__Profile=ClinicRelay` and a file key ring instead of DPAPI):
  pair → seed → cloud writes a fiche + payment + note d'honoraires → digests equal → cut the network
  (`docker network disconnect`) → assert cloud 423 at > 60 s and PC Holding at > 90 s, **never both writable**
  → PC writes a fiche + payment + note → reconnect → handback → digests equal, note numbers continuous,
  `reconcile-money` exit 0 **on both**, outbox rows not duplicated, Google catch-up called (fake).
- **One real rehearsal** on a Windows PC: pull the cable mid-session, work 10 min, plug back, check.
  Logged in `packaging/README.md`. Owner's OK first.

---

## Key signatures (sketch)

```text
ClinicWriteLease.IsCloudWritable(ClinicRelay? relay, DateTime nowUtc) : bool
ClinicWriteLease.RelayMayTakeOver(TimeSpan sinceLastAck) : bool
ClinicRelayScope.Resolve(IModel model) : ClinicArchivePlan          // archive plan + reasoned delta
ClinicChangeAppender.AppendAsync(ApplicationDbContext ctx, IReadOnlyList<PendingChange> rows, CancellationToken ct) : Task
IClinicRelayRowStore.ReadCurrentAsync(Guid clinicId, IReadOnlyCollection<ChangedKey> keys, CancellationToken ct) : Task<RelayPage>
IClinicRelayRowStore.ApplyAuthoritativeAsync(Guid clinicId, RelayPage page, CancellationToken ct) : Task<RelayApplyOutcome>
GetClinicChangesQuery(long AfterSeq, int Limit) -> Result<RelayPageDto>   // { schemaVersion, highWater, items[{seq, table, key, op, row?}] }
RelayHeartbeatCommand(long AppliedSeq, long Epoch) -> Result<RelayAckDto> // { epoch, fenced, cloudBuild }
UserSecretProtector.RewrapForRelay(string protectedSecret, byte[] relayPublicKey) : string
```

## Blast radius (first pass — complete it in `blast-radius.md` before the first edit)

| # | Touching | Other consumers | Verdict |
|---|---|---|---|
| 1 | `ApplicationDbContext.SaveChangesAsync` (+ change append) | every write in the product | must re-test — full suite + CI `e2e` |
| 2 | interceptor list (`Extensions.cs:113-119`) | audit, automatic-write, query counting | must re-test — audit chain tests |
| 3 | `DeploymentProfile` new kind + 4 capabilities | `Program.cs` gates, every `profile.X` read, `DeploymentProfileTests` | must change — tests in the same commit |
| 4 | recurring jobs (fence skip) | 11 jobs | must change — the four that write clinic rows |
| 5 | `UserSecretProtector` | login, 2FA, recovery | must re-test |
| 6 | `ClinicArchiveScope` (read by `ClinicRelayScope`) | archive export/restore, recovery points | unaffected — consumed, not modified |
| 7 | `connectivity.tsx` new state | header badge, every screen | must re-test |
| 8 | desktop / Android navigation-failure path | the « injoignable » panel | must re-test — a wrong switch replaces the whole app |
| 9 | `clinic-setup.iss` third role + silent `/RELAY` | SERVER and POSTE roles | must re-test both |
| 10 | `bridge.md` new optional `installRelay` | Android shell, iOS shell (never compiled) | unaffected — optional, feature-detected; re-test Android shows no offer |
| 11 | `app-shell.tsx` (offer + banner) | every signed-in screen | must re-test at 320 px |

## Test strategy

| Rule lives in | Test |
|---|---|
| lease math (60 / 90, ack-based, epochs, forced reclaim) | unit, fake clocks — the « never both writable » case first |
| capture (every op, child → clinic, SystemWide, deletes, int-keyed owned rows) | unit |
| scope derivation (every table in scope or excluded with a reason) | guard test, like `Every_Soft_Link_To_A_Fiche_Is_Accounted_For` |
| no `Execute*` on a scoped table | source-scan guard |
| every clinic-writing job consults the fence | source-scan guard over `Program.cs` registrations |
| profile matrix, all kinds enumerated | `DeploymentProfileTests` |
| TOTP re-wrap round trip | unit |
| handback ordering, numbers continuous, money reconciles | CI `relay-roundtrip` |
| shell switch, banners, 423 sentence, 320 px | `/test-in-browser` + Windows rehearsal |
| offer shown only to an admin, in the Windows app, with no PC paired | `/test-in-browser` (shell bridge stubbed) |
| one-click install: UAC accepted / refused, two PCs at once | Windows rehearsal + unit test on the unique pairing |
