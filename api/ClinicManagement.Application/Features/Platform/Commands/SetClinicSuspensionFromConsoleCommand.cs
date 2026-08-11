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
/// The vendor suspends a cabinet for abuse, or lifts that suspension (<c>platform-console</c> US-6).
///
/// <para><b>⚠️ Suspension is not a payment state, and nothing here touches the ledger</b> (AC-6.3). Non-payment is
/// the <i>absence</i> of a grant and expresses itself as expiry; suspension is a decision about conduct. So paying
/// does not lift a suspension, lifting one grants no time, and <b>no paid day is consumed while a cabinet is
/// suspended</b> (AC-6.4) — which is a property of not having spent anything rather than a restore step this command
/// performs. That is also why it does <b>not</b> use <c>SubscriptionRefold</c>: with the ledger untouched there is no
/// date to re-fold and no concurrent-grant convergence to reach, so a lost update here is an ordinary conflict and
/// 409 is the right answer (the companion's own reasoning, in
/// <c>SetSubscriptionSuspensionCommandHandler</c>).</para>
///
/// <para><b>⚠️ One command with a flag rather than a suspend/unsuspend pair</b>, mirroring the companion's
/// <c>SetSubscriptionSuspensionCommand</c>. Two handlers would be two copies of « resolve the cabinet, mutate, stage
/// the access row, save » — and this repository's dominant defect shape is the second copy that stops matching the
/// first. There are still two <i>endpoints</i>, so no client can flip a cabinet the wrong way by omitting a field.
/// Which access action is recorded is decided here, once.</para>
///
/// <para><b>⚠️ Part 4's shape is reused verbatim</b> (progress.md DEV-14): the <c>PlatformAccessEntry</c> is staged
/// <i>before</i> the single save, so a suspension with no ledger row behind it is not a state this command can
/// produce — the « an unattributable action must not aboutir » decision Part 3 settled for reads.</para>
///
/// <para>⚠️ <b>Re-suspending an already-suspended cabinet is a refusal, not a re-statement</b>
/// (<see cref="SetClinicSuspensionFromConsoleCommandHandler.AlreadySuspendedCode"/>): the entitlement holds exactly
/// one motif, one author and one moment, so a second <c>Suspend</c> would overwrite a colleague's reasoning with no
/// trace on the row. Changing a motif is therefore lift-then-suspend, and both halves appear in the journal.</para>
/// </summary>
public class SetClinicSuspensionFromConsoleCommand : IRequest<Result<PlatformSuspensionChangedDto>>
{
    public Guid ClinicId { get; set; }

    /// <summary><c>true</c> to suspend, <c>false</c> to lift. Never inferred from the current state: the vendor is
    /// stating an intention, and a toggle would act on whatever the screen last managed to read.</summary>
    public bool Suspend { get; set; }

    /// <summary>Mandatory when suspending (AC-6.1); ignored when lifting, which clears the whole trail.</summary>
    public string? Reason { get; set; }
}

public class SetClinicSuspensionFromConsoleCommandHandler
    : IRequestHandler<SetClinicSuspensionFromConsoleCommand, Result<PlatformSuspensionChangedDto>>
{
    public const string UnknownClinicCode = "clinic_not_found";

    public const string AlreadySuspendedCode = "clinic_already_suspended";

    public const string NotSuspendedCode = "clinic_not_suspended";

    public const string ReasonRequiredError =
        "Indiquez le motif de la suspension : il reste inscrit sur le cabinet et c'est la seule réponse à "
        + "« suspendu pourquoi ? » que le cabinet et vos collègues pourront lire ensuite.";

    public const string AlreadySuspendedError =
        "Ce cabinet est déjà suspendu. Son motif, son auteur et sa date figurent sur sa fiche — pour en changer le "
        + "motif, levez d'abord la suspension.";

    public const string NotSuspendedError =
        "Ce cabinet n'est pas suspendu : il n'y a aucune suspension à lever. S'il ne peut pas enregistrer de "
        + "nouveaux actes, c'est que sa couverture est arrivée à échéance, ce qui se corrige par un paiement.";

    private readonly IClinicRepository _clinics;
    private readonly IUserRepository _users;
    private readonly IClinicSubscriptionRepository _subscriptions;
    private readonly IPlatformAccessEntryRepository _accessEntries;
    private readonly IPlatformSessionContext _session;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<SetClinicSuspensionFromConsoleCommandHandler> _logger;

    public SetClinicSuspensionFromConsoleCommandHandler(
        IClinicRepository clinics,
        IUserRepository users,
        IClinicSubscriptionRepository subscriptions,
        IPlatformAccessEntryRepository accessEntries,
        IPlatformSessionContext session,
        IUnitOfWork unitOfWork,
        ITenantScope tenantScope,
        ILogger<SetClinicSuspensionFromConsoleCommandHandler> logger)
    {
        _clinics = clinics;
        _users = users;
        _subscriptions = subscriptions;
        _accessEntries = accessEntries;
        _session = session;
        _unitOfWork = unitOfWork;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    public async Task<Result<PlatformSuspensionChangedDto>> Handle(
        SetClinicSuspensionFromConsoleCommand request, CancellationToken cancellationToken)
    {
        // EC-12, as on every console path: an undeclared cross-clinic scope reads zero rows with no error, which
        // here would report every cabinet in the deployment as unknown.
        PlatformTenantScope.EnsureDeclared(_tenantScope);

        try
        {
            if (request.Suspend && string.IsNullOrWhiteSpace(request.Reason))
            {
                return Result<PlatformSuspensionChangedDto>.Failure(ReasonRequiredError);
            }

            // The companion's own « which cabinet » rule, so the console and the five verbs cannot disagree about
            // what identifies a practice.
            var clinicResult = await SubscriptionCabinetLookup.ResolveAsync(
                request.ClinicId, adminEmail: null, _clinics, _users, cancellationToken);

            if (clinicResult.IsFailure)
            {
                return Result<PlatformSuspensionChangedDto>.Failure(
                    clinicResult.Error ?? "Cabinet introuvable.", UnknownClinicCode);
            }

            var clinicId = clinicResult.Value;
            var clinic = await _clinics.GetByIdAsync(clinicId, cancellationToken);
            var subscription = await _subscriptions.GetByClinicAsync(clinicId, cancellationToken);

            if (clinic is null || subscription is null)
            {
                return Result<PlatformSuspensionChangedDto>.Failure(
                    SubscriptionRefusals.Missing, SubscriptionRefusals.MissingCode);
            }

            if (request.Suspend && subscription.IsSuspended)
            {
                return Result<PlatformSuspensionChangedDto>.Failure(AlreadySuspendedError, AlreadySuspendedCode);
            }

            if (!request.Suspend && !subscription.IsSuspended)
            {
                // Refused rather than answered « c'est fait »: `Unsuspend` on an unsuspended cabinet clears nothing,
                // so a silent success would record an `Unsuspended` row for an action that never happened — and on
                // the fiche it would read as having fixed a read-only cabinet whose real problem is its end date.
                return Result<PlatformSuspensionChangedDto>.Failure(NotSuspendedError, NotSuspendedCode);
            }

            var now = DateTime.UtcNow;

            // Resolved before anything is written: `SuspendedBy` lands on the row the cabinet is judged by, and
            // « nous ne savons pas qui » has to stop the write rather than be discovered while recording it.
            var accountId = PlatformAccessLedger.RequireAccountId(_session);

            if (request.Suspend)
            {
                // `console|{accountId}` through AuditActor's own constant — the same prefix the counter pass's
                // AC-2.2 exclusion reads, so suspending a dormant cabinet cannot make it read as active.
                subscription.Suspend(request.Reason!, AuditActor.Console(accountId).UserId, now);
            }
            else
            {
                subscription.Unsuspend(now);
            }

            await PlatformAccessLedger.RecordAsync(
                _accessEntries,
                _session,
                clinicId,
                clinic.Name,
                request.Suspend ? PlatformAccessAction.Suspended : PlatformAccessAction.Unsuspended,
                now,
                cancellationToken);

            await _subscriptions.UpdateAsync(subscription, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Console account {AccountId} set clinic {ClinicId} suspension to {IsSuspended}",
                accountId, clinicId, subscription.IsSuspended);

            // Read back through the one FR-1 rule rather than asserting the outcome: a lifted suspension can land on
            // a cabinet that is still expired, and only this rule knows that.
            var status = SubscriptionStateReader.Read(
                subscription.EndsOn,
                subscription.IsSuspended,
                ClinicClock.ClinicToday(),
                subscription.LatestCoverKind == SubscriptionPeriodKind.Trial);

            return Result<PlatformSuspensionChangedDto>.Success(new PlatformSuspensionChangedDto(
                ClinicId: clinicId,
                IsSuspended: subscription.IsSuspended,
                EndsOn: status.EndsOn,
                State: status.State.ToString(),
                StateLabel: SubscriptionLabels.State(status.State),
                DaysRemaining: status.DaysRemaining,
                MakesReadOnly: !status.AllowsWrites));
        }
        catch (ArgumentException ex)
        {
            // ClinicSubscription.Suspend's own French guards — a blank motif, or one over its length.
            return Result<PlatformSuspensionChangedDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error setting the suspension of clinic {ClinicId} from the console",
                request.ClinicId);
            return Result<PlatformSuspensionChangedDto>.Failure(
                "Erreur lors de la modification de la suspension du cabinet.");
        }
    }
}
