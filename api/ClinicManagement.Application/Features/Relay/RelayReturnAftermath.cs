using ClinicManagement.Application.Common.Behaviors;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Relay;

/// <summary>What the cloud does once a cut is back and it holds the cabinet's saves again (D18 phase 2).</summary>
public interface IRelayReturnAftermath
{
    Task AfterReturnAsync(Guid clinicId, DateTime returnAppliedAtUtc, CancellationToken cancellationToken);
}

/// <summary>
/// After the return (<c>clinic-pc-copy</c> AC-5.4, AC-5.8): every visit the cut created or moved goes to Google Agenda,
/// and every open screen of the cabinet refreshes — the cut's rows arrived in SQL, so no command broadcast them.
/// Best-effort, after the commit: the cabinet's saves are back on the cloud whatever this does.
///
/// <para>The reminders need nothing here: the PC's queued ones arrived with the return, and the dispatcher sends one
/// per visit and none for a visit already passed (EC-16) once the cloud is no longer fenced.</para>
/// </summary>
public sealed class RelayReturnAftermath : IRelayReturnAftermath
{
    private readonly IRelayHandbackStore _store;
    private readonly IRealtimeNotifier _realtime;
    private readonly IAppointmentGoogleSyncDispatcher _google;
    private readonly ILogger<RelayReturnAftermath> _logger;

    public RelayReturnAftermath(
        IRelayHandbackStore store, IRealtimeNotifier realtime, IAppointmentGoogleSyncDispatcher google,
        ILogger<RelayReturnAftermath> logger)
    {
        _store = store;
        _realtime = realtime;
        _google = google;
        _logger = logger;
    }

    public async Task AfterReturnAsync(Guid clinicId, DateTime returnAppliedAtUtc, CancellationToken cancellationToken)
    {
        var pushed = 0;
        try
        {
            var visits = await _store.KeysWrittenByReturnAsync(clinicId, nameof(Appointment), returnAppliedAtUtc, cancellationToken);
            foreach (var key in visits)
            {
                if (Guid.TryParse(key, out var appointmentId))
                {
                    _google.Dispatch(appointmentId, clinicId);
                    pushed++;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "D18: the visits of the cut could not be sent to Google Agenda for clinic {ClinicId}.", clinicId);
        }

        var keys = RealtimeResourceResolver.AllKeys();
        _logger.LogInformation("D18: after the return of clinic {ClinicId}, {Visits} visit(s) sent to Google Agenda and {Keys} screen key(s) refreshed.",
            clinicId, pushed, keys.Count);
        foreach (var key in keys)
        {
            try
            {
                await _realtime.NotifyEntityChangedAsync(clinicId, key, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "D18: the refresh « {Key} » after the return failed for clinic {ClinicId}.", key, clinicId);
            }
        }
    }
}
