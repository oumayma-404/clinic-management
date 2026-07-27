using MediatR;
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
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateLabWorkOrderStatusCommandHandler(
        ILabWorkOrderRepository labWorkOrderRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _labWorkOrderRepository = labWorkOrderRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
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

            await _labWorkOrderRepository.UpdateAsync(order, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<LabWorkOrderDto>.Success(order.ToDto());
        }
        catch (ArgumentException ex)
        {
            return Result<LabWorkOrderDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<LabWorkOrderDto>.Failure($"Erreur lors de la mise à jour du statut du bon de laboratoire : {ex.Message}");
        }
    }
}
