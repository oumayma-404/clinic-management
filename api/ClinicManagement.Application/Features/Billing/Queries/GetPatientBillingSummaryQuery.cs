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

            // Plans already billed into an issued invoice (devis→facture bridge) are represented by that
            // invoice — count the invoice, not the plan, so the same acts aren't counted twice.
            var billedPlanIds = PlanBillingRules.BilledPlanIds(invoices);

            // Treatment plans — count only committed plans (a Draft devis is an unaccepted quote, not debt),
            // and skip any already billed to an invoice above. Both rules live in PlanBillingRules, shared
            // with « Créances », la caisse and the dashboard so the four reads report the same figure.
            var plans = (await _planRepository.GetFilteredAsync(clinicId, patientId: request.PatientId, cancellationToken: cancellationToken))
                .Where(p => PlanBillingRules.CarriesDebt(p.Status) && !billedPlanIds.Contains(p.Id))
                .ToList();

            var invoiceOutstanding = InvoiceCalculator.RoundMoney(invoices.Sum(i => i.Outstanding));
            var installmentOutstanding = InvoiceCalculator.RoundMoney(plans.Sum(p => p.Outstanding));

            DateTime? oldestOverdue = plans
                .SelectMany(p => p.Installments)
                // Compared by CALENDAR DAY, not instant. Due dates are stored at midnight, so `DueDate < now`
                // made an échéance overdue from 00:00 on its own due date — a full day early. It is late only
                // once its day has passed. Matches GetPatientsToRecallQuery, which already truncates.
                .Where(i => !i.IsPaid && i.DueDate.Date < now.Date)
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
