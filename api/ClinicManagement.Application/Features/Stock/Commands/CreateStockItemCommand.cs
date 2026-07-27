using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Stock.Commands;

public class CreateStockItemCommand : IRequest<Result<StockItemDto>>
{
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

public class CreateStockItemCommandHandler : IRequestHandler<CreateStockItemCommand, Result<StockItemDto>>
{
    private readonly IStockItemRepository _stockItemRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationGenerator _notificationGenerator;

    public CreateStockItemCommandHandler(
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

    public async Task<Result<StockItemDto>> Handle(CreateStockItemCommand request, CancellationToken cancellationToken)
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

            var maximum = request.MaximumStockLevel.HasValue && request.MaximumStockLevel.Value >= request.MinimumStockLevel
                ? request.MaximumStockLevel.Value
                : request.MinimumStockLevel;

            var item = new StockItem(
                Guid.NewGuid(),
                clinic.Value,
                request.Name,
                request.Category,
                request.Unit,
                request.MinimumStockLevel,
                maximum,
                request.Description,
                request.UnitPrice,
                request.Supplier);

            if (request.CurrentStock > 0)
                item.SetCurrentStock(request.CurrentStock);

            await _stockItemRepository.AddAsync(item, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Notify if an item is created already at/below its minimum (finding #14) — the update path only
            // fires on a not-low→low crossing, so a born-low item would otherwise never notify. Best-effort.
            if (item.IsLowStock())
            {
                await _notificationGenerator.LowStockAsync(
                    clinic.Value, item.Id, item.Name, item.CurrentStock, item.MinimumStockLevel, cancellationToken);
            }

            return Result<StockItemDto>.Success(item.ToDto());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StockItemDto>.Failure($"Error creating stock item: {ex.Message}");
        }
    }
}
