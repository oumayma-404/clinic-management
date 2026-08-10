using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Platform.Queries;

/// <summary>
/// The strip above the portfolio (<c>platform-console</c> AC-2.7): how many cabinets there are, how many are
/// dormant, and how many the counter pass has never covered.
///
/// <para>⚠️ <b>One bounded read, not a fold of the list.</b> Paging the portfolio to total it would be the
/// AC-2.4a defect with the arithmetic done in the browser instead of the database — and the figures above a
/// table must describe the portfolio, not the page under them.</para>
///
/// <para>⚠️ <b>The vendor's revenue is deliberately absent rather than approximated.</b> AC-2.7 asks for what
/// the <i>vendor</i> recorded as collected from cabinets this month; that ledger is
/// <c>features/clinic-subscription/</c>'s and does not exist here. Summing the cabinets' own
/// <c>ClinicCollectedThisMonthDt</c> would produce a plausible number for an entirely different quantity — the
/// practices' turnover presented as the vendor's income — which is the one confusion AC-2.7 exists to forbid.
/// It stays null and the screen says why (<see cref="PlatformSubscriptionPlaceholder"/>).</para>
/// </summary>
public class GetPlatformSummaryQuery : IRequest<Result<PlatformSummaryDto>>
{
}

public class GetPlatformSummaryQueryHandler : IRequestHandler<GetPlatformSummaryQuery, Result<PlatformSummaryDto>>
{
    private readonly IClinicActivityRepository _activityRepository;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<GetPlatformSummaryQueryHandler> _logger;

    public GetPlatformSummaryQueryHandler(
        IClinicActivityRepository activityRepository,
        ITenantScope tenantScope,
        ILogger<GetPlatformSummaryQueryHandler> logger)
    {
        _activityRepository = activityRepository;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    public async Task<Result<PlatformSummaryDto>> Handle(
        GetPlatformSummaryQuery request, CancellationToken cancellationToken)
    {
        PlatformTenantScope.EnsureDeclared(_tenantScope);

        try
        {
            // « Dormant » is read off the same snapshot column the list's own filter uses, so the count above the
            // table and the rows in it answer the same question rather than two similar ones.
            var totals = await _activityRepository.GetPortfolioTotalsAsync(cancellationToken);

            return Result<PlatformSummaryDto>.Success(new PlatformSummaryDto(
                Clinics: totals.Clinics,
                Dormant: totals.Dormant,
                NeverMeasured: totals.NeverMeasured,
                InTrial: null,
                Active: null,
                ExpiringWithin14Days: null,
                Expired: null,
                Suspended: null,
                VendorCollectedThisMonthDt: null,
                SubscriptionDataAvailable: PlatformSubscriptionPlaceholder.DataAvailable));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error reading the platform summary");
            return Result<PlatformSummaryDto>.Failure("Erreur lors de la lecture du résumé du portefeuille.");
        }
    }
}
