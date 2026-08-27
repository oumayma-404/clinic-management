namespace ClinicManagement.Domain.Entities;

/// <summary>
/// Per-user read marker for a <see cref="StaffNotification"/>. Its presence means the given user has
/// read that notification. Composite key (<see cref="NotificationId"/>, <see cref="UserId"/>). This is a
/// join record, so — unlike aggregate roots — it does not extend <c>Entity&lt;TId&gt;</c>.
///
/// It has no <c>ClinicId</c>: it is always scoped by <see cref="UserId"/> (a user belongs to exactly one
/// clinic) and joined to its clinic-filtered <see cref="StaffNotification"/>, so it can never leak across
/// clinics (plan R-5).
/// </summary>
public class NotificationRead
{
    public Guid NotificationId { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public DateTime ReadAt { get; private set; }

    private NotificationRead() { } // For EF Core

    public NotificationRead(Guid notificationId, string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User id is required.", nameof(userId));

        NotificationId = notificationId;
        UserId = userId;
        ReadAt = DateTime.UtcNow;
    }
}
