using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Stock.Queries;

public class GetStockItemsQuery : IRequest<Result<IEnumerable<StockItemDto>>>
{
    public bool LowStockOnly { get; set; }
}

public class GetStockItemsQueryHandler : IRequestHandler<GetStockItemsQuery, Result<IEnumerable<StockItemDto>>>
{
    private readonly IStockItemRepository _stockItemRepository;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetStockItemsQueryHandler(
        IStockItemRepository stockItemRepository,
        ICurrentClinicResolver clinicResolver)
    {
        _stockItemRepository = stockItemRepository;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<IEnumerable<StockItemDto>>> Handle(GetStockItemsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
            {
                return Result<IEnumerable<StockItemDto>>.Failure(clinic.Error ?? "Unable to resolve current clinic");
            }

            var items = await _stockItemRepository.GetByClinicIdAsync(clinic.Value, request.LowStockOnly, cancellationToken);
            var dtos = items.Select(i => i.ToDto());

            return Result<IEnumerable<StockItemDto>>.Success(dtos);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<StockItemDto>>.Failure($"Error retrieving stock items: {ex.Message}");
        }
    }
}
