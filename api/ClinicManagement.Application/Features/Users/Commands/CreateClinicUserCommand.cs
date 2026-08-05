using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Users.Commands;

/// <summary>
/// Admin-only: creates a colleague's account directly, returning a one-time password to relay
/// (<c>multi-tenant-cloud</c> US-3).
///
/// <para><b>Why this had to exist.</b> Until now the only way a second person got an account was
/// <c>POST /api/auth/register</c> — self-registration behind the clinic's six-character join code. That is a
/// LAN-scale gate, so <c>HostedMultiTenant</c> closes it (<c>DeploymentProfile.AllowsSelfRegistration</c>), and
/// without this command a hosted clinic would have had <b>no way at all</b> to add staff. <c>UsersController</c>
/// exposed exactly <c>GET</c>, <c>{id}/reset-password</c>, <c>{id}/status</c> and <c>{id}/role</c>: every one of
/// them operates on an account somebody else had already created.</para>
///
/// <para>⚠️ <b>The account is created active, unlike <see cref="User.CreateSelfRegistered"/>.</b> The two differ in
/// who vouched for whom: a self-registration is a stranger asking to be let in, so it waits for approval (I5),
/// while this one <i>is</i> the approval — an admin typed the colleague's name. A pending account here would ask
/// the same admin to approve their own action.</para>
/// </summary>
public class CreateClinicUserCommand : IRequest<Result<CreatedClinicUserDto>>
{
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public class CreateClinicUserCommandHandler : IRequestHandler<CreateClinicUserCommand, Result<CreatedClinicUserDto>>
{
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly ILocalAuthService _localAuthService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CreateClinicUserCommandHandler> _logger;

    public CreateClinicUserCommandHandler(
        IUserRepository userRepository,
        IClinicContext clinicContext,
        ILocalAuthService localAuthService,
        IUnitOfWork unitOfWork,
        ILogger<CreateClinicUserCommandHandler> logger)
    {
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _localAuthService = localAuthService;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<CreatedClinicUserDto>> Handle(CreateClinicUserCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var callerId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(callerId))
            {
                return Result<CreatedClinicUserDto>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            // The class policy is AdminOnly, but the DB role is the authoritative one everywhere in this codebase
            // — a JWT minted before a demotion still carries the old role until it expires.
            var admin = await _userRepository.GetByAuth0SubAsync(callerId, cancellationToken);
            if (admin == null)
            {
                return Result<CreatedClinicUserDto>.Failure("Utilisateur introuvable.");
            }
            if (!admin.IsAdmin())
            {
                return Result<CreatedClinicUserDto>.Failure("Seuls les administrateurs peuvent créer un compte.");
            }

            var email = request.Email?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal))
            {
                return Result<CreatedClinicUserDto>.Failure("Un email valide est requis.");
            }
            if (string.IsNullOrWhiteSpace(request.FullName))
            {
                return Result<CreatedClinicUserDto>.Failure("Le nom complet est requis.");
            }

            var role = User.NormalizeRole(request.Role);
            if (role == null)
            {
                return Result<CreatedClinicUserDto>.Failure(
                    "Rôle invalide. Les rôles autorisés sont : admin, doctor, secretary.");
            }

            // The partial unique index on the lowercased email would otherwise surface as a 500. It is checked
            // across every clinic deliberately — a local account is identified by its email alone at login, so a
            // second clinic reusing one would make the two indistinguishable to the one query that resolves them.
            var existing = await _userRepository.GetByEmailAsync(email, cancellationToken);
            if (existing != null)
            {
                return Result<CreatedClinicUserDto>.Failure("Un compte existe déjà avec cet email.");
            }

            var temporaryPassword = _localAuthService.GenerateTemporaryPassword();
            var user = User.CreateLocalUser(
                admin.ClinicId,
                role,
                email,
                _localAuthService.HashPassword(temporaryPassword),
                request.FullName.Trim(),
                mustChangePassword: true);

            await _userRepository.AddAsync(user, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<CreatedClinicUserDto>.Success(new CreatedClinicUserDto
            {
                UserId = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                Role = user.Role,
                TemporaryPassword = temporaryPassword
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            // A-8 defect class: never echo server internals back to a caller — this one would carry the email.
            _logger.LogError(ex, "Unhandled failure creating a clinic user");
            return Result<CreatedClinicUserDto>.Failure("Erreur lors de la création du compte. Veuillez réessayer.");
        }
    }
}
