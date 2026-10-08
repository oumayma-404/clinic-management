# Handoff — PC de secours (`clinic-pc-copy`)

**Date:** 2026-10-08 (session 6) · **Overall:** ~33 % · **Part 1 (La copie):** ~94 %

## Pick up here

1. Open the session **in the worktree**: `C:\Users\Oumayma Benkhalifa\Desktop\clinic-management\.claude\worktrees\clinic-pc-copy`
   (branch `feature/clinic-pc-copy`, tree clean, **every commit local — never pushed; never push without the owner's OK**).
2. Read this file, then `progress.md` (Part status, deviations 1–35, verification log). Plan: `../plan.md`, spec: `../spec.md`.
3. Next sub-step: **serve the installer + slim update (D10)** — see « Next steps » below.
4. The owner works step by step: « next step » = one sub-step → build, gate, live check on the scratch rig, notes,
   local commit, then a short report ending with a % table and the roadmap table (`.claude/rules/response-style.md`).

## Commits on the branch (oldest → newest, all local)

| Commit | What |
|---|---|
| `ee88ed66` | spec, challenged plan, blueprint |
| `11470cfe` | kind `ClinicRelay`, pairing, change log, cloud feed, standby gate, `pair-relay` verb |
| `e32bfc21` · `95fddeb0` | PC follows the cloud (`RelayFeedJob`/`RelayFollower`) · 3 rehearsal defects |
| `ff92fac8` | « Paramètres → PC de secours » card |
| `9bfa9f94` | admin bell (`StaffNotification.TargetRole`, `RelayWatchJob`, `Stopped` state) |
| `e484d4bb` | vendor console « PC de secours » column (**never looked at live yet**) |
| `56307621` | vendor alert e-mails (`RelayIncidents`, `VendorAlertRecipients`) |
| `3ef27d40` | « Déclarer perdu ou volé » (every account re-credentialed) |
| `1d3ebb4f` | a retired PC opens for admins only (`RelayLocalStatus`, 403 `relay_retired_admins_only`) |
| `2382c056` | « Effacer la copie » on a retired PC + shared code field no longer scrolls sideways (`pushPasswordManagerStrategy="none"`) |
| `07d9b321` | uninstall verb `uninstall-relay [--erase]` (cloud told first, erase only once it answered) + `pair-relay` resets the follower state |
| `b7336cb2` | installer `/RELAY` role + uninstall prompt « Effacer aussi la copie du cabinet ? » |
| _(session 6)_ | bridge `relayHostFacts` + `installRelay` (shell 1.4), the offer (start-up dialog, card door, `/pc-de-secours` credentials door), code release, `canInstall`/`needBytes` |

## Part 1 — what is done, what is left

| Bullet | State |
|---|---|
| Kind and pairing | ✅ (installer role now ✅ too) |
| Copy (snapshot, feed, apply, files) | ✅ rehearsed end to end; CI `relay-copy` job **not written** (push needs OK) |
| Watching (heartbeat, card, bell, console, e-mails) | ✅ — console column never seen live |
| Lifecycle (re-wrap, retire, lost, erase, uninstall) | ✅ — AC-8.6 waits for Part 2's lock |
| One click and lockstep | 🔶 installer role ✅ (compiled, **not run**); bridge + offer ✅ (browser-rehearsed with a stand-in bridge); serving the installer/update, CI publish, self-update (D10b), promotion verbs (D11) left |

## Next steps, in order

1. **Serve the installer + slim update** from `deploy/updates/relay/` (D10) and the CI job building them — ⚠️ the
   first VPS publish needs the owner's OK. **Contract already fixed by the shell**: `GET /api/relay/installer` answering
   the bytes **with an `X-Content-SHA256` hex header** — no header ⇒ the shell refuses to run it; a 404 reads « Le cloud
   ne propose pas encore l'installation du PC de secours ».
2. **Self-update on « update needed »** in the heartbeat ack (D10b) — runs the installer with `/RELAY` and no
   `/PAIRFILE` (an update in place keeps its pairing; the installer refuses `/RELAY` without a code only when no relay
   is installed).
3. **Promotion verbs** (D11, offline-signed code).
4. **Windows rehearsal** of the one-click install on a real PC — owner's OK (UAC + three services). Needs shell 1.4
   and step 1. The browser pass `offer-run.mjs` (rig `pw\`) already covers everything around the UAC run.
5. Owed checks: console column live; « perdu ou volé » confirmed for real (resets the test accounts → new password +
   TOTP enrolment); A2 (a non-admin session opened before the retire → 401) is unit-tested only.
6. Then Part 2 « La relève » and Part 3 « Les appareils suivent ».

## Scratch test rig (survives the restart)

Scripts + how to bring it back: **`C:\Users\Oumayma Benkhalifa\clinic-pc-copy-rig\README.md`** (outside the repo on
purpose: it holds the scratch dev account's test password + TOTP secret). Scratch DBs `clinic_relay_cloud` /
`clinic_relay_pc` in the shared Docker Postgres — **ask before dropping**. The test PC is currently uninstalled +
erased; re-pair it before the next live check. My test servers were stopped before the restart.

Session 6: the scratch **cloud** runs `cloud-api-10` (:5098) / `web-cloud-9` (:3098); the shared Docker containers were
started from cold (lease holder: clinic-pc-copy). The test PC is still uninstalled + erased. Browser pass:
`node offer-run.mjs <outdir>` from the OLD scratchpad's `pw\` (it has `node_modules`; the rig copy has none).

## Gate (run all, unfiltered, after the last edit)

- API: `cd api && dotnet test ClinicManagement.UnitTests/ClinicManagement.UnitTests.csproj -c Release -p:BaseOutputPath=<scratchpad>/build-bell/` — last run **5 213 pass · 6 skip · 0 fail**.
- Shell: `cd desktop && dotnet test ClinicManagement.DesktopShell.sln -c Release -p:BaseOutputPath=<scratchpad>/build-shell/` — **114 pass**.
- Web: `cd web && npm run check:responsive && npx tsc --noEmit && npm run build` — last run 78/78 (N50 = the room sentence ↔ installer), green.
- Installer: `node packaging/lint-iss.mjs`; `node packaging/ci-stub-payloads.mjs` then
  `MSYS_NO_PATHCONV=1 "<LocalAppData>/Programs/Inno Setup 6/ISCC.exe" /Qp "/O<scratch-out>" 'packaging\setup\clinic-setup.iss'`.
- Migrations: scaffold out of tree (`BaseOutputPath=<scratchpad>/build-ef/ dotnet ef migrations add …`) and check for a stray `xmin` column.

## Gotchas met (still true)

- Repo files are CRLF, some with a BOM: prefer the `Edit` tool; a Python text-mode read + `newline=''` write turns CRLF into
  LF silently (it did to `AuthController.cs` once) — edit **bytes**; a Bash heredoc mangles « » — write scripts to files; a Python heredoc ate `\a` in `'{app}\api'` once (became a bell char) — write such scripts to a file.
- `sed -i` turns CRLF files into LF. Restore with `sed -i 's/\r$//; s/$/\r/'` or avoid it.
- A `dotnet publish` copy has no `appsettings.Development.json` → the cloud copy boots `SelfHostedLan`; copy it in.
- The PC's deferred startup migration wants `pg_dump` (not installed here) → migrate the PC DB with `dotnet ef database update --connection`.
- Console verbs print French in the console's OEM code page (garbled accents): the installer never relays their text, it words each exit code itself.
- Guard tests that need a reviewed entry for a new endpoint: `ControllerAuthorizationCoverageTests` (anonymous), `SubscriptionExemptionCoverageTests`, `RelayStandbyExemptionCoverageTests`, `PlatformReadShapeTests`.
- Several Claude sessions share this machine: `stack-lease.ps1 status` before touching the shared stack (the scratch rig uses its own ports).
