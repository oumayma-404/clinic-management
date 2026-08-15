using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Stock.Commands;

/// <summary>Consume (decrement) stock by a delta — the movement-based path that replaces the absolute
/// overwrite (finding #14). Fires the low-stock notification on a not-low→low crossing.</summary>
public class ConsumeStockCommand : IRequest<Result<StockItemDto>>
{
    public Guid Id { get; set; }
    public int Quantity { get; set; }

    /// <summary>Why the stock left. Recorded on the movement (AC-P4.17); all three write sites passed null.</summary>
    public string? Reason { get; set; }
}

public class ConsumeStockCommandHandler : IRequestHandler<ConsumeStockCommand, Result<StockItemDto>>
{
    private readonly IStockItemRepository _stockItemRepository;
    private readonly IStockMovementRepository _stockMovementRepository;
    private readonly ISupplierRepository _supplierRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationGenerator _notificationGenerator;

    public ConsumeStockCommandHandler(
        IStockItemRepository stockItemRepository,
        IStockMovementRepository stockMovementRepository,
        ISupplierRepository supplierRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        INotificationGenerator notificationGenerator)
    {
        _stockItemRepository = stockItemRepository;
        _stockMovementRepository = stockMovementRepository;
        _supplierRepository = supplierRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _notificationGenerator = notificationGenerator;
    }

    public async Task<Result<StockItemDto>> Handle(ConsumeStockCommand request, CancellationToken cancellationToken)
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

            if (request.Quantity > item.CurrentStock)
                return Result<StockItemDto>.Failure("Stock insuffisant pour cette sortie.");

            var wasLow = item.IsLowStock();
            item.RemoveStock(request.Quantity);

            // Audit the movement (finding #14) in the same transaction — ResultingStock is the post-mutation on-hand.
            await _stockMovementRepository.AddAsync(
                new StockMovement(
                    Guid.NewGuid(), clinic.Value, item.Id, StockMovementType.Consume, request.Quantity,
                    item.CurrentStock, request.Reason ?? "Sortie de stock manuelle"),
                cancellationToken);

            await _stockItemRepository.UpdateAsync(item, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Edge-triggered low-stock notification (best-effort): fire only on the not-low → low crossing.
            if (!wasLow && item.IsLowStock())
            {
                await _notificationGenerator.LowStockAsync(
                    clinic.Value, item.Id, item.Name, item.CurrentStock, item.MinimumStockLevel, cancellationToken);
            }

            // The client repaints the row from this response, so the supplier has to travel with it — otherwise a
            // sortie makes the article's fournisseur (and its WhatsApp action) disappear until the next refetch.
            var supplier = item.SupplierId is { } supplierId
                ? await _supplierRepository.GetByIdAsync(supplierId, cancellationToken)
                : null;

            return Result<StockItemDto>.Success(item.ToDto(supplier: supplier));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StockItemDto>.Failure($"Erreur lors de la sortie de stock : {ex.Message}");
        }
    }
}
