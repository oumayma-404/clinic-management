# Handoff — PC de secours (`clinic-pc-copy`)

**Date:** 2026-10-08 (end of session 10) · **Overall:** ~43 % · **Part 1 (La copie):** done in code (owed: the Windows
rehearsal and the first CI run — both need the owner's OK) · **Part 2 (La relève):** ~22 % (lease slices 1 and 2 done)

## Pick up here

1. Open the session **in the worktree**: `C:\Users\Oumayma Benkhalifa\Desktop\clinic-management\.claude\worktrees\clinic-pc-copy`
   (branch `feature/clinic-pc-copy`, tree clean, **every commit local — never pushed; never push or deploy without the
   owner's OK**).
2. Read this file, then `progress.md` (part status, deviations 1–83, verification log). Plan `../plan.md` (Part 2,
   D13–D20b), spec `../spec.md` (Part B), blueprint `../blueprint.md` (§ 3, Part B files).
3. **Next sub-step: Part 2 · lease slice 2b — the D15 safety nets** — detailed below.
4. The owner works step by step: « next step » = one sub-step → blast-radius rows in `progress.md` → build → full gate →
   live check on the scratch rig → notes → **local** commit → short report ending with a % table and the roadmap table
   (`.claude/rules/response-style.md`: bullets, tables, no paragraphs).

## The next sub-step — Part 2 · lease slice 2b (D15 safety nets)

**Goal:** no write reaches a fenced cloud or a non-holding PC by a door that is not a controller.

**Already built (slices 1–2):** `ClinicWriteLease` (fence, takeover, `SinceLastAckReceived`), the ack protocol, the
cloud's 423 `relay_silent` / `clinic_on_relay`, `ClinicRelay.PcHoldingSinceUtc`, `RelayLease` + `RelayLeaseKeeper` +
`GatewayBoxProbe` (`.local/relay-lease.json`), the follower's holding mode + pulse, the PC gate (`IsHolding` lifts
`relay_standby`; `[OnlineOnly]` → 423 `online_only`), the cut's-work guard on erase / uninstall / re-pair, and the
300-line simulation.

**To build**

1. The cloud's SaveChanges interceptor (D15): refuse a write to a fenced cabinet's rows outside the FR-11 table set
   (accounts, sessions, subscription, vendor ledgers, the relay row itself). Decide the table set from the model, not a
   hand list; the PC's own channel and the vendor console must pass.
2. The cloud's recurring jobs skip a fenced cabinet (`AppointmentProgressJob`, `MonthlyExpenseJob`, `StockExpiryJob`,
   `NotificationJob`, any other writer of cabinet rows) — check each saves per clinic so one fenced cabinet does not
   abort the others; a derived guard over the jobs.
3. The PC's interceptor: refuse non-exempt writes unless holding (the feed applier and the eraser use raw SQL —
   confirm they bypass it, and that the takeover journal row is allowed).
4. Leave for later slices: device reports (AC-6.2 unlock) + « Reprendre la main » (US-7) · number promises (D16) +
   idempotency (D17) · the return (D18, which also clears `PcHoldingSinceUtc` / `UnreturnedSinceUtc`) · banners and screens.

## Done in session 10 — the PC takes over (lease slice 2)

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
| 6 | Part 2 « La relève » — slices 1–2 done; next: 2b safety nets, then device reports + « Reprendre la main », numbers + idempotency, the return, screens | — |
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
| (session 10) | Part 2 lease slice 2: the PC takes over — `RelayLease`/`RelayLeaseKeeper`/`GatewayBoxProbe`, holding follower + pulse, `[OnlineOnly]`, `clinic_on_relay`, cut's-work guard, migration `AddRelayPcHolding` |

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
| Scratch cloud API :5098 | last copy **`cloud-api-14`** (current code); `cloud-api-12/updates/relay/` holds the 67 MB stand-in installer (`fakesetup.exe`) — copy it over to test an update. Both scratch DBs carry `AddRelayPcHolding` |
| Scratch cloud web :3098 | **`web-cloud-9`** |
| Test PC (:5097/:3097) | **`pc-api-13`** = current code, paired « PC-ACCUEIL (essai 5) » (essai 4 retired during the slice-2 cut; its lease file kept as `.local/relay-lease.kept-cut-work.json`), seeded, armed, stopped cleanly. `pc-api-11` is the PC **promoted** — never use it for Part 2. `e2e/lease2-run.mjs <pc|cloud> <label>` signs in and tries a save, two FR-5 actions (PC only — on the cloud FR-11 keeps them open and they WRITE) and a read; `e2e/lease2-retire.mjs` retires during a cut (mutates); `scratchpad/ctrlc-out/ctrlc.exe <pid> 1` (session-10 scratchpad) stops an API gracefully |
| Browser pass for the offer | `node offer-run.mjs <outdir>` — run from the OLD scratchpad's `pw\` (has `node_modules`); 38/38 last run |

Servers started by a session die with it: restart with `source env.sh; run_cloud cloud-api-12` and
`cd $E2E/web-cloud-9 && PORT=3098 … node server.js` (exact line in the rig README). Ask before dropping
`clinic_relay_cloud` / `clinic_relay_pc`.

## Gate (run all, unfiltered, after the last edit)

- API: `cd api && dotnet test ClinicManagement.UnitTests/ClinicManagement.UnitTests.csproj -c Release -p:BaseOutputPath=<scratchpad>/build-pc/` — last **5 676 pass · 6 skip · 0 fail**.
- Shell: `cd desktop && dotnet test ClinicManagement.DesktopShell.sln -c Release -p:BaseOutputPath=<scratchpad>/build-shell/` — **114 pass**.
- Web: `cd web && npm run check:responsive && npx tsc --noEmit && npm run build` — **78/78**, green.
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
- To take the PC over on the rig: stop the cloud (Ctrl+Break); the PC holds ~90 s after its last ack (the gateway of this machine answers). To undo: retire from the cloud, stop the PC, set `.local/relay-lease.json` aside, re-pair.
