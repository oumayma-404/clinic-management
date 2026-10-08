using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using MediatR;

namespace ClinicManagement.Application.Features.Relay.Queries;

/// <summary>
/// AC-6.2: asked every half-minute by the cabinet's Windows and Android apps — should this device try the PC de secours
/// now, and where? Yes only while the cloud is locked for a PC that said nothing, to a caller on the cabinet's internet
/// line, once the PC's addresses, port and certificate are known. <paramref name="CallerAddress"/> is the request's own.
/// </summary>
public sealed record GetRelayDeviceTargetQuery(string? CallerAddress) : IRequest<Result<RelayDeviceTargetDto>>;

public sealed class GetRelayDeviceTargetQueryHandler : IRequestHandler<GetRelayDeviceTargetQuery, Result<RelayDeviceTargetDto>>
{
    /// <summary>While the cabinet has a PC de secours: about 2 minutes from silence to unlock (60 s lock + two reports).</summary>
    public const int WatchingIntervalSeconds = 30;

    /// <summary>A cabinet with no PC de secours has nothing to watch; its devices ask again much later.</summary>
    public const int IdleIntervalSeconds = 600;

    private readonly IClinicContext _clinicContext;
    private readonly IClinicRelayRepository _relays;

    public GetRelayDeviceTargetQueryHandler(IClinicContext clinicContext, IClinicRelayRepository relays)
    {
        _clinicContext = clinicContext;
        _relays = relays;
    }

    public async Task<Result<RelayDeviceTargetDto>> Handle(GetRelayDeviceTargetQuery request, CancellationToken cancellationToken)
    {
        var clinicId = _clinicContext.GetClinicId();
        var relay = clinicId is { } id ? await _relays.GetCurrentForClinicAsync(id, cancellationToken) : null;
        return Result<RelayDeviceTargetDto>.Success(For(relay, request.CallerAddress, DateTime.UtcNow));
    }

    public static RelayDeviceTargetDto For(ClinicRelay? relay, string? callerAddress, DateTime nowUtc)
    {
        if (relay is null)
        {
            return new RelayDeviceTargetDto(false, Array.Empty<string>(), null, null, IdleIntervalSeconds);
        }

        var addresses = (relay.LanAddresses ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var probe = relay.PcHoldingSinceUtc is null
                    && ClinicWriteLease.IsCloudFenced(relay, nowUtc)
                    && addresses.Length > 0
                    && relay.HttpsPort is not null
                    && relay.CertificateFingerprint is not null
                    && relay.IsFromCabinetInternet(callerAddress);

        return probe
            ? new RelayDeviceTargetDto(true, addresses, relay.HttpsPort, relay.CertificateFingerprint, WatchingIntervalSeconds)
            : new RelayDeviceTargetDto(false, Array.Empty<string>(), null, null, WatchingIntervalSeconds);
    }
}
