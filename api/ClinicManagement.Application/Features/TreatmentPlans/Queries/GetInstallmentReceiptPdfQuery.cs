using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans.Queries;

/// <summary>Render a payment receipt (reçu) PDF for a treatment-plan installment payment.</summary>
public class GetInstallmentReceiptPdfQuery : IRequest<Result<ReceiptPdfResult>>
{
    public Guid PlanId { get; set; }
    public Guid InstallmentId { get; set; }
}

public class GetInstallmentReceiptPdfQueryHandler : IRequestHandler<GetInstallmentReceiptPdfQuery, Result<ReceiptPdfResult>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IPdfGenerationService _pdfGenerationService;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetInstallmentReceiptPdfQueryHandler> _logger;

    public GetInstallmentReceiptPdfQueryHandler(
        ITreatmentPlanRepository planRepository,
        IClinicRepository clinicRepository,
        IPatientRepository patientRepository,
        IPdfGenerationService pdfGenerationService,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetInstallmentReceiptPdfQueryHandler> logger)
    {
        _planRepository = planRepository;
        _clinicRepository = clinicRepository;
        _patientRepository = patientRepository;
        _pdfGenerationService = pdfGenerationService;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<ReceiptPdfResult>> Handle(GetInstallmentReceiptPdfQuery request, CancellationToken cancellationToken)
    {
        var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
        if (clinicResult.IsFailure)
        {
            return Result<ReceiptPdfResult>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
        }
        var clinicId = clinicResult.Value;

        var plan = await _planRepository.GetByIdAsync(request.PlanId, cancellationToken);
        if (plan == null || plan.ClinicId != clinicId)
        {
            throw new NotFoundException("Plan de traitement introuvable.");
        }

        var installment = plan.Installments.FirstOrDefault(i => i.Id == request.InstallmentId)
            ?? throw new NotFoundException("Échéance introuvable.");

        if (installment.LastPaidOn is null || installment.LastMethod is null)
        {
            throw new NotFoundException("Aucun paiement enregistré pour cette échéance.");
        }

        try
        {
            var clinic = await _clinicRepository.GetByIdAsync(clinicId, cancellationToken);
            var patient = await _patientRepository.GetByIdAsync(plan.PatientId, cancellationToken);

            var planLabel = string.IsNullOrWhiteSpace(plan.Number) ? plan.Title : $"Devis N° {plan.Number}";
            var data = new ReceiptPdfData
            {
                ClinicName = clinic?.Name ?? string.Empty,
                ClinicAddress = clinic?.Address,
                ClinicPhone = clinic?.Phone,
                MatriculeFiscal = clinic?.MatriculeFiscal,
                PatientName = patient?.GetFullName() ?? string.Empty,
                PaidOn = installment.LastPaidOn.Value,
                Amount = installment.AmountPaid,
                Method = PaymentMethodLabels.ToFrench(installment.LastMethod.Value),
                For = $"{planLabel} — échéance du {installment.DueDate:dd/MM/yyyy}",
                RemainingBalance = installment.Outstanding,
                Reference = plan.Number,
            };

            var bytes = await _pdfGenerationService.GenerateReceiptPdfAsync(data, cancellationToken);
            var suffix = string.IsNullOrWhiteSpace(plan.Number) ? plan.Id.ToString("N")[..8] : plan.Number;
            return Result<ReceiptPdfResult>.Success(new ReceiptPdfResult
            {
                Content = bytes,
                FileName = $"recu-echeance-{suffix}.pdf",
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating installment receipt PDF for plan {PlanId} installment {InstallmentId}",
                request.PlanId, request.InstallmentId);
            return Result<ReceiptPdfResult>.Failure("Erreur lors de la génération du reçu.");
        }
    }
}
