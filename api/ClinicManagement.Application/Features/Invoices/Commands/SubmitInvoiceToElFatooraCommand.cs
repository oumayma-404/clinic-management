using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Invoices.Commands;

/// <summary>
/// Send an issued invoice to TTN « El Fatoora » (US-1). Queues it into the offline outbox and, when the
/// server has internet, attempts the dispatch inline; offline, the recurring outbox job dispatches it once
/// connectivity returns (US-2). Idempotent per invoice (US-5 retry re-runs this on a Rejected/Failed one).
/// </summary>
public class SubmitInvoiceToElFatooraCommand : IRequest<Result<InvoiceDto>>
{
    public Guid Id { get; set; }
}

public class SubmitInvoiceToElFatooraCommandHandler : IRequestHandler<SubmitInvoiceToElFatooraCommand, Result<InvoiceDto>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IEInvoiceService _eInvoiceService;
    private readonly IInternetProbe _internetProbe;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SubmitInvoiceToElFatooraCommandHandler> _logger;

    public SubmitInvoiceToElFatooraCommandHandler(
        IInvoiceRepository invoiceRepository,
        IClinicRepository clinicRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IEInvoiceService eInvoiceService,
        IInternetProbe internetProbe,
        IUnitOfWork unitOfWork,
        ILogger<SubmitInvoiceToElFatooraCommandHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _clinicRepository = clinicRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _eInvoiceService = eInvoiceService;
        _internetProbe = internetProbe;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<InvoiceDto>> Handle(SubmitInvoiceToElFatooraCommand request, CancellationToken cancellationToken)
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

            // Idempotent: an invoice already validated or mid-flight at TTN is not re-sent.
            if (invoice.EInvoiceStatus is EInvoiceStatus.Valid or EInvoiceStatus.Submitted or EInvoiceStatus.Validating)
            {
                return Result<InvoiceDto>.Success(await MapAsync(invoice, cancellationToken));
            }

            // FR-8: only clinics that have enabled El Fatoora may submit — the toggle is enforced here, not
            // just hidden in the UI.
            var clinic = await _clinicRepository.GetByIdAsync(clinicId, cancellationToken);
            if (clinic == null)
            {
                return Result<InvoiceDto>.Failure("Cabinet introuvable.");
            }
            if (!clinic.TtnEInvoicingEnabled)
            {
                return Result<InvoiceDto>.Failure("La facturation électronique El Fatoora n'est pas activée pour ce cabinet.");
            }

            if (!invoice.CanSubmitToElFatoora)
            {
                return Result<InvoiceDto>.Failure("Émettez la facture avant de l'envoyer à El Fatoora.");
            }

            // Queue first (the outbox entry). This alone satisfies the offline path (US-2).
            invoice.QueueForElFatoora();
            await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Online: attempt the dispatch now. The service is best-effort and self-committing — it records
            // its own outcome on the invoice and never throws back, so a failure here just leaves it Queued.
            if (await _internetProbe.IsInternetReachableAsync())
            {
                await _eInvoiceService.ProcessAsync(invoice.Id, cancellationToken);
            }
            else
            {
                _logger.LogInformation(
                    "Invoice {InvoiceId} queued for El Fatoora; server offline, deferring to the outbox job.", invoice.Id);
            }

            // Re-load to reflect whatever state the dispatch attempt left behind.
            var refreshed = await _invoiceRepository.GetByIdAsync(invoice.Id, cancellationToken) ?? invoice;
            return Result<InvoiceDto>.Success(await MapAsync(refreshed, cancellationToken));
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
            _logger.LogError(ex, "Error submitting invoice {InvoiceId} to El Fatoora", request.Id);
            return Result<InvoiceDto>.Failure("Erreur lors de l'envoi à El Fatoora.");
        }
    }

    private async Task<InvoiceDto> MapAsync(Domain.Entities.Invoice invoice, CancellationToken cancellationToken)
    {
        var patient = await _patientRepository.GetByIdAsync(invoice.PatientId, cancellationToken);
        return invoice.ToDto(patient?.GetFullName());
    }
}
