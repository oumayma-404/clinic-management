using System.Globalization;
using System.Text.Json;
using ClinicManagement.Application.Common.Models;

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

/// <summary>How a stored working-hours value read back (AC-P1.24).</summary>
public enum WorkingHoursReadState
{
    /// <summary>Nothing stored. Per AC-P1.30 this means <b>unrestricted</b>, not "closed".</summary>
    Unset = 0,

    /// <summary>Stored and valid.</summary>
    Valid = 1,

    /// <summary>
    /// Stored but unreadable — malformed JSON, or content the validation rejects. Deliberately distinct from
    /// <see cref="Unset"/>: collapsing the two would silently turn a clinic's broken hours into
    /// "no restriction", losing enforcement they believe they have without telling them.
    /// </summary>
    Unreadable = 2,
}

/// <summary>The outcome of reading a stored working-hours JSON value.</summary>
public sealed record WorkingHoursRead(WorkingHoursReadState State, List<WorkingDayDto> Days)
{
    public static readonly WorkingHoursRead Unset = new(WorkingHoursReadState.Unset, new List<WorkingDayDto>());
    public static readonly WorkingHoursRead Unreadable = new(WorkingHoursReadState.Unreadable, new List<WorkingDayDto>());
}

/// <summary>
/// (De)serialization and <b>validation</b> for the working-hours JSON.
/// <para>
/// Adjacent defect <b>A-5</b>: <see cref="Normalize"/> claimed to validate but its only failure mode was a
/// <c>JsonException</c>. Nothing checked that a day was a real weekday, that <c>From</c>/<c>To</c> parsed as
/// <c>HH:mm</c> at all, that <c>From &lt; To</c>, or that a day appeared once. <c>[{"day":"Blursday"}]</c> and
/// <c>{"from":"banana"}</c> both round-tripped and persisted; worse, <c>"[]"</c> was "valid" and **wiped a
/// clinic's hours** through <c>UpdateClinicCommand</c>. Enforcing booking against that (AC-P1.28) would have
/// meant enforcing garbage, which is why validation lands in the same part.
/// </para>
/// </summary>
public static class WorkingHoursSerializer
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// The seven weekday names the UI stores, in <see cref="DayOfWeek"/> order so the index doubles as the
    /// mapping to a real date's day. English keys are the stored form — the French labels are display-only
    /// (`web/lib/working-hours.ts`), the same convention as the specialty map.
    /// </summary>
    public static readonly string[] Weekdays =
    {
        "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday",
    };

    /// <summary>Parses JSON into a day list; returns null for blank or unparseable input. Tolerant by design.</summary>
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

    /// <summary>
    /// Read a <b>stored</b> value, distinguishing "never configured" from "configured but broken" (AC-P1.24).
    /// Never throws — a bad row must not make the settings screen or the booking guard unusable.
    /// </summary>
    public static WorkingHoursRead Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return WorkingHoursRead.Unset;
        }

        var parsed = Parse(json);
        if (parsed == null)
        {
            return WorkingHoursRead.Unreadable;
        }

        // An empty array is treated as Unset rather than Unreadable: it is what the old Normalize happily
        // produced, so existing rows may legitimately hold it, and "no days" genuinely carries no restriction.
        if (parsed.Count == 0)
        {
            return WorkingHoursRead.Unset;
        }

        return Validate(parsed).IsFailure
            ? WorkingHoursRead.Unreadable
            : new WorkingHoursRead(WorkingHoursReadState.Valid, parsed);
    }

    /// <summary>
    /// Validate an <b>incoming</b> payload (AC-P1.23). French failures, never a silently-persisted string.
    /// </summary>
    public static Result<List<WorkingDayDto>> Validate(List<WorkingDayDto>? days)
    {
        if (days == null)
        {
            return Result<List<WorkingDayDto>>.Failure("Horaires de travail illisibles.");
        }

        if (days.Count == 0)
        {
            // Rejected rather than accepted-as-empty: the caller that means "clear the override" says so by
            // sending null, and accepting "[]" here is how a clinic's real hours got wiped.
            return Result<List<WorkingDayDto>>.Failure(
                "Les horaires ne peuvent pas être vides. Indiquez au moins un jour, ou supprimez les horaires spécifiques.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var day in days)
        {
            var name = day.Day?.Trim() ?? string.Empty;
            if (!Weekdays.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                return Result<List<WorkingDayDto>>.Failure($"Jour inconnu dans les horaires : « {name} ».");
            }

            if (!seen.Add(name))
            {
                return Result<List<WorkingDayDto>>.Failure($"Le jour « {FrenchDay(name)} » apparaît plusieurs fois.");
            }

            // Times are only meaningful on an open day; a closed day's blank From/To must stay acceptable, or
            // every existing "Sunday closed" row becomes invalid.
            if (!day.Enabled)
            {
                continue;
            }

            if (!TryParseTime(day.From, out var from))
            {
                return Result<List<WorkingDayDto>>.Failure(
                    $"Heure d'ouverture invalide pour « {FrenchDay(name)} » : « {day.From} » (format attendu HH:mm).");
            }

            if (!TryParseTime(day.To, out var to))
            {
                return Result<List<WorkingDayDto>>.Failure(
                    $"Heure de fermeture invalide pour « {FrenchDay(name)} » : « {day.To} » (format attendu HH:mm).");
            }

            if (from >= to)
            {
                return Result<List<WorkingDayDto>>.Failure(
                    $"« {FrenchDay(name)} » : l'heure de fermeture doit être postérieure à l'heure d'ouverture.");
            }
        }

        return Result<List<WorkingDayDto>>.Success(days);
    }

    /// <summary>Validate and canonicalize an incoming payload to stored JSON, or a French failure.</summary>
    public static Result<string> ValidateToJson(string? json)
    {
        var validated = Validate(Parse(json));
        return validated.IsFailure
            ? Result<string>.Failure(validated.Error ?? "Horaires de travail invalides.")
            : Result<string>.Success(JsonSerializer.Serialize(validated.Value));
    }

    /// <summary>
    /// Legacy shape kept for call sites that only need "canonical JSON or null". Now genuinely validating —
    /// a payload that fails <see cref="Validate"/> returns null instead of being persisted.
    /// </summary>
    public static string? Normalize(string? json)
    {
        var validated = ValidateToJson(json);
        return validated.IsSuccess ? validated.Value : null;
    }

    /// <summary><c>HH:mm</c> (24h) only — the format the UI's <c>&lt;input type="time"&gt;</c> emits.</summary>
    public static bool TryParseTime(string? value, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return TimeSpan.TryParseExact(value.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out time);
    }

    /// <summary>French weekday label for a message. Display-only; the stored key stays English.</summary>
    public static string FrenchDay(string englishDay) => englishDay?.Trim().ToLowerInvariant() switch
    {
        "monday" => "lundi",
        "tuesday" => "mardi",
        "wednesday" => "mercredi",
        "thursday" => "jeudi",
        "friday" => "vendredi",
        "saturday" => "samedi",
        "sunday" => "dimanche",
        _ => englishDay ?? string.Empty,
    };
}
