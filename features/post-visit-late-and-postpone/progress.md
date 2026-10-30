# Progress: Retard et report

**Started:** 2026-10-29
**Type:** Small
**Branch:** feature/post-visit-late-and-postpone (worktree `.claude/worktrees/post-visit-late`, from `main` 23ec0cfe)

## Status
- [x] Implementation
- [x] Quality checks — `dotnet build` 0 errors (no new warnings in changed files) · `tsc --noEmit` 0 · `check:responsive` 81/81 · `npm run build` ✓
- [x] `verify-schema` on a copy of the QA database, before and after the migration: identical (the 5 pre-existing drift lines are dev data — audit chain, plan steps, fiche links, messaging month, key ring)
- [x] `node e2e/scripts/check-coverage.mjs` — exit 1, **pre-existing** (64 tier-0 scenarios with no test; no e2e file or scenarios.md touched here)
- [x] Blast radius closed (19 rows: 15 changed, 4 to re-test)
- [x] Tests — 37 new, full suite 6 040: 6 033 pass, 6 skipped, **1 fails and is pre-existing** (see below)
- [x] Browser QA — `qa/run-1.md` **GREEN**, 41/41 checks over 26 scenarios, isolated stack on a DB copy; looked at
  320×640 · 390×844 · 667×375 · 820×1024 · 1180×820 coarse · 1440×900 · 1536×730
- [x] Gate re-run after the last edit: `tsc` 0 · `check:responsive` 81/81 · `npm run build` ✓
- [x] Regression rows D5–D8 (`qa/run-1b.md`) **GREEN** — tablet widths, agenda « Terminé » on an absent visit, merge, 4 e2e specs

## Working tree note (start of session)
The main checkout (`feature/security-remediation`, 184 commits behind `main`, 192 dirty files) was left untouched;
all work is in the worktree. Nothing is committed.

## Blast Radius
| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `Appointment.LateArrivalMinutes` + erasers | new column, 4 mutators clear it | job, update, archive | must change — invariant test |
| 2 | `UpdateAppointmentCommand.LateArrivalMinutes` | tri-state field | every caller omits it | unaffected — omitted = unchanged |
| 3 | `AppointmentDto` projections | 4 hand-built sites | Get, GetList, Create, Update | must change — all four + source scan |
| 4 | « end of séance » (`ExpectedEnd`) | shared rule | job residuals ×2, review timing ×3, `VisitClosureInput.EndUtc` | must change — all through `ExpectedEnd` (the « just completed → now » branch stays) |
| 5 | the booked slot | trigger column, exclusion constraint, `FindCollisionAsync`, duplicate merge | — | unaffected — deliberately untouched |
| 6 | `AllowedTransitions[NoShow]` += Completed | domain table | `ManualNextStatusesFrom` → 4 projections, status tests | must change — all existing transition tests still green |
| 7 | collision guard « regains its slot » | handler guard | reactivating a cancelled RDV | must re-test (named `slot_taken` instead of the DB's 409) |
| 8 | `PlanBookingRelease` on « Absent » | clears plan links | « Absent → Terminé » | unaffected — links stay released (notes.md) |
| 9 | `post-visit-review-popup.tsx` | the popup | `e2e/lib/goto.ts` (« Plus tard » by name), `booking-browser.spec.ts` | must change · e2e must re-test |
| 10 | `appointment-quick-actions.tsx` | agenda ⋯ | grid + phone agenda | must change |
| 11 | `visit-closure-list.tsx` | « À clôturer » | table + card trees | must change — both trees |
| 12 | `edit-appointment-dialog.tsx` payload | tri-state save | 409 / overlap / hours confirmations | must change — sent only when changed |
| 13 | `appointment-merge.ts` « Statut » section | « Recharger » reconcile | edit dialog only | must change — retard joins the statut section |
| 14 | `CarriedAppointment` (server switch) | carried form | PC de secours switch | must change — optional key, older drafts restore `null` |
| 15 | `appointment-calendar.tsx` block | agenda block | grid + phone | must change — « +15 » + accessible name |
| 16 | `ExportTables.Appointments` | CSV | no header test | must change — « Retard (min) » |
| 17 | clinic archive (metadata-driven) | `ClinicArchiveStore` | restore of an older archive | unaffected — new scalar travels; old archives restore null |
| 18 | PC de secours copy | row feed | — | must re-test (row-level JSON) |
| 19 | `appointment-labels.ts` `LATE_ARRIVAL_STATUSES` | TS mirror | domain set | must change — mirror test, red-proved |
| 20 | `NoShow → Completed` in `allowedNextStatuses` | served transition list | agenda ⋯ « Marquer comme » (no « quand même » there) | must re-test — QA D6 (named refusal, statut kept) |
| 21 | « À clôturer » presence cell, one more button | table on `lg:` | 1024–1279 with the rail open, coarse 44 px | must re-test — QA D5 |
| 22 | retard inside the « Statut » merge section | « Recharger » reconcile | edit dialog | must re-test — QA D7 |
| 23 | `UpdateAppointmentCommand` + popup + carried form | e2e hot paths | `appointment`, `concurrency`, `booking-browser`, `carried-draft-browser` | must re-test — 18 pass, EDIT-05 skips by its own timing guard |

Rows 20–23 were added on 2026-10-30, after the owner asked whether the blast radius was closed: the first pass
listed the changed files but not the screens that read the changed transition list and row width.

## Files Changed
- Domain: `Entities/Appointment.cs`
- Application: `DTOs/AppointmentDto.cs`, `Features/Appointments/{VisitClosure,VisitClosureReader}.cs`,
  `Commands/{Update,Create,CreateRecurringSeries}AppointmentCommand*.cs`, `Queries/{GetAppointment,GetAppointments}Query.cs`,
  `Common/Csv/ExportTables.cs`
- Infrastructure: `Repositories/AppointmentRepository.cs`, migration `20261029212122_AddAppointmentLateArrivalMinutes` + snapshot
- Web: `lib/api/{types,appointments}.ts`, `components/{appointment-labels,appointment-merge}.ts`,
  new `components/appointment-late-arrival-chips.tsx`, new `components/appointment-postpone-dialog.tsx`,
  `components/{post-visit-review-popup,appointment-quick-actions,edit-appointment-dialog,appointment-calendar}.tsx`,
  `components/visits/visit-closure-list.tsx`
- Tests: new `Domain/AppointmentLateArrivalTests.cs`, new `Features/Appointments/UpdateAppointmentLateArrivalTests.cs`

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| `PostponeForm` and `PostponeAppointmentDialog` share one file | one form, one wrapper; nothing else imports the form alone |
| « Pas encore de date » is labelled « Mettre en liste d'attente » | the label states what the press does (the RDV is cancelled into the list) |
| Popup link reads « Noter un retard » | « Signaler l'arrivée » already exists in the agenda ⋯ (staff chat) and means « tell the dentist » |
| `Reschedule` compares to the minute | a form rebuilds the instant without seconds; that must not erase a retard |
| The edit dialog's retard is part of the « Statut » reconcile section | `main` reconciles the RDV form by section since `clinic-pc-copy` D23; the blueprint predates reading it |
| Tablet toast: the three answers sit in the toast body | sonner 1.7.4 renders a node there; a programmatic `toast.dismiss` does not call `onDismiss`, so no snooze |
| No « Créneau libre » line in the form | the overlap hook reports nothing both for a free slot and a failed read |

## Significant Deviations
None.

## Fixed before the browser pass (found while writing the QA plan)
- `PostponeForm` re-read the visit right before saving and sent **that** version, so a colleague's move in between
  was silently overwritten — the blueprint's « Reporter sends the version through `useConflict` » was not true.
  It now sends the version it was opened on; a 409 shows the server's sentence with « Recharger », which hands the
  host the fresh read (`onReloaded`) and keeps the typed date and time.

## Known pre-existing failure
`StaffChatPushQueueTests.The_Dispatcher_Is_Woken_Just_After_The_Grace_Ends` fails when run during the
quiet-hours window: `PushFanOut` defers `SendNotBefore` past quiet hours and the test asserts a wake right
after the grace. Untouched by this change; measured at 22:36 Tunis.

## Free slots in « Reporter » (2026-10-30, blueprint-free-slots.md, owner picked option A)

### Blast radius (written before the first edit)
| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| F1 | `AppointmentScheduling.FindLeaveCoveringAsync` → day rule extracted to `LocalDaysOf` | private helper, now shared | `CheckWorkingHoursAsync` ← create · update · recurring series | must re-test — `DoctorLeaveBookingGuardTests` in the unfiltered suite |
| F2 | `DoctorLeave.Covers(from, to)` | new domain predicate | the repository's SQL overlap (same rule in SQL) | unaffected — additive; the agreement test holds the two together |
| F3 | `FreeSlotFinder` | new | none yet (online booking later) | unaffected — new |
| F4 | `GetFreeSlotsQuery` | new query in `Features/Appointments/Queries` | `RealtimeResourceResolver` derives keys from `.Commands` only | unaffected — re-run `RealtimeResourceResolverTests` (suite) |
| F5 | `AppointmentsController` `GET {id:guid}/free-slots` | new action | `ControllerAuthorizationCoverageTests`, subscription / money-mask gates (GET, unmarked) | must re-test — suite |
| F6 | `PostponeForm` date/time section | shared form | popup step · agenda ⋯ dialog · « À clôturer » dialog | must change + re-test all three hosts |
| F7 | popup postpone step wrapped in `DialogBody` | layout | `dialog-owns-one-scroller` check, 730 px footer | must re-test |
| F8 | `nextOpenDays` / `isOpenOn` deleted | client copy of the hours | only `PostponeForm` | must change |
| F9 | `walk.mjs` time-field selectors | QA driver | A2, B1, B2, B7, C4 | must change — the time field now sits behind « Autre heure… » |

### Files changed
| Layer | Files |
|---|---|
| Domain | `Entities/DoctorLeave.cs` (`Covers`) |
| Application | `Features/Appointments/FreeSlotFinder.cs` (new) · `Queries/GetFreeSlotsQuery.cs` (new) · `DTOs/FreeSlotsDto.cs` (new) · `AppointmentScheduling.cs` (`LocalDaysOf` extracted) |
| API | `Controllers/AppointmentsController.cs` (`GET {id}/free-slots`) |
| Tests | `UnitTests/Features/Appointments/FreeSlotFinderTests.cs` (new — incl. the agreement test and the query tests) |
| Web | `lib/api/types.ts` · `lib/api/appointments.ts` (`freeSlots`) · `lib/hooks/use-free-slots.ts` (new) · `components/appointment-postpone-dialog.tsx` |
| Docs / QA | `spec.md` (AC-12–14) · `notes.md` · `web/components/CLAUDE.md` · `web/lib/CLAUDE.md` · `api/ClinicManagement.Application/CLAUDE.md` · `qa/plan.md` · `qa/walk.mjs` |

### Deviations from `blueprint-free-slots.md` (auto-approved, behaviour-level, recorded for review)
| Blueprint said | Shipped | Why |
|---|---|---|
| congé and full chips « shown and not clickable » | pickable; picking one states why (« Le praticien est en congé ce jour-là. ») and keeps « Autre heure… » | a disabled chip states nothing, and the override path (typed time → named refusal → « Reporter quand même ») is a capability (§ 0); the calendar would reach the same day anyway |
| « the calendar fetches one day » | a calendar day outside the window loads the 14 days **from** it | the chips then show that week, so the next pick is one tap; same endpoint, no second read shape |
| « the first free start marked » | not marked | the grid is in time order, so the first cell is the earliest; a second highlight beside « choisi » means two things |
| popup modified to wrap the step in `DialogBody` | `PostponeForm` renders its own `DialogBody` | one place for all three hosts; the popup needed no edit |
| « Reporter » labelled « Reporter à 10:30 » | « Reporter »; the picked time stays highlighted | measured: the popup footer is 398 px and « Retour » + « Mettre en liste d'attente » + « Reporter à 09:00 » need 426 (459 with « Reporter quand même ») — run 3 found « Retour » clipped. The footer also wraps from `md:` now, so no label can clip it again |

### Results
| Gate | Result |
|---|---|
| Unit suite (unfiltered, Release) | 6051 passed · 0 failed · 6 skipped (pre-existing) — incl. `FreeSlotFinderTests` and its agreement test (red-proved) |
| `tsc --noEmit` · `check:responsive` · `npm run build` | 0 · 81/81 · ✓ — after the last edit |
| Browser, run 3 (whole plan + F4) | 53/53 scripted checks green; the eye pass found « Retour » clipped in the popup's refusal state → fixed |
| Browser, run 4 (whole plan) | **GREEN** — 55/55, incl. the two new footer checks · `qa/run-4.md` |
| Re-check asked by the owner | found: a listed time could pass on screen and the server accepts a past time; a listed time taken after the read left the list stale → re-checked at the press, list re-read on a refusal |
| Browser, run 5 (whole plan + F5–F7) | 58/58 scripted; the eye pass found the press's answer below the grid, off screen → moved to a pinned strip above the buttons · `qa/run-5.md` |
| Browser, run 6 (whole plan) | **GREEN** — 61/61, the « seen » checks now require the message on screen · `qa/run-6.md` |
