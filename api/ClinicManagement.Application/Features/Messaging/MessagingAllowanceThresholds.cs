namespace ClinicManagement.Application.Features.Messaging;

/// <summary>
/// Which of FR-6's warning thresholds a cabinet's consumption has crossed — <b>80 %, 95 % and 100 %</b>, rounded
/// down. Pure and total: (consumed, allowance) in, the crossed thresholds out.
///
/// <para><b>⚠️ It returns EVERY threshold crossed, not the largest one, and that is deliberately the opposite of
/// <c>SubscriptionStateReader.ThresholdReached</c>.</b> The two quantities move differently. An end date advances one
/// day per day, so a daily pass never misses much and a cabinet that slept past several day-thresholds only needs
/// telling where it now stands. Consumption can cross all three between two sends in one afternoon — and the 80 % row
/// is the one that could still have been acted on, so collapsing to « 100 % » would announce the outcome and throw
/// away the warning (AC-3.1).</para>
///
/// <para><b>⚠️ A zero allowance yields the 100 % row alone.</b> Nothing is 80 % of zero, and a cabinet the vendor has
/// allowed no messages is exhausted from the first tick — one honest row rather than three restatements of it.</para>
///
/// <para>⚠️ <b>No clock and no month.</b> The wording is derived from the threshold, the allowance and the month by
/// <c>INotificationGenerator</c> (AC-3.5), never from the live count — so a threshold that holds for four days
/// restates nothing. This class only answers which lines have been passed.</para>
/// </summary>
public static class MessagingAllowanceThresholds
{
    /// <summary>The three thresholds, ascending. Percentages of the month's allowance (FR-6).</summary>
    public static readonly IReadOnlyList<int> All = new[] { 80, 95, 100 };

    /// <summary>
    /// Every threshold in <see cref="All"/> that <paramref name="consumed"/> has reached against
    /// <paramref name="allowance"/>, ascending.
    /// </summary>
    /// <param name="allowance">
    /// The month's allowance. A <b>negative</b> figure is treated as zero rather than refused: this runs post-commit
    /// behind a send that has already happened, and throwing there would cost the warning for a value no writer can
    /// produce anyway (<c>ClinicMessagingMonth</c> refuses one).
    /// </param>
    public static IReadOnlyList<int> Crossed(int consumed, int allowance)
    {
        if (allowance <= 0)
        {
            // Exhausted by construction. Reporting 80 and 95 as well would be three rows saying one thing.
            return new[] { 100 };
        }

        if (consumed <= 0)
        {
            return Array.Empty<int>();
        }

        // Integer arithmetic on purpose: `consumed * 100 / allowance` truncates, which is FR-6's « rounded down » —
        // and a floating-point percentage would put 159/200 at 79.49999… on some inputs, i.e. one send short of a
        // threshold it has actually reached.
        var percent = (int)(consumed * 100L / allowance);

        return All.Where(t => percent >= t).ToList();
    }
}
