# Messagerie interne — Progress

**Feature:** staff-chat · **Branch:** `feature/staff-chat` (worktree `.claude/worktrees/staff-chat`, from `origin/main` 8c2b24de)

## Status

| Story | Status |
|---|---|
| 1 — Messagerie interne du cabinet (Full, 6 parts) | in-progress |

| Part | Status | Commit |
|---|---|---|
| 1A Model + storage | implemented | (this commit) |
| 1B Server | in-progress | — |
| 1C Browser | not-started | — |
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

## Learnings

- **A patient-bearing entity that is deliberately not an aggregate trips `ClinicalRecordAuditCoverageTests`** —
  the decision has to be written down there, which is the point.
- The Docker engine on this machine is **Rancher Desktop**, not Docker Desktop.
