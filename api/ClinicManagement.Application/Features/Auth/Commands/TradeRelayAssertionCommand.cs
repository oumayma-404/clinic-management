using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Auth.Commands;

/// <summary>
/// On the PC de secours (<c>clinic-pc-copy</c> D22): a cabinet app trades the cloud's ticket for a session here, so that
/// after a switch the person is already signed in. The app writes it straight into the page's cookie for this PC and
/// never refreshes it itself — one holder only, or the refresh chain's theft detection signs the person out.
///
/// <para>Every refusal is the same sentence and the same code: whether the ticket was forged, expired, for another PC,
/// or for an account changed since, the caller learns only « not prepared ».</para>
/// </summary>
public sealed record TradeRelayAssertionCommand(string? Assertion) : IRequest<Result<LoginResultDto>>;

public sealed class TradeRelayAssertionCommandHandler : IRequestHandler<TradeRelayAssertionCommand, Result<LoginResultDto>>
{
    private readonly IRelayLocalAssertionKey _localKey;
    private readonly IRelayLocalStatus _local;
    private readonly IUserRepository _users;
    private readonly ISessionFamilyRepository _sessionFamilies;
    private readonly ILocalAuthService _localAuth;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<TradeRelayAssertionCommandHandler> _logger;

    public TradeRelayAssertionCommandHandler(
        IRelayLocalAssertionKey localKey, IRelayLocalStatus local, IUserRepository users,
        ISessionFamilyRepository sessionFamilies, ILocalAuthService localAuth, IUnitOfWork unitOfWork,
        ILogger<TradeRelayAssertionCommandHandler> logger)
    {
        _localKey = localKey;
        _local = local;
        _users = users;
        _sessionFamilies = sessionFamilies;
        _localAuth = localAuth;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    private static Result<LoginResultDto> Refuse() =>
        Result<LoginResultDto>.Failure(
            ClinicAuthRefusals.MessageFor(ClinicAuthRefusals.RelaySessionRefused)!, ClinicAuthRefusals.RelaySessionRefused);

    public async Task<Result<LoginResultDto>> Handle(TradeRelayAssertionCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var key = _localKey.Current();
            var now = DateTime.UtcNow;
            var claims = key is null ? null : RelayAssertion.Read(key.Key, request.Assertion, now);
            if (key is null || claims is null || claims.RelayId != key.RelayId || claims.ClinicId != key.ClinicId)
            {
                return Refuse();
            }

            // The copied account decides: a token version moved on the cloud (disabled, password changed, « déconnecter
            // partout ») reaches this PC with the row, and every older ticket then means nothing.
            var user = await _users.GetByAuth0SubAsync(claims.UserId, cancellationToken);
            if (user is null || !user.IsActive || user.ClinicId != claims.ClinicId || user.TokenVersion != claims.TokenVersion
                || (_local.IsRetired && !user.IsAdmin()))
            {
                return Refuse();
            }

            // A session of this install's own — never copied, so nothing the cloud sends is touched. Trusted: the app
            // replaces it each day, and a cut that starts tonight must still find it valid tomorrow.
            var family = new SessionFamily(user.Id, SessionCredential.Hash(Guid.NewGuid().ToString()), now);
            await _sessionFamilies.AddAsync(family, cancellationToken);
            var refresh = _localAuth.GenerateRefreshToken(user, family.Id, trusted: true);
            family.Rotate(SessionCredential.Hash(refresh.AccessToken), refresh.ExpiresAtUtc);
            var access = _localAuth.GenerateToken(user, family.Id);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<LoginResultDto>.Success(new LoginResultDto
            {
                AccessToken = access.AccessToken,
                RefreshToken = refresh.AccessToken,
                ExpiresAt = access.ExpiresAtUtc,
                RefreshExpiresAt = refresh.ExpiresAtUtc,
                MustChangePassword = user.MustChangePassword,
                User = new UserDto
                {
                    Id = user.Id,
                    ClinicId = user.ClinicId,
                    Role = user.Role,
                    Email = user.Email,
                    FullName = user.FullName,
                    CreatedAt = user.CreatedAt,
                },
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "PC de secours: a prepared session could not be opened.");
            return Refuse();
        }
    }
}
