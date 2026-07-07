using MediatR;
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

    public CreateStockItemCommandHandler(
        IStockItemRepository stockItemRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _stockItemRepository = stockItemRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<StockItemDto>> Handle(CreateStockItemCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Result<StockItemDto>.Failure("Name is required");
            if (string.IsNullOrWhiteSpace(request.Category))
                return Result<StockItemDto>.Failure("Category is required");
            if (string.IsNullOrWhiteSpace(request.Unit))
                return Result<StockItemDto>.Failure("Unit is required");
            if (request.MinimumStockLevel < 0)
                return Result<StockItemDto>.Failure("Minimum stock level cannot be negative");
            if (request.CurrentStock < 0)
                return Result<StockItemDto>.Failure("Quantity cannot be negative");
            if (request.UnitPrice.HasValue && request.UnitPrice.Value < 0)
                return Result<StockItemDto>.Failure("Unit price cannot be negative");

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

            return Result<StockItemDto>.Success(item.ToDto());
        }
        catch (Exception ex)
        {
            return Result<StockItemDto>.Failure($"Error creating stock item: {ex.Message}");
        }
    }
}
