namespace ClinicManagement.Domain.Entities;

/// <summary>How far one administrator has read the « Nouveautés » page — the <see cref="StaffChatReadMarker"/> shape.</summary>
/// <remarks>Its own row rather than a <see cref="User"/> column: a read must not age the account's version or write the journal.</remarks>
public class PlatformNewsReadMarker
{
    public const int UserIdMaxLength = 200;

    public Guid ClinicId { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public DateTime ReadUpToUtc { get; private set; }

    private PlatformNewsReadMarker() { } // For EF Core

    public PlatformNewsReadMarker(Guid clinicId, string userId, DateTime readUpToUtc)
    {
        if (clinicId == Guid.Empty)
        {
            throw new ArgumentException("La clinique est obligatoire.", nameof(clinicId));
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("L'utilisateur est obligatoire.", nameof(userId));
        }

        ClinicId = clinicId;
        UserId = userId;
        ReadUpToUtc = readUpToUtc;
    }

    /// <summary>Moves forward only: a slower tab's late read never un-reads a newer news.</summary>
    public bool Advance(DateTime readUpToUtc)
    {
        if (readUpToUtc <= ReadUpToUtc)
        {
            return false;
        }

        ReadUpToUtc = readUpToUtc;
        return true;
    }
}
