# Implementation Plan: PC de secours

**Status:** APPROVED
**Challenged:** Yes (2026-10-07 — 11 issues, all fixed; see « Challenge changes » at the end)
**Created:** 2026-10-07
**Spec:** [spec.md](./spec.md) (APPROVED, challenged 2026-10-06) · facts: [exploration.md](./exploration.md) ·
superseded sketch: [blueprint.md](./blueprint.md)
**Scope:** the whole spec — Parts A (la copie), B (la relève), C (les appareils suivent), D (prouvé) — as
**one story** in **three parts** (owner's choice, 2026-10-07): 1 · La copie · 2 · La relève · 3 · Les appareils
suivent. Proof (spec Part D) is folded into the part it proves. Each part ends green and is committed on its own.

## Overview

A cabinet PC runs the same server bundle as a self-hosted install, as a third deployment kind,
**`ClinicRelay`**. It pairs with the cloud through a relay grant, seeds from a snapshot, then follows a
per-clinic **change log** the cloud writes in the same transaction as every save, and copies patient files.
Its state is computed by **one predicate** read by Paramètres, the bell and the vendor console.

When the cabinet's internet drops, a **lease** decides who may write: the cloud stops accepting the clinic's
saves 60 s after last hearing from a ready PC, the PC takes over at 90 s only if it still reaches the cabinet's
box, and the two act on **one shared answer** (the cloud's last acknowledgement). The PC then writes its own
change log; when the internet has been stable for 2 minutes it hands its work back, the cloud applies it
authoritatively (accounts and the subscription excepted) and lists anything both sides changed. The cabinet's
Windows and Android apps are prepared in advance — they know the PC's address and certificate fingerprint and
hold a PC session — so they switch already signed in, carrying any open form.

### Decisions taken (reversible — say if one is wrong)

| # | Decision | Why |
|---|---|---|
| **Copy** | | |
| D1 | Change capture in `ApplicationDbContext.SaveChangesAsync`, **inside the business transaction** — not an interceptor writing in its own scope like the audit ledger | a change row that can commit without its data (or the reverse) is a silent gap |
| D2 | Sequence from a per-clinic counter row (`ClinicChangeCursors`, `UPDATE … RETURNING`); capture **only when the clinic has a cursor row** (created at pairing, removed at retire), decided in the same transaction | commit order = seq order per clinic; clinics with no PC pay one no-op UPDATE |
| D3 | The log stores **keys, never values**; the feed sends each key's **current** row | immune to migrations; a key changed 40 times is sent once |
| D4 | Relay scope = the archive plan + a reasoned delta, derived from the EF model, guard-tested | the archive scope is already the model-derived answer; a hand list drifts |
| D5 | Owned collections travel with their owner as a whole list (`PatientPhoneNumbers`) | DB-generated int ids would collide across the two databases |
| D6 | The PC applies **authoritatively** (upsert + delete) in a new `ClinicRelayRowStore`; archive restore stays insert-only | different contract, never a mode flag on the restorer |
| D6b | **A batch is never cut by a row count.** Current rows are read in one snapshot for *every* key changed in `(after, high-water of that snapshot]` — the only cut whose rows are link-complete against what the PC already holds. A large backlog is downloaded in parts but **applied in one transaction** (staged); past a threshold the PC re-seeds instead. Inside a batch: deletes in reverse FK order, upserts in FK order, self-references (`Invoice.Supersedes*`) in two passes. **Same rule for the return (D18)**, PC → cloud. A row whose parent is missing is an error that stops the batch — never skipped like the archive restorer does | a row-limited page can hold a record pointing to one that changed in a later page: the batch then fails forever (copy stuck) or the row is dropped in silence (challenge 2026-10-07) |
| D7 | The PC pulls (`/relay/changes?after=`), nudged by the existing clinic hub, 15 s poll as safety net | reuses the realtime bus |
| D8 | One-time pairing code (admin + step-up, 10 min, single use) → long-lived relay secret; the PC sends an RSA public key (private half DPAPI-machine) | the cloud re-wraps TOTP secrets for that PC only |
| D9 | Admin-only bell = new nullable `StaffNotification.TargetRole` honoured by feed, count and dismissals | no such audience exists; per-admin fan-out goes stale |
| D10 | The cloud serves a **slim relay update** (API + web) and the full server installer from `deploy/updates/relay/`, built by CI | nothing hosts the server installer today; lockstep on every deploy; 113 MB is too much per deploy |
| D10b | **The heartbeat and its ack never refuse on version.** `relay_version_mismatch` applies to the copy, files, return and check calls only; on the heartbeat a mismatch is a field of the ack (« update needed »). The PC then **disarms first** (two-phase, D14), updates, catches up and re-arms. Its own update restart is a clean shutdown (disarm) too. ⚠️ Overrides the spec's API table, which lists the heartbeat under 409 `relay_version_mismatch` | refused heartbeats after a cloud deploy would lock every clinic with a PC at 60 s and make every PC take over at 90 s (challenge 2026-10-07) |
| D11 | Promotion code signed **offline** by a vendor key; the PC verifies with a built-in public key | the console dies with the cloud |
| D12 | A cloud that went back in time is never followed; the PC keeps its copy, alerts, and **sends the gap back through the return path** (Part B). Three tests, any one trips it: (1) **`FeedEpoch` is derived, never stored** — `pg_control_system().system_identifier` + `pg_control_checkpoint().timeline_id` + the database's OID, so a new cluster, a PITR (wal-g) and a dump-restore each change it with no operator step; (2) high-water below the PC's; (3) **continuity**: every pull sends `after` + the fingerprint of the change row the PC last applied (`Table`, `EntityKey`, `Op`), and the cloud's row at that seq must match | AC-9.4. A stored epoch is restored with the backup, and PCs are off at night: a restored cloud's seq can pass the PC's before it is switched on, and the hourly repair would then delete the only copy of the lost records (challenge 2026-10-07) |
| **Lease** | | |
| D13 | The fence is a **predicate**, never a flag a job flips: cloud refuses scoped writes when `relay armed ∧ now − LastConfirmedAckSentAt > 60 s ∧ not unlocked`; PC takes over when `sinceLastAck ≥ 90 s ∧ last ack said armed ∧ the cabinet's box answers`. **Every ack carries an id; every heartbeat carries the id of the last ack the PC received; the cloud's 60 s runs from when it SENT the last ack the PC has confirmed** — never from the last heartbeat it received | nothing can be late; a heartbeat whose answer was lost no longer moves the cloud's clock, so the PC (`≥ ack sent + 90 s`) is always after the cloud (`ack sent + 60 s`) on any line, lossy or not. Cost: the cloud locks up to ~10 s sooner after a cut (challenge 2026-10-07) |
| D14 | « Armed » and « disarmed » are **two-phase**: the cloud's ack states it, the PC confirms it, and the cloud keeps fencing-on-silence until the PC has confirmed a disarm | the cloud and the PC can never hold two different answers |
| D15 | Enforcement in two layers, both sides: `ClinicFenceInterceptor` (safety net in SaveChanges for scoped rows, catches jobs) + `RelayLeaseGateMiddleware` (the 423 sentence, `SubscriptionGateMiddleware`'s shape). FR-11 exemptions on the cloud: sign-in, account admin, vendor actions — **declared once, by table, and read by both layers**: `User`, `UserRecoveryCode`, the subscription tables, the vendor's messaging-quota tables, `StaffNotification` (the cloud's own relay bell rows, AC-6.4 / AC-5.9), `AuditEntry`, `ClinicChange`, `ClinicRelay`, processed requests; a guard test fails on an FR-11 command writing a table outside it | the middleware gives the sentence; the interceptor makes « never both » true for code paths no middleware sees; an endpoint-only exemption would still be refused one layer down (challenge 2026-10-07) |
| D16 | **Numbered saves are confirmed by the PC inside the transaction**: the cloud assigns the number, writes the rows (so a unique-index collision is caught *before* any promise), sends a « promise » to the PC over the hub (`InvokeAsync<T>` client result), waits ≤ 3 s, commits — the wait holds the clinic's audit-chain and cursor locks, so it is timed per save (`Diagnostics`) and the 3 s is a ceiling, not a target; no ack → rollback, 423 `relay_unconfirmed`. **The promise carries the save's `Idempotency-Key`** (D17); the PC stores `key → number`. The PC continues numbering after max(committed, promised) — **except a re-press of a promised key, which reuses that key's number** | the only way the number the secretary just printed cannot be issued again; and a re-press at the cut (EC-6 on a note) reprints the same n°, never the next one (challenge 2026-10-07) |
| D17 | Every web save carries an `Idempotency-Key` (generated per submit in `client.ts`, carried across a retry and a switch); the server stores processed keys per clinic (48 h) and replays the stored answer; the keys replicate. At the return, the PC's rows for a key the cloud already processed are **dropped only when no later PC change points to them** (decided from the PC's own log: no later key's current row references them); otherwise the pair is **listed** in « Modifications à vérifier » as a probable duplicate with both versions — never dropped, never applied twice | FR-6 / EC-6 — nothing does this today. A blind drop would erase a reprinted note (gap + paper in the patient's hand) and orphan a payment taken on it (challenge 2026-10-07) |
| D18 | The return: PC stops accepting (423 `relay_handing_back`), updates if needed, sends files then the current rows of every key in its own log since takeover + its journal rows; the cloud applies authoritatively except accounts/subscription — **sign-in traces are merged field by field (FR-11): a recovery code used on either side stays used, last login = latest, lockouts carried; everything else on an account stays the cloud's** — lists « modifications à vérifier » (keys the cloud changed after the PC's last applied seq), releases the fence in the same transaction; then Google catch-up and reminders (at most one per visit, none passed) | spec US-5 |
| D19 | « Reprendre la main » bumps the lease epoch; the PC, on reconnect, sees a newer epoch, stops, catches up, and lists the cut's keys under « À reprendre » from its own log | spec US-7 |
| D20 | « Internet coupé » vs « Le cloud est injoignable » decided by the PC: box answers + public probe fails = internet; public probe ok + cloud fails = cloud | banner tells the truth |
| D20b | **FR-3's clock: the PC corrects Windows' clock itself.** Each ack carries the cloud's time; when Windows' time is off by > 30 s, the service (LocalSystem, `SeSystemtimePrivilege`) sets it from the ack's time, adjusted by half the round trip, and raises EC-10's bell row. If it cannot set it, the PC is **not ready** (no takeover) and says why. Survives restarts by construction (the clock *is* corrected) | the server reads `DateTime.UtcNow` directly in 427 places, TOTP and sign-in included — no clock abstraction exists to shift, and correcting the one clock fixes dates, caisse days and authenticator codes at once (challenge 2026-10-07) |
| **Devices** | | |
| D21 | Shells learn the PC's addresses and **certificate fingerprint** from the cloud; on a failed address they find the PC by UDP discovery on the LAN; the certificate is **pinned by fingerprint** (WebView2 `ServerCertificateErrorDetected`, Android `onReceivedSslError`) | no CA step, survives the box giving the PC a new address |
| D22 | **Prepared session**: while online, the shell gets a short assertion signed by the cloud's relay key (user, device, token version) and trades it with the PC for a PC session. **One holder only: the shell never refreshes a PC session.** Each day it trades a *fresh* assertion for a *new* PC session and writes it straight into the WebView's cookie for the PC origin, replacing the old one; after a switch the page's cookie is the only copy. Dropped at sign-out and when the account is disabled (via the feed) | the PC cannot verify the cloud's HS256 tokens, and must not mint the cloud's. A refresh token is rotated on every use and a reused copy kills the whole family as theft — two holders (shell + page) would sign the person out before the next cut (challenge 2026-10-07) |
| D23 | Open forms are **carried** by the shell (`carryDraft` / `takeCarriedDraft` bridge pair) into a `useCarriedDraft` hook on the fiche, RDV, patient and devis forms; the first save after a switch is refused once with « Le serveur a changé pendant votre saisie… »; **« Recharger » reconciles three ways on those forms too** (the fiche's `fiche-merge.ts` approach, generalised) | the challenge chose « Recharger keeps the typing »; the merge keeps it without the silent overwrite the plain version carries |
| D24 | Browsers: « Préparer ce navigateur » = the existing trust page (CA) through a QR code; otherwise a warning | accepted limit in the spec |
| **Proof** | | |
| D25 | Hourly digest (per-table count + hash over key + row JSON) compared both sides; a mismatch re-seeds that table — never toward an older cloud (D12). **The hash leaves out the columns that differ by design** — `xmin`, `User.ProtectedTotpSecret` (re-wrapped per side), the Google token and SMS/WhatsApp keys the PC never holds — declared once beside the relay scope | FR-9. Without the exclusions `User` and `Clinic` mismatch every hour forever and the vendor is alerted for nothing (challenge 2026-10-07) |
| D27 | **`ClinicChanges` is pruned**: cloud rows `≤` the PC's confirmed `AppliedSeq` older than 7 days, keeping the last applied row (D12's fingerprint); for a clinic with no PC, nothing is written. The PC prunes its own log after a completed return | the return reads « cloud changes after the PC's last applied seq », D19 reads the PC's own log — both need retention, neither needs forever; unpruned, the hosted table grows with every save (challenge 2026-10-07) |
| D26 | CI `relay-roundtrip` (two instances, two Postgres, `docker network disconnect`) asserting « never both writable », numbers continuous, `reconcile-money` exit 0 on both | FR-10 |

## Files to Modify/Create

### Files to Create

| File | Purpose | Part |
|------|---------|------|
| `api/…/Domain/Entities/ClinicRelay.cs` | aggregate: label, grant hash, public key, cert fingerprint, LAN addresses, state, lease epoch, armed flags, `AppliedSeq`, `LastSeenAtUtc`, build, disk, files progress, clock skew, retirement | 1 |
| `api/…/Domain/Entities/ClinicRelayPairingCode.cs` | one-time code | 1 |
| `api/…/Domain/Entities/ClinicChange.cs` · `ClinicChangeCursor.cs` | log row `(ClinicId, Seq, Table, EntityKey, Op, Origin, IdempotencyKey)` · `(ClinicId, LastSeq)` — `FeedEpoch` is read from the database identity (D12), never stored | 1 |
| `api/…/Domain/Services/ClinicRelayHealth.cs` | the one FR-2 state predicate | 1 |
| `api/…/Domain/Services/ClinicWriteLease.cs` | the one lease predicate (60 / 90 s live here and nowhere else) | 2 |
| `api/…/Infrastructure/Persistence/ClinicRelayScope.cs` · `ClinicChangeCapture.cs` · `ClinicRelayRowStore.cs` | scope · capture · read/apply rows | 1 |
| `api/…/Infrastructure/Persistence/ClinicFenceInterceptor.cs` | refuses scoped writes when fenced (cloud) / not holding (PC) | 2 |
| `api/…/Infrastructure/Persistence/ProcessedRequest*.cs` | idempotency keys store | 2 |
| `api/…/Application/Features/Relay/**` | pairing, heartbeat, snapshot, changes, files, status, retire, lost, takeover, handback, reclaim, review lists, device preparation, digest | 1–3 |
| `api/…/API/Controllers/RelayController.cs` · `RelayLocalController.cs` | cloud side `/api/relay/*` · PC side (device preparation, discovery info, erase) | 1, 3 |
| `api/…/API/Middleware/RelayLeaseGateMiddleware.cs` · `IdempotencyMiddleware.cs` | 423 sentences · replay | 2 |
| `api/…/API/BackgroundJobs/RelayFeedJob.cs` · `RelayLeaseJob.cs` · `RelayDigestJob.cs` | PC: follow · takeover/return · hourly check | 1, 2 |
| `api/…/API/BackgroundJobs/PruneClinicChangesJob.cs` | cloud + PC: log retention (D27) | 1 |
| `api/…/API/Hosting/RelayDiscoveryResponder.cs` | PC: UDP « qui est le PC de secours de la clinique X ? » | 3 |
| `api/…/API/Maintenance/PromoteRelayCommand.cs` · `SignRelayPromotionCommand.cs` | promotion | 1 |
| migrations (one per part that needs one) | ⚠️ strip the scaffolded `xmin` each time | 1, 2 |
| `web/components/relay/*` | settings card, offer, banners, « Modifications à vérifier », « À reprendre » | 1, 2 |
| `web/lib/forms/form-merge.ts` + `web/lib/hooks/use-carried-draft.ts` | generalised three-way merge · carried drafts | 3 |
| `web/lib/api/relay.ts` | client | 1 |
| `desktop/…/RelayInstaller.cs` · `RelaySwitch.cs` · `RelayDiscovery.cs` | install · switch · discovery | 1, 3 |
| `mobile/android/…/RelaySwitch.kt` · `RelayDiscovery.kt` | same on Android | 3 |
| `.github/workflows/relay-package.yml` · CI job `relay-copy` → `relay-roundtrip` in `ci.yml` | packages · proof | 1, 2 |
| `api/ClinicManagement.UnitTests/**/Relay*Tests.cs` | see Testing Strategy | 1–3 |

### Files to Modify

| File | Changes | Part |
|------|---------|------|
| `DeploymentProfile.cs` + `DeploymentProfileTests.cs` | kind `ClinicRelay`; `PublishesChangeFeed`, `MirrorsCloudClinic`, `RunsClinicJobs`, `DispatchesOutboxes`, `HonoursRelayLease`; triple matrix + all-kinds test | 1, 2 |
| `OutboundEndpointPolicy.cs`, `LocalDataProtection.cs` (×2), `PermitsOsPush` | the four kind switches: relay = LAN behaviour | 1 |
| `ApplicationDbContext.cs`, `Infrastructure/Extensions.cs` | capture call; fence interceptor; fixed lock order cursor → audit chain | 1, 2 |
| `RealtimeResourceResolver.cs` + `web/lib/realtime/clinic-hub.ts` | `Relay` area excluded | 1 |
| `ClinicHub.cs` | relay connection group; number promises (client results) | 1, 2 |
| `LocalAuthClaims.cs`, `ScopedTokenFilter.cs`, `RateLimiting.cs`, `LocalAuthService.cs` | scope `clinic-relay`; relay signing key pair for D22 assertions | 1, 3 |
| `UserSecretProtector.cs` | `RewrapForRelay` | 1 |
| `StaffNotification.cs`, `NotificationGenerator.cs`, `PushNotificationGeneratorDecorator.cs`, `StaffNotificationRepository.cs`, `NotificationCategory.cs` (appended) | `TargetRole`; relay categories | 1 |
| `IssueInvoiceCommand.cs`, `BillDentalRecordCommand.cs`, `DevisNumbering.cs`, `CreateCreditNoteCommand.cs` | number promise before commit (D16) — through **one** `INumberPromise` service, guard-tested | 2 |
| `Program.cs` | endpoints, middlewares, job gating (`RunsClinicJobs`, `DispatchesOutboxes`, lease state), verbs | 1–3 |
| `NotificationJob.cs`, `AppointmentGoogleSyncDispatcher.cs`, `AuditChainAppender.cs` | held reminders + at most one per visit; catch-up entry; PC journal rows appended at return | 2 |
| `Platform/**` + `console/` | « PC de secours » column | 1 |
| `clinic-setup.iss` | third role, `/RELAY /PAIRFILE=`, free disk, uninstall erase, suppressible boxes | 1 |
| `VaultBridge.cs`, `MainWindow.xaml.cs`, `ServerConfig.cs`, `mobile/android/…/MainActivity.kt`, `ServerConfig.kt`, `network_security_config.xml`, `mobile/shared/bridge.md`, `web/types/clinic-shell.d.ts` | `installRelay`, `carryDraft`/`takeCarriedDraft`; switch on failure; pinned PC certificate + cabinet-network reports (Part 2); versions bumped | 1, 2, 3 |
| `web/lib/api/client.ts` | `Idempotency-Key` header; 423 codes → sentences (branch on `code`) | 2 |
| `web/lib/connectivity/connectivity.tsx`, `web/components/app-shell.tsx` | state `onRelay`; banners (cut / read-only / return / PC injoignable) | 2 |
| `patient-record-modal.tsx`, `edit-appointment-dialog.tsx`, `create-appointment-dialog.tsx`, `edit-patient-dialog.tsx`, `treatment-plan-form-modal.tsx`, `plan-workspace.tsx`, `plan-item-steps-dialog.tsx` | carried drafts; reconciling « Recharger » | 3 |
| `web/components/clinic-settings.tsx` | mount the card | 1 |
| `deploy/docker-compose.hosted.yml`, `deploy/README.md` | serve `deploy/updates/relay/`; deploys must stay under 60 s of API downtime | 1 |
| `features/offline-drafts/spec.md` | not offered with a PC de secours | 1 |

## Implementation Stories

### US-1: PC de secours

**Goal:** the whole spec — a cabinet PC holds a live copy, takes over during a cut, gives everything back,
devices follow by themselves, proven by a CI cut test and a real rehearsal.
**Blocked by:** None
**Layers:** DB, Domain, Application, API, jobs, installer, Windows shell, Android shell, web, console, CI

> Three parts, in order. Each ends with the gate green (unfiltered suite, `check:responsive`, `tsc`, build) and
> its own commit. Inside a part, the order of the bullets is the order of work.

#### Part 1 — La copie (spec Part A + the hourly check)

What the cabinet gets: its whole record on a PC it owns, seconds behind, watched, retirable, installed in one
click, and promotable by the vendor if the cloud is lost.

- **Kind and pairing.** `ClinicRelay` kind + capabilities + the four kind switches; jobs gated by
  `RunsClinicJobs`; staff writes on the PC → 423 `relay_standby`. `ClinicRelay` + one-time pairing code
  (admin + step-up, 409 `relay_already_paired`), token scope `clinic-relay`. Installer third role.
- **Copy.** Snapshot seed (scope export, one RepeatableRead transaction) applied by `ClinicRelayRowStore`; then
  the change log (`ClinicChangeCapture` in `SaveChangesAsync`, cursor created with the snapshot's high-water)
  and `/relay/changes` (keys → current rows/tombstones, `FeedEpoch`, `relay_version_mismatch`); hub nudge +
  15 s poll; derived epoch / high-water / continuity-fingerprint check (D12). Files by id at the same storage key, resumable, coffre excluded.
- **Watching.** Heartbeat every 10 s → `ClinicRelayHealth`; « Paramètres → PC de secours » card;
  `StaffNotification.TargetRole` + admin-only bell rows in opening hours; EC-9, EC-10; hourly digest with
  self-repair (never toward an older cloud); console column; vendor alert emails (shared with
  `server-loss-recovery` Part 3 — message that session first).
- **Lifecycle.** `User` in scope with TOTP re-wrapped per PC; retire → PC read-only, admins only; erase on the
  PC; uninstall prompt; « perdu ou volé ». Journal rows for every step.
- **One click and lockstep.** CI publishes installer + slim update (⚠️ owner's OK before the first VPS
  publish); silent `/RELAY /PAIRFILE=`; bridge `installRelay`; offer (AC-1.1–1.13); PC self-updates on
  « update needed » in the heartbeat ack (D10b: disarm → update → re-arm; the heartbeat itself never refuses on
  version). Promotion verbs.

*Check:* unit — profile ×3 kinds, scope guard, capture, raw-write guard, health table, pairing, lost fan-out,
promotion code, re-wrap; CI `relay-copy` (two instances, per-table hash equal; a cloud restored from an earlier dump **and then written past
the PC's seq** stops the PC);
`/test-in-browser` on the card, bell and offer; Windows rehearsal of the one-click install (owner's OK).

#### Part 2 — La relève (spec Part B + the CI cut test)

What the cabinet gets: when the internet drops, it keeps working on the PC, and everything goes back to the
cloud once — reachable from a plain browser at the PC's address even before Part 3.

- **Lease.** `ClinicWriteLease` (D13), two-phase arming (D14), `RelayLeaseJob` (box check), fence interceptor +
  gate middleware both sides with FR-5 / FR-11 rules, clean shutdown disarms, Windows Update held in opening
  hours, restart during a cut resumes in charge, Windows clock kept on the cloud's time (D20b).
- **Device reports (AC-6.2) — the shells' half, built here.** Only the Windows and Android apps report, **never a
  browser** (it cannot tell an untrusted certificate from a PC that is down, nor know it is on the cabinet
  network). So this part brings forward from Part 3: the PC's address + certificate fingerprint learned from the
  cloud, the pinned-certificate probe (D21), and « on the cabinet network » by gateway match. A report counts
  only when all three hold. *(Part 3 keeps the switch, UDP discovery, prepared sessions and carried forms.)*
- **Integrity.** `INumberPromise` in the four numbering paths (D16); `Idempotency-Key` end to end (D17).
- **Return.** 2 min stable → handback (D18): files, rows, journal; accounts/subscription untouched except the
  merged sign-in traces (used recovery codes, last login, lockouts — FR-11);
  « Modifications à vérifier »; Google catch-up; reminders at most one per visit; 7-day subscription cap; retry
  + alert after 15 min; a restored cloud's gap travels back here.
- **Screens.** Banners (cut, read-only, return, PC injoignable, « Le cloud est injoignable »); « Reprendre la
  main » (D19) and « À reprendre »; retire / lost while locked take the cloud back.

*Check:* fake-clock lease tests (« never both writable » first — including heartbeats that reach the cloud while
every answer is lost; unplugged PC never takes over; 4G device never counts); number-promise and idempotency tests; CI `relay-roundtrip` (`docker network disconnect`) with EC-1,
EC-5, EC-6, EC-7, EC-11, **EC-17** (PC cut off from the box: no takeover while simulated cabinet-app reports unlock
the cloud), **EC-21**, **EC-22** (note saved 2 s before the cut), plus « new cloud build deployed mid-run → no
lock, no takeover » (D10b) — numbers continuous, `reconcile-money` 0 on both, no duplicate outbox rows;
`/test-in-browser` on banners and lists at 320 / 390 / 820 / 1180 / 1440 and 730 px tall.

#### Part 3 — Les appareils suivent (spec Part C + the real rehearsal)

What the cabinet gets: PCs and phones switch by themselves, already signed in, with the form still typed.

- **Switch.** Windows and Android shells move to the PC while the PC holds — on a failed clinic document **or when
  the cloud answers `clinic_on_relay` and the PC answers « I hold »** (the PC alone lost the cloud: without this
  the whole cabinet sits on a read-only cloud) — and back after the return; only on the cabinet network (gateway
  match, Part 2); AC-3.8's line.
- **Reach and trust.** UDP discovery when the address changed (addresses, fingerprint and pinning already came
  with Part 2's device reports); prepared sessions (D22); browser « Préparer ce navigateur ».
- **Carried forms.** `carryDraft` / `takeCarriedDraft`; `useCarriedDraft` on the fiche, RDV, patient and devis;
  first save after a switch refused once; reconciling « Recharger » on the 5 forms that still discard typing.

*Check:* typed fiche survives a switch both ways; « PC alone loses the cloud » → the apps switch to it; switch →
return → daily renewal → switch again → still signed in (D22);
`/test-in-browser` two-session conflict on each form; the real
rehearsal in a cabinet setting — cable pulled mid-session, box restarted, 10 min of work, cable back,
everything in the cloud once (owner's OK first, logged in `packaging/README.md`).

**Validation (story):**
- [ ] Each part's check above.
- [ ] `verify-schema` and `reconcile-money` exit 0 on cloud and PC after a full cut-and-return.

## Testing Strategy

### Unit Tests (no database)
- Profile matrix ×3 kinds + all-kinds; relay scope coverage; capture (add/modify/delete, child → clinic, owned list, no cursor); raw-write guard.
- Batch completeness (D6b): a child re-pointed to a parent created/changed later in the same window applies in one batch; a self-referencing invoice pair applies; a missing parent stops the batch.
- `ClinicRelayHealth` and `ClinicWriteLease` tables with fake clocks — « never both writable » across every interleaving of heartbeat, ack, silence and device report.
- Number promise (ack / timeout / rollback / PC continuation / **re-press of a promised key reuses its number**); idempotency replay; handback apply (accounts untouched, **a recovery code used on the PC is refused on the cloud after the return**, conflicts listed, duplicate key dropped **only when nothing later points to it — else listed**); promotion code; TOTP re-wrap; lost-PC fan-out.
- Guards: every numbering path uses `INumberPromise`; every clinic-writing job consults the lease; every 423 `code` has a French sentence in `client.ts`; form payload classification (C3).

### Integration (CI)
- `relay-copy` (Part 1) grows into `relay-roundtrip` (Part 2): two instances (separate `.local`), two Postgres, MinIO; network cut by `docker network disconnect`.

### E2E / browser
- `/test-in-browser` at the end of each part, at 320 / 390 / 820 / 1180 / 1440 and 730 px tall.
- Windows rehearsals: the one-click install (Part 1) and the cabinet rehearsal (Part 3) — owner's OK first.

## Risk Register

| ID | Risk | Likelihood | Impact | Part | Mitigation |
|----|------|------------|--------|------|------------|
| R-1 | **One story of ~150 files across 9 layers, in three parts of ~50** — no session finishes a part | High | High | all | each part green and committed alone; inside a part, commit at each bullet boundary with the gate green; progress file |
| R-2 | Two writers at once (lease bug, clock, partition) → duplicate numbers, lost work | Med | High | 2 | one predicate, ack-measured, two-phase arming, box check, fence in two layers, fake-clock tests, CI cut test |
| R-3 | A write path skips `SaveChangesAsync` → copy diverges silently | Med | High | 1 | raw-write guard; hourly digest (D1) |
| R-4 | Builds drift between cloud and PC | High | High | 1 | `relay_version_mismatch`; PC updates first; deterministic migrations |
| R-5 | Capture / number promise slow clinic saves | Med | Med | 1, 2 | cursor only with a PC; promise only for numbered saves; measure SQL count/time |
| R-6 | Credentials on a cabinet PC | Med | High | 1, 3 | per-PC re-wrap, DPAPI machine ring, admin-only retired copy, encryption warning, « perdu ou volé » |
| R-7 | Prepared sessions on lost devices | Med | Med | 3 | daily refresh, sign-out drops it, disabled account drops it via the feed, `TokenVersion` |
| R-8 | Handback overwrites something the cloud legitimately changed | Med | High | 2 | FR-11 exemptions; conflicts listed, never overwritten silently; roundtrip asserts it |
| R-9 | Lock order between cursor, audit chain and number rows deadlocks | Low | High | 1, 2 | one fixed order, documented beside each |
| R-10 | VPS publishing (bandwidth, disk, prod) | Med | Med | 1 | slim update; owner's OK before the first publish |
| R-11 | Shell work needs a Mac for iOS | — | — | 3 | iOS out of scope (spec) |
| R-12 | `server-loss-recovery` Part 3 built in parallel | Med | Low | 1 | message that session before building the vendor alert sender |
| R-13 | A re-seed or repair toward a restored (older) cloud erases newer records | Low | High | 1 | derived epoch + high-water + continuity fingerprint (D12) land with the feed, before any repair exists |

## Breaking Changes

- `DeploymentKind` gains a member (4 switch sites, profile tests reshaped).
- `StaffNotification.TargetRole`: feed, unread count and dismissals change.
- Hosted saves of clinic rows run in an explicit transaction when the clinic has a PC; numbered saves may be refused with 423 while the PC is armed and silent.
- Every web write sends `Idempotency-Key`.
- New `NotificationCategory` members appended (persisted as int).

## Migrations

Part 1: relays, pairing codes, changes, cursors, `TargetRole` · Part 2: lease columns, number promises,
processed requests, review / reprendre lists. Check each for the scaffolded `xmin`;
`verify-schema` before and after; `reconcile-money` after Part 2's.

## Observations (found while planning, not in scope)

- ⚠️ **The clinic archive drops owned collections** — `Patient.AdditionalPhoneNumbers` never reaches an archive or a recovery point, so a restore loses every patient's extra numbers.
- `desktop/…/ArchiveGrant.cs:33` says the token endpoint allows 3 per 10 min; it is 20 per 10 min per IP.
- `packaging/README.md:490` says « no auto-updater », contradicting `:475`.

## Challenge changes (2026-10-07)

| # | Severity | Gap | Change |
|---|---|---|---|
| 1 | Critical | Lost answers moved the cloud's 60 s but not the PC's 90 s → both writable on a bad line | D13: cloud counts from the last ack the PC *confirmed* |
| 2 | Critical | Heartbeat refused on version → every deploy locks every clinic, every PC takes over | D10b: heartbeat never refuses; disarm → update → re-arm |
| 3 | Critical | Nothing changed `FeedEpoch`; a restore + PCs off at night → PC follows, repair deletes the only copy | D12: epoch derived from the database identity + continuity fingerprint |
| 4 | Major | Row-limited pages not link-complete → copy stuck or rows skipped | D6b: batch runs to the snapshot's high-water, applied in one transaction (both directions) |
| 5 | Major | Re-press at the cut: dropped note = gap + paper in hand; orphan payment | D16: promise carries the key, re-press reuses its number · D17: drop only if nothing points to it, else list |
| 6 | Major | Device reports (Part 2) needed Part 3; browsers could unlock wrongly; EC-17/EC-22 untested | Part 2 builds the apps' report half; browsers never report; EC-17 + EC-22 in CI |
| 7 | Major | PC alone loses the cloud → cabinet stuck on a read-only cloud | Part 3: apps also switch on `clinic_on_relay` + PC « I hold » |
| 8 | Major | FR-3 (cloud's time on the PC) not planned; 427 `DateTime.UtcNow` | D20b: the service corrects Windows' clock; can't → not ready |
| 9 | Major | FR-11 sign-in trace merge missing (recovery code reusable); lock exemptions by endpoint only | D18 merge · D15 exemption list by table, guard-tested |
| 10 | Major | Prepared session « refreshed daily » → two holders → theft alarm → sign-in at the cut | D22: one holder, fresh session daily, never refreshed by the shell |
| 11 | Minor | Digest compared per-side columns · log never pruned · promise placement unstated | D25 exclusions · D27 + `PruneClinicChangesJob` · D16 rows written before the promise, wait timed |

Also corrected: « the three numbering paths » → four (`IssueInvoiceCommand`, `BillDentalRecordCommand`,
`DevisNumbering`, `CreateCreditNoteCommand` — verified, no fifth). ⚠️ D10b overrides the spec's API table (heartbeat
listed under 409 `relay_version_mismatch`).
