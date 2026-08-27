using ClinicManagement.Application.Common;
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
/// <para>⚠️ <b>The vendor's revenue is read from the vendor's own ledger and is never a sum of the cabinets'.</b>
/// AC-2.7 asks for what the <i>vendor</i> was paid this month; summing the practices'
/// <c>ClinicCollectedThisMonthDt</c> would produce a plausible number for an entirely different quantity — their
/// turnover presented as the vendor's income — which is the one confusion AC-2.7 exists to forbid. Hence a separate
/// repository, a separate table and a separate field name.</para>
/// </summary>
public class GetPlatformSummaryQuery : IRequest<Result<PlatformSummaryDto>>
{
}

public class GetPlatformSummaryQueryHandler : IRequestHandler<GetPlatformSummaryQuery, Result<PlatformSummaryDto>>
{
    private readonly IClinicActivityRepository _activityRepository;
    private readonly IClinicSubscriptionRepository _subscriptions;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<GetPlatformSummaryQueryHandler> _logger;

    public GetPlatformSummaryQueryHandler(
        IClinicActivityRepository activityRepository,
        IClinicSubscriptionRepository subscriptions,
        ITenantScope tenantScope,
        ILogger<GetPlatformSummaryQueryHandler> logger)
    {
        _activityRepository = activityRepository;
        _subscriptions = subscriptions;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    public async Task<Result<PlatformSummaryDto>> Handle(
        GetPlatformSummaryQuery request, CancellationToken cancellationToken)
    {
        PlatformTenantScope.EnsureDeclared(_tenantScope);

        try
        {
            var today = ClinicClock.ClinicToday();

            // Every figure is counted through the same predicate the list filters with, so a chip above the table
            // and the rows it opens answer the same question rather than two similar ones.
            var totals = await _activityRepository.GetPortfolioTotalsAsync(
                today, PlatformPortfolioFilter.DefaultExpiringWithinDays, cancellationToken);

            // ⚠️ The month is the CLINIC's, through ClinicClock — a UTC month files a payment recorded at 00:30 on
            // the 1st into the one that has just closed, which is finding #20 one table over.
            // ⚠️ Month-to-DATE, not the whole month: a vendor payment dated later this month (a post-dated cheque
            // is a first-class concept here) has not been collected yet.
            var (monthFrom, monthTo) = ClinicClock.MonthToDateRangeUtc(today);
            var vendorCollected =
                await _subscriptions.GetVendorCollectedBetweenAsync(monthFrom, monthTo, cancellationToken);

            return Result<PlatformSummaryDto>.Success(new PlatformSummaryDto(
                Clinics: totals.Clinics,
                Dormant: totals.Dormant,
                NeverMeasured: totals.NeverMeasured,
                InTrial: totals.InTrial,
                Active: totals.Active,
                ExpiringWithin14Days: totals.ExpiringWithin14Days,
                Expired: totals.Expired,
                Suspended: totals.Suspended,
                NoEntitlement: totals.NoEntitlement,
                VendorCollectedThisMonthDt: vendorCollected));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error reading the platform summary");
            return Result<PlatformSummaryDto>.Failure("Erreur lors de la lecture du résumé du portefeuille.");
        }
    }
}
