using ClinicManagement.Application.Common.Exceptions;
using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.CnamNomenclature.Commands;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.CnamNomenclature.Queries;

// Read the DB-backed CNAM dental nomenclature (FR-5.1), optionally filtered by free text and/or category.
// GLOBAL reference data shared across clinics, so this query is NOT clinic-scoped (unlike ProcedureTypes);
// the controller still requires an authenticated user. Active-only by default; the admin screen passes
// IncludeInactive = true to also see deactivated rows.
public class GetCnamNomenclatureQuery : IRequest<Result<IEnumerable<CnamNomenclatureEntryDto>>>
{
    public string? Q { get; set; }
    public string? Category { get; set; }
    public bool IncludeInactive { get; set; }
}

public class GetCnamNomenclatureQueryHandler
    : IRequestHandler<GetCnamNomenclatureQuery, Result<IEnumerable<CnamNomenclatureEntryDto>>>
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

    public async Task<Result<IEnumerable<CnamNomenclatureEntryDto>>> Handle(
        GetCnamNomenclatureQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var entries = (await _repository.GetAllAsync(request.IncludeInactive, cancellationToken))
                .Select(CnamEntryMapper.ToDto)
                .AsEnumerable();

            if (!string.IsNullOrWhiteSpace(request.Category))
            {
                var category = request.Category.Trim();
                entries = entries.Where(e =>
                    string.Equals(e.Category, category, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(request.Q))
            {
                var q = request.Q.Trim();
                entries = entries.Where(e =>
                    e.CodeActe.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    e.DesignationFr.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    e.LettreCle.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            return Result<IEnumerable<CnamNomenclatureEntryDto>>.Success(entries.ToList());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error retrieving CNAM nomenclature");
            return Result<IEnumerable<CnamNomenclatureEntryDto>>.Failure(
                "Erreur lors de la récupération de la nomenclature CNAM.");
        }
    }
}
