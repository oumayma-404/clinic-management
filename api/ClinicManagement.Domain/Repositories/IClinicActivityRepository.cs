using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// The vendor console's activity counters (<c>platform-console</c> Part 2): the daily history and the per-cabinet
/// snapshot the portfolio list JOINs.
///
/// <para>⚠️ <b>Every method here is cross-cabinet by nature</b> and is reached only from the counter job or the
/// console, both of which declare <c>UseSystemWide</c>. Neither table carries a query filter — see
/// <c>TenantScopeFilterTests.UnfilteredByDesign</c> for why that is a named decision rather than an omission.</para>
/// </summary>
public interface IClinicActivityRepository
{
    /// <summary>Every cabinet's snapshot, for the pass that rewrites them. Ordered by clinic id so a run is reproducible.</summary>
    Task<IReadOnlyList<ClinicActivitySnapshot>> GetAllSnapshotsAsync(CancellationToken cancellationToken = default);

    Task AddSnapshotAsync(ClinicActivitySnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>One cabinet's day row, or null where the pass has not written that day yet.</summary>
    Task<ClinicActivityDay?> GetDayAsync(Guid clinicId, DateOnly day, CancellationToken cancellationToken = default);

    /// <summary>
    /// One cabinet's day rows over an inclusive clinic-local day range, oldest first — the input to
    /// « jours actifs (30 j) » and to Part 3's six-month trend.
    /// </summary>
    Task<IReadOnlyList<ClinicActivityDay>> GetDaysAsync(
        Guid clinicId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    Task AddDayAsync(ClinicActivityDay day, CancellationToken cancellationToken = default);

    /// <summary>
    /// The portfolio, filtered, sorted and paged in **one** bounded query over the Clinic ⋈ snapshot JOIN
    /// (AC-2.4a, EC-11).
    ///
    /// <para>⚠️ Filtering and sorting happen <b>before</b> the page is cut — that is the whole reason the
    /// snapshot table exists. A figure folded after the page was selected would filter and sort a window rather
    /// than the portfolio, so « les cabinets dormants » would mean « les cabinets dormants de cette page ».</para>
    ///
    /// <para>⚠️ Ordered on a unique column last, or <c>OFFSET</c> over a non-unique sort can show one cabinet on
    /// two pages and skip another — which reads as a practice having disappeared.</para>
    /// </summary>
    Task<PagedResult<PlatformClinicRow>> GetPortfolioAsync(
        PlatformPortfolioFilter filter,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The portfolio-wide counts behind the summary strip (AC-2.7), read once rather than by paging the list.
    /// </summary>
    Task<PlatformPortfolioTotals> GetPortfolioTotalsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// One cabinet's row — the same figures the list shows, for the detail (Part 3, AC-3.1). Null where no such
    /// cabinet exists, which the detail renders as « ce cabinet n'existe plus » rather than as an error (EC-13).
    ///
    /// <para>⚠️ <b>It shares the list's projection rather than restating it.</b> AC-3.1 is « the same figures »,
    /// so a second expression here would be two answers to one question, and the drift would show as a cabinet
    /// reading one way in the portfolio and another when opened — the hardest kind of discrepancy to notice,
    /// because both screens look right on their own.</para>
    /// </summary>
    Task<PlatformClinicRow?> GetClinicRowAsync(Guid clinicId, CancellationToken cancellationToken = default);
}

/// <summary>How the portfolio list is narrowed. Every field is optional; an omitted one narrows nothing.</summary>
/// <param name="SearchPattern">A <c>%…%</c> LIKE pattern from <c>SearchTerm.ToLikePattern</c>, matched against the
/// cabinet's name, its city <b>and its administrators' e-mail addresses</b> (AC-2.5).</param>
/// <param name="DormantOnly">« Rien enregistré depuis 30 jours »: a cabinet that <b>has</b> been measured and
/// whose <c>Writes30d</c> is zero. ⚠️ A cabinet the pass has never covered is deliberately <b>not</b> matched —
/// « mesuré, et rien » and « jamais mesuré » are different statements, and folding the second into the first is
/// how an unrun pass makes the whole portfolio look like it is churning (EC-15).</param>
/// <param name="Sort">One of <see cref="PlatformPortfolioSort"/>.</param>
public record PlatformPortfolioFilter(
    string? SearchPattern = null,
    bool DormantOnly = false,
    PlatformPortfolioSort Sort = PlatformPortfolioSort.Name);

/// <summary>
/// The orders AC-2.4 asks for, minus the one that cannot exist yet.
///
/// <para>⚠️ <b>« By end date » is deliberately absent rather than stubbed.</b> It is a property of the
/// subscription, which <c>features/clinic-subscription/</c> owns and which has not shipped here — see
/// <c>PlatformSubscriptionPlaceholder</c>. An enum member that silently sorted by something else would be a
/// screen quietly answering a different question; the member arrives with the data behind it.</para>
/// </summary>
public enum PlatformPortfolioSort
{
    Name = 0,
    /// <summary>Busiest first, by saves over 30 days.</summary>
    Activity = 1,
    /// <summary>Newest cabinet first.</summary>
    CreatedAt = 2
}

/// <summary>
/// One row of the portfolio, already JOINed. A <b>closed set of scalars</b> — counts, dates and one total, and
/// nothing that could name a patient (AC-2.6); <c>PlatformReadShapeTests</c> is what holds that shut.
/// </summary>
/// <param name="CountersComputedAt">Null where the pass has never covered this cabinet. Distinct from « zéro »
/// on purpose (EC-15): « pas encore mesuré » and « rien fait » are different statements.</param>
public record PlatformClinicRow(
    Guid ClinicId,
    string Name,
    string? City,
    DateTime CreatedAt,
    int Users,
    int Patients,
    int Appointments30d,
    int Writes7d,
    int Writes30d,
    int ActiveDays30d,
    DateTime? LastWriteAt,
    DateTime? LastLoginAt,
    decimal CollectedThisMonth,
    DateTime? CountersComputedAt);

/// <summary>Portfolio-wide counts for the summary strip. Subscription states are not here — see
/// <c>PlatformSubscriptionPlaceholder</c>.</summary>
/// <param name="Dormant">Cabinets whose counters say nothing was saved in 30 days.</param>
/// <param name="NeverMeasured">Cabinets with no snapshot at all — what stops an unrun pass reading as a
/// portfolio of dormant practices (EC-15).</param>
public record PlatformPortfolioTotals(int Clinics, int Dormant, int NeverMeasured);
