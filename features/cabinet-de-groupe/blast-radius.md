# Blast radius — cabinet de groupe

Written before the first edit (`.claude/rules/regression-safety.md` § 1). Verdicts: unaffected · must change ·
must re-test.

| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `CreateAppointmentCommand` / `UpdateAppointmentCommand` / `CreateRecurringSeriesCommand` dentist | booking writes (shape 3: one rule, several call sites) | 7 surfaces open the create dialog; edit dialog always sends `doctorId`; drag, « Reporter », quick actions, post-visit popup, « À clôturer », plan release omit it | must change (3 commands) · must re-test (every omitted-field write on an unassigned RDV) |
| 2 | `UpdateAppointmentCommand.DoctorIdSpecified` | tri-state field (shape 2) | edit dialog sends it always, everything else omits it | unaffected — the rule keys on it, absence keeps meaning « unchanged » |
| 3 | `StaffNotification` audience (`AddressedTo`) | shared predicate (shape 3) | list, unread count, pending reviews (`UnreadQuery`), « Tout effacer », `StaffNotificationAudienceSqlTests` | must change in one place · must re-test the bell and the popup |
| 4 | post-visit prompt target | mirrored rule (shape 4) | `NotificationGenerator.EnsurePostVisitReviewAsync`, `PushNotificationGeneratorDecorator` (own copy of the target) | must change both |
| 5 | `GetAppointmentsQuery` dentist filter | read (shape 5) | agenda (`useAppointments`), `ExportAppointmentsQuery`; repository read also used by `DoctorLeaveRules` | must change (agenda, via a flag) · unaffected (export, congé count — flag default false) |
| 6 | visits-to-close dentist filter | paged SQL read (shape 5) | « À clôturer », patient page « Travail non facturé » (by patient, no dentist) | must change · must re-test the patient page band |
| 7 | `create-appointment-dialog.tsx` dentist seed | shared dialog | agenda, waiting list, patient page, devis strip, plan workspace, plans table, treatments in progress, « Suites à planifier »; carried drafts restore `doctorId` | must re-test each opener; draft must win over the new default |
| 8 | `appointment-calendar.tsx` block content | large shared component (shape 6) | day, week, month, phone, drag ghost, legend | must re-test every view at 320/390/820/1180/1440 |
| 9 | `Doctor.LinkToUser` | domain method | `CreateClinicCommand`, `JoinClinicCommand`, `LocalClinicProvisioning`, `ChangeUserRoleCommand`, `CreateClinicUserCommand` | must re-test — all link a fresh record, so the new refusal never fires there |
| 10 | `CreateClinicUserCommand` request | wire DTO | `user-management.tsx`, `e2e/specs/staff-chat.spec.ts:33` (`doctorInfo`) | unaffected — new fields are optional |
| 11 | `DoctorDto` (`createdAt`, `userId`) | wire DTO | `useDoctors`, settings roster, document editor, leave dialogs | unaffected — additive |
| 12 | `use-doctors.ts` current-dentist match | shared hook | create/edit dialog, document editor, congé card + dialog | must re-test the document prescriber default |
| 13 | migration `AddStaffNotificationAlsoTarget` | schema (shape 8) | archive / PC de secours copy derive columns from the model | must re-test: `verify-schema` diff; the relay copy and the archive carry the new column |
| 14 | e2e helpers `createAppointment` / `booking()` | test plumbing | ~12 specs book through them | must change |
