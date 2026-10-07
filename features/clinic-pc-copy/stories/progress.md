# Implementation Progress: PC de secours

**Feature:** [features/clinic-pc-copy/](../)
**Started:** 2026-10-07
**Branch / worktree:** `feature/clinic-pc-copy` · `.claude/worktrees/clinic-pc-copy` (from `b618a1d8`)

> Owner's call (2026-10-07): no `/break-plan` — **one story (US-1)** implemented straight from `plan.md`, in its
> three parts. Each part ends green and is committed; inside a part, a commit at each bullet boundary.

## Story Status

| Story | Status | Started | Completed |
|-------|--------|---------|-----------|
| US-1: PC de secours | in-progress | 2026-10-07 | - |

## Part status

| Part | Bullet | Status | Commit |
|------|--------|--------|--------|
| 1 · La copie | Kind and pairing | done — standby gate, guards, tests, migration verified (installer role still in « One click ») | see git log |
| 1 | Copy (snapshot, capture, feed, apply, files) | in-progress — cloud side written and tested; PC agent not started | see git log |
| 1 | Watching (heartbeat, health, card, bell, digest, console) | pending | |
| 1 | Lifecycle (users + TOTP re-wrap, retire, lost, erase) | pending | |
| 1 | One click and lockstep (installer, bridge, offer, CI, self-update, promotion) | pending | |
| 2 · La relève | Lease · device reports · integrity · return · screens | pending | |
| 3 · Les appareils suivent | Switch · reach and trust · carried forms | pending | |

## Working tree note (start of session)

- The main checkout (`feature/security-remediation`) carries ~230 dirty files from peer sessions; this work runs
  in its own worktree from that branch's HEAD, so none of them are in scope or staged here.
- Peers live at start: `clinic-management-cd` (busy), `clinic-management-33` (idle), several `anakin-*` (other repo).

## Blast Radius

### Part 1 · bullet « Kind and pairing »

| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `DeploymentKind` + `DeploymentProfile.For` / `PermitsOsPush` | the kind switch (both `_ => throw`) | `Program.cs` startup (`IOsPushAvailability`), every `profile.X` read, `DeploymentProfileTests` matrix + 4 theories, `KeyRingProtectionTests`, `TransportAssuranceTests`, `DataResidencyAssuranceTests`, `SelfRegistrationGateTests`, `MaintenanceDatabaseTests`, `SecurityHeadersMiddlewareTests`, `LocalDataProtectionTests` | must change — third arm everywhere + matrix third column + all-kinds test |
| 2 | `OutboundEndpointPolicy`, `LocalDataProtection` (×2) | the other direct kind switches | relay must behave as LAN (DPAPI ring, private endpoints) | must change — explicit `ClinicRelay` arm, never the `_` default |
| 3 | `Program.cs` recurring jobs (11) | unconditional writers | on a mirror they would write rows the feed then overwrites, send reminders twice | must change — `RunsClinicJobs` / `DispatchesOutboxes` gates, `RemoveIfExists` else |
| 4 | `AuthController.GetMode` | capability flags sent to the browser | web shell/offer reads them | must change — `mirrorsCloudClinic` added |
| 5 | `SubscriptionGateMiddleware` + its order guard | write gate at `/api` | relay keeps `RequiresSubscription` | unaffected — new standby gate is placed before it, guard asserts relative order only |
| 6 | `RealtimeResourceResolver.ExcludedAreas` | mirrored key set (C# ↔ `clinic-hub.ts`) | `RealtimeResourceResolverTests` both directions | must change — `Relay` excluded |
| 7 | `ControllerAuthorizationCoverageTests` / `ScopedTokenCoverageTests` / `SystemWideCallerCoverageTests` | derived guards | new anonymous pair/token routes, new scope, new jobs | must change — allow-list rows with reasons, scope declared |
| 8 | Migration (relays, codes, changes, cursors, `TargetRole`) | schema | `verify-schema`, model snapshot | must re-test — strip `xmin`, `verify-schema` before/after |

_(rows for later bullets are added before their first edit)_

## Auto-Approved Deviations

| Deviation | Classification | Reason |
|-----------|----------------|--------|

## Deviations

| # | Plan said | Done instead | Why |
|---|---|---|---|
| 1 | `ClinicRelayPairingCode` entity | code hash on `ClinicRelay` | one setup attempt = one row; fewer tables |
| 2 | Feed nudged by the clinic hub | heartbeat ack carries `HighWater`; PC pulls when behind | no SignalR client needed until Part 2 |
| 3 | Large backlog downloaded in parts | one response ≤ 20 000 keys, else `ReseedRequired` | simpler; same correctness (D6b) |
| 4 | Reuse the archive store's entity reader | rows moved as Postgres JSON | exact types, owned columns, shadow FKs, no EF materialisation |
| 5 | `UserSecretProtector.RewrapForRelay` | static `RelaySecretEnvelope` + row-store transform | protector interface untouched |
| 6 | — | `ClinicChange.KeySeparator = '\u001f'` | user ids are `local|{guid}` |
| 7 | — | a retired relay keeps its secret hash | the PC learns « retiré » on token exchange (AC-8.1) |
| 8 | standby gate exempts « sign-in » | exactly five doors: login, refresh, logout, end-my-session, step-up (`[AllowedOnStandbyRelay]`) | change-password, TOTP enrolment, recovery codes and setup write rows the cloud owns — done on the cloud |
| 9 | per-side columns = TOTP secret | + `User.LastLoginAt`, `FailedLoginAttempts`, `LockoutEnd` | a sign-in on the PC is allowed, so these legitimately differ; D18 merges them at the return |
| 10 | raw-write guard: « decide mitigation » | `NotificationRepository` purge reviewed, repaired by the hourly digest | terminal reminders past 90 days; a late delete of history costs nothing |
| 11 | — | releasing an expired/abandoned setup is saved before the new row is inserted | in one save EF may send the INSERT first and the filtered unique index refuses it |
| 12 | `pair-relay` writes `Deployment:Profile=ClinicRelay` | it refuses unless the profile already is `ClinicRelay`; the installer's relay role writes it into `appsettings.Install.json` | a verb that rewrote the kind would turn a LAN server into a copy of another clinic with one mistyped command; the installer owns that layer |
| 13 | `RelayFeedJob` + `RelayDigestJob` (two files) | one `BackgroundService` loop; the hourly check runs inside it on its own clock | two loops would race on the position file; the check needs a caught-up copy, which only the follower knows |
| 14 | position kept by the PC (unspecified) | `.local/relay-state.json` beside the credentials, never a DB table | a re-seed replaces the copied tables wholesale and would overwrite a position stored among them; an unreadable file reads as « stopped », never « fresh » |
| 15 | — | `DeferredStartupService` skips the catalog / admin / Google backfills on a PC de secours | they would write rows the cloud never made into a copy (must-change consumer found while wiring) |
| 16 | — | a file the cloud cannot serve is retried hourly and left out of « première copie » | one missing object would otherwise hold the copy « en cours » for ever |

## Verification log

| When | What | Result |
|---|---|---|
| 2026-10-07 | full unit suite (unfiltered) | 5 045 pass · 6 skip · 0 fail (baseline 4 935) |
| 2026-10-07 | `verify-schema` on a scratch DB, before → after `AddClinicRelay` | 7 relay DRIFT → ok; only remaining drift `key-ring-protection` (dev env, pre-existing); no `xmin` column |
| 2026-10-07 | full unit suite after `RelayFeedJob` | 5 094 pass · 6 skip · 0 fail; wiring guard red-proofed (gate swapped → red, restored) |

## Learnings

- On Windows, `File.Move(tmp, path, overwrite: true)` over a file written milliseconds earlier is refused with « Access denied » (antivirus / indexer hold) — hit by 12 follower tests at once. `Relay/AtomicFile` retries the move; any repeated small-file write on the PC needs it.
