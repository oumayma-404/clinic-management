using ClinicManagement.Application.Common.Exceptions;
using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.CnamNomenclature.Commands;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

using ClinicManagement.Domain.Common;
namespace ClinicManagement.Application.Features.CnamNomenclature.Queries;

// Read the DB-backed CNAM dental nomenclature (FR-5.1), optionally filtered by free text and/or category.
// GLOBAL reference data shared across clinics, so this query is NOT clinic-scoped (unlike ProcedureTypes);
// the controller still requires an authenticated user. Active-only by default; the admin screen passes
// IncludeInactive = true to also see deactivated rows.
public class GetCnamNomenclatureQuery : IRequest<Result<PagedResult<CnamNomenclatureEntryDto>>>
{
    /// <summary>1-based page and page size. Both null = every matching row.</summary>
    public int? Page { get; set; }
    public int? PageSize { get; set; }

    public string? Q { get; set; }
    public string? Category { get; set; }
    public bool IncludeInactive { get; set; }
}

public class GetCnamNomenclatureQueryHandler
    : IRequestHandler<GetCnamNomenclatureQuery, Result<PagedResult<CnamNomenclatureEntryDto>>>
{
    private readonly ICnamCatalogRepository _repository;
    private readonly ILogger<GetCnamNomenclatureQueryHandler> _logger;

    public GetCnamNomenclatureQueryHandler(
        ICnamCatalogRepository repository,
        ILogger<GetCnamNomenclatureQueryHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<Result<PagedResult<CnamNomenclatureEntryDto>>> Handle(
        GetCnamNomenclatureQuery request, CancellationToken cancellationToken)
    {
        try
        {
            // Category and `Q` are repository arguments now: both used to be applied here, over the mapped
            // DTOs, after the whole catalog had been read. Filtering a page in memory shrinks it unpredictably,
            // and the search would only have looked at the rows already on it.
            var page = await _repository.GetAllAsync(
                request.IncludeInactive,
                request.Category,
                request.Q,
                PageRequest.From(request.Page, request.PageSize),
                cancellationToken);

            return Result<PagedResult<CnamNomenclatureEntryDto>>.Success(page.Map(CnamEntryMapper.ToDto));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error retrieving CNAM nomenclature");
            return Result<PagedResult<CnamNomenclatureEntryDto>>.Failure(
                "Erreur lors de la récupération de la nomenclature CNAM.");
        }
    }
}
