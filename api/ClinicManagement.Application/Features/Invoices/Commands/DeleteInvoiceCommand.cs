using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Invoices.Commands;

/// <summary>Delete a draft invoice. An issued invoice cannot be deleted (only cancelled).</summary>
public class DeleteInvoiceCommand : IRequest<Result>
{
    public Guid Id { get; set; }
}

public class DeleteInvoiceCommandHandler : IRequestHandler<DeleteInvoiceCommand, Result>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeleteInvoiceCommandHandler> _logger;

    public DeleteInvoiceCommandHandler(
        IInvoiceRepository invoiceRepository,
        ITreatmentPlanRepository planRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<DeleteInvoiceCommandHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _planRepository = planRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(DeleteInvoiceCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var invoice = await _invoiceRepository.GetByIdAsync(request.Id, cancellationToken);
            if (invoice == null || invoice.ClinicId != clinicId)
            {
                return Result.Failure("Facture introuvable.");
            }

            if (!invoice.CanBeDeleted)
            {
                return Result.Failure("Une facture émise ne peut pas être supprimée ; elle doit être annulée.");
            }

            // A DRAFT note is deleted rather than cancelled, so this is the half of the guard the cancel path
            // never sees — and the cheaper half to reach. See NoteCarriedActGuard.
            var carried = await NoteCarriedActGuard.EnsureNotCarriedAsync(
                clinicId, invoice, _planRepository, cancellationToken);
            if (carried.IsFailure)
            {
                return carried;
            }

            await _invoiceRepository.DeleteAsync(invoice.Id, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error deleting invoice {InvoiceId}", request.Id);
            return Result.Failure("Erreur lors de la suppression de la facture.");
        }
    }
}
