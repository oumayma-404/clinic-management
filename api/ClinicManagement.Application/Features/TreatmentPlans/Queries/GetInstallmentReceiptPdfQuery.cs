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

    /// <summary>
    /// Which payment to print. Required now that an échéance can hold several: the receipt used to print the
    /// CUMULATIVE AmountPaid dated LastPaidOn, so a second partial payment silently reissued a receipt for the
    /// running total rather than for the money just handed over.
    /// </summary>
    public Guid PaymentId { get; set; }
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

        var payment = installment.Payments.FirstOrDefault(p => p.Id == request.PaymentId)
            ?? throw new NotFoundException("Paiement introuvable pour cette échéance.");

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
                PaidOn = payment.PaidOn,
                Amount = payment.Amount,
                Method = PaymentMethodLabels.ToFrench(payment.Method),
                For = $"{planLabel} — échéance du {installment.DueDate:dd/MM/yyyy}",
                // The balance AS OF this payment, not the live one. Printing the current balance made a
                // reprint of the first of two receipts show a figure that never applied when it was issued —
                // and after a void it would show a balance that had GROWN.
                RemainingBalance = BalanceAsOf(installment, payment),
                IsVoided = payment.IsVoided,
                VoidedOn = payment.VoidedAt,
                VoidReason = payment.VoidReason,
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

    /// <summary>
    /// The échéance's balance immediately after <paramref name="payment"/> was received — i.e. counting only
    /// the live payments up to and including it. A receipt states what was true when it was issued.
    /// </summary>
    private static decimal BalanceAsOf(Domain.Entities.Installment installment, Domain.Entities.InstallmentPayment payment)
    {
        var collectedByThen = installment.Payments
            .Where(p => !p.IsVoided
                        && (p.PaidOn < payment.PaidOn
                            || (p.PaidOn == payment.PaidOn && p.CreatedAt <= payment.CreatedAt)))
            .Sum(p => p.Amount);

        return Math.Max(0m, installment.Amount - Domain.Services.InvoiceCalculator.RoundMoney(collectedByThen));
    }
}
