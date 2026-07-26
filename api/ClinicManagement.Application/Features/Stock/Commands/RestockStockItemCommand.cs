using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Stock.Commands;

/// <summary>Restock (increment) stock by a delta, optionally recording a new expiry/batch (finding #14).</summary>
public class RestockStockItemCommand : IRequest<Result<StockItemDto>>
{
    public Guid Id { get; set; }
    public int Quantity { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? BatchNumber { get; set; }
}

public class RestockStockItemCommandHandler : IRequestHandler<RestockStockItemCommand, Result<StockItemDto>>
{
    private readonly IStockItemRepository _stockItemRepository;
    private readonly IStockMovementRepository _stockMovementRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public RestockStockItemCommandHandler(
        IStockItemRepository stockItemRepository,
        IStockMovementRepository stockMovementRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _stockItemRepository = stockItemRepository;
        _stockMovementRepository = stockMovementRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<StockItemDto>> Handle(RestockStockItemCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.Quantity <= 0)
                return Result<StockItemDto>.Failure("La quantité doit être supérieure à 0.");

            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<StockItemDto>.Failure(clinic.Error ?? "Cabinet introuvable.");

            var item = await _stockItemRepository.GetByIdAsync(request.Id, cancellationToken);
            if (item == null || item.ClinicId != clinic.Value)
                return Result<StockItemDto>.Failure("Article de stock introuvable.");

            item.AddStock(request.Quantity, request.ExpiryDate, request.BatchNumber);

            // Audit the movement (finding #14) in the same transaction — ResultingStock is the post-mutation on-hand.
            await _stockMovementRepository.AddAsync(
                new StockMovement(Guid.NewGuid(), clinic.Value, item.Id, StockMovementType.Restock, request.Quantity, item.CurrentStock),
                cancellationToken);

            await _stockItemRepository.UpdateAsync(item, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<StockItemDto>.Success(item.ToDto());
        }
        catch (Exception ex)
        {
            return Result<StockItemDto>.Failure($"Erreur lors de l'entrée de stock : {ex.Message}");
        }
    }
}
