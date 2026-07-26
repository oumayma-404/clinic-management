using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Stock.Commands;

public class UpdateStockItemCommand : IRequest<Result<StockItemDto>>
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public int CurrentStock { get; set; }
    public int MinimumStockLevel { get; set; }
    public int? MaximumStockLevel { get; set; }
    public string? Description { get; set; }
    public decimal? UnitPrice { get; set; }
    public string? Supplier { get; set; }
}

public class UpdateStockItemCommandHandler : IRequestHandler<UpdateStockItemCommand, Result<StockItemDto>>
{
    private readonly IStockItemRepository _stockItemRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationGenerator _notificationGenerator;

    public UpdateStockItemCommandHandler(
        IStockItemRepository stockItemRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        INotificationGenerator notificationGenerator)
    {
        _stockItemRepository = stockItemRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _notificationGenerator = notificationGenerator;
    }

    public async Task<Result<StockItemDto>> Handle(UpdateStockItemCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Result<StockItemDto>.Failure("Le nom est requis.");
            if (string.IsNullOrWhiteSpace(request.Category))
                return Result<StockItemDto>.Failure("La catégorie est requise.");
            if (string.IsNullOrWhiteSpace(request.Unit))
                return Result<StockItemDto>.Failure("L'unité est requise.");
            if (request.MinimumStockLevel < 0)
                return Result<StockItemDto>.Failure("Le stock minimum ne peut pas être négatif.");
            if (request.CurrentStock < 0)
                return Result<StockItemDto>.Failure("La quantité ne peut pas être négative.");
            if (request.UnitPrice.HasValue && request.UnitPrice.Value < 0)
                return Result<StockItemDto>.Failure("Le prix unitaire ne peut pas être négatif.");

            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<StockItemDto>.Failure(clinic.Error ?? "Unable to resolve current clinic");

            var item = await _stockItemRepository.GetByIdAsync(request.Id, cancellationToken);
            if (item == null || item.ClinicId != clinic.Value)
                return Result<StockItemDto>.Failure("Article de stock introuvable.");

            var maximum = request.MaximumStockLevel.HasValue && request.MaximumStockLevel.Value >= request.MinimumStockLevel
                ? request.MaximumStockLevel.Value
                : request.MinimumStockLevel;

            // Capture the low-stock state before the mutations so we can detect a not-low → low crossing
            // (covers both a quantity drop and a MinimumStockLevel raise — spec US-5).
            var wasLow = item.IsLowStock();

            item.UpdateInfo(request.Name, request.Description, request.Category, request.Unit, request.UnitPrice, request.Supplier);
            item.UpdateStockLevels(request.MinimumStockLevel, maximum);
            item.SetCurrentStock(request.CurrentStock);

            await _stockItemRepository.UpdateAsync(item, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Edge-triggered low-stock notification (best-effort, never fails this command): fire only on
            // the not-low → low crossing. Staying low, or being created already low, generates nothing.
            if (!wasLow && item.IsLowStock())
            {
                await _notificationGenerator.LowStockAsync(
                    clinic.Value, item.Id, item.Name, item.CurrentStock, item.MinimumStockLevel, cancellationToken);
            }

            return Result<StockItemDto>.Success(item.ToDto());
        }
        catch (Exception ex)
        {
            return Result<StockItemDto>.Failure($"Error updating stock item: {ex.Message}");
        }
    }
}
