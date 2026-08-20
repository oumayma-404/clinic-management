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
/// The vendor removes one clinic account's second factor so its owner can enrol a new one — « way back #3 »,
/// reachable from the console instead of an SSH session (<c>hosted-security-hardening</c> FR-1.4).
///
/// <para><b>⚠️ Why the vendor has to be able to do this at all.</b> Clearing a factor may never rest on the
/// password alone, so somebody else has to vouch for the person who lost their authenticator. The clinic's own
/// administrator is the first answer (<c>ResetUserTotpCommand</c>) and a recovery code the person still holds is
/// the better one (<c>User.GrantTotpReplacement</c> — no vendor involved at all). Both fail in the same case, and
/// it is the ordinary one for this product: <b>a cabinet with a single administrator</b>, whose phone is gone and
/// whose recovery codes were never printed. Nothing they possess proves who they are, so a human must — and the
/// only humans left are at the vendor. Before this the vendor's only route was
/// <c>dotnet run -- reset-user-totp</c> on the server, which meant a support call was answered by whoever had
/// shell access, unrecorded in the console's own journal.</para>
///
/// <para>⚠️ <b>The motif is mandatory and lands on the ledger row</b> (<c>PlatformAccessEntry.Reason</c>), which is
/// unlike every other console write: a suspension writes its reason onto the entitlement and a cancellation onto
/// the entry it strikes through, whereas <c>DisableTotp</c> keeps no trace whatsoever. If the target and the motif
/// are not on that row, « qui a désarmé le compte de qui, et pourquoi ? » has no answer anywhere. That row is also
/// the only thing standing between this endpoint and a social-engineered call, so it is not a formality.</para>
///
/// <para>⚠️ <b>The affected person is told, in-app and by e-mail, and told that the VENDOR did it</b>
/// (<c>SecondFactorResetBy.Vendor</c>). The clinic-administrator wording would send somebody who did not request
/// this to warn an administrator who has no record of it and no power over it — i.e. to the one person who can do
/// nothing. Best-effort and post-commit, like every notification in this codebase: the reset has happened, and a
/// mail failure must not roll it back or hide it.</para>
///
/// <para>⚠️ <b>Scoped to the cabinet in the URL, and the target is named by e-mail.</b> Two reasons. The console
/// gains <b>no new read</b> of a practice's staff — there is no roster endpoint here, and the vendor types the
/// address the caller gave them over the phone — which keeps « the console cannot see your records » as narrow as
/// it was. And a mistyped address can then only reach somebody at the cabinet the vendor already has open, whose
/// fiche they are already recorded as having viewed, rather than any account in the deployment.</para>
///
/// <para>⚠️ <b>An account with no factor enrolled is a refusal, not a silent success.</b> The suspension
/// command's own reasoning: answering « c'est fait » would write a journal row for an action that never happened,
/// and would tell the vendor the caller can now sign in when whatever is really blocking them is untouched.</para>
/// </summary>
public class ResetClinicUserSecondFactorFromConsoleCommand : IRequest<Result<PlatformSecondFactorResetDto>>
{
    public Guid ClinicId { get; set; }

    /// <summary>The address of the account to disarm. Must belong to <see cref="ClinicId"/>.</summary>
    public string? Email { get; set; }

    /// <summary>Mandatory. The only durable answer to « pourquoi ? » — see the class remarks.</summary>
    public string? Reason { get; set; }
}

public class ResetClinicUserSecondFactorFromConsoleCommandHandler
    : IRequestHandler<ResetClinicUserSecondFactorFromConsoleCommand, Result<PlatformSecondFactorResetDto>>
{
    public const string UnknownClinicCode = "clinic_not_found";

    public const string UnknownAccountCode = "clinic_account_not_found";

    public const string NotEnrolledCode = "second_factor_not_enrolled";

    public const string ReasonRequiredError =
        "Indiquez le motif de la réinitialisation : c'est la seule trace de cette opération, et la seule réponse "
        + "à « qui a désarmé ce compte, et pourquoi ? » que vos collègues pourront lire ensuite.";

    public const string EmailRequiredError =
        "Indiquez l'adresse e-mail du compte à réinitialiser. Demandez-la à la personne au téléphone : la console "
        + "n'affiche pas la liste des comptes d'un cabinet.";

    /// <summary>
    /// ⚠️ <b>The same sentence whether the address is unknown to the deployment or belongs to another cabinet.</b>
    /// Distinguishing them would turn this endpoint into a way of asking « does this person work at that practice? »
    /// about any address the vendor cares to type, which is a question about a cabinet's staff that the console is
    /// not entitled to answer.
    /// </summary>
    public const string UnknownAccountError =
        "Aucun compte ne correspond à cette adresse dans ce cabinet. Vérifiez l'adresse auprès de la personne, et "
        + "qu'il s'agit bien du cabinet affiché.";

    public const string NotEnrolledError =
        "Ce compte n'a pas de second facteur enrôlé : il n'y a rien à réinitialiser, et cette personne peut se "
        + "connecter avec son mot de passe seul. Si elle n'y parvient pas, la cause est ailleurs — compte "
        + "désactivé, mot de passe oublié, ou cabinet suspendu.";

    private readonly IClinicRepository _clinics;
    private readonly IUserRepository _users;
    private readonly IPlatformAccessEntryRepository _accessEntries;
    private readonly IPlatformSessionContext _session;
    private readonly INotificationGenerator _notifications;
    private readonly ITransactionalEmailSender _email;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<ResetClinicUserSecondFactorFromConsoleCommandHandler> _logger;

    public ResetClinicUserSecondFactorFromConsoleCommandHandler(
        IClinicRepository clinics,
        IUserRepository users,
        IPlatformAccessEntryRepository accessEntries,
        IPlatformSessionContext session,
        INotificationGenerator notifications,
        ITransactionalEmailSender email,
        IUnitOfWork unitOfWork,
        ITenantScope tenantScope,
        ILogger<ResetClinicUserSecondFactorFromConsoleCommandHandler> logger)
    {
        _clinics = clinics;
        _users = users;
        _accessEntries = accessEntries;
        _session = session;
        _notifications = notifications;
        _email = email;
        _unitOfWork = unitOfWork;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    public async Task<Result<PlatformSecondFactorResetDto>> Handle(
        ResetClinicUserSecondFactorFromConsoleCommand request, CancellationToken cancellationToken)
    {
        // EC-12, as on every console path: an undeclared cross-cabinet scope reads zero rows with no error, which
        // here would report every account in the deployment as unknown.
        PlatformTenantScope.EnsureDeclared(_tenantScope);

        try
        {
            if (string.IsNullOrWhiteSpace(request.Email))
            {
                return Result<PlatformSecondFactorResetDto>.Failure(EmailRequiredError);
            }

            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                return Result<PlatformSecondFactorResetDto>.Failure(ReasonRequiredError);
            }

            // The companion's own « which cabinet » rule, so the console and the verbs cannot disagree about what
            // identifies a practice.
            var clinicResult = await SubscriptionCabinetLookup.ResolveAsync(
                request.ClinicId, adminEmail: null, _clinics, _users, cancellationToken);

            if (clinicResult.IsFailure)
            {
                return Result<PlatformSecondFactorResetDto>.Failure(
                    clinicResult.Error ?? "Cabinet introuvable.", UnknownClinicCode);
            }

            var clinicId = clinicResult.Value;
            var clinic = await _clinics.GetByIdAsync(clinicId, cancellationToken);

            if (clinic is null)
            {
                return Result<PlatformSecondFactorResetDto>.Failure("Cabinet introuvable.", UnknownClinicCode);
            }

            var target = await _users.GetByEmailAsync(request.Email.Trim(), cancellationToken);

            // ⚠️ The cabinet check is what bounds a typo: the address resolves across the deployment, so without
            // it a mis-keyed character could disarm somebody at a practice the vendor never opened.
            if (target is null || target.ClinicId != clinicId)
            {
                return Result<PlatformSecondFactorResetDto>.Failure(UnknownAccountError, UnknownAccountCode);
            }

            if (!target.IsTotpEnrolled)
            {
                return Result<PlatformSecondFactorResetDto>.Failure(NotEnrolledError, NotEnrolledCode);
            }

            var now = DateTime.UtcNow;

            // Resolved before anything is written, for the reason `PlatformAccessLedger.RequireAccountId` states:
            // an unattributable action must not aboutir.
            var accountId = PlatformAccessLedger.RequireAccountId(_session);

            // The address is captured BEFORE the reset, because the row has to name the person even though nothing
            // about the account changes here except its credential.
            var targetEmail = target.Email;
            var targetName = target.FullName;
            var targetRole = target.Role;

            // Clears the secret AND every recovery code AND bumps TokenVersion, so the lost authenticator stops
            // working, the old codes stop being spendable, and every session opened under the stronger rule ends.
            target.DisableTotp();
            _users.Update(target);

            // Part 4's shape: staged BEFORE the single save, so « a reset with no ledger row behind it » is not a
            // state this command can produce.
            await PlatformAccessLedger.RecordAsync(
                _accessEntries,
                _session,
                clinicId,
                clinic.Name,
                PlatformAccessAction.SecondFactorReset,
                now,
                cancellationToken,
                targetUserId: target.Id,
                targetEmail: targetEmail,
                reason: request.Reason);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Console account {AccountId} reset the second factor of user {UserId} at clinic {ClinicId}",
                accountId, target.Id, clinicId);

            // ── Post-commit, best-effort. The reset has happened; telling them is what must not fail it. ──
            await NotifyAsync(clinicId, target.Id, targetEmail, cancellationToken);

            return Result<PlatformSecondFactorResetDto>.Success(new PlatformSecondFactorResetDto(
                ClinicId: clinicId,
                TargetEmail: targetEmail,
                TargetName: targetName,
                TargetRole: targetRole,
                ResetAt: now));
        }
        catch (ArgumentException ex)
        {
            // PlatformAccessEntry's own French guard — a motif over its length.
            return Result<PlatformSecondFactorResetDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error resetting a second factor at clinic {ClinicId} from the console",
                request.ClinicId);
            return Result<PlatformSecondFactorResetDto>.Failure(
                "Erreur lors de la réinitialisation du second facteur.");
        }
    }

    /// <summary>
    /// Both channels, both swallowed. ⚠️ <b>The one thing that makes a social-engineered reset visible</b> — the
    /// person who did not ask for it is the only one placed to notice — so it is worth the two failure paths, and
    /// worth logging when either one cannot be delivered.
    /// </summary>
    private async Task NotifyAsync(
        Guid clinicId, string userId, string? email, CancellationToken cancellationToken)
    {
        try
        {
            await _notifications.SecondFactorResetAsync(
                clinicId, userId, SecondFactorResetBy.Vendor, cancellationToken);
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
            await _email.SendAsync(
                email,
                SecondFactorResetNotice.EmailSubject,
                SecondFactorResetNotice.EmailBody(SecondFactorResetBy.Vendor),
                cancellationToken);
        }
        catch (Exception ex)
        {
            // The in-app row above already carries the fact; e-mail is the second channel, not the only one — and
            // for somebody locked out of the application it is the one that will actually reach them.
            _logger.LogError(ex, "Impossible d'envoyer l'e-mail de réinitialisation du second facteur.");
        }
    }
}
