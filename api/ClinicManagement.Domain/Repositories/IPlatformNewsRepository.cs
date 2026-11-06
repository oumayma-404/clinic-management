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

    /// <summary>Tracked, without its recipients — what « Masquer / Afficher dans l'app » needs.</summary>
    Task<PlatformNewsBroadcast?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every news the app shows, newest first, with its items. Small by nature: the vendor writes a few a month.</summary>
    Task<IReadOnlyList<PlatformNewsAppRow>> ListVisibleInAppAsync(CancellationToken cancellationToken = default);

    /// <summary>How many news the app shows that were sent after <paramref name="after"/>.</summary>
    Task<int> CountVisibleInAppAfterAsync(DateTime after, CancellationToken cancellationToken = default);

    /// <summary>When the newest news the app shows was sent; null when it shows none.</summary>
    Task<DateTime?> GetNewestVisibleInAppAtAsync(CancellationToken cancellationToken = default);

    Task<PlatformNewsReadMarker?> GetReadMarkerAsync(string userId, CancellationToken cancellationToken = default);

    Task AddReadMarkerAsync(PlatformNewsReadMarker marker, CancellationToken cancellationToken = default);
}

public sealed record PlatformNewsAppRow(
    Guid Id,
    string Subject,
    DateTime CreatedAtUtc,
    IReadOnlyList<PlatformNewsAppItem> Items);

public sealed record PlatformNewsAppItem(string Emoji, string Text);

/// <summary>One line of the console's history of sends.</summary>
public sealed record PlatformNewsSummaryRow(
    Guid Id,
    string Subject,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    string SentByName,
    bool VisibleInApp,
    int Clinics,
    int Recipients,
    int Sent,
    int Failed,
    IReadOnlyList<string> FailedClinics);
