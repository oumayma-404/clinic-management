namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// A single clinic's Google Calendar connection — its own OAuth refresh token and target calendar id.
/// Passed explicitly into every low-level call so the calendar client is NEVER a process-wide/global
/// account (which leaked every clinic's appointments into one shared calendar). <c>CalendarId</c> null =
/// the connected account's "primary" calendar (feature cloud-security-and-tenant-isolation, #4).
/// </summary>
public sealed record GoogleCalendarConnection(string RefreshToken, string? CalendarId);

public interface IGoogleCalendarService
{
    Task<string?> CreateEventAsync(
        GoogleCalendarConnection connection,
        string summary,
        string description,
        DateTime startDateTime,
        DateTime endDateTime,
        string? location = null,
        CancellationToken cancellationToken = default);

    Task UpdateEventAsync(
        GoogleCalendarConnection connection,
        string eventId,
        string summary,
        string description,
        DateTime startDateTime,
        DateTime endDateTime,
        string? location = null,
        CancellationToken cancellationToken = default);

    /*
     * ⚠️ **There is deliberately NO DeleteEventAsync, and adding one back is a defect.**
     *
     * There was one. It was called whenever an appointment became `Cancelled` or `Completed`, so marking a séance
     * « Terminé » — the most ordinary action in the product, asked for on every visit by « À clôturer » and reached
     * by `AppointmentProgressJob` too — erased that appointment from the practice's own Google agenda and nulled the
     * event id, silently. The day a cabinet had worked came out emptier than the day it had not. The same call on
     * cancellation is how a cabinet tidying up a mistaken import destroyed a hundred real entries of its own.
     *
     * <b>The calendar belongs to the practice.</b> This product may add to it and correct what it added; it may not
     * remove anything from it. Removing the method rather than its call sites is the point — a comment is advice,
     * an absent method is a compile error. `GoogleCalendarNeverDeletesTests` asserts it stays absent.
     *
     * A terminal visit keeps its event and the event is UPDATED to say so (`Status: Cancelled` / `Status:
     * Completed`), which tells the practice strictly more than deleting it did.
     */

    Task<IEnumerable<GoogleCalendarEvent>> GetEventsAsync(
        GoogleCalendarConnection connection,
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken cancellationToken = default);
}

public class GoogleCalendarEvent
{
    public string Id { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime StartDateTime { get; set; }
    public DateTime EndDateTime { get; set; }
    public string? Location { get; set; }
    public DateTime? Updated { get; set; }
}











