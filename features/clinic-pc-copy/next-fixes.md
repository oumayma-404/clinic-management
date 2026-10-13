# PC de secours — fixes still to do (from the 2026-10-13 review)

Start here in a new session. Worktree `.claude/worktrees/clinic-pc-copy`, branch `feature/clinic-pc-copy` (pushed at
`6ed0e2e4`; prod still on `97d9bc9d`). Full detail of each finding: `reviews/feature-review.md` (same numbers).
Rules: write the blast-radius table first, run the whole unit suite (never `--filter`), end with
`bash e2e/relay-roundtrip/run.sh` (~12 min, isolated containers). Never push or deploy without the owner's OK.

## Already done — do not redo
- Criticals 1–7 + major 12: commit `070bcd4d` (deviations 214–218 in `stories/progress.md`).
- Critical 8 (database password readable on the PC): **declined by the owner** — do not reapply.

## Leftovers from the critical fixes
| What | Where | Note |
|---|---|---|
| The PC should act on « Reprendre la main » / « Retirer » even while a copy tick is busy | `RelayFollower.PulseAsync` (~l.215), `RelayLeaseKeeper` | careful: saving state from the pulse can be overwritten by the busy tick — stop the copy first, then end the lease |
| Drive the return from the lease loop, not only the copy tick | `RelayFollower.WhileHoldingAsync`, `RelayFeedJob` | the 60 s stall timeout already bounds the hang |
| SMS reminder sent for a visit the PC cancelled during a cut | `NotificationJob` | cannot be fixed without fencing the cloud; maybe pause reminders while an armed PC is silent > X min — owner decision |

## Major — data safety (do these first)
| # | Problem | Where | Fix |
|---|---|---|---|
| 9 | After a cloud restore, « Effacer la copie », a reinstall (`pair-relay --replace`) or `relay_unknown` → Released can erase the only copy of the newer data | `RelayLocalEraser.MayErase`, `PairRelayConsoleCommand` l.83, `RelayCloudClient.Classify` l.~367, `RelayUninstaller` | refuse all four while `RelayGap.Pending(state)`; a seeded PC told `relay_unknown` stops « gardé », never `Released` |
| 10 | Retire / uninstall / « perdu ou volé » during a cut strands the cut's work; Retire needs no authenticator code | `RetireRelayCommand` l.62, `RelayController.Retire`, `RelayUninstaller` l.82 | while `PcHoldingSinceUtc` is set, run a Reclaim first; uninstall checks unreturned work before telling the cloud; step-up on Retire |
| 11 | A reclaimed PC that needs an update never updates → cut never listed « À reprendre » | `RelayFollower.TickAsync` overruled branch l.144 | step the updater first when `state.UpdateNeeded`, like the gap branch |
| 13 | A re-seed running at the takeover can delete rows just saved on the PC | `RelayLeaseKeeper.TickAsync` l.783, `ClinicRelayRowStore.ReplaceAsync` | one gate shared by copy-apply and takeover (no offer while an apply runs) |
| 14 | A save sent at the moment of the cut is recorded twice (key lost at the switch) | `web/lib/api/client.ts` l.984, `web/lib/forms/carried-draft.ts` | carry unsettled Idempotency-Keys in the carried draft; reuse on the first save after the switch |
| 17 | « Effacer la copie » / uninstall leaves patient data on disk (uninstall stops the DB before erasing; backups never erased) | `clinic-setup.iss` `CurUninstallStepChanged` ~l.1720, `RelayLocalEraser` l.77 | stop only API+Web before `uninstall-relay --erase`; erase `api\Backups\clinic-backup-*` too |

## Major — ops / updates
| # | Problem | Where | Fix |
|---|---|---|---|
| 15 | A failed update leaves the PC de secours off | `clinic-setup.iss` `CurStepChanged`/`DeinitializeSetup` ~l.1604; `DeferredStartupService` | `InstallFinished` only on success, else restart services; `sc failureflag 1` + non-zero exit on fatal startup |
| 16 | Each migration deploy leaves a full backup copy on the PC, never pruned → disk full → PC silently can't take over | `DeferredStartupService` l.67 | skip the files copy (or the backup) on a relay; DB-only, keep 1 |
| 18 | Owner bug #5: a normal update reads « Éteint », then « Copie en retard », and e-mails the vendor | `RelayFollower.StandDownAsync` l.208, `ClinicRelayHealth` l.146 | pass `isUpdating: null`; keep « mise à jour » until first catch-up |
| 19 | Every deploy reinstalls every PC (10–25 min unprotected); installer never run in CI | `RelayServices.cs` `RelayBuildInfo.Compute`, `deploy-hosted.yml` | identity = newest migration + relay protocol version; Windows CI job that installs + updates; keep previous installer as fallback |
| 20 | Relay token rate limit per IP; a 429 counts as « internet coupé » | `RateLimiting.cs` l.285, `RelayCloudClient.Classify`, `RelayLease.ExchangeAsync` l.319 | partition by relay id; treat 4xx/5xx/429 as « the cloud answered » |
| 26 | Owner bug #7 (Bouka): PC advertises only VPN/virtual addresses; discovery can't recover | `RelayHostFacts.PreferNetworkAddresses` l.49; desktop `RelayDiscovery.cs` l.101, Android `RelayDiscovery.kt` l.65 | put first the address the OS uses to reach the cloud; drop tunnel/virtual adapters; use the UDP reply's source address |

## Major — devices (switch and return)
| # | Problem | Where | Fix |
|---|---|---|---|
| 21 | One missed « do you hold? » (Wi-Fi hiccup ≥5 s) throws the app off the PC — screen and typing lost | `MainWindow.xaml.cs` l.746, `MainActivity.kt` l.655 | leave only on explicit `holding == false`; re-ask on null |
| 22 | A 3–10 s Wi-Fi blip shows a false « Internet coupé », then reloads the home page | `MainWindow.AnswerRelaySwitchAsync` l.903, `MainActivity.kt` l.514 | confirm with a 2nd check ~3 s later; reload the current URL, never `BaseUrl` |
| 23 | Shared reception PC: the next user lands on the PC signed in as the first user of the day | `web/components/relay/relay-switch-watch.tsx` l.92 | key the « prepared at » by user; clear the PC session at sign-out |
| 24 | Cloud API down (502/503): devices get no button | `RelaySwitch.CloudAnswersAsync` l.143, Android `RelaySwitch.kt` l.113, `client.ts` l.740 | treat 502/503/504 as « not answering », or let the PC's `canTakeOver` decide |
| 25 | Android phone on 4G never sees the cut and keeps writing to the cloud | `RelaySwitch.kt` l.235 | bind relay traffic to Wi-Fi (`TRANSPORT_WIFI`); ask the PC « do you hold? » even when the cloud answers |

## Minor (when the majors are done)
27 carried patient form re-PUTs antécédents without version · 28 flaky line delays the return, no « bloqué » bell ·
29 « Retour au cloud en cours — quelques secondes » shown when blocked · 30 « disponible dans quelques secondes » for
ever when only this device is offline · 31 contradictory refusals ~20 s after the return · 32 no PC link for
Safari/iPad and non-admins · 33 review bell/« Vu » hide that cabinet typing was not saved · 34 take-over endpoint
anonymous, firewall open on every network · 35 offer can appear ~10 s after wake; 4xx/5xx counted as silence ·
36 self-update not signed · 37 lost tablet keeps a 30-day session on the PC · 38 seventeen one-line items (list in the
review). Suggestions: 39 per-save cost for every clinic · 40 stale docs/constants in `ClinicWriteLease`.
