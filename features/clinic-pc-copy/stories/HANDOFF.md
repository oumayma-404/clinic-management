# Handoff — PC de secours (`clinic-pc-copy`)

**Date:** 2026-10-08 (end of session 7) · **Overall:** ~35 % · **Part 1 (La copie):** ~98 %

## Pick up here

1. Open the session **in the worktree**: `C:\Users\Oumayma Benkhalifa\Desktop\clinic-management\.claude\worktrees\clinic-pc-copy`
   (branch `feature/clinic-pc-copy`, tree clean, **every commit local — never pushed; never push or deploy without the
   owner's OK**).
2. Read this file, then `progress.md` (part status, deviations 1–55, verification log). Plan `../plan.md`, spec `../spec.md`.
3. **Next sub-step: the promotion verbs (D11, AC-9.3)** — detailed below. It is the last bullet of Part 1.
4. The owner works step by step: « next step » = one sub-step → blast-radius rows in `progress.md` → build → full gate →
   live check on the scratch rig → notes → **local** commit → short report ending with a % table and the roadmap table
   (`.claude/rules/response-style.md`: bullets, tables, no paragraphs).

## The next sub-step — promotion verbs (D11, AC-9.3)

**Goal:** if the cloud is lost for good, the vendor turns a PC de secours into an ordinary local server, with a one-time
code issued **without the cloud** (the console lives on the same server and dies with it).

**To build** (plan.md row « `PromoteRelayCommand.cs` · `SignRelayPromotionCommand.cs` », blueprint § Part A)

1. `sign-relay-promotion` — run by the vendor on **their own machine**, offline: signs « promote relay `<RelayId>` of
   clinic `<ClinicId>`, valid until … » with a **vendor private key** that never sits on a server. Decide the key
   format (Ed25519 or ECDSA P-256 — `RelaySecretEnvelope` already uses .NET crypto; no new package if avoidable) and
   where the private key lives (a file the vendor keeps, path on the command line).
2. `promote-relay --code-file …` — run on the PC: verifies the code against a **public key built into the API**
   (a constant, so a PC cannot be talked into trusting another key), checks it names this PC's `RelayId`/`ClinicId`
   and is not expired, **refuses while the cloud answers** (a reachable cloud is not lost), then writes
   `Deployment:Profile=SelfHostedLan` into `appsettings.Install.json`, marks the relay retired locally (stops
   following), and prints what must be re-entered (SMS/WhatsApp credentials, Google link). Exit codes like the other
   verbs (0 · 1 · 2…), worded by whoever calls it.
3. What a promoted PC becomes afterwards is « an ordinary local install, under that product's rules » (spec out of
   scope) — check the `SelfHostedLan` boot on a DB that was a copy: catalog/admin backfills that `DeferredStartupService`
   skips on a relay (deviation 15) will now run; recurring jobs come back (`RunsClinicJobs`).
4. Guards: `SystemWideCallerCoverageTests` for both new verbs; no MediatR command behind them (like
   `platform-account`); a test that the built-in public key and the signer's key format agree.
5. Live check on the rig: sign with a scratch vendor key (a test key pair — the built-in key must then be the test one
   in a test build only; decide how, e.g. an override allowed only in `Development`), promote `pc-api-10` with the cloud
   stopped, boot it as `SelfHostedLan`, sign in, see the copied patients.

## Done this session — self-update (D10b)

`RelayUpdater` (+ `RelayInstallerDownloader`, `RelayUpdateLaunchers`): on « update needed » the PC fetches the cloud's
own installer **beside** the ticks (Range resume), checks `X-Content-SHA256`, announces « Mise à jour », and runs it
`/RELAY` with no code through a one-shot **SYSTEM scheduled task**; one run per cloud build, refusals reported, the
series cleared 10 min after the new build is up. The cloud keeps « Mise à jour » through ≤ 20 min of silence. Owed: the
SYSTEM task + the real installer replacing the services (Windows rehearsal, owner's OK). Details: deviations 49–55.

## Then, in order

| # | Step | Owner's OK |
|---|---|---|
| 3 | First `deploy-hosted` run with the new `relay-installer` job (builds the real installer on Windows, publishes it to `deploy/updates/relay/` before the swap) — never run yet | **yes** (VPS deploy) |
| 4 | Windows rehearsal of the one-click install + a self-update (the SYSTEM task, the real installer replacing the services), on a real PC | **yes** (UAC + 3 services) |
| 5 | Owed checks: console column live · « perdu ou volé » for real (resets the test accounts) · A2 (non-admin session opened before a retire → 401) unit-tested only · CI `relay-copy` job not written | console/CI: push needs OK |
| 6 | Part 2 « La relève » (lease, takeover, return, « Modifications à vérifier », « À reprendre ») | — |
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
| (this session) | the PC updates itself on « update needed » (D10b): `RelayUpdater`, resumable download, SYSTEM scheduled task, « Mise à jour » through the restart |

## Part 1 — done / left

| Bullet | State |
|---|---|
| Kind and pairing | ✅ |
| Copy (snapshot, feed, apply, files) | ✅ rehearsed end to end; CI `relay-copy` job not written |
| Watching (heartbeat, card, bell, console, e-mails) | ✅ — console column never seen live |
| Lifecycle (re-wrap, retire, lost, erase, uninstall) | ✅ — AC-8.6 waits for Part 2's lock |
| One click and lockstep | 🔶 installer role ✅ (not run) · bridge + offer ✅ (browser-rehearsed, stand-in bridge) · installer served + CI build ✅ (CI job not run) · self-update ✅ (rig, stand-in installer) · **promotion verbs (next)** |

## Scratch test rig

How to bring it back: **`C:\Users\Oumayma Benkhalifa\clinic-pc-copy-rig\README.md`** (outside the repo: it holds the
scratch account's test password + TOTP secret). The working copies live in the OLD scratchpad
`…\20e51cc5-ea54-481b-8728-3a2161ef3bf6\scratchpad\e2e` (`env.sh` points there).

| Piece | State at the end of session 7 |
|---|---|
| Shared Docker (postgres, minio, mailpit) | started from cold this session (lease holder clinic-pc-copy) |
| Scratch cloud API :5098 | last copy **`cloud-api-12`** (build `…+d10bc10ud…0002`, label only); `updates/relay/` serves a 67 MB stand-in installer (`fakesetup.exe`, writes `e2e/fakesetup-ran.txt`) for that build |
| Scratch cloud web :3098 | **`web-cloud-9`** |
| Test PC (:5097/:3097) | paired « PC-ACCUEIL (essai 4) », first copy done, last copy **`pc-api-10`** (same build as the cloud); run it with `Relay__UpdateLauncher=direct` + `FAKESETUP_MARKER` to replay an update |
| Browser pass for the offer | `node offer-run.mjs <outdir>` — run from the OLD scratchpad's `pw\` (has `node_modules`); 38/38 last run |

Servers started by a session die with it: restart with `source env.sh; run_cloud cloud-api-12` and
`cd $E2E/web-cloud-9 && PORT=3098 … node server.js` (exact line in the rig README). Ask before dropping
`clinic_relay_cloud` / `clinic_relay_pc`.

## Gate (run all, unfiltered, after the last edit)

- API: `cd api && dotnet test ClinicManagement.UnitTests/ClinicManagement.UnitTests.csproj -c Release -p:BaseOutputPath=<scratchpad>/build-pc/` — last **5 257 pass · 6 skip · 0 fail**.
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
