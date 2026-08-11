using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Subscriptions.Queries;

/// <summary>
/// Where this cabinet stands, and how to pay (US-2, AC-2.1).
///
/// <para><b>Readable by every role, including a secretary</b> (AC-2.2) — the person who meets a refused save is
/// often not the person who pays, and EC-10 depends on her being able to read why. The gate is the controller's
/// <c>AnyClinicRole</c>; what stays admin-only is the payment <i>history</i>
/// (<see cref="GetSubscriptionHistoryQuery"/>), not the screen.</para>
///
/// <para><b>⚠️ It reads the ledger, and the gate deliberately does not.</b> The one thing the entitlement row alone
/// cannot say is whether the cover in force is the free trial — that needs the fold — and
/// <c>SubscriptionStateReader</c> therefore takes <c>isTrial</c> as a parameter rather than deriving it. The gate
/// runs on every write and must stay one indexed row; this runs when somebody opens a screen.</para>
/// </summary>
public class GetSubscriptionQuery : IRequest<Result<SubscriptionDto>>
{
}

public class GetSubscriptionQueryHandler : IRequestHandler<GetSubscriptionQuery, Result<SubscriptionDto>>
{
    private readonly IClinicSubscriptionRepository _subscriptions;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ISubscriptionPricing _pricing;
    private readonly ILogger<GetSubscriptionQueryHandler> _logger;

    public GetSubscriptionQueryHandler(
        IClinicSubscriptionRepository subscriptions,
        ICurrentClinicResolver clinicResolver,
        ISubscriptionPricing pricing,
        ILogger<GetSubscriptionQueryHandler> logger)
    {
        _subscriptions = subscriptions;
        _clinicResolver = clinicResolver;
        _pricing = pricing;
        _logger = logger;
    }

    public async Task<Result<SubscriptionDto>> Handle(
        GetSubscriptionQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<SubscriptionDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var clinicId = clinicResult.Value;
            var subscription = await _subscriptions.GetByClinicAsync(clinicId, cancellationToken);

            if (subscription is null)
            {
                // A fault on our side, not a lapse on the cabinet's (EC-6), so it carries the gate's own sentence
                // and code rather than a generic error — « nous le rétablissons », never « renouvelez ». The screen
                // shows it as a retryable state, which is also EC-13's requirement: a failed read must never render
                // as « aucun abonnement ».
                return Result<SubscriptionDto>.Failure(
                    SubscriptionRefusals.Missing, SubscriptionRefusals.MissingCode);
            }

            var today = ClinicClock.ClinicToday();
            var entries = await _subscriptions.GetEntriesAsync(clinicId, cancellationToken);
            var status = SubscriptionStateReader.Read(
                subscription, today, SubscriptionTrial.IsOnTrial(entries, today));

            return Result<SubscriptionDto>.Success(new SubscriptionDto
            {
                State = status.State.ToString(),
                StateLabel = SubscriptionLabels.State(status.State),
                Plan = subscription.Plan?.ToString(),
                PlanLabel = subscription.Plan is { } plan ? SubscriptionLabels.Plan(plan) : null,
                EndsOn = status.EndsOn,
                DaysRemaining = status.DaysRemaining,
                AllowsWrites = status.AllowsWrites,
                ShouldWarn = status.ShouldWarn,
                SuspensionReason = subscription.SuspensionReason,
                PriceMonthlyDt = subscription.Plan is { } monthly ? _pricing.MonthlyPrice(monthly) : null,
                PriceAnnualDt = subscription.Plan is { } annual ? _pricing.AnnualPrice(annual) : null,
                Plans = PublishedTariff(),
                PaymentInstructions = _pricing.PaymentInstructions,
                ContactEmail = _pricing.ContactEmail,
                ContactPhone = _pricing.ContactPhone,
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error reading the clinic subscription.");
            return Result<SubscriptionDto>.Failure("Erreur lors de la lecture de l'abonnement.");
        }
    }

    /// <summary>
    /// The deployment's published tariff, every forfait present in enum order.
    ///
    /// <para>Zeros and blanks are <b>not</b> filled in: an unpublished price reads as absent — « sur devis » — which
    /// is a true statement about a deployment that has not filled the section in, where « 0,000 DT » is not. And a
    /// forfait with no figure is still listed, for the same reason la caisse prints « Espèces 0,000 »: an absent row
    /// is not a statement.</para>
    /// </summary>
    private List<SubscriptionPlanPriceDto> PublishedTariff() =>
        Enum.GetValues<SubscriptionPlan>()
            .Select(plan => new SubscriptionPlanPriceDto
            {
                Plan = plan.ToString(),
                Label = SubscriptionLabels.Plan(plan),
                PriceMonthlyDt = _pricing.MonthlyPrice(plan),
                PriceAnnualDt = _pricing.AnnualPrice(plan),
            })
            .ToList();
}
