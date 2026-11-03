using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Users.Commands;

/// <summary>
/// « Renvoyer l'e-mail » (<c>guided-first-days</c> C): a new link for a colleague who has never signed in. Re-arms the
/// invitation in place, so the previous link stops working. Administrator only, own cabinet only.
/// </summary>
public class ResendStaffInvitationCommand : IRequest<Result<StaffInvitationResultDto>>
{
    public string UserId { get; set; } = string.Empty;
}

/// <summary>Whether the e-mail left, and the new link (shown once).</summary>
public sealed record StaffInvitationResultDto(bool InvitationEmailed, string InvitationUrl);

public class ResendStaffInvitationCommandHandler
    : IRequestHandler<ResendStaffInvitationCommand, Result<StaffInvitationResultDto>>
{
    public const string AlreadySignedInCode = "invitation_not_needed";

    private readonly IClinicContext _clinicContext;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IStaffInvitationRepository _invitations;
    private readonly ITransactionalEmailSender _emailSender;
    private readonly IPublicAppUrlProvider _appUrl;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ResendStaffInvitationCommandHandler> _logger;

    public ResendStaffInvitationCommandHandler(
        IClinicContext clinicContext,
        IUserRepository users,
        IClinicRepository clinics,
        IStaffInvitationRepository invitations,
        ITransactionalEmailSender emailSender,
        IPublicAppUrlProvider appUrl,
        IUnitOfWork unitOfWork,
        ILogger<ResendStaffInvitationCommandHandler> logger)
    {
        _clinicContext = clinicContext;
        _users = users;
        _clinics = clinics;
        _invitations = invitations;
        _emailSender = emailSender;
        _appUrl = appUrl;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<StaffInvitationResultDto>> Handle(
        ResendStaffInvitationCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var callerId = _clinicContext.GetUserId();
            var admin = string.IsNullOrEmpty(callerId) ? null : await _users.GetByAuth0SubAsync(callerId, cancellationToken);
            if (admin is null || !admin.IsAdmin())
            {
                return Result<StaffInvitationResultDto>.Failure("Seuls les administrateurs peuvent renvoyer une invitation.");
            }

            var user = await _users.GetByIdAsync(request.UserId, cancellationToken);
            // Another cabinet's account reads as not found — never « exists elsewhere ».
            if (user is null || user.ClinicId != admin.ClinicId || !user.IsLocalAccount())
            {
                return Result<StaffInvitationResultDto>.Failure("Utilisateur introuvable.");
            }

            if (user.LastLoginAt is not null)
            {
                return Result<StaffInvitationResultDto>.Failure(
                    "Cette personne s'est déjà connectée : elle n'a plus besoin d'invitation.", AlreadySignedInCode);
            }

            if (!user.IsActive)
            {
                return Result<StaffInvitationResultDto>.Failure("Ce compte est désactivé. Réactivez-le d'abord.");
            }

            if (!_appUrl.IsConfigured || string.IsNullOrWhiteSpace(user.Email))
            {
                return Result<StaffInvitationResultDto>.Failure(
                    "L'invitation ne peut pas être envoyée depuis cette installation.");
            }

            var token = await StaffInvitations.ArmAsync(_invitations, user.Id, admin.Id, DateTime.UtcNow, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var url = StaffInvitations.LinkFor(_appUrl.BaseUrl, token);
            var clinic = await _clinics.GetByIdAsync(admin.ClinicId, cancellationToken);
            var emailed = await StaffInvitations.SendAsync(
                _emailSender,
                user.Email,
                new StaffInvitationFacts(
                    AdminName: admin.FullName ?? admin.Email ?? "Votre cabinet",
                    ClinicName: clinic?.Name ?? "votre cabinet",
                    FullName: user.FullName ?? string.Empty,
                    Email: user.Email,
                    InvitationUrl: url,
                    LoginUrl: $"{_appUrl.BaseUrl.TrimEnd('/')}/login"),
                _logger,
                cancellationToken);

            return Result<StaffInvitationResultDto>.Success(new StaffInvitationResultDto(emailed, url));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Resending a staff invitation failed.");
            return Result<StaffInvitationResultDto>.Failure("L'invitation n'a pas pu être renvoyée. Réessayez.");
        }
    }
}
