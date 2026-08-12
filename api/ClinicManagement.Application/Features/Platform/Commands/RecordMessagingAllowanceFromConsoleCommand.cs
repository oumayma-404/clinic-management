using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Messaging;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Platform.Commands;

/// <summary>
/// The vendor records a cabinet's WhatsApp reminder forfait from the console
/// (<c>vendor-whatsapp-messaging-quota</c> US-6).
///
/// <para><b>⚠️ It reuses the companion's own pieces rather than sending <c>GrantMessagingAllowanceCommand</c>, and the
/// reason is atomicity</b> — the shape <c>RecordSubscriptionPeriodCommand</c> settled and this file copies
/// deliberately. That command commits on its own, so the AC-6.8 access-ledger row would be a <i>second</i> transaction,
/// and an allocation recorded with nothing in the journal behind it is exactly the « an unattributable action must not
/// aboutir » the console settled for reads. Staging the ledger row and letting
/// <see cref="MessagingAllowanceRefold"/>'s single save carry both is the only shape in which AC-6.8 and AC-6.2 are
/// true of the same instant. The pieces reused — <c>SubscriptionCabinetLookup</c>,
/// <see cref="MessagingAllowancePlan"/>, <c>MessagingAllowanceEntry.Create</c>,
/// <see cref="MessagingAllowanceRefold"/> — are the companion's, so the rules are shared even though the pipeline is
/// not. An explicit transaction was rejected for the same reason as there: the refold retries on
/// <c>ConflictException</c>, and a failed statement aborts an ambient transaction.</para>
///
/// <para><b>⚠️ Standing-vs-top-up is the server's decision</b> (AC-6.4a), taken by
/// <see cref="MessagingAllowancePlan.Decide"/> — the same call the verb makes, so a lowering defers to next month
/// identically on both doors.</para>
///
/// <para><b>⚠️ A repeated submission is one entry</b> (AC-6.7), keyed on <see cref="IdempotencyKey"/> and enforced by
/// the <b>unique index</b> on <c>PlatformAccessEntries.IdempotencyKey</c> — not by this handler reading first, which
/// two simultaneous submissions both pass. Two <i>different</i> allocations both land and are both kept, with <b>no</b>
/// conflict response (EC-5): the surplus one is corrected by a cancellation, not by refusing money already
/// received.</para>
/// </summary>
public class RecordMessagingAllowanceFromConsoleCommand : IRequest<Result<PlatformMessagingAllowanceRecordedDto>>
{
    public Guid ClinicId { get; set; }

    /// <summary>
    /// The client's own key for this submission. Optional on the wire and supplied by the console's form, which mints
    /// one per opened sheet — so the second tap of a double-click carries the first tap's key.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>A standing monthly figure. Mutually exclusive with <see cref="TopUpMessages"/>; zero is legal.</summary>
    public int? MessagesPerMonth { get; set; }

    /// <summary>A one-off addition to <see cref="AppliesToMonth"/> alone.</summary>
    public int? TopUpMessages { get; set; }

    /// <summary>The <c>AAAA-MM</c> month a top-up applies to — current or future only (AC-6.5).</summary>
    public string? AppliesToMonth { get; set; }

    /// <summary>What the vendor was paid, or null for a complimentary forfait (AC-6.6) — never zero.</summary>
    public decimal? AmountDt { get; set; }

    /// <summary><c>Transfer</c> | <c>Cash</c> | <c>Cheque</c> | <c>Card</c>.</summary>
    public string? Method { get; set; }

    public string? Reference { get; set; }

    public string? Note { get; set; }
}

public class RecordMessagingAllowanceFromConsoleCommandHandler
    : IRequestHandler<RecordMessagingAllowanceFromConsoleCommand, Result<PlatformMessagingAllowanceRecordedDto>>
{
    public const string UnknownClinicCode = "clinic_not_found";

    public const string UnknownMethodError = "Ce moyen de paiement n'existe pas.";

    private readonly IMessagingAllowanceRepository _allowances;
    private readonly IClinicRepository _clinics;
    private readonly IUserRepository _users;
    private readonly IPlatformAccessEntryRepository _accessEntries;
    private readonly IPlatformSessionContext _session;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<RecordMessagingAllowanceFromConsoleCommandHandler> _logger;

    public RecordMessagingAllowanceFromConsoleCommandHandler(
        IMessagingAllowanceRepository allowances,
        IClinicRepository clinics,
        IUserRepository users,
        IPlatformAccessEntryRepository accessEntries,
        IPlatformSessionContext session,
        IUnitOfWork unitOfWork,
        ITenantScope tenantScope,
        ILogger<RecordMessagingAllowanceFromConsoleCommandHandler> logger)
    {
        _allowances = allowances;
        _clinics = clinics;
        _users = users;
        _accessEntries = accessEntries;
        _session = session;
        _unitOfWork = unitOfWork;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    public async Task<Result<PlatformMessagingAllowanceRecordedDto>> Handle(
        RecordMessagingAllowanceFromConsoleCommand request, CancellationToken cancellationToken)
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

            if (!TryParseMethod(request.Method, out var method))
            {
                return Result<PlatformMessagingAllowanceRecordedDto>.Failure(UnknownMethodError);
            }

            // The companion's own « which cabinet » rule, so the console and the three verbs cannot disagree about
            // what identifies a practice — refused under a code the screen branches on.
            var clinicResult = await SubscriptionCabinetLookup.ResolveAsync(
                request.ClinicId, adminEmail: null, _clinics, _users, cancellationToken);

            if (clinicResult.IsFailure)
            {
                return Result<PlatformMessagingAllowanceRecordedDto>.Failure(
                    clinicResult.Error ?? "Cabinet introuvable.", UnknownClinicCode);
            }

            var clinicId = clinicResult.Value;
            var clinic = await _clinics.GetByIdAsync(clinicId, cancellationToken);

            if (clinic is null)
            {
                return Result<PlatformMessagingAllowanceRecordedDto>.Failure(
                    "Cabinet introuvable.", UnknownClinicCode);
            }

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
                return Result<PlatformMessagingAllowanceRecordedDto>.FailureFrom(planned);
            }

            var plan = planned.Value!;
            var previous = MessagingAllowanceLedger.Fold(ledger, currentMonth);
            var now = DateTime.UtcNow;

            // Resolved before anything is built, not left to the ledger's own guard below: `RecordedBy` is written into
            // an append-only entry, so « nous ne savons pas qui » must stop the write rather than reach it.
            var accountId = PlatformAccessLedger.RequireAccountId(_session);

            var entry = MessagingAllowanceEntry.Create(
                clinicId,
                plan.Kind,
                plan.Messages,
                plan.EffectiveMonth,
                now,
                request.AmountDt,
                method,
                request.Reference,
                request.Note,
                // `console|{accountId}` through AuditActor's own constant, never a retyped literal — it is also the
                // prefix the counter pass's AC-2.2 exclusion reads, so a grant does not make a dormant cabinet look
                // busy the next morning.
                AuditActor.Console(accountId).UserId);

            await _allowances.AddEntryAsync(entry, cancellationToken);

            // Staged BEFORE the save, so the ledger row and the allocation it records land in one transaction — and,
            // because the key is unique, so does AC-6.7's « one entry per submission ».
            await PlatformAccessLedger.RecordAsync(
                _accessEntries,
                _session,
                clinicId,
                clinic.Name,
                PlatformAccessAction.GrantedMessagingAllowance,
                now,
                cancellationToken,
                idempotencyKey: submissionKey,
                messagingAllowanceEntryId: entry.Id);

            var saved = await MessagingAllowanceRefold.SaveAsync(
                clinicId, entry, plan.EffectiveMonth, _allowances, _unitOfWork, _logger, cancellationToken);

            if (saved.IsFailure)
            {
                return Result<PlatformMessagingAllowanceRecordedDto>.FailureFrom(saved);
            }

            _logger.LogInformation(
                "Console account {AccountId} recorded a {Kind} messaging allowance {EntryId} of {Messages} for "
                + "clinic {ClinicId}, effective {EffectiveMonth}",
                accountId, plan.Kind, entry.Id, plan.Messages, clinicId, plan.EffectiveMonth);

            var month = await _allowances.GetMonthAsync(clinicId, currentMonth, cancellationToken);

            return Result<PlatformMessagingAllowanceRecordedDto>.Success(new PlatformMessagingAllowanceRecordedDto(
                ClinicId: clinicId,
                EntryId: entry.Id,
                Kind: plan.Kind.ToString(),
                KindLabel: MessagingAllowanceLabels.Kind(plan.Kind),
                EffectiveMonth: plan.EffectiveMonth,
                EffectiveMonthLabel: ClinicClock.MonthLabelFr(plan.EffectiveMonth),
                Messages: plan.Messages,
                PreviousAllowanceThisMonth: previous,
                AllowanceThisMonth: saved.Value,
                ConsumedThisMonth: month?.ConsumedMessages,
                AlreadyRecorded: false));
        }
        catch (DbUpdateException ex) when (submissionKey is not null)
        {
            // EC-5's other half. Two identical submissions both read « rien encore enregistré » and both insert; the
            // unique index refuses the second. That is not an error to show — it is the first submission's answer,
            // which is exactly what AC-6.7 asks for. An unkeyed submission falls through to the generic branch, since
            // there is nothing to replay and the failure is then a real one.
            _logger.LogInformation(
                ex, "A repeated messaging-allowance submission lost the race on key {Key}; replaying the first",
                submissionKey);

            var winner = await _accessEntries.GetByIdempotencyKeyAsync(submissionKey, cancellationToken);
            return winner is null
                ? Result<PlatformMessagingAllowanceRecordedDto>.Failure(
                    "Erreur lors de l'enregistrement du forfait de rappels.")
                : await ReplayAsync(winner, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            // MessagingAllowanceEntry.Create's own French guards.
            return Result<PlatformMessagingAllowanceRecordedDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error recording a messaging allowance from the console");
            return Result<PlatformMessagingAllowanceRecordedDto>.Failure(
                "Erreur lors de l'enregistrement du forfait de rappels.");
        }
    }

    /// <summary>
    /// AC-6.7's answer to a submission already recorded: the <b>first</b> outcome, re-read from the entry that row
    /// names, and flagged so the screen can say « déjà enregistré » rather than claiming to have taken the money twice.
    ///
    /// <para>⚠️ <c>PreviousAllowanceThisMonth</c> is null on a replay rather than a guess: what the figure was before
    /// the first submission is not recoverable afterwards, and inventing it would make the console's « avant / après »
    /// pair read as a change that did not happen.</para>
    /// </summary>
    private async Task<Result<PlatformMessagingAllowanceRecordedDto>> ReplayAsync(
        PlatformAccessEntry recorded, CancellationToken cancellationToken)
    {
        var currentMonth = ClinicClock.CurrentMonthKey();
        var month = await _allowances.GetMonthAsync(recorded.ClinicId, currentMonth, cancellationToken);

        MessagingAllowanceEntry? entry = null;
        if (recorded.MessagingAllowanceEntryId is { } entryId)
        {
            entry = await _allowances.GetEntryAsync(recorded.ClinicId, entryId, cancellationToken);
        }

        return Result<PlatformMessagingAllowanceRecordedDto>.Success(new PlatformMessagingAllowanceRecordedDto(
            ClinicId: recorded.ClinicId,
            EntryId: recorded.MessagingAllowanceEntryId,
            Kind: entry?.Kind.ToString(),
            KindLabel: entry is { } e ? MessagingAllowanceLabels.Kind(e.Kind) : null,
            EffectiveMonth: entry?.EffectiveMonth,
            EffectiveMonthLabel: entry is null ? null : ClinicClock.MonthLabelFr(entry.EffectiveMonth),
            Messages: entry?.Messages,
            PreviousAllowanceThisMonth: null,
            AllowanceThisMonth: month?.AllowanceMessages,
            ConsumedThisMonth: month?.ConsumedMessages,
            AlreadyRecorded: true));
    }

    /// <summary>
    /// The vendor's payment methods, parsed once. An unknown value is <b>refused</b> rather than ignored — unlike a
    /// filter, where a stale value should narrow nothing: this one is a fact being written into a ledger nobody can
    /// edit afterwards.
    /// </summary>
    private static bool TryParseMethod(string? raw, out SubscriptionPaymentMethod? method)
    {
        method = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (!Enum.TryParse<SubscriptionPaymentMethod>(raw.Trim(), ignoreCase: true, out var parsed)
            || !Enum.IsDefined(parsed))
        {
            return false;
        }

        method = parsed;
        return true;
    }
}
