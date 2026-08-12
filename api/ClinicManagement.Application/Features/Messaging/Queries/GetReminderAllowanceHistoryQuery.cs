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
/// The cabinet's WhatsApp reminder consumption month by month — the current Tunisian month plus the twelve before
/// it, newest first (AC-2.3).
///
/// <para><b>⚠️ Floored, not padded (D-5).</b> The list starts at the <b>later</b> of the cabinet's creation month and
/// its earliest counting row, and every month below that floor is <b>omitted entirely</b> — the same treatment AC-2.4
/// gives a month before the cabinet existed. Without the second term, every cabinet that predates the rollout would
/// read « non mesuré » for the twelve months before this feature shipped, which is a statement about our counting
/// applied to months there was nothing to count in. It needs no config key and no floor column because the rollout
/// migration wrote that first row (FR-3).</para>
///
/// <para><b>⚠️ A gap INSIDE the range still reads « non mesuré », and that is the point.</b> The floor removes months
/// nobody promised to count; a hole above it means the daily pass did not run (FR-1a), which is exactly what the
/// reading is for.</para>
/// </summary>
public class GetReminderAllowanceHistoryQuery : IRequest<Result<ReminderAllowanceHistoryDto>>
{
    /// <summary>
    /// How many months precede the current one. Twelve, per AC-2.3 — « la même période l'an dernier » is the
    /// comparison a practice makes, and the read is bounded rather than all-time (NFR performance).
    /// </summary>
    public const int PrecedingMonths = 12;
}

public class GetReminderAllowanceHistoryQueryHandler
    : IRequestHandler<GetReminderAllowanceHistoryQuery, Result<ReminderAllowanceHistoryDto>>
{
    private readonly IMessagingAllowanceRepository _allowances;
    private readonly IClinicRepository _clinics;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetReminderAllowanceHistoryQueryHandler> _logger;

    public GetReminderAllowanceHistoryQueryHandler(
        IMessagingAllowanceRepository allowances,
        IClinicRepository clinics,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetReminderAllowanceHistoryQueryHandler> logger)
    {
        _allowances = allowances;
        _clinics = clinics;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<ReminderAllowanceHistoryDto>> Handle(
        GetReminderAllowanceHistoryQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<ReminderAllowanceHistoryDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var clinicId = clinicResult.Value;
            var currentMonth = ClinicClock.CurrentMonthKey();

            // Newest first, current month included — the order the table renders in, decided here rather than in
            // the browser so « the twelve preceding months » has one meaning.
            var window = new List<string> { currentMonth };
            window.AddRange(
                ClinicClock.PrecedingMonthKeys(currentMonth, GetReminderAllowanceHistoryQuery.PrecedingMonths));

            var rows = await _allowances.GetMonthsAsync(clinicId, window[^1], cancellationToken);
            var byMonth = rows.ToDictionary(r => r.MonthKey, StringComparer.Ordinal);

            var floor = await ResolveFloorAsync(clinicId, rows.Select(r => r.MonthKey), cancellationToken);

            var months = window
                .Where(key => floor is null || string.CompareOrdinal(key, floor) >= 0)
                .Select(key => ToDto(key, byMonth.GetValueOrDefault(key)))
                .ToList();

            return Result<ReminderAllowanceHistoryDto>.Success(new ReminderAllowanceHistoryDto(months));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error reading the WhatsApp reminder allowance history.");
            return Result<ReminderAllowanceHistoryDto>.Failure(
                "Erreur lors de la lecture de l'historique du forfait de rappels.");
        }
    }

    private static ReminderAllowanceMonthDto ToDto(string monthKey, Domain.Entities.ClinicMessagingMonth? row) => new(
        monthKey,
        ClinicClock.MonthLabelFr(monthKey),
        row?.AllowanceMessages,
        // A measured 0 stays 0 and reads « 0 rappel envoyé » (AC-2.4). Only an absent row is null.
        row?.ConsumedMessages,
        row is not null);

    /// <summary>
    /// D-5's floor: the <b>later</b> of the cabinet's creation month and its earliest counting row, or null when
    /// neither is knowable — in which case the whole window is shown rather than nothing, since an unreadable
    /// creation date must not silently empty the table (EC-12's rule, one field over).
    /// </summary>
    private async Task<string?> ResolveFloorAsync(
        Guid clinicId, IEnumerable<string> measuredMonths, CancellationToken cancellationToken)
    {
        var clinic = await _clinics.GetByIdAsync(clinicId, cancellationToken);
        var creationMonth = clinic is null
            ? null
            : ClinicClock.MonthKey(ClinicClock.ToClinicLocal(clinic.CreatedAt));

        var earliestMeasured = measuredMonths.OrderBy(m => m, StringComparer.Ordinal).FirstOrDefault();

        if (creationMonth is null)
        {
            return earliestMeasured;
        }

        return earliestMeasured is null || string.CompareOrdinal(creationMonth, earliestMeasured) >= 0
            ? creationMonth
            : earliestMeasured;
    }
}
