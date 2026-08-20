using ClinicManagement.Application.Common;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Dashboard.Readers;

/// <summary>
/// The « Tendance » sparkline: collected cash for each of the last <see cref="DashboardPeriod.TrendMonths"/>
/// clinic-local months, oldest first.
///
/// <para><b>One query per month, on purpose.</b> The first implementation was a single grouped read that bucketed by
/// the clinic-local month <i>in SQL</i> — <c>GroupBy(p => p.PaidOn.AddMinutes(offset).Month)</c>. It compiled, its unit
/// tests passed (they mock the repository), and it failed on the first real request with
/// <c>42883: function pg_catalog.timezone(unknown, interval) does not exist</c>: date arithmetic on a
/// <c>timestamptz</c> column has no valid translation here. Deriving each month's boundaries in C# through
/// <see cref="ClinicClock"/> and asking for a plain <c>SUM</c> over each removes timezone maths from the database
/// entirely — six indexed aggregates instead of one, which is a trade worth making for a read that cannot fail.</para>
///
/// <para>It also makes a documented guarantee <b>true rather than aspirational</b>: each point is produced by the very
/// method the « Encaissé » card uses (<c>GetCollectedBetweenAsync</c>), over bounds built by the same clock, so the
/// sparkline's last point and the card above it cannot disagree.</para>
///
/// <para><b>The current month is flagged, not silently plotted like the rest.</b> Read on the 3rd, the final point
/// holds three days of takings beside five whole months, which draws a collapse in revenue that is not in the data.
/// <c>MonthlyCollectedPointDto.IsPartial</c> says so and the client marks that point; the figure itself is left
/// exactly as computed, because it is correct — it is the <i>comparison</i> that is not.</para>
///
/// <para><b>Gaps are filled, never omitted.</b> A month in which the clinic collected nothing is a real and
/// informative zero — dropping it would silently shorten the series and slide every later point left, so a quiet
/// August would render as though it never happened.</para>
///
/// <para><b>Invoice payments only.</b> The series deliberately excludes treatment-plan installments, unlike the
/// « Encaissé » card. Installment collections are attributed by a cumulative <c>AmountPaid</c> and a last-payment date
/// rather than a per-payment date at this aggregate level, so bucketing them by month would attribute a schedule
/// topped up across two months entirely to the later one. A trend line that is wrong about which month money arrived
/// in is worse than a narrower one that is right, so the card is labelled for what it actually plots.</para>
/// </summary>
public class DashboardTrendReader : IDashboardTrendReader
{
    private readonly IInvoiceRepository _invoiceRepository;

    public DashboardTrendReader(IInvoiceRepository invoiceRepository)
    {
        _invoiceRepository = invoiceRepository;
    }

    public async Task<List<MonthlyCollectedPointDto>> ReadAsync(
        Guid clinicId, DashboardPeriod period, DateTime nowUtc, CancellationToken cancellationToken)
    {
        // The first month of the window, as a clinic-local calendar month.
        var (windowStart, _) = period.TrendWindow(nowUtc);
        var firstMonth = ClinicClock.ToClinicLocal(windowStart);
        var firstOfFirstMonth = new DateTime(firstMonth.Year, firstMonth.Month, 1);
        var clinicToday = ClinicClock.ClinicToday(nowUtc);

        var points = new List<MonthlyCollectedPointDto>(DashboardPeriod.TrendMonths);

        for (var offset = 0; offset < DashboardPeriod.TrendMonths; offset++)
        {
            var month = firstOfFirstMonth.AddMonths(offset);
            var lastDayOfMonth = month.AddMonths(1).AddDays(-1);

            // Both bounds are clinic-local midnights expressed as UTC, and the upper one is the last TICK of the
            // month's final day — GetCollectedBetweenAsync is inclusive on both ends, so the next midnight would
            // count a payment made at exactly that instant in this month AND the next (finding #20).
            var monthStartUtc = ClinicClock.StartOfLocalDayUtc(month);
            var monthEndUtc = ClinicClock.EndOfLocalDayUtc(lastDayOfMonth).AddTicks(-1);

            var collected = await _invoiceRepository.GetCollectedBetweenAsync(
                clinicId, monthStartUtc, monthEndUtc, cancellationToken: cancellationToken);

            points.Add(new MonthlyCollectedPointDto
            {
                Month = $"{month.Year:D4}-{month.Month:D2}",
                Collected = InvoiceCalculator.RoundMoney(collected),
                /*
                 * A month is partial when the clinic's today has not reached its last day. Computed per month
                 * rather than assumed of the final point: read on the 1st, the last point holds a single day, and
                 * "the last one is partial" would be a rule that happens to be right rather than one that is.
                 */
                IsPartial = clinicToday < lastDayOfMonth
            });
        }

        return points;
    }
}
