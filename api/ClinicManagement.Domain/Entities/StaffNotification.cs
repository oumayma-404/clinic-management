using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A clinic-scoped, in-app staff notification (bell/panel feed). One shared row per event; per-user
/// read state is tracked separately in <see cref="NotificationRead"/> (no write-time fan-out).
///
/// This is the in-app feed record — deliberately separate from the outbound email/SMS
/// <see cref="Notification"/> entity, which stays untouched and dormant.
/// </summary>
public class StaffNotification : AggregateRoot<Guid>
{
    public Guid ClinicId { get; private set; }
    public NotificationCategory Category { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;

    /// <summary>
    /// The time this notification takes effect in the feed: creation time for immediate categories,
    /// the reminder's due time for <see cref="NotificationCategory.Reminder"/>. It is the ordering,
    /// due-ness (<c>&lt;= now</c>), and late-joiner baseline anchor. Always UTC.
    /// </summary>
    public DateTime EffectiveFeedTime { get; private set; }

    /// <summary>
    /// The user who performed the action that generated this notification, if any. That user is
    /// excluded entirely from their own panel/badge. Null for actor-less events (reminders, low stock).
    /// </summary>
    public string? ActorUserId { get; private set; }

    public NotificationTargetKind TargetKind { get; private set; }
    public Guid? AppointmentId { get; private set; }
    public Guid? StockItemId { get; private set; }

    /// <summary>
    /// When set, only this user sees the row (a doctor-targeted post-visit review); when null, the row
    /// stays clinic-wide (all existing categories). Repository predicates honor this in addition to
    /// <see cref="ActorUserId"/> exclusion.
    /// </summary>
    public string? TargetUserId { get; private set; }

    /// <summary>
    /// Which subscription-expiry threshold this row announces — 7, 3, 1 or 0 days
    /// (<c>clinic-subscription</c> FR-5). The dedupe key for « one genuinely new unread row per threshold », and a
    /// real column rather than a French message prefix, because recovering behaviour by matching prose is the
    /// defect this repo deleted in <c>adoption-gaps-remediation</c>.
    ///
    /// <para>Written only by <see cref="ForSubscription"/>; null on every other category.</para>
    /// </summary>
    public int? SubscriptionThresholdDays { get; private set; }

    public DateTime CreatedAt { get; private set; }

    private StaffNotification() { } // For EF Core

    public StaffNotification(
        Guid id,
        Guid clinicId,
        NotificationCategory category,
        string title,
        string message,
        DateTime effectiveFeedTime,
        NotificationTargetKind targetKind,
        string? actorUserId = null,
        Guid? appointmentId = null,
        Guid? stockItemId = null,
        string? targetUserId = null)
    {
        Id = id;
        ClinicId = clinicId;
        Category = category;
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Message = message ?? throw new ArgumentNullException(nameof(message));
        EffectiveFeedTime = effectiveFeedTime;
        TargetKind = targetKind;
        ActorUserId = actorUserId;
        AppointmentId = appointmentId;
        StockItemId = stockItemId;
        TargetUserId = targetUserId;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// A subscription-expiry warning for one crossed threshold (<c>clinic-subscription</c> AC-3.4, AC-3.7).
    /// Clinic-wide with no actor and no target user: it is addressed to the whole practice, because the more
    /// likely the owner hears about it the better, and nobody « did » a date arriving.
    ///
    /// <para>A factory rather than a twelfth constructor parameter, because the threshold is meaningful for
    /// exactly one category — an optional argument on the ctor would let any of the other nine carry one.</para>
    /// </summary>
    public static StaffNotification ForSubscription(
        Guid id, Guid clinicId, string title, string message, DateTime effectiveFeedTimeUtc, int thresholdDays)
    {
        var notification = new StaffNotification(
            id, clinicId, NotificationCategory.SubscriptionExpiring, title, message,
            effectiveFeedTimeUtc, NotificationTargetKind.Subscription);
        notification.SubscriptionThresholdDays = thresholdDays;
        return notification;
    }

    /// <summary>
    /// Repoints a pending reminder to a new due time (used when its appointment is rescheduled),
    /// refreshing the French title/message to reflect the new time.
    /// </summary>
    public void MoveReminder(DateTime newDueTimeUtc, string title, string message)
    {
        EffectiveFeedTime = newDueTimeUtc;
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    /// <summary>
    /// Repoints a pending post-visit review to a new visible-at time (the appointment's new end) and
    /// recomputes its target user (the doctor may have changed on reschedule/update), refreshing text.
    /// </summary>
    public void MovePostVisitReview(DateTime newEffectiveFeedTimeUtc, string? targetUserId, string title, string message)
    {
        EffectiveFeedTime = newEffectiveFeedTimeUtc;
        TargetUserId = targetUserId;
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    /// <summary>
    /// Restates a live <see cref="NotificationCategory.StockExpiringSoon"/> alert to the item's current
    /// earliest expiring batch (AC-P4.6). Unlike <see cref="LowStock"/>, which fires once per not-low→low
    /// crossing, this alert is *ensured*: the daily scan re-evaluates the same item every run, so the row is
    /// kept in step with the batch it is about instead of a second row being written every day. The feed time
    /// is deliberately left alone — the alert's place in the feed is when the clinic was first told.
    /// </summary>
    /// <summary>
    /// Re-words a live alert in place, for the <b>ensure</b> categories whose underlying fact changed (a stock
    /// item's expiring batch, or the clinic's last successful backup).
    ///
    /// <para>Renamed from <c>RestateStockExpiry</c> when the backup-staleness pair arrived (L4d): it was never
    /// about stock — it is the one operation « ensure » needs beyond create — and a second, byte-identical
    /// mutator with a different name is how two categories start disagreeing about what restating means.</para>
    /// </summary>
    public void Restate(string title, string message)
    {
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }
}
