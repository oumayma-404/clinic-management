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
    /// Signals every connected client of <paramref name="clinicId"/> that appointments changed, so
    /// their calendar refetches. Carries no payload — clients refetch on the signal.
    /// </summary>
    Task NotifyAppointmentsChangedAsync(Guid clinicId, CancellationToken cancellationToken = default);
}
