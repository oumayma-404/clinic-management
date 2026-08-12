using System.Linq.Expressions;
using ClinicManagement.Application.Common;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

/// <summary>
/// The vendor console's counter tables, and the one bounded JOIN the portfolio list is (see
/// <see cref="IClinicActivityRepository"/> for why the snapshot exists at all).
/// </summary>
public class ClinicActivityRepository : IClinicActivityRepository
{
    private readonly ApplicationDbContext _context;

    public ClinicActivityRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ClinicActivitySnapshot>> GetAllSnapshotsAsync(
        CancellationToken cancellationToken = default) =>
        await _context.ClinicActivitySnapshots
            .OrderBy(s => s.ClinicId)
            .ToListAsync(cancellationToken);

    public async Task AddSnapshotAsync(ClinicActivitySnapshot snapshot, CancellationToken cancellationToken = default) =>
        await _context.ClinicActivitySnapshots.AddAsync(snapshot, cancellationToken);

    public async Task<ClinicActivityDay?> GetDayAsync(
        Guid clinicId, DateOnly day, CancellationToken cancellationToken = default) =>
        await _context.ClinicActivityDays
            .FirstOrDefaultAsync(d => d.ClinicId == clinicId && d.Day == day, cancellationToken);

    public async Task<IReadOnlyList<ClinicActivityDay>> GetDaysAsync(
        Guid clinicId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        await _context.ClinicActivityDays
            .AsNoTracking()
            .Where(d => d.ClinicId == clinicId && d.Day >= from && d.Day <= to)
            .OrderBy(d => d.Day)
            .ToListAsync(cancellationToken);

    public async Task AddDayAsync(ClinicActivityDay day, CancellationToken cancellationToken = default) =>
        await _context.ClinicActivityDays.AddAsync(day, cancellationToken);

    public async Task<PagedResult<PlatformClinicRow>> GetPortfolioAsync(
        PlatformPortfolioFilter filter,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default)
    {
        // A LEFT JOIN, not an inner one: a cabinet the pass has never covered must still appear — with its
        // counters stated as unknown rather than as zero (EC-8/EC-15). An inner join would hide exactly the
        // cabinets whose absence from the counters is the thing worth seeing.
        var query = PortfolioQuery(_context.Clinics.AsNoTracking(), filter.MessagingMonthKey);

        if (filter.SearchPattern is { } pattern)
        {
            // Name, city and the administrators' addresses (AC-2.5), all matched in SQL through `unaccent` so
            // « Béchir » finds « bechir ». The admin side is an EXISTS rather than a join: a cabinet with two
            // admins must appear once, and joining would duplicate its row and corrupt the page boundaries.
            query = query.Where(x =>
                EF.Functions.ILike(SqlSearch.Unaccent(x.clinic.Name)!, pattern, SqlSearch.EscapeString)
                || (x.clinic.City != null
                    && EF.Functions.ILike(SqlSearch.Unaccent(x.clinic.City)!, pattern, SqlSearch.EscapeString))
                || _context.Users.Any(u =>
                    u.ClinicId == x.clinic.Id
                    && u.Role == User.RoleAdmin
                    && u.Email != null
                    && EF.Functions.ILike(SqlSearch.Unaccent(u.Email)!, pattern, SqlSearch.EscapeString)));
        }

        if (filter.DormantOnly)
        {
            // Measured AND idle. A cabinet with no snapshot is deliberately excluded — see the filter's own
            // remarks: « jamais mesuré » is not a claim that nothing happened.
            query = query.Where(x => x.snapshot != null && x.snapshot.Writes30d == 0);
        }

        if (filter.Subscription is { } state)
        {
            query = query.Where(SubscriptionPredicate(state, filter.ClinicToday.Date, filter.ExpiringWithinDays));
        }

        if (filter.Messaging is { } messaging && filter.MessagingMonthKey is not null)
        {
            query = query.Where(MessagingPredicate(messaging));
        }

        // ⚠️ `.ThenBy(Id)` on every branch, never decoratively: OFFSET over a non-unique sort can show one
        // cabinet on two pages and skip another, which reads as a practice having disappeared from the portfolio.
        var ordered = filter.Sort switch
        {
            PlatformPortfolioSort.Activity => query
                .OrderByDescending(x => x.snapshot != null ? x.snapshot.Writes30d : 0)
                .ThenBy(x => x.clinic.Id),
            PlatformPortfolioSort.CreatedAt => query
                .OrderByDescending(x => x.clinic.CreatedAt)
                .ThenBy(x => x.clinic.Id),
            // Ascending, so the soonest deadline is first. « Sans échéance » and « aucun abonnement » are both null
            // here and land at the end under PostgreSQL's NULLS LAST — which is what this read wants, since neither
            // is a deadline; it is asserted rather than inherited by `PlatformPortfolioQueryTests`.
            PlatformPortfolioSort.EndsOn => query
                .OrderBy(x => x.subscription != null ? x.subscription.EndsOn : null)
                .ThenBy(x => x.clinic.Id),
            _ => query
                .OrderBy(x => x.clinic.Name)
                .ThenBy(x => x.clinic.Id)
        };

        return await ordered.Select(Projection).ToPagedResultAsync(paging, cancellationToken);
    }

    /// <summary>
    /// One JOINed row, three tables. The entitlement is a <b>LEFT</b> join for the reason the snapshot is: a cabinet
    /// with none must still appear — that is FR-13's failure state, and an inner join would hide precisely the rows
    /// worth seeing.
    /// </summary>
    /// <param name="messagingMonthKey">
    /// The Tunisian month the WhatsApp-forfait row is joined on (AC-8.2), or null to leave those columns unread. A
    /// **third** LEFT join and for the same reason as the other two: a cabinet with no counting row must still appear,
    /// with « non mesuré » stated rather than zeros implied (AC-8.3). Null yields no row for every cabinet, which the
    /// projection reads as unmeasured — the honest answer for a caller that did not ask.
    /// </param>
    private IQueryable<PortfolioJoin> PortfolioQuery(IQueryable<Clinic> clinics, string? messagingMonthKey) =>
        from clinic in clinics
        join snapshotRow in _context.ClinicActivitySnapshots.AsNoTracking()
            on clinic.Id equals snapshotRow.ClinicId into snapshots
        from snapshot in snapshots.DefaultIfEmpty()
        join subscriptionRow in _context.ClinicSubscriptions.AsNoTracking()
            on clinic.Id equals subscriptionRow.ClinicId into subscriptions
        from subscription in subscriptions.DefaultIfEmpty()
        // Keyed on the composite (cabinet, month) so exactly one row can match — the table's own unique index. Joining
        // on the cabinet alone would return every month it has ever had and multiply the portfolio's rows, which
        // corrupts every page boundary after the first.
        join messagingRow in _context.ClinicMessagingMonths.AsNoTracking()
            on new { ClinicId = clinic.Id, MonthKey = messagingMonthKey ?? string.Empty }
            equals new { messagingRow.ClinicId, messagingRow.MonthKey } into messagingMonths
        from messagingMonth in messagingMonths.DefaultIfEmpty()
        select new PortfolioJoin
        {
            clinic = clinic,
            snapshot = snapshot,
            subscription = subscription,
            messagingMonth = messagingMonth
        };

    /// <summary>
    /// AC-8.2's forfait filters, in <b>integer</b> arithmetic over the counting row.
    ///
    /// <para>⚠️ <c>consumed × 100 ≥ allowance × 90</c> rather than <c>consumed ≥ 0.90 × allowance</c>: the threshold is
    /// a boundary the vendor reads as exact, and a floating-point comparison would put « 450 sur 500 » in the list on
    /// some rows and not on others. The 90 comes from <see cref="PlatformPortfolioFilter.MessagingNearExhaustedPercent"/>,
    /// which the portfolio read also serves to the console — so the predicate and the chip's label are one figure.</para>
    ///
    /// <para>⚠️ <b>Both terms require the row to exist</b> (AC-8.3), which the null check carries: an unmeasured cabinet
    /// is a bookkeeping finding of ours, not a practice near its limit.</para>
    ///
    /// <para>⚠️ An allowance of <b>zero</b> matches both, and correctly: a cabinet the vendor decided sends no WhatsApp
    /// reminders is exhausted from the first tick — the counting row's own <c>IsExhausted</c> says the same thing.</para>
    /// </summary>
    private static Expression<Func<PortfolioJoin, bool>> MessagingPredicate(PlatformMessagingFilter messaging) =>
        messaging switch
        {
            PlatformMessagingFilter.Exhausted => x =>
                x.messagingMonth != null
                && x.messagingMonth.ConsumedMessages >= x.messagingMonth.AllowanceMessages,
            _ => x =>
                x.messagingMonth != null
                && x.messagingMonth.ConsumedMessages * 100
                    >= x.messagingMonth.AllowanceMessages * PlatformPortfolioFilter.MessagingNearExhaustedPercent
        };

    /// <summary>
    /// AC-2.3's filters as SQL, and the <b>same</b> branches the summary strip counts with — so a chip saying
    /// « 4 expirés » and the list it opens cannot disagree.
    ///
    /// <para>⚠️ Each branch reproduces <c>SubscriptionStateReader</c>'s precedence rather than inventing one:
    /// suspension outranks an expiry (EC-11), a null <c>EndsOn</c> is « sans échéance » and therefore covered, and
    /// « en essai » is a <i>label</i> on a covered cabinet — it ANDs with the covered terms, which is what makes the
    /// clock-free <c>LatestCoverKind</c> a correct answer here (see that property's remarks).</para>
    /// </summary>
    private static Expression<Func<PortfolioJoin, bool>> SubscriptionPredicate(
        PlatformSubscriptionFilter state, DateTime today, int expiringWithinDays)
    {
        var horizon = today.AddDays(expiringWithinDays);

        return state switch
        {
            PlatformSubscriptionFilter.Missing => x => x.subscription == null,
            PlatformSubscriptionFilter.Suspended => x => x.subscription != null && x.subscription.IsSuspended,
            PlatformSubscriptionFilter.Expired => x =>
                x.subscription != null
                && !x.subscription.IsSuspended
                && x.subscription.EndsOn != null
                && x.subscription.EndsOn < today,
            PlatformSubscriptionFilter.ExpiringSoon => x =>
                x.subscription != null
                && !x.subscription.IsSuspended
                && x.subscription.EndsOn != null
                && x.subscription.EndsOn >= today
                && x.subscription.EndsOn <= horizon,
            PlatformSubscriptionFilter.Trial => x =>
                x.subscription != null
                && !x.subscription.IsSuspended
                && (x.subscription.EndsOn == null || x.subscription.EndsOn >= today)
                && x.subscription.LatestCoverKind == SubscriptionPeriodKind.Trial,
            _ => x =>
                x.subscription != null
                && !x.subscription.IsSuspended
                && (x.subscription.EndsOn == null || x.subscription.EndsOn >= today)
        };
    }

    public async Task<PlatformClinicRow?> GetClinicRowAsync(
        Guid clinicId, string? messagingMonthKey = null, CancellationToken cancellationToken = default)
    {
        // The same LEFT JOINs and the same projection as the list — AC-3.1 is « the same figures », so the
        // expression is shared rather than retyped.
        var query = PortfolioQuery(_context.Clinics.AsNoTracking().Where(c => c.Id == clinicId), messagingMonthKey);

        return await query.Select(Projection).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// A cabinet beside its snapshot and its entitlement, or beside nothing. A named type rather than an anonymous
    /// one so the list and the detail can pass the <b>same</b> projection expression over it.
    /// </summary>
    private sealed class PortfolioJoin
    {
        public required Clinic clinic { get; init; }
        public ClinicActivitySnapshot? snapshot { get; init; }
        public ClinicSubscription? subscription { get; init; }

        /// <summary>The cabinet's WhatsApp-forfait counting row for the month asked about, or null for « non mesuré ».</summary>
        public ClinicMessagingMonth? messagingMonth { get; init; }
    }

    /// <summary>
    /// The one place a cabinet becomes a portfolio row.
    ///
    /// <para>⚠️ Every activity figure is read off the snapshot or defaulted, and <c>CountersComputedAt</c> stays
    /// <b>null</b> when there is no snapshot — that null is the only thing that can tell the screen the zeros
    /// above it mean « pas encore mesuré » rather than « rien fait » (EC-15).</para>
    /// </summary>
    private static readonly Expression<Func<PortfolioJoin, PlatformClinicRow>> Projection = x =>
        new PlatformClinicRow(
            x.clinic.Id,
            x.clinic.Name,
            x.clinic.City,
            x.clinic.CreatedAt,
            x.subscription != null,
            x.subscription != null ? x.subscription.Plan : null,
            x.subscription != null ? x.subscription.EndsOn : null,
            x.subscription != null && x.subscription.IsSuspended,
            x.subscription != null ? x.subscription.LatestCoverKind : null,
            x.snapshot != null ? x.snapshot.Users : 0,
            x.snapshot != null ? x.snapshot.Patients : 0,
            x.snapshot != null ? x.snapshot.Appointments30d : 0,
            x.snapshot != null ? x.snapshot.Writes7d : 0,
            x.snapshot != null ? x.snapshot.Writes30d : 0,
            x.snapshot != null ? x.snapshot.ActiveDays30d : 0,
            x.snapshot != null ? x.snapshot.LastWriteAt : null,
            x.snapshot != null ? x.snapshot.LastLoginAt : null,
            x.snapshot != null ? x.snapshot.CollectedThisMonth : 0m,
            x.snapshot != null ? x.snapshot.ComputedAt : (DateTime?)null,
            // ⚠️ The flag carries « non mesuré », and the two figures below are meaningless without it — they read 0
            // for an absent row exactly as they would for a quiet month, which are opposite facts (AC-8.3). Nothing
            // downstream may look at them without looking at this first.
            x.messagingMonth != null,
            x.messagingMonth != null ? x.messagingMonth.AllowanceMessages : 0,
            x.messagingMonth != null ? x.messagingMonth.ConsumedMessages : 0);

    public async Task<PlatformPortfolioTotals> GetPortfolioTotalsAsync(
        DateTime clinicToday, int expiringWithinDays, CancellationToken cancellationToken = default)
    {
        var clinics = await _context.Clinics.AsNoTracking().CountAsync(cancellationToken);

        var dormant = await _context.ClinicActivitySnapshots
            .AsNoTracking()
            .CountAsync(s => s.Writes30d == 0, cancellationToken);

        var measured = await _context.ClinicActivitySnapshots.AsNoTracking().CountAsync(cancellationToken);

        // The five entitlement figures are counted through the SAME predicates the list filters with, so a chip
        // saying « 4 expirés » and the page it opens cannot disagree. One JOINed query rather than five scalar
        // reads, so all five describe one instant.
        // No messaging month asked for: the strip counts entitlement states only, and joining a forfait row onto every
        // cabinet for six counts that never look at it is work with no reader.
        var joined = PortfolioQuery(_context.Clinics.AsNoTracking(), messagingMonthKey: null);
        var today = clinicToday.Date;

        var inTrial = await joined.CountAsync(
            SubscriptionPredicate(PlatformSubscriptionFilter.Trial, today, expiringWithinDays), cancellationToken);
        var active = await joined.CountAsync(
            SubscriptionPredicate(PlatformSubscriptionFilter.Active, today, expiringWithinDays), cancellationToken);
        var expiringSoon = await joined.CountAsync(
            SubscriptionPredicate(PlatformSubscriptionFilter.ExpiringSoon, today, expiringWithinDays), cancellationToken);
        var expired = await joined.CountAsync(
            SubscriptionPredicate(PlatformSubscriptionFilter.Expired, today, expiringWithinDays), cancellationToken);
        var suspended = await joined.CountAsync(
            SubscriptionPredicate(PlatformSubscriptionFilter.Suspended, today, expiringWithinDays), cancellationToken);
        var missing = await joined.CountAsync(
            SubscriptionPredicate(PlatformSubscriptionFilter.Missing, today, expiringWithinDays), cancellationToken);

        // ⚠️ « En essai » is a subset of « Actif » in SQL — both branches require a covered, unsuspended cabinet —
        // so the strip subtracts it out here. Without that the five buckets would over-count every trialling cabinet
        // and stop summing to the portfolio, which is the one property that makes the strip readable.
        return new PlatformPortfolioTotals(
            clinics, dormant, clinics - measured,
            inTrial, active - inTrial, expiringSoon, expired, suspended, missing);
    }
}
