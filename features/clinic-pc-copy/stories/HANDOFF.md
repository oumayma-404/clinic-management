# Handoff — PC de secours (`clinic-pc-copy`)

**Date:** 2026-10-07 (updated after session 2) · **Overall:** ~15 % · **Part 1 (La copie):** ~48 %

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

1. **`RelayFeedJob`** (`API/BackgroundJobs/`, `BackgroundService`, registered only where `MirrorsCloudClinic`; `RunAs` + `UseClinic(creds.ClinicId)`): load `RelayCredentialStore` (none → idle, log once) → `POST relay/token` with `X-Relay-Secret` → seed via `GET relay/snapshot` + `ReplaceAsync` → loop `GET relay/changes?after=&fingerprint=` → `ApplyBatchAsync`; `WentBack` → stop + alert (D12); `ReseedRequired` or apply failure → reseed; heartbeat every 10 s (`POST relay/heartbeat`), `UpdateNeeded` → flag. The ack carries `HighWater`: pull when behind (deviation 2). Send `X-Relay-Build` from `IRelayBuildInfo`.
   - The local apply cursor (last applied seq + head fingerprint + epoch) must persist on the PC — decide where (a `.local/relay-state.json` beside the credentials is simplest; not a DB table, which the snapshot would overwrite).
   - The TOTP unwrap: `RelayInboundUnwrap` = open with `creds.PrivateKey` (`RelaySecretEnvelope.Open`) → re-protect with this install's `UserSecretProtector`.
2. **Files**: `IRelayBlobIndex.ListKeysAsync` on the PC's DB → `GET relay/blob?key=` → write at the **same key** on local disk (a writer that bypasses `UploadAsync`'s key composition — check `LocalDiskFileStorage` key→path), temp then move.
3. **Hourly digest** (`GET relay/digest` vs local `DigestAsync`) → per-table `ReplaceAsync`, never toward a changed epoch; report unrepaired tables in the heartbeat's `MismatchTables`.
4. Unit tests for the job's decision logic (extract it as a pure state machine so it is testable without HTTP/DB); the end-to-end proof is CI `relay-copy` (two instances, per-table hash equal) — plan Part 1 « Check ».
5. Then « Watching » (`StaffNotification.TargetRole`, watch job, web card « Paramètres → PC de secours », console column, vendor alert email — message the `server-loss-recovery` session first), « Lifecycle » (users + TOTP re-wrap, retire → PC read-only, lost, erase, uninstall), « One click » (installer `/RELAY /PAIRFILE=` writing `Deployment:Profile=ClinicRelay` into `appsettings.Install.json` **then** running `pair-relay`, bridge `installRelay`, offer, CI `relay-package.yml`, promotion verbs).

⚠️ Decision (deviation 12, add to progress.md): `pair-relay` does **not** write `Deployment:Profile`; it refuses unless the profile is already `ClinicRelay`. The installer's relay role writes it into the layer it owns.

## Gotchas met this session

- Repo files are **CRLF**, some have a **BOM**: a Python rewrite added a BOM to `Program.cs` once (removed). Prefer the `Edit` tool; if scripting, preserve BOM + line endings.
- A quoted bash heredoc turns `\\u001f` into a raw control char — write the escape with the `Edit` tool.
- `cd` inside a Bash call moves the session's cwd; use absolute paths.
- Vendor alert email channel (AC-9.2) exists only **uncommitted** in sibling worktree `server-loss-recovery` — message that session before building a sender.
- Peers live on this machine; stack checks via `.claude/skills/start-clinic/scripts/stack-lease.ps1 status` before starting anything.
