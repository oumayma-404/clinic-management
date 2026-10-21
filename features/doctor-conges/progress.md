# Progress: Congés du praticien

**Started:** 2026-10-20
**Type:** Small
**Branch:** feature/doctor-conges (worktree `.claude/worktrees/doctor-conges`, off `origin/main` 59e88ec9)

## Status
- [x] Implementation
- [x] Quality checks — `dotnet build` 0 errors / no new warning in touched files · unit suite 5 954 passed, 0 failed · `tsc --noEmit` clean · `check:responsive` 81/81 · `npm run build` (see below)
- [x] Blast radius closed (13 rows: 4 changed, 7 to re-test, 2 unaffected-derived)
- [x] Tests — 4 new classes, 35 tests; full suite 5 989 passed / 0 failed / 6 skipped (see Test Plan)
- [x] Browser QA — 4 runs on an isolated stack (`qa/run-1..4.md`); 5 findings fixed, run 4 GREEN (F5 fixed on the owner's go past the cycle cap)
- [x] `verify-schema` before/after on the QA copy: only the 4 `DoctorLeaves` lines disappear

## Blast Radius
| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `AppointmentScheduling.CheckWorkingHoursAsync` (+ `IDoctorLeaveRepository` param) | shared booking guard | Create, Update, CreateRecurringSeries handlers | must change — all 3 wired in this commit |
| 2 | the 3 handlers' constructors | positional ctor | 13 test construction sites in 10 files | must change — compile fix (empty mock ⇒ no congé ⇒ old behaviour) |
| 3 | `outside_working_hours` code reused for a congé | refusal code clients branch on | create dialog, edit dialog, agenda drag-move, recurring outcome, waiting-list promote | must re-test — « Continuer quand même » on a congé day, each door |
| 4 | `isDayOpenOnAgenda` (now hours **and** congé) | agenda day-open predicate | phone header strip, week header « fermé », hour-cell hatch | must re-test |
| 5 | hour-cell hatch now also asks `isDayOpenOnAgenda` | grid shading | every closed configured day (e.g. Sunday) | must re-test — a closed day is now hatched whole, as the existing comment already claimed it was |
| 6 | sticky date header split into wrapper + date grid + congé ribbon | agenda chrome | Semaine/Jour at every width, `agenda-scroll` check | must re-test — header stays sticky, no seam |
| 7 | phone Mois chip slots minus one on a congé day | measured cell budget | phone month view | must re-test — no half chip under the marker |
| 8 | `DoctorsController` new routes (`leaves`, `{id}/leaves…`) | API surface | `{id:guid}` routes on the same controller | unaffected — `guid` constraint, `leaves` cannot bind as an id |
| 9 | new commands under `Features.Doctors.Commands` | realtime key `doctors` | « Mon profil » / Paramètres refetch on `doctors` | unaffected — an extra harmless refetch; key already declared |
| 10 | `DoctorLeave` entity (ClinicId, FK Doctor/Clinic) | EF model | tenant filter test, archive scope, relay scope, purge plan, verify-schema diff | unaffected — all derived from the model; suite green (filter added) |
| 11 | `AuditLabels` | French entity names | « Journal d'activité » | unaffected — additive |
| 12 | migration `AddDoctorLeaves` | schema | shared dev DB | must re-test — `verify-schema` before/after on an isolated DB (scaffolded `xmin` column removed by hand) |
| 13 | « Congés » card in Mon profil + Paramètres → Médecins | new UI | both pages' existing cards | must re-test — layout at 320/820/1440 |
| 14 | agenda bar label hinges (QA F1): « Congé » + Google labels on the bar's width (`@container`, 76/84rem) | the one-row agenda bar | the week/day title (the only shrinkable item) | must re-test — no overflow 1280–1920, title never narrower than before (`F1-bar` row) |
| 15 | « book anyway » confirm titles (QA F3) | 3 doors reading `outside_working_hours` | create, edit, drag-move | must change — all 3 now « en dehors des disponibilités »; no test asserts them |
| 16 | `noValidate` on the congé form (QA F2) | one form | — | unaffected — our own range check now runs |
| 17 | congé commands moved to `Features/DoctorLeaves` → realtime key `doctorleaves` (review #3) | realtime contract | `clinic-hub.ts`, the two congé hooks, `RealtimeResourceResolverTests` | must change — key declared both sides; suite green; « Mon profil » no longer reloads on a congé save (RV-profil) |
| 18 | agenda bar: `@container` moved to a wrapper; Aujourd'hui/Filtres/view labels on a 56rem bar hinge; title wraps under 40rem (review #5) | the one-row agenda bar | every agenda width | must re-test — F1-bar at 768–1920 green |
| 19 | hour-grid offset re-measured by a `ResizeObserver` (review #1) | block + drag-band positioning | every Jour/Semaine view | must re-test — RV-blocks green |
| 20 | `UnitOfWork` untouched; overlap race deferred (review #9) | — | — | follow-up `follow-up/doctor-leave-overlap-constraint.md` |

## Working tree note (start of session)
Fresh worktree from `origin/main`; the main checkout (`feature/security-remediation`, peer's dirty files) is untouched.

## Files Changed
- api: `Domain/Entities/DoctorLeave.cs`, `Domain/Repositories/IDoctorLeaveRepository.cs`, `Infrastructure/Persistence/Configurations/DoctorLeaveConfiguration.cs`, `Infrastructure/Repositories/DoctorLeaveRepository.cs`, `Infrastructure/Persistence/ApplicationDbContext.cs`, `Infrastructure/Extensions.cs`, `Infrastructure/Migrations/20261020110631_AddDoctorLeaves*.cs` + snapshot, `Application/DTOs/DoctorLeaveDto.cs`, `Application/Features/Doctors/DoctorLeaveRules.cs`, `…/Commands/{Create,Update,Delete}DoctorLeaveCommand.cs`, `…/Queries/{GetDoctorLeaves,GetClinicDoctorLeaves}Query.cs`, `Application/Features/Appointments/AppointmentScheduling.cs` + 3 booking handlers, `Application/Features/Audit/AuditLabels.cs`, `API/Controllers/DoctorsController.cs`, 10 test files (ctor compile fix)
- web: `lib/api/doctors.ts`, `lib/doctor-leaves.ts`, `lib/hooks/use-doctor-leaves.ts`, `components/doctor-leaves/doctor-leave-dialog.tsx`, `components/doctor-leaves/doctor-leaves-card.tsx`, `components/appointment-calendar.tsx`, `components/agenda-phone-header.tsx`, `components/mon-profil-content.tsx`, `components/clinic-settings.tsx`

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Save response also carries `firstAppointmentDay` | AC-7's « link to the agenda on the first one » needs the day; additive field |
| Test ctor compile fix in 10 files | build-required; an empty `IDoctorLeaveRepository` mock returns no congé, so every assertion is unchanged |
| « Congé » is labelled once the agenda BAR is 76rem wide (container query), not from `xl:` as AC-1b said; icon + tooltip below. Google's label moved to the same mechanism (84rem) | measured: the `xl:` labels pushed the week title to 0 px and the bar past the card at 1280–1440 (QA F1). Labelled at 1536 (the owner's laptop) |
| The out-of-hours confirm reads « … en dehors des disponibilités ? » and keeps its « Continuer » button (AC-4 said « Continuer quand même ») | the same code now carries a congé, and « heures d'ouverture » was false on an open day (QA F3); the existing confirm is reused, not re-worded |
| « Tous les congés » in the agenda « ⋯ » menu and the phone panel (admin + secretary) — the spec put the full list in Paramètres, which a secretary cannot open | owner's call, 2026-10-20 (review #13) |
| Review fixes: congés in their own feature area (`doctorleaves` key); « N rendez-vous » counts patient visits only; in desktop Mois a congé is a label (the day opens Jour, whose ribbon opens it); 404 for « introuvable » (other refusals stay 400) | `reviews/feature-review.md` § Fixes applied |
| A closed configured day (e.g. Sunday) is now hatched whole | the existing week-header comment already states « the closed-hour hatch runs the whole column »; `isHourOpen(null)` made it false. One predicate now answers both |

## Significant Deviations
1. **DEV-1 — refusals are 400, not 409.** The spec listed `409 leave_overlaps`. Every congé refusal (bad range, too long, overlap, not own) answers `400 { error }` with the French sentence, like `SetDoctorWorkingHours`; 409 stays the concurrency conflict only. No client branches on the overlap, so a code would be « a contract nobody is honouring » (`Result.Code`'s own rule). Approved: decided (reversible, no data at stake).

## Before the browser pass
- The migration is new: do **not** run this branch's API against the shared `clinic_management` DB while peers are live — use the isolated test stack (`:5099/:3099`, `isolated-test-stack` memory) on a DB copy, or ask before applying the migration to the shared one.
- `verify-schema` before and after the migration on that DB.

## Deferred to /test-small-feature
- Domain: `DoctorLeave.RefusalFor` (inverted range, 366/367 days, 200/201-char motif), `Overlaps`.
- Handlers: create/update/delete — own vs colleague (doctor), admin and secretary any, retired practitioner refused on create, overlap refused naming the existing congé, cross-clinic → not found, `appointmentsDuring` counts only slot-holding visits of that practitioner.
- Booking guard: create/update/recurring on a congé day → `outside_working_hours` with « … est en congé … »; `AllowOutsideWorkingHours` books it; a visit with no practitioner or another practitioner is unaffected; a 00:30 visit on the first day is on the congé (clinic-local).
- Query: window > 400 days refused; names resolved from the roster.

## Test Plan
| AC | Action | Target file | Notes |
|----|--------|-------------|-------|
| AC-1 | New test class | `UnitTests/Features/Doctors/DoctorLeaveCommandTests.cs` | admin + secretary any practitioner, doctor own only (create + delete), other clinic → « Praticien introuvable. », retired practitioner refused |
| AC-2 | New test class | `UnitTests/Domain/DoctorLeaveTests.cs` | end before start, 366 accepted / 367 refused, 200/201-char motif, blank motif → none, `Change` refuses an invalid period |
| AC-3 | Add scenarios | `DoctorLeaveCommandTests.cs` | overlap refused naming the existing congé on create **and** update; update never collides with itself (`excludeId`); version round-tripped |
| AC-4 / AC-6 | New test class | `UnitTests/Features/Appointments/DoctorLeaveBookingGuardTests.cs` | the shared guard: congé day → `outside_working_hours` + « … est en congé du 12/08 au 20/08. »; clinic-local day (23:30 UTC); other practitioner / no practitioner / other clinic unaffected; create refused then booked with `AllowOutsideWorkingHours`; moving into the congé refused; a notes-only edit of a visit already inside it is not |
| AC-7 | Add scenario | `DoctorLeaveCommandTests.cs` | counts only that practitioner's slot-holding visits, first day clinic-local, nothing moved |
| Reads | New test class | `UnitTests/Features/Doctors/DoctorLeaveQueryTests.cs` | names from the roster (retired included), 400-day window, other clinic's practitioner not found |

Coverage notes (no unit surface, covered elsewhere):
- AC-1b/1c/1d, AC-8–AC-11, AC-13 (agenda button, prefill, ribbon, hatch, phone markers, delete confirm, 320 px): browser QA `qa/run-4.md` — GREEN.
- AC-4 drag-move / waiting-list doors: the drag calls the same update endpoint, the waiting list opens the same create dialog — both reach the guard tested above; D1 walked the create door in the browser.
- AC-5 recurring series: `CreateRecurringSeriesCommand` calls the same `CheckWorkingHoursAsync` per occurrence (tested directly); no existing harness for that handler and its screen is withdrawn.
- AC-12 realtime: the commands live under `Features.Doctors`, whose `doctors` key `RealtimeResourceResolverTests` already pins in both directions (green in the full run).

## Tests Run
| Suite | Filter | Result |
|-------|--------|--------|
| Unit (new classes) | `FullyQualifiedName~DoctorLeave` | 35 passed, 0 failed |
| Unit (whole suite, the gate) | none | 5 989 passed, 0 failed, 6 skipped |
