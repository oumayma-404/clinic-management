using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Messaging;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Platform.Commands;

/// <summary>
/// The vendor strikes out an allocation recorded by mistake, from the console
/// (<c>vendor-whatsapp-messaging-quota</c> US-7).
///
/// <para><b>⚠️ The entry is kept, never edited and never deleted</b> (AC-6.2, AC-7.2). It stays in the ledger with its
/// motif, its canceller and the moment — which is what lets « what were we paid, and for what? » still be answered a
/// year later on the file whose purpose is to check that.</para>
///
/// <para><b>⚠️ It reuses the companion's pieces rather than sending <c>CancelMessagingAllowanceCommand</c>, and the
/// reason is atomicity</b> — the shape <c>CancelSubscriptionPeriodFromConsoleCommand</c> settled. That command commits
/// on its own, so the AC-6.8 access-ledger row would be a second transaction, and a correction recorded with no journal
/// row behind it is the « an unattributable action must not aboutir » this console settled for reads. Staging the ledger
/// row before <see cref="MessagingAllowanceRefold"/>'s single save is the only shape in which AC-6.8 and AC-7.2 are true
/// of the same instant.</para>
///
/// <para><b>⚠️ A cancellation reaches every month the entry fed, the current one included</b> (AC-7.4) — the deliberate
/// asymmetry with a lowering, which waits for the next month (AC-6.4, AC-7.4a). It falls out of the fold: a cancelled
/// entry is simply skipped, whatever month is asked about, so nothing here computes a figure. Consumption is untouched,
/// remaining is floored at zero, and a month whose forfait has fallen below what was already spent reads « épuisé ».</para>
///
/// <para>⚠️ <b>No idempotency key, unlike the grant, and deliberately.</b> A double-click on « Enregistrer » is the
/// vendor's own repeated action and replaying the first outcome is what they wanted — but an allocation already struck
/// through was struck through by <i>somebody</i>, and which colleague and for what motif is a fact the vendor needs. So
/// « déjà annulée » is a <b>refusal</b> carrying <see cref="AlreadyCancelledCode"/>, and the dialog re-reads the fiche
/// so that motif and author appear beside it (AC-7.5).</para>
/// </summary>
public class CancelMessagingAllowanceFromConsoleCommand : IRequest<Result<PlatformMessagingAllowanceCancelledDto>>
{
    public Guid ClinicId { get; set; }

    /// <summary>The allocation to strike through, located <b>within this cabinet's own ledger</b>.</summary>
    public Guid EntryId { get; set; }

    /// <summary>Mandatory (AC-7.1): the current month's forfait can fall below what is already spent, so « why » must
    /// be answerable afterwards.</summary>
    public string Reason { get; set; } = string.Empty;
}

public class CancelMessagingAllowanceFromConsoleCommandHandler
    : IRequestHandler<CancelMessagingAllowanceFromConsoleCommand, Result<PlatformMessagingAllowanceCancelledDto>>
{
    public const string UnknownClinicCode = "clinic_not_found";

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
    private readonly IPlatformAccessEntryRepository _accessEntries;
    private readonly IPlatformSessionContext _session;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<CancelMessagingAllowanceFromConsoleCommandHandler> _logger;

    public CancelMessagingAllowanceFromConsoleCommandHandler(
        IMessagingAllowanceRepository allowances,
        IClinicRepository clinics,
        IUserRepository users,
        IPlatformAccessEntryRepository accessEntries,
        IPlatformSessionContext session,
        IUnitOfWork unitOfWork,
        ITenantScope tenantScope,
        ILogger<CancelMessagingAllowanceFromConsoleCommandHandler> logger)
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

    public async Task<Result<PlatformMessagingAllowanceCancelledDto>> Handle(
        CancelMessagingAllowanceFromConsoleCommand request, CancellationToken cancellationToken)
    {
        // EC-12: an undeclared cross-clinic scope reads zero rows with no error, and here that would report every
        // cabinet — and every allocation — as unknown.
        PlatformTenantScope.EnsureDeclared(_tenantScope);

        try
        {
            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                return Result<PlatformMessagingAllowanceCancelledDto>.Failure(ReasonRequiredError);
            }

            var clinicResult = await SubscriptionCabinetLookup.ResolveAsync(
                request.ClinicId, adminEmail: null, _clinics, _users, cancellationToken);

            if (clinicResult.IsFailure)
            {
                return Result<PlatformMessagingAllowanceCancelledDto>.Failure(
                    clinicResult.Error ?? "Cabinet introuvable.", UnknownClinicCode);
            }

            var clinicId = clinicResult.Value;
            var clinic = await _clinics.GetByIdAsync(clinicId, cancellationToken);

            if (clinic is null)
            {
                return Result<PlatformMessagingAllowanceCancelledDto>.Failure(
                    "Cabinet introuvable.", UnknownClinicCode);
            }

            // Located within the cabinet's own ledger: another practice's allocation is then structurally unreachable
            // rather than checked for.
            var entry = await _allowances.GetEntryAsync(clinicId, request.EntryId, cancellationToken);

            if (entry is null)
            {
                return Result<PlatformMessagingAllowanceCancelledDto>.Failure(UnknownEntryError, UnknownEntryCode);
            }

            if (entry.IsCancelled)
            {
                return Result<PlatformMessagingAllowanceCancelledDto>.Failure(
                    AlreadyCancelledError, AlreadyCancelledCode);
            }

            var currentMonth = ClinicClock.CurrentMonthKey();
            var entries = await _allowances.GetEntriesAsync(clinicId, cancellationToken);
            var previous = MessagingAllowanceLedger.Fold(
                entries.Select(e => e.ToLedgerEntry()).ToList(), currentMonth);

            var now = DateTime.UtcNow;

            // Resolved before anything is written: `CancelledBy` lands on a row nobody can edit afterwards, so
            // « nous ne savons pas qui » has to stop the correction rather than be discovered while recording it.
            var accountId = PlatformAccessLedger.RequireAccountId(_session);

            entry.Cancel(request.Reason, AuditActor.Console(accountId).UserId, now);
            await _allowances.UpdateEntryAsync(entry, cancellationToken);

            await PlatformAccessLedger.RecordAsync(
                _accessEntries,
                _session,
                clinicId,
                clinic.Name,
                PlatformAccessAction.CancelledMessagingAllowance,
                now,
                cancellationToken,
                messagingAllowanceEntryId: entry.Id);

            // From the entry's OWN effective month, which is what makes AC-7.4 true: a standing entry cancelled in
            // March has to rewrite March and every later month that has a row, not only the current one.
            var saved = await MessagingAllowanceRefold.SaveAsync(
                clinicId, entry, entry.EffectiveMonth, _allowances, _unitOfWork, _logger, cancellationToken);

            if (saved.IsFailure)
            {
                return Result<PlatformMessagingAllowanceCancelledDto>.FailureFrom(saved);
            }

            // Read back after the save rather than derived from the fold: `ConsumedMessages` is the one figure a
            // cancellation must not have moved, and « épuisé » has one authority — the month row's own rule, which is
            // also what the outbox gate reads.
            var month = await _allowances.GetMonthAsync(clinicId, currentMonth, cancellationToken);

            _logger.LogInformation(
                "Console account {AccountId} cancelled messaging allowance {EntryId} for clinic {ClinicId}; "
                + "allowance this month is now {Allowance} against {Consumed} consumed",
                accountId, entry.Id, clinicId, saved.Value, month?.ConsumedMessages);

            return Result<PlatformMessagingAllowanceCancelledDto>.Success(new PlatformMessagingAllowanceCancelledDto(
                ClinicId: clinicId,
                EntryId: entry.Id,
                PreviousAllowanceThisMonth: previous,
                AllowanceThisMonth: saved.Value,
                ConsumedThisMonth: month?.ConsumedMessages,
                ExhaustedThisMonth: month?.IsExhausted ?? false));
        }
        catch (ArgumentException ex)
        {
            // MessagingAllowanceEntry.Cancel's own French guards — an empty motif, or one over its length.
            return Result<PlatformMessagingAllowanceCancelledDto>.Failure(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            // The entity's « déjà annulée » guard, reached only if two cancellations race past the check above.
            return Result<PlatformMessagingAllowanceCancelledDto>.Failure(ex.Message, AlreadyCancelledCode);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error cancelling a messaging allowance from the console");
            return Result<PlatformMessagingAllowanceCancelledDto>.Failure(
                "Erreur lors de l'annulation de l'allocation de forfait.");
        }
    }
}
