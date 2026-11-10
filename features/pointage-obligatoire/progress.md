# Progress — Pointage obligatoire (part 3 of 3)

**Branch:** `feature/pointage-obligatoire` (worktree `.claude/worktrees/conges-secretaires`), from `13302491`
(part 2 committed, not pushed). Built on part 2, not `main` — part 2 is required.

- [x] Implementation
- [x] Tests (handled by /test-small-feature)

## Working tree note (start of session)

Clean at start (`git status --short` empty on `feature/pointage-obligatoire`).

## Blast radius (written before the first edit)

| Symbol touched | Who else consumes it | Verdict |
|---|---|---|
| `PunchCommandHandler` (arrival path) | borne page, lock gate pad, sign-out pad | must re-test — the arrival now waits for a declaration when an earlier line is open |
| `AttendanceEntry` (+ `DeclareDeparture`, `DeclaredLastSaveUtc`) | sheet, totals, CSV export, `TeamTodayRules`, admin correction | must re-test — `Correct` on a declared line must read « Corrigé » |
| `AttendanceEntryDto` / `AttendanceTotalsDto` (new fields) | attendance-panel (admin, staff file, « Mes présences »), CSV | additive — re-test the sheet |
| `Clinic.AttendanceLockEnabled` + `ClinicDto` (7 hand-mapped sites) | Create ×2, Join ×2, RegenerateCode, Update, GetUserStatus | must update all 7 — grep `AttendanceEnabled =` |
| `SetTeamModulesCommand` (tri-state, + `AttendanceLock`) | team-modules-card | an omitted key must stay « unchanged » |
| New write gate (`AttendanceLockMiddleware`) | **every non-GET `/api` call of a tracked secretary** | the reach is the whole app — exemptions by attribute + coverage test both ways; only tracked accounts, only inside opening hours |
| `NotificationCategory` (+ `AttendanceDepartureMissing`, appended) | `StaffNotificationRules.ReachesALockedPhone` + `PushLabel`, bell icon/tone map | classify in both switches (they throw on an unclassified member) |
| `NotificationTargetKind` (+ `Attendance`, appended) | bell click routing in `dashboard-header.tsx` | add the route |
| `INotificationGenerator` (+ method) | `NotificationGenerator`, `PushNotificationGeneratorDecorator`, test fakes | implement in both |
| `IAuditEntryRepository` (+ last-save read) | `AuditEntryRepository`, test fakes | additive |
| `IClinicRepository` (+ lock inputs read) | `ClinicRepository`, test fakes | additive |
| User menu « Se déconnecter » | `dashboard-header.tsx` only | must re-test |
| Root layout (gate mounted) | every signed-in screen | must re-test: not over `/login`, `/change-password`, `/pointage`; under the session lock |

## Decisions and deviations (reversible, logged per the owner's « proceed, log them »)

- **DEV-1 · `clock-status` lives at `GET /api/pointage/me/clock-status`, not `/api/team/me/clock-status`.** The borne
  cookie is scoped to `/api/pointage` (part 2, on purpose: no other request carries it), and `canClockOutHere` is
  « this device is one of my cabinet's bornes » — only a request under `/api/pointage` can answer it honestly. Same
  response shape, plus `hasSchema`.
- **DEV-2 · The declaration step comes after the schéma, not before.** The 409 carries her previous day's arrival and
  last save; answering it before the schéma would show them to anyone who taps her tile. Order on the borne: name →
  schéma → « Votre départ d'hier n'a pas été pointé » → Valider → arrival. The arrival still waits for the hour.
- **DEV-3 · The lock also lifts when the cabinet may not record new work (subscription).** Otherwise an expired
  cabinet's secretary would face a lock the borne itself refuses to lift, and lose the reads an expired cabinet keeps.
- **DEV-4 · No schéma yet:** the lock says so and offers « Créer mon schéma »; the gate does not cover « Mon profil »
  in that case, else she could never lift it.
- **DEV-5 · The declared hour's « last save » is stored on the line (`DeclaredLastSaveUtc`)** — what she was shown,
  and what the admin's sheet prints beside « Déclaré » without a journal read per line. One nullable column, same
  migration.
- **DEV-6 · The reminder job runs every 5 minutes** (blueprint said 15), so « 15 min after closing » lands within
  5 minutes of it rather than 15.
- **DEV-7 · Half-day congé lifts the lock too** (« no accepted congé today »): which half she works is not recorded.
- **DEV-8 · « Last save » ignores the pointage's own rows and sign-in bookkeeping** (`AttendanceEntry`,
  `AttendanceTerminal`, `User`) — measured on the dev journal, `User` rows are 84 % `LastLoginAt`.
- **DEV-9 · An admin is never locked**, even when tracked by override: the switch that lifts the lock and « Ajouter un
  pointage » (AC-5) are hers, so a locked admin with a broken borne could never get back in.
- **DEV-10 · The lock is a Radix modal layer**, not a plain overlay: a form left open beneath keeps its typing and gets
  no presses, and the pad's « Heure de départ » keeps the focus a lower dialog would otherwise trap.
- **DEV-11 · Exempt writes kept to what a locked screen can reach**: sign-in/out and session (Auth, class), the borne
  (Pointage, class), her schéma, her congés (submit + cancel), the bell (4), push registration (3), the PC de secours
  device report. Compute-only POSTs are not exempt — nothing under the lock reaches them.
- **DEV-12 · « Ajouter une borne » picks the device (owner, 2026-11-06).** « Faire de cet appareil une borne » made
  the admin's own computer the borne, which is rarely the reception PC. The dialog now lists the cabinet's signed-in
  devices (live sessions of its active accounts, newest first, demo and PC-de-secours set-up left out) plus « Cet
  appareil ». Choosing another device stores a borne **with no secret** (`PendingSessionFamilyId`); that device's next
  clock-status read sees `borneWaiting` and `POST /api/pointage/me/claim-borne` mints the secret **there** and sets the
  cookie — the secret never crosses the admin's machine. A waiting borne is not a punch point: `HasActiveAsync` and the
  lock's « bornes » list skip it. Choosing the same device again retires the earlier wait; a device signed out before
  claiming reads « Appareil déconnecté … » in the list. Claiming retires the borne this device already was. The device
  name is now sent on **every** sign-in (it was sent only with « Rester connecté »), so the list can say « Chrome sur
  Windows »; a device that signed in before this reads « Appareil sans nom ». Migration `AddBorneAssignedToDevice`
  (`SecretHash` nullable, two columns, a filtered index; `Down` deletes waiting rows first). `ClaimBorne` is lock-exempt
  (the locked secretary's own PC may be the chosen device).

## Files changed

**Backend** — Domain: `Clinic` (+`AttendanceLockEnabled`), `AttendanceEntry` (+`DeclareDeparture`, `DeclaredLastSaveUtc`,
`IsDeclared`), `StaffNotification.ForDepartureMissing`, `NotificationCategory.AttendanceDepartureMissing = 24`,
`NotificationTargetKind.Attendance = 11`, five repository reads (clinic switches, line on a day, open lines of a day,
accepted congé covering a day, last save in the journal, a reminder since). Application: `AttendanceLockRules` +
`AttendanceLockFacts`, `AttendanceLockEvaluator`, `GetMyClockStatusQuery`, punch `DeclaredPreviousDeparture`,
`AllowsBeforeClockInAttribute`, `IRelayLocalStatus.IsPcDeSecours`, DTO fields (`ClockStatusDto`, `PreviousDayDto`,
`DepartureDeclared`/`DeclaredLastSave`, `DeclaredDepartures`, `AttendanceLockEnabled` ×7 `ClinicDto` sites +
`TeamModulesDto`), `SetTeamModulesCommand.AttendanceLock`, the reminder in `INotificationGenerator` / generator / push
decorator / `StaffNotificationRules` (pushed, label « Départ non pointé »). API: `AttendanceLockMiddleware` (after the
subscription gate), `AttendanceDepartureReminderJob` (every 5 min, `RunsClinicJobs`), `GET /api/pointage/me/clock-status`,
the punch's 409 body (`day`, `arrival`, `lastSaveAt`), 13 `[AllowsBeforeClockIn]` sites. Infrastructure: configs +
migration `20261105223612_AddAttendanceLockAndDeclaredDeparture` (two additive columns, no `xmin`).

**Web** — `lib/api/client.ts` (`ApiError.details`, `ClockInRequired`, `onClockInRequired`), `lib/api/pointage.ts`,
`lib/api/clinics.ts`, `lib/api/types.ts`; `components/team/punch-flow.tsx` (new), `clock-in-gate.tsx` (new),
`sign-out-prompt.tsx` (new), `attendance-panel.tsx`, `team-modules-card.tsx`, `notification-panel.tsx`,
`dashboard-header.tsx`, `app/pointage/page.tsx`, `app/layout.tsx`.

## Auto-Approved Deviations (build/guard upkeep)

- `PointageBorneTests` constructs the punch handler with the new `IAuditEntryRepository` mock (compile only).
- Three hand-written fakes gained the new repository members (`FakeAuditEntryRepository`, two `FeedRepository`s) — compile only.
- `PushFanOutTests` and `RelayFenceCoverageTests` classify the reminder (pushed; bell rows only under a fence).

## Quality gate (end of implementation)

| Check | Result |
|---|---|
| `dotnet build` (Release, full rebuild) | 0 errors · no warning in a touched file (76 pre-existing) |
| Unit suite (unfiltered) | 6604 ✅ · **1 ❌** · 6 skipped — the ❌ is deferred below |
| `npx tsc --noEmit` | clean |
| `npm run check:responsive` | 86 / 86 |
| `npm run build` | green |

## Deferred to /test-small-feature

- **Re-point** `PointageBorneTests.A_Line_Left_Open_Yesterday_Is_Never_Closed_The_Arrival_Opens_A_New_One` — the arrival now
  answers `previous_departure_missing` first (stale expectation, the behaviour changed on purpose).
- `AllowsBeforeClockIn` coverage test, both directions (AC-3), with its red-proofs.
- `AttendanceLockRules` — one test per lifting condition + the PC-de-secours and admin cases; `HoursOn` / `IsInsideOpeningHours`
  / `ReminderDueUtc` (unset hours, closed day, break, midnight).
- `AttendanceLockEvaluator` — stops at the first lifting fact (no query for an admin).
- Middleware — 423 body, exempt endpoint passes, reads pass, an unmatched path passes, untracked passes.
- Punch — declaration missing → 409 body; invalid hour (before arrival, malformed) → 400; declared → line closed `DeclaredBySelf`
  with the last save, then the arrival; wrong schéma still counted before any of it.
- `AttendanceEntry.DeclareDeparture` bounds; `Correct` on a declared line → « Corrigé ».
- `GetMyClockStatusQuery` — module off / untracked → not locked; `canClockOutHere` only for her cabinet's active borne.
- Reminder job — before due / after due / deduped / closed day / hours unset / PC de secours / one cabinet's failure.
- `SetTeamModulesCommand.AttendanceLock` tri-state; the 7 `ClinicDto` sites carry it.
- `StateLabel` (CSV « Déclaré · dernière action 17:46 »); `Totals.DeclaredDepartures`.

## Owner's polish (after browser run 3 — « fix them »)

The four off-plan observations of `qa/run-1.md`, all fixed:
- **CSV « État »** keeps both facts of a closed line: « Corrigé · Déclaré · dernière action 17:46 »
  (`AttendanceRules.StateLabel`).
- **Lock on an ordinary device:** the decorative second icon is gone.
- **Lock on a borne / sign-out window with the pad:** one headline — `PunchFlow` gained `showHeading`, off in both.
- **Sign-out window with the pad:** « Annuler » moved from inside the scrolling body to the pinned footer, beside
  « Se déconnecter sans pointer » (`PunchFlow.onBack` is optional now).

## Test Plan

| Area | Action | File | Covers |
|---|---|---|---|
| Punch: earlier day left open | **Modified** (re-pointed) + 6 scenarios | `Features/Team/PointageBorneTests.cs` | 409 body after the schéma; journal window + exclusions; wrong schéma counted first, nothing revealed; declared → `DeclaredBySelf` + last save, then the arrival; 4 refused hours; today's line still departs |
| Punch wire | 3 scenarios | `Api/PointageControllerTests.cs` | 409 with `day`/`arrival`/`lastSaveAt`; declared hour reaches the command; clock-status sends the device's cookie |
| Lock rule, hours, reminder due, entry, export, bell row | New class | `Features/Team/AttendanceLockRulesTests.cs` | every lifting fact; unset/unreadable/closed/inverted hours; window bounds; due = closing + 15; `DeclareDeparture` bounds; correction → « Corrigé »; `StateLabel` (incl. Corrigé + Déclaré); totals; `ForDepartureMissing` |
| Evaluator | New class | `Features/Team/AttendanceLockEvaluatorTests.cs` | locked case; admin / untracked / inactive / PC de secours cost no query; switches; hours; line today spares the congé read; congé; lapsed subscription; fenced cloud |
| Clock status | New class | `Features/Team/ClockStatusQueryTests.cs` | active bornes only; present → `presentSince`; her borne vs another cabinet's; module off; untracked; no account |
| 423 gate | New class | `Api/AttendanceLockMiddlewareTests.cs` | 423 body; reads, exempt, unroutable, non-`/api`, admin, anonymous, unscoped pass; after arrival passes; position in `Program.cs` |
| Exemptions | New class (guard) | `Api/AttendanceLockExemptionCoverageTests.cs` | reviewed set both directions; clinic work never open; reasons; red-proofs (newly opened write; blind to reads) |
| Reminder job | New class | `Api/AttendanceDepartureReminderJobTests.cs` | at/before due; once per line; closed day; pointage off; no hours; PC de secours; one cabinet failing; writes no pointage row |
| Push | 1 scenario | `Features/Notifications/PushFanOutTests.cs` | the reminder pushes to her alone, label = bell title, no hour |
| Team modules | 2 scenarios | `Features/Team/StaffLeaveCommandTests.cs` | lock on by default, turns off alone; omitted = unchanged |

Coverage notes: the lock screen, the sign-out prompt, the declaration step and the sheet are `.tsx` — covered by the
browser plan (`qa/run-1..3.md`, and the re-run after the polish). `ClinicDto` ×7 sites: `D3` reads it over the wire.

## Tests Run

| Suite | Filter | Result |
|---|---|---|
| Unit (whole suite, Release, temp `BaseOutputPath`) | none | **6710 passed · 0 failed · 6 skipped** (6604 before) |
| Web | `tsc` · `check:responsive` · `next build` | clean · 86/86 · green |

## Review fixes (reviews/feature-review.md, challenged — all 41 applied, 2026-11-06)

Owner's calls: « Congés » stays open under the lock (AC-3 over AC-2's « every screen »); « dernière action » ignores
`DeviceRegistration` **and** `StaffLeaveRequest`; Finding 28 is a spec wording fix only (Out of Scope narrowed).

| Area | What changed | Findings |
|---|---|---|
| Lock rule | `HasActiveBorne` fact — a cabinet with no borne left is not locked (read after the congé, before the subscription); `AttendanceLockReads` lets clock-status pass the two reads it already made; `FactsAsync` private; subscription judged at `nowUtc` | 1, 21, 30, 34 |
| Journal | `NotWorkInTheJournal` + `DeviceRegistration`, `StaffLeaveRequest` | 2 |
| Punch | a right schéma's counter reset is saved before the 409 / 400; every earlier open line is declared, oldest first (`ListOpenBeforeAsync`), one 409 per day; the 409 sentence names the day (« d'hier » / « du vendredi 30 octobre »); server and borne share one « doit être après l'arrivée » sentence; `Refused` carries `PreviousDay` | 6, 10, 31, 39 |
| Reminder job | narrow untracked read (`ListAttendanceHoursAsync`), `dayStart` once; dead PC-de-secours check removed; push only once the bell row is written (`Task<bool>`) | 20, 27, 36 |
| Seams | `IRelayLocalStatus.IsPcDeSecours` abstract; middleware `InvokeAtAsync` + query `NowUtc` so tests pin the instant; one `WriteGate` (prefix + read) for the four write gates; one `WorkingHoursSerializer.DayOf` for the three weekday lookups | 22, 32, 37, 38 |
| Lock screen | Overlay (scroll lock, popups wait), focus on the title, pad not autofocused, `covering` on the context (chat notices stand down — provider moved above `StaffChatProvider`), `/conges` uncovered + « Demander un congé », queued re-read (+ stale answer ignored once off), `clinics` broadcast, « Réessayer » after an ending refusal, toast after the lock lifts, version gate `z-[56]` above it | 3, 4, 5, 7, 8, 9, 13, 17, 18 |
| Sign-out prompt | footer + close held while a departure is in flight, sign-out once, one live region mounted from the start, pad capped by height | 11, 12, 14 |
| Declaration step | « Valider » takes « 930 » before the field commits (`timeFromTyped`), typing re-arms the idle timer, `attendanceDayLabel` reused | 15, 16, 33 |
| Small | switch row `coarse:pt-2`; « Déclaré » chip beside « Corrigé »; the strip keeps `StatStrip`'s own ladder; a « Départ non pointé » push opens `/mon-profil`; doc comments back in place; theory split | 19, 23, 24, 29, 35, 40, 41 |
| Docs | API map row, `web/lib` hooks, component map, spec Out of Scope | 25, 26, 28 |

**Gates after the last edit:** unit suite (whole, Release, temp output) **6716 passed · 0 failed · 6 skipped** ·
`check:responsive` 86/86 · `tsc` clean · `next build` green (new classes checked in the built CSS) · no new warning in
a touched file. No migration. **Browser plan not re-run** after these fixes.

## « Ajouter une borne » chooses the device (DEV-12, 2026-11-06)

**Gates after the last edit:** unit suite (whole, Release, temp output) **6743 passed · 0 failed · 6 skipped** (+27:
`BorneAssignmentTests`, 3 clock-status cases, the lock-exemption row) · `check:responsive` 86/86 · `tsc` clean ·
`next build` green (new classes in the built chunks) · migration `AddBorneAssignedToDevice` applied to the demo DB only
(no `xmin` column scaffolded). **Browser:** run 7 RED (F1 — sideways scroll at 320 px, fixed) → run 8 **GREEN 160/160**.
