# Handoff — PC de secours (`clinic-pc-copy`)

**Date:** 2026-10-08 (end of session 6) · **Overall:** ~34 % · **Part 1 (La copie):** ~96 %

## Pick up here

1. Open the session **in the worktree**: `C:\Users\Oumayma Benkhalifa\Desktop\clinic-management\.claude\worktrees\clinic-pc-copy`
   (branch `feature/clinic-pc-copy`, tree clean, **every commit local — never pushed; never push or deploy without the
   owner's OK**).
2. Read this file, then `progress.md` (part status, deviations 1–48, verification log). Plan `../plan.md`, spec `../spec.md`.
3. **Next sub-step: the PC de secours updates itself on « update needed » (D10b)** — detailed below.
4. The owner works step by step: « next step » = one sub-step → blast-radius rows in `progress.md` → build → full gate →
   live check on the scratch rig → notes → **local** commit → short report ending with a % table and the roadmap table
   (`.claude/rules/response-style.md`: bullets, tables, no paragraphs).

## The next sub-step — self-update (D10b)

**Goal:** after a cloud deploy, a PC de secours on the old build updates itself with no one at the PC, then catches up.

**What already exists**

| Piece | Where | State |
|---|---|---|
| Heartbeat ack says `UpdateNeeded` + `CloudBuild` | `RelayHeartbeatCommand.cs` (never refuses on version — D10b) | ✅ |
| The follower stops copying while `UpdateNeeded` | `Infrastructure/Relay/RelayFollower.cs` (~l. 99–131) | ✅ — and then does nothing else |
| Heartbeat field `IsUpdating` | `RelayFollower.cs` ~l. 154 sends `false` always; `ClinicRelayHealth` already shows « Mise à jour » when true | 🔶 to wire |
| The installer of the cloud's build | `GET /api/relay/installer` → bytes + `X-Content-SHA256` + `X-Relay-Build`; 404 « pas encore » while not published | ✅ (`fb9cfd9f`) |
| Installer in-place update | `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RELAY /RESULTFILE=` with **no** `/PAIRFILE` keeps the pairing (refused only when no relay is installed) | ✅ compiled, never run |

**To build**

1. A `RelayUpdater` (Infrastructure/Relay) the follower calls when `UpdateNeeded`:
   - download `/api/relay/installer` to `.local/updates/` as `.part`, rename on a complete stream;
   - refuse unless `X-Relay-Build` == the ack's `CloudBuild` **and** the SHA-256 matches;
   - 404 → retry later (≈ 5 min), never a download loop; remember the build last attempted in `relay-state.json`
     (`AtomicFile`), so one cloud build = one attempt series;
   - say `IsUpdating: true` on the heartbeat before launching;
   - launch the installer and let it stop/replace/restart the services.
2. ⚠️ **The launcher must outlive the service that starts it.** The PC's API is the native Windows service
   `ClinicManagementApi` (registered with `sc.exe`, `UseWindowsService`, LocalSystem — nssm is only for the web
   service), and the installer's first act is `sc stop` + `sc delete` of that very service. A plain child process
   normally survives its parent's exit (no job object by default), but nothing here proves it yet: prefer a one-shot
   **scheduled task** (`schtasks /create /sc once /ru SYSTEM … /f` then `/run`, deleted afterwards), which is outside the
   service's tree by construction, and confirm it in the Windows rehearsal. Put it behind an interface
   (`IRelayUpdateLauncher`) so tests and the rig use a fake.
3. **Disarm first (D10b/D14)** belongs to Part 2's lease, which does not exist yet: leave one clearly named hook where
   the disarm will go; nothing to disarm today.
4. After the restart the new build heartbeats → `UpdateNeeded` false → the copy resumes and catches up (existing code).
5. Tests: follower with a fake cloud + fake launcher (build mismatch on the header → no launch · SHA mismatch → no
   launch · 404 → retry later, no launch · success → `IsUpdating` then launch once · same build not re-attempted).
6. Live check on the rig without installing services: re-pair the test PC (`pc-api-N`) to the scratch cloud,
   republish the cloud with another `-p:SourceRevisionId` (new build ⇒ `UpdateNeeded`), publish a **fake installer**
   (a tiny console exe that writes a marker file + its arguments) with a manifest for the new build in
   `cloud-api-N/updates/relay/`, and check the PC downloads, verifies, reports « Mise à jour » and launches it with the
   right arguments. The real elevated run stays for the Windows rehearsal (owner's OK).

## Then, in order

| # | Step | Owner's OK |
|---|---|---|
| 2 | Promotion verbs (D11, code signed offline by a vendor key, verified by a key built into the PC) | — |
| 3 | First `deploy-hosted` run with the new `relay-installer` job (builds the real installer on Windows, publishes it to `deploy/updates/relay/` before the swap) — never run yet | **yes** (VPS deploy) |
| 4 | Windows rehearsal of the one-click install + a self-update, on a real PC | **yes** (UAC + 3 services) |
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

## Part 1 — done / left

| Bullet | State |
|---|---|
| Kind and pairing | ✅ |
| Copy (snapshot, feed, apply, files) | ✅ rehearsed end to end; CI `relay-copy` job not written |
| Watching (heartbeat, card, bell, console, e-mails) | ✅ — console column never seen live |
| Lifecycle (re-wrap, retire, lost, erase, uninstall) | ✅ — AC-8.6 waits for Part 2's lock |
| One click and lockstep | 🔶 installer role ✅ (not run) · bridge + offer ✅ (browser-rehearsed, stand-in bridge) · installer served + CI build ✅ (CI job not run) · **self-update (next)** · promotion verbs |

## Scratch test rig

How to bring it back: **`C:\Users\Oumayma Benkhalifa\clinic-pc-copy-rig\README.md`** (outside the repo: it holds the
scratch account's test password + TOTP secret). The working copies live in the OLD scratchpad
`…\20e51cc5-ea54-481b-8728-3a2161ef3bf6\scratchpad\e2e` (`env.sh` points there).

| Piece | State at the end of session 6 |
|---|---|
| Shared Docker (postgres, minio, mailpit) | started from cold this session (lease holder clinic-pc-copy) |
| Scratch cloud API :5098 | last copy **`cloud-api-11`** (build `…+1.0.0+47a105f6…`); `updates/relay/` holds a test manifest for an OLD build only |
| Scratch cloud web :3098 | **`web-cloud-9`** |
| Test PC (:5097/:3097) | uninstalled + erased — re-pair before any PC-side check (`issue-code.mjs` + `pair-relay`) |
| Browser pass for the offer | `node offer-run.mjs <outdir>` — run from the OLD scratchpad's `pw\` (has `node_modules`); 38/38 last run |

Servers started by a session die with it: restart with `source env.sh; run_cloud cloud-api-11` and
`cd $E2E/web-cloud-9 && PORT=3098 … node server.js` (exact line in the rig README). Ask before dropping
`clinic_relay_cloud` / `clinic_relay_pc`.

## Gate (run all, unfiltered, after the last edit)

- API: `cd api && dotnet test ClinicManagement.UnitTests/ClinicManagement.UnitTests.csproj -c Release -p:BaseOutputPath=<scratchpad>/build-pc/` — last **5 227 pass · 6 skip · 0 fail**.
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
