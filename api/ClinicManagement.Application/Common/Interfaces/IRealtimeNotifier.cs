namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Outbound seam for pushing real-time change notifications to connected clients of a clinic.
/// Implemented in the API layer over SignalR (the hub is a presentation concern). Real-time is
/// additive: implementations MUST NOT throw into the caller — a failed broadcast can never fail
/// the committed use case that raised it.
/// </summary>
public interface IRealtimeNotifier
{
    /// <summary>
    /// Signals every connected client of <paramref name="clinicId"/> that a resource changed, so
    /// views showing <paramref name="resource"/> refetch. <paramref name="resource"/> is a lowercase
    /// entity key (e.g. <c>"appointments"</c>, <c>"patients"</c>) derived from the mutating command's
    /// feature area. Carries no other payload — clients refetch on the signal.
    /// </summary>
    Task NotifyEntityChangedAsync(Guid clinicId, string resource, CancellationToken cancellationToken = default);
}
