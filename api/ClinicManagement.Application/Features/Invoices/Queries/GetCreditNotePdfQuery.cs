using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Invoices.Queries;

/// <summary>
/// Render the avoir (credit note) PDF — the document the clinic hands the patient as proof of the refund.
/// Without it the correction existed only as a row in the caisse arithmetic.
/// </summary>
public class GetCreditNotePdfQuery : IRequest<Result<AvoirPdfResult>>
{
    public Guid Id { get; set; }
}

public class GetCreditNotePdfQueryHandler : IRequestHandler<GetCreditNotePdfQuery, Result<AvoirPdfResult>>
{
    private readonly ICreditNoteRepository _creditNoteRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IPdfGenerationService _pdfGenerationService;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetCreditNotePdfQueryHandler> _logger;

    public GetCreditNotePdfQueryHandler(
        ICreditNoteRepository creditNoteRepository,
        IInvoiceRepository invoiceRepository,
        IClinicRepository clinicRepository,
        IPatientRepository patientRepository,
        IPdfGenerationService pdfGenerationService,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetCreditNotePdfQueryHandler> logger)
    {
        _creditNoteRepository = creditNoteRepository;
        _invoiceRepository = invoiceRepository;
        _clinicRepository = clinicRepository;
        _patientRepository = patientRepository;
        _pdfGenerationService = pdfGenerationService;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<AvoirPdfResult>> Handle(GetCreditNotePdfQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<AvoirPdfResult>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var creditNote = await _creditNoteRepository.GetByIdAsync(request.Id, cancellationToken);
            if (creditNote == null || creditNote.ClinicId != clinicId)
            {
                return Result<AvoirPdfResult>.Failure("Avoir introuvable.");
            }

            // The corrected invoice is not optional decoration: an avoir that does not cite the document it
            // corrects is not a usable fiscal piece, and the VAT split can only come from the invoice's frozen
            // posture. A dangling InvoiceId therefore fails rather than rendering half a document.
            var invoice = await _invoiceRepository.GetByIdAsync(creditNote.InvoiceId, cancellationToken);
            if (invoice == null || invoice.ClinicId != clinicId)
            {
                return Result<AvoirPdfResult>.Failure("La facture corrigée par cet avoir est introuvable.");
            }

            var clinic = await _clinicRepository.GetByIdAsync(clinicId, cancellationToken);
            var patient = await _patientRepository.GetByIdAsync(invoice.PatientId, cancellationToken);

            var data = BuildPdfData(creditNote, invoice, clinic, patient?.GetFullName() ?? string.Empty);
            var bytes = await _pdfGenerationService.GenerateAvoirPdfAsync(data, cancellationToken);

            return Result<AvoirPdfResult>.Success(new AvoirPdfResult
            {
                Content = bytes,
                FileName = $"avoir-{creditNote.Number}.pdf"
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error generating PDF for credit note {CreditNoteId}", request.Id);
            return Result<AvoirPdfResult>.Failure("Erreur lors de la génération du PDF.");
        }
    }

    private static AvoirPdfData BuildPdfData(CreditNote creditNote, Invoice invoice, Clinic? clinic, string patientName)
    {
        // The avoir stores one TTC scalar, so the split is derived by applying the corrected invoice's frozen
        // VAT posture to it — the same rate the patient was charged. Not applicable ⇒ the whole amount is HT,
        // which is how the invoice itself renders in that case.
        decimal amountHt;
        decimal amountVat;
        if (invoice.VatApplicable && invoice.VatRate > 0m)
        {
            amountHt = InvoiceCalculator.RoundMoney(creditNote.Amount / (1m + invoice.VatRate / 100m));
            amountVat = InvoiceCalculator.RoundMoney(creditNote.Amount - amountHt);
        }
        else
        {
            amountHt = creditNote.Amount;
            amountVat = 0m;
        }

        return new AvoirPdfData
        {
            ClinicName = clinic?.Name ?? string.Empty,
            ClinicAddress = clinic?.Address,
            ClinicPhone = clinic?.Phone,
            MatriculeFiscal = clinic?.MatriculeFiscal,
            PatientName = patientName,
            Number = creditNote.Number,
            IssueDate = creditNote.IssueDate,
            RefundedOn = creditNote.RefundedOn,
            InvoiceNumber = invoice.Number,
            InvoiceIssueDate = invoice.IssueDate,
            AmountHt = amountHt,
            AmountVat = amountVat,
            AmountTtc = creditNote.Amount,
            VatApplicable = invoice.VatApplicable,
            VatRate = invoice.VatRate,
            Reason = creditNote.Reason,
            Method = creditNote.Method.HasValue
                ? PaymentMethodLabels.ToFrench(creditNote.Method.Value)
                : null,
            CorrectedInvoiceIsTtnRegistered = InvoiceMappingExtensions.IsTtnRegistered(invoice)
        };
    }
}
