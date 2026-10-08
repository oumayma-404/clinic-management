# Handoff — PC de secours (`clinic-pc-copy`)

**Date:** 2026-10-08 (end of session 8) · **Overall:** ~36 % · **Part 1 (La copie):** done in code (owed: the Windows
rehearsal and the first CI run — both need the owner's OK) · **Part 2 (La relève):** next

## Pick up here

1. Open the session **in the worktree**: `C:\Users\Oumayma Benkhalifa\Desktop\clinic-management\.claude\worktrees\clinic-pc-copy`
   (branch `feature/clinic-pc-copy`, tree clean, **every commit local — never pushed; never push or deploy without the
   owner's OK**).
2. Read this file, then `progress.md` (part status, deviations 1–62, verification log). Plan `../plan.md` (Part 2 is
   « La relève », decisions D13–D20b), spec `../spec.md` (Part B), blueprint `../blueprint.md` (§ 3 « The lease »,
   § 4 « Handback », Part B files).
3. **Next sub-step: Part 2 · the lease, first slice** — detailed below.
4. The owner works step by step: « next step » = one sub-step → blast-radius rows in `progress.md` → build → full gate →
   live check on the scratch rig → notes → **local** commit → short report ending with a % table and the roadmap table
   (`.claude/rules/response-style.md`: bullets, tables, no paragraphs).

## The next sub-step — Part 2 · the lease, first slice (D13, D14)

**Goal:** the cloud and the PC can never both accept writes for one cabinet. This slice builds the rule and proves it;
the banners, the return and the devices come after.

**To build** (plan Part 2 « Lease », blueprint § 3)

1. `ClinicWriteLease` (D13): the fence is a **predicate**, never a flag a job flips — the cloud refuses the cabinet's
   scoped writes when `relay armed ∧ now − LastConfirmedAckSentAt > 60 s ∧ not unlocked`; the PC takes over on its
   side of the same clock (measured from the **ack**, not the send, so the PC's takeover lands after the cloud's
   fence). Read D13's full row before coding.
2. Two-phase arming (D14): the ack states armed/disarmed, the PC confirms, and the cloud keeps fencing-on-silence
   until the PC has confirmed a disarm. The self-update's `RelayUpdater.DisarmBeforeUpdateAsync` hook (today a no-op)
   is where the PC's planned disarm goes.
3. Enforcement in two layers both sides (D15): `ClinicFenceInterceptor` (SaveChanges safety net, catches jobs) +
   the existing `RelayLeaseGateMiddleware` (the 423 sentence) — read how that middleware and the standby gate work today.
4. Tests first: fake-clock lease tests, « never both writable » above all — including heartbeats that reach the cloud
   while every answer is lost, and an unplugged PC that must never take over.
5. Leave for the next slices: `RelayLeaseJob` box check + device reports (AC-6.2), clean shutdown / Windows Update /
   clock (D20b), number promises (D16), idempotency (D17), the return (D18), screens.

## Done in session 8 — promotion verbs (D11, AC-9.3)

`promote-relay` (on the PC) + `sign-relay-promotion` (on the vendor's machine): an ECDSA P-256 code bound to one PC and
one cabinet, verified against a compiled-in vendor key; refused while the cloud's own `/health` is healthy; on success
journal row → `Deployment:Profile=SelfHostedLan` → copy released. Rehearsed on the rig with the real vendor key: the
promoted PC booted as a local server, the copied admin signed in, a write was accepted. The vendor's private key is at
`C:\Users\Oumayma Benkhalifa\apexa-vendor-keys\relay-promotion-private.pem` (outside the repo — the owner must keep an
offline backup). Operator procedure: `packaging/README.md` § « PC de secours — making it the cabinet's server ».
Details: deviations 56–62.

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
| `bf4a65f7` | the PC updates itself on « update needed » (D10b): `RelayUpdater`, resumable download, SYSTEM scheduled task, « Mise à jour » through the restart |
| (session 8) | promotion verbs (D11): `promote-relay`, `sign-relay-promotion`, compiled-in vendor key, operator procedure in `packaging/README.md` |

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

| Piece | State at the end of session 8 |
|---|---|
| Shared Docker (postgres, minio, mailpit) | started from cold this session (lease holder clinic-pc-copy) |
| Scratch cloud API :5098 | last copy **`cloud-api-12`** (build `…+d10bc10ud…0002`, label only); `updates/relay/` serves a 67 MB stand-in installer (`fakesetup.exe`, writes `e2e/fakesetup-ran.txt`) for that build |
| Scratch cloud web :3098 | **`web-cloud-9`** |
| Test PC (:5097/:3097) | `pc-api-10` = paired « PC-ACCUEIL (essai 4) », first copy done (same build as the cloud; `Relay__UpdateLauncher=direct` + `FAKESETUP_MARKER` replays an update). `pc-api-11` = the same PC **promoted** to a local server (its `.local` and `appsettings.Install.json` say so) — to test Part 2 use `pc-api-10`, never 11; its DB `clinic_relay_pc` now also holds one scratch patient « Essai Promotion PC … » created on the promoted server |
| Browser pass for the offer | `node offer-run.mjs <outdir>` — run from the OLD scratchpad's `pw\` (has `node_modules`); 38/38 last run |

Servers started by a session die with it: restart with `source env.sh; run_cloud cloud-api-12` and
`cd $E2E/web-cloud-9 && PORT=3098 … node server.js` (exact line in the rig README). Ask before dropping
`clinic_relay_cloud` / `clinic_relay_pc`.

## Gate (run all, unfiltered, after the last edit)

- API: `cd api && dotnet test ClinicManagement.UnitTests/ClinicManagement.UnitTests.csproj -c Release -p:BaseOutputPath=<scratchpad>/build-pc/` — last **5 286 pass · 6 skip · 0 fail**.
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
