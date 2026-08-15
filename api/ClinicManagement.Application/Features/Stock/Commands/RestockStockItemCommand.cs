using MediatR;
using ClinicManagement.Application.Common.Exceptions;
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

    /// <summary>Why the stock arrived (supplier delivery, return…). Recorded on the movement (AC-P4.17).</summary>
    public string? Reason { get; set; }
}

public class RestockStockItemCommandHandler : IRequestHandler<RestockStockItemCommand, Result<StockItemDto>>
{
    private readonly IStockItemRepository _stockItemRepository;
    private readonly IStockMovementRepository _stockMovementRepository;
    private readonly ISupplierRepository _supplierRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly INotificationGenerator _notificationGenerator;
    private readonly IUnitOfWork _unitOfWork;

    public RestockStockItemCommandHandler(
        IStockItemRepository stockItemRepository,
        IStockMovementRepository stockMovementRepository,
        ISupplierRepository supplierRepository,
        IClinicRepository clinicRepository,
        ICurrentClinicResolver clinicResolver,
        INotificationGenerator notificationGenerator,
        IUnitOfWork unitOfWork)
    {
        _stockItemRepository = stockItemRepository;
        _stockMovementRepository = stockMovementRepository;
        _supplierRepository = supplierRepository;
        _clinicRepository = clinicRepository;
        _clinicResolver = clinicResolver;
        _notificationGenerator = notificationGenerator;
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
                new StockMovement(
                    Guid.NewGuid(), clinic.Value, item.Id, StockMovementType.Restock, request.Quantity,
                    item.CurrentStock, request.Reason ?? "Entrée de stock"),
                cancellationToken);

            await _stockItemRepository.UpdateAsync(item, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // AC-P4.6 — a delivery can arrive ALREADY inside the expiry lead window (short-dated stock is
            // routine), so flag it now rather than leaving it to the next daily StockExpiryJob run. Post-commit
            // and best-effort, exactly like the low-stock crossing: the generator swallows its own failures.
            var clinicRecord = await _clinicRepository.GetByIdAsync(clinic.Value, cancellationToken);
            var leadDays = clinicRecord?.StockExpiryLeadDays ?? Domain.Entities.Clinic.DefaultStockExpiryLeadDays;
            if (leadDays > 0 && item.HasStockExpiringSoon(DateTime.UtcNow, leadDays))
            {
                var earliest = item.EarliestRelevantExpiry();
                if (earliest.HasValue)
                {
                    await _notificationGenerator.EnsureStockExpiringSoonAsync(
                        clinic.Value, item.Id, item.Name, earliest.Value, cancellationToken);
                }
            }

            // The client repaints the row from this response — see ConsumeStockCommand for why the supplier has
            // to travel with it rather than being left for the next refetch.
            var supplier = item.SupplierId is { } supplierId
                ? await _supplierRepository.GetByIdAsync(supplierId, cancellationToken)
                : null;

            return Result<StockItemDto>.Success(item.ToDto(supplier: supplier));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StockItemDto>.Failure($"Erreur lors de l'entrée de stock : {ex.Message}");
        }
    }
}
