namespace ClinicManagement.Application.Features.Platform.Dtos;

/// <summary>
/// One cabinet, opened (<c>platform-console</c> US-3).
///
/// <para>⚠️ <see cref="Clinic"/> is the <b>same</b> <see cref="PlatformClinicRowDto"/> the list renders, not a
/// second shape carrying the same numbers — AC-3.1 is « the same figures », and two records would drift into a
/// cabinet reading one way in the portfolio and another when opened, which is the hardest kind of discrepancy to
/// notice because both screens look right alone.</para>
///
/// <para>⚠️ <b>The payment history AC-3.2 asks for is not here, and that is stated rather than empty.</b> The
/// subscription ledger belongs to <c>features/clinic-subscription/</c> (FR-4), which has not shipped on this
/// branch — the same gap the state column has, reported through the same
/// <see cref="PlatformSubscriptionPlaceholder"/>. An empty « Historique des paiements » section would assert that
/// this cabinet has never paid, which is a different and false statement.</para>
/// </summary>
/// <param name="Trend">Six clinic-local months, oldest first (AC-3.1). Always six entries — a month the counter
/// pass never covered is present with <c>DaysMeasured = 0</c> rather than absent, so « pas encore mesuré » and
/// « rien fait » stay distinguishable at the month level exactly as they are at the cabinet level (EC-15).</param>
/// <param name="SubscriptionExplanation">Why the entitlement and the payment history are absent, in French and
/// server-side, so this screen and the portfolio cannot word the same gap differently.</param>
public record PlatformClinicDetailDto(
    PlatformClinicRowDto Clinic,
    string? AdminName,
    string? AdminEmail,
    bool AdminIsActive,
    IReadOnlyList<PlatformActivityMonthDto> Trend,
    bool SubscriptionDataAvailable,
    string? SubscriptionExplanation);

/// <summary>
/// One month of a cabinet's activity, summed from its <c>ClinicActivityDay</c> rows.
///
/// <para>⚠️ <see cref="DaysMeasured"/> is the denominator, and it is what stops the trend lying on a young
/// deployment. The counter pass writes a rolling 30-day window, so five of these six months hold no rows at all
/// until the product has been running six months — and a chart drawing those as zero would show every cabinet
/// collapsing from « active » to « dead » the further back you look.</para>
/// </summary>
/// <param name="MonthLabel">« août 2026 » — built server-side, in French, so the chart's axis, its accessible
/// text alternative and any later export cannot disagree about what a bucket is called.</param>
public record PlatformActivityMonthDto(
    int Year,
    int Month,
    string MonthLabel,
    int Writes,
    int Appointments,
    int PatientsCreated,
    int DaysMeasured);
