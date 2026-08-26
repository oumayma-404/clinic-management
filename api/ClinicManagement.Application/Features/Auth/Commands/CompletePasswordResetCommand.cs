using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Auth.Commands;

/// <summary>
/// Spends a reset link and sets the password the person just chose — the second half of
/// <see cref="RequestPasswordResetCommand"/>, and the only place in that flow where an account changes.
///
/// <para>⚠️ <b>It signs nobody in.</b> A reset is not an authentication: the person must now go to the login screen
/// and present the new password <i>and</i> their six-digit code. Returning a session here would hand every
/// mailbox-holder a way past the second factor, which is the one thing this whole flow must not do — and it is why
/// the BFF route behind it writes no cookies.</para>
///
/// <para>⚠️ <b>Three things fall out of <see cref="User.SetPassword"/> and none of them is incidental.</b> It bumps
/// <c>TokenVersion</c>, so every session opened with the forgotten password dies the moment the new one is set —
/// which is exactly right if the reason it was forgotten is that somebody else changed it. It clears
/// <c>LockoutEnd</c>, so a person who locked themselves out guessing does not then have to wait fifteen minutes to
/// use the password they just chose. And it is the single choke point every password path in the product funnels
/// through, so none of that had to be re-implemented here.</para>
///
/// <para>⚠️ <b>The second factor is deliberately untouched</b>, and this is the load-bearing decision of the
/// feature. Controlling the mailbox is enough to replace a password precisely <i>because</i> TOTP still gates the
/// sign-in that follows. Clearing the factor here — or worse, opening a replacement window as
/// <c>RedeemRecoveryCodeCommand</c> does after proving two things — would convert read access to one inbox into
/// full account takeover.</para>
/// </summary>
public class CompletePasswordResetCommand : IRequest<Result>
{
    public string Token { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class CompletePasswordResetCommandHandler : IRequestHandler<CompletePasswordResetCommand, Result>
{
    /// <summary>
    /// The single refusal for every unusable link: unknown, expired, already spent, or pointing at an account that
    /// has since been deactivated or deleted.
    ///
    /// <para>⚠️ <b>One sentence for all of them, on purpose.</b> « Ce lien a déjà été utilisé » versus « ce lien
    /// est inconnu » tells a holder of stolen tokens which ones were once real, and tells anybody with a captured
    /// link whether the account still exists. The distinction is genuinely useful to the operator, so it goes to
    /// the log — where it is already recorded — and not to the response.</para>
    /// </summary>
    private const string UnusableLink =
        "Ce lien de réinitialisation n'est plus valable. Demandez-en un nouveau depuis l'écran de connexion.";

    /// <summary>Lets the controller answer 410 rather than 400 — the request was well-formed; the link is spent.</summary>
    public const string InvalidTokenCode = "password_reset_token_invalid";

    private readonly IPasswordResetRequestRepository _requests;
    private readonly IUserRepository _users;
    private readonly ILocalAuthService _localAuthService;
    private readonly ITransactionalEmailSender _emailSender;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CompletePasswordResetCommandHandler> _logger;

    public CompletePasswordResetCommandHandler(
        IPasswordResetRequestRepository requests,
        IUserRepository users,
        ILocalAuthService localAuthService,
        ITransactionalEmailSender emailSender,
        IUnitOfWork unitOfWork,
        ILogger<CompletePasswordResetCommandHandler> logger)
    {
        _requests = requests;
        _users = users;
        _localAuthService = localAuthService;
        _emailSender = emailSender;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(CompletePasswordResetCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Token))
            {
                return Result.Failure(UnusableLink, InvalidTokenCode);
            }

            // ⚠️ The length rule is checked BEFORE the token is looked up, and it is the one refusal that is not
            // `UnusableLink`. It is a fact about what was typed, not about whether the link is real, so it reveals
            // nothing — and checking it after would spend a perfectly good token on a password the server then
            // refuses, leaving the person with a dead link and a French sentence telling them to request another.
            if (string.IsNullOrEmpty(request.NewPassword)
                || request.NewPassword.Length < PasswordPolicy.MinLength)
            {
                return Result.Failure(
                    $"Le mot de passe doit contenir au moins {PasswordPolicy.MinLength} caractères.");
            }

            var nowUtc = DateTime.UtcNow;
            var candidateHash = PasswordResetRequest.HashToken(request.Token.Trim());

            var row = await _requests.GetByTokenHashAsync(candidateHash, cancellationToken);
            if (row is null || !PasswordResetRequest.TokenHashMatches(row.TokenHash, candidateHash))
            {
                _logger.LogInformation("A password-reset link was presented that matches no live request.");
                return Result.Failure(UnusableLink, InvalidTokenCode);
            }

            if (!row.IsUsable(nowUtc))
            {
                _logger.LogInformation(
                    "Password-reset request {RequestId} was presented after it was {State}.",
                    row.Id, row.ConsumedAtUtc is null ? "expired" : "already spent");
                return Result.Failure(UnusableLink, InvalidTokenCode);
            }

            var user = await _users.GetByIdAsync(row.UserId, cancellationToken);

            // The account may have been deactivated, or its e-mail moved to another provider, in the hour since
            // the link was mailed. Either way this link can no longer do what it says — and the row is spent on the
            // way out, so a link that has become useless cannot be retried indefinitely.
            if (user is null || !user.IsLocalAccount() || !user.IsActive)
            {
                row.Consume(nowUtc);
                await _requests.UpdateAsync(row, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                _logger.LogWarning(
                    "Password-reset request {RequestId} named an account that can no longer be reset.", row.Id);
                return Result.Failure(UnusableLink, InvalidTokenCode);
            }

            // `mustChangePassword: false` — unlike an administrator's reset or the console verb, which hand over a
            // temporary password somebody else generated. This one the owner chose themselves, so forcing them to
            // choose a second one at the next screen would be a ritual with no security in it.
            user.SetPassword(_localAuthService.HashPassword(request.NewPassword), mustChangePassword: false);
            row.Consume(nowUtc);

            _users.Update(user);
            await _requests.UpdateAsync(row, cancellationToken);

            // One save: the new password and the spent token commit together, or neither does. A token spent
            // without the password landing would lock the person out of the flow they were halfway through.
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Password reset completed for user {UserId}.", user.Id);

            // ── Post-commit, best-effort. The password has changed; telling them is what must not fail it. ──
            await NotifyAsync(user, cancellationToken);

            return Result.Success();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Completing a password reset failed.");
            return Result.Failure(
                "La réinitialisation n'a pas pu aboutir. Veuillez réessayer.");
        }
    }

    /// <summary>
    /// Tells the account holder their password was replaced.
    ///
    /// <para>⚠️ <b>The only thing that makes a mailbox compromise visible to its owner.</b> Somebody who takes over
    /// an inbox and resets the password leaves no other trace the person would notice — their sessions simply end,
    /// which reads as an ordinary timeout. Worth the failure path, and worth logging when it cannot be delivered.
    /// </para>
    ///
    /// <para>No in-app notification beside it, unlike the vendor-initiated reset: <c>StaffNotification</c> is
    /// clinic-scoped, this path is anonymous and holds no tenant scope, and the person is by construction signed
    /// out of every session that could have shown it to them.</para>
    /// </summary>
    private async Task NotifyAsync(User user, CancellationToken cancellationToken)
    {
        // Sent to the account's CURRENT address, not to the one the request row recorded. If the two differ, the
        // address on the account is the one its owner reads today — and telling the old mailbox that the password
        // changed would inform precisely the party a change of address exists to cut off.
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            _logger.LogWarning(
                "User {UserId} has no e-mail address; the password-change confirmation was not sent.", user.Id);
            return;
        }

        try
        {
            var sent = await _emailSender.SendAsync(
                user.Email,
                "Votre mot de passe a été modifié",
                $"""
                {EmailGreeting.For(user.FullName)}

                Le mot de passe de votre compte vient d'être modifié. Vos autres appareils ont été
                déconnectés et devront se reconnecter.

                Si vous n'êtes pas à l'origine de ce changement, contactez immédiatement l'administrateur
                de votre cabinet : quelqu'un a eu accès à votre boîte e-mail.
                """,
                cancellationToken);

            if (sent.Outcome != TransactionalEmailOutcome.Sent)
            {
                _logger.LogWarning(
                    "Password-change confirmation could not be sent to user {UserId}: {Outcome} {Reason}",
                    user.Id, sent.Outcome, sent.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Impossible d'envoyer la confirmation de changement de mot de passe.");
        }
    }
}
