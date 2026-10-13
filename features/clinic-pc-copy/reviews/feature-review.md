# Feature Review: clinic-pc-copy (« PC de secours ») — whole feature

**Status:** COMPLETE
**Challenged:** Yes
**Date:** 2026-10-13
**Challenged Date:** 2026-10-13
**Parent Branch:** main (feature merged through PRs #83–#92; prod on `97d9bc9d`)
**Merge Base:** n/a — whole-feature review. Feature = 71 first-parent commits `b618a1d8..cc1bef44`
(`git log --first-parent --no-merges b618a1d8..HEAD`); main's `security-remediation` merges excluded.
**Files Reviewed:** 380 files touched by the feature (+40 562 / −591), of which 276 production files (~25 k lines);
generated migration designers/snapshots, lock files and PNGs excluded. Read in full from the working tree, not as a diff.
**Review method:** 7 parallel reviewers by RISK (copy integrity · the return · lease/fence + latency · devices and
carried forms · security · installer/updates/migrations · Device & UX + wording). Challenge: every item re-read in the
source by 4 skeptic passes plus the orchestrator (Findings 1–4, 8, 17, 18 re-traced directly), against `spec.md` and
`progress.md`'s deviations, with `git blame` for pre-existing code.

## Challenge Summary

| Metric | Count |
|--------|-------|
| Original findings | 57 (37 findings + 20 one-line items in old Finding 37) |
| Confirmed | 37 |
| Confirmed (adjusted) | 19 (12 lowered, 1 raised, 6 reworded) |
| Dismissed (false positive) | 1 |
| Dismissed (pre-existing) | 0 |
| **Final findings** | 56 (40 numbered findings; Finding 38 holds 17 one-line items) |

## Applied (2026-10-13) — the 8 critical findings

| # | Outcome | How (deviations 214–219 in `stories/progress.md`) |
|---|---|---|
| 1 | fixed — owner chose « numbered by the cloud at the return » | the cloud never waits; the PC numbers `AAAA-P…`, the return renumbers in issue order (the 90 s window could not work: the cloud cannot tell asleep from holding) |
| 2 | fixed | savepoint per record on refusal, refused records « À reprendre »; booking constraint deferrable, deferred in relay applies |
| 3 | fixed | every row of an overruled cut is listed |
| 4 | fixed | `ClinicChange.Automatic`; job changes never a conflict nor « À vérifier »; `MonthlyExpenseJob` off the PC. Reminders for a visit the PC cancelled can still go — residual, needs the fence |
| 5 | fixed | per-record decisions via `Owner` (patient = folder); PC deletions never cascade into cloud work |
| 6 | fixed (narrowed) | 60 s stall timeout on every download; the lease-loop and `PulseAsync` parts not done — the timeout removes the hang they guarded |
| 7 | fixed | files sent before the freeze, no 100 s ceiling, 60 s stall rule; POSTs 10 min |
| 8 | **declined by the owner** (2026-10-13) — fix written then removed; the password stays in `appsettings.Install.json` | — |
| 12 | fixed with 2 | deferred constraint in the PC's feed apply and re-seed |

Verified: unit suite 5 710 pass / 0 fail; migration applied to a scratch database (constraint `condeferrable = t`,
column default false), `verify-schema` clean but for the dev key-ring warning; the renumbering, deferred swap and
savepoint set-aside SQL run in psql; installer lint + ISCC compile green. Not exercised: a real cut on the rig.

**Dismissed:** old 37 « `AddRelayHandback.Down` / `AddRelayReturnBeforeImages.Down` drop data » — no code path runs a
`Down()` (no `IMigrator`, no `database update` in api/, deploy/, packaging/, CI); a rollback by `image_tag` only migrates
forward; ~50 other migrations' `Down()` drop tables the same way.

> ⚠️ **Common root of Findings 2, 4, 5, 12, 25** — the manual-takeover redesign (deviation 213, 2026-10-12) removed the
> cloud's silence fence. During a real cut the cloud keeps accepting writes (people outside, phones on 4G, the cloud's
> own jobs) while the cabinet writes on the PC. The return was built when the cloud fenced itself within 45 s and
> treats « changed on both sides » as rare (deviation 126); it is now the normal case. « The cloud's version wins, the
> PC's is listed » IS the owner's call (deviation 213) and is not challenged here — what fails is everything around it:
> one transaction for the whole cut, decisions per row instead of per record, and machine writes counting as conflicts.
>
> ⚠️ For `/apply-review-fixes`: do **not** revert to « the cabinet's version wins » — that rule was replaced on purpose.

## Findings

### Critical

### Finding 1 — A sleeping PC de secours blocks every note, devis and avoir on the cloud
- **Severity:** Critical · **Category:** Handoff UX / Latency · **Verdict:** Confirmed
- **File:** api/ClinicManagement.Domain/Services/ClinicWriteLease.cs · **Line:** 112 · **Anchor:** `ClinicWriteLease.PcMustConfirmNumbers`
- **Comment:** the PC's last ack said « armé », then its lid closes (sleep), power drops, or it loses Wi-Fi — none stand it
  down (only a service stop does). `MayBeArmed` (`ClinicRelay.cs:132`) has no time limit. Every numbered save on the cloud
  waits 3 s in `RelayPromiseBroker.PromiseAsync` holding the cabinet's change-cursor lock, then is refused « Le PC de
  secours ne répond plus — réessayez dans un instant » (`NumberPromises.cs:136`); retrying never helps; « Reprendre la
  main » refuses (cloud not locked, `ReclaimRelayCommand.cs:64`). The only exit is « Retirer » the PC. **Owner decision
  needed** (legal numbering). Recommended: the cloud waits only for a PC heard in the last ~90 s; a PC that takes over
  with an older last ack records notes/devis/avoirs without a number during that cut, numbered by the cloud at the return.
- **Challenge note:** deviation 213 kept D16 « unchanged »; it was written to stop a sleeping laptop locking the cloud and
  did not notice the same laptop still blocks billing.

### Finding 2 — One conflicting row blocks the whole return, forever
- **Severity:** Critical · **Category:** Data safety · **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Relay/Commands/HandBackRelayCommand.cs · **Line:** 108 · **Anchor:** `HandBackRelayCommandHandler.Handle`
- **Comment:** the whole cut is applied in ONE transaction; any DB error rolls everything back and the PC retries every
  ~30 s forever (`RelayHandback.cs:130-139`). Triggers: the PC cancels A and books B in the same slot while the cloud keeps
  A active (Finding 4) → `EX_Appointments_NoDoubleBooking` (non-deferrable; no `DEFERRABLE` anywhere); a swap of two
  visits in one `INSERT … ON CONFLICT`; a fiche re-saved on both sides → unique `(DentalRecordId, ToothNumber)`; a PC
  child under a parent the cloud deleted → FK. The cloud stays read-only for everyone, the cabinet's saves are refused
  during each attempt, and the only exit (« Reprendre la main ») leads to Finding 3. Fix: apply per record under a
  SAVEPOINT, list the PC's record « À vérifier » on a violation; make the exclusion constraint deferrable and defer it
  inside relay applies.
- **Challenge note:** deviation 126 accepted this as « rare » when the cloud still fenced itself; no longer true.

### Finding 3 — « Reprendre la main » throws away the detail of the cut
- **Severity:** Critical · **Category:** Data safety · **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Relay/Commands/ListOverruledCutCommand.cs · **Line:** 126 · **Anchor:** `ListOverruledCutCommandHandler.Lines`
- **Comment:** `Lines()` stores only rows that point to nothing else of the cut. A fiche on a patient also edited in the
  cut, its acts, teeth, ordonnance and files; a note's lines; a payment — dropped. The PC then forgets its log and
  re-seeds (`RelayHandback.cs:172-175`), deleting them locally: the cut's clinical and money detail is gone from both
  sides, and « À reprendre » cannot be re-entered from what is left. Fix: store every row (children nested under their
  root); never re-seed until all rows are stored on the cloud.

### Finding 4 — The cloud's own jobs make the cabinet's appointment work « lose » at the return
- **Severity:** Critical · **Category:** Data safety / Two writers · **Verdict:** Confirmed (adjusted — reframed)
- **File:** api/ClinicManagement.Infrastructure/Relay/RelayFence.cs · **Line:** 86 · **Anchor:** `ClinicWriteFence.RefusesAsync` (with `RelayHandbackPlanner.cs:150`)
- **Comment:** during a manual cut the cloud is not fenced, so `AppointmentProgressJob` moves every visit whose slot
  starts or ends during the cut. At the return every such visit is « changed on both sides » and keeps the cloud's
  version: the cabinet's cancellations, no-shows, moves and « terminé » for the day are not applied, only listed.
  `MonthlyExpenseJob` posts the loyer on both sides (the PC's row is new, so it is applied — la caisse counts it twice);
  `NotificationJob` reminds patients of visits the PC cancelled. Fix: skip these jobs on the cloud while an armed PC has
  been silent > ~60 s (they catch up by design), and never count a `job|…` actor's change as a conflict.
- **Challenge note:** « cloud wins » is the owner's rule (deviation 213) and stays; the defect is that a machine write
  counts as the cloud's work, and that two jobs run on both sides.

### Finding 5 — The return decides row by row, so one record ends up half cloud, half PC
- **Severity:** Critical · **Category:** Data safety · **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Relay/RelayHandbackPlanner.cs · **Line:** 150 · **Anchor:** `RelayHandbackPlanner.Plan`
- **Comment:** (a) the PC records a payment on devis T while the cloud amends T → T and its `Installment.AmountPaid` kept,
  the new `InstallmentPayment` applied → la caisse counts it, the devis does not. (b) Both sides re-save fiche F →
  `SetActs` mints new ids, so both sides' acts survive → doubled. (c) The PC deletes a patient while the cloud adds a fiche
  or file → the delete runs and `ON DELETE CASCADE` (`DentalRecordConfiguration.cs:109`, `PatientFileConfiguration.cs:79`)
  removes the cloud's new rows; before-images cover applied rows only and nothing reads them. Fix: decide per record
  (kept parent → keep and list its children); list, never run, a delete that cascades into cloud rows written after the
  base; give before-images a reader.

### Finding 6 — A stalled download can freeze the return forever; then « Reprendre la main » is ignored
- **Severity:** Critical · **Category:** Two writers · **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Relay/RelayCloudClient.cs · **Line:** 242 · **Anchor:** `RelayCloudClient.DownloadAsync`
- **Comment:** blob/snapshot bodies are read with `ResponseHeadersRead` + `CopyToAsync(stoppingToken)` — no stall timeout,
  no keepalive (`RelayInstallerDownloader` has one; these do not). The return starts only from the copy tick
  (`RelayFollower.cs:291`), so a hung read stops it; pulses keep saying « je tiens » → the cloud stays fenced, the strip
  says « Retour au cloud en cours — quelques secondes » forever, no « Retour bloqué » bell. A reclaim or retire is then
  ignored (`PulseAsync` drops the answer, `RelayFollower.cs:215-229`; the keeper reads only the state file) → the PC keeps
  numbering while the reclaimed cloud numbers without waiting → **duplicate legal numbers**. Trigger needs the server's
  close to be lost (long cut, box back on a new public IP). Fix: stall timeout on every body read; drive the holding step
  from the lease loop; act on `Reclaimed`/`Released` in `PulseAsync`.

### Finding 7 — One large file taken during a cut can stop the return on a slow line
- **Severity:** Critical · **Category:** Data safety / Latency · **Verdict:** Confirmed (adjusted — description corrected)
- **File:** api/ClinicManagement.Infrastructure/Relay/RelayHandback.cs · **Line:** 242 · **Anchor:** `RelayHandback.TryOnceAsync`
- **Comment:** saves are frozen (`BeginHandback`, line 101) BEFORE the cut's files are sent; each file is one unchunked
  PUT on an unconfigured client (100 s total, `RelayCloudClient.cs:141-152`). A file that needs > 100 s (a panoramic over
  ~10 MB on an ADSL uplink) fails every cycle: ~100 s frozen, ~30–40 s of work, forever; « Reprendre la main » also sends
  files first (line 161), so the cut is not even listed. Fix: send files before freezing; chunked resumable upload with an
  idle timeout; keep the same handback id until a definitive answer.
- **Challenge note:** files already on the cloud are not re-sent (`SendFilesAsync` 203-209) — several smaller files cost
  one long freeze, once; only a single file over the 100 s budget loops forever.

### Finding 8 — The PC's database password is readable by every Windows account on that PC
- **Severity:** Critical · **Category:** Security · **Verdict:** Confirmed (adjusted — origin noted)
- **File:** packaging/setup/clinic-setup.iss · **Line:** 546 · **Anchor:** `WriteInstallConfig`
- **Comment:** `appsettings.Install.json` holds `Password=` in clear in `{app}\api`, which keeps the inherited « Users:
  Read & Execute »; `HardenInstallDirectories` (line 931) secures `.local`, `Files`, `logs`, `Backups`, `pgdata` only. A
  secretary's account (or malware running as it) reads it and runs the bundled `psql` → every patient record. Fix: harden
  the config files (or `{app}\api`), or keep the password in the protected `.local` credentials and build the string at
  startup; `verify-schema` fails when a config holding a password is readable by Users.
- **Challenge note:** the hardening list predates the feature (LAN installer, `43fe6d57`); kept because this feature
  installs it on every cloud clinic's reception PC with the clinic's whole record behind it.

### Major

### Finding 9 — After a cloud restore, ordinary buttons can erase the only copy of the newer data
- **Severity:** Major · **Category:** Data safety · **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/Controllers/RelayLocalController.cs · **Line:** 132 · **Anchor:** `RelayLocalController.Erase`
- **Comment:** while a restore gap is pending: « Effacer la copie » checks only retired + `MayErase`
  (`RelayLocalEraser.cs:51`, no `RelayGap.Pending`); a reinstall always passes `--replace` (`clinic-setup.iss:1540`) and
  `pair-relay` checks only unreturned work (`PairRelayConsoleCommand.cs:83`); `relay_unknown` → `Released`
  (`RelayCloudClient.cs:367`) then the uninstaller erases (`RelayUninstaller.cs:100`). Fix: all four doors refuse while a
  gap is pending; a seeded PC told `relay_unknown` stops « gardé », never `Released`.

### Finding 10 — Retire / uninstall / « perdu ou volé » during a cut strands the cut's work; Retire needs no code
- **Severity:** Major · **Category:** Data safety / Security · **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Relay/Commands/RetireRelayCommand.cs · **Line:** 62 · **Anchor:** `RetireRelayCommandHandler.Handle`
- **Comment:** a retired PC returns at `RelayFollower.cs:105`, before the « À reprendre » branch (144); the keeper's
  `End()` marks the work unreturned, so erase and `pair-relay` refuse forever. `uninstall-relay` reports to the cloud first
  (`RelayUninstaller.cs:82`). Retire takes no step-up (`RelayController.cs:96-109`) while Reclaim and Lost do. Fix: while
  `PcHoldingSinceUtc` is set, these run a Reclaim first; uninstall checks unreturned work first; Retire requires step-up.

### Finding 11 — A reclaimed PC that needs an update never updates, so its cut is never listed
- **Severity:** Major · **Category:** Data safety · **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Relay/RelayFollower.cs · **Line:** 144 · **Anchor:** `RelayFollower.TickAsync`
- **Comment:** the overruled branch (144-148) returns before the updater (169-172); its calls are build-gated
  (`RelayFeedQueries.cs:23-25`) → `relay_version_mismatch` on every tick. Fix: step the updater first, as the gap branch
  does (152-157).

### Finding 12 — The copy can get stuck forever on a cancel-and-rebook or a move into a freed slot
- **Severity:** Major · **Category:** Data safety · **Verdict:** Confirmed (adjusted)
- **File:** api/ClinicManagement.Infrastructure/Persistence/ClinicRelayRowStore.cs · **Line:** 402 · **Anchor:** `ClinicRelayRowStore.UpsertAsync`
- **Comment:** feed rows come from `GROUP BY` with no `ORDER BY` (133-134); B upserted before the stale A hits the
  non-deferrable exclusion constraint → `ReseedNeeded` → the snapshot upserts by id and, if B's id sorts first, fails every
  5 min forever → never caught up, never armed. Fix: as Finding 2 (deferrable + deferred in relay applies), apply in seq order.
- **Challenge note:** the link to owner bug #5 is not shown by the code (Finding 18 is its proven cause).

### Finding 13 — A re-copy running at the takeover can delete what was just saved on the PC
- **Severity:** Major · **Category:** Data safety · **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Relay/RelayLease.cs · **Line:** 783 · **Anchor:** `RelayLeaseKeeper.TickAsync`
- **Comment:** the offer needs `RowsSeeded` (still true during a re-seed) and never asks whether an apply is running;
  `ReplaceAsync` overwrites every column, then deletes rows missing from the snapshot (`ClinicRelayRowStore.cs:313-322`),
  never re-checking holding. Narrow window (a full re-seed still running ~35–40 s after the cut). Fix: one gate shared by
  apply and takeover.

### Finding 14 — A save sent just before the cut is recorded twice
- **Severity:** Major · **Category:** Two writers · **Verdict:** Confirmed (adjusted — side claim corrected)
- **File:** web/lib/api/client.ts · **Line:** 984 · **Anchor:** `withWriteKey` / `unsettledWriteKeys`
- **Comment:** the unsettled `Idempotency-Key` lives in memory (« a page reload forgets »); `CarriedDraft` has no key; the
  planner pairs only on equal keys (`RelayHandbackPlanner.cs:86-110`) → re-press on the PC = two RDV / payments / fiches.
  Spec FR-6/EC-6 require the key to cross the switch (deviation 106 deferred it). Fix: carry unsettled keys in the draft;
  say « cet enregistrement a peut-être déjà été fait » until it lands.
- **Challenge note:** patient fields are NOT locked after a switch; but a restored `createdPatientId` the PC never
  received makes every save fail until the dialog is closed.

### Finding 15 — A failed update leaves the PC de secours off
- **Severity:** Major · **Category:** Ops · **Verdict:** Confirmed
- **File:** packaging/setup/clinic-setup.iss · **Line:** 1604 · **Anchor:** `CurStepChanged` / `DeinitializeSetup`
- **Comment:** `InstallFinished` is set at `ssDone` even when a post-install step exits early, so `DeinitializeSetup`
  (1568) never restarts DB/Web/API; `DeferredStartupService` fatal paths call `StopApplication()` (clean stop) and
  `sc failure` has no `failureflag`, so Windows never restarts it. During an update-before-return the cloud stays fenced
  (exit: « Reprendre la main »). Fix: finished only on success, else restart; `failureflag 1` + non-zero exit code.

### Finding 16 — Every update with a migration leaves a full backup copy on the PC, never pruned
- **Severity:** Major · **Category:** Ops · **Verdict:** Confirmed (adjusted)
- **File:** api/ClinicManagement.API/Startup/DeferredStartupService.cs · **Line:** 67 · **Anchor:** `DeferredStartupService.InitializeAsync`
- **Comment:** `TryBackupBeforeMigratingAsync` writes pg_dump + every file into `api\Backups`; the only pruner runs where
  `BacksUpItsOwnData` (false for ClinicRelay, `Program.cs:1332`). Disk < 5 GB → `DiskNearlyFull` → not « Prêt » → no
  takeover possible; then a failed space check stops the API (Finding 15). Fix: skip the files copy (or the backup) on a
  relay; if kept, DB-only, keep 1.
- **Challenge note:** not silent — a bell says « disque presque plein », but not that the cause is our backups folder,
  which the admin cannot open. Code predates the feature; the feature's automatic updates expose it.

### Finding 17 — « Effacer la copie » and uninstall leave the patient data on the disk
- **Severity:** Major · **Category:** Security / Data safety · **Verdict:** Confirmed
- **File:** packaging/setup/clinic-setup.iss · **Line:** 1720 · **Anchor:** `CurUninstallStepChanged`
- **Comment:** uninstall with « Effacer aussi la copie ? » = Oui (default and silent answer): `StopClinicServices` stops
  the database (984) before `uninstall-relay --erase`, which cannot reach PostgreSQL → nothing erased, services deleted,
  `pgdata` + `Files` left behind, cloud already told « retiré ». `RelayLocalEraser.cs:77` also never deletes
  `api\Backups\clinic-backup-*` while the card says « Copie effacée ». Fix: stop only API + Web before the verb; erase
  Backups and temp folders too.

### Finding 18 — Owner bug #5: a normal update reads « Éteint », then « Copie en retard », and rings the vendor
- **Severity:** Major · **Category:** Handoff UX · **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Relay/RelayFollower.cs · **Line:** 208 · **Anchor:** `RelayFollower.StandDownAsync(CancellationToken)`
- **Comment:** the updater stands down with `isUpdating:true`; the installer then stops the service and
  `RelayFeedJob.StopAsync` stands down again with `isUpdating:false` → the 20-min update allowance is lost → « Éteint »
  after 2 min; then « Copie en retard de N min » counted from before the download (`ClinicRelayHealth.cs:146`), with bell
  and vendor e-mail. Fix: pass `isUpdating: null` there; keep « mise à jour » until the first catch-up.

### Finding 19 — Every deploy reinstalls every PC de secours (10–25 min unprotected), with an installer never run in CI
- **Severity:** Major · **Category:** Ops / Latency · **Verdict:** Confirmed
- **File:** .github/workflows/deploy-hosted.yml · **Line:** 116 · **Anchor:** `jobs.relay-installer` (with `RelayServices.cs:17` `RelayBuildInfo.Compute`)
- **Comment:** the build identity carries the commit SHA, compared by exact string; every deploy un-arms every PC and makes
  it download ~360 MB and run the installer as SYSTEM. CI only compiles the .iss against stubs (`ci.yml:126-128`); no
  staged rollout, no per-PC fallback. Fix: identity = newest migration + a relay protocol version; a Windows CI job that
  installs the last release, updates in place, checks `/health` + a heartbeat; keep and re-run the previous installer.

### Finding 20 — The relay token's rate limit is per IP, and a 429 counts as « internet coupé »
- **Severity:** Major · **Category:** Security / Ops · **Verdict:** Confirmed (adjusted)
- **File:** api/ClinicManagement.API/Startup/RateLimiting.cs · **Line:** 285 · **Anchor:** `RateLimiting.AddConfiguredRateLimiter` (`RelayTokenPolicy`)
- **Comment:** `relay-token:{ClientIp}` 20 / 600 s, shared by 6 anonymous routes; `Classify` maps 429 → `Unreachable`;
  the lease counts every non-released result as silence (`RelayLease.cs:319`). One client on the same public IP (guest
  Wi-Fi, CGNAT) can keep a PC silent: copy stopped, « Éteint », and Finding 1's refusals while it stays armed. Fix:
  partition by relay id; treat 4xx/5xx/429 as « the cloud answered »; share one token between the two clients.
- **Challenge note:** the « two writers » angle was weak (the false offer shows only on the PC's own pages; a cut's end is
  delayed ≤ 10 min, not blocked) — category changed.

### Finding 21 — One missed « do you hold? » answer throws a device off the PC (screen and typing lost)
- **Severity:** Major · **Category:** Handoff UX · **Verdict:** Confirmed
- **File:** desktop/ClinicManagement.DesktopShell/MainWindow.xaml.cs · **Line:** 746 · **Anchor:** `MainWindow.StartRelayTimer` (Android `MainActivity.kt:655`)
- **Comment:** `HoldingAsync(pc) != true` reads null (5 s timeout, non-2xx, exception) as « let go » → cloud (fails) →
  cut panel → PC home page. Fix: leave only on explicit `false`; re-ask on null within ~3 s.

### Finding 22 — A 3–10 s Wi-Fi blip shows a false « Internet coupé », then sends the app to the home page
- **Severity:** Major · **Category:** Handoff UX · **Verdict:** Confirmed
- **File:** desktop/ClinicManagement.DesktopShell/MainWindow.xaml.cs · **Line:** 903 · **Anchor:** `MainWindow.AnswerRelaySwitchAsync` (Android `MainActivity.kt:514`)
- **Comment:** one 3 s cloud check during the blip → `ShowCutAsync` hides the page; 10 s later `Navigate(BaseUrl)` (756)
  loads the home page, not the current URL → uncarried forms lost (ordonnance, invoice, payment, an upload). The
  2026-10-12 « offline blip » test emulated offline inside the page only, so it could not see this. Fix: confirm with a
  second check ~3 s later; show the still-loaded page again; reload the current URL, never `BaseUrl`.

### Finding 23 — On a shared reception PC, the next person lands on the PC de secours as the first person of the day
- **Severity:** Major · **Category:** Security / Handoff UX · **Verdict:** Confirmed
- **File:** web/components/relay/relay-switch-watch.tsx · **Line:** 92 · **Anchor:** `prepareIfDue`
- **Comment:** `apexa.relay.preparedAt` is one value per origin with a 20 h gate, never cleared at sign-out → B lands on
  the PC signed in as A, with A's role; B's carried draft is dropped (`carried-draft.ts:85`). Fix: key it by user; prepare
  again on a change; delete the PC session cookie at sign-out.

### Finding 24 — When the cloud API is down (502/503), devices get no button
- **Severity:** Major · **Category:** Handoff UX · **Verdict:** Confirmed
- **File:** desktop/ClinicManagement.DesktopShell/RelaySwitch.cs · **Line:** 143 · **Anchor:** `RelaySwitch.CloudAnswersAsync` (Android `RelaySwitch.kt:113`, `web/lib/api/client.ts:740-749`)
- **Comment:** the PC counts 502/503/504/429 as silence and offers; devices count any HTTP answer as « the cloud answers »
  → every save refused, no strip, no button; only the PC's screen offers, worded « Internet coupé ». Fix: one definition
  everywhere, or let the PC's `canTakeOver` decide; word it « Le cloud ne répond pas ».

### Finding 25 — An Android phone on 4G never sees the cut and keeps writing to the cloud
- **Severity:** Major · **Category:** Two writers · **Verdict:** Confirmed (still plausible — never run on a phone)
- **File:** mobile/android/app/src/main/java/com/clinicmanagement/shell/RelaySwitch.kt · **Line:** 235 · **Anchor:** `RelaySwitch.holding`
- **Comment:** no `TRANSPORT_WIFI` binding anywhere; when Android moves to mobile data the phone never fails and cannot
  reach the PC; the cloud is not fenced during a manual cut → conflicts at the return, and Finding 2's stuck return on a
  booking clash. Fix: bind relay traffic to Wi-Fi; while a PC is prepared, ask it « do you hold? » even when the cloud answers.

### Finding 26 — Owner bug #7: the PC can advertise only VPN / virtual addresses, and discovery cannot recover
- **Severity:** Major · **Category:** Ops · **Verdict:** Confirmed (adjusted — raised from Minor)
- **File:** api/ClinicManagement.Infrastructure/Relay/RelayHostFacts.cs · **Line:** 49 · **Anchor:** `RelayHostFacts.PreferNetworkAddresses`
- **Comment:** only gateway-less adapters are pushed back; a VPN/TAP/Hyper-V adapter with a gateway is kept, adapter type
  never checked; the list is cached 5 min (`RelayFeedJob.cs:36,407`); both shells ignore the UDP reply's source address
  (desktop `RelayDiscovery.cs:101-102`, Android `RelayDiscovery.kt:65-66`). Fix: put first the local address the OS uses
  to reach the cloud; drop tunnel/virtual adapters; don't cache a gateway-less list; add the reply's source address.
- **Challenge note:** raised because a real cabinet could not use its PC de secours; Bouka's exact adapters are unseen.

### Minor

### Finding 27 — A carried patient form can overwrite a colleague's antécédents
- **Severity:** Minor · **Category:** Data safety · **Verdict:** Confirmed (adjusted — was Major)
- **File:** web/components/edit-patient-dialog.tsx · **Line:** 503 · **Anchor:** `EditPatientDialog` restore (read suppressed at 746, save loop 1157-1175)
- **Comment:** after a restore the antécédents read is skipped, « Recharger » reconciles patient sections only, and the save
  re-PUTs every entry with no version. Fix: never skip the read after a restore; send only edited entries.
- **Challenge note:** the unversioned re-PUT predates the feature (`0e4d343c`, any long-open dialog); allergies are a
  versioned patient field, not an antécédent; the false « aucun antécédent » is display-only.

### Finding 28 — A flaky line delays the return and rings no bell
- **Severity:** Minor · **Category:** Handoff UX · **Verdict:** Confirmed (adjusted — was Major)
- **File:** api/ClinicManagement.Infrastructure/Relay/RelayHandback.cs · **Line:** 66 · **Anchor:** `RelayHandback.Unanswered`
- **Comment:** one miss resets the 30 s window; `ReturnFirstTriedAtUtc` is set only once a return starts, so a return that
  never starts never rings « Retour bloqué ». Fix: tolerate one miss; count « stuck » from the first answer while holding.
- **Challenge note:** with random loss four answers in a row come within minutes (~1.5 min at 25 %); delay, not a data risk.

### Finding 29 — « Retour au cloud en cours — quelques secondes » shows even when the return is blocked
- **Severity:** Minor · **Category:** Handoff UX · **Verdict:** Confirmed (adjusted — was Major; line corrected)
- **File:** api/ClinicManagement.Application/Features/Relay/Queries/GetRelayBannerQuery.cs · **Line:** 95 · **Anchor:** `RelayBanners.OnTheCloud`
- **Comment:** chosen from heartbeat recency only; `ReturnStuckSinceUtc` and `IsUpdating` are never consulted. Fix:
  choose the text from those facts; promise « quelques secondes » only when a return can happen now.
- **Challenge note:** wording only, clears by itself; shells do not move to a PC that refuses (they move only on
  `holding == true`); the strip keeps a stale line only while reads keep failing.

### Finding 30 — When only this device lost its connection, the panel promises the PC « dans quelques secondes » forever
- **Severity:** Minor · **Category:** Handoff UX · **Verdict:** Confirmed (adjusted — was Major)
- **File:** desktop/ClinicManagement.DesktopShell/RelaySwitch.cs · **Line:** 132 · **Anchor:** `RelaySwitch.WaitingLine` (Android `RelaySwitch.kt:102`)
- **Comment:** `/holding` says nothing about whether the PC reaches the cloud, so after the countdown the line repeats
  forever. Fix: add that fact and say « le problème vient de cet appareil — vérifiez sa connexion »; name a silent PC.

### Finding 31 — After the return, saves on the PC are refused for ~20 s with a sentence that contradicts the strip
- **Severity:** Minor · **Category:** Handoff UX · **Verdict:** Confirmed (adjusted — was Major)
- **File:** api/ClinicManagement.Application/Features/Relay/RelayRefusals.cs · **Line:** 148 · **Anchor:** `RelayRefusals.ForPcNotHolding` (`MainWindow.xaml.cs:580`)
- **Comment:** « …n'accepte des enregistrements que pendant une coupure » after the return, and before the button is
  pressed; the shell re-checks only every 20 s. Fix: sentences that say what happened; on `relay_standby` ask the shell to
  switch at once.
- **Challenge note:** ~20–30 s, clears by itself; a plain browser not switching is an accepted limit (spec).

### Finding 32 — Without the Windows/Android app, nothing points staff to the PC at the moment of the cut
- **Severity:** Minor · **Category:** Handoff UX · **Verdict:** Confirmed (adjusted — was Major)
- **File:** web/components/relay/relay-banner.tsx · **Line:** 169 · **Anchor:** `refresh` (with `clinic-settings.tsx:1518`)
- **Comment:** status 0 only notifies shell listeners; the PC's link/QR is on the admin-only card. Fix: on status 0
  without a shell show « Ouvrir le PC de secours » from the address stored while online; show the link to every role.
- **Challenge note:** AC-3.7 accepts « Safari gets the browser behaviour »; admins can prepare tablets in advance.

### Finding 33 — The return's bell and « Vu » hide that the cabinet's typing was not saved
- **Severity:** Minor · **Category:** Handoff UX · **Verdict:** Confirmed (adjusted — was Major)
- **File:** api/ClinicManagement.Application/Features/Relay/Queries/GetRelayReviewItemsQuery.cs · **Line:** 50 · **Anchor:** `RelayReviewLabels.BellMessage`
- **Comment:** the bell counts BothChanged/KeptAfterRestore as « modifications faites dans le cloud juste avant la
  coupure »; the action is « Vu ». Fix: count them apart, own sentence, verb « Recopié ».
- **Challenge note:** each list row's label does say the cabinet's version was not applied; the bell's summary and the
  verb mislead (and matter more given Finding 4's volume).

### Finding 34 — The take-over button needs no session or origin check, and the PC's ports are open on every network
- **Severity:** Minor · **Category:** Security · **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/Controllers/RelayLocalController.cs · **Line:** 75 · **Anchor:** `RelayLocalController.TakeOver`
- **Comment:** `[AllowAnonymous]`, no rate limit, no Origin check (none in the API); acts only while offered. Firewall
  rules have no `profile=`/`remoteip=` (`clinic-setup.iss:857-874`). Fix: Origin check + rate limit (or a session);
  `profile=private,domain remoteip=localsubnet`.

### Finding 35 — The offer can appear ~10–15 s after waking, and on answers that are not silence
- **Severity:** Minor · **Category:** Handoff UX · **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Relay/RelayLease.cs · **Line:** 248 · **Anchor:** `RelayLease.SilentLongEnoughAwake`
- **Comment:** real threshold 20 − 10 s = two failures; 4xx/5xx/429 count as silence (319); doc still says « 45 s more ».
  Fix: ≥ 3 failures over ≥ 20 s; answered = not silent.

### Finding 36 — The self-update runs as SYSTEM on a hash sent by the same server
- **Severity:** Minor · **Category:** Security · **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Relay/RelayUpdater.cs · **Line:** 257 · **Anchor:** `RelayUpdater.LaunchAsync` (desktop `RelayInstaller.cs:198`)
- **Comment:** `X-Content-SHA256` comes with the bytes; no signature anywhere. Needs write access to the VPS or the API.
  Fix: sign build + SHA-256 offline with the vendor key (`RelayPromotionCode` pattern) and verify before running.

### Finding 37 — A tablet ended on the cloud keeps a 30-day session on the PC
- **Severity:** Minor · **Category:** Security · **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Auth/Commands/TradeRelayAssertionCommand.cs · **Line:** 74 · **Anchor:** `TradeRelayAssertionCommandHandler.Handle`
- **Comment:** 26 h replayable ticket → new 30-day trusted family per trade; « Mes appareils » ends one session without
  bumping `TokenVersion`, and families are not copied. « Déconnecter partout » / password change / disable do reach the
  PC. Fix: single-use ticket, PC session ≤ ticket life, end the previous family on each trade.

### Finding 38 — Smaller defects (each Confirmed unless noted)
- **Severity:** Minor · **Category:** mixed
- `api/ClinicManagement.Infrastructure/Persistence/ClinicRelayRowStore.cs:327` — handback/gap rows' blob columns are not
  checked with `IsClinicKey`; `relay/blob` serves any key a row names (some keys are guessable, e.g. `clinics/{id}/logo`).
- `api/ClinicManagement.Application/Features/Relay/Commands/DeclareRelayLostCommand.cs:67` — acts on the newest row: with
  a new PC mid-setup it refuses (no account reset), and once paired retires the healthy new PC.
- `api/ClinicManagement.Application/Features/Relay/RelayBrowserAccess.cs:40` — the root CA is fetched in clear with no
  fingerprint shown to compare (the cloud already stores it).
- `api/ClinicManagement.API/Controllers/RelayPeerController.cs:322` (adjusted) — 512 MB handback/overruled/gap bodies,
  bound after the relay-token check (not « before any check »); `:67` — the ~360 MB installer is served by the shared API
  to anyone, 240×/10 min/IP.
- `api/ClinicManagement.Domain/Entities/ClinicRelay.cs:558` — `IsReturnedCut` compares PC wall-clock times; a clock step
  back between two cuts ignores the second (rare: D20b corrects only > 30 s).
- `api/ClinicManagement.Application/Features/Relay/Commands/HandBackRelayCommand.cs:132` — after a lost answer the next
  attempt skips a newer PC edit to a row already listed and re-adds the whole cut's journal.
- `api/ClinicManagement.Infrastructure/Persistence/ClinicChangeCapture.cs:176` — a numbering retry in a PC cabinet shows
  EF's English « already being tracked » (rolled-back `ClinicChange` still tracked).
- `api/ClinicManagement.Application/Features/Relay/Commands/ReportRelayUninstalledCommand.cs:50` — no `DropCursorAsync`:
  the cabinet keeps logging changes nobody prunes; « Retirer » then refuses.
- `api/ClinicManagement.Infrastructure/Repositories/NotificationRepository.cs:139` — the minutely purge bypasses the change
  log → the hourly digest re-copies the whole Notifications table to every PC.
- `api/ClinicManagement.Infrastructure/Relay/RelayFollower.cs:550` — files deleted on the cloud are never deleted on the PC.
- `desktop/ClinicManagement.DesktopShell/MainWindow.xaml.cs:1447` — the PC's refusal reason is dropped and the failure
  line is overwritten within 3 s.
- `mobile/android/app/src/main/java/com/clinicmanagement/shell/MainActivity.kt:603` — the first cut frame wraps the line
  in the old « Vérifiez que le serveur est allumé » template; every 10 s a raw `net::ERR_*` replaces the panel and hides
  the button.
- `api/ClinicManagement.Application/Features/Relay/RelayLabels.cs:86` (adjusted) — on the PC during a cut the card shows
  « l'on n'y enregistre rien » with a green check; (`:45` « Éteint depuis » is the spec's wording — suggestion only).
- `api/ClinicManagement.Application/Features/Relay/RelayAlertRules.cs:85` — during a restore gap the bell/card say « ne
  répond plus » and offer « Reprendre la main » while the PC is sending its data.
- `web/components/relay/relay-banner.tsx:66` (adjusted — was cited in the query) — on a phone the strip hides « lecture
  seule »; the spec asked for the detail on a second line (deviation 164 gives no reason).
- `web/app/settings/pc-de-secours/page.tsx:272 / 147 / 93 / 183` — label clamped at 320 px; stale rows relabelled during a
  tab switch; `?page=` written but never read; « Imprimer » prints 25 rows + the pager.
- `.github/workflows/deploy-hosted.yml:307` — a rollback older than the 3 kept installer manifests leaves every PC
  un-armed until the next deploy.

### Suggestion

### Finding 39 — Cost on the cloud for every clinic, with or without a PC
- **Severity:** Suggestion · **Category:** Latency · **Verdict:** Confirmed (adjusted)
- **File:** api/ClinicManagement.Infrastructure/Persistence/ClinicChangeCapture.cs · **Line:** 139 · **Anchor:** `ClinicChangeCapture.AppendAsync`
- **Comment:** per save, always 6 extra statements (idempotency DELETE + INSERT + UPDATE with up to 256 KB, two reads of
  `ClinicRelays`, a cursor UPDATE that matches nothing), up to ~9 (BEGIN/COMMIT, a third read on numbered saves, parent
  lookups). With a PC, the cursor and audit locks are held through the up-to-3 s promise wait. Every tab of every clinic
  polls `/relay/banner` every 20 s. Fix: read « has a relay » once per request and skip capture/planning/idempotency
  body when none; take the cursor lock after the promise; poll the banner only for clinics with a PC.
- **Challenge note:** the device-watch loop runs only in the apps, every 600 s without a PC (not 30 s), and is still
  live during a restore gap — not dead code.

### Finding 40 — Docs and constants still describe the retired silence fence
- **Severity:** Suggestion · **Category:** Code quality · **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Domain/Services/ClinicWriteLease.cs · **Line:** 16 · **Anchor:** `ClinicWriteLease`
- **Comment:** `CloudFencesAfter`, `PcTakesOverAfter`, `ClockStepTolerance` unused; four docs (class, `IsCloudFenced`,
  `LockedSinceUtc`, the gate middleware) describe the old design. Delete and rewrite.

## Latency — from the code (best / typical / worst)

| Step | Best | Typical | Worst | Where |
|---|---|---|---|---|
| Wi-Fi cut → button visible | 10 s | ~35 s | ~50 s (the countdown says 25 s) | `RelayLease.cs:125,254`, `RelayFeedJob.cs:31,33` |
| Press → working on the PC | ~3 s | ~5 s | ~25 s | `RelayLocalController.cs:88` |
| Internet back → cloud accepts saves | ~31 s | ~45 s | ~75 s + cut files (Finding 7) | `RelayHandback.cs:28` |
| Internet back → every device back | ~31 s | ~55 s | ~95 s | `MainWindow.xaml.cs:580`, `relay-banner.tsx:114` |
| Numbered cloud save while the PC sleeps | 3 s then refused, every time | | | Finding 1 |
| Each deploy, per cabinet | ~10 min | ~15 min | 25 min+ unprotected | Finding 19 |
| Extra per save, every clinic | 6–9 statements (~5–15 ms) | | | Finding 39 |

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 8 |
| Major | 18 |
| Minor | 28 (11 findings + 17 one-line items in Finding 38) |
| Suggestion | 2 |
| **Total** | 56 |
