using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

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
    /// <param name="clinicToday">
    /// The clinic-local day the entitlement counts are measured against. A parameter for
    /// <c>SubscriptionStateReader</c>'s reason: a repository that read the clock could not be asked about a midnight.
    /// </param>
    /// <param name="expiringWithinDays">The « expire bientôt » window, so the strip and the filter agree by construction.</param>
    Task<PlatformPortfolioTotals> GetPortfolioTotalsAsync(
        DateTime clinicToday, int expiringWithinDays, CancellationToken cancellationToken = default);

    /// <summary>
    /// One cabinet's row — the same figures the list shows, for the detail (Part 3, AC-3.1). Null where no such
    /// cabinet exists, which the detail renders as « ce cabinet n'existe plus » rather than as an error (EC-13).
    ///
    /// <para>⚠️ <b>It shares the list's projection rather than restating it.</b> AC-3.1 is « the same figures »,
    /// so a second expression here would be two answers to one question, and the drift would show as a cabinet
    /// reading one way in the portfolio and another when opened — the hardest kind of discrepancy to notice,
    /// because both screens look right on their own.</para>
    /// </summary>
    /// <param name="messagingMonthKey">
    /// The Tunisian month to read the cabinet's WhatsApp-forfait figures for (AC-8.1), or null to leave them unread —
    /// which the row then reports as « non mesuré », the honest answer for a caller that did not ask.
    /// </param>
    Task<PlatformClinicRow?> GetClinicRowAsync(
        Guid clinicId, string? messagingMonthKey = null, CancellationToken cancellationToken = default);
}

/// <summary>How the portfolio list is narrowed. Every field but the day is optional; an omitted one narrows nothing.</summary>
/// <param name="ClinicToday">
/// The clinic-local day the entitlement filters are measured against — never read from a clock in here, so the
/// repository can be asked about a midnight and the list, the strip and the detail all answer to one « today ».
/// </param>
/// <param name="SearchPattern">A <c>%…%</c> LIKE pattern from <c>SearchTerm.ToLikePattern</c>, matched against the
/// cabinet's name, its city <b>and its administrators' e-mail addresses</b> (AC-2.5).</param>
/// <param name="DormantOnly">« Rien enregistré depuis 30 jours »: a cabinet that <b>has</b> been measured and
/// whose <c>Writes30d</c> is zero. ⚠️ A cabinet the pass has never covered is deliberately <b>not</b> matched —
/// « mesuré, et rien » and « jamais mesuré » are different statements, and folding the second into the first is
/// how an unrun pass makes the whole portfolio look like it is churning (EC-15).</param>
/// <param name="ExpiringWithinDays">The window <see cref="PlatformSubscriptionFilter.ExpiringSoon"/> means.</param>
/// <param name="Sort">One of <see cref="PlatformPortfolioSort"/>.</param>
/// <param name="MessagingMonthKey">
/// The <c>AAAA-MM</c> Tunisian month the WhatsApp-forfait figures are read for
/// (<c>vendor-whatsapp-messaging-quota</c> AC-8.2). A <b>parameter</b> like <paramref name="ClinicToday"/> and for the
/// same reason twice over: month arithmetic belongs to <c>ClinicClock</c> (FR-8b) and this project references nothing,
/// so a repository deriving it would be the second copy that rule exists to prevent — and one that could not be asked
/// about the 23:59-on-the-31st boundary. Null leaves the forfait columns unread and the messaging filter inert.
/// </param>
/// <param name="Messaging">
/// AC-8.2's « épuisé ou presque ». ⚠️ Like <paramref name="DormantOnly"/>, a cabinet with <b>no counting row</b> is
/// deliberately matched by neither (AC-8.3): « non mesuré » is a bookkeeping finding of ours, not a practice near its
/// limit, and folding the two would send the vendor to top up a cabinet whose real problem is that nothing is counting.
/// </param>
public record PlatformPortfolioFilter(
    DateTime ClinicToday,
    string? SearchPattern = null,
    bool DormantOnly = false,
    PlatformSubscriptionFilter? Subscription = null,
    int ExpiringWithinDays = PlatformPortfolioFilter.DefaultExpiringWithinDays,
    PlatformPortfolioSort Sort = PlatformPortfolioSort.Name,
    string? MessagingMonthKey = null,
    PlatformMessagingFilter? Messaging = null)
{
    /// <summary>AC-2.7's « expire sous 14 jours », shared by the filter and the summary strip so they cannot drift.</summary>
    public const int DefaultExpiringWithinDays = 14;

    /// <summary>
    /// AC-8.2's « à moins de 10 % » expressed the way the data is stored: a cabinet is <b>near exhausted</b> once it has
    /// consumed this percentage of its forfait.
    ///
    /// <para><b>⚠️ One named constant, read by the SQL predicate and served to the console.</b> Two spellings of a
    /// threshold is how a filter and its own chip label come to disagree — and neither screen looks wrong on its own, so
    /// the vendor simply learns not to trust the number. The console does not retype 90 or 10: the portfolio read carries
    /// this figure back and the chip's label is built from it.</para>
    ///
    /// <para>⚠️ It is a percentage rather than a fraction so the predicate can stay in <b>integer</b> arithmetic
    /// (<c>consumed × 100 ≥ allowance × 90</c>). A <c>0.90</c> multiplication in SQL is a floating-point comparison on a
    /// boundary a vendor reads as exact, and « 450 sur 500 » would sometimes be in the list and sometimes not.</para>
    /// </summary>
    public const int MessagingNearExhaustedPercent = 90;
}

/// <summary>
/// AC-8.2's WhatsApp-forfait filters, as SQL predicates over the counting row the portfolio already JOINs.
///
/// <para>⚠️ <b><see cref="NearExhausted"/> is a superset of <see cref="Exhausted"/></b>, deliberately: the vendor's
/// question is « qui faut-il recharger ? », and a cabinet that has already run out is the most urgent answer to it. The
/// two are still separate filters because « déjà épuisé » is a practice whose reminders are being held <i>right now</i>,
/// which is a different conversation from « bientôt ».</para>
///
/// <para>⚠️ <b>Both require a counting row</b> (AC-8.3). A cabinet with none matches neither — see
/// <c>PlatformPortfolioFilter.Messaging</c>.</para>
/// </summary>
public enum PlatformMessagingFilter
{
    /// <summary>Nothing left: consumption has met or passed the forfait, so reminders are held.</summary>
    Exhausted = 0,

    /// <summary>
    /// Consumed at least <c>PlatformPortfolioFilter.MessagingNearExhaustedPercent</c> of the forfait — including the
    /// cabinets that have run out entirely.
    /// </summary>
    NearExhausted = 1
}

/// <summary>
/// AC-2.3's entitlement filters, expressed so each is a <b>SQL predicate over one JOINed row</b> — which is what
/// AC-2.4a demands: every figure the portfolio filters on must exist for every cabinet before a page is cut, and
/// folding N cabinets' ledgers to answer « en essai » is exactly the unbounded read EC-11 forbids.
///
/// <para>⚠️ <b><see cref="Trial"/> reads <c>ClinicSubscription.LatestCoverKind</c>, not a fold.</b> That column is
/// a clock-free denormalisation written by the same <c>RecomputeFrom</c> that writes <c>EndsOn</c>, and the filter
/// ANDs it with the state terms below — so a cabinet whose trial has lapsed is excluded by « expiré » regardless of
/// its kind. See that property's own remarks for why the tempting « what covers today » column is unstorable.</para>
/// </summary>
public enum PlatformSubscriptionFilter
{
    /// <summary>Covered, not suspended, and the last surviving entry is the free trial.</summary>
    Trial = 0,
    /// <summary>Covered and not suspended, whatever paid for it.</summary>
    Active = 1,
    /// <summary>Covered today, and its last day is within <c>ExpiringWithinDays</c>.</summary>
    ExpiringSoon = 2,
    /// <summary>Past its last day, and not suspended — those are different causes with different remedies (EC-11).</summary>
    Expired = 3,
    /// <summary>Stopped by the vendor. Outranks an expiry, exactly as the state rule does.</summary>
    Suspended = 4,
    /// <summary>No entitlement row at all — FR-13's failure state, and a state nobody chose.</summary>
    Missing = 5
}

/// <summary>The orders AC-2.4 asks for.</summary>
public enum PlatformPortfolioSort
{
    Name = 0,
    /// <summary>Busiest first, by saves over 30 days.</summary>
    Activity = 1,
    /// <summary>Newest cabinet first.</summary>
    CreatedAt = 2,
    /// <summary>
    /// Soonest to end first — « qui faut-il relancer ? ». ⚠️ A cabinet with no end date (« sans échéance ») and one
    /// with no entitlement at all sort <b>last</b>: neither is a deadline, and PostgreSQL's default of NULLS LAST on
    /// an ascending sort is the answer this read wants rather than one it inherits by accident.
    /// </summary>
    EndsOn = 3
}

/// <summary>
/// One row of the portfolio, already JOINed. A <b>closed set of scalars</b> — counts, dates and one total, and
/// nothing that could name a patient (AC-2.6); <c>PlatformReadShapeTests</c> is what holds that shut.
/// </summary>
/// <param name="CountersComputedAt">Null where the pass has never covered this cabinet. Distinct from « zéro »
/// on purpose (EC-15): « pas encore mesuré » and « rien fait » are different statements.</param>
/// <param name="HasEntitlement">
/// False where the cabinet has <b>no</b> <c>ClinicSubscription</c> row — FR-13's failure state, which the console
/// must be able to say out loud. ⚠️ Not the same as « sans échéance »: a grandfathered cabinet has an entitlement
/// whose <see cref="SubscriptionEndsOn"/> is null, and reading the two as one would report a deliberate arrangement
/// as a fault and a fault as a deliberate arrangement.
/// </param>
/// <param name="LatestCoverKind">
/// The clock-free denormalisation « en essai » filters on. See <c>ClinicSubscription.LatestCoverKind</c>.
/// </param>
/// <param name="HasMessagingMonth">
/// Whether a WhatsApp-forfait counting row exists for the month asked about
/// (<c>vendor-whatsapp-messaging-quota</c> AC-8.3). ⚠️ <b>False is « non mesuré », never « zéro »</b> — one row exists
/// per cabinet per Tunisian month (FR-1a), so its absence is a fault on our side and a claim about us, while « 0 rappel
/// envoyé » is a claim about the practice. It is a separate flag rather than a null in the two figures below so no screen
/// can get the distinction wrong by forgetting one of them.
/// </param>
public record PlatformClinicRow(
    Guid ClinicId,
    string Name,
    string? City,
    DateTime CreatedAt,
    bool HasEntitlement,
    SubscriptionPlan? Plan,
    DateTime? SubscriptionEndsOn,
    bool SubscriptionIsSuspended,
    SubscriptionPeriodKind? LatestCoverKind,
    int Users,
    int Patients,
    int Appointments30d,
    int Writes7d,
    int Writes30d,
    int ActiveDays30d,
    DateTime? LastWriteAt,
    DateTime? LastLoginAt,
    decimal CollectedThisMonth,
    DateTime? CountersComputedAt,
    bool HasMessagingMonth = false,
    int MessagingAllowance = 0,
    int MessagingConsumed = 0);

/// <summary>
/// Portfolio-wide counts for the summary strip (AC-2.7).
///
/// <para>⚠️ The five entitlement counts are <b>mutually exclusive and exhaustive</b>: every cabinet is counted in
/// exactly one of <see cref="InTrial"/>, <see cref="Active"/>, <see cref="Expired"/>, <see cref="Suspended"/> and
/// <see cref="NoEntitlement"/>, so they sum to <see cref="Clinics"/>. <see cref="ExpiringWithin14Days"/> is
/// deliberately <b>not</b> one of them — it is a subset of the covered cabinets, which is the whole point of
/// showing it, and it is labelled on screen so it cannot be read as a sixth bucket.</para>
/// </summary>
/// <param name="Dormant">Cabinets whose counters say nothing was saved in 30 days.</param>
/// <param name="NeverMeasured">Cabinets with no snapshot at all — what stops an unrun pass reading as a
/// portfolio of dormant practices (EC-15).</param>
/// <param name="NoEntitlement">FR-13's failure state. Counted so the five buckets add up to the portfolio; a
/// cabinet missing from every figure is how such a row would stay invisible.</param>
public record PlatformPortfolioTotals(
    int Clinics,
    int Dormant,
    int NeverMeasured,
    int InTrial,
    int Active,
    int ExpiringWithin14Days,
    int Expired,
    int Suspended,
    int NoEntitlement);
