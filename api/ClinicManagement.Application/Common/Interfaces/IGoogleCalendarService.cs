namespace ClinicManagement.Application.Common.Interfaces;

public interface IGoogleCalendarService
{
    Task<string?> CreateEventAsync(
        string summary,
        string description,
        DateTime startDateTime,
        DateTime endDateTime,
        string? location = null,
        CancellationToken cancellationToken = default);

    Task UpdateEventAsync(
        string eventId,
        string summary,
        string description,
        DateTime startDateTime,
        DateTime endDateTime,
        string? location = null,
        CancellationToken cancellationToken = default);

    Task DeleteEventAsync(string eventId, CancellationToken cancellationToken = default);

    Task<IEnumerable<GoogleCalendarEvent>> GetEventsAsync(
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











