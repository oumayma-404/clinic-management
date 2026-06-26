namespace ClinicManagement.Application.Common.Interfaces;

public interface IGoogleCalendarSyncService
{
    Task SyncAppointmentToGoogleCalendarAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default);

    Task SyncGoogleCalendarToAppointmentsAsync(
        CancellationToken cancellationToken = default);
}











