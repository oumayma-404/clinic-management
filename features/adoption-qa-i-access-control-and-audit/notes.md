# adoption-qa-i-access-control-and-audit — shipped notes

What this feature actually does in the code, and the decisions that are easy to undo by accident.
Moved out of the root `CLAUDE.md` verbatim so it is no longer loaded into every session; the root
indexes it under **Architecture notes**. `spec.md` is what was asked for, `stories/` how it was built,
and this is what shipped.

## Who may do what, and who did it (`adoption-qa-i-access-control-and-audit`)

the product had three
authorization policies defined and **never applied** — `DoctorOnly`, `SecretaryOnly`, `DoctorOrSecretary`, zero
usages for the whole life of the product — while **33 endpoints carried a bare `[Authorize]`** (any authenticated
user, any role) and **20 controllers carried no policy at all**, including la caisse, les créances, the
dashboard, patient delete/archive, the odontogram and every clinical note. They stayed green because the guard
test only asserted that a policy *existed*. Nor was it a hidden-menu-with-a-live-API case: `web/lib/nav.ts`
shipped « Tableau de bord » and the whole « Finances » group to every role, and the three finance pages contained
no `role` reference. **Every one of the 32 controllers now carries a class-level named policy and no bare
`[Authorize]` remains**, over a vocabulary of four — `Authenticated` (the onboarding surface, which in Cloud is
reached *before* the role is in the JWT), `AnyClinicRole`, `AdminOrDoctor`, `AdminOnly`.
⚠️ **The load-bearing distinction is not "lock the money down"**: a secretary must be able to take a payment and
read *one patient's* balance — that is reception's job — but must not read clinic-wide aggregates. Per-patient
money: yes. Clinic-wide money: no. So `POST /api/invoices/{id}/payments` and
`GET /api/patients/{id}/billing-summary` stay **deliberately open**, while `billing/caisse`, `/caisse/ledger`,
`billing/receivables`, `invoices/revenue` and the whole dashboard are `AdminOrDoctor`.
⚠️ **The second distinction was added later, and it reverses one of I1's own decisions: the clinical record is
`AnyClinicRole` to read and record, `AdminOrDoctor` to delete from.** I1 put fiches de soins, the odontogramme,
the antécédents and the medical documents wholly behind `AdminOrDoctor` under the heading « clinical authorship
and clinical free text » — including the `GET`s, which it forked on explicitly and decided the strict way. The
result was that a secretary opening a patient hit « Vous n'avez pas les droits » on « Dossiers médicaux » and
« Documents » before touching anything, and a practising dentist's account is that the assistant(e) is who fills
much of the record in. **The old line was also never true of the code around it**: `PUT /api/patients/{id}` is
`AnyClinicRole` and writes `Allergies`, `MedicalHistory`, `Notes` and `ImportantNotes`, and `POST /api/patients`
inserts `PatientMedicalHistory` rows outright — so reception could always type a patient's medical history
through « Modifier » while being refused a *read* of the same text one tab over. The boundary did not protect
the data; it chose which door reception had to use. **Record yes, erase no** is the replacement, and the four
deletes (fiche · document + blob · antécédent médical · antécédent familial) are now the *only* thing gating
it — `ClinicalRecordAccessTests` states the charter as data and fails on an unclassified new action.
⚠️ **Widening the write surface was only safe because two other things already existed**: `AuditSaveChangesInterceptor`
attributes every mutation (so a secretary-recorded fiche is answerable at `GET /api/audit`), and
`PractitionerAttribution` puts the caller **last**, so clinical *credit* goes to the visit's dentist or is left
honestly `null` — never to whoever typed it.
⚠️ **And opening document authorship needed a real fix, not a policy edit.** `PractitionerRenderSnapshot` resolved
the cachet + n° d'ordre CNOMDT from the **caller's** `Doctor` record, so a document authored by anyone without one
— reception, or an admin who is not a dentist — rendered with *no* practitioner identity, silently, on the class of
document whose entire purpose is to carry it. `ResolveAsync` now takes an **`IssuingDoctorId`** and resolves
chosen practitioner → caller → none, tenant-checking the chosen id against the clinic roster (a foreign or stale
one *falls through*, exactly like `PractitionerAttribution.Resolve`); the editor sends the practitioner the user
already picked, and the `doctors[0]` fall-back it kept for the four free-form types is gone, since with reception
authoring it had become the *normal* path rather than a near-unreachable one. **No schema change**: the resolved
snapshot has always been persisted into the document's `ContentJson`, so the missing piece was a selector on the
request, not the persisted `DoctorId` the code's own note said it would need. `IssuingDoctorId` is therefore
deliberately **not** stripped from the render payload the way the four reserved values are — it is a selector
checked against the caller's own roster, while `doctorCachetKey` is a storage key the unauthenticated
`PdfGenerationJob` later dereferences.
⚠️ **`AnyClinicRole` includes `admin`, and that is why it exists** rather than the spec's `DoctorOrSecretary`:
`CreateClinicCommand` makes a clinic's creator an **admin** and links the single dentist's `Doctor` record to that
same account, so in the common Tunisian practice the owner-dentist's role is `admin` — and a literal
`{doctor, secretary}` policy on the agenda, the patient list or the till would have locked the owner out of their
own practice, which is strictly worse than the defect being fixed.
**The audit ledger** is the other half: a `SaveChangesInterceptor` writing one row per mutated **aggregate root**
(actor, clinic, entity, action, and a compact changed-field summary for updates and deletes), read through
`GET /api/audit` (`AdminOnly`, paged). Before it there was **no audit trail of any kind** — zero hits for
`CreatedBy`, `ModifiedBy`, `DeletedBy`, `AuditLog`, `IAuditable`, `SaveChangesInterceptor` — and the only
attributable actions in the entire product were voiding a payment and voiding an installment (an avoir recorded
no actor). It is an interceptor rather than `CreatedBy`/`ModifiedBy` on `Entity<TId>` because those would be a
write-path obligation on 38 entities, any writer that forgets one produces an unattributed row indistinguishable
from a legitimate one, and they answer nothing about a delete — the question most often asked. See
`Infrastructure/Persistence/AuditSaveChangesInterceptor.cs` for the forced two-phase shape and its one stated
imprecision. **Self-registration** no longer mints a live account either: `POST /api/auth/register` creates it
**pending** an admin's activation, since the only secret was a 6-character clinic code shown on a settings screen
and known to everyone who ever worked at the practice. And `GET /api/patients/{id}/ai-summary` was **deleted** —
see the AI-summary bullet below.

## The patient AI summary is gone, and the claim that used to be here was false on both halves (`adoption-qa-i-access-control-and-audit` I4)

`GET /api/patients/{id}/ai-summary` was documented as "on the patient detail page … connectivity-gated" while having **zero callers** in `web/` (the button was removed and the endpoint outlived it) and **no** `IInternetProbe` gate — so on an offline LAN install it hung ~205 s on a `HttpClient` with no timeout before failing. What it did do was POST a patient's full name, allergies, every medical- and family-history entry, and every dental record — teeth, money and all free-text notes — to `router.huggingface.co`, with no record cap, no consent flag, no audit of which patient was sent, and a class-level `[Authorize]` as its only gate, so any secretary could trigger it. It was **deleted** rather than fixed (endpoint, `GetPatientAiSummaryQuery`, `PatientAiSummaryDto`, `patientsApi.getAiSummary`): keeping it would have required a policy, a probe gate, a timeout, a record cap and an audit row to restore a feature no screen asked for. `IHuggingFaceAIService` is **gone too** — its last caller, the AI chat, was deleted with the whole subsystem (see the Infrastructure guide's « AI — removed »), so no code in this product now reaches an inference endpoint at all. *(The old placeholder `PatientSummaryService`/`IPatientSummaryService` + the disabled `AISummaryJob`, the never-registered `GoogleAIService`/`IGoogleAIService`, and the dormant email `NotificationService`/`INotificationService` were removed as dead code in `reliability-and-polish` — there is no AI backend at all any more, and the live outbound reminders go through the `IReminderChannelSender` senders below.)*
