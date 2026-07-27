using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Invoices.Queries;

/// <summary>Render a payment receipt (reçu) PDF for a single invoice payment.</summary>
public class GetPaymentReceiptPdfQuery : IRequest<Result<ReceiptPdfResult>>
{
    public Guid PaymentId { get; set; }
}

public class GetPaymentReceiptPdfQueryHandler : IRequestHandler<GetPaymentReceiptPdfQuery, Result<ReceiptPdfResult>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IPdfGenerationService _pdfGenerationService;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetPaymentReceiptPdfQueryHandler> _logger;

    public GetPaymentReceiptPdfQueryHandler(
        IInvoiceRepository invoiceRepository,
        IClinicRepository clinicRepository,
        IPatientRepository patientRepository,
        IPdfGenerationService pdfGenerationService,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetPaymentReceiptPdfQueryHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _clinicRepository = clinicRepository;
        _patientRepository = patientRepository;
        _pdfGenerationService = pdfGenerationService;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<ReceiptPdfResult>> Handle(GetPaymentReceiptPdfQuery request, CancellationToken cancellationToken)
    {
        var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
        if (clinicResult.IsFailure)
        {
            return Result<ReceiptPdfResult>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
        }
        var clinicId = clinicResult.Value;

        var invoice = await _invoiceRepository.GetByPaymentIdAsync(request.PaymentId, cancellationToken);
        if (invoice == null || invoice.ClinicId != clinicId)
        {
            throw new NotFoundException("Paiement introuvable.");
        }

        var payment = invoice.Payments.FirstOrDefault(p => p.Id == request.PaymentId)
            ?? throw new NotFoundException("Paiement introuvable.");

        try
        {
            var clinic = await _clinicRepository.GetByIdAsync(clinicId, cancellationToken);
            var patient = await _patientRepository.GetByIdAsync(invoice.PatientId, cancellationToken);

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
                For = invoice.Number != null ? $"Note d'honoraires N° {invoice.Number}" : "Note d'honoraires",
                // The balance AS OF this payment, not the live one — a receipt states what was true when it
                // was issued. Reprinting the first of two receipts used to show a figure that never applied,
                // and after a void it would show a balance that had grown.
                RemainingBalance = BalanceAsOf(invoice, payment),
                IsVoided = payment.IsVoided,
                VoidedOn = payment.VoidedAt,
                VoidReason = payment.VoidReason,
                Reference = invoice.Number,
            };

            var bytes = await _pdfGenerationService.GenerateReceiptPdfAsync(data, cancellationToken);
            var suffix = invoice.Number ?? request.PaymentId.ToString("N")[..8];
            return Result<ReceiptPdfResult>.Success(new ReceiptPdfResult
            {
                Content = bytes,
                FileName = $"recu-{suffix}.pdf",
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating receipt PDF for payment {PaymentId}", request.PaymentId);
            return Result<ReceiptPdfResult>.Failure("Erreur lors de la génération du reçu.");
        }
    }

    /// <summary>The invoice's balance immediately after <paramref name="payment"/> was received.</summary>
    private static decimal BalanceAsOf(Domain.Entities.Invoice invoice, Domain.Entities.Payment payment)
    {
        var collectedByThen = invoice.Payments
            .Where(p => !p.IsVoided
                        && (p.PaidOn < payment.PaidOn
                            || (p.PaidOn == payment.PaidOn && p.CreatedAt <= payment.CreatedAt)))
            .Sum(p => p.Amount);

        return Math.Max(0m, invoice.TotalTtc - Domain.Services.InvoiceCalculator.RoundMoney(collectedByThen));
    }
}
