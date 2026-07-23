using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.LabOrders.Commands;

public class DeleteLabWorkOrderCommand : IRequest<Result<bool>>
{
    public Guid Id { get; set; }
}

public class DeleteLabWorkOrderCommandHandler : IRequestHandler<DeleteLabWorkOrderCommand, Result<bool>>
{
    private readonly ILabWorkOrderRepository _labWorkOrderRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteLabWorkOrderCommandHandler(
        ILabWorkOrderRepository labWorkOrderRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _labWorkOrderRepository = labWorkOrderRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<bool>> Handle(DeleteLabWorkOrderCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<bool>.Failure(clinic.Error ?? "Cabinet introuvable.");

            var order = await _labWorkOrderRepository.GetByIdAsync(request.Id, cancellationToken);
            if (order == null || order.ClinicId != clinic.Value)
                return Result<bool>.Failure("Bon de laboratoire introuvable.");

            await _labWorkOrderRepository.DeleteAsync(request.Id, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Erreur lors de la suppression du bon de laboratoire : {ex.Message}");
        }
    }
}
