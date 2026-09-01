namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Appointment → Google Calendar, and nothing in the other direction.
///
/// <para>⚠️ <b><c>SyncGoogleCalendarToAppointmentsAsync</c> was removed with the « Importer depuis Google »
/// feature.</b> Google→App was a mass write with no bounds and no way back: one press turned 97 days of a
/// practice's calendar into appointment rows, and the past week of them landed on « À clôturer » as visits nobody
/// could honestly close — which is how a cabinet ended up cancelling them and inflating its own « taux
/// d'absence ». The button, the 15-minute <c>GoogleCalendarImportJob</c> and the import half of
/// <c>GoogleCalendarSyncService</c> went with it.</para>
///
/// <para><b>Do not re-add a Google→App pull to this interface without reading</b>
/// <c>features/calendar-import-revert/notes.md</c>. The undo it needed is still in the product — a pull that did
/// not open a <c>CalendarImportRun</c> would be exactly the unrecoverable write that was retired.</para>
/// </summary>
public interface IGoogleCalendarSyncService
{
    Task SyncAppointmentToGoogleCalendarAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default);
}
