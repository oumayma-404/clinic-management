using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.LabOrders.Commands;

public class UpdateLabWorkOrderStatusCommand : IRequest<Result<LabWorkOrderDto>>
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class UpdateLabWorkOrderStatusCommandHandler : IRequestHandler<UpdateLabWorkOrderStatusCommand, Result<LabWorkOrderDto>>
{
    private readonly ILabWorkOrderRepository _labWorkOrderRepository;
    private readonly ISupplierRepository _supplierRepository;
    private readonly IExpenseRepository _expenseRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<UpdateLabWorkOrderStatusCommandHandler> _logger;

    public UpdateLabWorkOrderStatusCommandHandler(
        ILabWorkOrderRepository labWorkOrderRepository,
        ISupplierRepository supplierRepository,
        IExpenseRepository expenseRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<UpdateLabWorkOrderStatusCommandHandler> logger)
    {
        _labWorkOrderRepository = labWorkOrderRepository;
        _supplierRepository = supplierRepository;
        _expenseRepository = expenseRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<LabWorkOrderDto>> Handle(UpdateLabWorkOrderStatusCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (!Enum.TryParse<LabOrderStatus>(request.Status, ignoreCase: true, out var status))
                return Result<LabWorkOrderDto>.Failure("Statut invalide.");

            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<LabWorkOrderDto>.Failure(clinic.Error ?? "Cabinet introuvable.");

            var order = await _labWorkOrderRepository.GetByIdAsync(request.Id, cancellationToken);
            if (order == null || order.ClinicId != clinic.Value)
                return Result<LabWorkOrderDto>.Failure("Bon de laboratoire introuvable.");

            order.SetStatus(status);

            // The work arriving is money leaving, so la caisse learns of it here rather than waiting for somebody
            // to remember to file it. Both writes go in on the one SaveChangesAsync below — a bon must never be
            // « Reçu » with its dépense missing.
            await LabOrderCaisseExpense.PostIfDueAsync(_expenseRepository, order, clinic.Value, cancellationToken);

            await _labWorkOrderRepository.UpdateAsync(order, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // The board repaints the row from this response, so the laboratory's contact travels with it —
            // otherwise moving a bon to « Reçu » drops its WhatsApp action until the next refetch.
            var supplier = order.SupplierId is { } supplierId
                ? await _supplierRepository.GetByIdAsync(supplierId, cancellationToken)
                : null;

            return Result<LabWorkOrderDto>.Success(order.ToDto(supplier: supplier));
        }
        catch (ArgumentException ex)
        {
            return Result<LabWorkOrderDto>.Failure(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            // AC-P2.40: an illegal transition. The aggregate's message is already French and names both stages,
            // so it is surfaced verbatim rather than flattened into the generic failure below — which is what
            // would have happened while the generic catch was the only one that could see it.
            return Result<LabWorkOrderDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            // A-8 defect class: the raw exception was interpolated into a clinic-facing message. The detail
            // belongs in the log.
            _logger.LogError(ex, "Unhandled failure updating the status of lab work order {OrderId}", request.Id);
            return Result<LabWorkOrderDto>.Failure("Erreur lors de la mise à jour du statut du bon de laboratoire. Veuillez réessayer.");
        }
    }
}
