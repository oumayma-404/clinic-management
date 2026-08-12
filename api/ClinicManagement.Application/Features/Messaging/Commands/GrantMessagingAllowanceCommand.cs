using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Messaging.Commands;

/// <summary>
/// What the vendor gets back after recording an allocation — enough to read the outcome off a console or a terminal.
/// </summary>
/// <param name="EffectiveMonth">
/// The <c>AAAA-MM</c> month the entry starts applying in, <b>decided by the server</b> (AC-6.4a). It is returned
/// rather than left to be re-derived, because « prend effet le mois prochain » is the one thing a lowering has to say
/// out loud and no caller can compute it without the ledger.
/// </param>
/// <param name="AllowanceThisMonth">
/// The cabinet's folded allowance for the <i>current</i> month after the write, or null where it has none. A lowering
/// leaves this at the old figure — which is exactly AC-6.4, and the reason both numbers are returned.
/// </param>
public sealed record MessagingAllowanceGrantResult(
    Guid ClinicId,
    Guid EntryId,
    MessagingAllowanceKind Kind,
    string EffectiveMonth,
    int Messages,
    int? PreviousAllowanceThisMonth,
    int? AllowanceThisMonth);

/// <summary>
/// The vendor records a cabinet's WhatsApp reminder allocation — a <b>standing</b> monthly figure or a one-off
/// <b>top-up</b> for a named month (US-6, AC-6.1/6.2).
///
/// <para><b>⚠️ There is no HTTP path to this command and there must not be</b> (AC-9.3): a practice able to raise its
/// own forfait does not have one. Its only callers are the <c>messaging-grant</c> console verb and the vendor
/// console's own wrapper — neither of which is a clinic-facing endpoint — and
/// <c>MessagingVendorCommandReachabilityTests</c> holds that no controller source so much as names the type. It is a
/// MediatR command all the same, so the wrapper could send it if atomicity allowed (it does not — see the wrapper).</para>
///
/// <para><b>⚠️ Which of the two an entry is, is decided by <see cref="MessagingAllowancePlan"/> and never by the
/// caller</b> (AC-6.4a): a <b>raise</b> lands in the current month (AC-6.3) and a <b>lowering</b> in the next one
/// (AC-6.4), so a practice is never cut off mid-afternoon by a change it had no warning of.</para>
///
/// <para><b>⚠️ Nothing here computes a figure.</b> The entry carries what the vendor said and
/// <see cref="MessagingAllowanceRefold"/> re-folds the whole ledger onto every month the change can reach — which is
/// what makes a later cancellation of <i>any</i> entry able to move the same months (AC-7.4), and what keeps
/// <c>verify-schema</c>'s <c>monthly-allowance-matches-ledger</c> honest.</para>
///
/// <para>⚠️ <b>Two genuinely different allocations both land and are both kept</b> (EC-5): an append-only ledger has
/// no conflict to report, and the surplus one is corrected by a cancellation rather than by refusing the money. The
/// « one entry per submission » half (AC-6.7) belongs to the <i>console wrapper</i>, because it is enforced by the
/// access ledger's unique key — see <c>RecordMessagingAllowanceFromConsoleCommand</c>.</para>
/// </summary>
public class GrantMessagingAllowanceCommand : IRequest<Result<MessagingAllowanceGrantResult>>
{
    /// <summary>The cabinet, by id or by the e-mail of somebody who works there — <c>SubscriptionCabinetLookup</c>'s rule.</summary>
    public Guid? ClinicId { get; set; }

    public string? AdminEmail { get; set; }

    /// <summary>
    /// A <b>standing</b> monthly figure, « from now on ». Mutually exclusive with <see cref="TopUpMessages"/>.
    ///
    /// <para><b>Zero is legal</b> — « ce cabinet n'envoie pas de rappels WhatsApp » is a decision the vendor is
    /// allowed to record, and it is not the same state as having no allowance entry at all (AC-4.3).</para>
    /// </summary>
    public int? MessagesPerMonth { get; set; }

    /// <summary>A one-off addition to <see cref="AppliesToMonth"/> alone. Mutually exclusive with the above.</summary>
    public int? TopUpMessages { get; set; }

    /// <summary>
    /// The <c>AAAA-MM</c> month a top-up applies to — the current one or a future one, never a past one (AC-6.5).
    /// Required with <see cref="TopUpMessages"/> and refused with <see cref="MessagesPerMonth"/>, whose month the
    /// server decides.
    /// </summary>
    public string? AppliesToMonth { get; set; }

    /// <summary>What the vendor was paid, or null for a complimentary allocation (AC-6.6) — never an amount of zero.</summary>
    public decimal? AmountDt { get; set; }

    /// <summary>How the vendor was paid. The <b>vendor's</b> enum, never the clinic's <c>PaymentMethod</c> (FR-2).</summary>
    public SubscriptionPaymentMethod? Method { get; set; }

    public string? Reference { get; set; }

    public string? Note { get; set; }

    /// <summary>Who recorded it — <c>job|&lt;verb&gt;</c> from a terminal, <c>console|&lt;accountId&gt;</c> from the console.</summary>
    public string? RecordedBy { get; set; }
}

public class GrantMessagingAllowanceCommandHandler
    : IRequestHandler<GrantMessagingAllowanceCommand, Result<MessagingAllowanceGrantResult>>
{
    private readonly IMessagingAllowanceRepository _allowances;
    private readonly IClinicRepository _clinics;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<GrantMessagingAllowanceCommandHandler> _logger;

    public GrantMessagingAllowanceCommandHandler(
        IMessagingAllowanceRepository allowances,
        IClinicRepository clinics,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        ILogger<GrantMessagingAllowanceCommandHandler> logger)
    {
        _allowances = allowances;
        _clinics = clinics;
        _users = users;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<MessagingAllowanceGrantResult>> Handle(
        GrantMessagingAllowanceCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await SubscriptionCabinetLookup.ResolveAsync(
                request.ClinicId, request.AdminEmail, _clinics, _users, cancellationToken);

            if (clinicResult.IsFailure)
            {
                return Result<MessagingAllowanceGrantResult>.FailureFrom(clinicResult);
            }

            var clinicId = clinicResult.Value;
            var currentMonth = ClinicClock.CurrentMonthKey();

            var entries = await _allowances.GetEntriesAsync(clinicId, cancellationToken);
            var ledger = entries.Select(e => e.ToLedgerEntry()).ToList();

            var planned = MessagingAllowancePlan.Decide(
                request.MessagesPerMonth,
                request.TopUpMessages,
                request.AppliesToMonth,
                request.AmountDt,
                ledger,
                currentMonth);

            if (planned.IsFailure)
            {
                return Result<MessagingAllowanceGrantResult>.FailureFrom(planned);
            }

            var plan = planned.Value!;
            var previous = MessagingAllowanceLedger.Fold(ledger, currentMonth);

            var entry = MessagingAllowanceEntry.Create(
                clinicId,
                plan.Kind,
                plan.Messages,
                plan.EffectiveMonth,
                DateTime.UtcNow,
                request.AmountDt,
                request.Method,
                request.Reference,
                request.Note,
                request.RecordedBy);

            await _allowances.AddEntryAsync(entry, cancellationToken);

            var saved = await MessagingAllowanceRefold.SaveAsync(
                clinicId, entry, plan.EffectiveMonth, _allowances, _unitOfWork, _logger, cancellationToken);

            if (saved.IsFailure)
            {
                return Result<MessagingAllowanceGrantResult>.FailureFrom(saved);
            }

            _logger.LogInformation(
                "Recorded a {Kind} messaging allowance entry {EntryId} of {Messages} for clinic {ClinicId}, "
                + "effective {EffectiveMonth}; allowance this month is now {Allowance}",
                plan.Kind, entry.Id, plan.Messages, clinicId, plan.EffectiveMonth, saved.Value);

            return Result<MessagingAllowanceGrantResult>.Success(new MessagingAllowanceGrantResult(
                ClinicId: clinicId,
                EntryId: entry.Id,
                Kind: plan.Kind,
                EffectiveMonth: plan.EffectiveMonth,
                Messages: plan.Messages,
                PreviousAllowanceThisMonth: previous,
                AllowanceThisMonth: saved.Value));
        }
        catch (ArgumentException ex)
        {
            // MessagingAllowanceEntry.Create's own French guards: a negative figure, one over the typo ceiling, a
            // zero top-up, an over-long reference or note, a malformed month key.
            return Result<MessagingAllowanceGrantResult>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error recording a messaging allowance for a cabinet");
            return Result<MessagingAllowanceGrantResult>.Failure(
                "Erreur lors de l'enregistrement du forfait de rappels.");
        }
    }
}
