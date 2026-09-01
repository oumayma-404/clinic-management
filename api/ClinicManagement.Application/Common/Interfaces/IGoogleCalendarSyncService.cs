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
    /// <param name="triggeredByUserId">
    /// Who set the pass off, recorded on its <c>CalendarImportRun</c> — a user id from the button, or
    /// <c>job|GoogleCalendarImportJob</c> from the schedule. <b>A parameter for the same reason the clinic is
    /// one</b>: this service has no HTTP context, and a run whose author is unknown is one a practice cannot tell
    /// apart from the one it pressed itself.
    /// </param>
    Task<CalendarImportOutcome> SyncGoogleCalendarToAppointmentsAsync(
        Guid clinicId,
        string? triggeredByUserId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// What one Google→App pass did — returned so the button can say « 143 rendez-vous et 96 fiches importés » and
/// offer to undo it.
///
/// <para>It replaced a <c>void</c> whose endpoint answered <c>{ message, timestamp }</c>: the practice pressed a
/// button that rewrote a hundred rows and was told the time.</para>
/// </summary>
/// <param name="RunId">The recorded pass. Null only when no clinic connection was found and nothing ran.</param>
public readonly record struct CalendarImportOutcome(
    Guid? RunId,
    int AppointmentsCreated,
    int PatientsCreated,
    int AppointmentsUpdated,
    int AppointmentsLinked)
{
    /// <summary>A clinic with no Google connection — nothing ran, nothing was recorded.</summary>
    public static readonly CalendarImportOutcome NotConnected = new(null, 0, 0, 0, 0);
}











