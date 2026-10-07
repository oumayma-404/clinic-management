using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Relay.Commands;

/// <summary>The PC trades its long-lived secret for a short <c>clinic-relay</c> token. Every failure is the same refusal.</summary>
public sealed record ExchangeRelayTokenCommand(Guid RelayId, string Secret) : IRequest<Result<RelayTokenDto>>;

public sealed class ExchangeRelayTokenCommandHandler : IRequestHandler<ExchangeRelayTokenCommand, Result<RelayTokenDto>>
{
    private readonly IClinicRelayRepository _relays;
    private readonly ILocalAuthService _auth;

    public ExchangeRelayTokenCommandHandler(IClinicRelayRepository relays, ILocalAuthService auth)
    {
        _relays = relays;
        _auth = auth;
    }

    public async Task<Result<RelayTokenDto>> Handle(ExchangeRelayTokenCommand request, CancellationToken cancellationToken)
    {
        var relay = await _relays.GetByIdAcrossClinicsAsync(request.RelayId, cancellationToken);
        if (relay is null || string.IsNullOrEmpty(request.Secret) || !relay.SecretMatches(request.Secret))
        {
            return Result<RelayTokenDto>.Failure(RelayRefusals.UnknownRelay, RelayRefusals.UnknownRelayCode);
        }

        if (relay.Status == ClinicRelayStatus.Retired)
        {
            return Result<RelayTokenDto>.Failure(RelayRefusals.Retired, RelayRefusals.RetiredCode);
        }

        var token = _auth.GenerateRelayToken(relay);
        return Result<RelayTokenDto>.Success(new RelayTokenDto(token.AccessToken, token.ExpiresAtUtc));
    }
}
