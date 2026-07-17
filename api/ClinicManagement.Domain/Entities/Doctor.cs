using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

public class Doctor : AggregateRoot<Guid>
{
    public Guid ClinicId { get; private set; }
    public string FirstName { get; private set; }
    public string LastName { get; private set; }
    public string Specialty { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public string? UserId { get; private set; } // Link to User when they sign up with Auth0
    public string? CodeProfessionnelSante { get; private set; } // CNAM provider code, prints on the bulletin

    // Navigation properties
    public Clinic Clinic { get; private set; } = null!;

    // Computed property for full name
    public string FullName => $"{FirstName} {LastName}".Trim();

    private Doctor() { } // For EF Core

    public Doctor(
        Guid id,
        Guid clinicId,
        string firstName,
        string lastName,
        string specialty,
        string? phone = null,
        string? email = null,
        string? codeProfessionnelSante = null)
    {
        Id = id;
        ClinicId = clinicId;
        FirstName = firstName ?? throw new ArgumentNullException(nameof(firstName));
        LastName = lastName ?? throw new ArgumentNullException(nameof(lastName));
        Specialty = specialty ?? throw new ArgumentNullException(nameof(specialty));
        Phone = phone;
        Email = email;
        CodeProfessionnelSante = codeProfessionnelSante;
        CreatedAt = DateTime.UtcNow;
    }

    public void Update(string firstName, string lastName, string specialty, string? phone = null, string? email = null, string? codeProfessionnelSante = null)
    {
        FirstName = firstName;
        LastName = lastName;
        Specialty = specialty;
        Phone = phone;
        Email = email;
        CodeProfessionnelSante = codeProfessionnelSante;
        UpdatedAt = DateTime.UtcNow;
    }

    public void LinkToUser(string userId)
    {
        UserId = userId;
        UpdatedAt = DateTime.UtcNow;
    }
}


