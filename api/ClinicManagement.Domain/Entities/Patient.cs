using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.ValueObjects;
using ClinicManagement.Domain.Events;

namespace ClinicManagement.Domain.Entities;

public class Patient : AggregateRoot<Guid>
{
    public string FirstName { get; private set; }
    public string LastName { get; private set; }
    public DateTime DateOfBirth { get; private set; }
    public string Gender { get; private set; }
    public Email Email { get; private set; }
    public PhoneNumber PhoneNumber { get; private set; }
    public Address? Address { get; private set; }
    public InsuranceInfo? InsuranceInfo { get; private set; }
    public string? MedicalHistory { get; private set; }
    public string? Allergies { get; private set; }
    public string? EmergencyContactName { get; private set; }
    public PhoneNumber? EmergencyContactPhone { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    // Navigation properties
    private readonly List<PatientFlag> _flags = new();
    public IReadOnlyCollection<PatientFlag> Flags => _flags.AsReadOnly();

    private readonly List<PatientFile> _files = new();
    public IReadOnlyCollection<PatientFile> Files => _files.AsReadOnly();

    private readonly List<Appointment> _appointments = new();
    public IReadOnlyCollection<Appointment> Appointments => _appointments.AsReadOnly();

    private Patient() { } // For EF Core

    public Patient(
        Guid id,
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

    public string GetFullName() => $"{FirstName} {LastName}";
}

