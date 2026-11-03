using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Auth.Queries;
using ClinicManagement.Application.Features.Users;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Auth.Commands;

/// <summary>
/// The colleague chooses her password from the invitation link (<c>guided-first-days</c> C). Sets it, spends the
/// link, and opens <b>no session</b> — like the password reset, she signs in at <c>/login</c> (where the second factor,
/// if any, is asked). No « mot de passe modifié » e-mail: nobody had one before.
/// </summary>
public class AcceptStaffInvitationCommand : IRequest<Result>
{
    public string Token { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class AcceptStaffInvitationCommandHandler : IRequestHandler<AcceptStaffInvitationCommand, Result>
{
    private readonly IStaffInvitationRepository _invitations;
    private readonly IUserRepository _users;
    private readonly ILocalAuthService _localAuthService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<AcceptStaffInvitationCommandHandler> _logger;

    public AcceptStaffInvitationCommandHandler(
        IStaffInvitationRepository invitations,
        IUserRepository users,
        ILocalAuthService localAuthService,
        IUnitOfWork unitOfWork,
        ILogger<AcceptStaffInvitationCommandHandler> logger)
    {
        _invitations = invitations;
        _users = users;
        _localAuthService = localAuthService;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(AcceptStaffInvitationCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrEmpty(request.Password) || request.Password.Length < PasswordPolicy.MinLength)
            {
                return Result.Failure($"Le mot de passe doit contenir au moins {PasswordPolicy.MinLength} caractères.");
            }

            var nowUtc = DateTime.UtcNow;
            var found = await StaffInvitationLink.ResolveAsync(_invitations, _users, request.Token, nowUtc, cancellationToken);
            if (found is null)
            {
                _logger.LogInformation("A staff invitation link was presented that matches no usable invitation.");
                return Result.Failure(StaffInvitations.UnusableLink, StaffInvitations.InvalidCode);
            }

            var (invitation, user) = found.Value;
            user.SetPassword(_localAuthService.HashPassword(request.Password), mustChangePassword: false);
            invitation.Consume(nowUtc);

            _users.Update(user);
            await _invitations.UpdateAsync(invitation, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Staff invitation accepted for user {UserId}.", user.Id);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Accepting a staff invitation failed.");
            return Result.Failure("Le mot de passe n'a pas pu être enregistré. Réessayez.");
        }
    }
}
