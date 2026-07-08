using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

public class User : AggregateRoot<string> // Using Auth0 sub as ID (Cloud) or "local|{guid}" (Local mode)
{
    // Number of consecutive failed logins that triggers a temporary lockout (Local mode, AC-3.4).
    public const int MaxFailedLoginAttempts = 5;
    // How long an account stays locked after too many failed attempts.
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public Guid ClinicId { get; private set; }
    public string Role { get; private set; } // "doctor", "secretary", "admin"
    public string? Email { get; private set; }
    public string? FullName { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    // Local-mode credential fields. All are inert in Cloud mode (PasswordHash stays null).
    public string? PasswordHash { get; private set; }
    public bool IsActive { get; private set; } = true;
    public bool MustChangePassword { get; private set; }
    public DateTime? LastLoginAt { get; private set; }
    public int FailedLoginAttempts { get; private set; }
    public DateTime? LockoutEnd { get; private set; }

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

    /// <summary>
    /// Factory for a Local-mode account (offline email + password). Mints a stable
    /// <c>local|{guid}</c> id so it never collides with an Auth0 <c>sub</c>.
    /// </summary>
    public static User CreateLocalUser(
        Guid clinicId,
        string role,
        string email,
        string passwordHash,
        string fullName,
        bool mustChangePassword = false)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required for a local user.", nameof(email));
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Password hash is required for a local user.", nameof(passwordHash));
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Full name is required for a local user.", nameof(fullName));

        var user = new User($"local|{Guid.NewGuid()}", clinicId, role, email.Trim().ToLowerInvariant(), fullName.Trim())
        {
            PasswordHash = passwordHash,
            IsActive = true,
            MustChangePassword = mustChangePassword
        };
        return user;
    }

    public void Update(string role, string? email = null, string? fullName = null)
    {
        Role = role;
        Email = email;
        FullName = fullName;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>True for accounts that authenticate with a local email+password (Local mode).</summary>
    public bool IsLocalAccount() => PasswordHash != null;

    public bool IsLockedOut() => LockoutEnd.HasValue && LockoutEnd.Value > DateTime.UtcNow;

    /// <summary>Sets a new password hash and (optionally) forces a change at next login.</summary>
    public void SetPassword(string passwordHash, bool mustChangePassword = false)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Password hash is required.", nameof(passwordHash));

        PasswordHash = passwordHash;
        MustChangePassword = mustChangePassword;
        FailedLoginAttempts = 0;
        LockoutEnd = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Records a successful login: clears lockout state and stamps the login time.</summary>
    public void RecordSuccessfulLogin()
    {
        FailedLoginAttempts = 0;
        LockoutEnd = null;
        LastLoginAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Records a failed login; locks the account once the attempt threshold is crossed.</summary>
    public void RecordFailedLogin()
    {
        FailedLoginAttempts++;
        if (FailedLoginAttempts >= MaxFailedLoginAttempts)
        {
            LockoutEnd = DateTime.UtcNow.Add(LockoutDuration);
            FailedLoginAttempts = 0;
        }
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Activate()
    {
        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
    }

    public void ClearPasswordChangeRequirement()
    {
        MustChangePassword = false;
        UpdatedAt = DateTime.UtcNow;
    }

    public bool IsDoctor() => Role.Equals("doctor", StringComparison.OrdinalIgnoreCase);
    public bool IsSecretary() => Role.Equals("secretary", StringComparison.OrdinalIgnoreCase);
    public bool IsAdmin() => Role.Equals("admin", StringComparison.OrdinalIgnoreCase);
}
