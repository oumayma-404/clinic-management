# Progress: Congés des secrétaires

**Started:** 2026-11-05
**Type:** Small
**Branch:** feature/conges-secretaires (worktree `.claude/worktrees/conges-secretaires`, off `origin/main` 3262d2e0)

## Status
- [x] Implementation
- [x] Quality checks — dotnet build · tsc · check:responsive · `next build` green (the build ran on the rebased tree, see table)
- [x] Blast radius closed (14 rows: 9 changed, 5 to re-test)
- [x] Tests — 109 new unit tests, full suite 6329/0 (see Test Plan)
- [x] Browser QA — `qa/run-3.md` GREEN 86/86 on the redesign (run 1 found 2: cross-year « solde après », phone sheet footer — both fixed in run 2)
- [x] Review — `reviews/feature-review.md` challenged: 46 kept (5 Major · 21 Minor · 20 Suggestion), 2 dismissed. Not applied yet; the 320 px scroll it found (Finding 2) was a QA probe miss
- [x] Review fixes — 40 applied, 5 not applied with reasons (`reviews/feature-review.md` § Fixes applied); `qa/run-4.md` GREEN 91/91; suite 6342/0
- [x] Rebased on origin/main 7000b0e0 (Finding 1) — snapshot taken from main and the migration re-scaffolded on it (same `Up()`, same id `20261105114746`, `xmin` stripped); `verify-schema` before/after; suite 6450/0; `qa/run-5.md` GREEN

## Blast Radius
| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `Clinic.LeaveRequestsEnabled` → `ClinicDto` | hand-mapped DTO | 7 sites: Create ×2, Join ×2, RegenerateCode, Update, GetUserStatus | must change — all 7 |
| 2 | `User.AnnualLeaveAllowanceDays` (new column) | user row | archive / relay / purge scope (derived from the model), audit summary | unaffected — derived, additive; must re-test `verify-schema` |
| 3 | `NotificationCategory` +4 members | mirrored set | `StaffNotificationRules.ReachesALockedPhone` (throws on an unclassified member), web `CATEGORY_ICON` / `CATEGORY_TONE` | must change — all three |
| 4 | `NotificationTargetKind` +`StaffLeaveRequest` | mirrored set | web `dashboard-header` click routing, `lib/api/types.ts` doc list | must change |
| 5 | `INotificationGenerator` +3 methods | interface | `NotificationGenerator`, `PushNotificationGeneratorDecorator` | must change — both |
| 6 | new `Features/Team` area | realtime key `team` | `clinic-hub.ts`, `RealtimeResourceResolverTests` | must change — key declared |
| 7 | new entity `StaffLeaveRequest` | EF model | DbSet + tenant filter, `AuditLabels`, archive / relay / purge (derived) | must change (DbSet, filter, label); must re-test `verify-schema` |
| 8 | `DoctorLeaveRules.Period` → shared `FrenchDayPeriod` | French period phrase | doctor congé sentences (overlap, on-leave) | unaffected — delegates, same output |
| 9 | `buildNavSections` new argument | nav model | `dashboard-sidebar` (rail + drawer), `zones.ts` icon map | must change — sidebar passes it, `/conges` added to `EXTRA_ICONS` |
| 10 | `/users` page gains tabs | admin screen | `user-management.tsx` (unchanged inside the « Comptes » tab) | must re-test — 320/820/1440 |
| 11 | `clinic-settings.tsx` new « Équipe » card | settings page | the admin cards around it | must re-test — layout |
| 12 | rail badge on « Utilisateurs » | sidebar item render | every rail item, collapsed rail, drawer | must re-test |
| 13 | migration `AddStaffLeaveRequests` | schema | shared dev DB | must re-test — isolated DB only, `verify-schema` before/after |
| 14 | `ClinicsController` + new `TeamController` actions | API surface | `ControllerAuthorizationCoverageTests`, `AdminSurfaceCoverageTests`, relay-fence + subscription gates | must re-test — full unit suite |
| 15 | QA F-1: `StaffLeaveRules.DaysInYear` → delegates to new `ShareInYear`; new `BalanceAfter` | year split + balance after | `TakenIn`/`Balance` (5 readers: my leave, staff file, allowance save, decision e-mail, list); preview + list now call `BalanceAfter` | must re-test — A5/A8/A15/A16 (same output for single-year congés) |
| 16 | QA F-2: the 3 congé dialogs wrap their form in `DialogBody` | dialog layout | request (self + admin absence), Accepter, Refuser | must re-test — C2/C4 + every dialog row |
| 17 | Redesign: `StaffLeaveBalanceDto.Pending` (congé payé awaiting an answer, by year share) | additive DTO field | 5 `Balance` readers (unchanged), web balance card | unaffected server-side; web type must change |
| 18 | Redesign: `StaffFileDto.AcceptedByType` | additive DTO field | staff file page only | must change — web type + page |
| 19 | Redesign: `IStaffLeaveRequestRepository.ListAsync` gains a status filter | repo signature | `ListStaffLeaveRequestsQuery`, new overview query | must change — both |
| 20 | Redesign: new `GET api/team/overview` (AdminOnly) | API surface | `ControllerAuthorizationCoverageTests` | must re-test — full suite |
| 21 | Redesign: `components/staff-leave/*` rewritten (rows, decision cards, tables, calendar, dialogs) | feature UI | `/conges`, `/users` Congés tab, `/users/[userId]` | must re-test — the whole QA plan (walk selectors updated, no row removed) |

## Working tree note (start of session)
Fresh worktree from `origin/main`. Only untracked: the four `features/` spec folders copied from the main checkout.
`web/node_modules` is a real `npm ci` install (a junction is refused by Turbopack). Delete it when the branch is done.

## Files Changed
- **api — Domain:** `Entities/StaffLeaveRequest.cs` (new), `Enums/StaffLeaveType.cs` + `StaffLeaveStatus.cs` (new),
  `Repositories/IStaffLeaveRequestRepository.cs` (new), `Entities/User.cs` (+ allowance), `Entities/Clinic.cs`
  (+ switch), `Entities/StaffNotification.cs` (+ `ForStaffLeave`), `Enums/NotificationCategory.cs` (+4),
  `Enums/NotificationTargetKind.cs` (+1)
- **api — Application:** `Features/Team/` (new: `StaffLeaveRules`, `StaffLeaveNotices`, 5 commands, 6 queries),
  `Features/Clinics/Commands/SetTeamModulesCommand.cs` (new), `DTOs/StaffLeaveDtos.cs` (new), `DTOs/ClinicDto.cs`
  + the 7 mapping sites, `Common/FrenchDayPeriod.cs` (new) + `Features/DoctorLeaves/DoctorLeaveRules.cs`
  (delegates), `Common/Interfaces/INotificationGenerator.cs` + `Common/Services/NotificationGenerator.cs`
  (+ `StaffLeaveAsync`), `Common/Services/StaffNotificationRules.cs` (4 categories classified),
  `Features/Audit/AuditLabels.cs`
- **api — Infrastructure:** `Persistence/Configurations/StaffLeaveRequestConfiguration.cs` (new),
  `Repositories/StaffLeaveRequestRepository.cs` (new), `ApplicationDbContext.cs` (DbSet + tenant filter),
  `Extensions.cs`, `Persistence/Configurations/ClinicConfiguration.cs`,
  `Services/PushNotificationGeneratorDecorator.cs` (pass-through),
  `Migrations/20261105114746_AddStaffLeaveRequests*.cs` + snapshot (scaffolded; `xmin` removed by hand)
- **api — API:** `Controllers/TeamController.cs` (new), `Controllers/ClinicsController.cs` (+ `team-modules`)
- **web:** `lib/api/team.ts`, `lib/staff-leave.ts`, `lib/hooks/use-staff-leave.ts`,
  `components/staff-leave/` (request dialog, decision dialogs, request list, balance card, admin panel),
  `components/team-modules-card.tsx`, `app/conges/` (page + layout), `app/users/[userId]/page.tsx` (new);
  `app/users/page.tsx` (tabs), `components/user-management.tsx` (link to the file), `components/clinic-settings.tsx`,
  `components/dashboard-sidebar.tsx` (menu entry + badge), `components/notification-panel.tsx`,
  `components/dashboard-header.tsx` (bell routing), `lib/nav.ts`, `lib/zones.ts`, `lib/api/clinics.ts`,
  `lib/api/types.ts`, `lib/realtime/clinic-hub.ts` (`team` key)

## Redesign (2026-11-05, owner: « not clean, unprofessional ») — mockup https://claude.ai/artifact/7ysCZtWNAoxzgLivSBniV2
- **api:** `StaffLeaveBalanceDto.Pending`, `StaffFileDto.AcceptedByType` (`StaffLeaveRules.AcceptedByType`), new
  `GET api/team/overview` (`GetTeamLeaveOverviewQuery`), status filters `approved|rejected|cancelled` on the list
  (repository `ListAsync` gained a `StaffLeaveStatus?`).
- **web:** `staff-leave-bits.tsx` (pill, date tile, decision line), `staff-leave-month-calendar.tsx` (month + « Prochain
  congé »), `staff-leave-request-list.tsx` rewritten (`StaffLeaveRows`, `StaffLeaveDecisionCards`, `StaffLeaveHistory`),
  balance card, request dialog (type as a segmented choice, day summary panel, motif counter), decision dialogs (recap,
  − / + stepper), admin panel (team strip, status filter), `/conges` (two sections + month aside + phone CTA),
  `/users/[userId]` (Actif badge, « Absences acceptées en {année} », history table). Pending tone is now amber.
- **Kept on purpose:** the app's `PageHeader` and `Tabs` (the mockup's underline tabs would differ from every other screen);
  no year selector on the history (the server lists newest first; a year filter is a new read for a rare question).

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Three additive reads: `GET team/leave-preview`, `GET team/leave-requests/pending-count`, `GET team/leave-requests/{id}` | AC-3's count must be the server's (one owner, never a browser copy); AC-5's rail badge needs a count; `check:responsive` P9 requires a version to come from a fresh read |
| The admin list filters `status=pending\|decided` rather than any status | the screen asks exactly those two questions (« à décider » first, then the rest) |
| The Paramètres switch is `SetTeamModulesCommand` under `Features/Clinics` and sends no version | under `Clinics` it broadcasts `clinics`, which is what refreshes every open menu; one idempotent on/off where the last press is the intent (« Mode discret »'s shape) |
| Days stored `numeric(18,3)`, not `(5,1)` | the repo's decimal convention; a non-standard precision is a `verify-schema` exception to argue for |
| `StaffLeaveRequests.UserId` is 255 chars with no FK to `Users` | every user-id column here does that, so a history outlives a deleted account |
| `DoctorLeaveRules.Period` now delegates to `Common/FrenchDayPeriod` | the secretary congés need the same phrase; one owner, same output |
| The decision e-mail goes through `ITransactionalEmailSender` (the install's sender) | the precedent for an e-mail about a clinic user's own account (`ResetUserPasswordCommand`); a clinic's own SMTP is rarely configured |
| « Mes congés » for a non-secretary shows `AccessDeniedCard` | the page is the requester's; the admin decides from « Utilisateurs » |

## Significant Deviations
1. **DEV-1 — refusals are 400 with the French sentence, not the spec's codes.** Every refusal (module off,
   overlap, not pending, already started, motif missing) answers `400 { error }`; only « introuvable » carries a
   code (`staff_leave_not_found`) and answers 404. Same decision as `doctor-conges` DEV-1: no client branches on
   them, and a code nobody reads is a contract nobody honours. A decision on a request another admin already
   answered is refused by name (« Cette demande a déjà reçu une réponse. ») with « Recharger », re-read just
   before the write. Decided (reversible, no data at stake).
2. **DEV-2 — no lock-screen phone push for congé notifications.** AC-9 said « pushed to her phone ». Push here
   is appointment-keyed (`PushDispatchJob` re-reads the appointment) and special-cases chat; a congé push needs a
   new dispatch branch + FCM channel + `PushLabel` entries. The four categories are classified `false` in
   `ReachesALockedPhone`; she gets the bell row and the e-mail (which reaches the phone). Decided (reversible).
3. **DEV-3 — the yearly allowance is saved without a version.** The spec put `version` on the PUT, but the
   account row is rewritten by every sign-in (`LastLoginAt`), so a token would refuse an admin's edit for a save
   nobody made. Single admin-owned number, last write wins.

## Quality Checks
| Check | Result |
|---|---|
| `dotnet build ClinicManagement.sln` (to a scratch `BaseOutputPath`) | 0 errors; no warning in any touched file (37 pre-existing) |
| `tsc --noEmit` | 0 errors |
| `npm run check:responsive` | 85/85 |
| `next build` | ✅ on the rebased tree (2026-11-05) — it could not run earlier in the session (no internet for `next/font/google`) |
| Browser eye pass | not done here — `/test-in-browser` is the next pipeline step (SF1b) |

## Before the browser pass
- The migration is new: do **not** run this branch's API against the shared `clinic_management` DB while peers
  are live — use the isolated stack (`:5099/:3099`, the `isolated-test-stack` memory) on a DB copy.
- `verify-schema` before and after the migration on that DB.

## Deferred to /test-small-feature (done — see Test Plan below)
- Domain: `StaffLeaveRequest.RefusalFor` (no dates, inverted, 366/367, half-day on two days, blank / 501-char
  motif), the transition table (every legal and illegal move), `CountRefusalFor` (negative, 0.25, 367),
  `CanBeCancelledOn` (pending any day; approved only before its first day), `RecordByAdmin` lands Approved.
- `StaffLeaveRules`: `CountDays` (closed days, half day, unset / unreadable hours → Mon–Sat), `DaysInYear`
  (31 Dec → 2 Jan split, sums to the counted days), `TakenIn` (only Approved PaidLeave), labels exhaustive,
  `BalanceAfter` (QA F-1: a 30 déc → 2 janv request deducts only its 2026 share — preview and list must equal the
  balance after acceptance).
- Handlers: submit (secretary only, module off, unknown type, overlap names the other request, days snapshot,
  admins notified via `TargetRole`); decide (admin only, not pending, counted override, motif required, owner
  notified, e-mail sent / skipped when unconfigured / failure does not fail the decision, 409 on a stale
  version); cancel (owner or admin, started refused, approved-by-owner notifies admins, by-admin notifies her);
  record absence (secretary target only, cross-clinic not found); allowance (validation, cross-clinic);
  `SetTeamModulesCommand` (admin only, tri-state, no write when unchanged).
- Queries: list (pending first, balance after, others off), pending count (0 when off), my leave, staff file,
  preview (another account needs an admin), single read (another secretary's reads as not found).
- Guard tests already pass by construction and must stay green: `RealtimeResourceResolverTests` (`team`),
  `TenantScopeFilterTests`, `ControllerAuthorizationCoverageTests`, `ClinicArchiveScopeTests`,
  `ClinicPurgePlanTests`, the `ReachesALockedPhone` coverage test.

## Test Plan
| Area | Action | Target file | Covers |
|----|--------|-------------|-------|
| Entity | New class | `UnitTests/Domain/StaffLeaveRequestTests.cs` | AC-2 refusals (no dates, inverted, 366/367, half day on two days, blank / 501-char motif), AC-7 counted days in halves, transitions both ways, AC-10 cancel before the first day only, AC-11 absence lands accepted, `Covers` |
| Rules | New class | `UnitTests/Features/Team/StaffLeaveRulesTests.cs` | AC-3 day count (closed days, half day, unset / unreadable hours → Mon–Sat), AC-12 year split (rounds to half days, sums to the counted days), taken = accepted congé payé only, `Pending`, `BalanceAfter` cross-year = the balance once accepted (QA F-1), `AcceptedByType`, labels exhaustive, `ParseType` |
| Commands | New class | `UnitTests/Features/Team/StaffLeaveCommandTests.cs` | Submit (AC-2/3/4/5: secretary only, module off, unknown type, overlap named, one admin-role row, failed bell never fails), Decide (AC-7/8/9/14: version passed, counted override + default, motif required, both notices, both e-mails, unconfigured sender skipped, failed mail never fails, not-pending refused, cross-clinic not found), Cancel (AC-10: pending tells nobody, accepted tells admins, admin's tells her, started refused, refused final, colleague refused, cross-clinic), Absence (AC-11), allowance (AC-12), the switch (AC-1: on, unchanged/omitted writes nothing, admin only) |
| Queries | New class | `UnitTests/Features/Team/StaffLeaveQueryTests.cs` | AC-6 list (balance after year share, others off excluding herself, decided rows plain), every status filter reaching the repo, pending count 0 when off, her page, her file by type, cross-clinic file, preview (year share, non-paid no balance, other account admin-only, admin uses her allowance), single read scoped (AC-14 re-read), team strip (active secretaries only, next accepted congé) |

**Coverage notes:** AC-13 (realtime), AC-15 (device) and the rendered screens are browser-only — covered by `qa/run-3.md` (86/86). The migration is covered by `verify-schema` before/after (`qa/run-1.md` P1), outside this suite's reach by design.

## Tests Run
| Suite | Filter | Result |
|-------|--------|--------|
| Unit (Release, scratch `BaseOutputPath`) | none — the unfiltered suite | 6329 passed, 0 failed, 6 skipped (pre-existing) |

## « Droit à congé » — rule, starting balance, relevé (owner, 2026-11-06)

Blueprint: `droit-conges-blueprint.md` (option 1 of `/think-solution`). « Droit annuel » (one number reset each 1
January) becomes a **rule per secretary** (« N j par an » on 1 January or « N j par mois » on each 1st, from a date,
unused days lost on 31/12 or carried) plus a **starting balance** and **days ± with a motif**, all append-only and
cancelled with a motif. Acquired, taken, planned, lost and the relevé are folded on read (`StaffLeaveLedger`), so the
card, the file, a waiting request's « solde après » and the decision e-mail cannot disagree.

**Decisions (reversible):** a request beyond the balance is not refused (today's behaviour); a negative balance
carries into the next year; the leave year is the calendar year; « solde après » is read on the congé's first day (a
monthly rule's credits until then count); with no rule in force on 31/12, nothing expires.

**Migration `AddStaffLeaveAllowanceEntries`:** the table (no `xmin`, hand-removed), then each non-null
`Users.AnnualLeaveAllowanceDays` → a rule « N j par an, perdus au 31/12 » from 1 January of the clinic's year (no
balance moves), then the column is dropped (hand-ordered after the backfill). `Down` restores the latest yearly rule.
Applied to the demo copy only.

**Tests:** `StaffLeaveLedgerTests` (28) + `StaffLeaveAllowanceCommandTests` (11); the balance tests of
`StaffLeaveRulesTests` / `StaffLeaveQueryTests` moved onto `StaffLeaveAccounts` with the day pinned (`Today`); the old
allowance command and its 6 tests are gone. Whole suite **6772 passed · 0 failed · 6 skipped**. `check:responsive`
86/86 · `tsc` clean.

**Browser:** run 6 RED (F1 — « par an » → « par mois » emptied « À partir du » and the rule was not saved; the dialog
also re-seeded on every refetch — fixed in `RuleDialog`), run 7 RED on the walk alone, run 8 **GREEN 123/124 (1 ⏭
pre-existing)**. `next build` green after the fix.

**M1 — the database update with real values (2026-11-07, throwaway local copy `clinic_allowmig_test`, dropped after;
SQL written by `dotnet ef migrations script`, never `database update`, whose design-time factory reads the shared
`clinic_management` from appsettings):** `verify-schema` before → **found `Days` mapped `numeric(5,1)` against the
solution's `(18,3)` convention** — fixed in the configuration, the migration, its designer and the snapshot
(`has-pending-model-changes`: none). Down: table gone, Ines restored to 24 from her live « par an » rule. Values set
24 · 18,5 · 0, up: three rules « par an, perdus » from 1 January 2026, `Droit annuel (repris)`, column dropped, `Days`
`numeric(18,3)`. `verify-schema` after: every `StaffLeaveAllowanceEntries` check ok; the 5 drifts left were already on
the copy and are unrelated (audit chain, a backfill, messaging month, key ring, secrets generation).

**Every gap (2026-11-07):** run 9 RED on environment/probe only (a cold `next dev` and a touch emulation that did not
take), run 10 **GREEN 145/146** — monthly rule from January, a rule cancelled, 31/12 lost / carried / owed, her page
updating live, the e-mail's « restant », tablet touch sizes. Unit suite 6772 ✅ after the `(18,3)` fix.

