using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Users.Commands;

/// <summary>
/// An administrator removes a colleague's second factor so they can enrol a new one
/// (<c>hosted-security-hardening</c> FR-1.4) — way back #2 of three.
///
/// <para>⚠️ <b>Step-up is required</b> (the controller enforces it): this is the action that strips another
/// person's protection, so an unlocked machine with an admin session open must not be enough. The confirmation
/// proves somebody is present <i>now</i>.</para>
///
/// <para>⚠️ <b>The affected user is notified, in-app and by e-mail.</b> Without it this is a quiet way for a
/// stolen admin session to disarm a colleague's account and then sign in as them at leisure — the notification
/// is what makes that loud. It is best-effort and post-commit, like every other side effect in this codebase:
/// the reset has already happened, and a mail failure must not roll it back or hide it.</para>
///
/// <para>It clears the secret <b>and</b> every recovery code and bumps <c>TokenVersion</c>, so the colleague's
/// live sessions end with it — the account is in a weaker state than a moment ago, and every session under the
/// stronger rule is re-established deliberately.</para>
/// </summary>
public class ResetUserTotpCommand : IRequest<Result>
{
    public string UserId { get; set; } = string.Empty;
}

public class ResetUserTotpCommandHandler : IRequestHandler<ResetUserTotpCommand, Result>
{
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationGenerator _notifications;
    private readonly ITransactionalEmailSender _email;
    private readonly ILogger<ResetUserTotpCommandHandler> _logger;

    public ResetUserTotpCommandHandler(
        ICurrentClinicResolver clinicResolver,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        INotificationGenerator notifications,
        ITransactionalEmailSender email,
        ILogger<ResetUserTotpCommandHandler> logger)
    {
        _clinicResolver = clinicResolver;
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _notifications = notifications;
        _email = email;
        _logger = logger;
    }

    public async Task<Result> Handle(ResetUserTotpCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicId = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicId.IsFailure)
            {
                return Result.Failure(clinicId.Error!);
            }

            var target = await _userRepository.GetByAuth0SubAsync(request.UserId, cancellationToken);

            // Tenant-checked against the caller's own clinic: an admin administers their practice's roster and
            // nobody else's.
            if (target is null || target.ClinicId != clinicId.Value)
            {
                return Result.Failure("Utilisateur introuvable.");
            }

            if (!target.IsTotpEnrolled)
            {
                return Result.Failure("Ce compte n'a pas de second facteur à réinitialiser.");
            }

            target.DisableTotp();
            _userRepository.Update(target);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // ── Post-commit, best-effort. The reset has happened; telling them is what must not fail it. ──
            await NotifyAsync(clinicId.Value, target.Id, target.Email, cancellationToken);

            return Result.Success();
        }
        catch (Exception ex) when (ex is not Common.Exceptions.ConflictException)
        {
            return Result.Failure(ErrorMessages.Generic);
        }
    }

    private async Task NotifyAsync(Guid clinicId, string userId, string? email, CancellationToken cancellationToken)
    {
        const string subject = "Votre second facteur a été réinitialisé";
        const string body =
            "Un administrateur de votre clinique a réinitialisé votre second facteur d'authentification. "
            + "Vous devrez en enrôler un nouveau à votre prochaine connexion. "
            + "Si vous n'êtes pas à l'origine de cette demande, prévenez immédiatement votre administrateur.";

        try
        {
            await _notifications.SecondFactorResetAsync(clinicId, userId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Impossible d'écrire la notification de réinitialisation du second facteur.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        try
        {
            await _email.SendAsync(email, subject, body, cancellationToken);
        }
        catch (Exception ex)
        {
            // The in-app row above already carries the fact; e-mail is the second channel, not the only one.
            _logger.LogError(ex, "Impossible d'envoyer l'e-mail de réinitialisation du second facteur.");
        }
    }
}
