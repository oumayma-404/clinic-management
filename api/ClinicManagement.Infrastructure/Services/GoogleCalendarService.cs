using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Interfaces;
using System.Text.Json;

namespace ClinicManagement.Infrastructure.Services;

public class GoogleCalendarService : IGoogleCalendarService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GoogleCalendarService> _logger;
    private CalendarService? _calendarService;

    public GoogleCalendarService(IConfiguration configuration, ILogger<GoogleCalendarService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    private async Task<CalendarService> GetCalendarServiceAsync(CancellationToken cancellationToken = default)
    {
        if (_calendarService != null)
            return _calendarService;

        var clientId = _configuration["GoogleCalendar:ClientId"];
        var clientSecret = _configuration["GoogleCalendar:ClientSecret"];
        var refreshToken = _configuration["GoogleCalendar:RefreshToken"];
        var calendarId = _configuration["GoogleCalendar:CalendarId"] ?? "primary";

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            _logger.LogWarning("Google Calendar ClientId or ClientSecret are not configured. Calendar sync will be disabled.");
            throw new InvalidOperationException("Google Calendar ClientId and ClientSecret must be configured in appsettings.json");
        }

        if (string.IsNullOrEmpty(refreshToken))
        {
            _logger.LogWarning("Google Calendar refresh token is not configured. Please authorize the application first.");
            throw new InvalidOperationException("Google Calendar refresh token is not configured. Please click 'Sync to Google Calendar' to authorize the application.");
        }

        var clientSecrets = new ClientSecrets
        {
            ClientId = clientId,
            ClientSecret = clientSecret
        };

        var tokenResponse = await GoogleAuthUtils.RefreshAccessTokenAsync(
            clientSecrets,
            refreshToken,
            cancellationToken);

        var credential = GoogleCredential.FromAccessToken(tokenResponse.AccessToken);

        _calendarService = new CalendarService(new BaseClientService.Initializer()
        {
            HttpClientInitializer = credential,
            ApplicationName = "Clinic Management System"
        });

        return _calendarService;
    }

    public async Task<string?> CreateEventAsync(
        string summary,
        string description,
        DateTime startDateTime,
        DateTime endDateTime,
        string? location = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var service = await GetCalendarServiceAsync(cancellationToken);
            var calendarId = _configuration["GoogleCalendar:CalendarId"] ?? "primary";

            // Ensure dates are UTC
            var utcStart = startDateTime.Kind == DateTimeKind.Utc 
                ? startDateTime 
                : (startDateTime.Kind == DateTimeKind.Local ? startDateTime.ToUniversalTime() : DateTime.SpecifyKind(startDateTime, DateTimeKind.Utc));
            var utcEnd = endDateTime.Kind == DateTimeKind.Utc 
                ? endDateTime 
                : (endDateTime.Kind == DateTimeKind.Local ? endDateTime.ToUniversalTime() : DateTime.SpecifyKind(endDateTime, DateTimeKind.Utc));

            _logger.LogDebug("Creating Google Calendar event: Summary={Summary}, Start={StartDateTime} (UTC), End={EndDateTime} (UTC)", 
                summary, utcStart, utcEnd);

            var eventItem = new Event
            {
                Summary = summary,
                Description = description,
                Location = location,
                Start = new EventDateTime
                {
                    DateTimeDateTimeOffset = new DateTimeOffset(utcStart, TimeSpan.Zero),
                    TimeZone = "UTC"
                },
                End = new EventDateTime
                {
                    DateTimeDateTimeOffset = new DateTimeOffset(utcEnd, TimeSpan.Zero),
                    TimeZone = "UTC"
                }
            };

            var request = service.Events.Insert(eventItem, calendarId);
            var createdEvent = await request.ExecuteAsync(cancellationToken);

            _logger.LogInformation("Created Google Calendar event: {EventId}", createdEvent.Id);
            return createdEvent.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating Google Calendar event");
            throw;
        }
    }

    public async Task UpdateEventAsync(
        string eventId,
        string summary,
        string description,
        DateTime startDateTime,
        DateTime endDateTime,
        string? location = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var service = await GetCalendarServiceAsync(cancellationToken);
            var calendarId = _configuration["GoogleCalendar:CalendarId"] ?? "primary";

            var existingEvent = await service.Events.Get(calendarId, eventId).ExecuteAsync(cancellationToken);

            // Ensure dates are UTC
            var utcStart = startDateTime.Kind == DateTimeKind.Utc 
                ? startDateTime 
                : (startDateTime.Kind == DateTimeKind.Local ? startDateTime.ToUniversalTime() : DateTime.SpecifyKind(startDateTime, DateTimeKind.Utc));
            var utcEnd = endDateTime.Kind == DateTimeKind.Utc 
                ? endDateTime 
                : (endDateTime.Kind == DateTimeKind.Local ? endDateTime.ToUniversalTime() : DateTime.SpecifyKind(endDateTime, DateTimeKind.Utc));

            _logger.LogDebug("Updating Google Calendar event {EventId}: Summary={Summary}, Start={StartDateTime} (UTC), End={EndDateTime} (UTC)", 
                eventId, summary, utcStart, utcEnd);

            existingEvent.Summary = summary;
            existingEvent.Description = description;
            existingEvent.Location = location;
            existingEvent.Start = new EventDateTime
            {
                DateTimeDateTimeOffset = new DateTimeOffset(utcStart, TimeSpan.Zero),
                TimeZone = "UTC"
            };
            existingEvent.End = new EventDateTime
            {
                DateTimeDateTimeOffset = new DateTimeOffset(utcEnd, TimeSpan.Zero),
                TimeZone = "UTC"
            };

            await service.Events.Update(existingEvent, calendarId, eventId).ExecuteAsync(cancellationToken);
            _logger.LogInformation("Updated Google Calendar event: {EventId}", eventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating Google Calendar event: {EventId}", eventId);
            throw;
        }
    }

    public async Task DeleteEventAsync(string eventId, CancellationToken cancellationToken = default)
    {
        try
        {
            var service = await GetCalendarServiceAsync(cancellationToken);
            var calendarId = _configuration["GoogleCalendar:CalendarId"] ?? "primary";

            await service.Events.Delete(calendarId, eventId).ExecuteAsync(cancellationToken);
            _logger.LogInformation("Deleted Google Calendar event: {EventId}", eventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting Google Calendar event: {EventId}", eventId);
            throw;
        }
    }

    public async Task<IEnumerable<GoogleCalendarEvent>> GetEventsAsync(
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Getting events from Google Calendar. StartDate: {StartDate}, EndDate: {EndDate}", 
                startDate, endDate);
            
            var service = await GetCalendarServiceAsync(cancellationToken);
            var calendarId = _configuration["GoogleCalendar:CalendarId"] ?? "primary";
            
            _logger.LogInformation("Using calendar ID: {CalendarId}", calendarId);

            var request = service.Events.List(calendarId);
            var timeMin = startDate.HasValue 
                ? new DateTimeOffset(startDate.Value, TimeSpan.Zero)
                : new DateTimeOffset(DateTime.UtcNow.AddDays(-7), TimeSpan.Zero);
            var timeMax = endDate.HasValue
                ? new DateTimeOffset(endDate.Value, TimeSpan.Zero)
                : new DateTimeOffset(DateTime.UtcNow.AddDays(30), TimeSpan.Zero);
            
            request.TimeMinDateTimeOffset = timeMin;
            request.TimeMaxDateTimeOffset = timeMax;
            request.SingleEvents = true;
            request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;

            _logger.LogInformation("Requesting events from Google Calendar API (TimeMin: {TimeMin}, TimeMax: {TimeMax})", 
                timeMin, timeMax);

            var events = await request.ExecuteAsync(cancellationToken);

            var eventCount = events.Items?.Count() ?? 0;
            _logger.LogInformation("Google Calendar API returned {Count} events", eventCount);

            if (eventCount > 0)
            {
                _logger.LogDebug("First few events: {Events}", 
                    string.Join(", ", events.Items?.Take(3).Select(e => $"'{e.Summary}'") ?? Enumerable.Empty<string>()));
            }

            return events.Items?.Select(e => 
            {
                // Normalize DateTimeOffset to UTC DateTime
                var startDateTime = e.Start?.DateTimeDateTimeOffset?.UtcDateTime ?? DateTime.MinValue;
                var endDateTime = e.End?.DateTimeDateTimeOffset?.UtcDateTime ?? DateTime.MinValue;
                
                // Ensure Kind is UTC
                if (startDateTime != DateTime.MinValue && startDateTime.Kind != DateTimeKind.Utc)
                {
                    startDateTime = DateTime.SpecifyKind(startDateTime, DateTimeKind.Utc);
                }
                if (endDateTime != DateTime.MinValue && endDateTime.Kind != DateTimeKind.Utc)
                {
                    endDateTime = DateTime.SpecifyKind(endDateTime, DateTimeKind.Utc);
                }
                
                DateTime? updated = null;
                if (e.UpdatedDateTimeOffset.HasValue)
                {
                    updated = e.UpdatedDateTimeOffset.Value.UtcDateTime;
                    if (updated.Value.Kind != DateTimeKind.Utc)
                    {
                        updated = DateTime.SpecifyKind(updated.Value, DateTimeKind.Utc);
                    }
                }
                
                return new GoogleCalendarEvent
                {
                    Id = e.Id ?? string.Empty,
                    Summary = e.Summary ?? string.Empty,
                    Description = e.Description,
                    StartDateTime = startDateTime,
                    EndDateTime = endDateTime,
                    Location = e.Location,
                    Updated = updated
                };
            }) ?? Enumerable.Empty<GoogleCalendarEvent>();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not configured"))
        {
            _logger.LogWarning("Google Calendar is not configured. Cannot get events.");
            return Enumerable.Empty<GoogleCalendarEvent>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Google Calendar events: {Message}", ex.Message);
            throw;
        }
    }
}

// Helper class for token refresh
internal static class GoogleAuthUtils
{
    public static async Task<TokenResponse> RefreshAccessTokenAsync(
        ClientSecrets clientSecrets,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var tokenRequest = new TokenRequest
        {
            ClientId = clientSecrets.ClientId,
            ClientSecret = clientSecrets.ClientSecret,
            RefreshToken = refreshToken,
            GrantType = "refresh_token"
        };

        using var httpClient = new HttpClient();
        var response = await httpClient.PostAsync(
            "https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "client_id", tokenRequest.ClientId },
                { "client_secret", tokenRequest.ClientSecret },
                { "refresh_token", tokenRequest.RefreshToken },
                { "grant_type", tokenRequest.GrantType }
            }),
            cancellationToken);

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(content);
            var errorMessage = errorData?.ContainsKey("error_description") == true 
                ? errorData["error_description"].GetString() 
                : errorData?.ContainsKey("error") == true 
                    ? errorData["error"].GetString() 
                    : content;
            
            throw new InvalidOperationException(
                $"Failed to refresh access token. Status: {response.StatusCode}, Error: {errorMessage}. " +
                $"This usually means the refresh token is invalid or has been revoked. Please re-authorize the application.");
        }
        
        var tokenData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(content);

        return new TokenResponse
        {
            AccessToken = tokenData?["access_token"].GetString() ?? throw new InvalidOperationException("Failed to get access token"),
            ExpiresIn = tokenData?.ContainsKey("expires_in") == true ? tokenData["expires_in"].GetInt32() : 3600
        };
    }

    private class TokenRequest
    {
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public string GrantType { get; set; } = string.Empty;
    }

    public class TokenResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public int ExpiresIn { get; set; }
    }
}

