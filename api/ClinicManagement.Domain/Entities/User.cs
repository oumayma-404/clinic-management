using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

public class User : AggregateRoot<string> // Using Auth0 sub as ID
{
    public Guid ClinicId { get; private set; }
    public string Role { get; private set; } // "doctor", "secretary", "admin"
    public string? Email { get; private set; }
    public string? FullName { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    // Navigation properties
    public Clinic Clinic { get; private set; } = null!;

    private User() { } // For EF Core

    public User(
        string id, // Auth0 sub
        Guid clinicId,
        string role,
        string? email = null,
        string? fullName = null)
    {
        Id = id;
        ClinicId = clinicId;
        Role = role;
        Email = email;
        FullName = fullName;
        CreatedAt = DateTime.UtcNow;
    }

    public void Update(string role, string? email = null, string? fullName = null)
    {
        Role = role;
        Email = email;
        FullName = fullName;
        UpdatedAt = DateTime.UtcNow;
    }

    public bool IsDoctor() => Role.Equals("doctor", StringComparison.OrdinalIgnoreCase);
    public bool IsSecretary() => Role.Equals("secretary", StringComparison.OrdinalIgnoreCase);
    public bool IsAdmin() => Role.Equals("admin", StringComparison.OrdinalIgnoreCase);
}





