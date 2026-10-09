using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Relay.Queries;

/// <summary>
/// D22: the signed-in person's ticket to a session on the PC de secours, for the Windows or Android app to trade there
/// before any cut (and again each day). Also says where the PC is and which certificate it shows (D21), so the app can
/// reach it with nothing else. A read: the key it signs with is minted at the PC's heartbeat, never here.
/// </summary>
public sealed record GetRelayAssertionQuery : IRequest<Result<RelayAssertionDto>>;

public sealed record RelayAssertionDto(
    string Assertion, DateTime ExpiresAtUtc, Guid RelayId, IReadOnlyList<string> Addresses, int? Port, string? Fingerprint);

public sealed class GetRelayAssertionQueryHandler : IRequestHandler<GetRelayAssertionQuery, Result<RelayAssertionDto>>
{
    /// <summary>No PC de secours ready to receive one — nothing to prepare, which is not an error on the caller's side.</summary>
    public const string NotReadyCode = "relay_not_ready";

    private readonly IClinicContext _clinicContext;
    private readonly IUserRepository _users;
    private readonly IClinicRelayRepository _relays;
    private readonly IRelayAssertionKeys _keys;

    public GetRelayAssertionQueryHandler(
        IClinicContext clinicContext, IUserRepository users, IClinicRelayRepository relays, IRelayAssertionKeys keys)
    {
        _clinicContext = clinicContext;
        _users = users;
        _relays = relays;
        _keys = keys;
    }

    public async Task<Result<RelayAssertionDto>> Handle(GetRelayAssertionQuery request, CancellationToken cancellationToken)
    {
        var userId = _clinicContext.GetUserId();
        var user = userId is null ? null : await _users.GetByAuth0SubAsync(userId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return Result<RelayAssertionDto>.Failure("Utilisateur introuvable.");
        }

        var relay = await _relays.GetCurrentForClinicAsync(user.ClinicId, cancellationToken);
        var key = relay is { Status: ClinicRelayStatus.Active } ? _keys.Open(relay.AssertionKeyProtected) : null;
        if (relay is null || key is null)
        {
            return Result<RelayAssertionDto>.Failure("Aucun PC de secours prêt à recevoir ce poste.", NotReadyCode);
        }

        var now = DateTime.UtcNow;
        var expires = now + RelayAssertion.Lifetime;
        var assertion = RelayAssertion.Issue(key,
            new RelayAssertionClaims(user.Id, user.ClinicId, relay.Id, user.TokenVersion, now, expires));
        return Result<RelayAssertionDto>.Success(new RelayAssertionDto(
            assertion, expires, relay.Id, relay.LanAddressList, relay.HttpsPort, relay.CertificateFingerprint));
    }
}
