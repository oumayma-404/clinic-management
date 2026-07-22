using System.Text.Json;

namespace ClinicManagement.Application.DTOs;

/// <summary>
/// One day of a clinic's working hours (reliability-and-polish AC-7). <see cref="Day"/> is the (English)
/// weekday name the settings UI uses; <see cref="From"/>/<see cref="To"/> are <c>HH:mm</c>. Persisted as a
/// JSON array on the clinic (no per-day columns) and surfaced structurally on <see cref="ClinicDto"/>.
/// </summary>
public sealed class WorkingDayDto
{
    public string Day { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}

/// <summary>
/// (De)serialization for the clinic working-hours JSON. Case-insensitive on read (the frontend posts camelCase);
/// used both to validate/canonicalize an incoming payload and to project the stored JSON back to a structure.
/// </summary>
public static class WorkingHoursSerializer
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Parses stored/incoming JSON into a day list; returns null for blank or unparseable input.</summary>
    public static List<WorkingDayDto>? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<WorkingDayDto>>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Validates + canonicalizes an incoming payload to stored JSON; null when it isn't valid.</summary>
    public static string? Normalize(string? json)
    {
        var parsed = Parse(json);
        return parsed == null ? null : JsonSerializer.Serialize(parsed);
    }
}
