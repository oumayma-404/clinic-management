using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Invoices.Commands;

/// <summary>
/// Cancel an issued invoice (motif required). The number and frozen totals are kept. Role gating
/// (admin/doctor only) is enforced at the controller via the AdminOrDoctor policy.
/// </summary>
public class CancelInvoiceCommand : IRequest<Result<InvoiceDto>>
{
    public Guid Id { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class CancelInvoiceCommandHandler : IRequestHandler<CancelInvoiceCommand, Result<InvoiceDto>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CancelInvoiceCommandHandler> _logger;

    public CancelInvoiceCommandHandler(
        IInvoiceRepository invoiceRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<CancelInvoiceCommandHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<InvoiceDto>> Handle(CancelInvoiceCommand request, CancellationToken cancellationToken)
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

            invoice.Cancel(request.Reason);

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
            _logger.LogError(ex, "Error cancelling invoice {InvoiceId}", request.Id);
            return Result<InvoiceDto>.Failure("Erreur lors de l'annulation de la facture.");
        }
    }
}
