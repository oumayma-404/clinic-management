using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Subscriptions;

/// <summary>
/// The write half every vendor command shares: re-fold the cabinet's <b>whole</b> ledger onto its entitlement and
/// commit, in one save (AC-5.4). Shared rather than copied because the retry below is subtle and a second copy is
/// the one that would be missing it.
///
/// <para><b>⚠️ EC-5 is why the retry exists, and it is not the usual conflict handling.</b> Two grants recorded at
/// the same moment must <i>both</i> land and both be kept — « reporting a conflict here would promise an outcome
/// this ledger cannot produce ». But <c>Entity.Version</c> is mapped onto <c>xmin</c>, so the second writer's
/// <c>UPDATE … WHERE xmin = &lt;loaded&gt;</c> matches nothing and raises <see cref="ConflictException"/> → 409.
/// Retrying is <b>correct here specifically because <c>EndsOn</c> is derived</b>: whoever saves last recomputes the
/// same date from every entry, so the loop converges rather than papering over a lost update. On an ordinary
/// aggregate this would be exactly the wrong thing to do.</para>
///
/// <para>Each attempt is still <b>one</b> save, so no state is ever half-applied: a failed attempt rolls back the
/// staged entry with it and the next attempt re-inserts it once.</para>
/// </summary>
public static class SubscriptionRefold
{
    /// <summary>Bounds the loop, on <c>IssueInvoiceCommand</c>'s recompute-and-retry precedent.</summary>
    public const int MaxAttempts = 5;

    public const string ExhaustedError =
        "Impossible d'enregistrer l'abonnement : le cabinet a été modifié simultanément à plusieurs reprises. "
        + "Réessayez.";

    /// <param name="pendingEntry">
    /// A grant's own entry, already staged through <c>AddEntryAsync</c> and therefore <b>not</b> yet visible to
    /// <c>GetEntriesAsync</c> — so it is appended to what the fold sees. Null for a cancellation, whose entry is
    /// already in the ledger.
    /// </param>
    /// <param name="plan">
    /// AC-5.1's optional forfait, applied on every attempt because a reload replaces the instance it was set on.
    /// </param>
    /// <returns>The entitlement's new inclusive end day, or null for « sans échéance ».</returns>
    public static async Task<Result<DateTime?>> SaveAsync(
        Guid clinicId,
        ClinicSubscription subscription,
        SubscriptionPeriod? pendingEntry,
        SubscriptionPlan? plan,
        IClinicSubscriptionRepository subscriptions,
        IUnitOfWork unitOfWork,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var entries = await subscriptions.GetEntriesAsync(clinicId, cancellationToken);

            // Appended only when the read did not already return it. A staged entry is invisible to an EF query, so
            // in production it never does — but a fold that counted the new grant twice would double exactly the
            // duration somebody just paid for, and be indistinguishable from a generous one.
            var ledger = pendingEntry is null || entries.Any(e => e.Id == pendingEntry.Id)
                ? entries
                : entries.Append(pendingEntry).ToList();

            if (plan is { } chosen)
            {
                subscription.SetPlan(chosen, DateTime.UtcNow);
            }

            subscription.RecomputeFrom(ledger);
            await subscriptions.UpdateAsync(subscription, cancellationToken);

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return Result<DateTime?>.Success(subscription.EndsOn);
            }
            catch (ConflictException) when (attempt < MaxAttempts)
            {
                logger.LogWarning(
                    "Concurrent entitlement write on clinic {ClinicId}, attempt {Attempt}; re-folding the ledger",
                    clinicId, attempt);

                // Detach before reloading, or EF's identity map hands back the same stale instance and the next
                // attempt sends the same doomed WHERE clause.
                unitOfWork.StopTracking(subscription);

                var reloaded = await subscriptions.GetByClinicAsync(clinicId, cancellationToken);
                if (reloaded is null)
                {
                    return Result<DateTime?>.Failure(
                        SubscriptionRefusals.Missing, SubscriptionRefusals.MissingCode);
                }

                subscription = reloaded;
            }
            catch (ConflictException)
            {
                // The last attempt. Caught rather than allowed to escape as a 409, because EC-5 forbids showing a
                // conflict here at all — the caller is told to retry, in a sentence it can act on.
                logger.LogError(
                    "Gave up re-folding clinic {ClinicId}'s entitlement after {Attempts} concurrent conflicts",
                    clinicId, MaxAttempts);
                return Result<DateTime?>.Failure(ExhaustedError);
            }
        }

        return Result<DateTime?>.Failure(ExhaustedError);
    }
}
