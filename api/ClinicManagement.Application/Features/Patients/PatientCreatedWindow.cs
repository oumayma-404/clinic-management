using System.Globalization;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Models;

namespace ClinicManagement.Application.Features.Patients;

/// <summary>
/// The patients list's « Date de création » window, from what the caller sent: either the dashboard's two instants
/// (« Nouveaux patients » drill-through) or two <b>Tunisian days</b> (<c>AAAA-MM-JJ</c>) typed in the filter.
///
/// <para>⚠️ Days are turned into instants HERE, never in the browser — `CaissePeriod`'s rule: a browser builds
/// midnight on the workstation's clock, so « inscrits le 3 mars » would cover a window offset by hours from the
/// Tunisian day. Each bound is independent (« depuis le… », « jusqu'au… »), and a day key wins over an instant.</para>
/// </summary>
public static class PatientCreatedWindow
{
    public const string InvalidDay = "Date de création invalide : utilisez le format AAAA-MM-JJ.";
    public const string Inverted = "La date de fin doit être postérieure à la date de début.";

    public static Result<(DateTime? From, DateTime? To)> Resolve(
        string? fromDay, string? toDay, DateTime? from, DateTime? to)
    {
        var resolvedFrom = from;
        var resolvedTo = to;

        if (!string.IsNullOrWhiteSpace(fromDay))
        {
            if (ParseDay(fromDay) is not { } day)
            {
                return Result<(DateTime?, DateTime?)>.Failure(InvalidDay);
            }
            resolvedFrom = ClinicClock.StartOfLocalDayUtc(day);
        }

        if (!string.IsNullOrWhiteSpace(toDay))
        {
            if (ParseDay(toDay) is not { } day)
            {
                return Result<(DateTime?, DateTime?)>.Failure(InvalidDay);
            }
            // The last tick of that Tunisian day — inclusive, like every other bound this read takes.
            resolvedTo = ClinicClock.LastTickOfLocalDayUtc(day);
        }

        if (resolvedFrom is { } f && resolvedTo is { } t && t < f)
        {
            return Result<(DateTime?, DateTime?)>.Failure(Inverted);
        }

        return Result<(DateTime?, DateTime?)>.Success((resolvedFrom, resolvedTo));
    }

    private static DateTime? ParseDay(string value) =>
        DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : null;
}
