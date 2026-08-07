using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.PushDevices.Queries;

/// <summary>
/// What this installation can do about OS notifications, per platform (AC-51, AC-52).
///
/// <para>Read by two clients that must not each decide it for themselves: the settings screen, which states it to
/// an admin, and the native shell, which uses it to decide whether asking the OS for notification permission is
/// even meaningful. A shell that prompts on an installation with no credentials burns the one permission dialog
/// the OS gives it (AC-75).</para>
/// </summary>
public class GetPushAvailabilityQuery : IRequest<Result<PushAvailabilityDto>>;

public class GetPushAvailabilityQueryHandler
    : IRequestHandler<GetPushAvailabilityQuery, Result<PushAvailabilityDto>>
{
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IDeviceRegistrationRepository _devices;
    private readonly IOsPushAvailability _availability;
    private readonly ILogger<GetPushAvailabilityQueryHandler> _logger;

    public GetPushAvailabilityQueryHandler(
        ICurrentClinicResolver clinicResolver,
        IDeviceRegistrationRepository devices,
        IOsPushAvailability availability,
        ILogger<GetPushAvailabilityQueryHandler> logger)
    {
        _clinicResolver = clinicResolver;
        _devices = devices;
        _availability = availability;
        _logger = logger;
    }

    public async Task<Result<PushAvailabilityDto>> Handle(
        GetPushAvailabilityQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<PushAvailabilityDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var dto = new PushAvailabilityDto { AvailableAtAll = _availability.IsAvailableAtAll };

            // Every platform, always, in enum order — « iOS : non configuré » is a true statement about this
            // installation, and omitting the row would leave the reader unable to tell « not configured » from
            // « we forgot to ask ». The same reasoning as la caisse's zero-valued per-method figures.
            foreach (var platform in Enum.GetValues<DevicePlatform>())
            {
                dto.Platforms.Add(new PushPlatformAvailabilityDto
                {
                    Platform = platform,
                    Label = PlatformLabel(platform),
                    Supported = _availability.SupportsPush(platform),
                    Reason = _availability.UnavailableReason(platform),
                    RegisteredDevices = await _devices.CountActiveAsync(
                        clinicResult.Value, platform, cancellationToken)
                });
            }

            return Result<PushAvailabilityDto>.Success(dto);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Failed to read OS push availability");
            return Result<PushAvailabilityDto>.Failure("Impossible de lire l'état des notifications système.");
        }
    }

    /// <summary>
    /// The French-sentence name. Duplicating the two words here rather than reaching into Infrastructure for
    /// them keeps this layer's dependency direction intact, and « Android »/« iOS » are proper nouns that cannot
    /// drift the way a translated phrase could.
    /// </summary>
    private static string PlatformLabel(DevicePlatform platform) => platform switch
    {
        DevicePlatform.Android => "Android",
        DevicePlatform.Ios => "iOS",
        _ => platform.ToString()
    };
}
