# Handoff — PC de secours (`clinic-pc-copy`)

**Date:** 2026-10-07 (after session 4) · **Overall:** ~18 % · **Part 1 (La copie):** ~60 %

## Where the work is

| What | Where |
|---|---|
| Worktree | `C:\Users\Oumayma Benkhalifa\Desktop\clinic-management\.claude\worktrees\clinic-pc-copy` |
| Branch | `feature/clinic-pc-copy` (from `b618a1d8`, `feature/security-remediation`'s HEAD) |
| Commits | `ee88ed66` docs · **`11470cfe`** kind, pairing, change log, cloud feed, standby gate, `pair-relay` verb, migration (local, **not pushed**) |
| Code | committed; tree clean except this file |
| Plan | `features/clinic-pc-copy/plan.md` (challenged, 11 fixes applied) |
| Pointers to the codebase | `features/clinic-pc-copy/stories/context.md` |
| Progress log | `features/clinic-pc-copy/stories/progress.md` |

Owner's calls: no `/break-plan`, **one story (US-1)** in three parts; take recommended options without asking.
Start the new session **in the worktree** (`EnterWorktree` with that path, or `cd` into it).

## State of the build

- ✅ Whole solution builds, 0 new warnings. Full suite **5 049 pass / 6 skip / 0 fail** (baseline 4 935).
- ✅ Web: `check:responsive` (77) + `tsc` + `npm run build` green (worktree has its own `node_modules` now).
- ✅ `verify-schema` before/after `AddClinicRelay` on a throwaway DB: only the 7 relay lines moved DRIFT → ok.
- Gate: `cd api && dotnet test ClinicManagement.UnitTests/ClinicManagement.UnitTests.csproj -c Release -p:BaseOutputPath=<scratchpad>/bo/` (cold build ~6 min, tests ~2 min, never `--filter`).

## Done (committed in `11470cfe`; session 2 added the standby gate, the guards, the tests, the migration, `pair-relay` + `RelayCredentialStore`)

| Area | Files | What |
|---|---|---|
| Third kind | `Infrastructure/Deployment/DeploymentProfile.cs`, `Services/OutboundEndpointPolicy.cs`, `UnitTests/.../DeploymentProfileTests.cs` | `DeploymentKind.ClinicRelay` + 4 capabilities `PublishesChangeFeed`, `MirrorsCloudClinic`, `RunsClinicJobs`, `DispatchesOutboxes`; matrix widened to 3 columns; all-kinds test; push ✗ for relay |
| Jobs | `API/Program.cs` | `process-notifications` gated on `DispatchesOutboxes`; `start-running-appointments`, `flag-expiring-stock`, `post-monthly-expenses`, `count-clinic-activity`, `take-recovery-points` gated on `RunsClinicJobs` (else `RemoveIfExists`); subscription warning `RequiresSubscription && RunsClinicJobs` |
| Mode flags | `API/Controllers/AuthController.cs` (`GetMode`), `web/lib/api/auth.ts` | `relayFeedEnabled`, `isClinicRelay` |
| Domain | `Domain/Entities/ClinicRelay.cs`, `ClinicChange.cs` (+ `ClinicChangeCursor`), `Enums/ClinicRelayStatus.cs`, `Repositories/IClinicRelayRepository.cs`, `Services/ClinicRelayHealth.cs` | relay aggregate (pairing code folded in, heartbeat, retire, subject `relay|{id}`); change-log row + cursor; FR-2 state predicate |
| EF | `Persistence/Configurations/ClinicRelayConfiguration.cs`, `ApplicationDbContext.cs`, `Extensions.cs` | 3 tables, filtered unique « one non-retired relay per clinic », query filters, `ClinicRelay` skips the concurrency token, DbSets |
| Exclusions | `AuditSaveChangesInterceptor.cs`, `ClinicArchiveScope.cs` | relay tables out of the audit ledger and the archive |
| Relay scope | `Persistence/ClinicRelayScope.cs` | model-derived plan: archive scope − exclusions + `Added` (User, recovery codes, subscription, notifications, bell); FK order with deferred back-edge columns; owned collections; `WrappedSecrets`, `Redacted`, `PerSideColumns` |
| Capture | `Persistence/ClinicChangeCapture.cs` + `SaveChangesAsync` hook | one `ClinicChange` per touched key inside the save's transaction, cursor `UPDATE … RETURNING`, lock order cursor → audit chain; sync `SaveChanges` refuses when capture applies |
| Row store | `Persistence/ClinicRelayRowStore.cs` (+ `Application/Common/Interfaces/IClinicRelayRowStore.cs`) | all SQL: `row_to_json` out / `json_populate_recordset` in; feed batch to snapshot high-water (≤ 20 000 keys else reseed); snapshot; apply batch; replace (upsert + delete absent); digest; derived epoch (sysid-timeline-dboid); continuity fingerprint; blob index |
| Secrets | `Security/RelaySecretEnvelope.cs` | RSA-2048 OAEP-SHA256 seal/open for TOTP secrets |
| Token | `Auth/LocalAuthClaims.cs`, `Auth/LocalAuthService.cs`, `ILocalAuthService.cs`, `AuthorizationPolicies.cs` | scope `clinic-relay`; `GenerateRelayToken`; policy `ClinicRelayPeer` (subject starts `relay|`) |
| Application | `Application/Features/Relay/**` | refusals + codes, step-up actions (code-only, added to `StepUpCommand`), journal rows, principal, labels (FR-2 sentences), DTOs; commands: issue pairing code, pair, token exchange, heartbeat, retire; queries: status, changes, snapshot, digest, blob |
| Infra services | `Infrastructure/Relay/RelayServices.cs`, `Repositories/ClinicRelayRepository.cs` | build info (last migration + informational version), key validator |
| API | `Controllers/RelayController.cs` (admin), `RelayPeerController.cs` (PC), `Startup/RateLimiting.cs` | endpoints under `/api/relay/*`; `relay-token` rate-limit policy |
| Realtime | `RealtimeResourceResolver.cs` | `Relay` area excluded |

## Decisions taken while implementing (log them in progress.md « Deviations »)

| # | Plan said | Done instead | Why |
|---|---|---|---|
| 1 | `ClinicRelayPairingCode` entity | code hash on `ClinicRelay` | one setup attempt = one row; fewer tables |
| 2 | Feed nudged by the clinic hub | heartbeat ack carries `HighWater`; PC pulls when behind | no SignalR client needed until Part 2 (number promises) |
| 3 | Large backlog downloaded in parts | one response up to 20 000 keys, else `ReseedRequired` | simpler; same correctness (D6b) |
| 4 | Reuse archive store's entity reader | rows moved as Postgres JSON | exact types, owned columns, shadow FKs, no EF materialisation |
| 5 | `UserSecretProtector.RewrapForRelay` | static `RelaySecretEnvelope` + row-store transform | keeps the protector interface untouched |
| 6 | — | `ClinicChange.KeySeparator = '\u001f'` | user ids are `local|{guid}` |
| 7 | — | relay keeps its secret hash after retirement | so the PC learns « retiré » on token exchange (AC-8.1) |

## Next steps, in order

✅ **Done in session 3:** `RelayFeedJob` (one `BackgroundService`, 10 s tick, only where `MirrorsCloudClinic`, waits for
pending migrations) over `Infrastructure/Relay/`: `RelayCloudClient` (token cache + refresh on a bare 401),
`RelayFollower` (heartbeat → first copy / catch-up → files → hourly check), `RelayFeedDecisions` (pure D12 / D6b / D25
rules), `RelayFollowerState` (`.local/relay-state.json`), `RelayHostFacts`, `AtomicFile`. 46 new tests; wiring guard
red-proofed. Deviations 13–16 in progress.md.

1. ✅ **End-to-end rehearsal done (session 4)** — see progress.md « Verification log » and deviations 17–19. Remaining from it: turn it into the CI `relay-copy` job (`.github/workflows/ci.yml`, beside `local-mode`): two scratch DBs, cloud + PC APIs, pairing via a console stub instead of TOTP, assert per-table digests equal, a cloud save seen on the PC, a rewound cursor stops the PC.
   deferred FK columns, `ReplaceAsync`'s delete-the-rest) has **never run against PostgreSQL**; no unit test can reach
   it. Pattern: `isolated-test-stack` memory. `pg_dump` the dev DB into `clinic_relay_cloud` (read-only on the shared
   one), hosted API on :5099 against it; a second API with `Deployment__Profile=ClinicRelay` on another port against an
   empty migrated `clinic_relay_pc`; issue a code (step-up needs TOTP — or call the handler from a console stub), run
   `pair-relay`, watch `relay-state.json` reach `seq = high-water`, then compare `GET relay/digest` with the PC's
   `DigestAsync` (all tables equal). Save on the cloud → seen on the PC within 10 s. This is the plan's CI
   `relay-copy` job done by hand first; then write the CI job (`.github/workflows/ci.yml`, beside `local-mode`).
2. « Watching »: `StaffNotification.TargetRole`, watch job on the cloud (`ClinicRelayHealth` → bell rows in opening
   hours, EC-9/EC-10), web card « Paramètres → PC de secours » (`GET /api/relay/status`, issue code behind step-up,
   retire), console column, vendor alert email (message the `server-loss-recovery` session first).
3. « Lifecycle »: users + TOTP re-wrap check, retire → PC read-only for admins, « perdu ou volé », erase, uninstall;
   un-stopping a copy stopped by D12 (today: delete `relay-state.json` + re-pair).
4. « One click »: installer `/RELAY /PAIRFILE=` (writes `Deployment:Profile=ClinicRelay` into
   `appsettings.Install.json`, then runs `pair-relay`), bridge `installRelay`, offer, CI `relay-package.yml`, self-update
   on `UpdateNeeded` (D10b), promotion verbs.

## Gotchas met this session

- Repo files are **CRLF**, some have a **BOM**: a Python rewrite added a BOM to `Program.cs` once (removed). Prefer the `Edit` tool; if scripting, preserve BOM + line endings.
- A quoted bash heredoc turns `\\u001f` into a raw control char — write the escape with the `Edit` tool.
- `cd` inside a Bash call moves the session's cwd; use absolute paths.
- Vendor alert email channel (AC-9.2) exists only **uncommitted** in sibling worktree `server-loss-recovery` — message that session before building a sender.
- Peers live on this machine; stack checks via `.claude/skills/start-clinic/scripts/stack-lease.ps1 status` before starting anything.
