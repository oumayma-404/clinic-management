using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Stock.Queries;

public class GetStockItemsQuery : IRequest<Result<StockPageDto>>
{
    public bool LowStockOnly { get; set; }

    /// <summary>1-based page and page size. Both null = every matching row.</summary>
    public int? Page { get; set; }
    public int? PageSize { get; set; }

    /// <summary>Free-text filter, matched in SQL across the whole clinic — never only the requested page.</summary>
    public string? SearchTerm { get; set; }

    /// <summary>Exact category match. Applied in SQL — it was a client-side filter over the full list.</summary>
    public string? Category { get; set; }

    /// <summary>
    /// Only items with a lot at or inside the clinic's expiry lead time (already-expired lots included). Applied in
    /// SQL, using the same horizon the « Péremption (N) » count uses.
    /// </summary>
    public bool ExpiringOnly { get; set; }
}

public class GetStockItemsQueryHandler : IRequestHandler<GetStockItemsQuery, Result<StockPageDto>>
{
    private readonly IStockItemRepository _stockItemRepository;
    private readonly ISupplierRepository _supplierRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetStockItemsQueryHandler(
        IStockItemRepository stockItemRepository,
        ISupplierRepository supplierRepository,
        IClinicRepository clinicRepository,
        ICurrentClinicResolver clinicResolver)
    {
        _stockItemRepository = stockItemRepository;
        _supplierRepository = supplierRepository;
        _clinicRepository = clinicRepository;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<StockPageDto>> Handle(GetStockItemsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
            {
                return Result<StockPageDto>.Failure(clinic.Error ?? "Unable to resolve current clinic");
            }

            // One `now` and one lead time for the whole request (AC-P4.5/4.6), so two rows cannot disagree about
            // whether the same date counts as "expiring soon" — and so the filter, the count and each row's badge
            // are all derived from the same instant. Resolved BEFORE the read because the filter needs the horizon.
            var clinicRecord = await _clinicRepository.GetByIdAsync(clinic.Value, cancellationToken);
            var leadDays = clinicRecord?.StockExpiryLeadDays ?? Domain.Entities.Clinic.DefaultStockExpiryLeadDays;
            var now = DateTime.UtcNow;

            var page = await _stockItemRepository.GetByClinicIdAsync(
                clinic.Value,
                request.LowStockOnly,
                request.SearchTerm,
                request.Category,
                request.ExpiringOnly ? now.AddDays(leadDays) : null,
                PageRequest.From(request.Page, request.PageSize),
                cancellationToken);

            // The two counts and the category list are clinic-wide and deliberately ignore the current filters and
            // search: they are the chips that tell staff how much is wrong in the stockroom, so narrowing them to
            // the active view would make them report the filter back to itself.
            var lowStockCount = await _stockItemRepository.CountLowStockAsync(clinic.Value, cancellationToken);
            var expiringCount = await _stockItemRepository.CountExpiringSoonAsync(
                clinic.Value, leadDays, now, cancellationToken);
            var categories = await _stockItemRepository.GetDistinctCategoriesAsync(clinic.Value, cancellationToken);

            // One batched read for the page's suppliers — a per-row resolve is the companion-read defect
            // `list-pagination` names, and this list is the one that most often carries a supplier on every row.
            // GetByIdsAsync deliberately ignores IsActive: a deactivated supplier still owns the link.
            var supplierIds = page.Items
                .Where(i => i.SupplierId.HasValue)
                .Select(i => i.SupplierId!.Value)
                .ToList();
            var suppliers = await _supplierRepository.GetByIdsAsync(clinic.Value, supplierIds, cancellationToken);

            return Result<StockPageDto>.Success(new StockPageDto
            {
                Items = page.Items
                    .Select(i => i.ToDto(
                        leadDays,
                        now,
                        i.SupplierId.HasValue && suppliers.TryGetValue(i.SupplierId.Value, out var s) ? s : null))
                    .ToList(),
                LowStockCount = lowStockCount,
                ExpiringCount = expiringCount,
                Categories = categories.ToList(),
                Page = page.Page,
                PageSize = page.PageSize,
                TotalCount = page.TotalCount,
                TotalPages = page.TotalPages,
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StockPageDto>.Failure("Erreur lors de la récupération des articles de stock.");
        }
    }
}
