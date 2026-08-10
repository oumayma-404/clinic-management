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

    /// <summary>Matches the cabinet's name, its city, or an administrator's e-mail address (AC-2.5).</summary>
    public string? Q { get; set; }

    /// <summary>
    /// <c>name</c> | <c>activity</c> | <c>createdAt</c>. An unrecognised value falls back to <c>name</c> rather
    /// than refusing — the same tolerance the lab-order stage filter and the audit action filter apply, so a
    /// stale bookmark shows rows instead of a French error.
    /// </summary>
    public string? Sort { get; set; }

    public int? Page { get; set; }

    public int? PageSize { get; set; }
}

public class ListPlatformClinicsQueryHandler
    : IRequestHandler<ListPlatformClinicsQuery, Result<PlatformClinicPageDto>>
{
    private readonly IClinicActivityRepository _activityRepository;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<ListPlatformClinicsQueryHandler> _logger;

    public ListPlatformClinicsQueryHandler(
        IClinicActivityRepository activityRepository,
        ITenantScope tenantScope,
        ILogger<ListPlatformClinicsQueryHandler> logger)
    {
        _activityRepository = activityRepository;
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
            var filter = new PlatformPortfolioFilter(
                SearchPattern: SearchTerm.ToLikePattern(request.Q),
                DormantOnly: request.Dormant,
                Sort: ParseSort(request.Sort));

            // Omitting the paging parameters gets the FIRST PAGE, not everything — the opposite of the clinic
            // app's list reads, and for the audit ledger's reason: there is no legitimate caller for « every
            // cabinet at once », and an unpaged default is a hosted deployment's whole portfolio in one response.
            var paging = PageRequest.From(request.Page, request.PageSize) ?? PageRequest.Of(1, PageRequest.DefaultPageSize);

            var page = await _activityRepository.GetPortfolioAsync(filter, paging, cancellationToken);
            var items = page.Items.Select(ToDto).ToList();

            return Result<PlatformClinicPageDto>.Success(new PlatformClinicPageDto(
                Items: items,
                Page: page.Page,
                PageSize: page.PageSize,
                TotalCount: page.TotalCount,
                TotalPages: page.TotalPages,
                HasPreviousPage: page.HasPreviousPage,
                HasNextPage: page.HasNextPage,
                CountersAsOf: OldestMeasurement(items),
                SubscriptionDataAvailable: PlatformSubscriptionPlaceholder.DataAvailable));
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

    private static PlatformPortfolioSort ParseSort(string? sort) => sort?.Trim().ToLowerInvariant() switch
    {
        "activity" => PlatformPortfolioSort.Activity,
        "createdat" => PlatformPortfolioSort.CreatedAt,
        _ => PlatformPortfolioSort.Name
    };

    private static PlatformClinicRowDto ToDto(PlatformClinicRow row) => new(
        ClinicId: row.ClinicId,
        Name: row.Name,
        City: row.City,
        CreatedAt: row.CreatedAt,
        // The four entitlement members stay null until features/clinic-subscription/ ships. Part 4 replaces this
        // block with the companion's own read — see PlatformSubscriptionPlaceholder on why it is not folded here.
        Plan: null,
        State: null,
        EndsOn: null,
        DaysRemaining: null,
        Users: row.Users,
        Patients: row.Patients,
        Appointments30d: row.Appointments30d,
        Writes7d: row.Writes7d,
        Writes30d: row.Writes30d,
        ActiveDays30d: row.ActiveDays30d,
        LastWriteAt: row.LastWriteAt,
        LastLoginAt: row.LastLoginAt,
        ClinicCollectedThisMonthDt: row.CollectedThisMonth,
        CountersComputedAt: row.CountersComputedAt);
}
