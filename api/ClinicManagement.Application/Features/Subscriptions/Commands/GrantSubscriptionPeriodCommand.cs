using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Subscriptions.Commands;

/// <summary>What the vendor gets back after recording a payment — enough to read the outcome off the console.</summary>
/// <param name="PreviousEndsOn">The date before the grant, so EC-3 (« paying early never costs days ») is legible.</param>
public sealed record SubscriptionGrantResult(
    Guid ClinicId,
    Guid EntryId,
    DateTime? PreviousEndsOn,
    DateTime? EndsOn);

/// <summary>
/// Records a received payment against one cabinet and extends its entitlement (US-5, AC-5.1/5.2/5.3).
///
/// <para><b>There is no HTTP path to this command and there must not be</b> (FR-6): a cabinet able to extend its own
/// entitlement would not have one. It is reached only from the <c>subscription-grant</c> console verb, which builds
/// its container from <c>AddInfrastructure</c> alone and therefore constructs this handler directly — there is no
/// mediator in a verb. It is a MediatR command all the same, so the companion vendor console can send it unchanged.</para>
///
/// <para><b>⚠️ Nothing here computes a date.</b> The entry carries a <i>duration</i> and
/// <c>ClinicSubscription.RecomputeFrom</c> folds the whole ledger — which is what makes AC-5.2's
/// later-of-current-end-or-today fall out of the arithmetic instead of being restated here, and what makes
/// cancelling <i>any</i> entry later able to move the date (AC-5.4).</para>
/// </summary>
public class GrantSubscriptionPeriodCommand : IRequest<Result<SubscriptionGrantResult>>
{
    /// <summary>The cabinet, by id or by the e-mail of somebody who works there (AC-5.1).</summary>
    public Guid? ClinicId { get; set; }

    public string? AdminEmail { get; set; }

    /// <summary>Why the cabinet is covered. <c>Paid</c> for a payment, <c>Complimentary</c> for a gesture.</summary>
    public SubscriptionPeriodKind Kind { get; set; } = SubscriptionPeriodKind.Paid;

    public int? DurationMonths { get; set; }

    public int? DurationDays { get; set; }

    public DateTime? ExplicitEndsOn { get; set; }

    public SubscriptionPlan? Plan { get; set; }

    public decimal? Amount { get; set; }

    public SubscriptionPaymentMethod? Method { get; set; }

    public string? Reference { get; set; }

    public string? Note { get; set; }

    /// <summary>Who recorded it — the verb passes <c>job|&lt;command&gt;</c>, as the audit ledger does (FR-12).</summary>
    public string? RecordedBy { get; set; }
}

public class GrantSubscriptionPeriodCommandHandler
    : IRequestHandler<GrantSubscriptionPeriodCommand, Result<SubscriptionGrantResult>>
{
    public const string NoDurationError =
        "Indiquez la durée du paiement : --months <mois>, --days <jours> ou --until <AAAA-MM-JJ>.";

    private readonly IClinicSubscriptionRepository _subscriptions;
    private readonly IClinicRepository _clinics;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<GrantSubscriptionPeriodCommandHandler> _logger;

    public GrantSubscriptionPeriodCommandHandler(
        IClinicSubscriptionRepository subscriptions,
        IClinicRepository clinics,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        ILogger<GrantSubscriptionPeriodCommandHandler> logger)
    {
        _subscriptions = subscriptions;
        _clinics = clinics;
        _users = users;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<SubscriptionGrantResult>> Handle(
        GrantSubscriptionPeriodCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // An entry with no duration form at all is « sans échéance » — permanent free cover. Reachable by
            // forgetting one flag, and unnoticeable afterwards, so it is refused rather than merely undocumented:
            // a cabinet that should never expire is grandfathered by the migration, not granted from the console.
            if (request.DurationMonths is null && request.DurationDays is null && request.ExplicitEndsOn is null)
            {
                return Result<SubscriptionGrantResult>.Failure(NoDurationError);
            }

            var clinicResult = await SubscriptionCabinetLookup.ResolveAsync(
                request.ClinicId, request.AdminEmail, _clinics, _users, cancellationToken);

            if (clinicResult.IsFailure)
            {
                return Result<SubscriptionGrantResult>.FailureFrom(clinicResult);
            }

            var clinicId = clinicResult.Value;
            var subscription = await _subscriptions.GetByClinicAsync(clinicId, cancellationToken);
            if (subscription is null)
            {
                return Result<SubscriptionGrantResult>.Failure(
                    SubscriptionRefusals.Missing, SubscriptionRefusals.MissingCode);
            }

            var previousEndsOn = subscription.EndsOn;
            var now = DateTime.UtcNow;

            // The entry's own recorded clinic-day is the fold's anchor, which is what lets the fold read no clock.
            var entry = SubscriptionPeriod.Create(
                clinicId,
                request.Kind,
                ClinicClock.ClinicToday(),
                now,
                request.DurationMonths,
                request.DurationDays,
                request.ExplicitEndsOn,
                request.Amount,
                request.Method,
                request.Reference,
                request.Note,
                request.RecordedBy);

            await _subscriptions.AddEntryAsync(entry, cancellationToken);

            var saved = await SubscriptionRefold.SaveAsync(
                clinicId, subscription, entry, request.Plan,
                _subscriptions, _unitOfWork, _logger, cancellationToken);

            if (saved.IsFailure)
            {
                return Result<SubscriptionGrantResult>.FailureFrom(saved);
            }

            _logger.LogInformation(
                "Recorded a {Kind} subscription period {EntryId} for clinic {ClinicId}; entitlement now ends {EndsOn}",
                request.Kind, entry.Id, clinicId, saved.Value);

            return Result<SubscriptionGrantResult>.Success(
                new SubscriptionGrantResult(clinicId, entry.Id, previousEndsOn, saved.Value));
        }
        catch (ArgumentException ex)
        {
            // SubscriptionPeriod.Create's own French guards: a non-positive duration (AC-5.7), two duration forms,
            // a negative amount, an over-long reference or note.
            return Result<SubscriptionGrantResult>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error recording a subscription period.");
            return Result<SubscriptionGrantResult>.Failure(
                "Erreur lors de l'enregistrement de la période d'abonnement.");
        }
    }
}
