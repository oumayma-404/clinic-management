using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Billing.Queries;

/// <summary>The unified per-patient billing summary (« Solde patient ») — clinic-scoped.</summary>
public class GetPatientBillingSummaryQuery : IRequest<Result<PatientBillingSummaryDto>>
{
    public Guid PatientId { get; set; }
}

public class GetPatientBillingSummaryQueryHandler
    : IRequestHandler<GetPatientBillingSummaryQuery, Result<PatientBillingSummaryDto>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICnamBillingCalculator _cnamBillingCalculator;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetPatientBillingSummaryQueryHandler> _logger;

    public GetPatientBillingSummaryQueryHandler(
        IInvoiceRepository invoiceRepository,
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        ICnamBillingCalculator cnamBillingCalculator,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetPatientBillingSummaryQueryHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _cnamBillingCalculator = cnamBillingCalculator;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<PatientBillingSummaryDto>> Handle(
        GetPatientBillingSummaryQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<PatientBillingSummaryDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicId)
            {
                // Missing / cross-clinic patient → 404 (contract). NotFoundException → ExceptionMiddleware.
                throw new NotFoundException("Patient introuvable.");
            }

            var now = DateTime.UtcNow;

            // Invoices — only issued, non-cancelled ones carry a balance.
            var invoices = (await _invoiceRepository.GetFilteredAsync(clinicId, patientId: request.PatientId, cancellationToken: cancellationToken))
                .Where(i => i.Status != InvoiceStatus.Draft && i.Status != InvoiceStatus.Cancelled)
                .ToList();

            // Treatment plans — exclude cancelled.
            var plans = (await _planRepository.GetFilteredAsync(clinicId, patientId: request.PatientId, cancellationToken: cancellationToken))
                .Where(p => p.Status != TreatmentPlanStatus.Cancelled)
                .ToList();

            var invoiceOutstanding = InvoiceCalculator.RoundMoney(invoices.Sum(i => i.Outstanding));
            var installmentOutstanding = InvoiceCalculator.RoundMoney(plans.Sum(p => p.Outstanding));

            DateTime? oldestOverdue = plans
                .SelectMany(p => p.Installments)
                .Where(i => !i.IsPaid && i.DueDate < now)
                .Select(i => (DateTime?)i.DueDate)
                .DefaultIfEmpty(null)
                .Min();

            // Indicative CNAM split over everything billed (invoices at TTC, plans at total planned).
            var reimbursable = 0m;
            var totalBilled = 0m;
            foreach (var invoice in invoices)
            {
                var lines = invoice.Lines.Select(l => new CnamBillingLine(l.DentalActCodeId, l.LineTotalHt)).ToList();
                var split = await _cnamBillingCalculator.ComputeAsync(
                    lines, invoice.TotalTtc, patient.DateOfBirth, invoice.IssueDate ?? invoice.CreatedAt, cancellationToken);
                reimbursable += split.Reimbursable;
                totalBilled += invoice.TotalTtc;
            }
            foreach (var plan in plans)
            {
                var lines = plan.Items.Select(i => new CnamBillingLine(i.DentalActCodeId, i.PlannedCost)).ToList();
                var split = await _cnamBillingCalculator.ComputeAsync(
                    lines, plan.TotalPlanned, patient.DateOfBirth, plan.AcceptedDate ?? plan.CreatedAt, cancellationToken);
                reimbursable += split.Reimbursable;
                totalBilled += plan.TotalPlanned;
            }

            var cnamReimbursable = InvoiceCalculator.RoundMoney(reimbursable);
            var patientOutOfPocket = InvoiceCalculator.RoundMoney(Math.Max(0m, totalBilled - reimbursable));

            return Result<PatientBillingSummaryDto>.Success(new PatientBillingSummaryDto
            {
                InvoiceOutstanding = invoiceOutstanding,
                InstallmentOutstanding = installmentOutstanding,
                TotalOutstanding = InvoiceCalculator.RoundMoney(invoiceOutstanding + installmentOutstanding),
                OldestOverdueDate = oldestOverdue,
                CnamReimbursable = cnamReimbursable,
                PatientOutOfPocket = patientOutOfPocket,
            });
        }
        catch (NotFoundException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building billing summary for patient {PatientId}", request.PatientId);
            return Result<PatientBillingSummaryDto>.Failure("Erreur lors du calcul du solde patient.");
        }
    }
}
