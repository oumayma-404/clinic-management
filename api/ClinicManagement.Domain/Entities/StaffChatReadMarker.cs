namespace ClinicManagement.Domain.Entities;

/// <summary>How far one person has read one staff chat thread — the <see cref="NotificationRead"/> shape plus a <c>ClinicId</c>.</summary>
/// <remarks>The <c>ClinicId</c> gives the clinic purge a path to the row and lets the tenant filter apply.</remarks>
public class StaffChatReadMarker
{
    public Guid ClinicId { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public string ConversationKey { get; private set; } = string.Empty;
    public DateTime LastReadAt { get; private set; }

    private StaffChatReadMarker() { } // For EF Core

    public StaffChatReadMarker(Guid clinicId, string userId, string conversationKey, DateTime readUpToUtc)
    {
        if (clinicId == Guid.Empty)
        {
            throw new ArgumentException("La clinique est obligatoire.", nameof(clinicId));
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("L'utilisateur est obligatoire.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(conversationKey))
        {
            throw new ArgumentException("La conversation est obligatoire.", nameof(conversationKey));
        }

        ClinicId = clinicId;
        UserId = userId;
        ConversationKey = conversationKey;
        LastReadAt = readUpToUtc;
    }

    /// <summary>Moves the marker forward only: a late read from a slower tab never un-reads a newer message.</summary>
    public bool Advance(DateTime readUpToUtc)
    {
        if (readUpToUtc <= LastReadAt)
        {
            return false;
        }

        LastReadAt = readUpToUtc;
        return true;
    }
}
