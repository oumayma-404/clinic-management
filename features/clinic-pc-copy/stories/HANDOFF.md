# Handoff — PC de secours (`clinic-pc-copy`)

**Date:** 2026-10-08 (session 5, paused for a laptop restart) · **Overall:** ~31 % · **Part 1 (La copie):** ~90 %

## Pick up here

1. Open the session **in the worktree**: `C:\Users\Oumayma Benkhalifa\Desktop\clinic-management\.claude\worktrees\clinic-pc-copy`
   (branch `feature/clinic-pc-copy`, tree clean, **every commit local — never pushed; never push without the owner's OK**).
2. Read this file, then `progress.md` (Part status, deviations 1–35, verification log). Plan: `../plan.md`, spec: `../spec.md`.
3. Next sub-step: **Windows app bridge `installRelay` + the offer (AC-1.1–1.13)** — see « Next steps » below.
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

## Part 1 — what is done, what is left

| Bullet | State |
|---|---|
| Kind and pairing | ✅ (installer role now ✅ too) |
| Copy (snapshot, feed, apply, files) | ✅ rehearsed end to end; CI `relay-copy` job **not written** (push needs OK) |
| Watching (heartbeat, card, bell, console, e-mails) | ✅ — console column never seen live |
| Lifecycle (re-wrap, retire, lost, erase, uninstall) | ✅ — AC-8.6 waits for Part 2's lock |
| One click and lockstep | 🔶 installer role ✅ (compiled, **not run**); bridge, offer, serving the installer/update, CI publish, self-update (D10b), promotion verbs (D11) left |

## Next steps, in order

1. **Bridge + offer (AC-1.1–1.13)** — `desktop/` shell (`VaultBridge.cs`, `MainWindow.xaml.cs`, `ServerConfig.cs`),
   `mobile/shared/bridge.md` (the `window.__clinicShell` contract; bump the shell version), `web/types/clinic-shell.d.ts`,
   a web offer component gated on `window.__clinicShell?.installRelay` (so never in a browser/Android, AC-1.12).
   Flow: admin at app start (once; « Plus tard » = 7 days, « Pas sur ce PC » = never, per PC) or the menu item
   « Installer le PC de secours ici… » (email + password + code, AC-1.5) → step-up `relay-pairing` → `POST
   /api/relay/pairing-codes` → bridge `installRelay({ code, cloudUrl, label, needBytes })` → the shell writes the code
   to a file, downloads the server installer, runs it **elevated once** with
   `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RELAY /PAIRFILE= /CLOUD= /LABEL= /NEEDBYTES= /RESULTFILE=`, reads the
   exit code (0 ok · 20 pairing refused · 21 stopped · 7 refused before copying) and the UTF-8 sentence in the result
   file. Notices to add to the offer: battery (AC-1.6), unencrypted disk (AC-1.7), UPS (AC-1.13); free-space refusal
   (AC-1.8) needs the clinic's footprint from the cloud (a read that does not exist yet). Card shows « Copie en cours
   (40 %) » already (AC-1.9).
2. **Serve the installer + slim update** from `deploy/updates/relay/` (D10) and the CI job building them — ⚠️ the
   first VPS publish needs the owner's OK.
3. **Self-update on « update needed »** in the heartbeat ack (D10b) — runs the installer with `/RELAY` and no
   `/PAIRFILE` (an update in place keeps its pairing; the installer refuses `/RELAY` without a code only when no relay
   is installed).
4. **Promotion verbs** (D11, offline-signed code).
5. **Windows rehearsal** of the one-click install on a real PC — owner's OK (UAC + three services).
6. Owed checks: console column live; « perdu ou volé » confirmed for real (resets the test accounts → new password +
   TOTP enrolment); A2 (a non-admin session opened before the retire → 401) is unit-tested only.
7. Then Part 2 « La relève » and Part 3 « Les appareils suivent ».

## Scratch test rig (survives the restart)

Scripts + how to bring it back: **`C:\Users\Oumayma Benkhalifa\clinic-pc-copy-rig\README.md`** (outside the repo on
purpose: it holds the scratch dev account's test password + TOTP secret). Scratch DBs `clinic_relay_cloud` /
`clinic_relay_pc` in the shared Docker Postgres — **ask before dropping**. The test PC is currently uninstalled +
erased; re-pair it before the next live check. My test servers were stopped before the restart.

## Gate (run all, unfiltered, after the last edit)

- API: `cd api && dotnet test ClinicManagement.UnitTests/ClinicManagement.UnitTests.csproj -c Release -p:BaseOutputPath=<scratchpad>/build-bell/` — last run **5 193 pass · 6 skip · 0 fail**.
- Web: `cd web && npm run check:responsive && npx tsc --noEmit && npm run build` — last run 77/77, green.
- Installer: `node packaging/lint-iss.mjs`; `node packaging/ci-stub-payloads.mjs` then
  `MSYS_NO_PATHCONV=1 "<LocalAppData>/Programs/Inno Setup 6/ISCC.exe" /Qp "/O<scratch-out>" 'packaging\setup\clinic-setup.iss'`.
- Migrations: scaffold out of tree (`BaseOutputPath=<scratchpad>/build-ef/ dotnet ef migrations add …`) and check for a stray `xmin` column.

## Gotchas met (still true)

- Repo files are CRLF, some with a BOM: prefer the `Edit` tool; a Python heredoc ate `\a` in `'{app}\api'` once (became a bell char) — write such scripts to a file.
- `sed -i` turns CRLF files into LF. Restore with `sed -i 's/\r$//; s/$/\r/'` or avoid it.
- A `dotnet publish` copy has no `appsettings.Development.json` → the cloud copy boots `SelfHostedLan`; copy it in.
- The PC's deferred startup migration wants `pg_dump` (not installed here) → migrate the PC DB with `dotnet ef database update --connection`.
- Console verbs print French in the console's OEM code page (garbled accents): the installer never relays their text, it words each exit code itself.
- Guard tests that need a reviewed entry for a new endpoint: `ControllerAuthorizationCoverageTests` (anonymous), `SubscriptionExemptionCoverageTests`, `RelayStandbyExemptionCoverageTests`, `PlatformReadShapeTests`.
- Several Claude sessions share this machine: `stack-lease.ps1 status` before touching the shared stack (the scratch rig uses its own ports).
