using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.Features.Invoices.Commands;

/// <summary>Record a payment against an issued invoice. Over-payment is refused; reaching the TTC marks it paid.</summary>
public class RecordPaymentCommand : IRequest<Result<InvoiceDto>>
{
    public Guid InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; } = string.Empty;
    public DateTime PaidOn { get; set; }

    /// <summary>
    /// The cheque's number, bank and due date (L8) — all optional, and all refused for any method other than
    /// <c>Cheque</c>. Post-dated cheques are ubiquitous in Tunisian practice and had nowhere to be recorded.
    /// </summary>
    public string? ChequeNumber { get; set; }

    /// <inheritdoc cref="ChequeNumber"/>
    public string? ChequeBankName { get; set; }

    /// <inheritdoc cref="ChequeNumber"/>
    public DateTime? ChequeDueDate { get; set; }
}

public class RecordPaymentCommandHandler : IRequestHandler<RecordPaymentCommand, Result<InvoiceDto>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<RecordPaymentCommandHandler> _logger;

    public RecordPaymentCommandHandler(
        IInvoiceRepository invoiceRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<RecordPaymentCommandHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<InvoiceDto>> Handle(RecordPaymentCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (!Enum.TryParse<PaymentMethod>(request.Method, ignoreCase: true, out var method))
            {
                return Result<InvoiceDto>.Failure("Mode de paiement invalide.");
            }

            // PaidOn is a non-nullable DateTime with no validation anywhere, so a client that omits the key
            // posts 0001-01-01. That payment increments the collected total but is invisible in every cash
            // window forever — a permanent, silent divergence between the two ledgers.
            var paymentDateError = PaymentDateRules.Validate(request.PaidOn, "La date du paiement");
            if (paymentDateError is not null)
            {
                return Result<InvoiceDto>.Failure(paymentDateError);
            }

            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<InvoiceDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var invoice = await _invoiceRepository.GetByIdAsync(request.InvoiceId, cancellationToken);
            if (invoice == null || invoice.ClinicId != clinicId)
            {
                return Result<InvoiceDto>.Failure("Facture introuvable.");
            }

            // Throws `ArgumentException` when cheque details arrive on a non-cheque payment, which the catch below
            // already turns into the French `Result.Failure` this endpoint returns for every other refusal.
            var cheque = ChequeDetails.For(
                method, request.ChequeNumber, request.ChequeBankName, request.ChequeDueDate);

            invoice.RecordPayment(request.Amount, method, request.PaidOn, cheque: cheque);

            await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

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
            _logger.LogError(ex, "Error recording payment for invoice {InvoiceId}", request.InvoiceId);
            return Result<InvoiceDto>.Failure("Erreur lors de l'enregistrement du paiement.");
        }
    }
}
