using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Billing.Queries;

/// <summary>The clinic-wide « Créances » (accounts-receivable) list — patients with a positive balance,
/// sorted by amount owed (descending). Clinic-scoped.</summary>
public class GetReceivablesQuery : IRequest<Result<List<ReceivableDto>>>
{
}

public class GetReceivablesQueryHandler : IRequestHandler<GetReceivablesQuery, Result<List<ReceivableDto>>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetReceivablesQueryHandler> _logger;

    public GetReceivablesQueryHandler(
        IInvoiceRepository invoiceRepository,
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetReceivablesQueryHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<List<ReceivableDto>>> Handle(GetReceivablesQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<List<ReceivableDto>>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var now = DateTime.UtcNow;
            // The clinic's calendar day for the « depuis N jours » arithmetic below (AC-P6.4). `now` stays UTC —
            // the repository call takes an instant, not a date.
            var clinicToday = ClinicClock.ClinicToday(now);
            var invoiceByPatient = await _invoiceRepository.GetOutstandingByPatientAsync(clinicId, cancellationToken);

            // A plan bridged into a real invoice is already counted on the invoice track above; counting its
            // échéancier too would bill the same acts twice here while « Solde patient » counts them once.
            // Same shared rule, one light projection (no lines/payments loaded).
            var billedPlanIds = PlanBillingRules.BilledPlanIds(
                await _invoiceRepository.GetTreatmentPlanLinksAsync(clinicId, cancellationToken));
            var planByPatient = await _planRepository.GetInstallmentOutstandingByPatientAsync(
                clinicId, now, billedPlanIds, cancellationToken);

            // Merge the two tracks per patient.
            var totals = new Dictionary<Guid, decimal>();
            var oldestOverdue = new Dictionary<Guid, DateTime?>();

            foreach (var row in invoiceByPatient)
            {
                totals[row.PatientId] = totals.GetValueOrDefault(row.PatientId) + row.Outstanding;
            }
            foreach (var row in planByPatient)
            {
                totals[row.PatientId] = totals.GetValueOrDefault(row.PatientId) + row.Outstanding;
                if (row.OldestOverdueDueDate is not null)
                {
                    oldestOverdue[row.PatientId] = row.OldestOverdueDueDate;
                }
            }

            // Round and drop the settled rows FIRST, then resolve names in one round trip (AC-P6.21). This loop
            // used to call `GetByIdAsync` per patient — one query, plus its flags and both history collections,
            // for every patient with a balance, on the screen the clinic opens to chase money.
            var owing = totals
                .Select(kv => (PatientId: kv.Key, Outstanding: InvoiceCalculator.RoundMoney(kv.Value)))
                .Where(t => t.Outstanding > 0m)
                .ToList();

            var patients = await _patientRepository.GetByIdsAsync(
                clinicId, owing.Select(t => t.PatientId).ToList(), cancellationToken);

            var receivables = new List<ReceivableDto>();
            foreach (var (patientId, outstanding) in owing)
            {
                // Absent = outside the clinic or deleted; the repository already applied the clinic filter, so
                // this keeps the same defensive skip without a per-row ClinicId comparison.
                if (!patients.TryGetValue(patientId, out var patient))
                {
                    continue;
                }

                var overdue = oldestOverdue.GetValueOrDefault(patientId);
                int? daysOverdue = overdue is not null ? Math.Max(0, (clinicToday - overdue.Value.Date).Days) : null;

                receivables.Add(new ReceivableDto
                {
                    PatientId = patientId,
                    PatientName = patient.GetFullName(),
                    TotalOutstanding = outstanding,
                    OldestOverdueDate = overdue,
                    DaysOverdue = daysOverdue,
                });
            }

            var sorted = receivables
                .OrderByDescending(r => r.TotalOutstanding)
                .ThenBy(r => r.PatientName)
                .ToList();

            return Result<List<ReceivableDto>>.Success(sorted);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error building the receivables list");
            return Result<List<ReceivableDto>>.Failure("Erreur lors du calcul des créances.");
        }
    }
}
