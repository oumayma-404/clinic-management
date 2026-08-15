using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using MediatR;

namespace ClinicManagement.Application.Features.Suppliers.Queries;

/// <summary>The fournisseurs list — searchable, category-filtered, paged.</summary>
public class GetSuppliersQuery : IRequest<Result<SupplierPageDto>>
{
    /// <summary>Free-text over nom / catégorie / téléphone / adresse, matched in SQL across the whole clinic.</summary>
    public string? SearchTerm { get; set; }

    /// <summary>Exact catégorie match, or null for every category.</summary>
    public string? Category { get; set; }

    /// <summary>
    /// Show deactivated fournisseurs too. Off by default, which is what the pickers want; the list screen turns
    /// it on so a deactivated record stays reachable to be reactivated or corrected.
    /// </summary>
    public bool IncludeInactive { get; set; }

    /// <summary>1-based page and page size. Both null = every match (the picker, and the CSV export).</summary>
    public int? Page { get; set; }
    public int? PageSize { get; set; }
}

public class GetSuppliersQueryHandler : IRequestHandler<GetSuppliersQuery, Result<SupplierPageDto>>
{
    private readonly ISupplierRepository _suppliers;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetSuppliersQueryHandler(ISupplierRepository suppliers, ICurrentClinicResolver clinicResolver)
    {
        _suppliers = suppliers;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<SupplierPageDto>> Handle(
        GetSuppliersQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
            {
                return Result<SupplierPageDto>.Failure(clinic.Error ?? "Cabinet introuvable.");
            }

            var page = await _suppliers.GetFilteredAsync(
                clinic.Value,
                request.SearchTerm,
                request.Category,
                request.IncludeInactive,
                PageRequest.From(request.Page, request.PageSize),
                cancellationToken);

            // One batched read for the whole page — « N articles liés » per row must not be a query per row.
            var usage = await _suppliers.GetUsageAsync(
                clinic.Value, page.Items.Select(s => s.Id).ToList(), cancellationToken);

            var inUse = await _suppliers.GetCategoriesAsync(clinic.Value, cancellationToken);

            // The canonical suggestions first, in the order a cabinet reaches for one; the clinic's own follow
            // alphabetically. Appending keeps the familiar list where it was rather than reshuffling it the day
            // somebody files a « Menuisier ». Same shape as GetProcedureTypeCategoriesQuery, and the union is the
            // point: without offering a clinic's own labels back, the second person to want one retypes it.
            var categories = new List<string>(SupplierCategories.Canonical);
            categories.AddRange(inUse
                .Where(c => !SupplierCategories.IsCanonical(c))
                .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase));

            return Result<SupplierPageDto>.Success(new SupplierPageDto
            {
                Items = page.Items
                    .Select(s => s.ToDto(usage.TryGetValue(s.Id, out var u) ? u : default))
                    .ToList(),
                Categories = categories,
                Page = page.Page,
                PageSize = page.PageSize,
                TotalCount = page.TotalCount,
                TotalPages = page.TotalPages,
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<SupplierPageDto>.Failure("Erreur lors de la récupération des fournisseurs.");
        }
    }
}
