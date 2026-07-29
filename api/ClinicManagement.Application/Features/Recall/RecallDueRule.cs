namespace ClinicManagement.Application.Features.Recall;

/// <summary>
/// When a patient becomes due for a relance — the one place the rule lives.
///
/// <para>Extracted when the dashboard grew a « patients à relancer » count: the rule is a widened SQL bound plus an
/// exact in-memory test (see below), and a second hand-written copy of that pair would have been two chances to get
/// the clamping wrong. The same reasoning as <c>AppointmentScheduling</c>'s overlap predicate, which had already
/// drifted across three copies before it was consolidated.</para>
/// </summary>
public static class RecallDueRule
{
    /// <summary>The interval used when a clinic has none configured.</summary>
    public const int DefaultIntervalMonths = 6;

    /// <summary>
    /// The largest number of days <c>AddMonths</c> can move a date by when it clamps to a shorter month
    /// (31 January + 1 month = 28 February).
    /// </summary>
    private const int MaxMonthEndClampDays = 3;

    /// <summary>
    /// A deliberately <b>conservative</b> upper bound on the recall anchor, safe to push into SQL (AC-P4.42).
    ///
    /// <para>The real rule is <c>anchor.AddMonths(interval) &lt;= now</c>. Inverting that to
    /// <c>anchor &lt;= now.AddMonths(-interval)</c> so it becomes a plain comparison is <b>not</b> equivalent, because
    /// <c>AddMonths</c>'s end-of-month clamp does not survive the inversion: 31 January + 1 month is 28 February, so
    /// on 28 February that patient IS due — but 28 February − 1 month is 28 January, and 31 January is not ≤ 28
    /// January, so the inverted form drops them. The clamp can move a date by at most three days, so the bound is
    /// widened by three days to guarantee a superset, and <see cref="IsDue"/> applies the exact test to what comes
    /// back.</para>
    /// </summary>
    public static DateTime AnchorUpperBound(DateTime nowUtc, int intervalMonths) =>
        nowUtc.AddMonths(-intervalMonths).AddDays(MaxMonthEndClampDays);

    /// <summary>
    /// The exact rule, applied to a candidate returned under <see cref="AnchorUpperBound"/>'s widened bound. The
    /// three-day margin means some candidates are not actually due yet; this is what filters them out.
    /// </summary>
    public static bool IsDue(DateTime recallAnchorUtc, int intervalMonths, DateTime nowUtc) =>
        DueDate(recallAnchorUtc, intervalMonths) <= nowUtc;

    /// <summary>When the relance falls due for a given anchor.</summary>
    public static DateTime DueDate(DateTime recallAnchorUtc, int intervalMonths) =>
        recallAnchorUtc.AddMonths(intervalMonths);
}
