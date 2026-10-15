# Messagerie interne — Progress

**Feature:** staff-chat · **Branch:** `feature/staff-chat` (worktree `.claude/worktrees/staff-chat`, from `origin/main` 8c2b24de)

## Status

| Story | Status |
|---|---|
| 1 — Messagerie interne du cabinet (Full, 6 parts) | in-progress |

| Part | Status | Commit |
|---|---|---|
| 1A Model + storage | implemented | 8e7f024b |
| 1B Server | implemented | (this commit) |
| 1C Browser | in-progress | — |
| 2 Quick messages | not-started | — |
| 3 Shared desk | not-started | — |
| 4 Patient link + arrival | not-started | — |
| 5 Alerts | not-started | — |
| 6 Windows notification | not-started | — |

## Working tree note (start of session, 2026-10-15)

- Only `features/staff-chat/` untracked (this feature's docs). Nothing else dirty.

## Environment note

- Docker daemon **not running** at session start; shared stack fully down (`stack-lease status`). The DB is needed
  only for `verify-schema` — use an isolated database on the shared postgres so peers on older branches are not
  migrated (memory `isolated-test-stack`).

## Blast Radius

See [`../blast-radius.md`](../blast-radius.md).

## Auto-Approved Deviations

| Deviation | Classification | Reason |
|---|---|---|
| `StaffConversation` holds the full FR-2 desk rules already in Part 1A (plan put desk use in Part 3) | Trivial | Pure domain, no API/UI; one owner for the rule from day one, tested now |
| `StaffConversation` extends the repo's `ValueObject` base | Trivial | House convention for value objects |
| Doc comments kept to one or two lines | Trivial | Comment budget |
| No `StaffChatNotifier` class: handlers call `IRealtimeNotifier.NotifyStaffChatAsync` after commit inside a log-only catch | Trivial | One call per handler; a wrapper would add a file and no behaviour |
| `StaffPresence` counting tested through `ClinicHubTests` (first tab / last close) instead of a separate `StaffPresenceTests` | Trivial | The behaviour that matters is the hub's signal, tested where it happens |
| `StaffMessageCursor` lives beside `GetStaffMessagesQuery` | Trivial | Its only reader and writer |

## Deviations

_(none yet)_

## Gate results

### Part 1A — model + storage (2026-10-15)

| Gate | Result |
|---|---|
| Backend build | 0 errors; 54 pre-existing warnings, **0 in files this part added or changed** |
| Unit suite (unfiltered) | **5 750 passed, 0 failed**, 6 skipped (pre-existing) |
| Migration `20261015182301_AddStaffChat` | scaffolded `xmin` removed by hand; snapshot adds only the two tables |
| `verify-schema` before (isolated DB at `RelayReturnPerRecord`) | exit 2 — exactly the 5 StaffChat items (3 indexes, 2 FKs) |
| `verify-schema` after | **exit 0 — schema matches the model**; no `xmin` column on `StaffMessages` |

Guards that reacted (both correct, both fixed in this part):
- `ClinicalRecordAuditCoverageTests` — `StaffMessage` carries `PatientId` and is not journaled → added to
  `NotClinicalByDesign` with its reason (FR-8).
- My own `StaffConversationTests` used a culture-sensitive `OrderBy` → ordinal.

### Part 1B — server (2026-10-15)

| Gate | Result |
|---|---|
| Unit suite (unfiltered) | **5 795 passed, 0 failed**, 6 skipped (pre-existing) |
| SQL translation (`StaffMessageQuerySqlTests`) | keyset page (`Id <` — Npgsql translates `Guid.CompareTo`), latest-per-thread (`ROW_NUMBER() … PARTITION BY`), unread (`NOT EXISTS` on markers + `GROUP BY`) |
| New warnings in this part's files | 0 (`--no-incremental`; the two matches are pre-existing: Hangfire obsolete API in `Program.cs`, `LocalDataProtection`) |
| Guards that stayed green with a deliberate edit | `RealtimeResourceResolverTests` (`StaffChat` excluded), `ClinicHubTenantScopeTests` (+`StaffPresence` with reason), `SubscriptionExemptionCoverageTests` (+`StaffChat.MarkRead`), `ControllerAuthorizationCoverageTests` (class-level `AnyClinicRole`) |

**Real defect found by a test written in this part:** a two-person key naming an account of **another clinic**
passed membership for the caller and was refused as « compte désactivé » — a confirmation that the id exists.
`StaffConversation.IsMember` now requires both people in the cabinet; pinned in `StaffConversationTests` and
`StaffChatTenantIsolationTests`.

## Learnings

- **A patient-bearing entity that is deliberately not an aggregate trips `ClinicalRecordAuditCoverageTests`** —
  the decision has to be written down there, which is the point.
- The Docker engine on this machine is **Rancher Desktop**, not Docker Desktop.
