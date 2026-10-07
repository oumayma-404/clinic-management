# Handoff — PC de secours (`clinic-pc-copy`)

**Date:** 2026-10-07 · **Overall:** ~10–12 % · **Part 1 (La copie):** ~35 %

## Where the work is

| What | Where |
|---|---|
| Worktree | `C:\Users\Oumayma Benkhalifa\Desktop\clinic-management\.claude\worktrees\clinic-pc-copy` |
| Branch | `feature/clinic-pc-copy` (from `b618a1d8`, `feature/security-remediation`'s HEAD) |
| Commits | `ee88ed66` docs only (spec, challenged plan, blueprint) |
| Code | **all uncommitted** in the worktree (`git status` lists it) |
| Plan | `features/clinic-pc-copy/plan.md` (challenged, 11 fixes applied) |
| Pointers to the codebase | `features/clinic-pc-copy/stories/context.md` |
| Progress log | `features/clinic-pc-copy/stories/progress.md` |

Owner's calls: no `/break-plan`, **one story (US-1)** in three parts; take recommended options without asking.
Start the new session **in the worktree** (`EnterWorktree` with that path, or `cd` into it).

## State of the build

- ✅ Infrastructure compiled clean (0 new warnings) **before** the Application/API relay code was added.
- ❌ Application, API and UnitTests **not compiled since**. First job: build and fix.
- Baseline before any change: **4 935 pass / 6 skip / 0 fail**.
- Gate: `cd api && dotnet test ClinicManagement.UnitTests/ClinicManagement.UnitTests.csproj -c Release -p:BaseOutputPath=<scratchpad>/bo/` (cold build ~6 min, tests ~2 min, never `--filter`).

## Done (uncommitted)

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

1. **Compile** Application + API + tests; fix errors.
2. **Standby gate (Part 1 « Kind »)**: `API/Middleware/RelayLeaseGateMiddleware.cs` — on `MirrorsCloudClinic`, non-GET `/api` → **423 `{error, code: "relay_standby"}`** except endpoints marked allowed (sign-in: `auth/login`, `refresh`, `logout`, `recovery`, `step-up`; future `relay-local/*`). Use an attribute + derived coverage test, `AllowsWithoutSubscription`'s shape. Place it **before** `SubscriptionGateMiddleware` (AC-4.3). Mirror `relay_standby` and the other relay codes in `web/lib/api/client.ts` `ApiErrorCode`.
3. **Run the full suite** and fix the derived guards this work will trip (expected):
   - `ControllerAuthorizationCoverageTests` — 2 new anonymous: `RelayPeer.Pair`, `RelayPeer.Token`;
   - `ScopedTokenCoverageTests` — new scope `clinic-relay`;
   - `SubscriptionExemptionCoverageTests` — 4 new `[AllowsWithoutSubscription]` (pair, token, heartbeat, retire);
   - `AuthorizationPoliciesTests` — new `ClinicRelayPeer`;
   - `RealtimeResourceResolverTests` — `Relay` excluded;
   - `ClinicArchiveScopeTests`, `TenantScopeFilterTests`, `AdminSurfaceCoverageTests`, kind-pinning tests (`KeyRingProtectionTests`, `TransportAssuranceTests`, `DataResidencyAssuranceTests`, `SelfRegistrationGateTests`…).
4. **New unit tests**: relay scope coverage (every model table is in the plan or `Excluded`, no `Unplaced`; every FK from a relay table points into the plan or is nullable), capture (add/modify/delete, child→clinic, owned → owner, no cursor ⇒ nothing), `ClinicRelayHealth` table, pairing (expired code, double setup, abandoned release), envelope round trip, raw-write guard (`Execute*` on a relay-scoped table — today only `NotificationRepository.PurgeTerminalOlderThanAsync` hits one: decide mitigation).
5. **Migration** `AddClinicRelay` (3 tables) — strip scaffolded `xmin` columns; `verify-schema` before/after.
6. **Commit** « Kind and pairing » + « Copy (cloud side) ».
7. **PC side** (still Part 1 « Copy »):
   - console verb `pair-relay --code-file <f> --cloud <url>`: RSA key pair, cert fingerprint from `.local`, LAN addresses, `POST /api/relay/pair`, store relay id + secret + private key protected (IDataProtector, purpose `ClinicManagement.Relay.Credentials.v1`) in `.local/relay.json`, write `Deployment:Profile=ClinicRelay`;
   - `API/BackgroundJobs/RelayFeedJob.cs` (`BackgroundService`, registered only on `MirrorsCloudClinic`, declares `UseClinic`/`RunAs`): token → seed via `/snapshot` + `ReplaceAsync` → pull `/changes` (`after`, `fingerprint`) → `ApplyBatchAsync`; stop and alert on `WentBack` (D12); reseed on `ReseedRequired` or apply failure; heartbeat every 10 s; self-update flag on `UpdateNeeded`;
   - files: `IRelayBlobIndex.ListKeysAsync` on the PC's own DB → download missing via `/api/relay/blob?key=` → write at the **same storage key** on local disk (needs a writer that bypasses `IFileStorage.UploadAsync`'s key composition — check `LocalDiskFileStorage`'s key→path), temp file then move (resumable);
   - hourly digest compare (`/digest` vs local `DigestAsync`) → per-table `ReplaceAsync`, never toward a changed epoch.
8. Then « Watching » (`StaffNotification.TargetRole`, watch job, web card, console column), « Lifecycle » (lost, erase, uninstall), « One click » (installer `/RELAY /PAIRFILE=`, bridge `installRelay`, offer, CI `relay-package.yml`, promotion verbs) — as listed in `plan.md` Part 1.

## Gotchas met this session

- Repo files are **CRLF**, some have a **BOM**: a Python rewrite added a BOM to `Program.cs` once (removed). Prefer the `Edit` tool; if scripting, preserve BOM + line endings.
- A quoted bash heredoc turns `\\u001f` into a raw control char — write the escape with the `Edit` tool.
- `cd` inside a Bash call moves the session's cwd; use absolute paths.
- Vendor alert email channel (AC-9.2) exists only **uncommitted** in sibling worktree `server-loss-recovery` — message that session before building a sender.
- Peers live on this machine; stack checks via `.claude/skills/start-clinic/scripts/stack-lease.ps1 status` before starting anything.
