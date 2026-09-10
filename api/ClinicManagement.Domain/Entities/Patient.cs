using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Domain.Entities;

public class Patient : AggregateRoot<Guid>
{
    public Guid ClinicId { get; private set; }
    public string FirstName { get; private set; }
    public string LastName { get; private set; }
    /// <summary>
    /// Optional. A walk-in registered at the desk with nothing but a name has no date of birth, and the app used to
    /// substitute « thirty years ago » so this column could stay NOT NULL — which made « we never asked » look
    /// exactly like a recorded birthday, and charted the patient on adult teeth on the strength of it.
    /// <see cref="Email"/> below is the shape this now follows.
    /// </summary>
    public DateTime? DateOfBirth { get; private set; }
    public string Gender { get; private set; }

    /// <summary>
    /// Which set of teeth this patient is charted on. Asked once here instead of three times in the UI — see
    /// <see cref="DentitionType"/> for why it has only two values and what that costs.
    /// </summary>
    public DentitionType Dentition { get; private set; } = DentitionType.Adult;

    /// <summary>
    /// When a human last answered « quelle denture ? » for this patient — <c>null</c> while nobody ever has.
    ///
    /// <para>
    /// ⚠️ <b>It exists because <see cref="Dentition"/> cannot answer that question, and a screen was asking
    /// it every single visit as a result.</b> The column is NOT NULL with an entity default of
    /// <see cref="DentitionType.Adult"/>, so « the dentist looked at the chart and said Définitive » and
    /// « nobody has ever been asked » are the same stored row. The odontogramme's « Quelle denture afficher ? »
    /// fires only when nothing can seed the chart — no date of birth, nothing charted — and had no way to know
    /// it had already been answered, so it came back on every reload of the patient's page, for ever. Reported
    /// as « je choisis, puis au rechargement il redemande ».
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>A nullable marker rather than making <c>Dentition</c> nullable.</b> That was the other candidate and
    /// it is a wider change than it looks: the column is read on every chart, every fiche and every document, and
    /// « not recorded » would become a third state each of those readers has to answer for. This adds a fact
    /// without changing one.
    /// </para>
    ///
    /// <para>
    /// Set by <see cref="SetDentition"/> and by nothing else, so any explicit answer marks it — the odontogramme's
    /// prompt, the patient form's « Denture » control, an import. It is never cleared: un-answering is not an
    /// operation anybody needs, and a null would put the prompt back on a patient whose dentition is on file.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>Not backfilled.</b> Every existing patient reads null, so one whose dentition really was chosen
    /// before this column existed is asked once more — and answering settles it. Inventing a timestamp for rows
    /// nobody can prove were answered would be the opposite error: it would silence the prompt for the undated
    /// walk-ins it exists to catch.
    /// </para>
    /// </summary>
    public DateTime? DentitionAnsweredAtUtc { get; private set; }
    /// <summary>
    /// Optional. A walk-in with no e-mail is an ordinary patient, not a data-quality problem — the app used to
    /// require both and manufactured <c>noemail@example.com</c> / <c>0000000000</c> to satisfy itself, which
    /// made "has no contact details" indistinguishable from "we have a real address on file".
    /// <see cref="EmergencyContactPhone"/> below is the shape this now follows.
    /// </summary>
    public Email? Email { get; private set; }
    public PhoneNumber? PhoneNumber { get; private set; }
    public Address? Address { get; private set; }
    public CnamInfo? CnamInfo { get; private set; }

    /// <summary>
    /// « Motif de consultation » — why this patient came in the first place, in their own terms.
    ///
    /// <para>Patient-level and free text on purpose. It is the standing reason this person is on the books
    /// (« douleur 36 », « suivi ortho », « esthétique »), which is why it is read beside the name rather than
    /// filed under a visit: whoever opens the fiche wants it before anything else on the screen.</para>
    ///
    /// <para>Unbounded (<c>text</c>, like <see cref="Notes"/>) rather than length-capped: a capped column turns a
    /// long paste into a <c>SaveChanges</c> failure that surfaces as the generic error, naming no field. The
    /// display clamps to one line instead, which costs nothing and refuses no record.</para>
    /// </summary>
    public string? ConsultationReason { get; private set; }

    /// <summary>« Maladies » — chronic or passing, in one free-text list. The interface used to call this
    /// « Maladies chroniques / affections », which was three words for one column and disagreed with the two other
    /// names the same value carries elsewhere.</summary>
    public string? MedicalHistory { get; private set; }
    public string? Allergies { get; private set; }

    /// <summary>
    /// « Médicaments » — what the patient is taking, free text.
    ///
    /// <para>Its absence is what this field is for. There was nowhere to record an anticoagulant, so practices wrote
    /// it into <see cref="ImportantNotes"/>, where it is a sentence rather than a field: it reaches no document, no
    /// summary and no structured read. Beside <see cref="Allergies"/> and <see cref="MedicalHistory"/> it is the
    /// third of the three lists a practitioner checks before touching the patient, and the three are now written,
    /// stored and displayed as a set.</para>
    ///
    /// <para>Uncapped <c>text</c> for the same reason as <see cref="ConsultationReason"/>: a length-capped column
    /// turns a long paste into a <c>SaveChanges</c> failure that names no field, and the display clamps instead.</para>
    /// </summary>
    public string? Medications { get; private set; }

    /// <summary>
    /// « Tabac ». Null means nobody has asked — see <see cref="ValueObjects.TobaccoUse"/>, which is where the
    /// difference between that and an answered « Non-fumeur » is written down.
    /// </summary>
    public TobaccoUse? TobaccoUse { get; private set; }
    public string? EmergencyContactName { get; private set; }
    public PhoneNumber? EmergencyContactPhone { get; private set; }

    /// <summary>
    /// Who referred this patient — « adressé par ». Optional, and free text on purpose: the referrer is normally a
    /// practitioner *outside* this clinic, so there is nothing in the model to link to and an FK to
    /// <see cref="Doctor"/> could not record the ordinary case at all.
    /// </summary>
    public string? ReferredBy { get; private set; }

    /// <summary>
    /// When the Google Calendar import created this record from an event title alone. Non-null means <b>nobody has
    /// confirmed it</b>: the name was read off a calendar and everything else — birth date, gender, telephone — is
    /// absent rather than answered.
    ///
    /// <para>⚠️ It is cleared by <see cref="UpdatePersonalInfo"/> on <b>any</b> human edit, not once the fields are
    /// full. What is being tracked is confirmation, not completeness — <see cref="DateOfBirth"/>'s own note says a
    /// patient with no birth date is an ordinary patient rather than a data-quality problem, so clearing on
    /// completeness would nag for ever about someone whose birthday the practice genuinely does not have.</para>
    /// </summary>
    public DateTime? CalendarImportPendingReviewSince { get; private set; }

    /// <summary>
    /// The existing patient this imported record is probably a duplicate of — the one whose name is the <b>same
    /// name written differently</b> (« Chaima Benkhalifa » for « Chaïma Ben Khalifa »). Stamped by the import when
    /// it finds exactly one such patient, and answered by a human: accepting merges, refusing clears this.
    ///
    /// <para>⚠️ <b>No foreign key, deliberately.</b> The suggested patient can be deleted or archived on its own,
    /// and a stale id must degrade to "no suggestion" on read rather than to a load failure — a dangling
    /// suggestion is a question that has expired, not corrupt data.</para>
    ///
    /// <para>⚠️ Null while <see cref="CalendarImportPendingReviewSince"/> is set is the ordinary case: most
    /// imported patients resemble nobody. The two are independent — refusing a suggestion leaves the record still
    /// awaiting its details.</para>
    /// </summary>
    public Guid? CalendarImportSuggestedDuplicateId { get; private set; }

    /// <summary>
    /// Free-standing notes about the patient themselves — what the dentist wants to be reminded of on every visit,
    /// as opposed to a <see cref="DentalRecord"/>'s notes, which describe one séance.
    ///
    /// <para>
    /// Deliberately free text like <see cref="MedicalHistory"/> and <see cref="Allergies"/> rather than a child
    /// collection: these are read as a paragraph, never filtered, sorted or counted, and a table would buy nothing
    /// but a second write path. <see cref="ImportantNotes"/> is the same field with a different weight — it is
    /// rendered highlighted at the top of the patient's file, so it must be separately storable rather than a
    /// convention inside one blob.
    /// </para>
    /// </summary>
    public string? Notes { get; private set; }

    /// <inheritdoc cref="Notes"/>
    public string? ImportantNotes { get; private set; }

    // Patient recall / relance (clinical-workflow-depth). The next-due date is derived on read from the last
    // completed visit + the clinic recall interval, so these fields hold only the optional per-patient overrides:
    // a snooze (drop off the "à relancer" list until then), the reason label, and the last-contacted stamp.
    public DateTime? RecallSnoozedUntil { get; private set; }
    public string? RecallReason { get; private set; }
    public DateTime? LastRecallContactedAt { get; private set; }

    // Consent to be contacted by SMS/WhatsApp. Recording a phone number used to enrol the patient into
    // reminders with no way for anyone — the patient or the cabinet — to opt out; this is that way out.
    //
    // ⚠️ The two stamps are not decoration. « Le patient a refusé » with nobody's name and no date is not a
    // consent record a cabinet can defend to the INPDP, and it is the half that is always dropped as
    // unnecessary. Written together in SetReminderConsent so they cannot drift apart.
    public PatientReminderConsent ReminderConsent { get; private set; } = PatientReminderConsent.NotRecorded;
    public DateTime? ReminderConsentRecordedAtUtc { get; private set; }
    public string? ReminderConsentRecordedBy { get; private set; }

    /// <summary>
    /// May this patient be sent an automated reminder or recall at all?
    ///
    /// <para>⚠️ <b>This is the ONLY place the consent enum is turned into a yes/no</b>, and every enqueue path
    /// must ask it rather than compare the enum itself. A second `!= Refused` written at a call site is how a
    /// later state (a withdrawal, an expiry) gets honoured in one place and ignored in the other — the failure
    /// this repository produces most often. <c>ReminderConsentCoverageTests</c> is the derived guard.</para>
    ///
    /// <para>See <see cref="PatientReminderConsent.NotRecorded"/> for why an unasked patient is still
    /// reachable.</para>
    /// </summary>
    public bool AcceptsReminders => ReminderConsent != PatientReminderConsent.Refused;

    // Archiving (data-and-money-integrity, AC-7..AC-9). Deleting a patient is refused whenever any clinical or
    // financial record is attached — which would otherwise leave no way at all to remove a duplicate or a
    // test entry, since this app has no merge and no soft delete. Archiving is that escape hatch: the patient
    // disappears from lists, search, recall and every picker, keeps every record, and is fully reversible.
    // Deliberately NOT a global query filter — no status flag in this codebase uses one, and an archived
    // patient must stay reachable by direct URL.
    public bool IsArchived { get; private set; }
    public DateTime? ArchivedAt { get; private set; }
    public string? ArchiveReason { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    // Navigation properties
    public Clinic Clinic { get; private set; } = null!;

    private readonly List<PatientFile> _files = new();
    public IReadOnlyCollection<PatientFile> Files => _files.AsReadOnly();

    private readonly List<Appointment> _appointments = new();
    public IReadOnlyCollection<Appointment> Appointments => _appointments.AsReadOnly();

    private readonly List<PatientMedicalHistory> _medicalHistoryEntries = new();
    public IReadOnlyCollection<PatientMedicalHistory> MedicalHistoryEntries => _medicalHistoryEntries.AsReadOnly();

    private readonly List<PatientFamilyHistory> _familyHistoryEntries = new();
    public IReadOnlyCollection<PatientFamilyHistory> FamilyHistoryEntries => _familyHistoryEntries.AsReadOnly();

    private Patient() { } // For EF Core

    public Patient(
        Guid id,
        Guid clinicId,
        string firstName,
        string lastName,
        DateTime? dateOfBirth,
        string gender,
        Email? email = null,
        PhoneNumber? phoneNumber = null,
        Address? address = null)
    {
        Id = id;
        ClinicId = clinicId;
        FirstName = firstName ?? throw new ArgumentNullException(nameof(firstName));
        LastName = lastName ?? throw new ArgumentNullException(nameof(lastName));
        DateOfBirth = dateOfBirth;
        Gender = gender ?? throw new ArgumentNullException(nameof(gender));
        Email = email;
        PhoneNumber = phoneNumber;
        Address = address;
        CreatedAt = DateTime.UtcNow;
    }

    public void UpdatePersonalInfo(
        string firstName,
        string lastName,
        DateTime? dateOfBirth,
        string gender,
        Email? email,
        PhoneNumber? phoneNumber,
        Address? address = null)
    {
        FirstName = firstName;
        LastName = lastName;
        DateOfBirth = dateOfBirth;
        Gender = gender;
        Email = email;
        PhoneNumber = phoneNumber;
        Address = address;
        UpdatedAt = DateTime.UtcNow;
        // A human has been through this record, which is the whole of what the stamp was waiting for. Cleared here
        // rather than at the call sites so none of them can forget — see the property's own note.
        CalendarImportPendingReviewSince = null;
        // And with it the duplicate question: somebody who edited this fiche and kept it has answered it.
        CalendarImportSuggestedDuplicateId = null;
        // The dismissal is only ever meaningful while the review stamp is set, so it goes with it rather than
        // being left behind as a value nothing reads and every future query has to remember to ignore.
        CalendarReviewDismissedAtUtc = null;
    }

    /// <summary>
    /// Stamps this record as conjured from a calendar event title, awaiting a human's confirmation — optionally
    /// naming the existing patient it is probably a duplicate of.
    /// </summary>
    public void MarkImportedFromCalendar(DateTime importedAtUtc, Guid? suggestedDuplicateId = null)
    {
        CalendarImportPendingReviewSince = importedAtUtc;
        CalendarImportSuggestedDuplicateId = suggestedDuplicateId;
    }

    /// <summary>
    /// « Non, ce n'est pas le même patient. » Clears the suggestion and <b>keeps the review stamp</b> — the record
    /// is still a name off a calendar with no details, so it stays on « Patients à compléter » with its own action.
    /// </summary>
    public void RejectCalendarImportSuggestion()
    {
        CalendarImportSuggestedDuplicateId = null;
    }

    /// <summary>
    /// The <see cref="CalendarImportRun"/> that <b>created</b> this record, or null. Set only on a patient the
    /// Google→App pass conjured from an event title — never on one it matched, which the clinic already had and
    /// which must survive the run being undone. See <c>Appointment.CalendarImportRunId</c> for why it is a plain
    /// indexed column with no foreign key.
    /// </summary>
    public Guid? CalendarImportRunId { get; private set; }

    /// <inheritdoc cref="CalendarImportRunId"/>
    public void StampImportRun(Guid runId) => CalendarImportRunId = runId;

    /// <summary>
    /// When somebody took this record off « Patients à compléter » without saying it was correct.
    ///
    /// <para>⚠️ <b>Deliberately NOT a clearing of <see cref="CalendarImportPendingReviewSince"/>.</b> That stamp
    /// is what identifies a record as conjured-and-unconfirmed, and it is the signal « Annuler cet import » uses
    /// to find what a run created. A « ne plus afficher » that cleared it would look identical to a human
    /// confirmation, and would silently destroy the evidence the undo needs — the same self-inflicted loss as a
    /// cancellation nulling <c>Appointment.GoogleCalendarEventId</c>.</para>
    ///
    /// <para>« Je ne veux plus voir cette ligne » and « j'ai vérifié que cette fiche est correcte » are different
    /// facts about a record, and a product that stores them in one column can never tell them apart again.</para>
    /// </summary>
    public DateTime? CalendarReviewDismissedAtUtc { get; private set; }

    /// <summary>True when the record is still unconfirmed but has been taken off the list.</summary>
    public bool IsCalendarReviewDismissed => CalendarReviewDismissedAtUtc.HasValue;

    /// <inheritdoc cref="CalendarReviewDismissedAtUtc"/>
    public void DismissCalendarReview(DateTime whenUtc)
    {
        if (CalendarReviewDismissedAtUtc.HasValue)
        {
            return;
        }

        CalendarReviewDismissedAtUtc = whenUtc;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Put the record back on « Patients à compléter ». Idempotent.</summary>
    public void RestoreCalendarReview()
    {
        if (!CalendarReviewDismissedAtUtc.HasValue)
        {
            return;
        }

        CalendarReviewDismissedAtUtc = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>« Motif de consultation ». Blank clears it — see <see cref="ConsultationReason"/>.</summary>
    public void SetConsultationReason(string? reason)
    {
        ConsultationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>« Tabac ». A null block clears the answer back to « jamais renseigné ».</summary>
    public void UpdateTobaccoUse(TobaccoUse? tobaccoUse)
    {
        TobaccoUse = tobaccoUse;
        UpdatedAt = DateTime.UtcNow;
    }

    // A null (or all-empty) CnamInfo clears the stored CNAM identity.
    public void UpdateCnamInfo(CnamInfo? cnamInfo)
    {
        CnamInfo = cnamInfo is { IsEmpty: true } ? null : cnamInfo;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// The three free-text health lists — « Maladies », « Allergies », « Médicaments » — written together.
    ///
    /// <para>⚠️ <b>All three are positional and all three are always written</b>, so a caller that has only one of
    /// them must re-send the other two as they stand. That is the shape this method already had for two fields; the
    /// third joins it rather than getting a setter of its own, because « on a mis à jour les allergies sans
    /// toucher aux médicaments » is precisely the tri-state confusion <see cref="UpdateContactDetails"/> exists to
    /// avoid, and there is no caller that wants it here — both writers (the create builder and the update handler)
    /// send the whole block.</para>
    /// </summary>
    public void UpdateMedicalHistory(string? medicalHistory, string? allergies, string? medications)
    {
        MedicalHistory = medicalHistory;
        Allergies = allergies;
        Medications = medications;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Set the patient's own contact details, each independently clearable.
    ///
    /// <para>
    /// Separate from <see cref="UpdatePersonalInfo"/> deliberately. There the two contact fields sit positionally
    /// among six, so a caller wanting to clear only the e-mail would have to re-send name, birth date, gender and
    /// address — and any of those being stale would silently overwrite. Tri-state needs a method that touches
    /// nothing else. Same shape as <see cref="UpdateEmergencyContact"/>.
    /// </para>
    /// </summary>
    public void UpdateContact(Email? email, PhoneNumber? phoneNumber)
    {
        Email = email;
        PhoneNumber = phoneNumber;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Record the patient's answer about automated reminders, with who asked and when.
    ///
    /// <para>⚠️ <b>Deliberately NOT folded into <see cref="UpdateContact"/>.</b> Changing a phone number is not
    /// a consent event, and a patient who refused keeps refusing when reception corrects a typo in their
    /// number. Tying the two together would silently reset a refusal on an unrelated edit — exactly the
    /// « auto-enrol on a phone number » behaviour this exists to end.</para>
    ///
    /// <para><paramref name="recordedBy"/> is the staff member who took the answer. It is stored as free text
    /// rather than a user id on purpose: the account may be deleted years before the record is questioned, and
    /// a consent record that dangles is worse than one naming somebody who has left.</para>
    /// </summary>
    public void SetReminderConsent(PatientReminderConsent consent, DateTime nowUtc, string? recordedBy)
    {
        ReminderConsent = consent;

        // « Not recorded » is the absence of an answer, so it carries no stamps — leaving the old ones would
        // assert that somebody recorded a non-answer at a date.
        if (consent == PatientReminderConsent.NotRecorded)
        {
            ReminderConsentRecordedAtUtc = null;
            ReminderConsentRecordedBy = null;
        }
        else
        {
            ReminderConsentRecordedAtUtc = nowUtc;
            ReminderConsentRecordedBy = string.IsNullOrWhiteSpace(recordedBy) ? null : recordedBy.Trim();
        }

        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateEmergencyContact(string? name, PhoneNumber? phone)
    {
        EmergencyContactName = name;
        EmergencyContactPhone = phone;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Set which teeth this patient is charted on.
    ///
    /// <para>
    /// Editable rather than write-once on purpose: it is the *only* escape hatch for the mixed-dentition gap
    /// documented on <see cref="DentitionType"/>. A child charted on baby teeth must be switchable to
    /// <see cref="DentitionType.Adult"/> the day a permanent tooth needs recording, and nothing about already-stored
    /// records changes when they are — each stored tooth is still classified by its own FDI range.
    /// </para>
    /// </summary>
    public void SetDentition(DentitionType dentition)
    {
        Dentition = dentition;
        DentitionAnsweredAtUtc = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Set (or clear, when blank) who referred the patient. Its own method, like the two above, so a
    /// caller can change it without re-sending name, birth date, gender and address.</summary>
    public void SetReferredBy(string? referredBy)
    {
        ReferredBy = string.IsNullOrWhiteSpace(referredBy) ? null : referredBy.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Set (or clear, when blank) the patient-level notes. Both are resolved by the caller, so each can be cleared
    /// independently — the pair travels together because they are edited together in one form section.
    /// </summary>
    public void UpdateNotes(string? notes, string? importantNotes)
    {
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        ImportantNotes = string.IsNullOrWhiteSpace(importantNotes) ? null : importantNotes.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Archive the patient: hidden from lists, search, recall and every picker, but nothing is destroyed and it
    /// is fully reversible. Idempotent — re-archiving an archived patient keeps the original stamp and reason,
    /// so a double-click never rewrites when the decision was actually taken.
    /// </summary>
    /// <remarks>
    /// The "no outstanding balance, no future appointment" guard lives in the handler, not here: a
    /// <see cref="Patient"/> holds no invoices or treatment plans, exactly as the billed-plan block sits in the
    /// amend handler rather than on <c>TreatmentPlan</c>.
    /// </remarks>
    public void Archive(string? reason = null)
    {
        if (IsArchived)
        {
            return;
        }

        IsArchived = true;
        ArchivedAt = DateTime.UtcNow;
        ArchiveReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Restore an archived patient everywhere. Idempotent, and clears the archive stamp and reason.</summary>
    public void Unarchive()
    {
        if (!IsArchived)
        {
            return;
        }

        IsArchived = false;
        ArchivedAt = null;
        ArchiveReason = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void AddFile(PatientFile file)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file));

        if (!_files.Contains(file))
        {
            _files.Add(file);
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public void RemoveFile(Guid fileId)
    {
        var file = _files.FirstOrDefault(f => f.Id == fileId);
        if (file != null)
        {
            _files.Remove(file);
            UpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Files an antecedent against this patient.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately does NOT stamp <c>UpdatedAt</c>, and neither do the three collection methods below
    /// it.</b> A child row changed; the patient’s own fields did not.
    /// <para>
    /// On this entity that distinction is not academic. <c>Version</c> maps onto PostgreSQL’s <c>xmin</c>,
    /// which advances on <i>any</i> UPDATE of the row — so writing <c>UpdatedAt</c> here moved the concurrency
    /// token a patient form was holding. The front end saves a patient by PUTting the patient and then writing
    /// each history entry in turn, and every entry bumped the token again, so the version returned by the PUT
    /// was stale before the save sequence had finished and the next save was refused with « cet enregistrement
    /// a été modifié par quelqu’un d’autre », naming a colleague who did not exist. A sequence that failed
    /// partway was worse: the form kept a version no later click could match, until a full page reload.
    /// </para>
    /// <para>
    /// Nothing reads <c>Patient.UpdatedAt</c> — it is persisted and never queried, sorted or projected — so the
    /// stamp was buying nothing and costing that.
    /// </para>
    /// </remarks>
    public void AddMedicalHistoryEntry(PatientMedicalHistory entry)
    {
        if (entry == null)
            throw new ArgumentNullException(nameof(entry));

        if (!_medicalHistoryEntries.Contains(entry))
        {
            _medicalHistoryEntries.Add(entry);
        }
    }

    public void RemoveMedicalHistoryEntry(Guid entryId)
    {
        var entry = _medicalHistoryEntries.FirstOrDefault(e => e.Id == entryId);
        if (entry != null)
        {
            _medicalHistoryEntries.Remove(entry);
        }
    }

    public void AddFamilyHistoryEntry(PatientFamilyHistory entry)
    {
        if (entry == null)
            throw new ArgumentNullException(nameof(entry));

        if (!_familyHistoryEntries.Contains(entry))
        {
            _familyHistoryEntries.Add(entry);
        }
    }

    public void RemoveFamilyHistoryEntry(Guid entryId)
    {
        var entry = _familyHistoryEntries.FirstOrDefault(e => e.Id == entryId);
        if (entry != null)
        {
            _familyHistoryEntries.Remove(entry);
        }
    }

    /// <summary>
    /// Push the recall out until <paramref name="until"/> — drops the patient off the "à relancer" list until
    /// then. Optionally updates the recall reason label.
    /// </summary>
    public void SnoozeRecall(DateTime until, string? reason = null)
    {
        RecallSnoozedUntil = until;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            RecallReason = reason.Trim();
        }
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Record that the patient was contacted about their recall and snooze it until <paramref name="snoozeUntil"/>
    /// so it temporarily leaves the active list. Optionally updates the recall reason label.
    /// </summary>
    public void MarkRecallContacted(DateTime snoozeUntil, string? reason = null)
    {
        LastRecallContactedAt = DateTime.UtcNow;
        RecallSnoozedUntil = snoozeUntil;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            RecallReason = reason.Trim();
        }
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Undo a recall snooze because the contact never actually happened (AC-P3.5): the dispatch that the
    /// « Relancer » action enqueued reached <c>Failed</c> on every configured channel. Returns the patient to
    /// the « à relancer » list and clears <see cref="LastRecallContactedAt"/> — leaving that stamp would have
    /// the list report a contact date for a message nobody received, which is the same lie one step later.
    /// The reason label is kept: it still describes why the patient is being recalled.
    /// Returns <c>false</c> when there was nothing to undo, so callers can tell a real recovery from a no-op.
    /// </summary>
    public bool ClearRecallSnooze()
    {
        if (RecallSnoozedUntil == null && LastRecallContactedAt == null)
        {
            return false;
        }

        RecallSnoozedUntil = null;
        LastRecallContactedAt = null;
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>Set (or clear, when blank) the patient's recall reason label.</summary>
    public void SetRecallReason(string? reason)
    {
        RecallReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public string GetFullName() => $"{FirstName} {LastName}";
}

