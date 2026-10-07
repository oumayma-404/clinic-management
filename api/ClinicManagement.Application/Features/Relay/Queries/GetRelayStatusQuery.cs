using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using MediatR;

namespace ClinicManagement.Application.Features.Relay.Queries;

/// <summary>« Paramètres → PC de secours » (AC-2.1). A failed read is a failure, never « aucun PC de secours » (AC-2.4).</summary>
public sealed record GetRelayStatusQuery : IRequest<Result<RelayStatusDto>>;

public sealed class GetRelayStatusQueryHandler : IRequestHandler<GetRelayStatusQuery, Result<RelayStatusDto>>
{
    private readonly IClinicContext _clinicContext;
    private readonly IUserRepository _users;
    private readonly IClinicRelayRepository _relays;

    public GetRelayStatusQueryHandler(IClinicContext clinicContext, IUserRepository users, IClinicRelayRepository relays)
    {
        _clinicContext = clinicContext;
        _users = users;
        _relays = relays;
    }

    public async Task<Result<RelayStatusDto>> Handle(GetRelayStatusQuery request, CancellationToken cancellationToken)
    {
        var admin = await Commands.RelayAdmin.ResolveAsync(_clinicContext, _users, cancellationToken);
        if (admin.IsFailure)
        {
            return Result<RelayStatusDto>.FailureFrom(admin);
        }

        var relay = await _relays.GetLatestForClinicAsync(admin.Value!.ClinicId, cancellationToken);
        return Result<RelayStatusDto>.Success(ToDto(relay, DateTime.UtcNow));
    }

    public static RelayStatusDto ToDto(ClinicRelay? relay, DateTime nowUtc)
    {
        var reading = ClinicRelayHealth.Read(relay, nowUtc);
        return new RelayStatusDto(
            relay is not null,
            relay?.Id,
            relay?.Label,
            RelayLabels.Key(reading.State),
            RelayLabels.Sentence(reading, relay, nowUtc),
            reading.IsProblem,
            reading.Since,
            reading.SeedPercent,
            relay?.LanAddressList ?? Array.Empty<string>(),
            relay?.CertificateFingerprint,
            relay?.PairedAtUtc,
            relay?.SeededAtUtc,
            relay?.RetiredAtUtc,
            relay?.FilesTotal ?? 0,
            relay?.FilesCopied ?? 0,
            relay?.DiskFreeBytes);
    }
}
