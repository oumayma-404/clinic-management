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
/// The vendor corrects a mistake (<c>platform-console</c> US-5): one ledger entry is cancelled with a written
/// reason, and the cabinet's end date recomputes — possibly into the past.
///
/// <para><b>⚠️ The entry is never edited and never deleted</b> (AC-5.2). It stays in the ledger, struck through,
/// carrying its motif, its canceller and the moment — which is what lets « what were we paid, and for what? » still
/// be answered a year later, on the one screen whose purpose is to check that. Nothing here removes a row.</para>
///
/// <para><b>⚠️ It reuses the companion's pieces rather than sending its cancel command, and the reason is
/// atomicity</b> — the shape Part 4 settled (progress.md DEV-14). <c>CancelSubscriptionPeriodCommand</c> commits on
/// its own, so the FR-5 access-ledger row would be a second transaction, and a correction recorded with no ledger
/// row behind it is the « an unattributable action must not aboutir » that Part 3 settled for reads. Staging the
/// ledger row before <see cref="SubscriptionRefold"/>'s single save is the only shape in which AC-5.1 and AC-7.3 are
/// true of the same instant.</para>
///
/// <para><b>⚠️ No date is computed here.</b> <c>ClinicSubscription.RecomputeFrom</c> stays the only writer of
/// <c>EndsOn</c> (AC-4.2), which is also why cancelling a <i>middle</i> entry correctly shortens every stretch after
/// it: the fold re-runs over what remains rather than subtracting a duration from a running total.</para>
///
/// <para>⚠️ <b>« Déjà annulée » is a refusal and not a silent success</b>, deliberately. Unlike a grant — where a
/// repeated submission is the vendor's own double-click and replaying the first outcome is what they wanted
/// (AC-4.6) — an entry already struck through was struck through by <i>somebody</i>, and which colleague and for
/// what motif is on the screen the refusal sends the reader back to. It carries
/// <see cref="CancelSubscriptionPeriodFromConsoleCommandHandler.AlreadyCancelledCode"/> so the console can present
/// it as a state of the world rather than as a failed action.</para>
/// </summary>
public class CancelSubscriptionPeriodFromConsoleCommand : IRequest<Result<PlatformSubscriptionCancelledDto>>
{
    public Guid ClinicId { get; set; }

    /// <summary>The entry to strike through, located <b>within this cabinet's own ledger</b>.</summary>
    public Guid EntryId { get; set; }

    /// <summary>Mandatory (AC-5.1): the date can move into the past, so « why » must be answerable afterwards.</summary>
    public string Reason { get; set; } = string.Empty;
}

public class CancelSubscriptionPeriodFromConsoleCommandHandler
    : IRequestHandler<CancelSubscriptionPeriodFromConsoleCommand, Result<PlatformSubscriptionCancelledDto>>
{
    public const string UnknownClinicCode = "clinic_not_found";

    public const string UnknownEntryCode = "period_not_found";

    public const string AlreadyCancelledCode = "period_already_cancelled";

    public const string ReasonRequiredError =
        "Indiquez le motif de l'annulation : il reste inscrit sur la période et explique, plus tard, pourquoi la "
        + "couverture du cabinet a été raccourcie.";

    public const string AlreadyCancelledError =
        "Cette période est déjà annulée. Son motif, son auteur et sa date figurent sur la fiche du cabinet.";

    private readonly IClinicRepository _clinics;
    private readonly IUserRepository _users;
    private readonly IClinicSubscriptionRepository _subscriptions;
    private readonly IPlatformAccessEntryRepository _accessEntries;
    private readonly IPlatformSessionContext _session;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<CancelSubscriptionPeriodFromConsoleCommandHandler> _logger;

    public CancelSubscriptionPeriodFromConsoleCommandHandler(
        IClinicRepository clinics,
        IUserRepository users,
        IClinicSubscriptionRepository subscriptions,
        IPlatformAccessEntryRepository accessEntries,
        IPlatformSessionContext session,
        IUnitOfWork unitOfWork,
        ITenantScope tenantScope,
        ILogger<CancelSubscriptionPeriodFromConsoleCommandHandler> logger)
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

    public async Task<Result<PlatformSubscriptionCancelledDto>> Handle(
        CancelSubscriptionPeriodFromConsoleCommand request, CancellationToken cancellationToken)
    {
        // EC-12, as on every console path: an undeclared cross-clinic scope reads zero rows with no error, and here
        // that would report every cabinet — and every entry — as unknown.
        PlatformTenantScope.EnsureDeclared(_tenantScope);

        try
        {
            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                return Result<PlatformSubscriptionCancelledDto>.Failure(ReasonRequiredError);
            }

            // The companion's own « which cabinet » rule, so the console and the five verbs cannot disagree about
            // what identifies a practice — refused under a code the screen branches on.
            var clinicResult = await SubscriptionCabinetLookup.ResolveAsync(
                request.ClinicId, adminEmail: null, _clinics, _users, cancellationToken);

            if (clinicResult.IsFailure)
            {
                return Result<PlatformSubscriptionCancelledDto>.Failure(
                    clinicResult.Error ?? "Cabinet introuvable.", UnknownClinicCode);
            }

            var clinicId = clinicResult.Value;
            var clinic = await _clinics.GetByIdAsync(clinicId, cancellationToken);
            var subscription = await _subscriptions.GetByClinicAsync(clinicId, cancellationToken);

            if (clinic is null || subscription is null)
            {
                return Result<PlatformSubscriptionCancelledDto>.Failure(
                    SubscriptionRefusals.Missing, SubscriptionRefusals.MissingCode);
            }

            // Located within the cabinet's own ledger rather than fetched by id, exactly as the companion does it:
            // an entry belonging to another practice is then structurally unreachable rather than checked for.
            var entries = await _subscriptions.GetEntriesAsync(clinicId, cancellationToken);
            var entry = entries.FirstOrDefault(e => e.Id == request.EntryId);

            if (entry is null)
            {
                return Result<PlatformSubscriptionCancelledDto>.Failure(
                    "Cette période ne figure pas dans le journal d'abonnement de ce cabinet.", UnknownEntryCode);
            }

            if (entry.IsCancelled)
            {
                return Result<PlatformSubscriptionCancelledDto>.Failure(
                    AlreadyCancelledError, AlreadyCancelledCode);
            }

            var previousEndsOn = subscription.EndsOn;
            var now = DateTime.UtcNow;

            // Resolved before anything is written: `CancelledBy` lands on a row nobody can edit afterwards, so
            // « nous ne savons pas qui » has to stop the correction rather than be discovered while recording it.
            var accountId = PlatformAccessLedger.RequireAccountId(_session);

            // AC-5.1's motif and AC-7.3's actor, on the entry itself — `console|{accountId}` through AuditActor's own
            // constant, which is also the prefix the counter pass's AC-2.2 exclusion reads.
            entry.Cancel(request.Reason, AuditActor.Console(accountId).UserId, now);

            await PlatformAccessLedger.RecordAsync(
                _accessEntries,
                _session,
                clinicId,
                clinic.Name,
                PlatformAccessAction.CancelledPeriod,
                now,
                cancellationToken,
                subscriptionPeriodId: entry.Id);

            // No pending entry: the cancelled row is already in the ledger the re-fold reads.
            var saved = await SubscriptionRefold.SaveAsync(
                clinicId, subscription, pendingEntry: null, plan: null,
                _subscriptions, _unitOfWork, _logger, cancellationToken);

            if (saved.IsFailure)
            {
                return Result<PlatformSubscriptionCancelledDto>.FailureFrom(saved);
            }

            _logger.LogInformation(
                "Console account {AccountId} cancelled subscription period {EntryId} for clinic {ClinicId}; "
                + "entitlement now ends {EndsOn}",
                accountId, entry.Id, clinicId, saved.Value);

            // Read back through the one FR-1 rule rather than trusting the preview the vendor confirmed: the ledger
            // may have moved between the page render and the click, and this is the answer that is true now.
            var status = SubscriptionStateReader.Read(
                saved.Value,
                subscription.IsSuspended,
                ClinicClock.ClinicToday(),
                subscription.LatestCoverKind == SubscriptionPeriodKind.Trial);

            return Result<PlatformSubscriptionCancelledDto>.Success(new PlatformSubscriptionCancelledDto(
                ClinicId: clinicId,
                EntryId: entry.Id,
                PreviousEndsOn: previousEndsOn,
                EndsOn: status.EndsOn,
                State: status.State.ToString(),
                StateLabel: SubscriptionLabels.State(status.State),
                DaysRemaining: status.DaysRemaining,
                MakesReadOnly: !status.AllowsWrites));
        }
        catch (ArgumentException ex)
        {
            // SubscriptionPeriod.Cancel's own French guards — an empty motif, or one over its length.
            return Result<PlatformSubscriptionCancelledDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error cancelling a subscription period from the console");
            return Result<PlatformSubscriptionCancelledDto>.Failure(
                "Erreur lors de l'annulation de la période d'abonnement.");
        }
    }
}
