using MediatR;
using ClinicManagement.Application.Common.Exceptions;
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
    private readonly IClinicRepository _clinicRepository;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetStockItemsQueryHandler(
        IStockItemRepository stockItemRepository,
        IClinicRepository clinicRepository,
        ICurrentClinicResolver clinicResolver)
    {
        _stockItemRepository = stockItemRepository;
        _clinicRepository = clinicRepository;
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
            // One `now` and one lead time for the whole page (AC-P4.5/4.6), so two rows cannot disagree about
            // whether the same date counts as "expiring soon".
            var clinicRecord = await _clinicRepository.GetByIdAsync(clinic.Value, cancellationToken);
            var leadDays = clinicRecord?.StockExpiryLeadDays ?? Domain.Entities.Clinic.DefaultStockExpiryLeadDays;
            var now = DateTime.UtcNow;
            var dtos = items.Select(i => i.ToDto(leadDays, now)).ToList();

            return Result<IEnumerable<StockItemDto>>.Success(dtos);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<IEnumerable<StockItemDto>>.Failure("Erreur lors de la récupération des articles de stock.");
        }
    }
}
