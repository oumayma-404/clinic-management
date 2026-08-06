using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// The OS-push outbox. Its scan mirrors <see cref="INotificationRepository"/>'s deliberately: the two queues
/// have the same shape, so a fairness or starvation fix made in one has an obvious home in the other.
/// </summary>
public interface IPushDeliveryRepository
{
    Task AddRangeAsync(IEnumerable<PushDelivery> deliveries, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rows whose moment has come: <c>Pending</c> and <c>SendNotBefore &lt;= nowUtc</c>, oldest first, capped at
    /// <paramref name="batchSize"/> overall and <paramref name="perClinicBound"/> per clinic.
    ///
    /// <para>The per-clinic bound is not decoration: this is an oldest-first scan with no clinic dimension of its
    /// own, so on a hosted install one practice's backlog would own every tick. Clinics are served
    /// oldest-due-first, so a clinic can neither buy priority by queueing more nor lose it by queueing less.</para>
    ///
    /// <para><paramref name="nowUtc"/> is the caller's instant rather than <c>DateTime.UtcNow</c> read inside, so
    /// the enqueue side and the scan can be tested against one clock.</para>
    /// </summary>
    Task<IReadOnlyList<PushDelivery>> GetDueForDispatchAsync(
        int batchSize, int perClinicBound, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Parked rows to reconsider (AC-50). Bounded by the same batch size so recovering a large backlog costs a
    /// tick at a time rather than one very long tick.
    /// </summary>
    Task<IReadOnlyList<PushDelivery>> GetBlockedForReviewAsync(
        int batchSize, CancellationToken cancellationToken = default);

    Task UpdateAsync(PushDelivery delivery, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops terminal rows past the retention window. Never a <c>Pending</c> or <c>Blocked</c> one — a parked row
    /// is waiting for an operator, and purging it would delete the evidence of what is misconfigured.
    /// </summary>
    Task<int> PurgeTerminalOlderThanAsync(DateTime olderThanUtc, CancellationToken cancellationToken = default);
}
