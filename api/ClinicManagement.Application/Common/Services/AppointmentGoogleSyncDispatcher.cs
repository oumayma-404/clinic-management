using ClinicManagement.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Common.Services;

/// <summary>
/// Fire-and-forget dispatcher that pushes an appointment to Google Calendar after its command commits,
/// gated on the SERVER's internet reachability. A fresh DI scope is created because the work outlives the
/// request scope (avoids DbContext disposal). Failures never propagate to the caller. The "not configured"
/// case is handled inside <c>IGoogleCalendarSyncService</c>, so only unexpected errors are logged here.
/// </summary>
public class AppointmentGoogleSyncDispatcher : IAppointmentGoogleSyncDispatcher
{
    private readonly IServiceScopeFactory _scopeFactory;

    public AppointmentGoogleSyncDispatcher(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public void Dispatch(Guid appointmentId)
    {
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppointmentGoogleSyncDispatcher>>();
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

                // The LAN clients have no internet of their own — the server makes the outbound call, so gate
                // on ITS egress. Skip the sync (and its OAuth refresh) when offline; connectivity returning
                // re-enables it on the next create/update. IInternetProbe caches, so this is cheap.
                var probe = scope.ServiceProvider.GetRequiredService<IInternetProbe>();
                if (!await probe.IsInternetReachableAsync(cts.Token))
                {
                    logger.LogDebug("Server offline; skipping Google Calendar sync for appointment {AppointmentId}", appointmentId);
                    return;
                }

                var syncService = scope.ServiceProvider.GetRequiredService<IGoogleCalendarSyncService>();
                await syncService.SyncAppointmentToGoogleCalendarAsync(appointmentId, cts.Token);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error syncing appointment {AppointmentId} to Google Calendar", appointmentId);
            }
        });
    }
}
