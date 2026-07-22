using MediatR;
using Microsoft.Extensions.Logging;
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
            var invoiceByPatient = await _invoiceRepository.GetOutstandingByPatientAsync(clinicId, cancellationToken);
            var planByPatient = await _planRepository.GetInstallmentOutstandingByPatientAsync(clinicId, now, cancellationToken);

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

            var receivables = new List<ReceivableDto>();
            foreach (var (patientId, total) in totals)
            {
                var outstanding = InvoiceCalculator.RoundMoney(total);
                if (outstanding <= 0m)
                {
                    continue;
                }

                var patient = await _patientRepository.GetByIdAsync(patientId, cancellationToken);
                if (patient == null || patient.ClinicId != clinicId)
                {
                    continue; // defensive: skip anything outside the clinic
                }

                var overdue = oldestOverdue.GetValueOrDefault(patientId);
                int? daysOverdue = overdue is not null ? Math.Max(0, (now.Date - overdue.Value.Date).Days) : null;

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building the receivables list");
            return Result<List<ReceivableDto>>.Failure("Erreur lors du calcul des créances.");
        }
    }
}
