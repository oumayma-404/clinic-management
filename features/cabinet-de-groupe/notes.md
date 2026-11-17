# Cabinet de groupe — what shipped, and why

Plan: [plan.md](./plan.md) · blast radius: [blast-radius.md](./blast-radius.md). Source: the investigation page
« Cabinet de groupe readiness » (2026-11-16), every point checked against prod's code.

## Every RDV with a patient names its dentist (US-1)

- **One rule, `AppointmentPractitioner.ResolveAsync`**, asked by the three commands that book (create, update, recurring
  series): a booking that names a dentist, or has no patient, is left alone; a patient booking with none gets the
  cabinet's only active practitioner, and is refused with `practitioner_required` when there are several.
  `AppointmentPractitionerCoverageTests` fails when a file building an `Appointment` does not ask it.
- **Why the one-dentist half matters today**: on prod, 90 of the 121 RDV secretaries booked since September named no
  dentist. Such an RDV cannot reach anyone with « Patient arrivé », credits a secretary's fiche to nobody, and — under
  L2b — blocks every chair. The booking dialog pre-picked the signed-in dentist only, and a secretary is not one.
- ⚠️ **On update the rule runs only when the save SENDS the dentist field** (`DoctorIdSpecified`). A drag, « Reporter »,
  a status change or a lateness never send it and must keep working on an old RDV that has none. The edit dialog always
  sends it, so opening and saving such an RDV is where it gets its dentist.
- ⚠️ **No backfill.** Old RDV keep `DoctorId` null until edited; no prod data was rewritten.
- The rule resolves the dentist **before** the overlap and hours checks, so both judge the slot against the dentist the
  RDV will carry — a secretary's booking now meets the dentist's congés and hours, which a null dentist skipped.
- A « créneau occupé » (no patient) may stay cabinet-wide: the dialog offers « Tout le cabinet » (a sentinel, since a
  Radix item cannot carry ""), because it now pre-picks a dentist.
- **The edit dialog shows the dentist the save will name** (QA run 3 observation): a patient RDV with none, at a
  one-dentist cabinet, opens on that dentist — it read « Aucun praticien » while the server stored the dentist.
  ⚠️ **Derived on render, never written into the form state**: opening a visit must not make it dirty, so the merge
  snapshot and the carried draft keep the raw value. « Aucun praticien » stays for a « créneau occupé » and for a
  cabinet with no dentist.
- **The visit prompt goes to the dentist AND whoever booked** (owner's call, 2026-11-16): `StaffNotification.AlsoTargetUserId`
  (migration `AddStaffNotificationAlsoTarget`, one nullable column, no backfill), read by the single audience clause
  `AddressedTo`. The secretary at a cabinet where she fills the fiches keeps her prompts once the RDV names the dentist.
  ⚠️ The push decorator reads its audience **off the row the feed just wrote**, rather than re-deriving it — it had its
  own copy of the target rule, so the phone and the bell could have reached different people.
- Browser half: `web/lib/appointments/practitioner.ts` (the server's sentence word for word, the seed order: carried
  draft › the caller's `defaultDoctorId` › the signed-in dentist › the only one).

## You can see whose RDV is whose (US-2)

- With 2+ active dentists, each agenda block shows the dentist's **initials on a tint** (`components/agenda/doctor-chip.tsx`),
  and the block's accessible name says « avec Dr … » at every size. A chip, never the block colour: the surface means
  the act and the right strip the statut. Tints follow the roster's arrival order (`DoctorDto.createdAt`), retired
  practitioners included, so nobody is repainted when a colleague joins or leaves.
- **A filtered view keeps the RDV that name no dentist** — the agenda through `?includeUnassigned=true` (off by default,
  so the export and the congé count keep the strict filter), « À clôturer » in its SQL predicate. Without it, filtering
  hid every « créneau occupé » and every old unassigned visit from every dentist at once.
- The « Praticien » choice is **remembered per account on the device** (`lib/agenda/doctor-view.ts`, browser storage,
  every access in try/catch) and shared by the agenda and « À clôturer ». The URL still wins. A dentist's first visit
  opens on their own RDV; everyone else's on « Tous les praticiens ».
- Phones get the Praticien select in the agenda's panel — below `md:` the bar holding it is hidden, so a phone could
  remove the filter's chip and never pick a dentist.

## One practitioner record per dentist (US-3)

- `PractitionerLink` is the one rule for the three doors that tie an account to a practitioner: inviting a colleague
  (`CreateClinicUserCommand.ExistingDoctorId`, and `DoctorInfo` now accepted for an admin who is also a dentist),
  promoting one to « Médecin » (links the **one** unlinked record with the same e-mail, else creates), and the new
  « Lier à un praticien » (`PUT /api/users/{id}/practitioner`).
- ⚠️ `Doctor.LinkToUser` refuses a record already linked to another account — linking is never a way to take a
  colleague's cachet and history. An account that already has a record is refused, never re-pointed. Merging two
  records is not offered (none on prod).
- The users list shows « Praticien : … » or « Sans fiche praticien » per admin/doctor account.
- `useDoctors().currentUserDoctor` now searches the whole roster for a `userId` match before trying e-mail or name: in
  one `find`, an unlinked record sharing the person's e-mail could win over their own linked one.

## Not done here

⚠️ **Deviation from the plan:** the « sans compte » badge on the Paramètres practitioner list was not built. The users
screen states it per account instead (« Praticien : … » / « Sans fiche praticien »), and « Lier à un praticien » is the
fix — so the question is answered where it can be acted on.

Privacy between associates, a « par praticien » money table, rétrocession, a dentist on the devis PDF and in reminders,
per-dentist letterhead, refusing to retire a practitioner who still has an account.
