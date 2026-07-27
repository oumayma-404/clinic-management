using ClinicManagement.Domain.Common;
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

    /// <summary>Set (or clear, when blank) the patient's recall reason label.</summary>
    public void SetRecallReason(string? reason)
    {
        RecallReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public string GetFullName() => $"{FirstName} {LastName}";
}

