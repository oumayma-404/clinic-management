using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Platform.Queries;

/// <summary>Who a news e-mail would reach right now — the figures the confirm states and the list the page shows.</summary>
public class GetPlatformNewsAudienceQuery : IRequest<Result<PlatformNewsAudienceDto>>
{
}

public class GetPlatformNewsAudienceQueryHandler
    : IRequestHandler<GetPlatformNewsAudienceQuery, Result<PlatformNewsAudienceDto>>
{
    private readonly IClinicActivityRepository _activity;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<GetPlatformNewsAudienceQueryHandler> _logger;

    public GetPlatformNewsAudienceQueryHandler(
        IClinicActivityRepository activity,
        ITenantScope tenantScope,
        ILogger<GetPlatformNewsAudienceQueryHandler> logger)
    {
        _activity = activity;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    public async Task<Result<PlatformNewsAudienceDto>> Handle(
        GetPlatformNewsAudienceQuery request, CancellationToken cancellationToken)
    {
        PlatformTenantScope.EnsureDeclared(_tenantScope);

        try
        {
            var audience = await _activity.GetNewsAudienceAsync(ClinicClock.ClinicToday(), cancellationToken);
            var plan = PlatformNewsAudiencePlan.From(audience);

            return Result<PlatformNewsAudienceDto>.Success(
                new PlatformNewsAudienceDto(
                    plan.Clinics,
                    plan.Recipients.Count,
                    plan.ClinicsWithoutAdminEmail,
                    plan.Recipients
                        .Select(r => new PlatformNewsAudienceRecipientDto(r.ClinicName, r.FullName, r.Email))
                        .ToList(),
                    plan.SkippedClinics));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error reading the news audience");
            return Result<PlatformNewsAudienceDto>.Failure("Impossible de calculer les destinataires.", ex);
        }
    }
}
