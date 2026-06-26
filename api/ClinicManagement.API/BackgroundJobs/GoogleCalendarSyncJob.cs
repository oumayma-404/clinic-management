using ClinicManagement.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Hangfire;

namespace ClinicManagement.API.BackgroundJobs;

public class GoogleCalendarSyncJob
{
    private readonly IGoogleCalendarSyncService _syncService;
    private readonly ILogger<GoogleCalendarSyncJob> _logger;

    public GoogleCalendarSyncJob(
        IGoogleCalendarSyncService syncService,
        ILogger<GoogleCalendarSyncJob> logger)
    {
        _syncService = syncService;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3)]
    public async Task SyncFromGoogleCalendar()
    {
        try
        {
            _logger.LogInformation("Starting Google Calendar sync job");
            await _syncService.SyncGoogleCalendarToAppointmentsAsync();
            _logger.LogInformation("Completed Google Calendar sync job");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not configured"))
        {
            _logger.LogWarning("Google Calendar is not configured. Skipping sync job");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Google Calendar sync job");
            throw;
        }
    }
}











