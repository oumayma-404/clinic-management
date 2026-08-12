using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Messaging;

/// <summary>
/// The write half every allocation command shares: re-fold the cabinet's <b>whole</b> ledger onto every month the
/// change can reach, and commit, in one save (AC-6.3, AC-7.4). Shared rather than copied because the retry below is
/// subtle and a second copy is the one that would be missing it — <c>SubscriptionRefold</c>'s reasoning and its shape.
///
/// <para><b>⚠️ EC-5 is why the retry exists, and it is not the usual conflict handling.</b> Two allocations recorded at
/// the same moment must <i>both</i> land and both be kept. But <c>Entity.Version</c> is mapped onto <c>xmin</c>, so the
/// second writer's <c>UPDATE … WHERE xmin = &lt;loaded&gt;</c> matches nothing and raises
/// <see cref="ConflictException"/> → 409. Retrying is <b>correct here specifically because the snapshot is
/// derived</b>: whoever saves last recomputes the same figures from every entry, so the loop converges rather than
/// papering over a lost update. On an ordinary aggregate this would be exactly the wrong thing to do.</para>
///
/// <para><b>⚠️ Which months are rewritten is bounded, and the bound is not laziness.</b> A standing entry reaches
/// every month from its effective month onwards, so « every month it feeds » is unbounded into the future — but only
/// rows that <i>exist</i> can be rewritten, and rows are written by the daily pass for the current month and by the
/// dispatcher's ensure-create. So the working set is « the cabinet's existing counting rows from the entry's effective
/// month onwards », which is finite, and a future month gets the right figure from the fold on the day its row is
/// created. Nothing is left stale: <c>verify-schema</c>'s <c>monthly-allowance-matches-ledger</c> re-derives every
/// stored row through the real fold (R-6).</para>
///
/// <para>Each attempt is still <b>one</b> save, so no state is ever half-applied: a failed attempt rolls back the
/// staged entry with it and the next attempt re-inserts it once.</para>
/// </summary>
public static class MessagingAllowanceRefold
{
    /// <summary>Bounds the loop, on <c>SubscriptionRefold</c>'s and <c>IssueInvoiceCommand</c>'s precedent.</summary>
    public const int MaxAttempts = 5;

    public const string ExhaustedError =
        "Impossible d'enregistrer le forfait : le cabinet a été modifié simultanément à plusieurs reprises. "
        + "Réessayez.";

    /// <param name="pendingEntry">
    /// <b>This command's own entry</b>: a grant's staged row (not yet visible to <c>GetEntriesAsync</c>, so it is
    /// appended to what the fold sees) or a cancellation's already-persisted one (present in the read, so the append
    /// is skipped). Naming it either way is what lets the retry tell the row carrying <i>this</i> command's unsaved
    /// change apart from the rest of the ledger, which it must re-read.
    /// </param>
    /// <param name="fromMonthKey">
    /// The earliest month the change can reach — the entry's own effective month. Months before it are untouched,
    /// which is what makes a closed month's snapshot safe from every later allocation.
    /// </param>
    /// <returns>The cabinet's allowance for the current month after the refold, or null where it has none.</returns>
    public static async Task<Result<int?>> SaveAsync(
        Guid clinicId,
        MessagingAllowanceEntry? pendingEntry,
        string fromMonthKey,
        IMessagingAllowanceRepository allowances,
        IUnitOfWork unitOfWork,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var currentMonth = ClinicClock.CurrentMonthKey();

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var entries = await allowances.GetEntriesAsync(clinicId, cancellationToken);

            // Appended only when the read did not already return it. A staged entry is invisible to an EF query, so
            // in production it never does — but a fold that counted a new allocation twice would double exactly the
            // figure somebody just paid for, and be indistinguishable from a generous one.
            var ledger = pendingEntry is null || entries.Any(e => e.Id == pendingEntry.Id)
                ? entries
                : entries.Append(pendingEntry).ToList();

            var projected = ledger.Select(e => e.ToLedgerEntry()).ToList();
            var months = await allowances.GetMonthsAsync(clinicId, fromMonthKey, cancellationToken);
            var now = DateTime.UtcNow;

            foreach (var month in months)
            {
                // A month whose fold is null keeps whatever snapshot it had: null means « no allowance record
                // reaches this month », and writing 0 there would turn our own bookkeeping gap into a statement that
                // the vendor allowed the practice nothing (AC-4.3's distinction, one layer down).
                if (MessagingAllowanceLedger.Fold(projected, month.MonthKey) is { } allowance)
                {
                    month.SetAllowance(allowance, now);
                    await allowances.UpdateMonthAsync(month, cancellationToken);
                }
            }

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return Result<int?>.Success(MessagingAllowanceLedger.Fold(projected, currentMonth));
            }
            catch (ConflictException) when (attempt < MaxAttempts)
            {
                logger.LogWarning(
                    "Concurrent messaging-allowance write on clinic {ClinicId}, attempt {Attempt}; re-folding",
                    clinicId, attempt);

                // ⚠️ Both the ledger AND the month rows have to be detached, and the ledger half is the one easily
                // missed: the re-read goes through EF's identity map, so an already-tracked entry comes back with its
                // attempt-1 values. A concurrent cancellation would then be invisible and this loop would commit a
                // snapshot that still counts the voided entry — the very drift `monthly-allowance-matches-ledger`
                // exists to catch, produced by the code meant to converge. This command's own entry is deliberately
                // kept tracked: detaching it would discard the unsaved change the whole call is here to persist.
                foreach (var stale in entries.Where(e => e.Id != pendingEntry?.Id))
                {
                    unitOfWork.StopTracking(stale);
                }

                foreach (var month in months)
                {
                    unitOfWork.StopTracking(month);
                }
            }
            catch (ConflictException)
            {
                // The last attempt. Caught rather than allowed to escape as a 409, because EC-5 forbids showing a
                // conflict here at all — the caller is told to retry, in a sentence it can act on.
                logger.LogError(
                    "Gave up re-folding clinic {ClinicId}'s messaging allowance after {Attempts} conflicts",
                    clinicId, MaxAttempts);
                return Result<int?>.Failure(ExhaustedError);
            }
        }

        return Result<int?>.Failure(ExhaustedError);
    }
}
