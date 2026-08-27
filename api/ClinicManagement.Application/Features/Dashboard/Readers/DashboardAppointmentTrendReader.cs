using ClinicManagement.Application.Common;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Dashboard.Readers;

/// <summary>
/// « Rendez-vous — 6 derniers mois »: how many séances the clinic had in each of the last
/// <see cref="DashboardPeriod.TrendMonths"/> clinic-local months, oldest first.
///
/// <para><b>One indexed aggregate per month, exactly like <see cref="DashboardTrendReader"/>.</b> The reasoning is
/// that reader's and is worth not re-learning: bucketing by clinic-local month <i>in SQL</i> has no valid
/// translation against a <c>timestamptz</c> column — it fails at runtime with
/// <c>42883: function pg_catalog.timezone(unknown, interval) does not exist</c> — so the month boundaries are
/// derived in C# through <see cref="ClinicClock"/> and each month is a plain <c>GROUP BY</c>. Six cheap aggregates
/// instead of one clever query that cannot run.</para>
///
/// <para><b>Both measures come from the same read.</b> <c>CountByStatusBetweenAsync</c> returns the whole status
/// breakdown, so <c>Total</c> and <c>Completed</c> are two projections of one <c>GROUP BY</c> and cannot disagree
/// about which month a visit fell in. It also makes a real guarantee rather than an aspirational one: this is the
/// same method and the same bounds « Rendez-vous honorés » is computed from, so the card and the figure above it
/// agree by construction.</para>
///
/// <para><b>Gaps are filled, never omitted</b> — a month the clinic saw nobody is a real and informative zero, and
/// dropping it would silently shorten the series and slide every later point left.</para>
/// </summary>
public class DashboardAppointmentTrendReader : IDashboardAppointmentTrendReader
{
    private readonly IAppointmentRepository _appointmentRepository;

    public DashboardAppointmentTrendReader(IAppointmentRepository appointmentRepository)
    {
        _appointmentRepository = appointmentRepository;
    }

    public async Task<List<MonthlyAppointmentPointDto>> ReadAsync(
        Guid clinicId, DashboardPeriod period, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var (windowStart, _) = period.TrendWindow(nowUtc);
        var firstMonth = ClinicClock.ToClinicLocal(windowStart);
        var firstOfFirstMonth = new DateTime(firstMonth.Year, firstMonth.Month, 1);
        var clinicToday = ClinicClock.ClinicToday(nowUtc);

        var points = new List<MonthlyAppointmentPointDto>(DashboardPeriod.TrendMonths);

        for (var offset = 0; offset < DashboardPeriod.TrendMonths; offset++)
        {
            var month = firstOfFirstMonth.AddMonths(offset);
            var lastDayOfMonth = month.AddMonths(1).AddDays(-1);

            var monthStartUtc = ClinicClock.StartOfLocalDayUtc(month);
            // The last TICK of the month's final local day, never the next midnight: CountByStatusBetweenAsync is
            // inclusive at both ends, so an exclusive upper bound counts a midnight booking in two adjacent months.
            var monthEndUtc = ClinicClock.LastTickOfLocalDayUtc(lastDayOfMonth);

            var counts = await _appointmentRepository.CountByStatusBetweenAsync(
                clinicId, monthStartUtc, monthEndUtc, cancellationToken);

            points.Add(new MonthlyAppointmentPointDto
            {
                Month = $"{month.Year:D4}-{month.Month:D2}",
                Total = counts.Values.Sum(),
                Completed = counts.TryGetValue(AppointmentStatus.Completed, out var done) ? done : 0,
                /*
                 * A month is partial when the clinic's today has not reached its last day. That is almost always
                 * only the final point, but it is computed per month rather than assumed of the last one — a
                 * clinic reading the dashboard on the 1st has a final point holding a single day, and hard-coding
                 * "the last one is partial" would be a rule that happens to be right rather than one that is.
                 */
                IsPartial = clinicToday < lastDayOfMonth
            });
        }

        return points;
    }
}
