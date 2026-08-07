using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Domain.Entities;

public class Patient : AggregateRoot<Guid>
{
    public Guid ClinicId { get; private set; }
    public string FirstName { get; private set; }
    public string LastName { get; private set; }
    public DateTime DateOfBirth { get; private set; }
    public string Gender { get; private set; }

    /// <summary>
    /// Which set of teeth this patient is charted on. Asked once here instead of three times in the UI — see
    /// <see cref="DentitionType"/> for why it has only two values and what that costs.
    /// </summary>
    public DentitionType Dentition { get; private set; } = DentitionType.Adult;
    /// <summary>
    /// Optional. A walk-in with no e-mail is an ordinary patient, not a data-quality problem — the app used to
    /// require both and manufactured <c>noemail@example.com</c> / <c>0000000000</c> to satisfy itself, which
    /// made "has no contact details" indistinguishable from "we have a real address on file".
    /// <see cref="EmergencyContactPhone"/> below is the shape this now follows.
    /// </summary>
    public Email? Email { get; private set; }
    public PhoneNumber? PhoneNumber { get; private set; }
    public Address? Address { get; private set; }
    public InsuranceInfo? InsuranceInfo { get; private set; }
    public CnamInfo? CnamInfo { get; private set; }
    public string? MedicalHistory { get; private set; }
    public string? Allergies { get; private set; }
    public string? EmergencyContactName { get; private set; }
    public PhoneNumber? EmergencyContactPhone { get; private set; }

    /// <summary>
    /// Who referred this patient — « adressé par ». Optional, and free text on purpose: the referrer is normally a
    /// practitioner *outside* this clinic, so there is nothing in the model to link to and an FK to
    /// <see cref="Doctor"/> could not record the ordinary case at all.
    /// </summary>
    public string? ReferredBy { get; private set; }

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
    private readonly List<PatientFlag> _flags = new();
    public IReadOnlyCollection<PatientFlag> Flags => _flags.AsReadOnly();

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
        DateTime dateOfBirth,
        string gender,
        Email? email = null,
        PhoneNumber? phoneNumber = null,
        Address? address = null,
        InsuranceInfo? insuranceInfo = null)
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
        InsuranceInfo = insuranceInfo;
        CreatedAt = DateTime.UtcNow;
    }

    public void UpdatePersonalInfo(
        string firstName,
        string lastName,
        DateTime dateOfBirth,
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
    }

    public void UpdateInsuranceInfo(InsuranceInfo? insuranceInfo)
    {
        InsuranceInfo = insuranceInfo;
        UpdatedAt = DateTime.UtcNow;
    }

    // A null (or all-empty) CnamInfo clears the stored CNAM identity — mirrors UpdateInsuranceInfo.
    public void UpdateCnamInfo(CnamInfo? cnamInfo)
    {
        CnamInfo = cnamInfo is { IsEmpty: true } ? null : cnamInfo;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateMedicalHistory(string? medicalHistory, string? allergies)
    {
        MedicalHistory = medicalHistory;
        Allergies = allergies;
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

    public void AddFlag(PatientFlag flag)
    {
        if (flag == null)
            throw new ArgumentNullException(nameof(flag));

        if (!_flags.Contains(flag))
        {
            _flags.Add(flag);
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public void RemoveFlag(Guid flagId)
    {
        var flag = _flags.FirstOrDefault(f => f.Id == flagId);
        if (flag != null)
        {
            _flags.Remove(flag);
            UpdatedAt = DateTime.UtcNow;
        }
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

    public void AddMedicalHistoryEntry(PatientMedicalHistory entry)
    {
        if (entry == null)
            throw new ArgumentNullException(nameof(entry));

        if (!_medicalHistoryEntries.Contains(entry))
        {
            _medicalHistoryEntries.Add(entry);
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public void RemoveMedicalHistoryEntry(Guid entryId)
    {
        var entry = _medicalHistoryEntries.FirstOrDefault(e => e.Id == entryId);
        if (entry != null)
        {
            _medicalHistoryEntries.Remove(entry);
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public void AddFamilyHistoryEntry(PatientFamilyHistory entry)
    {
        if (entry == null)
            throw new ArgumentNullException(nameof(entry));

        if (!_familyHistoryEntries.Contains(entry))
        {
            _familyHistoryEntries.Add(entry);
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public void RemoveFamilyHistoryEntry(Guid entryId)
    {
        var entry = _familyHistoryEntries.FirstOrDefault(e => e.Id == entryId);
        if (entry != null)
        {
            _familyHistoryEntries.Remove(entry);
            UpdatedAt = DateTime.UtcNow;
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

