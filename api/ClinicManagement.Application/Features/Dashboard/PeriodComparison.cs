using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Dashboard;

/// <summary>
/// One dashboard figure measured over the current period and the preceding equivalent one.
///
/// <para><b>Why this is a type and not two properties.</b> The alternative — <c>Collected</c> plus
/// <c>PreviousCollected</c> on a flat DTO — is the shape the event-sourced installment ledger was introduced to
/// replace: parallel scalars share no rounding rule, have no single representation of "there is no comparison here",
/// and push the delta arithmetic out to each caller. Eight figures × three consumers is eight chances to divide by
/// zero differently.</para>
///
/// <para>Point-in-time figures (créances, the à-traiter counts) deliberately do <b>not</b> use this type. A live
/// balance has no previous value, and giving it one would invite a meaningless delta.</para>
/// </summary>
/// <param name="Current">The figure over the current period. Null when it is undefined — see <see cref="Rate"/>.</param>
/// <param name="Previous">
/// The same figure over the preceding period, or null when there is nothing to compare against (a clinic in its
/// first month, or a rate whose denominator was zero).
/// </param>
/// <param name="DeltaPercent">
/// Signed percentage change from <see cref="Previous"/> to <see cref="Current"/>, rounded to one decimal, or null
/// when no meaningful percentage exists.
/// </param>
public sealed record PeriodComparison(decimal? Current, decimal? Previous, decimal? DeltaPercent)
{
    /// <summary>
    /// Builds a comparison for an <b>absolute</b> figure — a count or an amount, where zero is a real value.
    /// </summary>
    public static PeriodComparison Of(decimal current, decimal previous) =>
        new(current, previous, Delta(current, previous));

    /// <summary>
    /// Builds a comparison for a <b>rate</b>, where each side may be undefined because its denominator was zero.
    ///
    /// <para>A period with no appointments has <i>no</i> taux d'absence — reporting <c>0 %</c> would read as perfect
    /// attendance, which is a stronger and different claim than "nothing was scheduled". Both sides are therefore
    /// nullable, and a null on either side suppresses the delta.</para>
    /// </summary>
    public static PeriodComparison Rate(decimal? current, decimal? previous) =>
        new(
            current.HasValue ? InvoiceCalculator.RoundMoney(current.Value) : null,
            previous.HasValue ? InvoiceCalculator.RoundMoney(previous.Value) : null,
            current.HasValue && previous.HasValue ? Delta(current.Value, previous.Value) : null);

    /// <summary>
    /// Percentage change, or null when it cannot be expressed as one.
    ///
    /// <para>A zero baseline yields null rather than infinity or an arbitrary 100 %: going from no revenue to some
    /// revenue is not "+100 %", and the UI has a defined rendering for « — ». Note this is asymmetric on purpose —
    /// falling from a real figure to zero <i>is</i> −100 %, which is both expressible and worth seeing.</para>
    /// </summary>
    private static decimal? Delta(decimal current, decimal previous)
    {
        if (previous == 0m)
        {
            return null;
        }

        // Math.Abs on the baseline keeps the sign meaningful if a figure that can legitimately go negative (net) had
        // a negative previous period: a rise from −100 to −50 must read as an improvement, not a fall.
        return Math.Round((current - previous) / Math.Abs(previous) * 100m, 1, MidpointRounding.AwayFromZero);
    }
}
