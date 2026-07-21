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

    // Official-documents production-readiness (FR-2.5 / FR-3.1): the practitioner's CNOMDT registration
    // number and their scanned cachet/signature image. The cachet content type is persisted explicitly so
    // the image is served back with the right MIME type (unlike the clinic-logo path, which hardcodes PNG).
    public string? OrdreNumberCnomdt { get; private set; }
    public string? CachetStorageKey { get; private set; }
    public string? CachetContentType { get; private set; }

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

    // FR-2.5: the CNOMDT order number is set on the practitioner's own profile and pre-filled onto
    // certificats/liaisons. Blank clears it.
    public void SetOrdreNumber(string? ordreNumber)
    {
        OrdreNumberCnomdt = string.IsNullOrWhiteSpace(ordreNumber) ? null : ordreNumber.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    // FR-3.1: point the doctor at a stored cachet blob and remember its content type (both required).
    public void SetCachet(string storageKey, string contentType)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
            throw new ArgumentException("Cachet storage key is required.", nameof(storageKey));
        if (string.IsNullOrWhiteSpace(contentType))
            throw new ArgumentException("Cachet content type is required.", nameof(contentType));

        CachetStorageKey = storageKey;
        CachetContentType = contentType;
        UpdatedAt = DateTime.UtcNow;
    }

    // FR-3.1: remove the cachet; documents then fall back to a plain signature line (FR-3.2).
    public void RemoveCachet()
    {
        CachetStorageKey = null;
        CachetContentType = null;
        UpdatedAt = DateTime.UtcNow;
    }
}


