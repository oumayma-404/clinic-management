# Implementation Plan: Cabinet de groupe — the three High traps

**Status:** APPROVED (owner, 2026-11-16)
**Challenged:** No
**Created:** 2026-11-16
**Spec:** none — the source is the investigation page « Cabinet de groupe readiness »
(https://claude.ai/artifact/MnjNEK5eYvSEw9BaicRcbt), every point checked against prod's code (a3249a8).
**Branch / worktree:** `feature/cabinet-de-groupe` from `main` (a3249a87) · `.claude/worktrees/cabinet-de-groupe`
**Blast radius:** [blast-radius.md](./blast-radius.md)

## Overview

A cabinet with several dentists can sign up today, but three things would hurt in its first week:
an RDV booked with no dentist, an agenda where nobody can tell whose RDV is whose, and duplicate or missing
dentist records when the team is set up. This plan fixes those three, and nothing else.

Prod evidence (read-only, 2026-11-16): **90 of the 121 RDV booked by secretaries since 1 September have no
dentist** — in today's one-dentist cabinets. So « Patient arrivé » cannot be sent for them, the fiche a secretary
writes is credited to nobody, and each one blocks every chair. US-1 fixes that for the existing cabinets too,
not only for a future group cabinet. All 16 practitioner records on prod are linked to an account; no admin is
without one; no cabinet has two dentists — so nothing on prod needs repairing, only preventing.

The approach follows the patterns already in the code:
- **One server rule, asked by every command that books** (`AppointmentScheduling`'s shape), with a derived test
  that fails when a new booking command skips it.
- **The agenda filter keeps cabinet-wide rows** — the L2b rule « an RDV with no dentist belongs to everybody » is
  applied to the read, where it was missing.
- **A practitioner record is linked, never duplicated** — an existing one can be chosen at invite time and linked
  afterwards; `Doctor.LinkToUser` refuses to steal one.

## Decisions

| # | Decision | Why |
|---|---|---|
| D1 | A patient RDV must name a dentist once the cabinet has **2+ active** practitioners — refused by the server (`practitioner_required`) and pre-checked by the dialogs | the server is the authority; every booking surface is covered at once |
| D2 | With **exactly one** active practitioner, the server fills in that dentist when a patient RDV names none | covers every booking surface (waiting list, devis, « Suites à planifier »…) without touching each; fixes the 90-of-121 |
| D3 | A **créneau bloqué** (no patient) may stay cabinet-wide — the dialog offers « Tout le cabinet » | L2b: that is how a lunch break blocks every chair |
| D4 | On update the rule runs **only when the save sends the dentist field** | a drag, a « Reporter », a status change or a lateness never send it, and must keep working on old RDV with no dentist |
| D5 | **No backfill** of old RDV | no prod data write; an old RDV gets its dentist the next time it is edited (automatically in a one-dentist cabinet) |
| D6 | The visit prompt « Ajoutez le dossier » goes to **the RDV's dentist and the person who booked it** | owner's call 2026-11-16: #5's secretary keeps her prompts, #1 is unchanged, a group cabinet stays quiet |
| D7 | The dentist is shown on each agenda block as an **initials chip**, tinted per dentist, only when the cabinet has 2+ active dentists | the block's colour already means the act, its right strip the status; a third colour channel would collide |
| D8 | Each person's agenda dentist filter is **remembered on the device** (per account); a dentist's first visit opens on « Mes rendez-vous », everyone else on « Tous les praticiens » | browser storage, the « Premiers pas » pattern — no migration; the URL still wins |
| D9 | « À clôturer » uses **the same remembered choice** as the agenda | one preference, one owner — not two copies that drift |
| D10 | A filtered view (agenda, « À clôturer ») **always includes RDV with no dentist** | otherwise filtering hides blocked slots and every unassigned visit |
| D11 | An admin can be invited **as a dentist too**; inviting a dentist can **link an existing practitioner**; an account with no practitioner can be **linked later** from « Utilisateurs » | the three ways a duplicate or a missing record happens today |
| D12 | Promoting an account to « Médecin » **links an unlinked practitioner with the same e-mail** instead of creating a second one | the role change is a Select with no form behind it; an exact e-mail match is a fact, not a guess |

Not in this plan: privacy between associates, a « par praticien » money table, rétrocession, a dentist on the
devis PDF and in reminders, per-dentist letterhead, merging two existing records (0 on prod), refusing to retire a
practitioner who still has an account.

## Files to Modify/Create

### Files to Create
| File | Purpose |
|------|---------|
| `api/ClinicManagement.Application/Features/Appointments/AppointmentPractitioner.cs` | D1/D2: the one rule — resolve or refuse the dentist of a patient RDV |
| `api/ClinicManagement.Application/Features/Users/Commands/LinkUserPractitionerCommand.cs` | D11: link an account to an existing practitioner, or create one |
| `api/ClinicManagement.Infrastructure/Migrations/<ts>_AddStaffNotificationAlsoTarget.cs` (+ Designer, snapshot) | D6: one nullable column |
| `web/lib/agenda/doctor-view.ts` | D8/D9: read, write and default the remembered dentist choice |
| `web/components/agenda/doctor-chip.tsx` | D7: initials + tone per dentist |
| `api/ClinicManagement.UnitTests/Features/Appointments/AppointmentPractitionerTests.cs` | the rule's truth table |
| `api/ClinicManagement.UnitTests/Features/Appointments/AppointmentPractitionerCoverageTests.cs` | derived guard: every Application file that builds an `Appointment` asks the rule |
| `api/ClinicManagement.UnitTests/Features/Users/LinkUserPractitionerCommandTests.cs` | D11 |
| `features/cabinet-de-groupe/notes.md` | what shipped and why (written at the end) |

### Files to Modify
| File | Changes |
|------|---------|
| `Application/Features/Appointments/Commands/CreateAppointmentCommand.cs` | resolve the dentist first, so the hours and congé checks use it |
| `…/UpdateAppointmentCommand.cs` | same, only when `DoctorIdSpecified` |
| `…/CreateRecurringSeriesCommand.cs` | same |
| `Application/Features/Appointments/Queries/GetAppointmentsQuery.cs` + `API/Controllers/AppointmentsController.cs` | `includeUnassigned` flag (default false, so the export is unchanged) |
| visits-to-close read (`GetVisitsToCloseQuery`, `VisitClosureReader` / `AppointmentRepository` ~157) + controller | same flag, in SQL (the list is paged) |
| `Domain/Entities/StaffNotification.cs` + its configuration | `AlsoTargetUserId`; `MovePostVisitReview` keeps it |
| `Infrastructure/Repositories/StaffNotificationRepository.cs` | `AddressedTo`: target OR also-target |
| `Application/Common/Services/NotificationGenerator.cs` + `INotificationGenerator.cs` | `EnsurePostVisitReviewAsync(…, bookedByUserId)` |
| `Infrastructure/Services/PushNotificationGeneratorDecorator.cs` | its own copy of the prompt's audience must push to both |
| `Domain/Entities/Doctor.cs` | `LinkToUser` refuses a record linked to someone else |
| `Application/Features/Users/Commands/CreateClinicUserCommand.cs` | practitioner for an admin; `ExistingDoctorId` |
| `…/ChangeUserRoleCommand.cs` | D12 |
| `Application/DTOs/ClinicUserDto.cs` + `ListUsersQuery.cs` | `DoctorId`, `PractitionerName` |
| `Application/DTOs/CreateClinicRequest.cs` (`DoctorDto`) + `GetUserStatusQuery.cs` + `UpdateDoctorsCommand.cs` response | `createdAt` (stable tone order), `userId` kept in the roster response |
| `API/Controllers/UsersController.cs` | `PUT users/{id}/practitioner`, `[OnlineOnly(…)]` |
| `web/components/create-appointment-dialog.tsx` | `defaultDoctorId`; seed order; required when 2+; « Tout le cabinet » for a blocked slot |
| `web/components/edit-appointment-dialog.tsx` | no « Aucun praticien » on a patient RDV when 2+; the only dentist pre-picked |
| `web/app/appointments/page.tsx` | remembered filter; `defaultDoctorId` to the dialog |
| `web/app/waiting-list/page.tsx` | pass the entry's preferred dentist |
| `web/components/appointment-calendar.tsx` | chip on blocks + month chips; `blockLabel`; legend; phone Praticien select |
| `web/lib/hooks/use-appointments.ts` + `web/lib/api/appointments.ts` | `includeUnassigned` |
| `web/app/a-cloturer/page.tsx` | Praticien select in the header actions |
| `web/lib/hooks/use-doctors.ts` | a `userId` match wins over e-mail/name |
| `web/components/user-management.tsx` + `web/lib/api/users.ts` | invite: « aussi praticien », « praticien existant »; row action « Lier à un praticien »; practitioner shown per account |
| `web/components/clinic-settings.tsx` | « sans compte » per practitioner; refresh on `users` |
| `e2e/lib/api.ts`, `e2e/lib/fixtures.ts` | bookings default to the QA clinic's first active dentist |
| `features/e2e-hot-paths/scenarios.md` + `e2e/specs/…` | new BOOK rows |
| CLAUDE.md files (root map, `api/…Application`, `web/lib`, `web/components`) | keep the map true |

## Implementation Stories

### US-1: Every RDV with a patient names its dentist

**Goal:** a secretary can no longer book a patient without a dentist in a group cabinet; in a one-dentist
cabinet the dentist is filled in by itself, so « Patient arrivé » works and the fiche is credited; the visit
prompt reaches the dentist and whoever booked.
**Blocked by:** None
**Layers:** DB (one column), API, UI, e2e

**Steps:**
1. `AppointmentPractitioner.ResolveAsync(IDoctorRepository, clinicId, patientId, requestedDoctorId, ct)` →
   `Result<Guid?>`: no patient → the request unchanged; a dentist named → unchanged (tenant check stays where it
   is); none named → 0 active = null, 1 active = that one, 2+ = failure `practitioner_required`,
   « Choisissez le praticien de ce rendez-vous : le cabinet en compte plusieurs. ». Active = `Doctor.IsActive`.
2. Call it in `CreateAppointmentCommand` and `CreateRecurringSeriesCommand` **before** `FindCollisionAsync` /
   `CheckWorkingHoursAsync`, and use the resolved id everywhere after. In `UpdateAppointmentCommand` only when
   `DoctorIdSpecified` (D4), with the RDV's resulting patient.
3. `AppointmentPractitionerCoverageTests`: scan `Application/**/*.cs` for `new Appointment(`; every such file
   must reference `AppointmentPractitioner` (prove it fails on a throwaway file first). Demo builder: always names
   a dentist — confirm it passes, or route it through the rule.
4. `StaffNotification.AlsoTargetUserId` (nullable, same length as `TargetUserId`); migration — check for the
   scaffolded `xmin`, no default, no backfill; `verify-schema` before/after.
   `AddressedTo`: `(TargetUserId == null || TargetUserId == userId || AlsoTargetUserId == userId)`.
   `EnsurePostVisitReviewAsync(…, string? bookedByUserId)`: also-target = the booker when it is not the dentist's
   own account; `MovePostVisitReview` keeps the stored also-target. Callers: create + recurring pass the caller;
   update passes null (keep). `PushNotificationGeneratorDecorator` pushes to both.
5. `create-appointment-dialog.tsx`: prop `defaultDoctorId`; seed order = carried draft › `defaultDoctorId` (if
   active) › `currentUserDoctor` › the only active dentist. With a patient and 2+ dentists the field is required
   (same sentence as the server, inline). Without a patient: an option « Tout le cabinet » (value empty).
6. `edit-appointment-dialog.tsx`: hide « Aucun praticien » for a patient RDV when 2+; pre-pick the only dentist
   when the field is empty.
7. `appointments/page.tsx`: pass `defaultDoctorId={doctorFilterId}`. `waiting-list/page.tsx`: pass
   `preferredDoctorId`.
8. e2e: `createAppointment`/`booking()` default `doctorId` to the QA clinic's first active dentist (read once
   from `/clinics/user-status`); add BOOK rows « 2 dentistes : un RDV patient sans praticien est refusé »,
   « 1 dentiste : le praticien est rempli seul », « créneau bloqué : Tout le cabinet » — the refusal and the
   pre-fill in a browser (verification.md § 4b).

**Validation:**
- [ ] Unit: the truth table (0/1/2+ dentists × patient/blocked × create/update-with-field/update-without/recurring);
      a status-only and a time-only update on an unassigned patient RDV in a 2-dentist cabinet still succeed;
      the prompt reaches dentist + booker, and only the dentist when they are the same person
- [ ] `AppointmentPractitionerCoverageTests` red on a throwaway violation, green after
- [ ] Unfiltered `dotnet test -c Release`; `check:responsive` + `tsc` + `build`; `check-coverage.mjs`
- [ ] `verify-schema` diff = the one column

### US-2: You can see whose RDV is whose

**Goal:** in a cabinet with 2+ dentists, every RDV block names its dentist; each person's dentist filter is
remembered (a dentist opens on their own RDV); a filtered agenda still shows blocked slots and RDV with no
dentist; a phone can pick the dentist; « À clôturer » filters by dentist.
**Blocked by:** US-1 (`defaultDoctorId` on the dialog)
**Layers:** API, UI

**Steps:**
1. `GetAppointmentsQuery.IncludeUnassigned` + `?includeUnassigned=` on the controller: when a dentist is asked for,
   read the window without it and keep `DoctorId == id || DoctorId == null` (the window bounds the rows). Default
   false, so `ExportAppointmentsQuery` and `DoctorLeaveRules` are unchanged.
2. Visits to close: same flag, as a SQL predicate (the list is paged); `appointmentsApi.visitsToClose` sends it.
3. `DoctorDto.createdAt` (server projection + TS type) for a stable tone order.
4. `doctor-chip.tsx`: `initialsOf` (existing) + a tone from the dentist's rank by `createdAt` among **all**
   practitioners, retired included (so a retirement shifts no colour), over the `--person-N` palette; 11 px floor.
5. `appointment-calendar.tsx`: the chip on the desktop block's second line (and the name row when the lane is
   wide), the phone day view's second line and the desktop month chip — only when 2+ active dentists; the
   dentist's name in `blockLabel` at every size; a « Praticiens » line in the legend; the Praticien select in the
   phone panel (`panelExtra`).
6. `lib/agenda/doctor-view.ts`: key per account e-mail, every access in try/catch; default = own practitioner
   when 2+ dentists, else « Tous ». `appointments/page.tsx` seeds URL › stored › default and writes on change;
   a stored id no longer on the roster is ignored. `useAppointments` sends `includeUnassigned` whenever a
   dentist is set.
7. `a-cloturer/page.tsx`: Praticien select in `PageHeader` actions (2+ dentists only), same preference, sent to
   the read; the count badge follows it.

**Validation:**
- [ ] Unit: `includeUnassigned` returns the dentist's rows + the unassigned ones and nothing of a colleague's;
      export unchanged
- [ ] `check:responsive` + `tsc` + `build`
- [ ] `/test-in-browser` on a 2-dentist test clinic: chip at 320/390/820/1180/1440 (and 730 px tall), week/day/
      month, phone select, remembered choice after leaving and coming back, a blocked slot visible on a filtered
      view; regression rows from the blast radius (one-dentist cabinet shows no chip and no select)

### US-3: Each dentist has exactly one practitioner record

**Goal:** inviting a dentist never makes a duplicate or a dentist with no cachet; an admin who is also a dentist
gets a practitioner record; an existing practitioner can be linked to an account; the admin can see who is
linked.
**Blocked by:** None (can ship before US-2)
**Layers:** API, UI

**Steps:**
1. `Doctor.LinkToUser`: same user → no-op; another user → French `InvalidOperationException`.
2. `CreateClinicUserCommand`: `DoctorInfo` accepted for an **admin** (optional, same validation); new
   `ExistingDoctorId` for an admin or a doctor (same clinic, active, unlinked → link; else `practitioner_unavailable`).
   Fix the docstring at 43-47.
3. `ChangeUserRoleCommand`: on promotion to « Médecin » with no linked record, link an unlinked active practitioner
   with the same e-mail, else create (today's behaviour). Add the missing assertion that a record is created.
4. `LinkUserPractitionerCommand` + `PUT users/{id}/practitioner` (`[OnlineOnly]`, admin only): the account is an
   admin or a doctor with no linked record; link an existing one or create one.
5. `ClinicUserDto.DoctorId` / `PractitionerName` filled in `ListUsersQuery` with one roster read; `userId` kept in
   the roster response and in `clinic-settings.tsx`.
6. `user-management.tsx`: role admin → « C'est aussi un praticien » revealing the practitioner block; when
   unlinked practitioners exist → « Praticien existant » select (pre-picked when its e-mail matches the typed one);
   the practitioner name per account (table and card); row action « Lier à un praticien » (shared `rowMenu`).
7. `clinic-settings.tsx`: « sans compte » beside an unlinked practitioner; refresh on `users` too.
8. `use-doctors.ts`: look for a `userId` match across the whole roster first; e-mail/name only when none matches.

**Validation:**
- [ ] Unit: admin + practitioner; existing practitioner linked; refused when linked / retired / another clinic;
      promotion links by e-mail and creates otherwise; `LinkToUser` invariant; `RelayOnlineOnlyCoverageTests` green
- [ ] `check:responsive` + `tsc` + `build`; `/test-in-browser` on the invite form and the row action (card + table)

## Testing Strategy

### Unit Tests
- `AppointmentPractitionerTests`, `AppointmentPractitionerCoverageTests`, the prompt's audience (two recipients,
  one when the same person), `includeUnassigned`, the US-3 commands — plus the existing suites
  (`AppointmentPartialUpdateTests`, `AppointmentOverlapOverrideTests`, `DoctorLeaveBookingGuardTests`,
  `CreateClinicUserCommandHandlerTests`, `ChangeUserRoleCommandHandlerTests`, `StaffNotificationAudienceSqlTests`)
  run unfiltered.

### Integration Tests
- `verify-schema` before/after the migration; the e2e wire tests for booking with and without a dentist.

### E2E Tests
- New BOOK rows in `scenarios.md`, driven in a browser (the refusal and the pre-fill live in `.tsx`).
- `/test-in-browser` per story with a UI, then `/fix-qa-findings`.

## Risk Register

| ID | Risk | Likelihood | Impact | Story | Mitigation |
|----|------|------------|--------|-------|------------|
| R-1 | The e2e suite books without a dentist in a QA clinic that already has 2+ active dentists (staff-chat spec), so every such booking is refused | High | Med | US-1 | helpers default the dentist in the same commit; run the suite before pushing |
| R-2 | The rule refuses a drag, « Reporter » or status change on an old RDV with no dentist | Low | High | US-1 | D4 + unit tests for each omitted-field write |
| R-3 | A booking command added later skips the rule | Med | Med | US-1 | `AppointmentPractitionerCoverageTests` |
| R-4 | Push and feed disagree on who gets the visit prompt | Med | Med | US-1 | change the decorator in the same commit; one test asserting both |
| R-5 | Including unassigned rows changes the export or the congé count | Low | Med | US-2 | flag default false; those two callers untouched |
| R-6 | `appointment-calendar.tsx` is very large; a chip breaks the smallest block | Med | Low | US-2 | chip only on lines that already exist; label carries the name at every size; eye pass at 320 |
| R-7 | Linking hits the unique index on `Doctors.UserId` (500) | Low | Med | US-3 | pre-check « account already has a record » before the save |
| R-8 | One-dentist cabinets see a behaviour change: new RDV now name the dentist | High | Low | US-1 | intended; the visit prompt still reaches the secretary who booked (D6) |

## Breaking Changes

- A patient RDV with no dentist is refused once a cabinet has 2+ active practitioners (no cabinet on prod today).
- In a one-dentist cabinet, new patient RDV carry the dentist — « Patient arrivé » starts working, the visit prompt
  goes to dentist + booker instead of everyone.
- `CreateClinicUserCommand` docstring « an admin is not a practitioner » is reversed.

## Migrations

- `AddStaffNotificationAlsoTarget`: one nullable `text`/`varchar` column on `StaffNotifications`, no default, no
  backfill, no index (read beside `TargetUserId` inside an already-filtered query). Remove a scaffolded `xmin` if
  any. `verify-schema` diff must be exactly that column.
