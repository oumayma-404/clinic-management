using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Platform.Commands;

/// <summary>
/// The vendor replaces one clinic account's password with a fresh temporary one, so its owner can get back in —
/// the sibling of <see cref="ResetClinicUserSecondFactorFromConsoleCommand"/>, for the credential beside the factor.
///
/// <para><b>⚠️ Why the vendor has to be able to do this at all.</b> There are three ways back from a forgotten
/// password and each fails in the same case. An administrator of the cabinet can reset a colleague's
/// (<c>ResetUserPasswordCommand</c>) — useless when the person locked out <i>is</i> the only administrator. The
/// person can reset it themselves from the login screen (<c>RequestPasswordResetCommand</c>) — useless when the
/// address on the account is unreachable, which is the ordinary case for a cabinet whose e-mail was set up once by
/// somebody who has left. And <c>reset-admin-password</c> works, but only for whoever holds a shell on the server:
/// before this, that meant a support call answered from a bash history, with no row in the console's own journal and
/// no way for the practice to learn afterwards that anything happened. This is the last resort, and it is the
/// heaviest-audited of the three because it is the one a stranger would try to talk their way into.</para>
///
/// <para>⚠️ <b>The motif is mandatory and lands on the ledger row</b> (<c>PlatformAccessEntry.Reason</c>).
/// <c>User.SetPassword</c> is a single choke point that writes no trace of who called it or why, so if the target
/// and the motif are not on that row, « qui a réinitialisé le mot de passe de qui, et pourquoi ? » has no answer
/// anywhere. That row is also the only thing standing between this endpoint and a social-engineered call, so it is
/// not a formality.</para>
///
/// <para>⚠️ <b>The affected person is told, in-app and by e-mail, and told that the VENDOR did it</b>
/// (<c>PasswordResetBy.Vendor</c>). The clinic-administrator wording would send somebody who did not request this
/// to warn an administrator who has no record of it and no power over it. Best-effort and post-commit, like every
/// notification in this codebase: the reset has happened, and a mail failure must not roll it back or hide it.</para>
///
/// <para>⚠️ <b>The temporary password is returned once and never mailed.</b> It goes back to the vendor's screen to
/// be read out over the telephone. Putting it in the notification e-mail would place a live credential in the very
/// mailbox that is either unreachable (the reason this path exists) or in somebody else's hands (the reason the
/// notification exists) — the notice would become the delivery mechanism for the takeover it is meant to reveal.</para>
///
/// <para>⚠️ <b>The second factor is untouched.</b> Somebody who talks support into a password reset still cannot
/// sign in: they need the six-digit code too, and clearing that is a separate call with its own journal row. Doing
/// both here — « while I'm at it » — would collapse two independent proofs into one phone call, which is exactly
/// the attack the split defends against.</para>
///
/// <para>⚠️ <b>Scoped to the cabinet in the URL, and the target is named by e-mail</b>, for the reason its sibling
/// states: the console gains no roster read, and a mistyped address can then only reach somebody at the cabinet the
/// vendor already has open and is already recorded as having viewed.</para>
///
/// <para>⚠️ <b>An account with no password is a refusal, not a silent success.</b> An Auth0-backed account has no
/// <c>PasswordHash</c> to replace, and answering « c'est fait » would tell the vendor the caller can now sign in
/// while whatever is really blocking them is untouched.</para>
/// </summary>
public class ResetClinicUserPasswordFromConsoleCommand : IRequest<Result<PlatformPasswordResetDto>>
{
    public Guid ClinicId { get; set; }

    /// <summary>The address of the account to re-credential. Must belong to <see cref="ClinicId"/>.</summary>
    public string? Email { get; set; }

    /// <summary>Mandatory. The only durable answer to « pourquoi ? » — see the class remarks.</summary>
    public string? Reason { get; set; }
}

public class ResetClinicUserPasswordFromConsoleCommandHandler
    : IRequestHandler<ResetClinicUserPasswordFromConsoleCommand, Result<PlatformPasswordResetDto>>
{
    public const string UnknownClinicCode = "clinic_not_found";

    public const string UnknownAccountCode = "clinic_account_not_found";

    public const string NotLocalAccountCode = "account_has_no_password";

    public const string ReasonRequiredError =
        "Indiquez le motif de la réinitialisation : c'est la seule trace de cette opération, et la seule réponse "
        + "à « qui a réinitialisé ce mot de passe, et pourquoi ? » que vos collègues pourront lire ensuite.";

    public const string EmailRequiredError =
        "Indiquez l'adresse e-mail du compte à réinitialiser. Demandez-la à la personne au téléphone : la console "
        + "n'affiche pas la liste des comptes d'un cabinet.";

    /// <summary>
    /// ⚠️ <b>The same sentence whether the address is unknown to the deployment or belongs to another cabinet.</b>
    /// Distinguishing them would turn this endpoint into a way of asking « does this person work at that practice? »
    /// about any address the vendor cares to type — a question about a cabinet's staff the console is not entitled
    /// to answer.
    /// </summary>
    public const string UnknownAccountError =
        "Aucun compte ne correspond à cette adresse dans ce cabinet. Vérifiez l'adresse auprès de la personne, et "
        + "qu'il s'agit bien du cabinet affiché.";

    public const string NotLocalAccountError =
        "Ce compte ne possède pas de mot de passe géré par le logiciel : son authentification est assurée par un "
        + "fournisseur externe. Il n'y a donc rien à réinitialiser ici, et si cette personne ne parvient pas à se "
        + "connecter, la cause est ailleurs — compte désactivé, second facteur perdu, ou cabinet suspendu.";

    private readonly IClinicRepository _clinics;
    private readonly IUserRepository _users;
    private readonly IPlatformAccessEntryRepository _accessEntries;
    private readonly IPlatformSessionContext _session;
    private readonly ILocalAuthService _localAuth;
    private readonly INotificationGenerator _notifications;
    private readonly ITransactionalEmailSender _email;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<ResetClinicUserPasswordFromConsoleCommandHandler> _logger;

    public ResetClinicUserPasswordFromConsoleCommandHandler(
        IClinicRepository clinics,
        IUserRepository users,
        IPlatformAccessEntryRepository accessEntries,
        IPlatformSessionContext session,
        ILocalAuthService localAuth,
        INotificationGenerator notifications,
        ITransactionalEmailSender email,
        IUnitOfWork unitOfWork,
        ITenantScope tenantScope,
        ILogger<ResetClinicUserPasswordFromConsoleCommandHandler> logger)
    {
        _clinics = clinics;
        _users = users;
        _accessEntries = accessEntries;
        _session = session;
        _localAuth = localAuth;
        _notifications = notifications;
        _email = email;
        _unitOfWork = unitOfWork;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    public async Task<Result<PlatformPasswordResetDto>> Handle(
        ResetClinicUserPasswordFromConsoleCommand request, CancellationToken cancellationToken)
    {
        // EC-12, as on every console path: an undeclared cross-cabinet scope reads zero rows with no error, which
        // here would report every account in the deployment as unknown.
        PlatformTenantScope.EnsureDeclared(_tenantScope);

        try
        {
            if (string.IsNullOrWhiteSpace(request.Email))
            {
                return Result<PlatformPasswordResetDto>.Failure(EmailRequiredError);
            }

            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                return Result<PlatformPasswordResetDto>.Failure(ReasonRequiredError);
            }

            // The companion's own « which cabinet » rule, so the console and the verbs cannot disagree about what
            // identifies a practice.
            var clinicResult = await SubscriptionCabinetLookup.ResolveAsync(
                request.ClinicId, adminEmail: null, _clinics, _users, cancellationToken);

            if (clinicResult.IsFailure)
            {
                return Result<PlatformPasswordResetDto>.Failure(
                    clinicResult.Error ?? "Cabinet introuvable.", UnknownClinicCode);
            }

            var clinicId = clinicResult.Value;
            var clinic = await _clinics.GetByIdAsync(clinicId, cancellationToken);

            if (clinic is null)
            {
                return Result<PlatformPasswordResetDto>.Failure("Cabinet introuvable.", UnknownClinicCode);
            }

            var target = await _users.GetByEmailAsync(request.Email.Trim(), cancellationToken);

            // ⚠️ The cabinet check is what bounds a typo: the address resolves across the deployment, so without
            // it a mis-keyed character could hand out a credential at a practice the vendor never opened.
            if (target is null || target.ClinicId != clinicId)
            {
                return Result<PlatformPasswordResetDto>.Failure(UnknownAccountError, UnknownAccountCode);
            }

            if (!target.IsLocalAccount())
            {
                return Result<PlatformPasswordResetDto>.Failure(NotLocalAccountError, NotLocalAccountCode);
            }

            var now = DateTime.UtcNow;

            // Resolved before anything is written, for the reason `PlatformAccessLedger.RequireAccountId` states:
            // an unattributable action must not aboutir.
            var accountId = PlatformAccessLedger.RequireAccountId(_session);

            // Captured BEFORE the reset, so the row and the response name the person even though the account's own
            // identity fields are not what this write changes.
            var targetEmail = target.Email;
            var targetName = target.FullName;
            var targetRole = target.Role;

            var temporaryPassword = _localAuth.GenerateTemporaryPassword();

            // `mustChangePassword: true` — the vendor has seen this credential and read it down a telephone, so it
            // is a handover token and not a password. `SetPassword` also bumps TokenVersion (every session opened
            // under the old password ends) and clears the lockout, so a person who locked themselves out guessing
            // can use the temporary one immediately.
            target.SetPassword(_localAuth.HashPassword(temporaryPassword), mustChangePassword: true);
            _users.Update(target);

            // Part 4's shape: staged BEFORE the single save, so « a reset with no ledger row behind it » is not a
            // state this command can produce.
            await PlatformAccessLedger.RecordAsync(
                _accessEntries,
                _session,
                clinicId,
                clinic.Name,
                PlatformAccessAction.PasswordReset,
                now,
                cancellationToken,
                targetUserId: target.Id,
                targetEmail: targetEmail,
                reason: request.Reason);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // ⚠️ The temporary password is deliberately absent from this line and from every other log statement:
            // an operator's log is read by more people, and kept longer, than the screen that shows it once.
            _logger.LogInformation(
                "Console account {AccountId} reset the password of user {UserId} at clinic {ClinicId}",
                accountId, target.Id, clinicId);

            // ── Post-commit, best-effort. The reset has happened; telling them is what must not fail it. ──
            await NotifyAsync(clinicId, target.Id, targetEmail, cancellationToken);

            return Result<PlatformPasswordResetDto>.Success(new PlatformPasswordResetDto(
                ClinicId: clinicId,
                TargetEmail: targetEmail,
                TargetName: targetName,
                TargetRole: targetRole,
                OneTimePassword: temporaryPassword,
                ResetAt: now));
        }
        catch (ArgumentException ex)
        {
            // PlatformAccessEntry's own French guard — a motif over its length.
            return Result<PlatformPasswordResetDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error resetting a password at clinic {ClinicId} from the console",
                request.ClinicId);
            return Result<PlatformPasswordResetDto>.Failure(
                "Erreur lors de la réinitialisation du mot de passe.");
        }
    }

    /// <summary>
    /// Both channels, both swallowed. ⚠️ <b>The one thing that makes a social-engineered reset visible</b> — the
    /// person who did not ask for it is the only one placed to notice — so it is worth the two failure paths, and
    /// worth logging when either one cannot be delivered.
    ///
    /// <para>⚠️ <b>Neither channel carries the temporary password</b>; see the class remarks.</para>
    /// </summary>
    private async Task NotifyAsync(
        Guid clinicId, string userId, string? email, CancellationToken cancellationToken)
    {
        try
        {
            await _notifications.PasswordResetAsync(
                clinicId, userId, PasswordResetBy.Vendor, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Impossible d'écrire la notification de réinitialisation du mot de passe.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        try
        {
            var sent = await _email.SendAsync(
                email,
                PasswordResetNotice.EmailSubject,
                PasswordResetNotice.EmailBody(PasswordResetBy.Vendor),
                cancellationToken);

            if (sent.Outcome != TransactionalEmailOutcome.Sent)
            {
                _logger.LogWarning(
                    "Password-reset notice could not be mailed to user {UserId}: {Outcome} {Reason}",
                    userId, sent.Outcome, sent.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Impossible d'envoyer l'e-mail de réinitialisation du mot de passe.");
        }
    }
}
