using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.ValueObjects;
using ClinicManagement.Domain.Events;

namespace ClinicManagement.Domain.Entities;

public class Patient : AggregateRoot<Guid>
{
    public Guid ClinicId { get; private set; }
    public string FirstName { get; private set; }
    public string LastName { get; private set; }
    public DateTime DateOfBirth { get; private set; }
    public string Gender { get; private set; }
    public Email Email { get; private set; }
    public PhoneNumber PhoneNumber { get; private set; }
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
        Email email,
        PhoneNumber phoneNumber,
        Address? address = null,
        InsuranceInfo? insuranceInfo = null)
    {
        Id = id;
        ClinicId = clinicId;
        FirstName = firstName ?? throw new ArgumentNullException(nameof(firstName));
        LastName = lastName ?? throw new ArgumentNullException(nameof(lastName));
        DateOfBirth = dateOfBirth;
        Gender = gender ?? throw new ArgumentNullException(nameof(gender));
        Email = email ?? throw new ArgumentNullException(nameof(email));
        PhoneNumber = phoneNumber ?? throw new ArgumentNullException(nameof(phoneNumber));
        Address = address;
        InsuranceInfo = insuranceInfo;
        CreatedAt = DateTime.UtcNow;
    }

    public void UpdatePersonalInfo(
        string firstName,
        string lastName,
        DateTime dateOfBirth,
        string gender,
        Email email,
        PhoneNumber phoneNumber,
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

    public void UpdateEmergencyContact(string? name, PhoneNumber? phone)
    {
        EmergencyContactName = name;
        EmergencyContactPhone = phone;
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
            AddDomainEvent(new PatientFlagAddedEvent(Id, flag.Id));
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

