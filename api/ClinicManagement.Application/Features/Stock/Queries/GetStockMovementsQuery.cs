using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Stock.Queries;

/// <summary>Movement history (sorties/entrées) for a stock item, newest-first (finding #14).</summary>
public class GetStockMovementsQuery : IRequest<Result<IReadOnlyList<StockMovementDto>>>
{
    public Guid StockItemId { get; set; }
}

public class GetStockMovementsQueryHandler : IRequestHandler<GetStockMovementsQuery, Result<IReadOnlyList<StockMovementDto>>>
{
    private readonly IStockItemRepository _stockItemRepository;
    private readonly IStockMovementRepository _stockMovementRepository;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetStockMovementsQueryHandler(
        IStockItemRepository stockItemRepository,
        IStockMovementRepository stockMovementRepository,
        ICurrentClinicResolver clinicResolver)
    {
        _stockItemRepository = stockItemRepository;
        _stockMovementRepository = stockMovementRepository;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<IReadOnlyList<StockMovementDto>>> Handle(GetStockMovementsQuery request, CancellationToken cancellationToken)
    {
        var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
        if (clinic.IsFailure)
            return Result<IReadOnlyList<StockMovementDto>>.Failure(clinic.Error ?? "Cabinet introuvable.");

        // Tenant isolation: an item from another clinic reads as "not found".
        var item = await _stockItemRepository.GetByIdAsync(request.StockItemId, cancellationToken);
        if (item == null || item.ClinicId != clinic.Value)
            return Result<IReadOnlyList<StockMovementDto>>.Failure("Article de stock introuvable.");

        var movements = await _stockMovementRepository.GetByStockItemAsync(request.StockItemId, cancellationToken);
        var dtos = movements.Select(m => new StockMovementDto
        {
            Id = m.Id,
            StockItemId = m.StockItemId,
            Type = m.Type.ToString(),
            Quantity = m.Quantity,
            ResultingStock = m.ResultingStock,
            Reason = m.Reason,
            CreatedAt = m.CreatedAt,
        }).ToList();

        return Result<IReadOnlyList<StockMovementDto>>.Success(dtos);
    }
}
