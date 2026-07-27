using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Invoices.Commands;

/// <summary>
/// Establish an avoir (credit note) against a paid/partially-paid invoice — the lawful correction path
/// for collected cash (finding #8). Numbering is gapless per-clinic-per-year (unique index + retry). The
/// invoice keeps its number/status; the caisse/recettes net the refund out.
/// </summary>
public class CreateCreditNoteCommand : IRequest<Result<CreditNoteDto>>
{
    public Guid InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Method { get; set; }
    public DateTime? RefundedOn { get; set; }
}

public class CreateCreditNoteCommandHandler : IRequestHandler<CreateCreditNoteCommand, Result<CreditNoteDto>>
{
    private const int MaxNumberingAttempts = 5;

    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ICreditNoteRepository _creditNoteRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CreateCreditNoteCommandHandler> _logger;

    public CreateCreditNoteCommandHandler(
        IInvoiceRepository invoiceRepository,
        ICreditNoteRepository creditNoteRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<CreateCreditNoteCommandHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _creditNoteRepository = creditNoteRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<CreditNoteDto>> Handle(CreateCreditNoteCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<CreditNoteDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            // Tenant isolation: an invoice from another clinic reads as "not found".
            var invoice = await _invoiceRepository.GetByIdAsync(request.InvoiceId, cancellationToken);
            if (invoice == null || invoice.ClinicId != clinicId)
            {
                return Result<CreditNoteDto>.Failure("Facture introuvable.");
            }

            // The gate is now the aggregate's own CanCreateCreditNote — "a real invoice with collected money
            // on it" — instead of a Paid|PartiallyPaid status whitelist maintained here. The two are close
            // but not identical, and the whitelist was the copy the UI never saw: InvoiceDto.CanCreateAvoir
            // has always been fed by CanCreateCreditNote, so the button and the endpoint were free to
            // disagree about any state the whitelist forgot. One predicate, one answer.
            if (!invoice.CanCreateCreditNote)
            {
                return Result<CreditNoteDto>.Failure(
                    "Un avoir ne peut être établi que sur une facture émise dont un montant a été encaissé.");
            }

            if (request.Amount <= 0)
            {
                return Result<CreditNoteDto>.Failure("Le montant de l'avoir doit être supérieur à 0.");
            }
            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                return Result<CreditNoteDto>.Failure("Le motif de l'avoir est requis.");
            }

            // An avoir offsets collected cash — the running total of avoirs must never exceed what was collected.
            var alreadyCredited = await _creditNoteRepository.GetTotalForInvoiceAsync(invoice.Id, cancellationToken);
            var creditable = InvoiceCalculator.RoundMoney(invoice.AmountCollected - alreadyCredited);
            if (request.Amount > creditable)
            {
                return Result<CreditNoteDto>.Failure(
                    "Le montant de l'avoir dépasse le montant encaissé restant à créditer.");
            }

            // An unrecognised method used to be silently dropped to null, so a typo produced an avoir with no
            // recorded means of refund and nobody was told.
            PaymentMethod? method = null;
            if (!string.IsNullOrWhiteSpace(request.Method))
            {
                if (!Enum.TryParse<PaymentMethod>(request.Method, ignoreCase: true, out var parsed))
                {
                    return Result<CreditNoteDto>.Failure("Mode de remboursement invalide.");
                }
                method = parsed;
            }

            var refundedOn = request.RefundedOn ?? DateTime.UtcNow;
            var dateError = PaymentDateRules.Validate(refundedOn, "La date de remboursement");
            if (dateError != null)
            {
                return Result<CreditNoteDto>.Failure(dateError);
            }
            var year = DateTime.UtcNow.Year;
            CreditNote? creditNote = null;

            for (var attempt = 1; attempt <= MaxNumberingAttempts; attempt++)
            {
                var nextSequence = await _creditNoteRepository.GetMaxSequenceForYearAsync(clinicId, year, cancellationToken) + 1;
                var number = $"{year}-{nextSequence:D4}";

                if (creditNote == null)
                {
                    creditNote = new CreditNote(
                        Guid.NewGuid(), clinicId, invoice.Id, number, request.Amount, request.Reason, method, refundedOn);
                    await _creditNoteRepository.AddAsync(creditNote, cancellationToken);
                }
                else
                {
                    creditNote.SetNumber(number);
                }

                try
                {
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("Created avoir {Number} for invoice {InvoiceId}", creditNote.Number, invoice.Id);
                    return Result<CreditNoteDto>.Success(creditNote.ToDto(invoice));
                }
                catch (DbUpdateException) when (attempt < MaxNumberingAttempts)
                {
                    _logger.LogWarning("Avoir number {Number} collided on attempt {Attempt}; recomputing", number, attempt);
                }
            }

            return Result<CreditNoteDto>.Failure("Impossible d'attribuer un numéro d'avoir unique. Veuillez réessayer.");
        }
        catch (ArgumentException ex)
        {
            return Result<CreditNoteDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error creating credit note for invoice {InvoiceId}", request.InvoiceId);
            return Result<CreditNoteDto>.Failure("Erreur lors de l'établissement de l'avoir.");
        }
    }
}
