using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

public interface IAttendanceTerminalRepository
{
    Task<AttendanceTerminal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The active borne holding this secret, in ANY cabinet. A borne request has no user and so no clinic yet:
    /// this is the read that tells the caller which clinic to scope to.
    /// </summary>
    Task<AttendanceTerminal?> FindActiveBySecretHashAcrossClinicsAsync(
        string secretHash, CancellationToken cancellationToken = default);

    /// <summary>The cabinet's bornes, active first, newest first.</summary>
    Task<IReadOnlyList<AttendanceTerminal>> ListAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The cabinet's bornes a device can punch on (not retired, not waiting) and when the first went live. Untracked —
    /// the arrival lock asks it.
    /// </summary>
    Task<LiveBornes> GetLiveBornesAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>The borne assigned to this signed-in device and not yet claimed, or null. Tracked.</summary>
    Task<AttendanceTerminal?> FindPendingForSessionAsync(Guid clinicId, Guid sessionFamilyId, CancellationToken cancellationToken = default);

    Task AddAsync(AttendanceTerminal terminal, CancellationToken cancellationToken = default);
}

/// <summary>Whether any borne is live, and the earliest instant one went live (null = before that was recorded).</summary>
public sealed record LiveBornes(bool Any, DateTime? FirstActivatedAtUtc)
{
    public static readonly LiveBornes None = new(false, null);

    /// <summary>The one fold, for the SQL read and the in-memory list alike: a live borne with no recorded activation is old.</summary>
    public static LiveBornes From(IReadOnlyCollection<DateTime?> liveActivations) =>
        liveActivations.Count == 0
            ? None
            : new LiveBornes(true, liveActivations.Any(a => a is null) ? null : liveActivations.Min());

    public static LiveBornes Of(IEnumerable<AttendanceTerminal> terminals) =>
        From(terminals.Where(t => t.IsLive).Select(t => t.ActivatedAtUtc).ToList());
}
