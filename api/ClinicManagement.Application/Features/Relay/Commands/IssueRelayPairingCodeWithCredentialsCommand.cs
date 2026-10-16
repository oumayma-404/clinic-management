using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Auth;
using ClinicManagement.Application.Features.Auth.Commands;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Relay.Commands;

/// <summary>
/// « Installer le PC de secours ici… » on any PC (AC-1.5): an admin's email, password and authenticator code, so the
/// always-on reception PC can be chosen while a secretary is signed in on it. The proof is the sign-in's own — the same
/// lockout, the same replay guard, the same refusals — and the session it opens is ended before the answer leaves.
/// </summary>
public sealed record IssueRelayPairingCodeWithCredentialsCommand(string Email, string Password, string TotpCode, string Label)
    : IRequest<Result<RelayPairingCodeDto>>;

public sealed class IssueRelayPairingCodeWithCredentialsCommandHandler
    : IRequestHandler<IssueRelayPairingCodeWithCredentialsCommand, Result<RelayPairingCodeDto>>
{
    /// <summary>What « Mes appareils » and the journal call the session this door opens for an instant.</summary>
    public const string DeviceLabel = "Installation du PC de secours";

    private readonly ISender _sender;
    private readonly IUserRepository _users;
    private readonly IClinicRelayRepository _relays;
    private readonly IAuditEntryRepository _auditEntries;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<IssueRelayPairingCodeWithCredentialsCommandHandler> _logger;

    public IssueRelayPairingCodeWithCredentialsCommandHandler(
        ISender sender,
        IUserRepository users,
        IClinicRelayRepository relays,
        IAuditEntryRepository auditEntries,
        IUnitOfWork unitOfWork,
        ITenantScope tenantScope,
        ILogger<IssueRelayPairingCodeWithCredentialsCommandHandler> logger)
    {
        _sender = sender;
        _users = users;
        _relays = relays;
        _auditEntries = auditEntries;
        _unitOfWork = unitOfWork;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    public async Task<Result<RelayPairingCodeDto>> Handle(
        IssueRelayPairingCodeWithCredentialsCommand request, CancellationToken cancellationToken)
    {
        // The code is the point of this door: asked before the password is checked, so no attempt is spent on it.
        if (string.IsNullOrWhiteSpace(request.TotpCode))
        {
            return Result<RelayPairingCodeDto>.Failure(
                ClinicAuthRefusals.MessageFor(ClinicAuthRefusals.TotpRequired)!, ClinicAuthRefusals.TotpRequired);
        }

        var login = await _sender.Send(new LoginCommand
        {
            Email = request.Email,
            Password = request.Password,
            TotpCode = request.TotpCode,
            DeviceLabel = DeviceLabel,
        }, cancellationToken);

        if (login.IsFailure)
        {
            return Result<RelayPairingCodeDto>.FailureFrom(login);
        }

        try
        {
            var user = await _users.GetByAuth0SubAsync(login.Value!.User.Id, cancellationToken);
            if (user is null || !user.IsActive)
            {
                return Result<RelayPairingCodeDto>.Failure("Session invalide, veuillez réessayer.");
            }

            if (!user.IsAdmin())
            {
                return Result<RelayPairingCodeDto>.Failure(RelayRefusals.NotAdmin, RelayRefusals.NotAdminCode);
            }

            // Where the deployment does not require a factor of an admin, the sign-in accepted one without a code: this
            // door does not, since a password alone must not be able to install a copy of every record.
            if (!user.IsTotpEnrolled)
            {
                return Result<RelayPairingCodeDto>.Failure(
                    RelayRefusals.AuthenticatorRequired, RelayRefusals.AuthenticatorRequiredCode);
            }

            if (login.Value.MustChangePassword)
            {
                return Result<RelayPairingCodeDto>.Failure(
                    RelayRefusals.PasswordChangeRequired, RelayRefusals.PasswordChangeRequiredCode);
            }

            // An anonymous request has no clinic in scope, and an Unset scope reads zero rows: the clinic's current PC
            // would be invisible and a second one would be refused by the index as « un autre PC ».
            _tenantScope.UseClinic(user.ClinicId);

            return await RelayPairingIssuance.IssueAsync(user, request.Label, new AuditActor(user.Id, user.Email),
                _relays, _auditEntries, _unitOfWork, _logger, cancellationToken);
        }
        finally
        {
            // The sign-in was a proof, not a session: nothing holds its credential, so it ends here whatever happened.
            await _sender.Send(new EndSessionCommand { RefreshToken = login.Value!.RefreshToken }, cancellationToken);
        }
    }
}
