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

    Task DeleteEventAsync(GoogleCalendarConnection connection, string eventId, CancellationToken cancellationToken = default);

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











