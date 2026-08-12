using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Messaging.Queries;

/// <summary>
/// What this cabinet has left of its WhatsApp reminder forfait this Tunisian month (US-2, AC-2.1).
///
/// <para><b>Readable by every clinic role including a secretary</b> (AC-2.2) — the gate is the controller's
/// <c>AnyClinicRole</c>. Reception is who meets a refused « Relancer » chairside and needs to know why.</para>
///
/// <para><b>⚠️ It reports what was counted, and never folds the ledger to fill a gap.</b> A cabinet with no counting
/// row reads « non mesuré » with the three figures <b>null</b>, because a row exists for every cabinet every month
/// (FR-1a) and its absence is a fault on our side. Folding here would paper that over — and it would make « non
/// mesuré » unreachable, which is the one reading that tells a quiet practice apart from a broken counter (AC-2.4).</para>
///
/// <para><b>⚠️ A failed read is a <c>Result.Failure</c>, never a zeroed DTO</b> (AC-2.5, EC-12). « 0 restant » is a
/// statement about the cabinet where the truth is a statement about us.</para>
/// </summary>
public class GetReminderAllowanceQuery : IRequest<Result<ReminderAllowanceDto>>
{
}

public class GetReminderAllowanceQueryHandler
    : IRequestHandler<GetReminderAllowanceQuery, Result<ReminderAllowanceDto>>
{
    private readonly IMessagingAllowanceRepository _allowances;
    private readonly IClinicReminderSettingsRepository _settings;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IMessagingAllowancePolicy _policy;
    private readonly ILogger<GetReminderAllowanceQueryHandler> _logger;

    public GetReminderAllowanceQueryHandler(
        IMessagingAllowanceRepository allowances,
        IClinicReminderSettingsRepository settings,
        ICurrentClinicResolver clinicResolver,
        IMessagingAllowancePolicy policy,
        ILogger<GetReminderAllowanceQueryHandler> logger)
    {
        _allowances = allowances;
        _settings = settings;
        _clinicResolver = clinicResolver;
        _policy = policy;
        _logger = logger;
    }

    public async Task<Result<ReminderAllowanceDto>> Handle(
        GetReminderAllowanceQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<ReminderAllowanceDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var clinicId = clinicResult.Value;
            var today = ClinicClock.ClinicToday();
            var monthKey = ClinicClock.MonthKey(today);

            var month = await _allowances.GetMonthAsync(clinicId, monthKey, cancellationToken);
            var settings = await _settings.GetByClinicIdAsync(clinicId, cancellationToken);

            // Part 4 stores a per-cabinet template state; until then it is genuinely unknown and the connection
            // alone decides — see the ⚠️ on MessagingSender.From for why null is not NotSubmitted.
            var senderState = MessagingSender.From(
                settings?.WhatsAppConnectionStatus ?? Domain.Enums.WhatsAppConnectionStatus.NotConnected,
                template: null);

            return Result<ReminderAllowanceDto>.Success(new ReminderAllowanceDto
            {
                Month = monthKey,
                MonthLabel = ClinicClock.MonthLabelFr(monthKey),
                Allowance = month?.AllowanceMessages,
                Consumed = month?.ConsumedMessages,
                Remaining = month?.RemainingMessages,
                // False where nothing was measured: an unknown is not an exhaustion, and telling a practice its
                // forfait is spent when we simply have no row is the AC-4.3 confusion on a screen instead of a queue.
                Exhausted = month?.IsExhausted ?? false,
                ResetsOn = ClinicClock.FirstDayOfNextMonth(today),
                Measured = month is not null,
                SenderState = senderState.ToString(),
                SenderStateLabel = MessagingSender.Label(senderState),
                SenderNumber = null, // see ReminderAllowanceDto.SenderNumber — nothing stores the number today
                ContactEmail = _policy.ContactEmail,
                ContactPhone = _policy.ContactPhone,
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error reading the WhatsApp reminder allowance.");
            return Result<ReminderAllowanceDto>.Failure("Erreur lors de la lecture du forfait de rappels.");
        }
    }
}
