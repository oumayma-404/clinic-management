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
/// Admin-only: move a clinic user between <c>admin</c>, <c>doctor</c> and <c>secretary</c> (AC-P2.23).
/// <para>
/// No user's role could ever be changed before — a member onboarded as a secretary who turned out to be the
/// practitioner had to be deactivated and re-registered under a new account, orphaning everything already
/// recorded against the old one.
/// </para>
/// </summary>
public class ChangeUserRoleCommand : IRequest<Result<ClinicUserDto>>
{
    public string TargetUserId { get; set; } = string.Empty;
    /// <summary>One of <see cref="User.AssignableRoles"/>, case-insensitive.</summary>
    public string Role { get; set; } = string.Empty;
}

public class ChangeUserRoleCommandHandler : IRequestHandler<ChangeUserRoleCommand, Result<ClinicUserDto>>
{
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ChangeUserRoleCommandHandler> _logger;

    public ChangeUserRoleCommandHandler(
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork,
        ILogger<ChangeUserRoleCommandHandler> logger)
    {
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<ClinicUserDto>> Handle(ChangeUserRoleCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var callerId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(callerId))
            {
                return Result<ClinicUserDto>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            var admin = await _userRepository.GetByAuth0SubAsync(callerId, cancellationToken);
            if (admin == null)
            {
                return Result<ClinicUserDto>.Failure("Utilisateur introuvable.");
            }

            // The controller is AdminOnly, but the authoritative check is the DB role — the same
            // defense-in-depth every other admin command applies rather than trusting the JWT claim.
            if (!admin.IsAdmin())
            {
                return Result<ClinicUserDto>.Failure("Seuls les administrateurs peuvent modifier le rôle d'un utilisateur.");
            }

            if (string.IsNullOrWhiteSpace(request.TargetUserId))
            {
                return Result<ClinicUserDto>.Failure("L'utilisateur cible est requis.");
            }

            // AC-P2.24 (A-11): validated against the closed set before anything is loaded or mutated. An
            // unvalidated role matches no authorization policy, so the account would silently lose every
            // surface — a "successful" call that quietly breaks the user.
            var role = User.NormalizeRole(request.Role);
            if (role == null)
            {
                return Result<ClinicUserDto>.Failure(
                    "Rôle invalide. Choisissez « admin », « doctor » ou « secretary ».");
            }

            var target = await _userRepository.GetByIdAsync(request.TargetUserId, cancellationToken);
            // Scope to the admin's own clinic (tenant isolation): another clinic's user reads as not found.
            if (target == null || target.ClinicId != admin.ClinicId)
            {
                return Result<ClinicUserDto>.Failure("Utilisateur introuvable.");
            }

            // AC-P2.26: the self-lockout guard, extended from SetUserActiveCommand's deactivation case. An
            // admin demoting themselves is legitimate as long as someone else can still administer the clinic;
            // doing it as the ONLY active admin leaves nobody able to manage users, and the offline recovery
            // utility resets a password — it does not grant a role.
            var demotingSelf = string.Equals(request.TargetUserId, admin.Id, StringComparison.Ordinal)
                && admin.IsAdmin()
                && role != User.RoleAdmin;
            if (demotingSelf)
            {
                // Unpaged: the guard below asks whether ANY other active admin exists, which a page cannot answer.
                var clinicUsers = (await _userRepository.GetByClinicIdAsync(
                    admin.ClinicId, cancellationToken: cancellationToken)).Items;
                var otherActiveAdmins = clinicUsers.Count(u =>
                    u.IsAdmin()
                    && u.IsActive
                    && !string.Equals(u.Id, admin.Id, StringComparison.Ordinal));
                if (otherActiveAdmins == 0)
                {
                    return Result<ClinicUserDto>.Failure(
                        "Vous êtes le seul administrateur actif du cabinet : nommez d'abord un autre administrateur.");
                }
            }

            // Returns false when the account already holds that role — do not bump TokenVersion (and log the
            // user out) for a re-selection that changes nothing.
            if (target.ChangeRole(role))
            {
                _userRepository.Update(target);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Result<ClinicUserDto>.Success(new ClinicUserDto
            {
                Id = target.Id,
                ClinicId = target.ClinicId,
                Role = target.Role,
                Email = target.Email,
                FullName = target.FullName,
                IsActive = target.IsActive,
                MustChangePassword = target.MustChangePassword,
                LastLoginAt = target.LastLoginAt,
                CreatedAt = target.CreatedAt
            });
        }
        catch (ArgumentException ex)
        {
            // ChangeRole's own guard. Reachable only if the NormalizeRole check above were ever bypassed, but
            // the message is already French and caller-safe.
            return Result<ClinicUserDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Unhandled failure changing the role of user {TargetUserId}", request.TargetUserId);
            return Result<ClinicUserDto>.Failure("Erreur lors de la modification du rôle. Veuillez réessayer.");
        }
    }
}
