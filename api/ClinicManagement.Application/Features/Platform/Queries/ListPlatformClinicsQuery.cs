using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Platform.Queries;

/// <summary>
/// The vendor's portfolio: every cabinet with its real activity beside it, filtered, sorted and paged
/// (<c>platform-console</c> US-2).
///
/// <para>⚠️ <b>Filter, sort and page all happen in one bounded query over the counter snapshot</b> (AC-2.4a,
/// EC-11) — never over rows this handler has already fetched. « Les cabinets dormants » must mean the
/// portfolio's dormant cabinets, not the dormant ones on the page the user happens to be looking at, and the
/// read must stay bounded by the number of cabinets rather than by the busiest practice's whole history.</para>
///
/// <para>⚠️ <b>The free-text term is normalised here and matched in SQL</b> (<c>SearchTerm</c> +
/// <c>SqlSearch</c>): folding accents in C# over the page would answer about the page, and a cabinet on page 3
/// would read as « aucun résultat ».</para>
/// </summary>
public class ListPlatformClinicsQuery : IRequest<Result<PlatformClinicPageDto>>
{
    /// <summary>« rien enregistré depuis 30 jours » (AC-2.3).</summary>
    public bool Dormant { get; set; }

    /// <summary>
    /// <c>trial</c> | <c>active</c> | <c>expiringSoon</c> | <c>expired</c> | <c>suspended</c> | <c>missing</c>
    /// (AC-2.3). An unrecognised value narrows <b>nothing</b> rather than refusing — the tolerance the lab-order
    /// stage filter and the audit action filter apply, so a stale bookmark shows the portfolio instead of an error.
    /// </summary>
    public string? State { get; set; }

    /// <summary>Matches the cabinet's name, its city, or an administrator's e-mail address (AC-2.5).</summary>
    public string? Q { get; set; }

    /// <summary>
    /// <c>name</c> | <c>activity</c> | <c>createdAt</c> | <c>endsOn</c>. An unrecognised value falls back to
    /// <c>createdAt</c> rather than refusing — the same tolerance the lab-order stage filter and the audit action
    /// filter apply, so a stale bookmark shows rows instead of a French error.
    ///
    /// <para>⚠️ <b>The fallback is the newest cabinet first, and the console's own default matches it</b> — which is
    /// what keeps the screen's URL clean, since a default it agreed with the server about needs no query parameter.
    /// Moving one of the two alone would make « Création » look active while the list arrived alphabetically.</para>
    /// </summary>
    public string? Sort { get; set; }

    /// <summary>
    /// <c>exhausted</c> | <c>near</c> — AC-8.2's WhatsApp-forfait narrowing, over the <b>stored counting row</b> for the
    /// current Tunisian month so it applies to the portfolio rather than to the page (AC-2.4a's rule, one feature over).
    ///
    /// <para>⚠️ An unrecognised value narrows <b>nothing</b>, the same tolerance every other filter here applies. And a
    /// cabinet with no counting row matches neither term (AC-8.3): « non mesuré » is a bookkeeping finding of ours, not
    /// a practice near its limit.</para>
    /// </summary>
    public string? Messaging { get; set; }

    public int? Page { get; set; }

    public int? PageSize { get; set; }
}

public class ListPlatformClinicsQueryHandler
    : IRequestHandler<ListPlatformClinicsQuery, Result<PlatformClinicPageDto>>
{
    private readonly IClinicActivityRepository _activityRepository;
    private readonly IUserRepository _userRepository;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<ListPlatformClinicsQueryHandler> _logger;

    public ListPlatformClinicsQueryHandler(
        IClinicActivityRepository activityRepository,
        IUserRepository userRepository,
        ITenantScope tenantScope,
        ILogger<ListPlatformClinicsQueryHandler> logger)
    {
        _activityRepository = activityRepository;
        _userRepository = userRepository;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    public async Task<Result<PlatformClinicPageDto>> Handle(
        ListPlatformClinicsQuery request, CancellationToken cancellationToken)
    {
        // EC-12: an undeclared scope reads zero rows with no error, which is indistinguishable from a
        // deployment with no cabinets. This throws instead, so « je n'ai pas pu lire » reaches the screen.
        PlatformTenantScope.EnsureDeclared(_tenantScope);

        try
        {
            // One « today », resolved once and handed to the repository: the filters, the sort and the countdown
            // every row carries must all be measured against the same clinic-local day, and a repository that read
            // the clock itself could not be asked about a midnight.
            var today = ClinicClock.ClinicToday();

            // Derived from the same « today » as the entitlement filters, and passed in rather than left to the
            // repository: month arithmetic belongs to ClinicClock (FR-8b), and one clock read per request is what keeps
            // the page's own month label agreeing with the rows it labels across a 23:59 boundary.
            var messagingMonth = ClinicClock.MonthKey(today);

            var filter = new PlatformPortfolioFilter(
                ClinicToday: today,
                SearchPattern: SearchTerm.ToLikePattern(request.Q),
                DormantOnly: request.Dormant,
                Subscription: ParseState(request.State),
                Sort: ParseSort(request.Sort),
                MessagingMonthKey: messagingMonth,
                Messaging: ParseMessaging(request.Messaging));

            // Omitting the paging parameters gets the FIRST PAGE, not everything — the opposite of the clinic
            // app's list reads, and for the audit ledger's reason: there is no legitimate caller for « every
            // cabinet at once », and an unpaged default is a hosted deployment's whole portfolio in one response.
            var paging = PageRequest.From(request.Page, request.PageSize) ?? PageRequest.Of(1, PageRequest.DefaultPageSize);

            var page = await _activityRepository.GetPortfolioAsync(filter, paging, cancellationToken);

            // Bounded by the page, and one read for all of it: a contact per row would be 25 queries on the screen
            // the vendor opens first. It is not part of the portfolio JOIN because « which admin is the contact? » is
            // a precedence rule owned by IUserRepository, and expressing it here too is how the list and the fiche
            // come to name two different people.
            var admins = await _userRepository.GetPrimaryAdminContactsAsync(
                page.Items.Select(row => row.ClinicId), cancellationToken);

            var items = page.Items
                .Select(row => PlatformClinicRowMapper.ToDto(
                    row, today, admins.TryGetValue(row.ClinicId, out var admin) ? admin.Email : null))
                .ToList();

            return Result<PlatformClinicPageDto>.Success(new PlatformClinicPageDto(
                Items: items,
                Page: page.Page,
                PageSize: page.PageSize,
                TotalCount: page.TotalCount,
                TotalPages: page.TotalPages,
                HasPreviousPage: page.HasPreviousPage,
                HasNextPage: page.HasNextPage,
                CountersAsOf: OldestMeasurement(items),
                MessagingMonth: messagingMonth,
                MessagingMonthLabel: ClinicClock.MonthLabelFr(messagingMonth),
                MessagingNearThresholdPercent: PlatformPortfolioFilter.MessagingNearExhaustedPercent));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error reading the platform portfolio");
            return Result<PlatformClinicPageDto>.Failure("Erreur lors de la lecture du portefeuille des cabinets.");
        }
    }

    /// <summary>
    /// The <b>oldest</b> measurement on the page, so the freshness on screen is a floor (AC-2.8). Taking the
    /// newest would let one cabinet measured this morning vouch for thirty measured last week.
    ///
    /// <para>Null where no cabinet on the page has ever been measured — which the screen states rather than
    /// leaving a portfolio whose pass has never run to read as one full of dormant practices (EC-15).</para>
    /// </summary>
    private static DateTime? OldestMeasurement(IReadOnlyList<PlatformClinicRowDto> items)
    {
        var measured = items
            .Where(i => i.CountersComputedAt.HasValue)
            .Select(i => i.CountersComputedAt!.Value)
            .ToList();

        return measured.Count == 0 ? null : measured.Min();
    }

    /// <summary>
    /// An unrecognised value is the <b>newest cabinet first</b>, not the alphabet: the portfolio is opened to see who
    /// has just arrived and who has stopped, and a name is only useful to somebody who already knows which name.
    /// </summary>
    private static PlatformPortfolioSort ParseSort(string? sort) => sort?.Trim().ToLowerInvariant() switch
    {
        "name" => PlatformPortfolioSort.Name,
        "activity" => PlatformPortfolioSort.Activity,
        "endson" => PlatformPortfolioSort.EndsOn,
        _ => PlatformPortfolioSort.CreatedAt
    };

    /// <summary>
    /// An unrecognised value narrows nothing — deliberately not « matches nothing ». A stale bookmark should show
    /// the portfolio, and a filter silently matching zero cabinets reads as a deployment that has lost its clients.
    /// </summary>
    /// <summary>
    /// AC-8.2's two forfait terms. An unrecognised value narrows nothing, for <see cref="ParseState"/>'s reason.
    ///
    /// <para>« near » rather than « nearexhausted » on the wire: the console builds its own label from the served
    /// threshold percentage, so the query value never has to spell the figure out and cannot come to disagree with it.</para>
    /// </summary>
    private static PlatformMessagingFilter? ParseMessaging(string? messaging) =>
        messaging?.Trim().ToLowerInvariant() switch
        {
            "exhausted" => PlatformMessagingFilter.Exhausted,
            "near" => PlatformMessagingFilter.NearExhausted,
            _ => null
        };

    private static PlatformSubscriptionFilter? ParseState(string? state) => state?.Trim().ToLowerInvariant() switch
    {
        "trial" => PlatformSubscriptionFilter.Trial,
        "active" => PlatformSubscriptionFilter.Active,
        "expiringsoon" => PlatformSubscriptionFilter.ExpiringSoon,
        "expired" => PlatformSubscriptionFilter.Expired,
        "suspended" => PlatformSubscriptionFilter.Suspended,
        "missing" => PlatformSubscriptionFilter.Missing,
        _ => null
    };
}
