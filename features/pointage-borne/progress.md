# Progress: Pointage sur la borne du cabinet

**Started:** 2026-11-05
**Type:** Small (but measured at ~45 files — see DEV-1)
**Branch:** feature/pointage-borne, from feature/conges-secretaires (b1a35839: part 1 committed on origin/main 7000b0e0)
**Worktree:** .claude/worktrees/conges-secretaires (part 1's worktree, branch switched in place)

## Status
- [x] Implementation
- [x] Quality checks (build 0 errors · unit suite 6450/0 · tsc · check:responsive 86/86 · next build)
- [x] Blast radius closed
- [x] Tests — 5 classes, +140 cases; unfiltered suite 6590 passed / 0 failed / 6 skipped (was 6450)
- [x] Browser QA — qa/run-1.md RED (1 major, 3 minor, all fixed) → qa/run-2.md GREEN (34-row plan, 320–1536 px) → after the review fixes qa/run-3.md GREEN (3 consecutive drives, 121/0 each; found and fixed the lock-ordering defect)

## Build checklist (resume here after a context reset)
Backend
- [x] Domain: `AttendanceTerminal`, `AttendanceEntry`, `AttendanceDepartureSource`, `User` schéma fields, `Clinic.AttendanceEnabled`, 2 repo interfaces
- [x] Infrastructure: 2 configurations, DbSets + tenant filters, 2 repositories, DI, `User`/`Clinic` column config
- [x] Migration `AddAttendance` (strip `xmin`), `verify-schema` before/after on `clinic_staffleave_qa` — the 6 attendance drifts cleared, nothing new
- [x] Archive: `AttendanceTerminal` excluded (DEV-2); relay carries it (`Added`); `User` already off the archive and on the relay
- [x] Application: `AttendanceRules`, DTOs, commands (register/revoke terminal, schéma set/reset, profile, punch, correct, add), queries (status, settings, sheet, export, my attendance, team today), `SetTeamModulesCommand.Attendance`, `ClinicDto.AttendanceEnabled` ×7
- [x] Staff file figures (frontend reads `GET /team/attendance?userId=`)
- [x] API: `PointageController` (anonymous, cookie `apexa_borne`), `TeamController` actions; `team-modules` binds the command as is
- [x] Guard lists: `ExpectedAnonymous` (+2), `SecretProtectionCoverageTests` (+`SecretHash`), `DemoExemptionCoverageTests` (+export), `RateLimiting.PointagePolicy`
- [x] Unit suite unfiltered: 6450 passed / 0 failed / 6 skipped
Frontend
- [x] `lib/api/pointage.ts` (+ `clinics.ts` `attendanceEnabled`, `dashboard-blocks.ts` `teamToday`)
- [x] `/pointage` borne page (tiles → Arrivée/Départ → schema pad → confirmation), public route, no session gate
- [x] `components/team/schema-pad.tsx` (pointer events, keys 1–9, ≥44 px)
- [x] Mon profil « Schéma de pointage » card
- [x] Paramètres « Équipe »: attendance switch, bornes list + Retirer, « Faire de cet appareil une borne » (step-up), « Personnes qui pointent »
- [x] « Pointage » on /login and in the top bar when this device is a borne (`team/borne-link.tsx`)
- [x] /users « Présences » tab: today + month sheet (table from lg, CardList below) + Exporter + Corriger + Ajouter un pointage
- [x] Staff file: month figures + Présences (works with congés off too)
- [x] « Mes présences » — in Mon profil (DEV-3)
- [x] Dashboard « Équipe aujourd'hui » (admin, hideable; server key `teamToday`)

## Blast Radius
| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `User` new columns | aggregate root, audited, archived, relayed | archive scope, relay scope, `SchemaVerification`, user repo reads | must change — archive redaction of `AttendanceSchemaHash`; relay keeps it |
| 2 | `Clinic.AttendanceEnabled` | tenant root flag | `ClinicDto` 7 build sites, `ClinicConfiguration` | must change — all 7 sites |
| 3 | `SetTeamModulesCommand` | tri-state switch from part 1 | `TeamModulesCard`, settings version handling | must change — gains `attendance` |
| 4 | `TeamController` | part 1 controller | `ControllerAuthorizationCoverageTests` | must re-test |
| 5 | New anonymous controller | `/api/pointage` | `ExpectedAnonymous`, subscription gate, demo/relay fences | must change — reviewed allow-list |
| 6 | Realtime key `team` | part 1 key | `clinic-hub.ts`, resolver test | unaffected — same area, same key |
| 7 | `/users` tabs | part 1 tabs + main's guide | « Votre secrétaire » tour | must re-test |
| 8 | `/login`, top bar | shared chrome | every page | must re-test |
| 9 | Dashboard blocks | `DASHBOARD_BLOCKS` + server key set | dashboard customisation | must change — new block key both sides |
| 10 | Archive/relay secret guards | derived guards | `ClinicArchiveScopeTests` secret-shape scan | must change |

## Working tree note (start of session)
Clean: part 1 committed (256ff8ce + b1a35839) before branching.

## Files Changed
(see `git status` — every file belongs to this feature)

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| `AttendanceDepartureSource` declares `DeclaredBySelf = 1` now, unused until part 3 | the blueprint fixes the ints; appending later would leave a gap or reorder |
| `GET /api/team/terminals` is `GET /api/team/attendance-settings` (bornes + people in one read) | both cards of Paramètres → Équipe read them together; the POST/DELETE routes are as pinned |
| `PointageStatusDto.enabled` added | a borne of a cabinet that switched the pointage off must say so, not show « aucune personne » |
| `attendance-profile` takes `version` (as pinned) and `AttendancePersonSettingDto.version` carries it | `ChangeUserRoleCommand`'s precedent |
| Punch refusals `schema_wrong`/`schema_locked` carry `attemptsLeft`/`lockedUntil`; the client shows the server sentence | the sentence already says « Encore N essais » / « Réessayez dans N min »; the tile re-reads its lock from the status |
| A half-day accepted congé does not grey the tile nor refuse the punch | she works the other half |
| Registering a borne on a device that already is one retires the old borne | its secret is about to be overwritten in the cookie; the old row could never punch again |
| A wrong schéma is journalled as `job|borne <name>`, a right one as the person | nobody proved who typed a wrong one |
| « Personnes qui pointent » switch sends `version: 0` | one idempotent switch, the last press is the intent — `TeamModulesCard`'s shape; a login bumps the row's `xmin`, so a version would 409 the admin after every sign-in |
| A closed line's hours are its « Corriger » button; a red line carries an explicit « Corriger » | a button on every line of a month table is noise; the spec's « visible on a red line » holds |

## Significant Deviations
1. **DEV-1 — a « small » spec that is not small.** The surface is ~45 files (2 tables, an anonymous borne endpoint, a month sheet, an export, six screens). The owner asked to start without waiting; built in one pass, no scope cut.
3. **DEV-3 — « Mes présences » lives in Mon profil, not in « Mes congés ».** `/conges` exists only while the congés are on, so with the pointage alone a secretary would have no way to her own lines. Mon profil already holds her schéma card and is reachable by every role.
2. **DEV-2 — `AttendanceTerminal` is EXCLUDED from the archive rather than its `SecretHash` redacted.** The spec says « redacted in the archive ». `SecretHash` is non-null and unique, so a redacted (null) value cannot be restored, and a restored borne row names a device whose cookie no longer matches anything. Excluding the table keeps the secret out of the zip (the spec's intent) and leaves nothing unrestorable; the lines keep the borne's name (`AttendanceEntry.TerminalName`). `User.AttendanceSchemaHash` needs nothing: `User` is already excluded from the archive and carried by the relay.

4. **DEV-4 — review fixes that move the contract (2026-11-05).** The punch takes `expected` and refuses a stale tile
   (`409 punch_state_changed`); a malformed pattern is `400 schema_invalid` and costs no try; the punch runs in one
   transaction holding the account's row (`SELECT … FOR UPDATE`), so parallel guesses are each counted; re-registering a
   device sends `replacingTerminalId` (from the status read) because the cookie never reaches `/api/team/terminals`;
   « Dernier pointage » is written once per cabinet day (« Utilisée le … »), sparing the journal a borne row per punch;
   the server's `Initials` now follow the web's `initialsOf` (first two words). Spec § API Contract updated.

## Review fixes (2026-11-05)
57 challenged findings: **54 applied**, 3 skipped as spec decisions (AC-7's 15-min lock without escalation · no pepper on
`AttendanceSchemaHash`, which the spec copies to the PC de secours · the borne cookie re-issued unchanged, per § Edge
Cases). Finding 56 is half applied: the borne hides the trail; asking for the old schéma before a change is a new
capability, not taken. Finding 38 is fixed the other way round: the server's initials now match `initialsOf`, since
`InitialsAvatar` forbids pre-computed initials. Details in `reviews/feature-review.md`.

## Test Plan
| AC | Action | Target file | Notes |
|----|--------|-------------|-------|
| AC-1 | Add scenarios | `UnitTests/Features/Team/StaffLeaveCommandTests.cs` | off by default; switch on leaves congés alone; congés off leaves pointage alone |
| AC-1, AC-3, AC-15 | New class | `UnitTests/Features/Team/PointageBorneTests.cs` | module off · unknown/blank secret · request scoped to another cabinet · lapsed / missing subscription · no ledger read when not enforced |
| AC-2 | New class | `PointageBorneTests.cs` | register: only the SHA-256 stored, name trimmed, admin only, name required, module off; re-register retires this cabinet's previous borne, never another's |
| AC-2 (Retirer), AC-5, AC-4 | New class | `UnitTests/Features/Team/AttendanceAdminCommandTests.cs` | revoke (own / foreign / secretary); reset clears schéma + lock; tracked override stored as « follow the role » when equal, version passed; Mon profil schéma 4–9 distinct dots, untracked refused |
| AC-4, AC-6 | New class | `PointageBorneTests.cs` | tiles = active tracked only; out / in « since » / leave; half-day leave → out; needs schéma; locked; initials; order |
| AC-6, AC-8 | New class | `PointageBorneTests.cs` | arrival opens today's line; second tap closes it (source Borne); yesterday's open line never closed; lunch → second pair; full-day congé refuses, half-day does not; congé never blocks a departure; realtime `team` sent, a hub failure does not undo |
| AC-7 | New class | `PointageBorneTests.cs` + `AttendanceRulesTests.cs` | wrong schéma counted, journal names the borne; malformed pattern counts without the hasher; 5th locks 15 min; locked refuses unchecked; success clears; rehash |
| AC-9, AC-11, AC-12 | New class | `UnitTests/Features/Team/AttendanceQueryTests.cs` + `AttendanceRulesTests.cs` | sheet (team / one person / current month / bad month / foreign account / admin only / module off); CSV rows with borne hours + motif; « Mes présences » own lines only; « Équipe aujourd'hui » order + pending count only with congés on; réglages; sheet builder days/closed days/congés/totals; PeopleFor |
| AC-10 | New class | `AttendanceAdminCommandTests.cs` | correct keeps punched hours, motif required, bad / backwards hours refused, foreign line not found, secretary refused, version passed; add a day (past only, no invented departure, foreign account refused) |
| AC-14 | New class | `AttendanceRulesTests.cs` | 24 h parse/format, UTC+1, line on the cabinet's day; hours to come refused |
| API contract | New class | `UnitTests/Api/PointageControllerTests.cs` | each code → its status (401/402/403/409/423/400); refusal body carries `attemptsLeft` / `lockedUntil`; cookie re-issued only on a landed punch (HttpOnly, Strict, Path, 400 d); secret from the cookie, never the body |

**Coverage notes**
- AC-2 « Pointage » on login/top bar, AC-13 live refresh in the browser, AC-16 device: frontend only, no test runner in `web/` — covered by qa/run-2.md (GREEN) + tsc / check:responsive / build. The server half of AC-13 (the `team` broadcast) is unit-tested.
- Repository SQL (`FindActiveBySecretHashAcrossClinicsAsync` filtering retired bornes, `FindLatestOpenAsync`) and the migration: no database in this suite — `verify-schema` before/after (Build checklist) + browser B12.

## Tests Run
| Suite | Filter | Result |
|-------|--------|--------|
| Unit (whole suite, Release, scratch `BaseOutputPath`) | none | 6590 passed, 0 failed, 6 skipped |
| New-file warnings | `--no-incremental` build, grep on the 5 files | 0 |
