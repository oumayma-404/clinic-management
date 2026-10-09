# Handoff — PC de secours (`clinic-pc-copy`)

**Date:** 2026-10-09 (session 12) · **Overall:** ~78 % · **Part 1 (La copie):** done in code (owed: the Windows
rehearsal and the first CI run — both need the owner's OK) · **Part 2 (La relève):** ~95 % — code complete (lease 1, 2, 2b, « Reprendre
la main », device reports, D17, D16, **the return (D18) complete**, the PC's clock and Windows Update (D20b), the cut's strip on every screen (D20), the change log pruned (D27), the cut test (D26), the card's cut states) · **all scratch servers stopped**

## Pick up here

1. Open the session **in the worktree**: `C:\Users\Oumayma Benkhalifa\Desktop\clinic-management\.claude\worktrees\clinic-pc-copy`
   (branch `feature/clinic-pc-copy`, tree clean, **every commit local — never pushed; never push or deploy without the
   owner's OK**).
2. Read this file, then `progress.md` (part status, deviations 1–188, verification log). Plan `../plan.md` (Part 2's
   « Return » bullet, D18), spec `../spec.md` (US-5, AC-5.4, AC-5.6, AC-5.8, US-7 AC-7.3–7.6, EC-11, EC-15, EC-16,
   AC-9.4), blueprint `../blueprint.md`.
3. **Next: Part 3 slice c — carried forms** (D23: `carryDraft` / `takeCarriedDraft`, `useCarriedDraft` on the fiche,
   the RDV, the patient and the devis; the first save after a switch refused once, then reconciled through
   « Recharger »). Slices a (D22 server), b1 (Windows, shell 1.6) and b2 (Android, shell 1.3.0) are done, deviations
   180–188; the window switch itself is owed to the Windows rehearsal and to a phone. Still owed with the owner's OK:
   the first GitHub run of `relay-roundtrip` and the Windows rehearsal.
4. The owner said « when you complete, start the next step right away »: after each sub-step's local commit and short
   report, go straight on to the next one.

## Done in session 11–12 — D18b3b, a restored cloud's gap (AC-9.4, EC-21)

The cloud keeps a restore mark on its change cursor (`Epoch`/`EpochFromSeq`, moved at startup and on each heartbeat);
a PC following another history (`FollowedEpoch`) keeps the cloud read-only for the cabinet (423 `relay_restoring`,
15 min cap) until `RelayGap` has sent what the cloud lacks (digests → per-row hashes → rows, files first,
`POST relay/gap`, once per `GapId`). Rows the cloud changed since its restore keep the cloud's version
(`KeptAfterRestore`, listed). During a cut the gap goes before the handback. The vendor gets `CloudRestored`. Rig: real
dump/restore into a new DB, 8 rows back 3 s after the restored cloud started, then the cut, numbers continuous.
Details: deviations 147–155.

## The next sub-step — the return, slice b (D18b)

**Done in b1 (session 11, deviations 129–133):** items 2, 3, 4 below and the review bell row + API of item 1
(`GET relay/review-items`, `POST relay/review-items/{id}/seen`, `RelayAlert.ToReview`).
**Done in b2 (session 11, deviations 135–142):** the screen of item 1 and item 7 — `/settings/pc-de-secours`
« Retours du PC de secours » (« À vérifier » / « À reprendre », « Vu » / « Repris », printable), two count links on the
card, an overruled PC sends its cut to `POST relay/handback/overruled` and re-copies, the numbered paper flagged, the
« À reprendre » bell row. Also fixed `useUrlFilterSeed` (a `<Link>` with a query string landed on the defaults).
**Done in b3a (deviations 143–146):** item 6 (the PC runs the agenda's progress and the monthly dépenses while it
holds — `RunsCutJobs`) and item 5 (EC-11: fetch while working, then saves refused and the update, the new build returns at
once). **Done in b3b (deviations 147–155):** item 8 — the return (D18) is complete.

What slice a left, each named in deviation 128 (and 121, 124, 88, 91):
1. **« Modifications à vérifier »** — the rows are stored (`RelayReviewItems`, AC-5.6): the admins' bell row « N
   modifications faites dans le cloud juste avant la coupure sont à vérifier », a list screen (cards on a phone, table
   from desk width, both versions, the cloud author, « Vu » → `RelayReviewItem.MarkReviewed`), its read + write endpoints.
2. **AC-5.8** — cloud screens open during the cut refresh by themselves after the return: the return writes rows in raw
   SQL and the relay area broadcasts nothing, so phase 2 must send one realtime refresh per resource the cut touched.
3. **AC-5.4 / EC-16** — reminders for visits booked or changed during the cut: at most one per visit, none for a visit
   already passed (the PC's `Notification` rows arrive with the return; the cloud's dispatcher sends them once released).
   **Google catch-up**: appointments the cut created or moved reach Google Agenda after phase 2.
4. **EC-15** — the 7-day subscription cap on the PC during a cut.
5. **EC-11** — the PC updates itself before the return when the cloud runs another build (today: blocked, reported stuck).
6. **Deviation 88** — the PC runs the clinic jobs while it holds (agenda progress, monthly dépenses…), and not otherwise.
7. **« À reprendre »** (US-7, deviation 91) — a PC overruled by « Reprendre la main » keeps its cut's log: list it for
   re-entry (AC-7.3 → 7.6), flag the documents it numbered (AC-7.5).
8. **AC-9.4** — a restored cloud's gap travels back through the same path.

Also still open in Part 2 after the return: (D26 done) the CI cut
test (D26); the screens step (card states « en charge », « repris par les appareils »).

## Done in session 11 — the return, slice a (D18, US-5, FR-6, FR-11)

While it holds, the PC logs its own changes (`ClinicChangeCapture`, cursor created on demand). After 2 min of answered
heartbeats (`RelayHandback`): saves refused (« Retour au cloud en cours », AC-5.2), every save past the gate waited for
under the cursor's row lock, the cut read (log, rows, journal, sign-in traces, spent codes), missing files sent, then
`POST relay/handback` — **phase 1**: the cloud applies it in one transaction (`HandBackRelayCommand`,
`RelayHandbackPlanner`, `ClinicRelayRowStore.Handback.cs`) and **stays fenced**; the PC drops its log and stops holding;
its next heartbeat (`ReturnedHandbackId`) is **phase 2**: the cloud takes the saves back and journals « retour ».
Decisions: the cabinet's version wins and the pair is listed; FR-11 tables never taken (traces merged); D17 duplicate
dropped unless pointed to; cloud-only changes listed. Lost answer → same id for 30 s, then a new one. Stuck 15 min →
bell `ReturnStuck` + vendor e-mail. Live on the rig: cloud edit lost at the cut, PC took over in 87 s, 7 changes on the
PC, return 2 min 11 s after the cloud came back, cabinet's version kept, note 2026-0793 continuous, 1 review line,
journal « Via PC de secours », PC armed and caught up. Details: deviations 115–128.

## Done earlier — numbers confirmed by the PC (D16) and idempotency (D17)

**D16 (as built):** no numbering path was touched — the context collects the numbers a save assigns
(`NumberedDocuments`), plans the promises after the write, and `NumberPromiseTransactionInterceptor` confirms them just
before the commit through a long poll the PC keeps open (`POST /api/relay/promises`, `IRelayPromiseBroker`). Not
confirmed in 3 s → 423 `relay_unconfirmed`, nothing saved. The PC keeps each promise (`RelayNumberPromises`) and numbers
after it. Live: kept in 21 ms; killed PC → refused in 3,2 s; floor → 0791 after a promised 0790; stood down → not asked.
The key→number reuse lands with Part 3 (deviation 112). Details: deviations 109–114.

**D17 — the original brief, for reference (FR-3, FR-6, EC-6, EC-22):** a note, devis or avoir number is final on the cloud only once the PC de secours holds
it, and work saved once is never recorded twice — including a save whose answer was lost at the cut and pressed again.

**Plan's own words** (`../plan.md` D16, D17, the « Integrity » bullet, files rows 82 / 108 / 114):

1. **D16 — one `INumberPromise` service** used by the four numbering paths (`IssueInvoiceCommand`,
   `BillDentalRecordCommand`, `DevisNumbering`, `CreateCreditNoteCommand`; a derived guard that every numbering path uses
   it). The cloud assigns the number, writes the rows (a unique-index collision is caught *before* any promise), sends a
   « promise » to the PC, waits ≤ 3 s (timed per save), then commits; no answer → rollback, 423 `relay_unconfirmed` with
   FR-3's sentence, the form stays open. A PC that stood down (AC-6.1) or no PC at all is not waited for. The promise
   carries the save's `Idempotency-Key`; the PC stores `key → number` and numbers after max(committed, promised) — a
   re-press of a promised key reuses that key's number.
   ⚠️ The PC has **no push channel** from the cloud today (deviation 2: heartbeat + pull). D16 names the clinic hub
   (`InvokeAsync<T>` client result) — that means a SignalR client on the PC under its relay token, or another channel
   with a ≤ 3 s round trip. Decide it first and record it as a deviation if it differs.
2. **D17 — `Idempotency-Key` end to end**: generated per submit in `web/lib/api/client.ts` (carried across a retry and,
   in Part 3, a switch); an `IdempotencyMiddleware` stores processed keys per clinic for 48 h and replays the stored
   answer; the keys replicate to the PC (they are rows). The return's « drop only when nothing points to it, else list »
   rule belongs to the return step (D18) — this step only has to make every web write carry and honour a key.
3. Guards: every numbering path uses `INumberPromise`; every web write sends `Idempotency-Key`.

## Done in session 10 — idempotency (D17, FR-6, EC-6)

Every write from `client.ts` carries an `Idempotency-Key` (`withWriteKey`; an identical write whose answer never came
back reuses its key for 10 min). `IdempotencyMiddleware` (before the lease / subscription gates) claims the key, keeps a
2xx JSON answer 48 h and replays it; in-flight → 409, wrong door → 422; a refusal frees the key. The store is per side
(raw SQL, never copied); the key is stamped on every `ClinicChange`, which is what D18 will compare. Live: API 7/7,
browser 8/8 (answer lost → re-press → one dépense). Details: deviations 105–108.

## Done in session 10 — device reports (AC-6.2, EC-17, EC-23)

The cabinet's Windows (1.5) and Android (1.2.0) apps unlock a silent PC: the web loop (`relay-device-watch.tsx`, inert
without the bridge) asks `GET /api/relay/devices/target` every 30 s; while the cloud is locked for a PC that said
nothing, from the cabinet's line, it gets the PC's address / port / fingerprint, the shell's `relayProbe` tries it
through the pinned certificate and returns its own gateways, and `POST /api/relay/devices/report` counts it only from the
cabinet's line **and** box. Two « PC no » 30 s apart, none reaching the PC → a reclaim journalled `job|relay-devices`.
Live: 22-check pass ALL CLEAR (PC hard-killed → locked → mobile data / guest box not counted → unlocked in 30 s → saves
201 → PC back armed). ⚠️ Owed to the screens step: the card after an automatic unlock (deviation 104). Details:
deviations 97–104.

## Done in session 10 — « Reprendre la main » (US-7, D19) + AC-6.4's bell row

`POST /api/relay/reclaim` (admin + code) frees a locked cabinet; a PC whose takeover predates it is overruled when it
reconnects (stops its copy, keeps the cut's work — « À reprendre » comes with the return). Card: lock line, the button,
AC-7.1's warning (also on retire / lost while locked). Bell: « Le PC de secours ne répond plus depuis 10:42 ». Live:
both scenarios on the rig + a 16-check browser pass. ⚠️ Owed to the screens step: while the PC holds the saves, the card's
state chip still reads « Copie en retard » (deviation 96). Details: deviations 90–96.

## Done in session 10 — slice 2b (the safety nets)

D15's net in the change capture on both sides; the agenda job, the monthly dépenses, the reminder outbox and the two
startup backfills skip a fenced cabinet before acting. Live: a fenced cabinet's visit was left alone for two ticks and
moved once the PC was back; the cloud restarted cleanly during a fence. Details: deviations 84–89.

## Done earlier in session 10 — the PC takes over (lease slice 2)

Live on the scratch rig: the cloud stopped → the PC took over **91 s** after its last ack; saves on the PC 201, FR-5
actions 423 `online_only`; the cloud back → refused the cabinet's saves with « …depuis 14:00… » (`clinic_on_relay`),
sign-in still open; the PC restarted and was still in charge; « Retirer » during the cut freed the cloud, and the PC
kept the cut's work and refused to erase it or re-pair over it. ⚠️ **Interim**: once released, the cut's work stays on
the PC, readable by admins, until « À reprendre » / the return exist. Details: deviations 71–83.

## Then, in order

| # | Step | Owner's OK |
|---|---|---|
| 3 | First `deploy-hosted` run with the new `relay-installer` job (builds the real installer on Windows, publishes it to `deploy/updates/relay/` before the swap) — never run yet | **yes** (VPS deploy) |
| 4 | Windows rehearsal of the one-click install + a self-update (the SYSTEM task, the real installer replacing the services), on a real PC | **yes** (UAC + 3 services) |
| 5 | Owed checks: console column live · « perdu ou volé » for real (resets the test accounts) · A2 (non-admin session opened before a retire → 401) unit-tested only · CI `relay-copy` job not written | console/CI: push needs OK |
| 6 | Part 2 « La relève » — lease 1, 2, 2b, « Reprendre la main », device reports, D17, D16 and the return (slice a) done; next: D18b, then the clock (D20b), banners (D20), pruning (D27), the CI cut test (D26), screens | — |
| 7 | Part 3 « Les appareils suivent » (devices switch, pinned certificate, carried forms) | — |

## Commits on the branch (oldest → newest, all local)

| Commit | What |
|---|---|
| `ee88ed66` | spec, challenged plan, blueprint |
| `11470cfe` | kind `ClinicRelay`, pairing, change log, cloud feed, standby gate, `pair-relay` verb |
| `e32bfc21` · `95fddeb0` | PC follows the cloud (`RelayFeedJob`/`RelayFollower`) · 3 rehearsal defects |
| `ff92fac8` | « Paramètres → PC de secours » card |
| `9bfa9f94` | admin bell (`StaffNotification.TargetRole`, `RelayWatchJob`, `Stopped` state) |
| `e484d4bb` | vendor console « PC de secours » column (**never looked at live**) |
| `56307621` | vendor alert e-mails (`RelayIncidents`, `VendorAlertRecipients`) |
| `3ef27d40` | « Déclarer perdu ou volé » |
| `1d3ebb4f` | a retired PC opens for admins only |
| `2382c056` | « Effacer la copie » on a retired PC + code field no longer scrolls sideways |
| `07d9b321` | `uninstall-relay [--erase]` + `pair-relay` resets the follower state |
| `b7336cb2` | installer `/RELAY` role + uninstall prompt |
| `768862a8` | handoff before the laptop restart |
| `47a105f6` | Windows app bridge 1.4 (`relayHostFacts`, `installRelay`), the offer (start-up dialog · card door · `/pc-de-secours` credentials door), code release, `canInstall`/`needBytes`, N50 |
| `fb9cfd9f` | `GET /api/relay/installer` (this build only), `relay-build` verb, commit stamped into the image + installer, `deploy-hosted.yml` builds and publishes the installer before the swap |
| `9327a858` | handoff before session 7 |
| `bf4a65f7` | the PC updates itself on « update needed » (D10b): `RelayUpdater`, resumable download, SYSTEM scheduled task, « Mise à jour » through the restart |
| `13e64bfd` | promotion verbs (D11): `promote-relay`, `sign-relay-promotion`, compiled-in vendor key, operator procedure in `packaging/README.md` |
| `6e58a6bd` | Part 2 lease slice 1: `ClinicWriteLease`, ack protocol, cloud fence (423 `relay_silent`), two-phase stand-down, migration `AddRelayWriteLease` |
| `f5de9b96` | Part 2 lease slice 2: the PC takes over — `RelayLease`/`RelayLeaseKeeper`/`GatewayBoxProbe`, holding follower + pulse, `[OnlineOnly]`, `clinic_on_relay`, cut's-work guard, migration `AddRelayPcHolding` |
| `a2e20458` | Part 2 lease slice 2b: D15's net in `ClinicChangeCapture`, `ClinicFencedException` (423), `IClinicWriteFence` in the jobs and startup backfills, two derived guards |
| `1945f81d` | « Reprendre la main » (US-7, D19), AC-6.4's bell row, the card's lock line and warning, migration `AddRelayReclaim` |
| `56d3c8cd` | idempotency (D17): `IdempotencyMiddleware`, `IdempotencyRecords` (per side), `withWriteKey` in `client.ts`, N51 |
| `020c8f09` | number promise (D16): `NumberedDocuments`, `NumberPromiseCoordinator` + transaction interceptor, `IRelayPromiseBroker`, `POST /api/relay/promises`, the PC's promise loop + `RelayNumberPromises` floor |
| `54b8badd` | device reports (AC-6.2): `RelayDeviceController`, `relayProbe` (Windows 1.5, Android 1.2.0), the web loop, migration `AddRelayDeviceReports` |
| `e60be0df` | the return, slice a (D18): the PC's own log, two-phase handback (`POST relay/handback`, files), `RelayHandbackPlanner`, FR-11 merge, `RelayReviewItems`, stuck alarms, migration `AddRelayHandback` |
| (session 11) | after the return (D18b1): screens refresh + Google catch-up on phase 2 (`RelayReturnAftermath`), one reminder per visit (EC-16), 7-day grace on a holding PC (EC-15), review list API + bell row |

## Part 1 — done / left

| Bullet | State |
|---|---|
| Kind and pairing | ✅ |
| Copy (snapshot, feed, apply, files) | ✅ rehearsed end to end; CI `relay-copy` job not written |
| Watching (heartbeat, card, bell, console, e-mails) | ✅ — console column never seen live |
| Lifecycle (re-wrap, retire, lost, erase, uninstall) | ✅ — AC-8.6 waits for Part 2's lock |
| One click and lockstep | 🔶 installer role ✅ (not run) · bridge + offer ✅ (browser-rehearsed, stand-in bridge) · installer served + CI build ✅ (CI job not run) · self-update ✅ (rig, stand-in installer) · promotion verbs ✅ (rig, real vendor key) — **done** |

## Scratch test rig

How to bring it back: **`C:\Users\Oumayma Benkhalifa\clinic-pc-copy-rig\README.md`** (outside the repo: it holds the
scratch account's test password + TOTP secret). The working copies live in the OLD scratchpad
`…\20e51cc5-ea54-481b-8728-3a2161ef3bf6\scratchpad\e2e` (`env.sh` points there).

| Piece | State at the end of session 10 |
|---|---|
| Shared Docker (postgres, minio, mailpit) | started from cold this session (lease holder clinic-pc-copy) |
| Scratch cloud API :5098 | last copy **`cloud-api-21`** / PC **`pc-api-20`** (D18b1, both stopped; `return-api.mjs` gained `pc-visit`, `review`, `seen`). Before: **`cloud-api-20`** (D18a, run bound to `127.0.0.1:5098`; PC **`pc-api-19`**, « essai 6 », stood down; both DBs carry `AddRelayHandback`; `e2e
eturn-api.mjs seed|cloud-edit|pc-work|cloud-try|check` = the return pass — seed, edit on the cloud and Ctrl+Break it at once, wait the takeover, pc-work, restart the cloud, wait ~2 min, check). Before: last copy **`cloud-api-19`** (D16; PC **`pc-api-18`**, web **`web-cloud-12`** — all **stopped**; `e2e\promise-api.mjs armed|killed <pid>|floor|stood` = the D16 checks). Before: **`cloud-api-18`** (D17; `e2e\idem-api.mjs` + `pw\idem-browser.mjs` = the D17 passes), **`cloud-api-17`**, run **bound to `127.0.0.1`** (`Hosting__Urls=http://127.0.0.1:5098`) so the PC and a device arrive on one address family; web **`web-cloud-11`** (built with `NEXT_PUBLIC_API_URL=http://127.0.0.1:5098/api`). `pw\device-pass.mjs <outdir> <pcPid> <fingerprint>` = the device-report pass (22 checks, kills the PC); the shell probe harness is `scratchpad\probe-harness` (session-10 scratchpad). Before that: **`cloud-api-16`** (current code; web **`web-cloud-10`**); `cloud-api-12/updates/relay/` holds the 67 MB stand-in installer (`fakesetup.exe`) — copy it over to test an update. Both scratch DBs carry `AddRelayPcHolding` |
| Scratch cloud web :3098 | **`web-cloud-9`** |
| Test PC (:5097/:3097) | **`pc-api-16`** = current code (same « essai 6 », armed). Before that: **`pc-api-15`**, paired « PC-ACCUEIL (essai 6) » (essai 5 retired after the reclaim rehearsal; its lease kept as `.local/relay-lease.kept-cut-work-2.json`). Earlier: « PC-ACCUEIL (essai 5) » (essai 4 retired during the slice-2 cut; its lease file kept as `.local/relay-lease.kept-cut-work.json`), seeded, armed, stopped cleanly. `pc-api-11` is the PC **promoted** — never use it for Part 2. `e2e/lease2-run.mjs <pc|cloud> <label>` signs in and tries a save, two FR-5 actions (PC only — on the cloud FR-11 keeps them open and they WRITE) and a read; `e2e/lease2-retire.mjs` retires during a cut (mutates); `scratchpad/ctrlc-out/ctrlc.exe <pid> 1` (session-10 scratchpad) stops an API gracefully |
| Browser pass for the offer | `node offer-run.mjs <outdir>` — run from the OLD scratchpad's `pw\` (has `node_modules`); 38/38 last run |

Servers started by a session die with it: restart with `source env.sh; run_cloud cloud-api-12` and
`cd $E2E/web-cloud-9 && PORT=3098 … node server.js` (exact line in the rig README). Ask before dropping
`clinic_relay_cloud` / `clinic_relay_pc`.

## Gate (run all, unfiltered, after the last edit)

- API: `cd api && dotnet test ClinicManagement.UnitTests/ClinicManagement.UnitTests.csproj -c Release -p:BaseOutputPath=<scratchpad>/build-pc/` — last **5 785 pass · 6 skip · 0 fail**.
- Shell: `cd desktop && dotnet test ClinicManagement.DesktopShell.sln -c Release -p:BaseOutputPath=<scratchpad>/build-shell/` — **135 pass**.
- Android: `cd mobile/android && ./gradlew.bat --no-daemon -q assembleDebug lintDebug assembleRelease` — green.
- Web: `cd web && npm run check:responsive && npx tsc --noEmit && npm run build` — **79/79**, green.
- Installer: `node packaging/lint-iss.mjs`; `node packaging/ci-stub-payloads.mjs` then
  `MSYS_NO_PATHCONV=1 "<LocalAppData>/Programs/Inno Setup 6/ISCC.exe" /Qp "/O<scratch-out>" 'packaging\setup\clinic-setup.iss'`.
- Migrations: scaffold out of tree (`BaseOutputPath=<scratchpad>/build-ef/ dotnet ef migrations add …`), check for a stray `xmin`.
- Build identity: `dotnet ClinicManagement.API.dll relay-build` on both sides of a comparison.

## Gotchas (still true)

- Repo files are CRLF, some with a BOM: prefer the `Edit` tool. A Python text-mode read + `newline=''` write turns CRLF
  into LF silently (it did to `AuthController.cs`) — edit **bytes**. A Bash heredoc mangles « » and can eat `\a` —
  write scripts to the scratchpad and run them (`PYTHONUTF8=1 python file.py`).
- A `dotnet publish` copy has no `appsettings.Development.json` → a cloud copy boots `SelfHostedLan`; copy it in.
- **A build with no `.git` (Docker) has no commit in its version** → always pin `-p:SourceRevisionId=<sha>` when two
  builds must compare equal (deviation 47).
- The PC's deferred startup migration wants `pg_dump` (not installed here) → migrate the PC DB with `dotnet ef database update --connection`.
- Console verbs print French in the console's OEM code page: the installer words each exit code itself; `relay-build` is ASCII only.
- New endpoint ⇒ reviewed entries in `ControllerAuthorizationCoverageTests` (anonymous), `SubscriptionExemptionCoverageTests`
  (writes), `RelayStandbyExemptionCoverageTests`, `PlatformReadShapeTests`; a new console verb ⇒ `SystemWideCallerCoverageTests` (`Exempt` or a declared scope).
- `System.Text.Json` escapes `'` as `\u0027` — compare decoded values, not serialised text.
- A driven browser at desktop width meets the post-visit prompt first; the start-up offer waits for it (≤ 2 min).
- Several Claude sessions share this machine: `stack-lease.ps1 status` before touching the shared stack.
- A standard (non-elevated) account cannot register a SYSTEM or « highest rights » scheduled task (`schtasks` says « Access is denied » — the XML parsed fine); check the task format under your own SID with `LeastPrivilege` + `InteractiveToken`, keep the SYSTEM run for the rehearsal.
- A Bash heredoc collapses `\\` to `\` and breaks Python string escapes — write scripts with the Write tool.
- A process launched from the Bash tool ignores Ctrl+C (`GenerateConsoleCtrlEvent(0)`); Ctrl+Break (`1`) reaches .NET's graceful shutdown — and kills the sending helper with it (exit -1073741510), which is harmless.
- `TenantScope` takes an `ILogger<TenantScope>` — tests pass `NullLogger<TenantScope>.Instance`.
- `dotnet ef … -- -p:BaseOutputPath=…` passes the switch to the APP, not to MSBuild (« short switch not defined ») — EF then builds in-tree; harmless when no API holds `bin/`, otherwise use `--no-build` after a scratch build.
- Browser passes: `pw\reclaim-pass.mjs <outdir>` (16 checks) is the template — fresh profile per run, Chrome launched with
  the three anti-throttling flags, the post-visit prompt dismissed with « Plus tard » (forced click). `e2e\reclaim-api.mjs
  status|reclaim|retire` drives the card's API.
- To take the PC over on the rig: stop the cloud (Ctrl+Break); the PC holds ~90 s after its last ack (the gateway of this machine answers). To undo: retire from the cloud, stop the PC, set `.local/relay-lease.json` aside, re-pair.
