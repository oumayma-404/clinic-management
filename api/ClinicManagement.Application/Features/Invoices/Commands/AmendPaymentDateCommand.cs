using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Invoices.Commands;

/// <summary>
/// Correct the day a payment was received — the caisse's own « ce n'était pas ce jour-là ».
///
/// <para><b>No document is touched, and none needs to be.</b> Every money read in the product attributes a
/// payment by <c>PaidOn</c> (la caisse and <c>GetInvoiceRevenueQuery</c> alike), while the note d'honoraires
/// legitimately keeps the day it was written. So this is not a fiscal edit and it does not want an avoir: it
/// corrects the record of when cash changed hands, which is the only thing that was wrong.</para>
///
/// <para>Refused on a banked cheque — that row is reconciled against a bank statement, and moving its date would
/// put the two out of agreement with nothing on screen to say so. The guard lives on the aggregate
/// (<c>Invoice.AmendPaymentDate</c>) so the fiche's own date-propagation and this command cannot disagree
/// about it.</para>
/// </summary>
public class AmendPaymentDateCommand : IRequest<Result<InvoiceDto>>
{
    public Guid Id { get; set; }
    public Guid PaymentId { get; set; }

    /// <summary>The day the money actually changed hands.</summary>
    public DateTime PaidOn { get; set; }
}

public class AmendPaymentDateCommandHandler : IRequestHandler<AmendPaymentDateCommand, Result<InvoiceDto>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<AmendPaymentDateCommandHandler> _logger;

    public AmendPaymentDateCommandHandler(
        IInvoiceRepository invoiceRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<AmendPaymentDateCommandHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<InvoiceDto>> Handle(AmendPaymentDateCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<InvoiceDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            // The same rule every other payment date goes through — a payment in the future, or before the clinic
            // existed, is a typo whichever door it arrives by.
            var dateError = PaymentDateRules.Validate(request.PaidOn, "La date de paiement");
            if (dateError is not null)
            {
                return Result<InvoiceDto>.Failure(dateError);
            }

            var invoice = await _invoiceRepository.GetByIdAsync(request.Id, cancellationToken);
            if (invoice == null || invoice.ClinicId != clinicResult.Value)
            {
                return Result<InvoiceDto>.Failure("Facture introuvable.");
            }

            invoice.AmendPaymentDate(request.PaymentId, request.PaidOn);

            await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Payment {PaymentId} on invoice {Number} moved to {Date:yyyy-MM-dd}",
                request.PaymentId, invoice.Number, request.PaidOn);

            var patient = await _patientRepository.GetByIdAsync(invoice.PatientId, cancellationToken);
            return Result<InvoiceDto>.Success(invoice.ToDto(patient?.GetFullName()));
        }
        catch (InvalidOperationException ex)
        {
            return Result<InvoiceDto>.Failure(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Result<InvoiceDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error amending payment {PaymentId} date on invoice {InvoiceId}", request.PaymentId, request.Id);
            return Result<InvoiceDto>.Failure("Erreur lors de la correction de la date du paiement.");
        }
    }
}
