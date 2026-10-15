using ClinicManagement.Application.Common.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace ClinicManagement.API.Hubs;

/// <summary>
/// SignalR-backed <see cref="IRealtimeNotifier"/>. Broadcasts to the originating clinic's group
/// (including the caller — a harmless self-refetch is acceptable). Real-time is additive (AC-5):
/// a broadcast failure is logged and swallowed so it can never fail the committed use case.
/// </summary>
public class SignalRRealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<ClinicHub> _hubContext;
    private readonly ILogger<SignalRRealtimeNotifier> _logger;

    public SignalRRealtimeNotifier(IHubContext<ClinicHub> hubContext, ILogger<SignalRRealtimeNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task NotifyEntityChangedAsync(Guid clinicId, string resource, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hubContext.Clients
                .Group(ClinicGroups.Name(clinicId))
                .SendAsync(ClinicHub.EntityChanged, resource, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast {Event} ({Resource}) to clinic {ClinicId}", ClinicHub.EntityChanged, resource, clinicId);
        }
    }

    public async Task NotifyStaffChatAsync(
        Guid clinicId, IReadOnlyCollection<string>? userIds, CancellationToken cancellationToken = default)
    {
        try
        {
            var clients = userIds is null
                ? _hubContext.Clients.Group(ClinicGroups.Name(clinicId))
                : _hubContext.Clients.Groups(userIds.Distinct(StringComparer.Ordinal).Select(u => ClinicGroups.User(clinicId, u)).ToList());

            await clients.SendAsync(ClinicHub.StaffChatChanged, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to signal {Event} in clinic {ClinicId}", ClinicHub.StaffChatChanged, clinicId);
        }
    }
}
