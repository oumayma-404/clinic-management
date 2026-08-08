using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Models;

namespace ClinicManagement.Application.Features.Billing;

/// <summary>
/// The one authority on what window a caisse read covers — « du 3 au 5 août » resolved into the pair of UTC
/// instants every money query is bounded by.
///
/// <para><b>The defect it closes (AC-6).</b> The browser computed those instants itself:
/// <c>new Date(`${day}T00:00:00`).toISOString()</c> — midnight in the <b>workstation's</b> timezone. On a machine
/// set to anything but UTC+1 (a laptop brought back from a trip, a VM on UTC, a phone that followed the traveller)
/// « la caisse du 3 août » meant a window offset by hours from the Tunisian day, so a payment taken at 23:30 landed
/// in the wrong day's till, and on the 1st in the wrong month's revenue. The clinic's day is a fact about the
/// clinic, not about whoever is looking at it — so the client now sends a bare <c>YYYY-MM-DD</c> and the server
/// resolves it through <see cref="ClinicClock"/>, exactly like every other « today » in the product.</para>
///
/// <para><b>Why a type rather than two more lines in each handler.</b> The summary and the statement resolved their
/// bounds with byte-identical code, and they <i>have</i> to agree: a statement describing a different period from
/// the totals above it is worse than no statement. It was two copies of the same six lines, in two files, held
/// together by a comment saying « resolved exactly as GetCaisseSummaryQuery resolves them ».</para>
///
/// <para>⚠️ <b>The upper bound is the last tick of the day, never the next midnight.</b> Every money read is
/// inclusive on both ends, so <see cref="ClinicClock.EndOfLocalDayUtc"/> — which is the *next* midnight — would
/// count a payment recorded at exactly 00:00 in <b>both</b> adjacent periods (finding #20).</para>
/// </summary>
public sealed class CaissePeriod
{
    /// <summary>Inclusive lower bound, UTC.</summary>
    public DateTime From { get; }

    /// <summary>Inclusive upper bound, UTC — the last tick of the window.</summary>
    public DateTime To { get; }

    private CaissePeriod(DateTime from, DateTime to)
    {
        From = from;
        To = to;
    }

    /// <summary>
    /// Resolve the window a caisse read covers, from whichever of the four parameters the caller supplied.
    ///
    /// <para>Precedence, and it is deliberate: the <b>day keys</b> win, because they are the form that carries no
    /// timezone at all and are what every screen in the product now sends. The <c>from</c>/<c>to</c> instants are
    /// kept for the callers that legitimately have one — a job, an export driven by another read's bounds — and
    /// because removing them would break every existing integration for no gain.</para>
    ///
    /// <para>With nothing supplied at all the window is the current <b>clinic-local</b> day.</para>
    /// </summary>
    /// <param name="fromDay">Bare <c>YYYY-MM-DD</c>, a clinic-local calendar day.</param>
    /// <param name="toDay">Bare <c>YYYY-MM-DD</c>; defaults to <paramref name="fromDay"/>, so one day is one day.</param>
    /// <returns>The window, or a French refusal when a day key is unreadable or the window ends before it starts.</returns>
    public static Result<CaissePeriod> Resolve(string? fromDay, string? toDay, DateTime? from, DateTime? to)
    {
        if (!string.IsNullOrWhiteSpace(fromDay) || !string.IsNullOrWhiteSpace(toDay))
        {
            var start = ParseDay(fromDay);
            var end = ParseDay(toDay) ?? start;

            // A `toDay` alone is not « from the beginning of time to that day » — it is a client that lost half its
            // state. Refused rather than silently answered, because the answer would be a plausible wrong number.
            if (start is null || end is null)
            {
                return Result<CaissePeriod>.Failure("Période invalide : utilisez le format AAAA-MM-JJ.");
            }

            if (end < start)
            {
                return Result<CaissePeriod>.Failure("La date de fin doit être postérieure à la date de début.");
            }

            return Result<CaissePeriod>.Success(new CaissePeriod(
                ClinicClock.StartOfLocalDayUtc(start.Value),
                ClinicClock.LastTickOfLocalDayUtc(end.Value)));
        }

        var (todayFrom, todayToInclusive) = ClinicClock.TodayRangeUtc();
        var resolvedFrom = from ?? todayFrom;
        // A supplied `from` with no `to` still means "the 24 hours from there", unchanged — only the no-arguments
        // default moves off UTC.
        var resolvedTo = to ?? (from.HasValue ? resolvedFrom.AddDays(1).AddTicks(-1) : todayToInclusive);

        if (resolvedTo <= resolvedFrom)
        {
            return Result<CaissePeriod>.Failure("La date de fin doit être postérieure à la date de début.");
        }

        return Result<CaissePeriod>.Success(new CaissePeriod(resolvedFrom, resolvedTo));
    }

    /// <summary>
    /// <c>YYYY-MM-DD</c> and nothing else. <c>DateTime.TryParse</c> with the invariant culture would also accept
    /// « 03/08/2026 », whose meaning differs by locale — the one ambiguity a bare day key exists to remove.
    /// </summary>
    private static DateTime? ParseDay(string? value) =>
        DateTime.TryParseExact(
            value?.Trim(), "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var day)
            ? day
            : null;
}
