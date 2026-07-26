using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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

            if (invoice.Status != InvoiceStatus.Paid && invoice.Status != InvoiceStatus.PartiallyPaid)
            {
                return Result<CreditNoteDto>.Failure("Un avoir ne peut être établi que sur une facture (partiellement) payée.");
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

            PaymentMethod? method = null;
            if (!string.IsNullOrWhiteSpace(request.Method)
                && Enum.TryParse<PaymentMethod>(request.Method, ignoreCase: true, out var parsed))
            {
                method = parsed;
            }

            var refundedOn = request.RefundedOn ?? DateTime.UtcNow;
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
                    return Result<CreditNoteDto>.Success(ToDto(creditNote));
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating credit note for invoice {InvoiceId}", request.InvoiceId);
            return Result<CreditNoteDto>.Failure("Erreur lors de l'établissement de l'avoir.");
        }
    }

    private static CreditNoteDto ToDto(CreditNote c) => new()
    {
        Id = c.Id,
        InvoiceId = c.InvoiceId,
        Number = c.Number,
        IssueDate = c.IssueDate,
        Amount = c.Amount,
        Reason = c.Reason,
        Method = c.Method?.ToString(),
        RefundedOn = c.RefundedOn,
    };
}
