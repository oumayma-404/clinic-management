using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Platform.Commands;

/// <summary>
/// The vendor records a payment it has received and the cabinet is unlocked (<c>platform-console</c> US-4).
///
/// <para><b>⚠️ Nothing here computes a date</b> (AC-4.2). The entry carries a <i>duration</i> and
/// <c>ClinicSubscription.RecomputeFrom</c> folds the whole ledger, which is what makes « the later of the current
/// end or today » fall out of the arithmetic rather than being restated — and what lets a later cancellation of
/// <i>any</i> entry still move the date. The console introduces no second answer to « until when is this cabinet
/// entitled? », which is the FR-4 violation this whole feature is defined around.</para>
///
/// <para><b>⚠️ It reuses the companion's own write half rather than sending its grant command, and the reason is
/// atomicity.</b> That command commits on its own, so a <c>PlatformAccessEntry</c> written after it would be a
/// second transaction — and a payment recorded with no ledger row behind it is exactly the « an unattributable
/// action must not aboutir » that Part 3 settled for reads. Staging the ledger row and letting
/// <see cref="SubscriptionRefold"/>'s single save carry both is the only shape in which AC-4.7 and AC-7.3 are true
/// of the same instant. The pieces reused — <c>SubscriptionCabinetLookup</c>, <c>SubscriptionPeriod.Create</c>,
/// <c>SubscriptionRefold</c> — are the companion's, so the rules are shared even though the pipeline is not.</para>
///
/// <para><b>⚠️ A repeated submission is one entry</b> (AC-4.6, EC-5), keyed on
/// <see cref="IdempotencyKey"/> and enforced by a <b>unique index</b> on the ledger — not by this handler reading
/// first, which two simultaneous submissions both pass. And two <i>different</i> grants landing together are two
/// entries in an append-only ledger, both kept, with <b>no conflict response</b> (EC-6): the surplus one is
/// corrected by a cancellation, not by refusing the money.</para>
///
/// <para>⚠️ <b>Open-ended cover cannot be granted from here</b>, deliberately: the companion refuses it in its own
/// handler on the grounds that a cabinet which should never expire is grandfathered by a migration rather than
/// granted by a console. EC-14 is met on the <i>read</i> side — « sans échéance » is shown in words.</para>
/// </summary>
public class RecordSubscriptionPeriodCommand : IRequest<Result<PlatformSubscriptionRecordedDto>>
{
    public Guid ClinicId { get; set; }

    /// <summary>
    /// The client's own key for this submission. Optional on the wire and supplied by the console's form, which
    /// mints one per opened sheet — so the second tap of a double-click carries the first tap's key.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>« Offert » (AC-4.8) — a complimentary stretch with no amount, recorded as such rather than as a
    /// payment of zero. « Offert » and « payé 0,000 DT » are different statements.</summary>
    public bool Complimentary { get; set; }

    public int? DurationMonths { get; set; }

    public int? DurationDays { get; set; }

    /// <summary>An inclusive last day named outright (AC-4.1), for what a duration cannot express.</summary>
    public DateTime? EndsOn { get; set; }

    /// <summary><c>Cabinet</c> | <c>Clinique</c> | <c>SurMesure</c>. Optional — a forfait gates nothing (FR-10).</summary>
    public string? Plan { get; set; }

    public decimal? AmountDt { get; set; }

    /// <summary><c>Transfer</c> | <c>Cash</c> | <c>Cheque</c> | <c>Card</c>.</summary>
    public string? Method { get; set; }

    public string? Reference { get; set; }

    public string? Note { get; set; }
}

public class RecordSubscriptionPeriodCommandHandler
    : IRequestHandler<RecordSubscriptionPeriodCommand, Result<PlatformSubscriptionRecordedDto>>
{
    public const string UnknownClinicCode = "clinic_not_found";

    public const string NoDurationError =
        "Indiquez la durée couverte par ce paiement : un nombre de mois, un nombre de jours, ou une date de fin.";

    public const string UnknownPlanError = "Ce forfait n'existe pas.";

    public const string UnknownMethodError = "Ce moyen de paiement n'existe pas.";

    public const string ComplimentaryWithAmountError =
        "Une période offerte ne porte pas de montant : retirez le montant, ou enregistrez-la comme un paiement.";

    private readonly IClinicRepository _clinics;
    private readonly IUserRepository _users;
    private readonly IClinicSubscriptionRepository _subscriptions;
    private readonly IPlatformAccessEntryRepository _accessEntries;
    private readonly IPlatformSessionContext _session;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<RecordSubscriptionPeriodCommandHandler> _logger;

    public RecordSubscriptionPeriodCommandHandler(
        IClinicRepository clinics,
        IUserRepository users,
        IClinicSubscriptionRepository subscriptions,
        IPlatformAccessEntryRepository accessEntries,
        IPlatformSessionContext session,
        IUnitOfWork unitOfWork,
        ITenantScope tenantScope,
        ILogger<RecordSubscriptionPeriodCommandHandler> logger)
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

    public async Task<Result<PlatformSubscriptionRecordedDto>> Handle(
        RecordSubscriptionPeriodCommand request, CancellationToken cancellationToken)
    {
        // EC-12, as on every console path: an undeclared cross-clinic scope reads zero rows with no error, and here
        // that would report every cabinet as unknown.
        PlatformTenantScope.EnsureDeclared(_tenantScope);

        var submissionKey =
            string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey.Trim();

        try
        {
            if (submissionKey is not null
                && await _accessEntries.GetByIdempotencyKeyAsync(submissionKey, cancellationToken)
                    is { } alreadyRecorded)
            {
                return await ReplayAsync(alreadyRecorded, cancellationToken);
            }

            var validated = Validate(request);
            if (validated.IsFailure)
            {
                return Result<PlatformSubscriptionRecordedDto>.FailureFrom(validated);
            }

            // The companion's own « which cabinet » rule, so the console and the five verbs cannot disagree about
            // what identifies a practice — with its refusal carried under a code the screen branches on (AC-4.5).
            var clinicResult = await SubscriptionCabinetLookup.ResolveAsync(
                request.ClinicId, adminEmail: null, _clinics, _users, cancellationToken);

            if (clinicResult.IsFailure)
            {
                return Result<PlatformSubscriptionRecordedDto>.Failure(
                    clinicResult.Error ?? "Cabinet introuvable.", UnknownClinicCode);
            }

            var clinicId = clinicResult.Value;
            var clinic = await _clinics.GetByIdAsync(clinicId, cancellationToken);
            var subscription = await _subscriptions.GetByClinicAsync(clinicId, cancellationToken);

            if (clinic is null || subscription is null)
            {
                // FR-13's failure state, met head-on: this is the endpoint whose purpose is to end a refusal, so it
                // says what is wrong on our side rather than « renouvelez ».
                return Result<PlatformSubscriptionRecordedDto>.Failure(
                    SubscriptionRefusals.Missing, SubscriptionRefusals.MissingCode);
            }

            var previousEndsOn = subscription.EndsOn;
            var now = DateTime.UtcNow;

            // Resolved before anything is built, not left to the ledger's own guard below: `RecordedBy` is written
            // into an append-only entry, so « nous ne savons pas qui » must stop the write rather than reach it.
            var accountId = PlatformAccessLedger.RequireAccountId(_session);

            var entry = SubscriptionPeriod.Create(
                clinicId,
                request.Complimentary ? SubscriptionPeriodKind.Complimentary : SubscriptionPeriodKind.Paid,
                ClinicClock.ClinicToday(),
                now,
                request.DurationMonths,
                request.DurationDays,
                request.EndsOn,
                request.Complimentary ? null : request.AmountDt,
                validated.Value.Method,
                request.Reference,
                request.Note,
                // AC-4.7: `console|{accountId}` through AuditActor's own constant, never a retyped literal — it is
                // the prefix the counter pass's AC-2.2 exclusion reads too. So the grant is answerable in that
                // cabinet's « Journal d'activité » as a vendor action and does not make it read as busy.
                AuditActor.Console(accountId).UserId);

            await _subscriptions.AddEntryAsync(entry, cancellationToken);

            // Staged BEFORE the save, so the ledger row and the payment it records land in one transaction — and,
            // because the key is unique, so does AC-4.6's « one entry per submission ».
            await PlatformAccessLedger.RecordAsync(
                _accessEntries,
                _session,
                clinicId,
                clinic.Name,
                PlatformAccessAction.GrantedPeriod,
                now,
                cancellationToken,
                subscriptionPeriodId: entry.Id,
                idempotencyKey: submissionKey);

            var saved = await SubscriptionRefold.SaveAsync(
                clinicId, subscription, entry, validated.Value.Plan,
                _subscriptions, _unitOfWork, _logger, cancellationToken);

            if (saved.IsFailure)
            {
                return Result<PlatformSubscriptionRecordedDto>.FailureFrom(saved);
            }

            _logger.LogInformation(
                "Console account {AccountId} recorded subscription period {EntryId} for clinic {ClinicId}; "
                + "entitlement now ends {EndsOn}",
                _session.GetAccountId(), entry.Id, clinicId, saved.Value);

            return Result<PlatformSubscriptionRecordedDto>.Success(
                Recorded(clinicId, entry.Id, previousEndsOn, saved.Value, subscription.IsSuspended,
                    subscription.LatestCoverKind, alreadyRecorded: false));
        }
        catch (DbUpdateException ex) when (submissionKey is not null)
        {
            // EC-5's other half. Two identical submissions both read « rien encore enregistré » and both insert; the
            // unique index refuses the second. That is not an error to show — it is the first submission's answer,
            // which is exactly what AC-4.6 asks for. An unkeyed submission falls through to the generic branch,
            // since there is nothing to replay and the failure is then a real one.
            _logger.LogInformation(
                ex, "A repeated submission lost the race on key {Key}; replaying the first", submissionKey);

            var winner = await _accessEntries.GetByIdempotencyKeyAsync(submissionKey, cancellationToken);
            return winner is null
                ? Result<PlatformSubscriptionRecordedDto>.Failure("Erreur lors de l'enregistrement du paiement.")
                : await ReplayAsync(winner, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            // SubscriptionPeriod.Create's own French guards: two duration forms, a negative amount, an over-long
            // reference or note (AC-4.5).
            return Result<PlatformSubscriptionRecordedDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error recording a subscription period from the console");
            return Result<PlatformSubscriptionRecordedDto>.Failure(
                "Erreur lors de l'enregistrement du paiement.");
        }
    }

    /// <summary>
    /// AC-4.6's answer to a submission already recorded: the <b>first</b> outcome, re-read, and flagged so the
    /// screen can say « déjà enregistré » rather than claiming to have taken the money twice.
    /// </summary>
    private async Task<Result<PlatformSubscriptionRecordedDto>> ReplayAsync(
        PlatformAccessEntry recorded, CancellationToken cancellationToken)
    {
        var subscription = await _subscriptions.GetByClinicAsync(recorded.ClinicId, cancellationToken);
        if (subscription is null)
        {
            return Result<PlatformSubscriptionRecordedDto>.Failure(
                SubscriptionRefusals.Missing, SubscriptionRefusals.MissingCode);
        }

        // ⚠️ `PreviousEndsOn` is null on a replay rather than a guess: what the date was before the first
        // submission is not recoverable afterwards, and inventing it would make EC-3's « paying early never costs
        // days » read as a period that moved by nothing.
        return Result<PlatformSubscriptionRecordedDto>.Success(
            Recorded(recorded.ClinicId, recorded.SubscriptionPeriodId, previousEndsOn: null, subscription.EndsOn,
                subscription.IsSuspended, subscription.LatestCoverKind, alreadyRecorded: true));
    }

    private static PlatformSubscriptionRecordedDto Recorded(
        Guid clinicId,
        Guid? entryId,
        DateTime? previousEndsOn,
        DateTime? endsOn,
        bool isSuspended,
        SubscriptionPeriodKind? latestCoverKind,
        bool alreadyRecorded)
    {
        // AC-4.3: the console shows the new state and end date immediately, and it reads them from the one FR-1
        // rule rather than inferring « c'est payé, donc actif » — a suspended cabinet stays suspended after a
        // payment, and telling the vendor otherwise would be the worst possible moment to be wrong.
        var status = SubscriptionStateReader.Read(
            endsOn, isSuspended, ClinicClock.ClinicToday(), latestCoverKind == SubscriptionPeriodKind.Trial);

        return new PlatformSubscriptionRecordedDto(
            ClinicId: clinicId,
            EntryId: entryId,
            PreviousEndsOn: previousEndsOn,
            EndsOn: status.EndsOn,
            State: status.State.ToString(),
            StateLabel: SubscriptionLabels.State(status.State),
            DaysRemaining: status.DaysRemaining,
            AlreadyRecorded: alreadyRecorded);
    }

    /// <summary>
    /// AC-4.5's refusals, and the two closed vocabularies parsed once. An unknown forfait or method is <b>refused</b>
    /// rather than ignored — unlike a filter, where a stale value should narrow nothing: this one is a fact being
    /// written into a ledger nobody can edit afterwards.
    /// </summary>
    private static Result<(SubscriptionPlan? Plan, SubscriptionPaymentMethod? Method)> Validate(
        RecordSubscriptionPeriodCommand request)
    {
        var forms = (request.DurationMonths.HasValue ? 1 : 0)
                    + (request.DurationDays.HasValue ? 1 : 0)
                    + (request.EndsOn.HasValue ? 1 : 0);

        if (forms == 0)
        {
            return Result<(SubscriptionPlan?, SubscriptionPaymentMethod?)>.Failure(NoDurationError);
        }

        if (request.Complimentary && request.AmountDt is not null)
        {
            return Result<(SubscriptionPlan?, SubscriptionPaymentMethod?)>.Failure(ComplimentaryWithAmountError);
        }

        SubscriptionPlan? plan = null;
        if (!string.IsNullOrWhiteSpace(request.Plan))
        {
            if (!Enum.TryParse<SubscriptionPlan>(request.Plan.Trim(), ignoreCase: true, out var parsedPlan))
            {
                return Result<(SubscriptionPlan?, SubscriptionPaymentMethod?)>.Failure(UnknownPlanError);
            }

            plan = parsedPlan;
        }

        SubscriptionPaymentMethod? method = null;
        if (!string.IsNullOrWhiteSpace(request.Method))
        {
            if (!Enum.TryParse<SubscriptionPaymentMethod>(request.Method.Trim(), ignoreCase: true, out var parsedMethod))
            {
                return Result<(SubscriptionPlan?, SubscriptionPaymentMethod?)>.Failure(UnknownMethodError);
            }

            method = parsedMethod;
        }

        return Result<(SubscriptionPlan?, SubscriptionPaymentMethod?)>.Success((plan, method));
    }
}
