using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Invoices.Commands;

/// <summary>
/// Issue a draft invoice: assign the per-clinic sequential number (<c>AAAA-NNNN</c>) and freeze the
/// clinic's VAT/stamp settings + totals. Numbering is gapless and concurrency-safe (unique index +
/// recompute-and-retry on collision).
/// </summary>
public class IssueInvoiceCommand : IRequest<Result<InvoiceDto>>
{
    public Guid Id { get; set; }
}

public class IssueInvoiceCommandHandler : IRequestHandler<IssueInvoiceCommand, Result<InvoiceDto>>
{
    // Bounds the recompute-and-retry loop when concurrent issuances collide on the unique number index.
    private const int MaxNumberingAttempts = 5;

    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<IssueInvoiceCommandHandler> _logger;

    public IssueInvoiceCommandHandler(
        IInvoiceRepository invoiceRepository,
        IClinicRepository clinicRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<IssueInvoiceCommandHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _clinicRepository = clinicRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<InvoiceDto>> Handle(IssueInvoiceCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<InvoiceDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var invoice = await _invoiceRepository.GetByIdAsync(request.Id, cancellationToken);
            if (invoice == null || invoice.ClinicId != clinicId)
            {
                return Result<InvoiceDto>.Failure("Facture introuvable.");
            }

            var clinic = await _clinicRepository.GetByIdAsync(clinicId, cancellationToken);
            if (clinic == null)
            {
                return Result<InvoiceDto>.Failure("Cabinet introuvable.");
            }

            var year = DateTime.UtcNow.Year;

            for (var attempt = 1; attempt <= MaxNumberingAttempts; attempt++)
            {
                var nextSequence = await _invoiceRepository.GetMaxSequenceForYearAsync(clinicId, year, cancellationToken) + 1;
                var number = $"{year}-{nextSequence:D4}";

                if (attempt == 1)
                {
                    invoice.Issue(number, clinic.VatApplicable, clinic.VatRate, clinic.StampDutyEnabled, clinic.StampDutyAmount);
                }
                else
                {
                    // A concurrent issuance took our number; keep the frozen totals, reassign the number only.
                    invoice.SetIssuedNumber(number);
                }

                await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

                try
                {
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("Issued invoice {InvoiceId} as {Number}", invoice.Id, invoice.Number);
                    await TryAutoQueueElFatooraAsync(invoice, clinic, cancellationToken);
                    return Result<InvoiceDto>.Success(await MapAsync(invoice, cancellationToken));
                }
                catch (DbUpdateException) when (attempt < MaxNumberingAttempts)
                {
                    _logger.LogWarning(
                        "Invoice number {Number} collided on issue attempt {Attempt}; recomputing", number, attempt);
                }
            }

            return Result<InvoiceDto>.Failure("Impossible d'attribuer un numéro de facture unique. Veuillez réessayer.");
        }
        catch (InvalidOperationException ex)
        {
            return Result<InvoiceDto>.Failure(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Result<InvoiceDto>.Failure(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error issuing invoice {InvoiceId}", request.Id);
            return Result<InvoiceDto>.Failure("Erreur lors de l'émission de la facture.");
        }
    }

    /// <summary>
    /// Best-effort (P1-E): for a clinic that has enabled TTN « El Fatoora », queue the freshly-issued
    /// invoice into the outbox so issuing and e-invoicing are one action — the legally-mandated e-invoice
    /// no longer needs a second manual "envoyer à El Fatoora" click. The recurring <c>EInvoiceOutboxJob</c>
    /// (or a manual submit) dispatches it. Runs post-commit: never fails issuance, and a clinic that has
    /// NOT enabled El Fatoora queues nothing (no stuck row).
    /// </summary>
    private async Task TryAutoQueueElFatooraAsync(Domain.Entities.Invoice invoice, Domain.Entities.Clinic clinic, CancellationToken cancellationToken)
    {
        if (!clinic.TtnEInvoicingEnabled || !invoice.CanSubmitToElFatoora)
        {
            return;
        }

        try
        {
            invoice.QueueForElFatoora();
            await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Auto-queued invoice {InvoiceId} for El Fatoora on issue.", invoice.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to auto-queue invoice {InvoiceId} for El Fatoora on issue; leaving it un-queued.", invoice.Id);
        }
    }

    private async Task<InvoiceDto> MapAsync(Domain.Entities.Invoice invoice, CancellationToken cancellationToken)
    {
        var patient = await _patientRepository.GetByIdAsync(invoice.PatientId, cancellationToken);
        return invoice.ToDto(patient?.GetFullName());
    }
}
