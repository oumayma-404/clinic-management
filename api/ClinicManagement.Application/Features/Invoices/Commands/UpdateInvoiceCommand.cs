using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Invoices.Commands;

/// <summary>Update a draft invoice's lines / patient links. Fails if the invoice is not a draft.</summary>
public class UpdateInvoiceCommand : IRequest<Result<InvoiceDto>>
{
    /// <summary>
    /// The <c>Version</c> the client read. Round-tripped so the save is validated against the copy the user
    /// actually edited rather than the one the handler just loaded. Omit (0) to skip the check — the seam
    /// server-internal writers use; see <c>IUnitOfWork.SetExpectedVersion</c>.
    /// </summary>
    public uint Version { get; set; }

    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid? DentalRecordId { get; set; }
    public Guid? AppointmentId { get; set; }
    public List<InvoiceLineRequest> Lines { get; set; } = new();
}

public class UpdateInvoiceCommandHandler : IRequestHandler<UpdateInvoiceCommand, Result<InvoiceDto>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<UpdateInvoiceCommandHandler> _logger;

    public UpdateInvoiceCommandHandler(
        IInvoiceRepository invoiceRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<UpdateInvoiceCommandHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<InvoiceDto>> Handle(UpdateInvoiceCommand request, CancellationToken cancellationToken)
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

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicId)
            {
                return Result<InvoiceDto>.Failure("Patient introuvable.");
            }

            // Preserve the invoice's existing dental-record / appointment links when the edit request omits
            // them (the edit UI sends only patient + lines). The header DentalRecordId drives the
            // "already invoiced" guard, so nulling it on every edit would silently break that link. A
            // non-null id in the request still updates the link; explicitly clearing a link is a separate
            // action (out of scope).
            invoice.UpdateLinks(
                request.PatientId,
                request.DentalRecordId ?? invoice.DentalRecordId,
                request.AppointmentId ?? invoice.AppointmentId);
            invoice.SetLines(request.Lines.Select(l => (l.Designation, l.Quantity, l.UnitPriceHt, l.DentalRecordId, l.DentalActCodeId, l.CodeActe)));

            // Validate the save against the version the USER was editing, not the one this
            // handler just loaded — that one always matches and would detect nothing.
            _unitOfWork.SetExpectedVersion(invoice, request.Version);
            await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<InvoiceDto>.Success(invoice.ToDto(patient.GetFullName()));
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
            _logger.LogError(ex, "Error updating invoice {InvoiceId}", request.Id);
            return Result<InvoiceDto>.Failure("Erreur lors de la mise à jour de la facture.");
        }
    }
}
