using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Subscriptions.Commands;

/// <summary>The resulting flag and the date it stands on — a lifted suspension does not extend anything.</summary>
public sealed record SubscriptionSuspensionResult(
    Guid ClinicId,
    bool IsSuspended,
    DateTime? EndsOn);

/// <summary>
/// Suspends a cabinet with a mandatory written reason, or lifts the suspension (FR-7).
///
/// <para><b>⚠️ Suspension is for abuse or fraud, and non-payment is not suspension</b> — non-payment is the absence
/// of a grant and expresses itself as expiry. That is why this command does not touch the ledger at all: paying does
/// not lift a suspension, and lifting one grants no time. A suspended cabinet then stands on its own end date, which
/// may itself already be in the past.</para>
///
/// <para>The reason is mandatory because <c>Suspendu</c> deliberately outranks <c>Expiré</c> on the cabinet's own
/// screen (EC-11): the practice is told it is suspended rather than lapsed, so « suspended why? » must be
/// answerable or there is nothing for it to act on.</para>
/// </summary>
public class SetSubscriptionSuspensionCommand : IRequest<Result<SubscriptionSuspensionResult>>
{
    public Guid? ClinicId { get; set; }

    public string? AdminEmail { get; set; }

    public bool Suspend { get; set; }

    /// <summary>Mandatory when suspending; ignored when lifting, which clears the whole trail.</summary>
    public string? Reason { get; set; }

    public string? ActedBy { get; set; }
}

public class SetSubscriptionSuspensionCommandHandler
    : IRequestHandler<SetSubscriptionSuspensionCommand, Result<SubscriptionSuspensionResult>>
{
    public const string ReasonRequiredError = "Le motif de suspension est obligatoire (--reason).";

    private readonly IClinicSubscriptionRepository _subscriptions;
    private readonly IClinicRepository _clinics;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SetSubscriptionSuspensionCommandHandler> _logger;

    public SetSubscriptionSuspensionCommandHandler(
        IClinicSubscriptionRepository subscriptions,
        IClinicRepository clinics,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        ILogger<SetSubscriptionSuspensionCommandHandler> logger)
    {
        _subscriptions = subscriptions;
        _clinics = clinics;
        _users = users;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<SubscriptionSuspensionResult>> Handle(
        SetSubscriptionSuspensionCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.Suspend && string.IsNullOrWhiteSpace(request.Reason))
            {
                return Result<SubscriptionSuspensionResult>.Failure(ReasonRequiredError);
            }

            var clinicResult = await SubscriptionCabinetLookup.ResolveAsync(
                request.ClinicId, request.AdminEmail, _clinics, _users, cancellationToken);

            if (clinicResult.IsFailure)
            {
                return Result<SubscriptionSuspensionResult>.FailureFrom(clinicResult);
            }

            var clinicId = clinicResult.Value;
            var subscription = await _subscriptions.GetByClinicAsync(clinicId, cancellationToken);
            if (subscription is null)
            {
                return Result<SubscriptionSuspensionResult>.Failure(
                    SubscriptionRefusals.Missing, SubscriptionRefusals.MissingCode);
            }

            var now = DateTime.UtcNow;
            if (request.Suspend)
            {
                subscription.Suspend(request.Reason!, request.ActedBy, now);
            }
            else
            {
                subscription.Unsuspend(now);
            }

            // No re-fold: the ledger is untouched, so EndsOn cannot have changed and there is no concurrent-grant
            // convergence to reach. A lost update here would be an ordinary conflict, and 409 is the right answer.
            await _subscriptions.UpdateAsync(subscription, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Clinic {ClinicId} suspension set to {IsSuspended}", clinicId, subscription.IsSuspended);

            return Result<SubscriptionSuspensionResult>.Success(
                new SubscriptionSuspensionResult(clinicId, subscription.IsSuspended, subscription.EndsOn));
        }
        catch (ArgumentException ex)
        {
            return Result<SubscriptionSuspensionResult>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error setting the clinic subscription suspension.");
            return Result<SubscriptionSuspensionResult>.Failure(
                "Erreur lors de la modification de la suspension de l'abonnement.");
        }
    }
}
