using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>The vendor's news broadcasts. Belongs to no cabinet, so nothing here is tenant-filtered.</summary>
public interface IPlatformNewsRepository
{
    Task AddAsync(PlatformNewsBroadcast broadcast, CancellationToken cancellationToken = default);

    Task<PlatformNewsBroadcast?> GetByRequestIdAsync(Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>The broadcast with its recipients loaded — the dispatcher's read.</summary>
    Task<PlatformNewsBroadcast?> GetWithRecipientsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Broadcasts recorded before <paramref name="createdBefore"/> and not yet completed, oldest first.</summary>
    Task<IReadOnlyList<Guid>> ListUnfinishedIdsAsync(DateTime createdBefore, CancellationToken cancellationToken = default);

    /// <summary>The newest broadcasts first, counted in SQL.</summary>
    Task<IReadOnlyList<PlatformNewsSummaryRow>> ListRecentAsync(int take, CancellationToken cancellationToken = default);
}

/// <summary>One line of the console's history of sends.</summary>
public sealed record PlatformNewsSummaryRow(
    Guid Id,
    string Subject,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    string SentByName,
    int Clinics,
    int Recipients,
    int Sent,
    int Failed,
    IReadOnlyList<string> FailedClinics);
