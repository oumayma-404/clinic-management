namespace ClinicManagement.Domain.Entities;

/// <summary>
/// Per-user dismissal marker for a <see cref="StaffNotification"/>. Its presence means the given user has
/// cleared that notification from their own bell. Composite key (<see cref="NotificationId"/>,
/// <see cref="UserId"/>). A join record, so — like <see cref="NotificationRead"/> and unlike an aggregate
/// root — it does not extend <c>Entity&lt;TId&gt;</c>.
///
/// <para>⚠️ <b>Why a marker and not a delete.</b> A <see cref="StaffNotification"/> row is <i>shared</i>: one
/// written with a null <c>TargetUserId</c> is the same row in every colleague's feed. So « Supprimer » in the
/// bell cannot remove the row — a secretary clearing their own list would take the low-stock alert and the
/// post-visit prompt out of every dentist's bell at the same time, silently and with no way back. Dismissal is
/// a fact about one reader, exactly as « lu » already is, and it is stored the same way.</para>
///
/// <para>It has no <c>ClinicId</c>, for <see cref="NotificationRead"/>'s reason: it is always scoped by
/// <see cref="UserId"/> (a user belongs to exactly one clinic) and joined to its clinic-filtered
/// <see cref="StaffNotification"/>, so it can never leak across clinics.</para>
///
/// <para>⚠️ Dismissing is <b>not</b> reading, and the two markers are deliberately separate rows rather than one
/// nullable column. « Je l'ai vu » and « je ne veux plus le voir » are different statements: the unread badge is
/// built from the first, and folding them together would make « Tout effacer » indistinguishable from « Tout
/// marquer comme lu » for anything that later re-reads that history.</para>
/// </summary>
public class NotificationDismissal
{
    public Guid NotificationId { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public DateTime DismissedAt { get; private set; }

    private NotificationDismissal() { } // For EF Core

    public NotificationDismissal(Guid notificationId, string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User id is required.", nameof(userId));

        NotificationId = notificationId;
        UserId = userId;
        DismissedAt = DateTime.UtcNow;
    }
}
