using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Stock.Commands;

public class DeleteStockItemCommand : IRequest<Result<bool>>
{
    public Guid Id { get; set; }
}

public class DeleteStockItemCommandHandler : IRequestHandler<DeleteStockItemCommand, Result<bool>>
{
    private readonly IStockItemRepository _stockItemRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteStockItemCommandHandler(
        IStockItemRepository stockItemRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _stockItemRepository = stockItemRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<bool>> Handle(DeleteStockItemCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<bool>.Failure(clinic.Error ?? "Unable to resolve current clinic");

            var item = await _stockItemRepository.GetByIdAsync(request.Id, cancellationToken);
            if (item == null || item.ClinicId != clinic.Value)
                return Result<bool>.Failure("Article de stock introuvable.");

            await _stockItemRepository.DeleteAsync(request.Id, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Error deleting stock item: {ex.Message}");
        }
    }
}
