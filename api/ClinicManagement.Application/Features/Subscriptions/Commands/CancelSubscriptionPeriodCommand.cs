using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Subscriptions.Commands;

/// <summary>The corrected state, so the operator can see the date move — possibly into the past (EC-4).</summary>
public sealed record SubscriptionCancelResult(
    Guid ClinicId,
    Guid EntryId,
    DateTime? PreviousEndsOn,
    DateTime? EndsOn);

/// <summary>
/// Voids one ledger entry with a written reason (AC-5.5). The row is <b>kept</b>, struck through, and the end date
/// recomputes from the entries that remain — which is how a grant recorded against the wrong practice is corrected
/// (EC-4), the date possibly returning to the past and the cabinet becoming read-only again.
///
/// <para><b>⚠️ Cancelling a <i>middle</i> entry has to move the date, and that is the whole reason the ledger stores
/// durations rather than windows</b> (FR-2). With absolute stored windows a mis-keyed 12-month grant followed by a
/// correct one would keep all 24 months after the wrong one was voided, because the later window's end is still the
/// maximum. Here the full re-fold removes exactly that entry's days wherever it sits.</para>
///
/// <para>The entry is located <b>within the cabinet's own ledger</b> rather than fetched by id, so an entry
/// belonging to another cabinet is structurally unreachable rather than checked for.</para>
/// </summary>
public class CancelSubscriptionPeriodCommand : IRequest<Result<SubscriptionCancelResult>>
{
    public Guid? ClinicId { get; set; }

    public string? AdminEmail { get; set; }

    public Guid EntryId { get; set; }

    /// <summary>Mandatory (AC-5.5) — the date can move into the past, so « why » must be answerable afterwards.</summary>
    public string Reason { get; set; } = string.Empty;

    public string? CancelledBy { get; set; }
}

public class CancelSubscriptionPeriodCommandHandler
    : IRequestHandler<CancelSubscriptionPeriodCommand, Result<SubscriptionCancelResult>>
{
    public const string ReasonRequiredError = "Le motif d'annulation est obligatoire (--reason).";
    public const string EntryRequiredError = "Indiquez la période à annuler (--entry <identifiant>).";

    private readonly IClinicSubscriptionRepository _subscriptions;
    private readonly IClinicRepository _clinics;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CancelSubscriptionPeriodCommandHandler> _logger;

    public CancelSubscriptionPeriodCommandHandler(
        IClinicSubscriptionRepository subscriptions,
        IClinicRepository clinics,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        ILogger<CancelSubscriptionPeriodCommandHandler> logger)
    {
        _subscriptions = subscriptions;
        _clinics = clinics;
        _users = users;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<SubscriptionCancelResult>> Handle(
        CancelSubscriptionPeriodCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                return Result<SubscriptionCancelResult>.Failure(ReasonRequiredError);
            }

            if (request.EntryId == Guid.Empty)
            {
                return Result<SubscriptionCancelResult>.Failure(EntryRequiredError);
            }

            var clinicResult = await SubscriptionCabinetLookup.ResolveAsync(
                request.ClinicId, request.AdminEmail, _clinics, _users, cancellationToken);

            if (clinicResult.IsFailure)
            {
                return Result<SubscriptionCancelResult>.FailureFrom(clinicResult);
            }

            var clinicId = clinicResult.Value;
            var subscription = await _subscriptions.GetByClinicAsync(clinicId, cancellationToken);
            if (subscription is null)
            {
                return Result<SubscriptionCancelResult>.Failure(
                    SubscriptionRefusals.Missing, SubscriptionRefusals.MissingCode);
            }

            var entries = await _subscriptions.GetEntriesAsync(clinicId, cancellationToken);
            var entry = entries.FirstOrDefault(e => e.Id == request.EntryId);
            if (entry is null)
            {
                return Result<SubscriptionCancelResult>.Failure(
                    $"Aucune période {request.EntryId} dans le journal de ce cabinet.");
            }

            if (entry.IsCancelled)
            {
                return Result<SubscriptionCancelResult>.Failure("Cette période d'abonnement est déjà annulée.");
            }

            var previousEndsOn = subscription.EndsOn;
            entry.Cancel(request.Reason, request.CancelledBy, DateTime.UtcNow);

            // Named as this command's own entry although it is already in the ledger the re-fold reads: the append
            // is skipped by id, and naming it is what keeps the retry from detaching the unsaved cancellation.
            var saved = await SubscriptionRefold.SaveAsync(
                clinicId, subscription, pendingEntry: entry, plan: null,
                _subscriptions, _unitOfWork, _logger, cancellationToken);

            if (saved.IsFailure)
            {
                return Result<SubscriptionCancelResult>.FailureFrom(saved);
            }

            _logger.LogInformation(
                "Cancelled subscription period {EntryId} for clinic {ClinicId}; entitlement now ends {EndsOn}",
                entry.Id, clinicId, saved.Value);

            return Result<SubscriptionCancelResult>.Success(
                new SubscriptionCancelResult(clinicId, entry.Id, previousEndsOn, saved.Value));
        }
        catch (Exception ex) when (SubscriptionRefusals.IsDomainRefusal(ex))
        {
            // Both kinds: the domain throws its French guards through ArgumentException AND
            // InvalidOperationException, and a genuine programming fault falls through to the log below.
            return Result<SubscriptionCancelResult>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error cancelling a subscription period.");
            return Result<SubscriptionCancelResult>.Failure(
                "Erreur lors de l'annulation de la période d'abonnement.");
        }
    }
}
