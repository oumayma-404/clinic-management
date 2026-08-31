namespace ClinicManagement.Application.Common.Interfaces;

public interface IGoogleCalendarSyncService
{
    Task SyncAppointmentToGoogleCalendarAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pull a clinic's Google events into its appointments (Google→App).
    ///
    /// <para>⚠️ <b>The clinic is a parameter, not read from the HTTP context.</b> It used to resolve itself through
    /// <c>ICurrentClinicResolver</c>, which made the direction reachable only from a request — so the recurring
    /// import (`GoogleCalendarImportJob`) had nowhere to say which practice it was importing for, and a job's
    /// tenant scope is <c>Unset</c>, which reads zero rows and logs a clean pass.</para>
    /// </summary>
    Task SyncGoogleCalendarToAppointmentsAsync(
        Guid clinicId,
        CancellationToken cancellationToken = default);
}











