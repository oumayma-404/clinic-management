using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Messaging.Commands;

/// <summary>What the vendor gets back after striking one allocation through.</summary>
/// <param name="AllowanceThisMonth">
/// The current month's folded allowance after the cancellation, or null where the cabinet now has no allowance record
/// reaching this month at all — which is a different fact from « zéro » and is why it is nullable (AC-4.3).
/// </param>
/// <param name="ConsumedThisMonth">
/// Untouched by a cancellation (AC-7.4): the messages were sent and the vendor was billed for them. Returned so the
/// console can show « épuisé » with the arithmetic visible rather than as a bare verdict.
/// </param>
public sealed record MessagingAllowanceCancelledResult(
    Guid ClinicId,
    Guid EntryId,
    int? PreviousAllowanceThisMonth,
    int? AllowanceThisMonth,
    int? ConsumedThisMonth,
    bool ExhaustedThisMonth);

/// <summary>
/// The vendor strikes out an allocation recorded by mistake (US-7, AC-7.1/7.2).
///
/// <para><b>⚠️ The entry is kept, never edited and never deleted</b> (AC-6.2, AC-7.2). It stays in the ledger with its
/// motif, its canceller and the moment — which is what lets « what were we paid, and for what? » still be answered a
/// year later, on the one screen whose purpose is to check that.</para>
///
/// <para><b>⚠️ A cancellation reaches EVERY month the entry fed, the current one included</b> (AC-7.4) — the
/// deliberate asymmetry with a <i>lowering</i>, which waits for the next month (AC-6.4, AC-7.4a). The distinction is
/// that a lowering is a decision about the future while a cancellation says the entry should never have existed, so a
/// mis-keyed « +3000 » must be correctable in the month it was keyed into. It falls out of
/// <see cref="MessagingAllowanceLedger"/> for free — a cancelled entry is simply skipped, whatever month is asked
/// about — which is why nothing here computes a figure.</para>
///
/// <para>⚠️ <b>Consumption is untouched, and the month may end up reading « épuisé ».</b> Nothing is unsent and
/// nothing is clawed back; remaining is <c>max(0, allowance − consumed)</c> and reminders are held from that moment.
/// That is the honest outcome, and it is the one the console's confirmation states in advance.</para>
///
/// <para>⚠️ <b>There is no HTTP path to this command</b> (AC-9.3), exactly as for its grant sibling — the callers are
/// the <c>messaging-cancel</c> verb and the console's own wrapper.</para>
/// </summary>
public class CancelMessagingAllowanceCommand : IRequest<Result<MessagingAllowanceCancelledResult>>
{
    public Guid? ClinicId { get; set; }

    public string? AdminEmail { get; set; }

    /// <summary>The entry to strike through, located <b>within this cabinet's own ledger</b>.</summary>
    public Guid EntryId { get; set; }

    /// <summary>
    /// Mandatory (AC-7.1). Every month the entry fed recomputes as a result — including, possibly, into « épuisé » —
    /// so « pourquoi le forfait de ce cabinet a-t-il diminué ? » must stay answerable afterwards.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Who cancelled it: <c>job|&lt;verb&gt;</c> or <c>console|&lt;accountId&gt;</c>.</summary>
    public string? CancelledBy { get; set; }
}

public class CancelMessagingAllowanceCommandHandler
    : IRequestHandler<CancelMessagingAllowanceCommand, Result<MessagingAllowanceCancelledResult>>
{
    public const string UnknownEntryCode = "messaging_allowance_entry_not_found";

    public const string AlreadyCancelledCode = "messaging_allowance_entry_already_cancelled";

    public const string ReasonRequiredError =
        "Indiquez le motif de l'annulation : il reste inscrit sur l'allocation et explique, plus tard, pourquoi le "
        + "forfait de ce cabinet a diminué — y compris pour le mois en cours.";

    public const string UnknownEntryError =
        "Cette allocation ne figure pas dans le journal des forfaits de ce cabinet.";

    public const string AlreadyCancelledError =
        "Cette allocation est déjà annulée. Son motif, son auteur et sa date figurent sur la fiche du cabinet.";

    private readonly IMessagingAllowanceRepository _allowances;
    private readonly IClinicRepository _clinics;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CancelMessagingAllowanceCommandHandler> _logger;

    public CancelMessagingAllowanceCommandHandler(
        IMessagingAllowanceRepository allowances,
        IClinicRepository clinics,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        ILogger<CancelMessagingAllowanceCommandHandler> logger)
    {
        _allowances = allowances;
        _clinics = clinics;
        _users = users;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<MessagingAllowanceCancelledResult>> Handle(
        CancelMessagingAllowanceCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                return Result<MessagingAllowanceCancelledResult>.Failure(ReasonRequiredError);
            }

            var clinicResult = await SubscriptionCabinetLookup.ResolveAsync(
                request.ClinicId, request.AdminEmail, _clinics, _users, cancellationToken);

            if (clinicResult.IsFailure)
            {
                return Result<MessagingAllowanceCancelledResult>.FailureFrom(clinicResult);
            }

            var clinicId = clinicResult.Value;

            // Scoped to the cabinet, not fetched by id alone: another practice's entry is then structurally
            // unreachable rather than checked for — the shape `CancelSubscriptionPeriodFromConsoleCommand` uses.
            var entry = await _allowances.GetEntryAsync(clinicId, request.EntryId, cancellationToken);

            if (entry is null)
            {
                return Result<MessagingAllowanceCancelledResult>.Failure(UnknownEntryError, UnknownEntryCode);
            }

            if (entry.IsCancelled)
            {
                // AC-7.5. A refusal and not a silent success: that entry was struck through by *somebody*, and which
                // colleague and for what motif is on the file this refusal sends the reader back to.
                return Result<MessagingAllowanceCancelledResult>.Failure(AlreadyCancelledError, AlreadyCancelledCode);
            }

            var currentMonth = ClinicClock.CurrentMonthKey();
            var entries = await _allowances.GetEntriesAsync(clinicId, cancellationToken);
            var previous = MessagingAllowanceLedger.Fold(
                entries.Select(e => e.ToLedgerEntry()).ToList(), currentMonth);

            entry.Cancel(request.Reason, request.CancelledBy, DateTime.UtcNow);
            await _allowances.UpdateEntryAsync(entry, cancellationToken);

            // From the entry's OWN effective month, which is what makes AC-7.4 true: a standing entry cancelled in
            // March has to rewrite March, April and every month after it that has a row, not only the current one.
            var saved = await MessagingAllowanceRefold.SaveAsync(
                clinicId, entry, entry.EffectiveMonth, _allowances, _unitOfWork, _logger, cancellationToken);

            if (saved.IsFailure)
            {
                return Result<MessagingAllowanceCancelledResult>.FailureFrom(saved);
            }

            // Read back AFTER the save rather than computed from the fold above: `ConsumedMessages` belongs to the
            // month row and is the one figure a cancellation must not have moved, so it is worth reading rather than
            // asserting.
            var month = await _allowances.GetMonthAsync(clinicId, currentMonth, cancellationToken);

            _logger.LogInformation(
                "Cancelled messaging allowance entry {EntryId} for clinic {ClinicId}; allowance this month is now "
                + "{Allowance} against {Consumed} consumed",
                entry.Id, clinicId, saved.Value, month?.ConsumedMessages);

            return Result<MessagingAllowanceCancelledResult>.Success(new MessagingAllowanceCancelledResult(
                ClinicId: clinicId,
                EntryId: entry.Id,
                PreviousAllowanceThisMonth: previous,
                AllowanceThisMonth: saved.Value,
                ConsumedThisMonth: month?.ConsumedMessages,
                // Read off the month row's own rule, never from `allowance <= consumed` here: « épuisé » has one
                // authority and it is the entity the gate reads too.
                ExhaustedThisMonth: month?.IsExhausted ?? false));
        }
        catch (ArgumentException ex)
        {
            // MessagingAllowanceEntry.Cancel's own French guards — an empty motif, or one over its length.
            return Result<MessagingAllowanceCancelledResult>.Failure(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            // The entity's « déjà annulée » guard, reached only if two cancellations race past the check above.
            return Result<MessagingAllowanceCancelledResult>.Failure(ex.Message, AlreadyCancelledCode);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error cancelling a messaging allowance entry");
            return Result<MessagingAllowanceCancelledResult>.Failure(
                "Erreur lors de l'annulation de l'allocation de forfait.");
        }
    }
}
