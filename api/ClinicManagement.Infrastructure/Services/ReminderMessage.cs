using System.Globalization;
using System.Text.RegularExpressions;
using ClinicManagement.Application.Common;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// The one place that knows how a reminder states an appointment's moment — and therefore the one place able to
/// tell whether a queued reminder is still telling the truth.
///
/// <para><b>Why the check reads the body.</b> A reminder's text and its <c>ScheduledFor</c> are frozen at enqueue,
/// so any write path that moves an appointment without voiding and re-enqueuing leaves a row that will cheerfully
/// announce the old day (L3b — the Google→App sync did exactly that: it called <c>Reschedule</c> and committed
/// straight through the repository, with <c>IReminderScheduler</c> not injected into that class at all). The
/// dispatcher's existing safety net re-checked the appointment's <b>status</b> and never its <b>time</b>.</para>
///
/// <para>Reading the body rather than a stored snapshot is deliberate: it needs no column, and it validates the
/// exact thing the patient will read. The formatter below is shared with the scheduler that writes the body, so
/// the writer and the checker cannot drift — which is the whole reason this is a class and not two string
/// literals in two files.</para>
/// </summary>
public static class ReminderMessage
{
    private static readonly CultureInfo FrCulture = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>
    /// Does the message carry a day <b>and</b> a time at all? A clinic whose custom wording omits
    /// <c>{date}</c> has nothing to be wrong about, and must not have its reminders dropped by a check for a
    /// statement it never makes.
    /// </summary>
    private static readonly Regex CarriesADay = new(@"\b\d{2}/\d{2}/\d{4}\b", RegexOptions.Compiled);
    private static readonly Regex CarriesAnHour = new(@"\b\d{1,2}:\d{2}\b", RegexOptions.Compiled);

    /// <summary>
    /// How every reminder body states the appointment's moment: clinic-local (Tunisia is UTC+1), French order.
    /// </summary>
    public static string FormatAppointmentMoment(DateTime appointmentUtc) =>
        ClinicClock.ToClinicLocal(appointmentUtc).ToString("dd/MM/yyyy 'à' HH:mm", FrCulture);

    /// <summary>
    /// True when the message states a moment and that moment is <b>not</b> the appointment's current one — i.e.
    /// the appointment moved after this row was queued and nothing re-enqueued it.
    ///
    /// <para>Conservative in the one direction that matters: a message with no day-and-time token is never
    /// stale, and neither is one that still contains the current moment. A clinic that hard-codes a *fixed*
    /// date into its template would read as stale — but a template naming one calendar day is not a template,
    /// and the alternative (trusting the row) is the defect this exists to close.</para>
    /// </summary>
    public static bool AnnouncesStaleMoment(string? message, DateTime appointmentUtc)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        if (!CarriesADay.IsMatch(message) || !CarriesAnHour.IsMatch(message))
        {
            return false;
        }

        return !message.Contains(FormatAppointmentMoment(appointmentUtc), StringComparison.Ordinal);
    }
}
